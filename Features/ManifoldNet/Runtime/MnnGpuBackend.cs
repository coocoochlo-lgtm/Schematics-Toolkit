using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The network on the GPU: every instance and every nudged copy steps in one set of dispatches (one thread per
    /// state, or per state and head), and training updates every weight in parallel. It mirrors <see cref="MnnCpu"/>
    /// step for step; the slow-moving bookkeeping (error trace, baseline, α, head usage, revival, rollback, surprise
    /// resets) stays on the CPU, fed by a small asynchronous readback each step, exactly as MnnCpu computes it.
    /// </summary>
    internal sealed class MnnGpuBackend : IMnnBackend
    {
        private const int Threads = 64;

        private readonly MnnSettings _s;
        private readonly WeightLayout _layout;
        private readonly ComputeShader _shader;
        private readonly int _instances, _rows, _practiceRow, _copies, _stateFloats, _statsStride, _banks;
        private readonly bool _wfc, _memory, _identity, _imitation;
        private readonly int _t, _f, _k, _creditSteps;

        private readonly int _kMemoryQueue, _kMemoryFade, _kMemoryRecall, _kMasterForward, _kMemoryRead, _kSlaveMove, _kSampleSpace,
            _kWfcPlan, _kTranslatorForward, _kAccumulateError, _kForkState, _kReduceSupervised, _kReduceReported, _kClearCopyErrors,
            _kApplyUpdate, _kCopyParameters, _kReinitializeLayer, _kReviveHead, _kResetRows, _kSurpriseReset, _kPlanReset,
            _kPackState, _kUnpackState, _kImitationRead, _kImitationBlend, _kIdentityUpdate, _kImitationWrite, _kClearRows;
        private readonly int[] _kernels;

        private GraphicsBuffer _params, _moment, _variance, _best;
        private GraphicsBuffer _points, _values, _timescale, _rest, _clocks, _pointTypes, _gridStart, _gridOrder, _neighbors, _layerTable;
        private GraphicsBuffer _pos, _vel, _attr, _target, _gate, _sample, _trail, _feedback, _prevInput, _stepCount;
        private GraphicsBuffer _planSpread, _planType, _planAge, _typePartials, _memKey, _memAttn, _memSlots, _memQueue;
        private GraphicsBuffer _inputs, _targets, _targetWeights, _outputs, _scratch;
        private GraphicsBuffer _slotError, _rowPenalty, _stepStats, _copyError, _copyCount, _rowReports, _staging;
        private GraphicsBuffer _idSoft, _idHard, _imitProj, _imitSlots, _imitKeys, _imitStateKey, _imitRing, _imitRingKey, _signal, _rowImit, _rowBlend;

        // Upload scratch, preallocated so a step allocates nothing.
        private readonly float[] _inputUpload, _targetUpload, _weightUpload, _reportUpload, _queueUpload, _signalUpload;
        private readonly float[] _latestOutputs, _statsScratch;
        private bool _haveOutputs, _anyQueued;

        // Bookkeeping, as in MnnCpu.
        private readonly double[] _rowError, _sigError;
        private readonly bool[] _rowReported, _sigHas;
        private int _windowStep, _windowSteps, _sequentialCopy, _adamStep;
        private uint _window, _practiceCount, _noiseWindow;
        private float _sigma;
        private bool _windowOpen, _windowHasError;
        private float _trace, _baseline, _alpha;
        private double _pendingError;
        private int _pendingCount;
        private readonly float[] _usage, _errorRing = new float[256], _confidenceRing = new float[256];
        private readonly int[] _lowSteps;
        private int _ringIndex;
        private uint _reviveGeneration;
        private float _bestBaseline = float.MaxValue;
        private int _rollbackSteps;
        private bool _rolledBack, _surprised, _planSurprised;
        private uint _steps;
        private int _updates, _revivals, _rollbacks, _planResets, _memoryWrites;
        private float _memoryConfidence, _spread, _familiarity, _imitationConfidence;

        /// <summary>Read stats and outputs back right away instead of a frame later (tests, editor tools).</summary>
        public bool Synchronous;

        public bool Training { get; set; } = true;
        public int Updates => _updates;
        public uint LastPlanReset { get; private set; }

        public MnnStats Stats => new()
        {
            ErrorTrace = _trace, ErrorBaseline = _baseline, Alpha = _alpha,
            Updates = _updates, Revivals = _revivals, Rollbacks = _rollbacks, Steps = _steps,
            PlanResets = _planResets, MemoryWrites = _memoryWrites, MemoryConfidence = _memoryConfidence,
            ImitationConfidence = _imitationConfidence, Familiarity = _familiarity,
        };

        public static bool Supported => SystemInfo.supportsComputeShaders && LoadShader() != null;

        private static ComputeShader LoadShader() => Resources.Load<ComputeShader>("ManifoldNet");

        public MnnGpuBackend(MnnSettings settings, int instances)
        {
            settings.Validate();
            _s = settings.Clone();
            _layout = new WeightLayout(_s);
            _shader = UnityEngine.Object.Instantiate(LoadShader());
            _instances = Math.Max(1, instances);
            _rows = _instances + 1;
            _practiceRow = _instances;
            _copies = _s.CopiesPerWindow;
            _wfc = _s.wfcEnabled;
            _memory = _s.memoryEnabled;
            _t = _wfc ? _s.typeCount : 0;
            _f = _s.PlanColumns;
            _banks = _memory ? _s.MemoryBanks(_instances) : 0;
            _identity = _s.identityMasks;
            _imitation = _s.imitationEnabled;
            _k = _s.IdentityWidth;
            _creditSteps = _s.CreditSteps;
            _stateFloats = new MnnState(_s).FlatLength;
            _statsStride = _s.headCount + 4;

            _kMemoryQueue = _shader.FindKernel("MemoryQueue");
            _kMemoryFade = _shader.FindKernel("MemoryFade");
            _kMemoryRecall = _shader.FindKernel("MemoryRecall");
            _kMasterForward = _shader.FindKernel("MasterForward");
            _kMemoryRead = _shader.FindKernel("MemoryRead");
            _kSlaveMove = _shader.FindKernel("SlaveMove");
            _kSampleSpace = _shader.FindKernel("SampleSpace");
            _kWfcPlan = _shader.FindKernel("WfcPlan");
            _kTranslatorForward = _shader.FindKernel("TranslatorForward");
            _kAccumulateError = _shader.FindKernel("AccumulateError");
            _kForkState = _shader.FindKernel("ForkState");
            _kReduceSupervised = _shader.FindKernel("ReduceSupervised");
            _kReduceReported = _shader.FindKernel("ReduceReported");
            _kClearCopyErrors = _shader.FindKernel("ClearCopyErrors");
            _kApplyUpdate = _shader.FindKernel("ApplyUpdate");
            _kCopyParameters = _shader.FindKernel("CopyParameters");
            _kReinitializeLayer = _shader.FindKernel("ReinitializeLayer");
            _kReviveHead = _shader.FindKernel("ReviveHead");
            _kResetRows = _shader.FindKernel("ResetRows");
            _kSurpriseReset = _shader.FindKernel("SurpriseReset");
            _kPlanReset = _shader.FindKernel("PlanReset");
            _kPackState = _shader.FindKernel("PackState");
            _kUnpackState = _shader.FindKernel("UnpackState");
            _kImitationRead = _shader.FindKernel("ImitationRead");
            _kImitationBlend = _shader.FindKernel("ImitationBlend");
            _kIdentityUpdate = _shader.FindKernel("IdentityUpdate");
            _kImitationWrite = _shader.FindKernel("ImitationWrite");
            _kClearRows = _shader.FindKernel("ClearRows");
            _kernels = new[] { _kMemoryQueue, _kMemoryFade, _kMemoryRecall, _kMasterForward, _kMemoryRead, _kSlaveMove, _kSampleSpace,
                _kWfcPlan, _kTranslatorForward, _kAccumulateError, _kForkState, _kReduceSupervised, _kReduceReported, _kClearCopyErrors,
                _kApplyUpdate, _kCopyParameters, _kReinitializeLayer, _kReviveHead, _kResetRows, _kSurpriseReset, _kPlanReset,
                _kPackState, _kUnpackState, _kImitationRead, _kImitationBlend, _kIdentityUpdate, _kImitationWrite, _kClearRows };

            _inputUpload = new float[_rows * _s.inputCount];
            _targetUpload = new float[_rows * _s.outputCount];
            _weightUpload = new float[_rows * _s.outputCount];
            _reportUpload = new float[_rows * 2];
            _queueUpload = new float[_instances * (_s.outputCount + 3)];
            _latestOutputs = new float[_instances * _s.outputCount];
            _statsScratch = new float[_rows * _statsStride];
            _rowError = new double[_rows];
            _rowReported = new bool[_rows];
            _sigError = new double[_rows];
            _sigHas = new bool[_rows];
            _signalUpload = new float[_rows * 2];
            _usage = new float[_s.headCount];
            _lowSteps = new int[_s.headCount];

            CreateBuffers();
            BindAll();
            ResetState(-1);
        }

        #region Buffers

        private static GraphicsBuffer Buffer(int count, int stride = sizeof(float))
            => new(GraphicsBuffer.Target.Structured, Math.Max(1, count), stride);

        private void CreateBuffers()
        {
            int h = _s.headCount, d = _s.spaceDimensions, n = _s.outputCount, i = _s.inputCount, p = _s.pointsPerSpace;
            int states = _rows * _copies;
            int cells = _wfc ? h * _f : 0;

            // Weights, value spaces, point types and the grid tables come from the CPU reference, so both start identical.
            var reference = new MnnCpu(_s, 1);
            _params = Buffer(_layout.Count); _params.SetData(reference.Parameters);
            _moment = Buffer(_layout.Count); _moment.SetData(new float[_layout.Count]);
            _variance = Buffer(_layout.Count); _variance.SetData(new float[_layout.Count]);
            _best = Buffer(_layout.Count); _best.SetData(reference.Parameters);

            _points = Buffer(h * p * d); _points.SetData(reference.Points.ToArray());
            _values = Buffer(h * p * n); _values.SetData(reference.FixedValues.ToArray());
            _timescale = Buffer(h); _timescale.SetData(reference.HeadTimescales.ToArray());
            _rest = Buffer(h * d); _rest.SetData(reference.HeadRest.ToArray());
            var clocks = _s.ClockCount > 0 ? _s.clockFrequencies : new[] { 0f };
            _clocks = Buffer(clocks.Length); _clocks.SetData(clocks);

            var types = reference.PointTypes.ToArray();
            var typesU = new uint[types.Length];
            for (int k = 0; k < types.Length; k++) typesU[k] = (uint)types[k];
            _pointTypes = Buffer(typesU.Length, sizeof(uint)); _pointTypes.SetData(typesU);

            var grid = reference.Grid;
            if (grid != null)
            {
                _gridStart = Buffer(grid.Start.Length, sizeof(uint)); _gridStart.SetData(grid.Start);
                _gridOrder = Buffer(grid.Order.Length, sizeof(uint)); _gridOrder.SetData(grid.Order);
            }
            else
            {
                _gridStart = Buffer(1, sizeof(uint)); _gridStart.SetData(new uint[1]);
                _gridOrder = Buffer(1, sizeof(uint)); _gridOrder.SetData(new uint[1]);
            }

            MnnCpu.BuildNeighbors(_s, out var start, out var list);
            var neighbors = new int[start.Length + list.Length];
            Array.Copy(start, neighbors, start.Length);
            Array.Copy(list, 0, neighbors, start.Length, list.Length);
            _neighbors = Buffer(neighbors.Length, sizeof(int)); _neighbors.SetData(neighbors);

            var table = new Vector4Int[_layout.Layers.Length];
            for (int l = 0; l < table.Length; l++)
            {
                var layer = _layout.Layers[l];
                table[l] = new Vector4Int(layer.In, layer.Out, layer.Weights, layer.Biases);
            }
            _layerTable = Buffer(table.Length, 16); _layerTable.SetData(table);

            _pos = Buffer(states * h * d);
            _vel = Buffer(states * h * d);
            _attr = Buffer(states * h * d);
            _target = Buffer(states * h * d);
            _gate = Buffer(states * h);
            _sample = Buffer(states * h * n);
            _trail = Buffer(states * h * n);
            _feedback = Buffer(states * n);
            _prevInput = Buffer(states * i);
            _stepCount = Buffer(states, sizeof(uint));

            _planSpread = Buffer(states * cells * _t);
            _planType = Buffer(states * cells, sizeof(int));
            _planAge = Buffer(states * cells, sizeof(int));
            _typePartials = Buffer(_wfc ? states * h * _t * (n + 1) : 1);
            _memKey = Buffer(_memory ? states * _s.hiddenWidth : 1);
            _memAttn = Buffer(_memory ? states * _s.memorySlots : 1);
            _memSlots = Buffer(_memory ? _banks * _s.memorySlots * _s.MemoryStride : 1);
            _memSlots.SetData(new float[_memSlots.count]);
            _memQueue = Buffer(_queueUpload.Length);

            _inputs = Buffer(_rows * i);
            _targets = Buffer(_rows * n);
            _targetWeights = Buffer(_rows * n);
            _outputs = Buffer(_instances * n);
            _scratch = Buffer(states * ScratchStride);

            _slotError = Buffer(states); _slotError.SetData(new float[states]);
            _rowPenalty = Buffer(_rows); _rowPenalty.SetData(new float[_rows]);
            _stepStats = Buffer(_rows * _statsStride);
            _copyError = Buffer(_copies); _copyError.SetData(new float[_copies]);
            _copyCount = Buffer(_copies); _copyCount.SetData(new float[_copies]);
            _rowReports = Buffer(_rows * 2);
            _staging = Buffer(_stateFloats);

            // Identity masks (seeded per instance, as MnnCpu) and the imitation memory.
            int masks = _instances * h * _k;
            _idSoft = Buffer(masks);
            _idHard = Buffer(masks); _idHard.SetData(new float[Math.Max(1, masks)]);
            ResetIdentityMasks();
            var projection = _imitation ? MnnCpu.BuildImitationProjection(_s) : new float[1];
            _imitProj = Buffer(projection.Length); _imitProj.SetData(projection);
            int slots = _imitation ? _s.imitationSlots : 0;
            _imitSlots = Buffer(slots * _s.ImitationStride); _imitSlots.SetData(new float[_imitSlots.count]);
            _imitKeys = Buffer(slots * 2, sizeof(uint)); _imitKeys.SetData(new uint[_imitKeys.count]);
            _imitStateKey = Buffer(_imitation ? states * 2 : 1, sizeof(uint));
            _imitRing = Buffer(_instances * _creditSteps * (n + 1)); _imitRing.SetData(new float[_imitRing.count]);
            _imitRingKey = Buffer(_instances * _creditSteps * 2, sizeof(uint)); _imitRingKey.SetData(new uint[_imitRingKey.count]);
            _signal = Buffer(_rows * 2); _signal.SetData(new float[_rows * 2]);
            _rowImit = Buffer(_rows * 2); _rowImit.SetData(new float[_rows * 2]);
            _rowBlend = Buffer(_rows); _rowBlend.SetData(new float[_rows]);
        }

        // Matches the compute shader's scratch layout (see its comment above MasterAt).
        private int ScratchFixed => _s.MasterInputs + _s.MasterOutputs + _s.TranslatorInputs + 2 * _s.hiddenWidth + _s.outputCount
            + 3 * _t * _t + _t + _s.outputCount + 2 + _s.ImitationKeyInputs;
        private int HeadScratch => 2 * _s.hiddenWidth + _s.SlaveOutputs;
        private int ScratchStride => ScratchFixed + _s.headCount * HeadScratch;

        private struct Vector4Int
        {
            public int X, Y, Z, W;
            public Vector4Int(int x, int y, int z, int w) { X = x; Y = y; Z = z; W = w; }
        }

        private void BindAll()
        {
            var bindings = new (string, GraphicsBuffer)[]
            {
                ("_Params", _params), ("_Moment", _moment), ("_Variance", _variance), ("_Best", _best),
                ("_Points", _points), ("_Values", _values), ("_Timescale", _timescale), ("_Rest", _rest), ("_ClockFrequencies", _clocks),
                ("_PointTypes", _pointTypes), ("_GridStart", _gridStart), ("_GridOrder", _gridOrder), ("_Neighbors", _neighbors),
                ("_LayerTable", _layerTable),
                ("_Pos", _pos), ("_Vel", _vel), ("_Attr", _attr), ("_Target", _target), ("_Gate", _gate), ("_Sample", _sample),
                ("_Trail", _trail), ("_Feedback", _feedback), ("_PrevInput", _prevInput), ("_StepCount", _stepCount),
                ("_PlanSpread", _planSpread), ("_PlanType", _planType), ("_PlanAge", _planAge), ("_TypePartials", _typePartials),
                ("_MemKey", _memKey), ("_MemAttn", _memAttn), ("_MemSlots", _memSlots), ("_MemQueue", _memQueue),
                ("_Inputs", _inputs), ("_Targets", _targets), ("_TargetWeights", _targetWeights), ("_Outputs", _outputs), ("_Scratch", _scratch),
                ("_SlotError", _slotError), ("_RowPenalty", _rowPenalty), ("_StepStats", _stepStats), ("_CopyError", _copyError),
                ("_CopyCount", _copyCount), ("_RowReports", _rowReports), ("_Staging", _staging),
                ("_IdSoft", _idSoft), ("_IdHard", _idHard), ("_ImitProj", _imitProj), ("_ImitSlots", _imitSlots), ("_ImitKeys", _imitKeys),
                ("_ImitStateKey", _imitStateKey), ("_ImitRing", _imitRing), ("_ImitRingKey", _imitRingKey), ("_Signal", _signal),
                ("_RowImit", _rowImit), ("_RowBlend", _rowBlend),
            };
            foreach (int k in _kernels)
                foreach (var (name, buffer) in bindings)
                    _shader.SetBuffer(k, name, buffer);

            var s = _s;
            SetInt("_I", s.inputCount); SetInt("_N", s.outputCount); SetInt("_H", s.headCount); SetInt("_M", s.maskWidth);
            SetInt("_D", s.spaceDimensions); SetInt("_P", s.pointsPerSpace); SetInt("_SO", s.SlaveOutputs);
            SetInt("_HiddenWidth", s.hiddenWidth); SetInt("_HiddenLayers", s.hiddenLayers);
            SetInt("_MasterInputs", s.MasterInputs); SetInt("_MasterOutputs", s.MasterOutputs);
            SetInt("_TranslatorInputs", s.TranslatorInputs); SetInt("_HeadBlock", s.TranslatorHeadBlock);
            SetInt("_Copies", _copies); SetInt("_Rows", _rows); SetInt("_Instances", _instances);
            SetInt("_Seed", unchecked((int)s.seed));
            SetInt("_MlpCount", _layout.MlpCount); SetInt("_RulesOffset", _layout.RulesOffset); SetInt("_RulesCount", _layout.RulesCount);
            SetInt("_BetaOffset", _layout.BetaOffset); SetInt("_ValuesOffset", _layout.ValuesOffset); SetInt("_ParameterCount", _layout.Count);
            SetInt("_TrainableValues", s.trainableValueSpaces ? 1 : 0); SetInt("_Antithetic", s.perturbMode == PerturbMode.Antithetic ? 1 : 0);
            SetInt("_Pairs", s.perturbPairs);
            _shader.SetFloat("_ValueSigmaScale", s.valueSpaceRate);
            _shader.SetFloat("_Omega", s.sineOmega);
            _shader.SetFloat("_AlphaMax", s.sineAlphaMax);
            _shader.SetFloat("_Pull", s.pull);
            _shader.SetFloat("_Settle", s.settle);
            _shader.SetFloat("_TrajectorySpeed", s.trajectorySpeed);
            _shader.SetFloat("_TrailTimescale", s.trailTimescale);
            _shader.SetFloat("_Dispersion", s.dispersion);
            SetInt("_Gates", s.liquidGates ? 1 : 0);
            SetInt("_Nearest", s.samplingMode == SamplingMode.Nearest ? 1 : 0);
            SetInt("_ClockCount", s.ClockCount); SetInt("_ChangeInputs", s.changeInputs ? 1 : 0); SetInt("_VelocityReadout", s.velocityReadout ? 1 : 0);
            _shader.SetFloat("_BalanceLambda", s.balanceLambda);
            SetInt("_ScratchStride", ScratchStride); SetInt("_ScratchFixed", ScratchFixed); SetInt("_HeadScratch", HeadScratch);
            SetInt("_StatsStride", _statsStride);
            _shader.SetFloat("_LearningRate", s.learningRate);
            _shader.SetFloat("_Beta1", s.momentum);
            _shader.SetFloat("_Beta2", 0.999f);
            SetInt("_UpdateRule", s.updateRule == UpdateRule.Adam ? 1 : 0);
            _shader.SetFloat("_ValueRate", s.valueSpaceRate);
            _shader.SetFloat("_RuleRate", s.wfcRuleLrScale);
            _shader.SetFloat("_ResetAmount", s.resetAmount);

            SetInt("_Wfc", _wfc ? 1 : 0); SetInt("_T", _t); SetInt("_F", _f); SetInt("_PlanBias", s.PlanBiasCount > 0 ? 1 : 0);
            SetInt("_Coexist", s.wfcAxis != WfcAxis.Time ? 1 : 0); SetInt("_Follow", s.wfcAxis != WfcAxis.Heads ? 1 : 0);
            SetInt("_Handoff", s.wfcAxis == WfcAxis.Grid ? 1 : 0);
            SetInt("_Passes", s.wfcPasses); SetInt("_Budget", s.wfcCollapseBudget); SetInt("_PlanTickSteps", Math.Max(1, s.planTickSteps));

            SetInt("_Memory", _memory ? 1 : 0); SetInt("_Slots", _memory ? s.memorySlots : 0); SetInt("_MemStride", s.MemoryStride);
            SetInt("_MemShared", s.memoryScope == MemoryScope.Shared ? 1 : 0);
            SetInt("_MemInputAt", s.headCount * s.TranslatorHeadBlock + s.inputCount);
            _shader.SetFloat("_MergeThreshold", s.mergeThreshold);
            _shader.SetFloat("_HalfLife", s.memoryHalfLife);

            SetInt("_Identity", _identity ? 1 : 0); SetInt("_K", _k); SetInt("_IdentityOffset", _layout.IdentityOffset);
            _shader.SetFloat("_IdentityAlpha", s.identityAlpha);
            _shader.SetFloat("_IdentityNoise", s.identityNoise);
            _shader.SetFloat("_IdentityRate", s.identityRate);
            SetInt("_Imitation", _imitation ? 1 : 0); SetInt("_ImitSlotCount", _imitation ? s.imitationSlots : 0);
            SetInt("_ImitStride", s.ImitationStride); SetInt("_KeyInputs", s.ImitationKeyInputs); SetInt("_KeyFeedback", s.imitationKeyFeedback ? 1 : 0);
            SetInt("_CreditSteps", _creditSteps); SetInt("_MergeBits", s.imitationMergeBits);
            _shader.SetFloat("_ImitWeight", s.imitationWeight);
            _shader.SetFloat("_ImitRate", s.imitationRate);
            _shader.SetFloat("_ImitFalloff", s.imitationFalloff);
            _shader.SetFloat("_CreditDecay", s.creditDecay);
            SetInt("_FamExplore", s.familiarityExploration ? 1 : 0);
            _shader.SetFloat("_FamScale", s.familiarExploreScale);
            SetInt("_GpuSignal", s.trainingMode == TrainingMode.Supervised ? 1 : 0);

            bool grid = s.UsesSpatialGrid;
            SetInt("_Grid", grid ? 1 : 0);
            if (grid)
            {
                var g = new SpatialGrid(s, new float[s.headCount * s.pointsPerSpace * s.spaceDimensions]);
                SetInt("_GridSize", g.Size); SetInt("_GridCells", g.CellCount); SetInt("_GridAround", g.CellList.Length);
                _shader.SetFloat("_GridCellSize", g.CellSize);
            }
        }

        private void SetInt(string name, int value) => _shader.SetInt(name, value);
        private void SetUInt(string name, uint value) => _shader.SetInt(name, unchecked((int)value));

        // Big dispatches spill into a second row of groups (the kernels flatten id.x + id.y × _DispatchWidth).
        private void Dispatch(int kernel, int threads)
        {
            if (threads <= 0)
                return;
            int groups = (threads + Threads - 1) / Threads;
            int rows = (groups + 65534) / 65535;
            int columns = (groups + rows - 1) / rows;
            SetInt("_DispatchWidth", columns * Threads);
            _shader.Dispatch(kernel, columns, rows, 1);
        }

        #endregion

        #region Stepping

        public void Step(ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights, float deltaTime)
        {
            int i = _s.inputCount, n = _s.outputCount;
            if (inputs.Length < _instances * i)
                throw new ArgumentException($"Expected {_instances * i} inputs.");

            float dt = deltaTime > 0f ? deltaTime : _s.TickDt;
            ProcessSignals(dt);
            FlushErrors();
            ProcessMemory(dt);
            bool supervised = Training && _s.trainingMode == TrainingMode.Supervised;
            bool exploring = Training && _s.trainingMode != TrainingMode.Supervised;

            if (_windowOpen && _windowStep >= _windowSteps)
                CloseWindow();
            if (Training && !_windowOpen)
                OpenWindow();
            SurpriseReset();
            PlanSurpriseReset();

            inputs.Slice(0, _instances * i).CopyTo(_inputUpload);
            _inputs.SetData(_inputUpload, 0, 0, _instances * i);
            bool haveTargets = supervised && targets.Length >= _instances * n;
            if (haveTargets)
            {
                targets.Slice(0, _instances * n).CopyTo(_targetUpload);
                _targets.SetData(_targetUpload, 0, 0, _instances * n);
                UploadWeights(weights, 0, _instances);
            }

            SetInt("_RowStart", 0);
            SetInt("_RowCount", _instances);
            SetInt("_SlotCount", supervised ? _copies : 1);
            SetInt("_CopyMode", supervised ? 0 : exploring && _s.trainingMode == TrainingMode.Population ? 1 : 2);
            SetInt("_FixedCopy", exploring ? _sequentialCopy : 0);
            SetUInt("_Window", _noiseWindow);
            SetUInt("_AssignWindow", _window);
            SetFrame(dt);
            SetInt("_HaveTargets", haveTargets ? 1 : 0);
            SetInt("_WeightPractice", 0);
            SetInt("_Training", Training ? 1 : 0);
            Forward(_instances * (supervised ? _copies : 1));

            if (haveTargets)
                _windowHasError = true;
            if (_windowOpen)
                _windowStep++;
            _steps++;
            ReadBack();
        }

        private void SetFrame(float dt)
        {
            _shader.SetFloat("_Sigma", _sigma);
            _shader.SetFloat("_Alpha", _alpha);
            _shader.SetFloat("_Ratio", ErrorRatio());
            _shader.SetFloat("_Dt", dt);
            _shader.SetFloat("_Move", dt / _s.TickDt);
        }

        // The step's kernels, in the blueprint's order, for _RowCount rows × _SlotCount slots.
        private void Forward(int states)
        {
            Dispatch(_kMasterForward, states);
            if (_memory)
                Dispatch(_kMemoryRead, states);
            if (_s.UsesFamiliarity)
                Dispatch(_kImitationRead, states);
            Dispatch(_kSlaveMove, states * _s.headCount);
            Dispatch(_kSampleSpace, states * _s.headCount);
            if (_wfc)
                Dispatch(_kWfcPlan, states);
            Dispatch(_kTranslatorForward, states);
            Dispatch(_kAccumulateError, states);
        }

        private void UploadWeights(ReadOnlySpan<float> weights, int row, int rows)
        {
            int n = _s.outputCount;
            int count = rows * n;
            if (weights.Length >= count)
                weights.Slice(0, count).CopyTo(new Span<float>(_weightUpload, 0, count));
            else
                Array.Fill(_weightUpload, 1f, 0, count);
            _targetWeights.SetData(_weightUpload, 0, row * n, count);
        }

        public void ReportError(int instance, float error)
        {
            if (instance < 0 || instance >= _instances || float.IsNaN(error))
                return;
            // MnnCpu.ReportError: with imitation, the step's report is blended on the GPU at the start of the next step.
            if (!_imitation || _s.trainingMode == TrainingMode.Supervised)
                _rowError[instance] += error;
            if (_s.UsesSignals && _s.trainingMode != TrainingMode.Supervised)
            {
                _sigError[instance] += error;
                _sigHas[instance] = true;
            }
            _rowReported[instance] = true;
            _pendingError += error;
            _pendingCount++;
        }

        private void ReadBack()
        {
            if (Synchronous)
            {
                _outputs.GetData(_latestOutputs);
                _haveOutputs = true;
                _stepStats.GetData(_statsScratch);
                AbsorbStats(_statsScratch);
                return;
            }
            AsyncGPUReadback.Request(_outputs, request =>
            {
                if (request.hasError || _disposed) return;
                request.GetData<float>().CopyTo(_latestOutputs);
                _haveOutputs = true;
            });
            AsyncGPUReadback.Request(_stepStats, request =>
            {
                if (request.hasError || _disposed) return;
                request.GetData<float>().CopyTo(_statsScratch);
                AbsorbStats(_statsScratch);
            });
        }

        // The real network's error, head contributions and recall confidence for the step (what MnnCpu tracks inline).
        private void AbsorbStats(float[] stats)
        {
            int h = _s.headCount;
            for (int row = 0; row < _instances; row++)
            {
                float error = stats[row * _statsStride];
                if (error >= 0f) { _pendingError += error; _pendingCount++; }
            }
            const float rate = 0.01f;
            for (int head = 0; head < h; head++)
            {
                float usage = 0f;
                for (int row = 0; row < _instances; row++)
                    usage += stats[row * _statsStride + 1 + head];
                _usage[head] += (usage / _instances - _usage[head]) * rate;
            }
            _memoryConfidence = stats[1 + h];
            _confidenceRing[(int)((_steps + 255) % 256)] = _memoryConfidence;
            if (_s.UsesFamiliarity)
            {
                _imitationConfidence = stats[2 + h];
                float sum = 0f;
                for (int row = 0; row < _instances; row++) sum += stats[row * _statsStride + 3 + h];
                _familiarity = sum / _instances;
            }
        }

        public bool TryGetOutputs(float[] destination)
        {
            if (!_haveOutputs)
                return false;
            Array.Copy(_latestOutputs, destination, Math.Min(destination.Length, _latestOutputs.Length));
            return true;
        }

        public void GetOutputsImmediate(float[] destination)
        {
            _outputs.GetData(_latestOutputs);
            _haveOutputs = true;
            Array.Copy(_latestOutputs, destination, Math.Min(destination.Length, _latestOutputs.Length));
        }

        public GraphicsBuffer OutputBuffer => _outputs;
        public GraphicsBuffer HeadPositions => _pos;
        public int HeadStride => _copies * _s.headCount * _s.spaceDimensions;
        public ReadOnlySpan<float> HeadUsage => _usage;
        public ReadOnlySpan<float> ErrorRing => _errorRing;
        public int ErrorRingIndex => _ringIndex;
        public ReadOnlySpan<float> ConfidenceRing => _confidenceRing;

        private float ErrorRatio() => _baseline <= 1e-8f ? 0f : Mathf.Clamp(_trace / _baseline, 0f, 4f) - 1f;

        #endregion

        #region Memory

        public void Remember(int instance, ReadOnlySpan<float> value, float strength)
        {
            int n = _s.outputCount;
            if (!_memory || instance < 0 || instance >= _instances || value.Length < n || float.IsNaN(strength))
                return;
            int at = instance * (n + 3);
            value.Slice(0, n).CopyTo(new Span<float>(_queueUpload, at, n));
            _queueUpload[at + n] = Mathf.Clamp(strength, -MnnCpu.MemoryMaxStrength, MnnCpu.MemoryMaxStrength);
            _queueUpload[at + n + 1] = 1f;
            _anyQueued = true;
        }

        public void Reinforce(int instance, float amount)
        {
            int n = _s.outputCount;
            if (!_memory || instance < 0 || instance >= _instances || float.IsNaN(amount))
                return;
            _queueUpload[instance * (n + 3) + n + 2] += amount;
            _anyQueued = true;
        }

        // MnnCpu.ProcessMemory: queued writes in instance order, fading, recall marks.
        private void ProcessMemory(float dt)
        {
            if (!_memory)
                return;
            int n = _s.outputCount;
            if (_anyQueued)
            {
                for (int row = 0; row < _instances; row++)
                    if (_queueUpload[row * (n + 3) + n + 1] > 0.5f) _memoryWrites++;
                _memQueue.SetData(_queueUpload);
                _shader.Dispatch(_kMemoryQueue, 1, 1, 1);
                Array.Clear(_queueUpload, 0, _queueUpload.Length);
                _anyQueued = false;
            }
            _shader.SetFloat("_Fade", MathF.Exp(-dt / _s.memoryHalfLife * 0.6931472f));
            _shader.SetFloat("_MemDt", dt);
            Dispatch(_kMemoryFade, _banks * _s.memorySlots);
            Dispatch(_kMemoryRecall, _instances);
        }

        public void ClearMemory()
        {
            if (_memory) _memSlots.SetData(new float[_memSlots.count]);
        }

        public float[] GetMemoryBank()
        {
            if (!_memory) return Array.Empty<float>();
            var data = new float[_memSlots.count];
            _memSlots.GetData(data);
            return data;
        }

        public void SetMemoryBank(ReadOnlySpan<float> bank)
        {
            if (_memory && bank.Length == _memSlots.count)
                _memSlots.SetData(bank.ToArray());
        }

        #endregion

        #region Identity masks and imitation

        // MnnCpu.ProcessSignals: last step's signal (computed on the GPU when supervised, reported otherwise).
        private void ProcessSignals(float dt)
        {
            if (!_s.UsesSignals)
                return;
            bool reward = _s.trainingMode != TrainingMode.Supervised;
            if (reward)
            {
                for (int row = 0; row < _rows; row++)
                {
                    _signalUpload[row * 2] = (float)_sigError[row];
                    _signalUpload[row * 2 + 1] = _sigHas[row] ? 1f : 0f;
                }
                _signal.SetData(_signalUpload);
            }
            if (Training)
            {
                if (_imitation && reward)
                    Dispatch(_kImitationBlend, _instances);
                if (!(_baseline == 0f && _trace == 0f))
                {
                    _shader.SetFloat("_Baseline", _baseline);
                    _shader.SetFloat("_Spread", _spread);
                    if (_identity)
                        Dispatch(_kIdentityUpdate, _instances * _s.headCount);
                    if (_imitation)
                    {
                        _shader.SetFloat("_ImitFade", MathF.Exp(-dt / _s.imitationHalfLife * 0.6931472f));
                        _shader.Dispatch(_kImitationWrite, 1, 1, 1);
                    }
                }
            }
            Array.Clear(_sigHas, 0, _sigHas.Length);
            Array.Clear(_sigError, 0, _sigError.Length);
        }

        private void ResetIdentityMasks()
        {
            int per = _s.headCount * _k;
            var masks = new float[Math.Max(1, _instances * per)];
            for (int row = 0; row < _instances; row++)
                for (int j = 0; j < per; j++)
                    masks[row * per + j] = MnnCpu.InitialIdentity(_s.seed, row, j);
            _idSoft.SetData(masks);
        }

        public float[] GetIdentityMasks()
        {
            if (!_identity) return Array.Empty<float>();
            var data = new float[_idSoft.count];
            _idSoft.GetData(data);
            return data;
        }

        public float[] GetImitationBank()
        {
            if (!_imitation) return Array.Empty<float>();
            var data = new float[_imitSlots.count];
            _imitSlots.GetData(data);
            return data;
        }

        public void SetIdentityMasks(ReadOnlySpan<float> masks)
        {
            int per = _s.headCount * _k;
            if (per == 0 || masks.Length < per || masks.Length % per != 0)
                return;
            var data = new float[_instances * per];
            int saved = masks.Length / per;
            for (int row = 0; row < _instances; row++)
                masks.Slice((row % saved) * per, per).CopyTo(new Span<float>(data, row * per, per));
            _idSoft.SetData(data);
        }

        #endregion

        #region Training

        private void OpenWindow()
        {
            _windowOpen = true;
            _windowStep = 0;
            _windowSteps = _s.evalWindow;
            _windowHasError = false;
            Array.Clear(_rowError, 0, _rowError.Length);
            Array.Clear(_rowReported, 0, _rowReported.Length);

            if (_s.trainingMode != TrainingMode.Sequential || _sequentialCopy == 0)
            {
                _noiseWindow = _window;
                _sigma = _s.perturbSigma * (_s.alphaScalesSigma ? 1f + _alpha : 1f) * ExploreFactor(_familiarity);
            }
            if (_s.trainingMode != TrainingMode.Supervised)
                Dispatch(_kClearRows, _rows);
            if (_s.trainingMode == TrainingMode.Sequential && _sequentialCopy == 0)
                Dispatch(_kClearCopyErrors, _copies);

            if (_s.trainingMode == TrainingMode.Supervised)
            {
                SetInt("_RowStart", 0);
                SetInt("_RowCount", _instances);
                // Clears the copies' error sums (a practice run can leave a window's behind), then forks.
                _shader.SetFloat("_ErrorScale", 0f);
                Dispatch(_kReduceSupervised, _copies);
                if (_copies > 1)
                    Dispatch(_kForkState, _instances * (_copies - 1));
            }
        }

        private void CloseWindow()
        {
            _windowOpen = false;
            bool update = false;
            SetUInt("_Window", _noiseWindow);
            SetUInt("_AssignWindow", _window);
            _shader.SetFloat("_Sigma", _sigma);
            SetInt("_WindowSteps", _windowSteps);

            switch (_s.trainingMode)
            {
                case TrainingMode.Supervised:
                    SetInt("_RowStart", 0);
                    SetInt("_RowCount", _instances);
                    _shader.SetFloat("_ErrorScale", 1f / (_instances * _windowSteps));
                    Dispatch(_kReduceSupervised, _copies);
                    update = _windowHasError;
                    break;

                case TrainingMode.Population:
                    UploadReports();
                    Dispatch(_kClearCopyErrors, _copies);
                    SetInt("_CopyMode", 1);
                    Dispatch(_kReduceReported, _copies);
                    update = true;
                    break;

                case TrainingMode.Sequential:
                    UploadReports();
                    SetInt("_CopyMode", 2);
                    SetInt("_FixedCopy", _sequentialCopy);
                    Dispatch(_kReduceReported, _copies);
                    _sequentialCopy++;
                    if (_sequentialCopy >= _copies)
                    {
                        _sequentialCopy = 0;
                        update = true;
                    }
                    break;
            }

            if (update)
                ApplyUpdate();
            if (_s.trainingMode != TrainingMode.Sequential || _sequentialCopy == 0)
                _window++;
            AfterUpdate();
        }

        private void UploadReports()
        {
            for (int row = 0; row < _rows; row++)
            {
                _reportUpload[row * 2] = (float)_rowError[row];
                _reportUpload[row * 2 + 1] = _rowReported[row] ? 1f : 0f;
            }
            _rowReports.SetData(_reportUpload);
        }

        private void ApplyUpdate()
        {
            _adamStep++;
            _shader.SetFloat("_Correction1", 1f - Mathf.Pow(_s.momentum, _adamStep));
            _shader.SetFloat("_Correction2", 1f - Mathf.Pow(0.999f, _adamStep));
            Dispatch(_kApplyUpdate, _layout.Count);
            _updates++;
        }

        // Keep the best, roll back from a cliff (once per cliff), revive neglected heads: MnnCpu.AfterUpdate.
        private void AfterUpdate()
        {
            int warmup = (int)Math.Min(2000f, 1f / Math.Max(1e-4f, 1f - _s.baselineFalloff));
            if (_s.bestWeightsRollback && _steps > warmup && _baseline > 0f)
            {
                if (_baseline < _bestBaseline)
                {
                    _bestBaseline = _baseline;
                    CopyParameters(0);
                    _rollbackSteps = 0;
                    _rolledBack = false;
                }
                else if (_baseline > _bestBaseline * (1f + _s.rollbackMargin))
                {
                    _rollbackSteps += _windowSteps;
                    if (_rollbackSteps >= _s.rollbackSteps)
                    {
                        _rollbackSteps = 0;
                        if (_rolledBack)
                        {
                            _bestBaseline = _baseline;
                            CopyParameters(0);
                            _rolledBack = false;
                        }
                        else
                        {
                            CopyParameters(1);
                            _adamStep = 0;
                            _rollbacks++;
                            _rolledBack = true;
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

        private void CopyParameters(int source)
        {
            SetInt("_CopySource", source);
            Dispatch(_kCopyParameters, _layout.Count);
        }

        private void ReviveHeads()
        {
            int h = _s.headCount;
            if (_s.reviveSteps <= 0 || h < 2)
                return;
            float total = 0f;
            for (int head = 0; head < h; head++) total += _usage[head];
            if (total <= 1e-8f)
                return;
            float mean = total / h;
            for (int head = 0; head < h; head++)
            {
                if (_usage[head] / total >= _s.reviveThreshold)
                {
                    _lowSteps[head] = 0;
                    continue;
                }
                _lowSteps[head] += _windowSteps;
                if (_lowSteps[head] < _s.reviveSteps)
                    continue;

                _reviveGeneration++;
                SetUInt("_Generation", _reviveGeneration);
                var block = _layout.Slaves[head];
                for (int l = block.FirstLayer; l < block.FirstLayer + block.LayerCount; l++)
                {
                    var layer = _layout.Layers[l];
                    SetInt("_Layer", l);
                    Dispatch(_kReinitializeLayer, layer.In * layer.Out + layer.Out);
                }
                SetInt("_Head", head);
                Dispatch(_kReviveHead, _rows * _copies);
                _usage[head] = mean;
                _lowSteps[head] = 0;
                _revivals++;
            }
        }

        private void FlushErrors()
        {
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
                    _spread += (MathF.Abs(error - _baseline) - _spread) * (1f - _s.baselineFalloff);
                    _trace += (error - _trace) * (1f - _s.errorFalloff);
                    _baseline += (error - _baseline) * (1f - _s.baselineFalloff);
                }
                _errorRing[_ringIndex] = error;
                _ringIndex = (_ringIndex + 1) % _errorRing.Length;
                _pendingError = 0;
                _pendingCount = 0;
            }
            _alpha = _baseline > 1e-8f ? Mathf.Clamp(_s.sineGain * (_trace / _baseline - 1f), 0f, _s.sineAlphaMax) * ExploreFactor(_familiarity) : 0f;
        }

        private float ExploreFactor(float familiarity)
            => _s.familiarityExploration ? 1f + (_s.familiarExploreScale - 1f) * familiarity : 1f;

        private void SurpriseReset()
        {
            if (!_s.surpriseReset || _baseline <= 1e-8f)
                return;
            float ratio = _trace / _baseline;
            if (ratio < 1f)
            {
                _surprised = false;
                return;
            }
            if (_surprised || ratio <= _s.resetThreshold)
                return;
            _surprised = true;
            Dispatch(_kSurpriseReset, _instances * _copies);
        }

        private void PlanSurpriseReset()
        {
            if (!_wfc || _baseline <= 1e-8f || _f < 2)
                return;
            float ratio = _trace / _baseline;
            if (ratio < 1f)
            {
                _planSurprised = false;
                return;
            }
            if (_planSurprised || ratio <= _s.wfcResetThreshold)
                return;
            _planSurprised = true;
            _planResets++;
            LastPlanReset = _steps;
            Dispatch(_kPlanReset, _instances * _copies);
        }

        public bool Practice(MnnState start, ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights, int steps)
        {
            int i = _s.inputCount, n = _s.outputCount;
            if (!Training || steps <= 0 || inputs.Length < steps * i || targets.Length < steps * n)
                return false;

            double weightTotal = 0;
            for (int k = 0; k < steps * n; k++)
                weightTotal += weights.Length >= steps * n ? weights[k] : 1f;
            if (weightTotal <= 1e-8)
                return false;

            FlushErrors();
            uint noise = 0x80000000u | _practiceCount++;
            float sigma = _s.perturbSigma * (_s.alphaScalesSigma ? 1f + _alpha : 1f) * ExploreFactor(_familiarity);

            RestoreRow(_practiceRow, start);
            SetInt("_RowStart", _practiceRow);
            SetInt("_RowCount", 1);
            SetInt("_SlotCount", _copies);
            SetInt("_CopyMode", 0);
            SetUInt("_Window", noise);
            _shader.SetFloat("_Sigma", sigma);
            _shader.SetFloat("_Alpha", _alpha);
            _shader.SetFloat("_Ratio", ErrorRatio());
            _shader.SetFloat("_Dt", _s.TickDt);
            _shader.SetFloat("_Move", 1f);
            SetInt("_HaveTargets", 1);
            SetInt("_WeightPractice", 1);
            SetInt("_Training", 1);
            for (int t = 0; t < steps; t++)
            {
                inputs.Slice(t * i, i).CopyTo(new Span<float>(_inputUpload, _practiceRow * i, i));
                _inputs.SetData(_inputUpload, _practiceRow * i, _practiceRow * i, i);
                targets.Slice(t * n, n).CopyTo(new Span<float>(_targetUpload, _practiceRow * n, n));
                _targets.SetData(_targetUpload, _practiceRow * n, _practiceRow * n, n);
                var stepWeights = weights.Length >= (t + 1) * n ? weights.Slice(t * n, n) : default;
                if (stepWeights.Length == n) stepWeights.CopyTo(new Span<float>(_weightUpload, _practiceRow * n, n));
                else Array.Fill(_weightUpload, 1f, _practiceRow * n, n);
                _targetWeights.SetData(_weightUpload, _practiceRow * n, _practiceRow * n, n);
                Forward(_copies);
            }

            _shader.SetFloat("_ErrorScale", 1f / steps);
            Dispatch(_kReduceSupervised, _copies);
            ApplyUpdate();

            _windowOpen = false;
            if (_s.trainingMode == TrainingMode.Sequential) _sequentialCopy = 0;
            return true;
        }

        #endregion

        #region State and weights

        public void ResetState(int instance)
        {
            SetInt("_RowStart", instance < 0 ? 0 : instance);
            SetInt("_RowCount", instance < 0 ? _rows : 1);
            Dispatch(_kResetRows, (instance < 0 ? _rows : 1) * _copies);
        }

        public void CaptureState(int instance, Action<MnnState> done)
        {
            SetInt("_RowStart", instance);
            _shader.Dispatch(_kPackState, 1, 1, 1);
            if (Synchronous)
            {
                done(PeekStaging());
                return;
            }
            AsyncGPUReadback.Request(_staging, request =>
            {
                if (request.hasError || _disposed) return;
                var state = new MnnState(_s);
                state.Unpack(request.GetData<float>().ToArray());
                done(state);
            });
        }

        public MnnState PeekStateImmediate(int instance)
        {
            SetInt("_RowStart", instance);
            _shader.Dispatch(_kPackState, 1, 1, 1);
            return PeekStaging();
        }

        private MnnState PeekStaging()
        {
            var data = new float[_stateFloats];
            _staging.GetData(data);
            var state = new MnnState(_s);
            state.Unpack(data);
            return state;
        }

        public void RestoreState(int instance, MnnState state) => RestoreRow(instance, state);

        private void RestoreRow(int row, MnnState state)
        {
            var data = new float[_stateFloats];
            state.Pack(data);
            _staging.SetData(data);
            SetInt("_RowStart", row);
            Dispatch(_kUnpackState, _copies);
        }

        public float[] GetParameters()
        {
            var data = new float[_layout.Count];
            _params.GetData(data);
            return data;
        }

        public void SetParameters(ReadOnlySpan<float> parameters)
        {
            if (parameters.Length != _layout.Count)
                throw new ArgumentException($"Expected {_layout.Count} parameters, got {parameters.Length}. The brains have different layouts.");
            _params.SetData(parameters.ToArray());
            CopyParameters(2);
            CopyParameters(0);
            _adamStep = 0;
            _bestBaseline = float.MaxValue;
            _rolledBack = false;
            _windowOpen = false;
        }

        #endregion

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var buffer in new[] { _params, _moment, _variance, _best, _points, _values, _timescale, _rest, _clocks, _pointTypes,
                _gridStart, _gridOrder, _neighbors, _layerTable, _pos, _vel, _attr, _target, _gate, _sample, _trail, _feedback, _prevInput,
                _stepCount, _planSpread, _planType, _planAge, _typePartials, _memKey, _memAttn, _memSlots, _memQueue, _inputs, _targets,
                _targetWeights, _outputs, _scratch, _slotError, _rowPenalty, _stepStats, _copyError, _copyCount, _rowReports, _staging,
                _idSoft, _idHard, _imitProj, _imitSlots, _imitKeys, _imitStateKey, _imitRing, _imitRingKey, _signal, _rowImit, _rowBlend })
                buffer?.Release();
            if (_shader != null)
                UnityEngine.Object.DestroyImmediate(_shader);
        }
    }
}
