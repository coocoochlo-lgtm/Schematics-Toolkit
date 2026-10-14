using System;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The reference implementation: the whole network and its training in plain C#. It's the source of truth the GPU
    /// kernels are tested against, the fallback where compute shaders aren't available, and often the faster choice
    /// for a single small network.
    /// <para>
    /// One step: the master MLP reads the inputs (plus last step's outputs and how training's going) and writes a
    /// steering mask per head; each head's tiny MLP turns its slice into a target in its own value space (and a liquid
    /// gate), and the head moves: pulled toward the target and, gently, toward the nearest cluster of points (a
    /// Hopfield-style attractor), at a speed set by its own timescale. Each head then samples the values around where
    /// it is and keeps a trail; the translator reads every head's sample and trail (plus the inputs) and writes the
    /// outputs, which feed back into the master next step. Optional: a shared wave-function-collapse plan the heads
    /// commit to (MnnCpu.Wfc.cs), and an episodic memory bank the translator can recall from (MnnCpu.Memory.cs).
    /// </para>
    /// <para>
    /// Training is weight perturbation: run nudged copies of the weights alongside the real ones, then move the
    /// weights toward the nudges that did better. Nothing is differentiated, so every part of it is forward-only.
    /// </para>
    /// <para>The GPU kernels (Resources/ManifoldNet.compute) mirror these methods operation for operation, in float32.</para>
    /// </summary>
    public sealed partial class MnnCpu
    {
        public readonly MnnSettings Settings;
        public readonly WeightLayout Layout;

        private readonly int _i, _n, _h, _m, _d, _p, _so;
        private readonly int _instances, _rows, _practiceRow, _copies;

        // Parameters: the weights in use, momentum (and Adam's second moment), best-so-far, one materialized copy per nudge.
        private readonly float[] _theta, _momentum, _best;
        private readonly float[][] _copyParams;
        private readonly float[] _gradient;

        // Value spaces and the heads' fixed traits.
        private readonly float[] _points, _values;
        private readonly int[] _types;
        private readonly float[] _timescale, _rest;
        private readonly SpatialGrid _grid;

        // State per (row, slot): rows are the instances plus one for practice; slots are the copies being compared.
        private readonly MnnState[] _state;

        // Scratch.
        private readonly float[] _layerA, _layerB, _masterIn, _masterOut, _slaveOut, _translatorIn, _out, _contribution, _offset;
        private readonly float[] _outputs;

        // Training bookkeeping.
        private readonly double[] _slotError;     // supervised: error per copy over the window
        private readonly double[] _rowError;      // population/sequential: reported error per row over the window
        private readonly double[] _rowPenalty;
        private readonly bool[] _rowReported;
        private readonly double[] _copyError;
        private readonly double[] _pairDelta;
        private readonly float[] _variance;
        private int _adamStep;
        private readonly int[] _copyCount;
        private int _windowStep, _windowSteps;
        private uint _window;                     // counts windows, for the noise
        private uint _practiceCount;
        private bool _windowOpen, _windowHasError;
        private int _sequentialCopy;

        // Error trace and head bookkeeping.
        private float _trace, _baseline, _alpha;
        private double _pendingError;
        private int _pendingCount;
        private readonly float[] _usage;
        private readonly int[] _lowSteps;
        private uint _reviveGeneration;
        private float _bestBaseline = float.MaxValue;
        private int _rollbackSteps;
        private bool _rolledBack, _surprised;
        private uint _steps;
        private int _updates, _revivals, _rollbacks;
        private readonly float[] _errorRing = new float[256];
        private int _ringIndex;
        private float _memoryConfidence;

        public bool Training = true;

        public int InstanceCount => _instances;
        public float[] Parameters => _theta;
        public ReadOnlySpan<float> Outputs => _outputs;
        public ReadOnlySpan<float> Points => _points;
        public ReadOnlySpan<float> ErrorRing => _errorRing;
        public int ErrorRingIndex => _ringIndex;
        public ReadOnlySpan<float> HeadUsage => _usage;

        /// <summary>Each head's base timescale τh in seconds and rest point (fixed by the seed); shared with the GPU.</summary>
        public ReadOnlySpan<float> HeadTimescales => _timescale;
        public ReadOnlySpan<float> HeadRest => _rest;
        public ReadOnlySpan<float> FixedValues => _values;

        /// <summary>Each point's type (H × P, 0..T-1), assigned from the seed. Used by the WFC grid.</summary>
        public ReadOnlySpan<int> PointTypes => _types;

        /// <summary>The spatial grid's tables (empty when off): cell start offsets per head and point order.</summary>
        internal SpatialGrid Grid => _grid;

        /// <summary>The point values in use (the trainable ones when value spaces train).</summary>
        public ReadOnlySpan<float> Values => Settings.trainableValueSpaces
            ? new ReadOnlySpan<float>(_theta, Layout.ValuesOffset, Layout.ValuesCount)
            : _values;

        public MnnStats Stats => new()
        {
            ErrorTrace = _trace, ErrorBaseline = _baseline, Alpha = _alpha,
            Updates = _updates, Revivals = _revivals, Rollbacks = _rollbacks, Steps = _steps,
            PlanResets = _planResets, MemoryWrites = _memoryWrites, MemoryConfidence = _memoryConfidence,
            ImitationConfidence = _imitationConfidence, Familiarity = _familiarity, ImitationWrites = _imitationWrites,
        };

        public MnnCpu(MnnSettings settings, int instanceCount = 1)
        {
            settings.Validate();
            Settings = settings.Clone();
            Layout = new WeightLayout(Settings);
            var s = Settings;
            _i = s.inputCount; _n = s.outputCount; _h = s.headCount; _m = s.maskWidth; _d = s.spaceDimensions; _p = s.pointsPerSpace;
            _so = s.SlaveOutputs;
            _instances = Math.Max(1, instanceCount);
            _rows = _instances + 1;
            _practiceRow = _instances;
            _copies = s.CopiesPerWindow;

            _theta = new float[Layout.Count];
            _momentum = new float[Layout.Count];
            _variance = new float[Layout.Count];
            _best = new float[Layout.Count];
            _gradient = new float[Layout.Count];
            _copyParams = new float[_copies][];
            _copyParams[0] = _theta;
            for (int c = 1; c < _copies; c++)
                _copyParams[c] = new float[Layout.Count];

            _points = new float[_h * _p * _d];
            _values = new float[_h * _p * _n];
            _types = new int[_h * _p];
            _timescale = new float[_h];
            _rest = new float[_h * _d];

            _state = new MnnState[_rows * _copies];
            for (int i = 0; i < _state.Length; i++)
                _state[i] = new MnnState(s);

            int width = Math.Max(Layout.MaxWidth, Math.Max(s.MasterInputs, s.TranslatorInputs));
            _layerA = new float[width];
            _layerB = new float[width];
            _masterIn = new float[s.MasterInputs];
            _masterOut = new float[s.MasterOutputs];
            _slaveOut = new float[_so];
            _translatorIn = new float[s.TranslatorInputs];
            _out = new float[_n];
            _contribution = new float[_h];
            _offset = new float[_d];
            _outputs = new float[_instances * _n];

            _slotError = new double[_copies];
            _rowError = new double[_rows];
            _rowPenalty = new double[_rows];
            _rowReported = new bool[_rows];
            _copyError = new double[_copies];
            _pairDelta = new double[s.perturbPairs];
            _copyCount = new int[_copies];
            _usage = new float[_h];
            _lowSteps = new int[_h];

            Generate();
            _grid = s.UsesSpatialGrid ? new SpatialGrid(s, _points) : null;
            Layout.Initialize(_theta, s.seed);
            if (s.trainableValueSpaces)
                Array.Copy(_values, 0, _theta, Layout.ValuesOffset, Layout.ValuesCount);
            Array.Copy(_theta, _best, _theta.Length);

            InitializeWfc();
            InitializeMemory();
            InitializeSignals();
            ResetState();
        }

        #region Setup

        // The value spaces and the heads' fixed traits, all from the seed.
        private void Generate()
        {
            var s = Settings;
            uint seed = s.seed;
            const float r = MnnSettings.SpaceRadius;
            int types = Math.Max(1, s.typeCount);
            for (int h = 0; h < _h; h++)
            {
                for (int j = 0; j < _p; j++)
                {
                    for (int d = 0; d < _d; d++)
                        _points[(h * _p + j) * _d + d] = MnnHash.Signed(MnnHash.Hash(seed, MnnHash.StreamPoints, (uint)h, (uint)j, (uint)d)) * r;
                    for (int n = 0; n < _n; n++)
                        _values[(h * _p + j) * _n + n] = MnnHash.Signed(MnnHash.Hash(seed, MnnHash.StreamValues, (uint)h, (uint)j, (uint)n));
                    _types[h * _p + j] = (int)(MnnHash.Hash(seed, MnnHash.StreamTypes, (uint)h, (uint)j, 0) % (uint)types);
                }
                for (int d = 0; d < _d; d++)
                    _rest[h * _d + d] = MnnHash.Signed(MnnHash.Hash(seed, MnnHash.StreamHeads, (uint)h, (uint)d, 0)) * r;
                _timescale[h] = HeadTimescale(s, h);
            }
        }

        /// <summary>
        /// Head h's base timescale τh: spread evenly on a log scale from timescaleMin (head 0, fast and twitchy) to
        /// timescaleMax (the last head, slow and steady), so the heads remember over different spans.
        /// </summary>
        public static float HeadTimescale(MnnSettings s, int head)
        {
            float t = s.headCount > 1 ? (float)head / (s.headCount - 1) : 0.5f;
            return s.timescaleMin * MathF.Pow(s.timescaleMax / s.timescaleMin, t);
        }

        /// <summary>Puts heads back at their rest points and forgets the past (one instance, or all with -1).</summary>
        public void ResetState(int instance = -1)
        {
            for (int row = 0; row < _rows; row++)
            {
                if (instance >= 0 && row != instance)
                    continue;
                for (int slot = 0; slot < _copies; slot++)
                    ResetOne(State(row, slot));
                ClearRing(row);
            }
        }

        private void ResetOne(MnnState state)
        {
            Array.Copy(_rest, state.Positions, _rest.Length);
            Array.Clear(state.Velocities, 0, state.Velocities.Length);
            Array.Copy(_rest, state.Attractors, _rest.Length);
            Array.Copy(_rest, state.Targets, _rest.Length);
            Array.Clear(state.Gates, 0, state.Gates.Length);
            Array.Clear(state.Samples, 0, state.Samples.Length);
            Array.Clear(state.Trails, 0, state.Trails.Length);
            Array.Clear(state.Feedback, 0, state.Feedback.Length);
            Array.Clear(state.PreviousInputs, 0, state.PreviousInputs.Length);
            ResetPlan(state);
            Array.Clear(state.MemoryKey, 0, state.MemoryKey.Length);
            Array.Clear(state.MemoryAttention, 0, state.MemoryAttention.Length);
            state.Step = 0;
        }

        private MnnState State(int row, int slot) => _state[row * _copies + slot];

        public MnnState CaptureState(int instance)
        {
            var copy = new MnnState(Settings);
            copy.CopyFrom(State(instance, 0));
            return copy;
        }

        public void CaptureState(int instance, MnnState into) => into.CopyFrom(State(instance, 0));

        public void RestoreState(int instance, MnnState state)
        {
            for (int slot = 0; slot < _copies; slot++)
                State(instance, slot).CopyFrom(state);
            ClearRing(instance);
        }

        /// <summary>The real (unperturbed) state of one instance, for drawing. Don't modify it.</summary>
        public MnnState PeekState(int instance) => State(instance, 0);

        /// <summary>The heads' positions for one instance (H × D), for drawing.</summary>
        public ReadOnlySpan<float> HeadPositions(int instance) => State(instance, 0).Positions;

        /// <summary>Replaces the weights (a copied or bred brain). Restarts the current training window.</summary>
        public void SetParameters(ReadOnlySpan<float> parameters)
        {
            if (parameters.Length != _theta.Length)
                throw new ArgumentException($"Expected {_theta.Length} parameters, got {parameters.Length}. The brains have different layouts.");
            parameters.CopyTo(_theta);
            ResetOptimizer();
            Array.Copy(_theta, _best, _theta.Length);
            _bestBaseline = float.MaxValue;
            _rolledBack = false;
            _windowOpen = false;
        }

        #endregion

        #region Stepping

        /// <summary>
        /// One step of every instance. <paramref name="inputs"/> is instances × inputCount. In supervised training,
        /// <paramref name="targets"/> (instances × outputCount) are the right answers, weighted per output by
        /// <paramref name="targetWeights"/> (empty for all 1; 0 means "no answer for this one").
        /// <paramref name="deltaTime"/> is the real time since the last step in seconds (0 or less: one nominal tick).
        /// </summary>
        public void Step(ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets = default, ReadOnlySpan<float> targetWeights = default, float deltaTime = 0f)
        {
            if (inputs.Length < _instances * _i)
                throw new ArgumentException($"Expected {_instances * _i} inputs.");

            var s = Settings;
            float dt = deltaTime > 0f ? deltaTime : s.TickDt;
            ProcessSignals(dt);
            FlushErrors();
            ProcessMemory(dt);
            bool supervised = Training && s.trainingMode == TrainingMode.Supervised;
            bool exploring = Training && s.trainingMode != TrainingMode.Supervised;

            // A finished window is scored and the weights move, before anything else runs.
            if (_windowOpen && _windowStep >= _windowSteps)
                CloseWindow();
            if (Training && !_windowOpen)
                OpenWindow();

            SurpriseReset();
            PlanSurpriseReset();
            float ratio = ErrorRatio();
            bool haveTargets = supervised && targets.Length >= _instances * _n;
            bool imitation = s.imitationEnabled;
            _familiaritySum = 0f;

            for (int row = 0; row < _instances; row++)
            {
                var rowInputs = inputs.Slice(row * _i, _i);
                if (supervised)
                {
                    // Every copy runs from the same state, each with its own nudge.
                    for (int slot = 0; slot < _copies; slot++)
                    {
                        float penalty = Forward(State(row, slot), _copyParams[slot], rowInputs, ratio, row, dt, slot == 0);
                        float error = haveTargets ? SquaredError(targets.Slice(row * _n, _n), Weights(targetWeights, row), out _) : 0f;
                        _slotError[slot] += (imitation && haveTargets ? BlendError(error) : error) + penalty;
                        if (slot == 0)
                        {
                            CopyOutputs(row);
                            TrackUsage(row);
                            RecordStep(row, State(row, 0));
                            if (haveTargets) { _pendingError += error; _pendingCount++; }
                            if (haveTargets && s.UsesSignals) { _sigError[row] = error; _sigHas[row] = true; }
                        }
                    }
                }
                else
                {
                    int copy = exploring ? CopyFor(row) : 0;
                    float penalty = Forward(State(row, 0), _copyParams[copy], rowInputs, ratio, row, dt, true);
                    _rowPenalty[row] += penalty;
                    _rowImitConf[row] = _imitConf;
                    _rowImitErr[row] = _imitErr;
                    CopyOutputs(row);
                    TrackUsage(row);
                    RecordStep(row, State(row, 0));
                }
            }
            if (s.UsesFamiliarity)
                _familiarity = _familiaritySum / _instances;

            if (haveTargets)
                _windowHasError = true;
            if (_windowOpen)
                _windowStep++;
            _steps++;
            UpdateHeadUsage();
        }

        /// <summary>
        /// How wrong (positive) or right (negative) the last step of one instance was, for population and sequential
        /// training. Call it after <see cref="Step"/>, any number of times; it adds up over the window.
        /// </summary>
        public void ReportError(int instance, float error)
        {
            if (instance < 0 || instance >= _instances || float.IsNaN(error))
                return;
            // With imitation, the step's report is blended with its imitation term at the start of the next step.
            if (!Settings.imitationEnabled || Settings.trainingMode == TrainingMode.Supervised)
                _rowError[instance] += error;
            if (Settings.UsesSignals && Settings.trainingMode != TrainingMode.Supervised)
            {
                _sigError[instance] += error;
                _sigHas[instance] = true;
            }
            _rowReported[instance] = true;
            _pendingError += error;
            _pendingCount++;
        }

        private ReadOnlySpan<float> Weights(ReadOnlySpan<float> weights, int row)
            => weights.Length >= (row + 1) * _n ? weights.Slice(row * _n, _n) : default;

        private void CopyOutputs(int row)
        {
            Array.Copy(_out, 0, _outputs, row * _n, _n);
            if (row == 0 && Settings.memoryEnabled)
                _memoryConfidence = _translatorIn[MemoryInputAt + _n];
        }

        private float SquaredError(ReadOnlySpan<float> target, ReadOnlySpan<float> weights, out float weightSum)
        {
            double sum = 0, total = 0;
            for (int n = 0; n < _n; n++)
            {
                float w = weights.Length == _n ? weights[n] : 1f;
                float e = _out[n] - target[n];
                sum += w * e * e;
                total += w;
            }
            weightSum = (float)total;
            return total > 1e-8 ? (float)(sum / total) : 0f;
        }

        #endregion

        #region Forward pass

        private float ErrorRatio()
        {
            if (_baseline <= 1e-8f)
                return 0f;
            return Math.Clamp(_trace / _baseline, 0f, 4f) - 1f;
        }

        private int MemoryInputAt => _h * Settings.TranslatorHeadBlock + _i;

        /// <summary>
        /// One step of one copy from one state (row is the instance, for the memory bank and the plan's dice). Leaves
        /// the outputs in _out; returns its balance penalty. The order of the blocks is the GPU's kernel order:
        /// MasterForward, MemoryRead, ImitationRead, SlaveMove, SampleSpace, WfcPlan, TranslatorForward, AccumulateError.
        /// <paramref name="record"/>: this is the real network of a live instance (it keeps its identity expression).
        /// </summary>
        private float Forward(MnnState state, float[] parameters, ReadOnlySpan<float> inputs, float ratio, int row, float dt, bool record)
        {
            var s = Settings;

            // 1. Master: inputs, last outputs, how training's going, clocks, changes → one mask slice per head (and plan biases).
            int k = 0;
            for (int i = 0; i < _i; i++) _masterIn[k++] = inputs[i];
            for (int n = 0; n < _n; n++) _masterIn[k++] = MathF.Tanh(state.Feedback[n]);
            _masterIn[k++] = ratio;
            for (int c = 0; c < s.ClockCount; c++)
            {
                float phase = 2f * MathF.PI * s.clockFrequencies[c] * state.Step;
                _masterIn[k++] = MathF.Sin(phase);
                _masterIn[k++] = MathF.Cos(phase);
            }
            if (s.changeInputs)
                for (int i = 0; i < _i; i++) _masterIn[k++] = inputs[i] - state.PreviousInputs[i];
            Mlp(Layout.Master, parameters, _masterIn, _masterOut, s.memoryEnabled ? state.MemoryKey : null);

            // 2. Memory: recall what the bank holds for a moment like this (into the translator's memory inputs).
            if (s.memoryEnabled)
                MemoryRead(state, parameters, row, _translatorIn, MemoryInputAt);

            // 2b. Imitation: the situation's key, the remembered good output and how familiar this is.
            if (s.UsesFamiliarity)
                Familiarize(state, inputs);

            // 3. Heads move: toward the master's target, settling into nearby clusters, at their own liquid pace.
            float move = dt / s.TickDt;
            for (int h = 0; h < _h; h++)
                MoveHead(state, parameters, h, dt, move);

            // 4. Each head samples its value space (and finds its attractor for next step).
            int block = s.TranslatorHeadBlock;
            ReadOnlySpan<float> values = s.trainableValueSpaces ? new ReadOnlySpan<float>(parameters, Layout.ValuesOffset, Layout.ValuesCount) : _values;
            for (int h = 0; h < _h; h++)
                SampleSpace(state, h, values, h * block);

            // 5. The shared plan: commit heads to point types, and committed heads read only their type's points.
            if (s.wfcEnabled)
                WfcPlan(state, parameters, row);

            // Each head's identity reshapes what it read.
            if (s.identityMasks)
                ApplyIdentity(state, parameters, row, record);

            // Trails (liquid: they fade faster when the gate says things are changing) and velocity readout.
            for (int h = 0; h < _h; h++)
            {
                int at = h * block;
                float rate = TrailRate(dt, s.trailTimescale, state.Gates[h], s.liquidGates);
                for (int n = 0; n < _n; n++)
                {
                    state.Samples[h * _n + n] = _translatorIn[at + n];
                    float trail = state.Trails[h * _n + n];
                    trail += (_translatorIn[at + n] - trail) * rate;
                    state.Trails[h * _n + n] = trail;
                    _translatorIn[at + _n + n] = trail;
                }
                if (s.velocityReadout)
                    for (int d = 0; d < _d; d++)
                        _translatorIn[at + 2 * _n + d] = state.Velocities[h * _d + d];
            }

            // 6. Translator: every head (and the inputs, so it never depends on one head alone) → outputs.
            int skip = _h * block;
            for (int i = 0; i < _i; i++) _translatorIn[skip + i] = inputs[i];
            Mlp(Layout.Translator, parameters, _translatorIn, _out);
            if (s.imitationEnabled)
                ImitationError();

            for (int n = 0; n < _n; n++) state.Feedback[n] = _out[n];
            for (int i = 0; i < _i; i++) state.PreviousInputs[i] = inputs[i];
            state.Step++;

            return BalancePenalty(parameters);
        }

        /// <summary>
        /// One head's move (SlaveMove): its MLP turns the mask slice into a target (and gate g); steering is
        /// pull·(target - p) + settle·(attractor - p) the short way round the torus; the liquid rate
        /// 1 - exp(-dt·(1/τh + softplus(g))) blends the velocity toward it; the speed is capped; p wraps.
        /// </summary>
        private void MoveHead(MnnState state, float[] parameters, int h, float dt, float move)
        {
            var s = Settings;
            const float r = MnnSettings.SpaceRadius;
            Mlp(Layout.Slaves[h], parameters, new ReadOnlySpan<float>(_masterOut, h * _m, _m), _slaveOut);
            float g = s.liquidGates ? _slaveOut[_d] : 0f;
            float rate = LiquidRate(dt, _timescale[h], g, s.liquidGates);
            state.Gates[h] = g;

            float speed2 = 0f;
            for (int d = 0; d < _d; d++)
            {
                int index = h * _d + d;
                float p = state.Positions[index];
                float target = MathF.Tanh(_slaveOut[d]) * r;
                state.Targets[index] = target;
                float steering = s.pull * WrapDelta(target - p) + s.settle * WrapDelta(state.Attractors[index] - p);
                float v = state.Velocities[index];
                v += (steering - v) * rate;
                state.Velocities[index] = v;
                speed2 += v * v;
            }
            float speed = MathF.Sqrt(speed2);
            if (speed > s.trajectorySpeed)
            {
                float scale = s.trajectorySpeed / speed;
                for (int d = 0; d < _d; d++) state.Velocities[h * _d + d] *= scale;
            }
            for (int d = 0; d < _d; d++)
            {
                int index = h * _d + d;
                state.Positions[index] = Wrap(state.Positions[index] + state.Velocities[index] * move);
            }
        }

        /// <summary>
        /// The liquid time constant in closed form: how far a head's velocity moves toward its steering in dt seconds,
        /// 1 - exp(-dt·(1/τ + softplus(g))). The gate g speeds it up when the input says "pay attention". Exact for any
        /// dt, so two half-steps blend exactly like one full step.
        /// </summary>
        public static float LiquidRate(float dt, float timescale, float gate, bool liquidGates)
            => 1f - MathF.Exp(-dt * (1f / timescale + (liquidGates ? Softplus(gate) : 0f)));

        /// <summary>The trail's liquid rate: 1 - exp(-dt/τtrail·(1 + softplus(g))), so trails fade faster when things change suddenly.</summary>
        public static float TrailRate(float dt, float trailTimescale, float gate, bool liquidGates)
            => 1f - MathF.Exp(-dt / trailTimescale * (1f + (liquidGates ? Softplus(gate) : 0f)));

        /// <summary>log(1 + e^x), written so it never overflows (same form on the GPU).</summary>
        public static float Softplus(float x) => MathF.Max(x, 0f) + MathF.Log(1f + MathF.Exp(-MathF.Abs(x)));

        /// <summary>One small dense network: hidden layers use the adaptive sine activation; the last is linear.</summary>
        private void Mlp(WeightLayout.Block block, float[] parameters, ReadOnlySpan<float> input, Span<float> output, float[] lastHidden = null)
        {
            ReadOnlySpan<float> x = input;
            float[] buffer = _layerA;
            for (int l = 0; l < block.LayerCount; l++)
            {
                var layer = Layout.Layers[block.FirstLayer + l];
                bool last = l == block.LayerCount - 1;
                Span<float> y = last ? output : buffer;
                for (int o = 0; o < layer.Out; o++)
                {
                    float sum = parameters[layer.Biases + o];
                    int row = layer.Weights + o * layer.In;
                    for (int i = 0; i < layer.In; i++)
                        sum += parameters[row + i] * x[i];
                    y[o] = last ? sum : Activate(sum);
                }
                if (!last)
                {
                    if (lastHidden != null && l == block.LayerCount - 2)
                        Array.Copy(buffer, lastHidden, Math.Min(lastHidden.Length, layer.Out));
                    x = new ReadOnlySpan<float>(buffer, 0, layer.Out);
                    buffer = buffer == _layerA ? _layerB : _layerA;
                }
            }
        }

        private float Activate(float x)
        {
            float calm = MathF.Tanh(x);
            return _alpha <= 0f ? calm : (1f - _alpha) * calm + _alpha * MathF.Sin(Settings.sineOmega * x);
        }

        /// <summary>
        /// SampleSpace for one head: the values where it is (a Gaussian-weighted blend of points within 3σ, or the
        /// nearest point's values), the attractor for next step's move (the same weights' centre, measured the short
        /// way round the torus), and, with the WFC grid on, the weights and values summed per point type.
        /// </summary>
        private void SampleSpace(MnnState state, int h, ReadOnlySpan<float> values, int at)
        {
            var s = Settings;
            int pointBase = h * _p;
            int positionAt = h * _d;
            bool nearestMode = s.samplingMode == SamplingMode.Nearest;
            var dest = _translatorIn;

            for (int n = 0; n < _n; n++) dest[at + n] = 0f;
            for (int d = 0; d < _d; d++) _offset[d] = 0f;
            if (s.wfcEnabled) ClearPartials(h);
            _visitTotal = 0f;
            _visitBest = float.MaxValue;
            _visitNearest = 0;

            if (_grid != null)
            {
                // Only the cells around the head (cell size ≥ 3σ), so the same points pass the cutoff.
                int cellCount = _grid.Candidates(state.Positions, positionAt);
                for (int c = 0; c < cellCount; c++)
                {
                    int startAt = h * _grid.StartStride + _grid.CellList[c];
                    for (int k = _grid.Start[startAt]; k < _grid.Start[startAt + 1]; k++)
                        Visit(state, values, h, _grid.Order[pointBase + k], at);
                }
                // Nearest mode needs the true nearest: if nothing was within one cell, fall back to every point.
                if (nearestMode && _visitBest > _grid.CellSize * _grid.CellSize)
                {
                    _visitBest = float.MaxValue;
                    for (int j = 0; j < _p; j++)
                    {
                        float d2 = Distance2(state, h, j);
                        if (d2 < _visitBest) { _visitBest = d2; _visitNearest = j; }
                    }
                }
            }
            else
            {
                for (int j = 0; j < _p; j++)
                    Visit(state, values, h, j, at);
            }

            float norm = 1f / (_visitTotal + 1e-6f);
            if (nearestMode)
                for (int n = 0; n < _n; n++)
                    dest[at + n] = values[(pointBase + _visitNearest) * _n + n];
            else
                for (int n = 0; n < _n; n++) dest[at + n] *= norm;
            for (int d = 0; d < _d; d++)
                state.Attractors[positionAt + d] = Wrap(state.Positions[positionAt + d] + _offset[d] * norm);
        }

        private float _visitTotal, _visitBest;
        private int _visitNearest;

        private float Distance2(MnnState state, int h, int j)
        {
            int point = h * _p + j;
            float d2 = 0f;
            for (int d = 0; d < _d; d++)
            {
                float delta = WrapDelta(_points[point * _d + d] - state.Positions[h * _d + d]);
                d2 += delta * delta;
            }
            return d2;
        }

        // One point's part in a head's sample, attractor and per-type sums.
        private void Visit(MnnState state, ReadOnlySpan<float> values, int h, int j, int at)
        {
            var s = Settings;
            float sigma = s.dispersion * MnnSettings.SpaceRadius;
            float cutoff = 9f * sigma * sigma;
            float inv = 1f / (2f * sigma * sigma);
            bool nearestMode = s.samplingMode == SamplingMode.Nearest;
            int point = h * _p + j;
            float d2 = Distance2(state, h, j);
            if (nearestMode && d2 < _visitBest) { _visitBest = d2; _visitNearest = j; }
            if (d2 > cutoff)
                return;
            float w = MathF.Exp(-MathF.Min(d2 * inv, 80f));
            _visitTotal += w;
            for (int d = 0; d < _d; d++)
                _offset[d] += w * WrapDelta(_points[point * _d + d] - state.Positions[h * _d + d]);
            int valueAt = point * _n;
            if (!nearestMode)
                for (int n = 0; n < _n; n++)
                    _translatorIn[at + n] += w * values[valueAt + n];
            if (s.wfcEnabled)
                AddPartial(h, _types[point], w, values, valueAt);
        }

        /// <summary>Into [-R, R): the space is a torus, so heads never escape.</summary>
        public static float Wrap(float x)
        {
            const float r = MnnSettings.SpaceRadius;
            return x - 2f * r * MathF.Floor((x + r) / (2f * r));
        }

        /// <summary>The shortest way from one point to another across the torus.</summary>
        public static float WrapDelta(float delta) => Wrap(delta);

        // How much the translator leans on each head (from its first layer), and the penalty for leaning unevenly.
        private float BalancePenalty(float[] parameters)
        {
            var s = Settings;
            var first = Layout.Layers[Layout.Translator.FirstLayer];
            int block = s.TranslatorHeadBlock;
            float sum = 0f, sum2 = 0f;
            for (int h = 0; h < _h; h++)
            {
                float c = 0f;
                for (int i = h * block; i < (h + 1) * block; i++)
                {
                    float column = 0f;
                    for (int o = 0; o < first.Out; o++)
                        column += MathF.Abs(parameters[first.Weights + o * first.In + i]);
                    c += MathF.Abs(_translatorIn[i]) * column;
                }
                _contribution[h] = c;
                sum += c;
                sum2 += c * c;
            }
            if (s.balanceLambda <= 0f || sum <= 1e-8f)
                return 0f;
            return s.balanceLambda * (_h * sum2 / (sum * sum) - 1f);
        }

        // The real network's contributions (copy 0) feed the usage averages.
        private readonly float[] _usageStep = new float[MnnSettings.MaxHeads];
        private int _usageRows;

        private void TrackUsage(int row)
        {
            for (int h = 0; h < _h; h++) _usageStep[h] += _contribution[h];
            _usageRows++;
        }

        private void UpdateHeadUsage()
        {
            if (_usageRows == 0)
                return;
            const float rate = 0.01f;
            for (int h = 0; h < _h; h++)
            {
                _usage[h] += (_usageStep[h] / _usageRows - _usage[h]) * rate;
                _usageStep[h] = 0f;
            }
            _usageRows = 0;
        }

        #endregion

        #region Training

        private int CopyFor(int row)
        {
            if (Settings.trainingMode == TrainingMode.Sequential)
                return _sequentialCopy;
            return (int)((row + _window) % (uint)_copies);
        }

        private float Sigma => Settings.perturbSigma * (Settings.alphaScalesSigma ? 1f + _alpha : 1f) * ExploreFactor(_familiarity);

        // A window starts: every copy gets its nudge, and supervised copies start from the real state.
        private void OpenWindow()
        {
            var s = Settings;
            _windowOpen = true;
            _windowStep = 0;
            _windowSteps = s.evalWindow;
            _windowHasError = false;
            Array.Clear(_slotError, 0, _slotError.Length);
            Array.Clear(_rowError, 0, _rowError.Length);
            Array.Clear(_rowPenalty, 0, _rowPenalty.Length);
            Array.Clear(_rowReported, 0, _rowReported.Length);

            if (s.trainingMode == TrainingMode.Sequential && _sequentialCopy == 0)
            {
                Array.Clear(_copyError, 0, _copyError.Length);
                Array.Clear(_copyCount, 0, _copyCount.Length);
            }
            if (s.trainingMode != TrainingMode.Sequential || _sequentialCopy == 0)
                Materialize(_window);

            if (s.trainingMode == TrainingMode.Supervised)
                for (int row = 0; row < _instances; row++)
                    Fork(row);
        }

        // ForkState: every copy of a row starts the window from the real state (slot 0).
        private void Fork(int row)
        {
            var real = State(row, 0);
            for (int slot = 1; slot < _copies; slot++)
                State(row, slot).CopyFrom(real);
        }

        /// <summary>Copy c's weights: θ + sign·σ·ε, where ε comes from the hash of (seed, window, pair, index).</summary>
        private void Materialize(uint window)
        {
            float sigma = Sigma;
            for (int c = 1; c < _copies; c++)
            {
                NudgeOf(c, out uint pair, out float sign);
                var copy = _copyParams[c];
                for (int i = 0; i < _theta.Length; i++)
                    copy[i] = _theta[i] + sign * (sigma * Layout.SigmaScale(i)) * Noise(window, pair, i);
            }
            _materializedWindow = window;
            _materializedSigma = sigma;
        }

        private uint _materializedWindow;
        private float _materializedSigma;

        private float Noise(uint window, uint pair, int index) => NoiseAt(Settings.seed, window, pair, index);

        /// <summary>
        /// The k-th noise vector's value for one weight in one window: a PCG hash of (seed, window, k, index), unit
        /// variance. Never stored or uploaded: the GPU computes the same number wherever it needs it.
        /// </summary>
        public static float NoiseAt(uint seed, uint window, uint pair, int index)
            => MnnHash.Noise(MnnHash.Hash(seed, MnnHash.StreamNoise, window, pair, (uint)index));

        private void NudgeOf(int copy, out uint pair, out float sign)
        {
            if (Settings.perturbMode == PerturbMode.Antithetic)
            {
                pair = (uint)((copy - 1) / 2);
                sign = (copy - 1) % 2 == 0 ? 1f : -1f;
            }
            else
            {
                pair = (uint)(copy - 1);
                sign = 1f;
            }
        }

        // The window is over: score the copies and move the weights.
        private void CloseWindow()
        {
            var s = Settings;
            _windowOpen = false;
            bool update = false;

            switch (s.trainingMode)
            {
                case TrainingMode.Supervised:
                    if (_windowHasError)
                    {
                        double scale = 1.0 / (_instances * _windowSteps);
                        for (int c = 0; c < _copies; c++) { _copyError[c] = _slotError[c] * scale; _copyCount[c] = 1; }
                        update = true;
                    }
                    // RestoreState: the real state (slot 0) simply carries on; copies are re-forked next window.
                    break;

                case TrainingMode.Population:
                    Array.Clear(_copyError, 0, _copyError.Length);
                    Array.Clear(_copyCount, 0, _copyCount.Length);
                    for (int row = 0; row < _instances; row++)
                    {
                        if (!_rowReported[row]) continue;
                        int c = CopyFor(row);
                        _copyError[c] += (_rowError[row] + _rowPenalty[row]) / _windowSteps;
                        _copyCount[c]++;
                    }
                    update = true;
                    break;

                case TrainingMode.Sequential:
                    if (_rowReported[0])
                    {
                        _copyError[_sequentialCopy] += (_rowError[0] + _rowPenalty[0]) / _windowSteps;
                        _copyCount[_sequentialCopy]++;
                    }
                    _sequentialCopy++;
                    if (_sequentialCopy >= _copies)
                    {
                        _sequentialCopy = 0;
                        update = true;
                    }
                    break;
            }

            if (update && ApplyUpdate())
                _updates++;
            if (s.trainingMode != TrainingMode.Sequential || _sequentialCopy == 0)
                _window++;
            AfterUpdate();
        }

        /// <summary>
        /// Moves θ toward the nudges that did better: θ += η · (1/K) Σ (E⁻ − E⁺)/(2σ) · ε_k (antithetic), or
        /// (E₀ − E_k)/σ (one-sided), through momentum or Adam. False if there wasn't enough to compare.
        /// </summary>
        private bool ApplyUpdate()
        {
            var s = Settings;
            Array.Clear(_gradient, 0, _gradient.Length);
            int used = 0;

            // Score differences per pair (positive = the + nudge did better).
            if (s.perturbMode == PerturbMode.Antithetic)
            {
                for (int k = 0; k < s.perturbPairs; k++)
                {
                    int plus = 1 + 2 * k, minus = plus + 1;
                    _pairDelta[k] = _copyCount[plus] == 0 || _copyCount[minus] == 0 ? double.NaN
                        : _copyError[minus] / _copyCount[minus] - _copyError[plus] / _copyCount[plus];
                }
            }
            else
            {
                double eBase = _copyCount[0] > 0 ? _copyError[0] / _copyCount[0] : double.NaN;
                for (int k = 0; k < s.perturbPairs; k++)
                {
                    int copy = 1 + k;
                    _pairDelta[k] = _copyCount[copy] == 0 ? double.NaN : eBase - _copyError[copy] / _copyCount[copy];
                }
            }

            // Each difference, per unit of nudge, is how steep the error is along that nudge.
            double denominator = s.perturbMode == PerturbMode.Antithetic ? 2.0 * _materializedSigma : _materializedSigma;
            for (int k = 0; k < s.perturbPairs; k++)
            {
                if (!double.IsFinite(_pairDelta[k])) continue;   // a blown-up copy says nothing useful
                Accumulate((uint)k, (float)(_pairDelta[k] / denominator));
                used++;
            }
            if (used == 0)
                return false;

            float inv = 1f / used;
            if (s.updateRule == UpdateRule.Adam)
            {
                // Every weight's step is scaled by its own recent gradient size, so steps stay a steady size whatever
                // the scale of the errors (or rewards), and shrink where the signal is mostly noise.
                _adamStep++;
                float beta1 = s.momentum, beta2 = 0.999f;
                float correction1 = 1f - MathF.Pow(beta1, _adamStep);
                float correction2 = 1f - MathF.Pow(beta2, _adamStep);
                for (int i = 0; i < _theta.Length; i++)
                {
                    float g = _gradient[i] * inv;
                    float m = beta1 * _momentum[i] + (1f - beta1) * g;
                    float v = beta2 * _variance[i] + (1f - beta2) * g * g;
                    _momentum[i] = m;
                    _variance[i] = v;
                    float step = (m / correction1) / (MathF.Sqrt(v / correction2) + 1e-8f);
                    _theta[i] += s.learningRate * Layout.RateScale(i) * step;
                }
            }
            else
            {
                // The blueprint's rule: a plain step along the estimate, with momentum.
                for (int i = 0; i < _theta.Length; i++)
                {
                    float m = s.momentum * _momentum[i] + _gradient[i] * inv;
                    _momentum[i] = m;
                    _theta[i] += s.learningRate * Layout.RateScale(i) * m;
                }
            }
            return true;
        }

        private void ResetOptimizer()
        {
            Array.Clear(_momentum, 0, _momentum.Length);
            Array.Clear(_variance, 0, _variance.Length);
            _adamStep = 0;
        }

        private void Accumulate(uint pair, float weight)
        {
            for (int i = 0; i < _gradient.Length; i++)
                _gradient[i] += weight * Noise(_materializedWindow, pair, i);
        }

        // After the weights move: keep the best, roll back from a cliff, revive neglected heads.
        private void AfterUpdate()
        {
            var s = Settings;
            int warmup = (int)Math.Min(2000f, 1f / Math.Max(1e-4f, 1f - s.baselineFalloff));
            if (s.bestWeightsRollback && _steps > warmup && _baseline > 0f)
            {
                if (_baseline < _bestBaseline)
                {
                    _bestBaseline = _baseline;
                    Array.Copy(_theta, _best, _theta.Length);
                    _rollbackSteps = 0;
                    _rolledBack = false;
                }
                else if (_baseline > _bestBaseline * (1f + s.rollbackMargin))
                {
                    _rollbackSteps += _windowSteps;
                    if (_rollbackSteps >= s.rollbackSteps)
                    {
                        _rollbackSteps = 0;
                        if (_rolledBack)
                        {
                            // The restored weights couldn't hold the best either: it was a lucky stretch, not a
                            // level to return to. Accept where training is now as the new reference.
                            _bestBaseline = _baseline;
                            Array.Copy(_theta, _best, _theta.Length);
                            _rolledBack = false;
                        }
                        else
                        {
                            Array.Copy(_best, _theta, _theta.Length);
                            ResetOptimizer();
                            _rollbacks++;
                            _rolledBack = true;
                            // The restored weights earned the best baseline: judge them from there, or the stale
                            // average of the bad stretch would roll back to the same weights again at once.
                            _baseline = _bestBaseline;
                            _trace = Math.Min(_trace, _bestBaseline);
                        }
                    }
                }
                else
                    _rollbackSteps = 0;
            }

            ReviveHeads();
        }

        private void ReviveHeads()
        {
            var s = Settings;
            if (s.reviveSteps <= 0 || _h < 2)
                return;
            float total = 0f;
            for (int h = 0; h < _h; h++) total += _usage[h];
            if (total <= 1e-8f)
                return;
            float mean = total / _h;
            for (int h = 0; h < _h; h++)
            {
                if (_usage[h] / total >= s.reviveThreshold)
                {
                    _lowSteps[h] = 0;
                    continue;
                }
                _lowSteps[h] += _windowSteps;
                if (_lowSteps[h] < s.reviveSteps)
                    continue;

                // New weights for its MLP, and a fresh start somewhere random.
                _reviveGeneration++;
                Layout.Reinitialize(_theta, Layout.Slaves[h], s.seed, _reviveGeneration);
                var block = Layout.Slaves[h];
                Array.Clear(_momentum, block.Start, block.End - block.Start);
                Array.Clear(_variance, block.Start, block.End - block.Start);
                for (int row = 0; row < _rows; row++)
                    for (int slot = 0; slot < _copies; slot++)
                    {
                        var state = State(row, slot);
                        for (int d = 0; d < _d; d++)
                        {
                            float p = MnnHash.Signed(MnnHash.Hash(s.seed, MnnHash.StreamRevive, _reviveGeneration, (uint)h, (uint)d)) * MnnSettings.SpaceRadius;
                            state.Positions[h * _d + d] = p;
                            state.Attractors[h * _d + d] = p;
                            state.Velocities[h * _d + d] = 0f;
                        }
                        for (int n = 0; n < _n; n++) state.Trails[h * _n + n] = 0f;
                    }
                _usage[h] = mean;
                _lowSteps[h] = 0;
                _revivals++;
            }
        }

        // Error trace and baseline from the errors of the last step, and the activation's α from their ratio.
        private void FlushErrors()
        {
            var s = Settings;
            if (_pendingCount > 0)
            {
                float error = (float)(_pendingError / _pendingCount);
                if (_baseline == 0f && _trace == 0f)
                {
                    _trace = error;
                    _baseline = error;
                    _spread = MathF.Abs(error);
                }
                else
                {
                    _spread += (MathF.Abs(error - _baseline) - _spread) * (1f - s.baselineFalloff);
                    _trace += (error - _trace) * (1f - s.errorFalloff);
                    _baseline += (error - _baseline) * (1f - s.baselineFalloff);
                }
                _errorRing[_ringIndex] = error;
                _ringIndex = (_ringIndex + 1) % _errorRing.Length;
                _pendingError = 0;
                _pendingCount = 0;
            }
            _alpha = _baseline > 1e-8f ? Math.Clamp(s.sineGain * (_trace / _baseline - 1f), 0f, s.sineAlphaMax) * ExploreFactor(_familiarity) : 0f;
        }

        // A sudden spike in error pulls the heads partway back toward rest, dropping a context that just went wrong.
        private void SurpriseReset()
        {
            var s = Settings;
            if (!s.surpriseReset || _baseline <= 1e-8f)
                return;
            float ratio = _trace / _baseline;
            if (ratio < 1f)
            {
                _surprised = false;
                return;
            }
            if (_surprised || ratio <= s.resetThreshold)
                return;
            _surprised = true;
            for (int row = 0; row < _instances; row++)
                for (int slot = 0; slot < _copies; slot++)
                {
                    var state = State(row, slot);
                    for (int i = 0; i < _rest.Length; i++)
                    {
                        float p = Wrap(state.Positions[i] + WrapDelta(_rest[i] - state.Positions[i]) * s.resetAmount);
                        state.Positions[i] = p;
                        state.Attractors[i] = p;
                        state.Velocities[i] *= 1f - s.resetAmount;
                    }
                }
        }

        #endregion

        #region Practice

        /// <summary>
        /// Trains on a recorded stretch: starting from <paramref name="start"/>, runs <paramref name="steps"/> steps of
        /// recorded inputs with every nudged copy, scores them against <paramref name="targets"/> (weighted per output
        /// by <paramref name="weights"/>), and moves the weights. The live instances aren't touched, but their current
        /// training window restarts. Used for replaying lessons, e.g. decisions credited by a reward. Runs at the
        /// nominal tick; reads the memory bank (bank 0 when it's per instance) but never writes it.
        /// </summary>
        public bool Practice(MnnState start, ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights, int steps)
        {
            if (!Training || steps <= 0 || inputs.Length < steps * _i || targets.Length < steps * _n)
                return false;
            var s = Settings;

            // A fresh set of nudges, apart from the live windows'.
            FlushErrors();
            uint noiseWindow = 0x80000000u | _practiceCount++;
            Materialize(noiseWindow);
            for (int slot = 0; slot < _copies; slot++)
                State(_practiceRow, slot).CopyFrom(start);

            Array.Clear(_slotError, 0, _slotError.Length);
            double weightTotal = 0;
            float ratio = ErrorRatio();
            float dt = s.TickDt;
            for (int t = 0; t < steps; t++)
            {
                var stepInputs = inputs.Slice(t * _i, _i);
                var stepTargets = targets.Slice(t * _n, _n);
                var stepWeights = weights.Length >= (t + 1) * _n ? weights.Slice(t * _n, _n) : default;
                for (int slot = 0; slot < _copies; slot++)
                {
                    float penalty = Forward(State(_practiceRow, slot), _copyParams[slot], stepInputs, ratio, _practiceRow, dt, false);
                    float error = SquaredError(stepTargets, stepWeights, out float weightSum);
                    // Steps with stronger lessons count for more.
                    _slotError[slot] += error * weightSum + penalty;
                    if (slot == 0) weightTotal += weightSum;
                }
            }
            if (weightTotal <= 1e-8)
                return false;

            for (int c = 0; c < _copies; c++) { _copyError[c] = _slotError[c] / steps; _copyCount[c] = 1; }
            bool updated = ApplyUpdate();
            if (updated) _updates++;

            // The live window's nudges were made from the old weights.
            _windowOpen = false;
            if (s.trainingMode == TrainingMode.Sequential) _sequentialCopy = 0;
            return updated;
        }

        #endregion
    }
}
