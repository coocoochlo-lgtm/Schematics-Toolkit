using System;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The shared wave-function-collapse commitment grid (optional, off by default). Every point has one of T types;
    /// the plan grid holds, per head and plan column (now + future steps), a spread over types or a committed type.
    /// Three shared T × T rulebooks link cells: Coexist (neighbouring heads, same column), Follow (same head, next
    /// column) and Handoff (neighbouring head, next column). Each step: measure the now column from the heads' samples,
    /// seed the future, propagate messages, collapse the lowest-entropy cells, let committed heads read only their
    /// type's points, and every planTickSteps roll the grid left. The rulebooks (and the master's plan biases) train
    /// with everything else.
    /// </summary>
    public sealed partial class MnnCpu
    {
        /// <summary>The floor in every message: a rule can discourage a type but never forbid it, so there are no contradictions.</summary>
        public const float WfcEpsilon = 1e-4f;

        /// <summary>Below this per-type weight sum a committed head falls back to its full sample.</summary>
        public const float WfcEmptyType = 1e-6f;

        private int _t, _f;
        private float[] _partialW, _partialV, _ruleTable, _msg;
        private int[] _neighborStart, _neighborList;
        private int _planResets;
        private bool _planSurprised;

        /// <summary>Raised (with the step count) when a surprise wipes every plan's future, for the debug view.</summary>
        public uint LastPlanReset { get; private set; }

        private void InitializeWfc()
        {
            var s = Settings;
            if (!s.wfcEnabled)
                return;
            _t = s.typeCount;
            _f = s.PlanColumns;
            _partialW = new float[_h * _t];
            _partialV = new float[_h * _t * _n];
            _ruleTable = new float[3 * _t * _t];
            _msg = new float[_t];
            BuildNeighbors(s, out _neighborStart, out _neighborList);
        }

        /// <summary>
        /// Each head's neighbours in a fixed order: a ring (previous, then next) or every other head. Shared with the
        /// GPU as two tables.
        /// </summary>
        public static void BuildNeighbors(MnnSettings s, out int[] start, out int[] list)
        {
            int h = s.headCount;
            start = new int[h + 1];
            var all = new System.Collections.Generic.List<int>();
            for (int head = 0; head < h; head++)
            {
                start[head] = all.Count;
                if (s.headNeighbors == HeadNeighbors.All)
                {
                    for (int other = 0; other < h; other++)
                        if (other != head) all.Add(other);
                }
                else
                {
                    int previous = (head - 1 + h) % h, next = (head + 1) % h;
                    if (previous != head) all.Add(previous);
                    if (next != head && next != previous) all.Add(next);
                }
            }
            start[h] = all.Count;
            list = all.ToArray();
        }

        private void ResetPlan(MnnState state)
        {
            if (!Settings.wfcEnabled)
                return;
            float uniform = 1f / _t;
            for (int c = 0; c < state.PlanType.Length; c++)
            {
                state.PlanType[c] = -1;
                state.PlanAge[c] = 0;
                for (int t = 0; t < _t; t++) state.PlanSpread[c * _t + t] = uniform;
            }
        }

        private void ClearPartials(int h)
        {
            for (int t = 0; t < _t; t++)
            {
                _partialW[h * _t + t] = 0f;
                for (int n = 0; n < _n; n++) _partialV[(h * _t + t) * _n + n] = 0f;
            }
        }

        private void AddPartial(int h, int type, float w, ReadOnlySpan<float> values, int valueAt)
        {
            int at = h * _t + type;
            _partialW[at] += w;
            for (int n = 0; n < _n; n++)
                _partialV[at * _n + n] += w * values[valueAt + n];
        }

        private bool CoexistLive => Settings.wfcAxis != WfcAxis.Time;
        private bool FollowLive => Settings.wfcAxis != WfcAxis.Heads;
        private bool HandoffLive => Settings.wfcAxis == WfcAxis.Grid;

        /// <summary>The WfcPlan step for one state (see the class summary). The heads' samples are in _translatorIn.</summary>
        private void WfcPlan(MnnState state, float[] parameters, int row)
        {
            var s = Settings;
            int tt = _t * _t;

            // The rulebooks this copy sees, through softplus (Coexist made symmetric: (R + Rᵀ)/2).
            int rules = Layout.RulesOffset;
            for (int a = 0; a < _t; a++)
                for (int b = 0; b < _t; b++)
                {
                    _ruleTable[a * _t + b] = Softplus(0.5f * (parameters[rules + a * _t + b] + parameters[rules + b * _t + a]));
                    _ruleTable[tt + a * _t + b] = Softplus(parameters[rules + tt + a * _t + b]);
                    _ruleTable[2 * tt + a * _t + b] = Softplus(parameters[rules + 2 * tt + a * _t + b]);
                }

            // 1-3. Measure the present, seed the future, propagate.
            Propagate(state);

            // 4. Collapse the most certain undecided cells, one at a time.
            for (int b = 0; b < s.wfcCollapseBudget; b++)
            {
                int best = -1;
                float bestEntropy = float.MaxValue;
                for (int c = 0; c < _h * _f; c++)
                {
                    if (state.PlanType[c] >= 0) continue;
                    float entropy = 0f;
                    for (int t = 0; t < _t; t++)
                    {
                        float q = state.PlanSpread[c * _t + t];
                        if (q > 1e-12f) entropy -= q * MathF.Log(q);
                    }
                    if (entropy < bestEntropy) { bestEntropy = entropy; best = c; }
                }
                if (best < 0)
                    break;
                int type = ChooseType(state, best, row, b);
                state.PlanType[best] = type;
                state.PlanAge[best] = 0;
                for (int t = 0; t < _t; t++) state.PlanSpread[best * _t + t] = t == type ? 1f : 0f;
                Propagate(state);
            }

            // 5. Execute the now column: a committed head reads only its type's points (its partial average), unless
            // nothing of that type is near it.
            int block = s.TranslatorHeadBlock;
            for (int h = 0; h < _h; h++)
            {
                int type = state.PlanType[h * _f];
                if (type < 0) continue;
                float w = _partialW[h * _t + type];
                if (w <= WfcEmptyType) continue;
                float inv = 1f / w;
                for (int n = 0; n < _n; n++)
                    _translatorIn[h * block + n] = _partialV[(h * _t + type) * _n + n] * inv;
            }

            // Ages, then 6. roll the grid left every planTickSteps ticks (a fresh undecided column joins at the horizon).
            for (int c = 0; c < _h * _f; c++)
                if (state.PlanType[c] >= 0) state.PlanAge[c]++;
            if ((state.Step + 1) % (uint)s.planTickSteps == 0)
                RollPlan(state);
        }

        // Undecided cells are re-seeded and reshaped by messages from their neighbours, wfcPasses times, in a fixed order.
        private void Propagate(MnnState state)
        {
            var s = Settings;
            for (int c = 0; c < _h * _f; c++)
                if (state.PlanType[c] < 0) Seed(state, c);
            int tt = _t * _t;
            for (int pass = 0; pass < s.wfcPasses; pass++)
            {
                for (int h = 0; h < _h; h++)
                    for (int col = 0; col < _f; col++)
                    {
                        int c = h * _f + col;
                        if (state.PlanType[c] >= 0) continue;
                        Seed(state, c);
                        if (CoexistLive)
                            for (int k = _neighborStart[h]; k < _neighborStart[h + 1]; k++)
                                Message(state, _neighborList[k] * _f + col, c, 0, false);
                        if (FollowLive)
                        {
                            if (col > 0) Message(state, c - 1, c, tt, false);
                            if (col < _f - 1) Message(state, c + 1, c, tt, true);
                        }
                        if (HandoffLive)
                            for (int k = _neighborStart[h]; k < _neighborStart[h + 1]; k++)
                            {
                                int other = _neighborList[k] * _f + col;
                                if (col > 0) Message(state, other - 1, c, 2 * tt, false);
                                if (col < _f - 1) Message(state, other + 1, c, 2 * tt, true);
                            }
                        Normalize(state, c);
                    }
            }
        }

        // A cell's starting spread: the head's measured type spread for the now column; uniform × the master's plan
        // bias for that column in the future.
        private void Seed(MnnState state, int c)
        {
            int h = c / _f, col = c % _f;
            if (col == 0)
            {
                float total = 0f;
                for (int t = 0; t < _t; t++) total += _partialW[h * _t + t];
                if (total > 1e-12f)
                {
                    float inv = 1f / total;
                    for (int t = 0; t < _t; t++) state.PlanSpread[c * _t + t] = _partialW[h * _t + t] * inv;
                    return;
                }
            }
            else if (Settings.PlanBiasCount > 0)
            {
                int at = _h * _m + (col - 1) * _t;
                for (int t = 0; t < _t; t++)
                    state.PlanSpread[c * _t + t] = MathF.Exp(Math.Clamp(_masterOut[at + t], -6f, 6f));
                Normalize(state, c);
                return;
            }
            float uniform = 1f / _t;
            for (int t = 0; t < _t; t++) state.PlanSpread[c * _t + t] = uniform;
        }

        // m(τ) = ε + Σσ q_from(σ)·softplus(R[σ, τ]) (Rᵀ for messages running backward in time), scaled to mean 1 so
        // the product never under- or overflows, multiplied into the cell.
        private void Message(MnnState state, int from, int to, int table, bool backward)
        {
            float sum = 0f;
            for (int tau = 0; tau < _t; tau++)
            {
                float m = WfcEpsilon;
                for (int sigma = 0; sigma < _t; sigma++)
                {
                    float rule = backward ? _ruleTable[table + tau * _t + sigma] : _ruleTable[table + sigma * _t + tau];
                    m += state.PlanSpread[from * _t + sigma] * rule;
                }
                _msg[tau] = m;
                sum += m;
            }
            float scale = _t / sum;
            for (int tau = 0; tau < _t; tau++)
                state.PlanSpread[to * _t + tau] *= _msg[tau] * scale;
        }

        private void Normalize(MnnState state, int c)
        {
            float sum = 0f;
            for (int t = 0; t < _t; t++) sum += state.PlanSpread[c * _t + t];
            if (sum > 1e-30f)
            {
                float inv = 1f / sum;
                for (int t = 0; t < _t; t++) state.PlanSpread[c * _t + t] *= inv;
            }
            else
            {
                float uniform = 1f / _t;
                for (int t = 0; t < _t; t++) state.PlanSpread[c * _t + t] = uniform;
            }
        }

        /// <summary>
        /// The type a collapsing cell commits to: normally the most likely (ties to the lowest type). While the network
        /// is struggling (adaptive-sine α above 0) it samples from the spread instead, sharpened as q^(αmax/α), so a
        /// struggling network tries other plans. The dice are a PCG hash of (seed, step, row, collapse) like the GPU's.
        /// </summary>
        private int ChooseType(MnnState state, int c, int row, int collapse)
        {
            int best = 0;
            for (int t = 1; t < _t; t++)
                if (state.PlanSpread[c * _t + t] > state.PlanSpread[c * _t + best]) best = t;
            if (_alpha <= 0f)
                return best;

            float temperature = Math.Clamp(_alpha / Math.Max(Settings.sineAlphaMax, 1e-6f), 1e-3f, 1f);
            float exponent = 1f / temperature;
            float total = 0f;
            for (int t = 0; t < _t; t++) total += MathF.Pow(state.PlanSpread[c * _t + t], exponent);
            float u = MnnHash.Unit(MnnHash.Hash(Settings.seed, MnnHash.StreamWfc, state.Step, (uint)row, (uint)collapse)) * total;
            float acc = 0f;
            for (int t = 0; t < _t; t++)
            {
                acc += MathF.Pow(state.PlanSpread[c * _t + t], exponent);
                if (acc > u) return t;
            }
            return best;
        }

        private void RollPlan(MnnState state)
        {
            float uniform = 1f / _t;
            for (int h = 0; h < _h; h++)
            {
                for (int col = 0; col < _f - 1; col++)
                {
                    int to = h * _f + col, from = to + 1;
                    state.PlanType[to] = state.PlanType[from];
                    state.PlanAge[to] = state.PlanAge[from];
                    for (int t = 0; t < _t; t++) state.PlanSpread[to * _t + t] = state.PlanSpread[from * _t + t];
                }
                int last = h * _f + _f - 1;
                state.PlanType[last] = -1;
                state.PlanAge[last] = 0;
                for (int t = 0; t < _t; t++) state.PlanSpread[last * _t + t] = uniform;
            }
        }

        // When the error trace spikes past wfcResetThreshold × the baseline, every future column goes back to
        // undecided (the now column stays). Once per spike.
        private void PlanSurpriseReset()
        {
            var s = Settings;
            if (!s.wfcEnabled || _baseline <= 1e-8f || _f < 2)
                return;
            float ratio = _trace / _baseline;
            if (ratio < 1f)
            {
                _planSurprised = false;
                return;
            }
            if (_planSurprised || ratio <= s.wfcResetThreshold)
                return;
            _planSurprised = true;
            _planResets++;
            LastPlanReset = _steps;
            for (int row = 0; row < _instances; row++)
                for (int slot = 0; slot < _copies; slot++)
                    ClearFuture(State(row, slot));
        }

        private void ClearFuture(MnnState state)
        {
            for (int h = 0; h < _h; h++)
                for (int col = 1; col < _f; col++)
                {
                    state.PlanType[h * _f + col] = -1;
                    state.PlanAge[h * _f + col] = 0;
                }
        }
    }
}
