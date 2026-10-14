using System;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// Learning from the per-step error signal, outside the weight nudges (all optional, off by default):
    /// <list type="bullet">
    /// <item><b>Identity masks</b>: each head has a persistent soft identity (one probability per sample value it reads,
    /// per instance). Every step it's expressed as hard bits (with a little exploration noise while training), XORed
    /// with the sample's sign bits, projected back through the head's trainable K × K matrix P_h and added to the
    /// sample (× identityAlpha). The identity follows the bits it expressed when the step beat the error baseline and
    /// moves away from them when it did worse: soft += rate · advantage · (hard − soft), a Bernoulli REINFORCE step,
    /// not a gradient. Only the real network's expression and error count.</item>
    /// <item><b>Self-imitation memory</b>: a fixed table of situations (a 64-bit SimHash of the inputs, and of the last
    /// outputs when keyed on feedback) and the outputs that beat the baseline there. A step with positive advantage
    /// pulls its situation's remembered output toward what was just done (or takes the weakest slot). Every entry's
    /// quality and familiarity fade each step. Each copy recalls a target and a confidence c for its situation, and
    /// its training error becomes (1 − c) · error + c · imitationWeight · |output − target|², so a bare score turns
    /// into a direction to learn in.</item>
    /// <item><b>Familiarity-scaled exploration</b>: the nudges, the adaptive sine and the identity noise shrink toward
    /// familiarExploreScale in situations the imitation memory (or the episodic memory) knows well.</item>
    /// <item><b>Trajectory credit</b>: a step that beat the baseline also credits the outputs of the last creditSteps
    /// steps, fading by creditDecay per step back, so a late reward marks the actions that led to it.</item>
    /// </list>
    /// The signal of a step (a supervised step's error, or what was reported with ReportError) is used at the start of
    /// the next step, in instance order, so the GPU can do the same thing with the same numbers.
    /// </summary>
    public sealed partial class MnnCpu
    {
        // Identity masks: soft identities and the real network's last expression (instances × H × K), and scratch.
        private int _k;
        private float[] _idSoft = Array.Empty<float>(), _idHard = Array.Empty<float>();
        private readonly float[] _idBits = new float[MnnSettings.MaxOutputs];

        // Imitation memory: the SimHash projection (64 × (key inputs + 1)), keys (2 words per slot), slots, and each
        // instance's ring of recent (key, output) for trajectory credit.
        private int _iSlots, _iStride, _keyInputs, _creditSteps;
        private float[] _iProj = Array.Empty<float>(), _iBank = Array.Empty<float>(), _ring = Array.Empty<float>();
        private uint[] _iKeys = Array.Empty<uint>(), _ringKeys = Array.Empty<uint>();
        private float[] _keyIn, _imitTarget;
        private uint _keyLo, _keyHi;
        private float _imitConf, _imitErr, _familiarityNow;
        private float[] _rowImitConf, _rowImitErr;

        // The step's signal per instance, consumed at the start of the next step.
        private double[] _sigError;
        private bool[] _sigHas;
        private float _spread, _familiarity, _familiaritySum, _imitationConfidence;
        private int _imitationWrites;

        /// <summary>Each instance's head identities, soft (instances × H × K, 0-1; above 0.5 = bit set). Saved with the brain.</summary>
        public float[] IdentityMasks => _idSoft;

        /// <summary>The bits each instance's heads expressed last step (instances × H × K, 0 or 1).</summary>
        public ReadOnlySpan<float> IdentityExpression => _idHard;

        /// <summary>The imitation slots: S × (output (N), quality, familiarity, used).</summary>
        public ReadOnlySpan<float> ImitationBank => _iBank;

        /// <summary>The imitation slots' keys: S × 2 words (bits 0-31, 32-63).</summary>
        public ReadOnlySpan<uint> ImitationKeys => _iKeys;

        /// <summary>The SimHash projection: 64 rows of (key inputs weights, offset). Shared with the GPU.</summary>
        public ReadOnlySpan<float> ImitationProjection => _iProj;

        private void InitializeSignals()
        {
            var s = Settings;
            _sigError = new double[_rows];
            _sigHas = new bool[_rows];
            _rowImitConf = new float[_rows];
            _rowImitErr = new float[_rows];
            _imitTarget = new float[_n];

            _k = s.IdentityWidth;
            if (s.identityMasks)
            {
                _idSoft = new float[_instances * _h * _k];
                _idHard = new float[_instances * _h * _k];
                ResetIdentityMasks();
            }

            _keyInputs = s.ImitationKeyInputs;
            _keyIn = new float[_keyInputs];
            if (s.imitationEnabled)
            {
                _iSlots = s.imitationSlots;
                _iStride = s.ImitationStride;
                _creditSteps = s.CreditSteps;
                _iBank = new float[_iSlots * _iStride];
                _iKeys = new uint[_iSlots * 2];
                _ring = new float[_instances * _creditSteps * (_n + 1)];
                _ringKeys = new uint[_instances * _creditSteps * 2];
                _iProj = BuildImitationProjection(s);
            }
        }

        /// <summary>
        /// The SimHash: bit b is set when offset_b + Σ R[b, i] · v_i > 0, with R uniform of unit variance scaled by
        /// 1/√inputs and offsets uniform in [-1, 1] (so it tells magnitudes apart, not only directions). From the seed.
        /// </summary>
        public static float[] BuildImitationProjection(MnnSettings s)
        {
            int inputs = s.ImitationKeyInputs, row = inputs + 1;
            var proj = new float[MnnSettings.ImitationKeyBits * row];
            float scale = 1.7320508f / MathF.Sqrt(inputs);
            for (int b = 0; b < MnnSettings.ImitationKeyBits; b++)
            {
                for (int i = 0; i < inputs; i++)
                    proj[b * row + i] = MnnHash.Signed(MnnHash.Hash(s.seed, MnnHash.StreamImitation, (uint)b, (uint)i, 0)) * scale;
                proj[b * row + inputs] = MnnHash.Signed(MnnHash.Hash(s.seed, MnnHash.StreamImitation, (uint)b, 0xFFFFu, 1));
            }
            return proj;
        }

        /// <summary>
        /// A situation's 64-bit key (bits 0-31 in <paramref name="lo"/>, 32-63 in <paramref name="hi"/>): bit b is set
        /// when offset_b + Σ R[b, i] · values_i > 0 (<see cref="BuildImitationProjection"/>). Nearby situations share
        /// most bits.
        /// </summary>
        public static void SimHash(ReadOnlySpan<float> projection, ReadOnlySpan<float> values, out uint lo, out uint hi)
        {
            int inputs = values.Length, row = inputs + 1;
            lo = 0u;
            hi = 0u;
            for (int b = 0; b < MnnSettings.ImitationKeyBits; b++)
            {
                float p = projection[b * row + inputs];
                for (int i = 0; i < inputs; i++) p += projection[b * row + i] * values[i];
                if (p > 0f)
                {
                    if (b < 32) lo |= 1u << b;
                    else hi |= 1u << (b - 32);
                }
            }
        }

        /// <summary>Bits that differ between two keys.</summary>
        public static int Hamming(uint loA, uint hiA, uint loB, uint hiB) => PopCount(loA ^ loB) + PopCount(hiA ^ hiB);

        /// <summary>Every instance's identities back to their seeded start (near 0.5, a little different per instance and head).</summary>
        public void ResetIdentityMasks()
        {
            for (int row = 0; row < _instances; row++)
                for (int j = 0; j < _h * _k; j++)
                {
                    _idSoft[row * _h * _k + j] = InitialIdentity(Settings.seed, row, j);
                    _idHard[row * _h * _k + j] = 0f;
                }
        }

        /// <summary>Instance <paramref name="row"/>'s starting identity value <paramref name="index"/> (head × K + bit).</summary>
        public static float InitialIdentity(uint seed, int row, int index)
            => 0.5f + 0.1f * MnnHash.Signed(MnnHash.Hash(seed, MnnHash.StreamIdentity, 0xFFFFFFFFu, (uint)row, (uint)index));

        /// <summary>
        /// Replaces the identities (a loaded brain). A brain saved with fewer instances fills the rest by repeating its
        /// instances in order. Ignored with identity masks off or a different H × K.
        /// </summary>
        public void SetIdentityMasks(ReadOnlySpan<float> masks)
        {
            int per = _h * _k;
            if (per == 0 || masks.Length < per || masks.Length % per != 0)
                return;
            int saved = masks.Length / per;
            for (int row = 0; row < _instances; row++)
                masks.Slice((row % saved) * per, per).CopyTo(new Span<float>(_idSoft, row * per, per));
        }

        /// <summary>Forgets every imitation entry.</summary>
        public void ClearImitation()
        {
            if (_iBank.Length > 0) Array.Clear(_iBank, 0, _iBank.Length);
            if (_iKeys.Length > 0) Array.Clear(_iKeys, 0, _iKeys.Length);
        }

        private void ClearRing(int row)
        {
            if (_creditSteps == 0 || row >= _instances) return;
            Array.Clear(_ring, row * _creditSteps * (_n + 1), _creditSteps * (_n + 1));
            Array.Clear(_ringKeys, row * _creditSteps * 2, _creditSteps * 2);
        }

        /// <summary>lerp(1, familiarExploreScale, familiarity) with familiarity exploration on; 1 otherwise.</summary>
        private float ExploreFactor(float familiarity)
            => Settings.familiarityExploration ? 1f + (Settings.familiarExploreScale - 1f) * familiarity : 1f;

        /// <summary>
        /// How much better than usual an error is, in [-1, 1]: (baseline − error) / (2 · spread), where spread is the
        /// errors' running mean absolute deviation from the baseline. Scale-free, so "+1" and "+500" rewards act alike.
        /// </summary>
        private float Advantage(float error) => Math.Clamp((_baseline - error) / (2f * _spread + 1e-8f), -1f, 1f);

        private static int PopCount(uint x)
        {
            x -= (x >> 1) & 0x55555555u;
            x = (x & 0x33333333u) + ((x >> 2) & 0x33333333u);
            x = (x + (x >> 4)) & 0x0F0F0F0Fu;
            return (int)((x * 0x01010101u) >> 24);
        }

        #region Forward hooks

        /// <summary>
        /// ImitationRead: this copy's situation key, the remembered target and confidence c, and its familiarity
        /// (the larger of c and the episodic memory's recall confidence). Runs after MemoryRead, before the heads move.
        /// </summary>
        private void Familiarize(MnnState state, ReadOnlySpan<float> inputs)
        {
            var s = Settings;
            float familiarity = 0f;
            _imitConf = 0f;
            if (s.imitationEnabled)
            {
                for (int i = 0; i < _i; i++) _keyIn[i] = inputs[i];
                if (s.imitationKeyFeedback)
                    for (int n = 0; n < _n; n++) _keyIn[_i + n] = MathF.Tanh(state.Feedback[n]);
                SimHash(_iProj, _keyIn, out uint lo, out uint hi);
                _keyLo = lo;
                _keyHi = hi;
                Recall(lo, hi);
                familiarity = _imitConf;
            }
            if (s.memoryEnabled)
                familiarity = MathF.Max(familiarity, _translatorIn[MemoryInputAt + _n]);
            _familiarityNow = familiarity;
        }

        // The remembered output for a situation: entries weighted by similarity × familiarity × quality.
        private void Recall(uint lo, uint hi)
        {
            float falloff = Settings.imitationFalloff;
            float total = 0f, top = 0f;
            for (int n = 0; n < _n; n++) _imitTarget[n] = 0f;
            for (int slot = 0; slot < _iSlots; slot++)
            {
                int at = slot * _iStride;
                if (_iBank[at + _n + 2] < 0.5f) continue;
                int distance = PopCount(lo ^ _iKeys[slot * 2]) + PopCount(hi ^ _iKeys[slot * 2 + 1]);
                float w = MathF.Exp(-(float)distance * falloff) * _iBank[at + _n + 1] * MathF.Max(_iBank[at + _n], 0f);
                if (w <= 0f) continue;
                for (int n = 0; n < _n; n++) _imitTarget[n] += w * _iBank[at + n];
                total += w;
                if (w > top) top = w;
            }
            if (total > 1e-6f)
            {
                float inv = 1f / total;
                for (int n = 0; n < _n; n++) _imitTarget[n] *= inv;
                _imitConf = MathF.Min(top, 1f);
            }
            else
                _imitConf = 0f;
        }

        /// <summary>
        /// Each head's identity reshapes its sample (in _translatorIn), before the trail: hard bits from the soft
        /// identity (+ noise while training), XOR the sample's sign bits, through P_h, × α. <paramref name="record"/>:
        /// the real network keeps its expression for the identity update.
        /// </summary>
        private void ApplyIdentity(MnnState state, float[] parameters, int row, bool record)
        {
            var s = Settings;
            int block = s.TranslatorHeadBlock;
            int idRow = row < _instances ? row : 0;
            float noiseScale = Training ? s.identityNoise * ExploreFactor(_familiarityNow) : 0f;
            for (int h = 0; h < _h; h++)
            {
                int at = h * block;
                int idAt = (idRow * _h + h) * _k;
                for (int k = 0; k < _k; k++)
                {
                    float noise = MnnHash.Signed(MnnHash.Hash(s.seed, MnnHash.StreamIdentity, state.Step, (uint)row, (uint)(h * _k + k))) * noiseScale;
                    float hard = _idSoft[idAt + k] + noise > 0.5f ? 1f : 0f;
                    if (record) _idHard[idAt + k] = hard;
                    float bit = _translatorIn[at + k] > 0f ? 1f : 0f;
                    _idBits[k] = hard != bit ? 1f : 0f;
                }
                int p = Layout.IdentityOffset + h * _k * _k;
                for (int k = 0; k < _k; k++)
                {
                    float sum = 0f;
                    for (int j = 0; j < _k; j++) sum += parameters[p + k * _k + j] * _idBits[j];
                    _translatorIn[at + k] += s.identityAlpha * sum;
                }
            }
        }

        /// <summary>After the translator: how far this copy's outputs are from the remembered target (mean squared).</summary>
        private void ImitationError()
        {
            float sum = 0f;
            if (_imitConf > 0f)
                for (int n = 0; n < _n; n++)
                {
                    float e = _out[n] - _imitTarget[n];
                    sum += e * e;
                }
            _imitErr = sum / _n;
        }

        /// <summary>The training error with imitation: (1 − c) · error + c · imitationWeight · |output − target|².</summary>
        private float BlendError(float error) => ImitationBlend(error, _imitConf, Settings.imitationWeight, _imitErr);

        /// <summary>
        /// (1 − c) · error + c · weight · imitationError: the more confident the memory, the more a copy is scored by how
        /// close it came to the remembered good output (so copies nearer to it score better).
        /// </summary>
        public static float ImitationBlend(float error, float confidence, float weight, float imitationError)
            => (1f - confidence) * error + confidence * weight * imitationError;

        /// <summary>The real network's step, for later: its key and outputs into the instance's ring, and its familiarity.</summary>
        private void RecordStep(int row, MnnState state)
        {
            var s = Settings;
            if (s.UsesFamiliarity)
            {
                _familiaritySum += _familiarityNow;
                if (row == 0) _imitationConfidence = _imitConf;
            }
            if (_creditSteps == 0)
                return;
            int at = row * _creditSteps + (int)((state.Step - 1u) % (uint)_creditSteps);
            _ringKeys[at * 2] = _keyLo;
            _ringKeys[at * 2 + 1] = _keyHi;
            for (int n = 0; n < _n; n++) _ring[at * (_n + 1) + n] = _out[n];
            _ring[at * (_n + 1) + _n] = 1f;
        }

        #endregion

        #region Signals

        /// <summary>
        /// Start of a step: last step's signal per instance. In reward modes with imitation, the reported errors are
        /// blended into the window's scores now (ImitationBlend). While training, with a baseline to judge by: the
        /// identities move (IdentityUpdate), and the imitation memory fades and takes the good attempts
        /// (ImitationWrite), in instance order.
        /// </summary>
        private void ProcessSignals(float dt)
        {
            var s = Settings;
            if (!s.UsesSignals)
                return;
            bool reward = s.trainingMode != TrainingMode.Supervised;
            if (Training)
            {
                if (s.imitationEnabled && reward)
                    for (int row = 0; row < _instances; row++)
                        if (_sigHas[row])
                        {
                            float c = _rowImitConf[row];
                            _rowError[row] += ImitationBlend((float)_sigError[row], c, s.imitationWeight, _rowImitErr[row]);
                        }

                if (!(_baseline == 0f && _trace == 0f))
                {
                    if (s.identityMasks)
                        for (int row = 0; row < _instances; row++)
                        {
                            if (!_sigHas[row]) continue;
                            float step = s.identityRate * Advantage((float)_sigError[row]);
                            for (int j = row * _h * _k; j < (row + 1) * _h * _k; j++)
                                _idSoft[j] = Math.Clamp(_idSoft[j] + step * (_idHard[j] - _idSoft[j]), 0f, 1f);
                        }

                    if (s.imitationEnabled)
                        WriteImitation(dt);
                }
            }
            Array.Clear(_sigHas, 0, _sigHas.Length);
            Array.Clear(_sigError, 0, _sigError.Length);
        }

        // Every entry fades; then each instance's good step (and, with trajectory credit, the steps before it) is written.
        private void WriteImitation(float dt)
        {
            var s = Settings;
            float fade = MathF.Exp(-dt / s.imitationHalfLife * 0.6931472f);
            for (int slot = 0; slot < _iSlots; slot++)
            {
                int at = slot * _iStride;
                if (_iBank[at + _n + 2] < 0.5f) continue;
                _iBank[at + _n] *= fade;
                _iBank[at + _n + 1] *= fade;
            }

            for (int row = 0; row < _instances; row++)
            {
                if (!_sigHas[row]) continue;
                float advantage = Advantage((float)_sigError[row]);
                if (advantage <= 0f) continue;
                uint step = State(row, 0).Step;
                float strength = advantage;
                for (int k = 0; k < _creditSteps; k++)
                {
                    long index = (long)step - 1 - k;
                    if (index < 0) break;
                    int at = row * _creditSteps + (int)(index % _creditSteps);
                    if (_ring[at * (_n + 1) + _n] > 0.5f)
                        Imitate(_ringKeys[at * 2], _ringKeys[at * 2 + 1], at * (_n + 1), strength);
                    strength *= s.creditDecay;
                }
            }
        }

        // One good attempt: merge into its situation's entry (pulling the output toward it by rate × strength), or
        // take an empty slot, or replace the weakest (quality × familiarity, ties to the lowest index).
        private void Imitate(uint lo, uint hi, int valueAt, float strength)
        {
            var s = Settings;
            int best = -1, bestDistance = s.imitationMergeBits + 1;
            for (int slot = 0; slot < _iSlots; slot++)
            {
                if (_iBank[slot * _iStride + _n + 2] < 0.5f) continue;
                int distance = PopCount(lo ^ _iKeys[slot * 2]) + PopCount(hi ^ _iKeys[slot * 2 + 1]);
                if (distance < bestDistance) { bestDistance = distance; best = slot; }
            }
            _imitationWrites++;
            if (best >= 0)
            {
                int at = best * _iStride;
                float rate = MathF.Min(1f, s.imitationRate * strength);
                for (int n = 0; n < _n; n++) _iBank[at + n] += (_ring[valueAt + n] - _iBank[at + n]) * rate;
                _iBank[at + _n] += (strength - _iBank[at + _n]) * 0.25f;
                _iBank[at + _n + 1] = MathF.Min(1f, _iBank[at + _n + 1] + 0.25f * strength);
                return;
            }

            int target = -1;
            float lowest = float.MaxValue;
            for (int slot = 0; slot < _iSlots; slot++)
            {
                int at = slot * _iStride;
                if (_iBank[at + _n + 2] < 0.5f) { target = slot; break; }
                float score = _iBank[at + _n] * _iBank[at + _n + 1];
                if (score < lowest) { lowest = score; target = slot; }
            }
            int to = target * _iStride;
            for (int n = 0; n < _n; n++) _iBank[to + n] = _ring[valueAt + n];
            _iBank[to + _n] = strength;
            _iBank[to + _n + 1] = 0.5f;
            _iBank[to + _n + 2] = 1f;
            _iKeys[target * 2] = lo;
            _iKeys[target * 2 + 1] = hi;
        }

        #endregion
    }
}
