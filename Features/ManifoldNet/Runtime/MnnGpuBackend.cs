using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The network on the GPU: every instance and every nudged copy steps in one dispatch, and training updates every
    /// weight in parallel. It mirrors <see cref="MnnCpu"/> step for step; the slow-moving bookkeeping (error trace,
    /// head usage, revival, rollback) stays on the CPU, fed by a small asynchronous readback each step.
    /// </summary>
    internal sealed class MnnGpuBackend : IMnnBackend
    {
        private const int Threads = 64;

        private readonly MnnSettings _s;
        private readonly WeightLayout _layout;
        private readonly ComputeShader _shader;
        private readonly int _instances, _rows, _practiceRow, _copies, _stateFloats;

        private readonly int _kStep, _kFork, _kReduceSupervised, _kReduceReported, _kClearCopyErrors, _kUpdate,
            _kCopyParameters, _kReinitializeLayer, _kReviveHead, _kResetRows, _kSurpriseReset, _kPackState, _kUnpackState;

        private GraphicsBuffer _params, _moment, _variance, _best;
        private GraphicsBuffer _points, _values, _inertia, _speed, _rest, _clocks, _layerTable;
        private GraphicsBuffer _pos, _vel, _trail, _feedback, _prevInput, _stepCount;
        private GraphicsBuffer _inputs, _targets, _targetWeights, _outputs, _scratch;
        private GraphicsBuffer _slotError, _rowPenalty, _stepStats, _copyError, _copyCount, _rowReports, _staging;

        // Upload scratch, preallocated so a step allocates nothing.
        private readonly float[] _inputUpload, _targetUpload, _weightUpload, _reportUpload;
        private readonly float[] _latestOutputs, _statsScratch;
        private bool _haveOutputs;

        // Bookkeeping, as in MnnCpu.
        private readonly double[] _rowError;
        private readonly bool[] _rowReported;
        private int _windowStep, _windowSteps, _sequentialCopy, _adamStep;
        private uint _window, _practiceCount, _noiseWindow;
        private float _sigma;
        private bool _windowOpen, _windowHasError;
        private float _trace, _baseline, _alpha;
        private double _pendingError;
        private int _pendingCount;
        private readonly float[] _usage, _errorRing = new float[256];
        private readonly int[] _lowSteps;
        private int _ringIndex;
        private uint _reviveGeneration;
        private float _bestBaseline = float.MaxValue;
        private int _rollbackSteps;
        private bool _surprised;
        private uint _steps;
        private int _updates, _revivals, _rollbacks;

        /// <summary>Read stats and outputs back right away instead of a frame later (tests, editor tools).</summary>
        public bool Synchronous;

        public bool Training { get; set; } = true;
        public int Updates => _updates;

        public MnnStats Stats => new()
        {
            ErrorTrace = _trace, ErrorBaseline = _baseline, Alpha = _alpha,
            Updates = _updates, Revivals = _revivals, Rollbacks = _rollbacks, Steps = _steps,
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
            _stateFloats = 2 * _s.headCount * _s.spaceDimensions + _s.headCount * _s.outputCount + _s.outputCount + _s.inputCount + 1;

            _kStep = _shader.FindKernel("Step");
            _kFork = _shader.FindKernel("Fork");
            _kReduceSupervised = _shader.FindKernel("ReduceSupervised");
            _kReduceReported = _shader.FindKernel("ReduceReported");
            _kClearCopyErrors = _shader.FindKernel("ClearCopyErrors");
            _kUpdate = _shader.FindKernel("Update");
            _kCopyParameters = _shader.FindKernel("CopyParameters");
            _kReinitializeLayer = _shader.FindKernel("ReinitializeLayer");
            _kReviveHead = _shader.FindKernel("ReviveHead");
            _kResetRows = _shader.FindKernel("ResetRows");
            _kSurpriseReset = _shader.FindKernel("SurpriseReset");
            _kPackState = _shader.FindKernel("PackState");
            _kUnpackState = _shader.FindKernel("UnpackState");

            _inputUpload = new float[_rows * _s.inputCount];
            _targetUpload = new float[_rows * _s.outputCount];
            _weightUpload = new float[_rows * _s.outputCount];
            _reportUpload = new float[_rows * 2];
            _latestOutputs = new float[_instances * _s.outputCount];
            _statsScratch = new float[_rows * (1 + _s.headCount)];
            _rowError = new double[_rows];
            _rowReported = new bool[_rows];
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

            // Weights and the value spaces come from the CPU reference, so both start identical.
            var reference = new MnnCpu(_s, 1);
            _params = Buffer(_layout.Count); _params.SetData(reference.Parameters);
            _moment = Buffer(_layout.Count); _moment.SetData(new float[_layout.Count]);
            _variance = Buffer(_layout.Count); _variance.SetData(new float[_layout.Count]);
            _best = Buffer(_layout.Count); _best.SetData(reference.Parameters);

            _points = Buffer(h * p * d); _points.SetData(reference.Points.ToArray());
            _values = Buffer(h * p * n); _values.SetData(reference.FixedValues.ToArray());
            _inertia = Buffer(h); _inertia.SetData(reference.HeadInertia.ToArray());
            _speed = Buffer(h); _speed.SetData(reference.HeadSpeed.ToArray());
            _rest = Buffer(h * d); _rest.SetData(reference.HeadRest.ToArray());
            var clocks = _s.ClockCount > 0 ? _s.clockFrequencies : new[] { 0f };
            _clocks = Buffer(clocks.Length); _clocks.SetData(clocks);

            var table = new Vector4Int[_layout.Layers.Length];
            for (int l = 0; l < table.Length; l++)
            {
                var layer = _layout.Layers[l];
                table[l] = new Vector4Int(layer.In, layer.Out, layer.Weights, layer.Biases);
            }
            _layerTable = Buffer(table.Length, 16); _layerTable.SetData(table);

            _pos = Buffer(states * h * d);
            _vel = Buffer(states * h * d);
            _trail = Buffer(states * h * n);
            _feedback = Buffer(states * n);
            _prevInput = Buffer(states * i);
            _stepCount = Buffer(states, sizeof(uint));

            _inputs = Buffer(_rows * i);
            _targets = Buffer(_rows * n);
            _targetWeights = Buffer(_rows * n);
            _outputs = Buffer(_instances * n);
            _scratch = Buffer(states * ScratchStride);

            _slotError = Buffer(states); _slotError.SetData(new float[states]);
            _rowPenalty = Buffer(_rows); _rowPenalty.SetData(new float[_rows]);
            _stepStats = Buffer(_rows * (1 + h));
            _copyError = Buffer(_copies); _copyError.SetData(new float[_copies]);
            _copyCount = Buffer(_copies); _copyCount.SetData(new float[_copies]);
            _rowReports = Buffer(_rows * 2);
            _staging = Buffer(_stateFloats);
        }

        private int ScratchStride => _s.MasterInputs + _s.headCount * _s.maskWidth + _s.TranslatorInputs + 2 * _s.hiddenWidth
            + Math.Max(_s.spaceDimensions, _s.outputCount);

        private struct Vector4Int
        {
            public int X, Y, Z, W;
            public Vector4Int(int x, int y, int z, int w) { X = x; Y = y; Z = z; W = w; }
        }

        private void BindAll()
        {
            int[] kernels = { _kStep, _kFork, _kReduceSupervised, _kReduceReported, _kClearCopyErrors, _kUpdate, _kCopyParameters,
                _kReinitializeLayer, _kReviveHead, _kResetRows, _kSurpriseReset, _kPackState, _kUnpackState };
            foreach (int k in kernels)
            {
                _shader.SetBuffer(k, "_Params", _params);
                _shader.SetBuffer(k, "_Moment", _moment);
                _shader.SetBuffer(k, "_Variance", _variance);
                _shader.SetBuffer(k, "_Best", _best);
                _shader.SetBuffer(k, "_Points", _points);
                _shader.SetBuffer(k, "_Values", _values);
                _shader.SetBuffer(k, "_Inertia", _inertia);
                _shader.SetBuffer(k, "_Speed", _speed);
                _shader.SetBuffer(k, "_Rest", _rest);
                _shader.SetBuffer(k, "_ClockFrequencies", _clocks);
                _shader.SetBuffer(k, "_LayerTable", _layerTable);
                _shader.SetBuffer(k, "_Pos", _pos);
                _shader.SetBuffer(k, "_Vel", _vel);
                _shader.SetBuffer(k, "_Trail", _trail);
                _shader.SetBuffer(k, "_Feedback", _feedback);
                _shader.SetBuffer(k, "_PrevInput", _prevInput);
                _shader.SetBuffer(k, "_StepCount", _stepCount);
                _shader.SetBuffer(k, "_Inputs", _inputs);
                _shader.SetBuffer(k, "_Targets", _targets);
                _shader.SetBuffer(k, "_TargetWeights", _targetWeights);
                _shader.SetBuffer(k, "_Outputs", _outputs);
                _shader.SetBuffer(k, "_Scratch", _scratch);
                _shader.SetBuffer(k, "_SlotError", _slotError);
                _shader.SetBuffer(k, "_RowPenalty", _rowPenalty);
                _shader.SetBuffer(k, "_StepStats", _stepStats);
                _shader.SetBuffer(k, "_CopyError", _copyError);
                _shader.SetBuffer(k, "_CopyCount", _copyCount);
                _shader.SetBuffer(k, "_RowReports", _rowReports);
                _shader.SetBuffer(k, "_Staging", _staging);
            }

            var s = _s;
            SetInt("_I", s.inputCount); SetInt("_N", s.outputCount); SetInt("_H", s.headCount); SetInt("_M", s.maskWidth);
            SetInt("_D", s.spaceDimensions); SetInt("_P", s.pointsPerSpace);
            SetInt("_HiddenWidth", s.hiddenWidth); SetInt("_HiddenLayers", s.hiddenLayers);
            SetInt("_MasterInputs", s.MasterInputs); SetInt("_TranslatorInputs", s.TranslatorInputs); SetInt("_HeadBlock", s.TranslatorHeadBlock);
            SetInt("_Copies", _copies); SetInt("_Rows", _rows); SetInt("_Instances", _instances);
            SetInt("_Seed", unchecked((int)s.seed));
            SetInt("_MlpCount", _layout.MlpCount); SetInt("_ValuesOffset", _layout.ValuesOffset); SetInt("_ParameterCount", _layout.Count);
            SetInt("_TrainableValues", s.trainableValueSpaces ? 1 : 0); SetInt("_Antithetic", s.perturbMode == PerturbMode.Antithetic ? 1 : 0);
            SetInt("_Pairs", s.perturbPairs);
            _shader.SetFloat("_ValueSigmaScale", s.valueSpaceRate);
            _shader.SetFloat("_Omega", s.sineOmega);
            _shader.SetFloat("_Pull", s.pull);
            _shader.SetFloat("_TrailRate", s.trailRate);
            _shader.SetFloat("_Dispersion", s.dispersion);
            SetInt("_Nearest", s.samplingMode == SamplingMode.Nearest ? 1 : 0);
            SetInt("_ClockCount", s.ClockCount); SetInt("_ChangeInputs", s.changeInputs ? 1 : 0); SetInt("_VelocityReadout", s.velocityReadout ? 1 : 0);
            _shader.SetFloat("_BalanceLambda", s.balanceLambda);
            SetInt("_ScratchStride", ScratchStride);
            _shader.SetFloat("_LearningRate", s.learningRate);
            _shader.SetFloat("_Beta1", s.momentum);
            _shader.SetFloat("_Beta2", 0.999f);
            _shader.SetFloat("_ValueRate", s.valueSpaceRate);
            _shader.SetFloat("_ResetAmount", s.resetAmount);
        }

        private void SetInt(string name, int value) => _shader.SetInt(name, value);
        private void SetUInt(string name, uint value) => _shader.SetInt(name, unchecked((int)value));

        private void Dispatch(int kernel, int threads)
        {
            if (threads > 0)
                _shader.Dispatch(kernel, (threads + Threads - 1) / Threads, 1, 1);
        }

        #endregion

        #region Stepping

        public void Step(ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights)
        {
            int i = _s.inputCount, n = _s.outputCount;
            if (inputs.Length < _instances * i)
                throw new ArgumentException($"Expected {_instances * i} inputs.");

            FlushErrors();
            bool supervised = Training && _s.trainingMode == TrainingMode.Supervised;
            bool exploring = Training && _s.trainingMode != TrainingMode.Supervised;

            if (_windowOpen && _windowStep >= _windowSteps)
                CloseWindow();
            if (Training && !_windowOpen)
                OpenWindow();
            SurpriseReset();

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
            _shader.SetFloat("_Sigma", _sigma);
            _shader.SetFloat("_Alpha", _alpha);
            _shader.SetFloat("_Ratio", ErrorRatio());
            SetInt("_HaveTargets", haveTargets ? 1 : 0);
            SetInt("_WeightPractice", 0);
            Dispatch(_kStep, _instances * (supervised ? _copies : 1));

            if (haveTargets)
                _windowHasError = true;
            if (_windowOpen)
                _windowStep++;
            _steps++;
            ReadBack();
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
            _rowError[instance] += error;
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
                var data = request.GetData<float>();
                data.CopyTo(_statsScratch);
                AbsorbStats(_statsScratch);
            });
        }

        // The real network's error and head contributions for the step (what MnnCpu tracks inline).
        private void AbsorbStats(float[] stats)
        {
            int h = _s.headCount;
            double usage = 0;
            for (int row = 0; row < _instances; row++)
            {
                float error = stats[row * (1 + h)];
                if (error >= 0f) { _pendingError += error; _pendingCount++; }
            }
            const float rate = 0.01f;
            for (int head = 0; head < h; head++)
            {
                usage = 0;
                for (int row = 0; row < _instances; row++)
                    usage += stats[row * (1 + h) + 1 + head];
                _usage[head] += ((float)(usage / _instances) - _usage[head]) * rate;
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

        private float ErrorRatio() => _baseline <= 1e-8f ? 0f : Mathf.Clamp(_trace / _baseline, 0f, 4f) - 1f;

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
                _sigma = _s.perturbSigma * (_s.alphaScalesSigma ? 1f + _alpha : 1f);
            }
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
                    Dispatch(_kFork, _instances * (_copies - 1));
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
            Dispatch(_kUpdate, _layout.Count);
            _updates++;
        }

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
                }
                else if (_baseline > _bestBaseline * (1f + _s.rollbackMargin))
                {
                    _rollbackSteps += _windowSteps;
                    if (_rollbackSteps >= _s.rollbackSteps)
                    {
                        CopyParameters(1);
                        _adamStep = 0;
                        _rollbackSteps = 0;
                        _rollbacks++;
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
                }
                else
                {
                    _trace += (error - _trace) * (1f - _s.errorFalloff);
                    _baseline += (error - _baseline) * (1f - _s.baselineFalloff);
                }
                _errorRing[_ringIndex] = error;
                _ringIndex = (_ringIndex + 1) % _errorRing.Length;
                _pendingError = 0;
                _pendingCount = 0;
            }
            _alpha = _baseline > 1e-8f ? Mathf.Clamp(_s.sineGain * (_trace / _baseline - 1f), 0f, _s.sineAlphaMax) : 0f;
        }

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
            float sigma = _s.perturbSigma * (_s.alphaScalesSigma ? 1f + _alpha : 1f);

            RestoreRow(_practiceRow, start);
            SetInt("_RowStart", _practiceRow);
            SetInt("_RowCount", 1);
            SetInt("_SlotCount", _copies);
            SetInt("_CopyMode", 0);
            SetUInt("_Window", noise);
            _shader.SetFloat("_Sigma", sigma);
            _shader.SetFloat("_Alpha", _alpha);
            _shader.SetFloat("_Ratio", ErrorRatio());
            SetInt("_HaveTargets", 1);
            SetInt("_WeightPractice", 1);
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
                Dispatch(_kStep, _copies);
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
                var data = new float[_stateFloats];
                _staging.GetData(data);
                done(Unpack(data));
                return;
            }
            AsyncGPUReadback.Request(_staging, request =>
            {
                if (request.hasError || _disposed) return;
                done(Unpack(request.GetData<float>().ToArray()));
            });
        }

        private MnnState Unpack(float[] data)
        {
            var state = new MnnState(_s);
            int k = 0;
            Array.Copy(data, k, state.Positions, 0, state.Positions.Length); k += state.Positions.Length;
            Array.Copy(data, k, state.Velocities, 0, state.Velocities.Length); k += state.Velocities.Length;
            Array.Copy(data, k, state.Trails, 0, state.Trails.Length); k += state.Trails.Length;
            Array.Copy(data, k, state.Feedback, 0, state.Feedback.Length); k += state.Feedback.Length;
            Array.Copy(data, k, state.PreviousInputs, 0, state.PreviousInputs.Length); k += state.PreviousInputs.Length;
            state.Step = BitConverter.ToUInt32(BitConverter.GetBytes(data[k]), 0);
            return state;
        }

        public void RestoreState(int instance, MnnState state) => RestoreRow(instance, state);

        private void RestoreRow(int row, MnnState state)
        {
            var data = new float[_stateFloats];
            int k = 0;
            Array.Copy(state.Positions, 0, data, k, state.Positions.Length); k += state.Positions.Length;
            Array.Copy(state.Velocities, 0, data, k, state.Velocities.Length); k += state.Velocities.Length;
            Array.Copy(state.Trails, 0, data, k, state.Trails.Length); k += state.Trails.Length;
            Array.Copy(state.Feedback, 0, data, k, state.Feedback.Length); k += state.Feedback.Length;
            Array.Copy(state.PreviousInputs, 0, data, k, state.PreviousInputs.Length); k += state.PreviousInputs.Length;
            data[k] = BitConverter.ToSingle(BitConverter.GetBytes(state.Step), 0);
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
            _windowOpen = false;
        }

        #endregion

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var buffer in new[] { _params, _moment, _variance, _best, _points, _values, _inertia, _speed, _rest, _clocks, _layerTable,
                _pos, _vel, _trail, _feedback, _prevInput, _stepCount, _inputs, _targets, _targetWeights, _outputs, _scratch,
                _slotError, _rowPenalty, _stepStats, _copyError, _copyCount, _rowReports, _staging })
                buffer?.Release();
            if (_shader != null)
                UnityEngine.Object.DestroyImmediate(_shader);
        }
    }
}
