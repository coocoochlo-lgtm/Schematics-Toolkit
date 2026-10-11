using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// A Manifold Neuron Network: a small recurrent network whose memory is where its heads sit in their value spaces.
    /// Create it from a config (and optionally a trained brain), call <see cref="Step"/> with inputs, read the outputs,
    /// and tell it how it did. Many instances (e.g. one per bot) share the weights and step together.
    /// <code>
    /// net = new ManifoldNet(config);
    /// void FixedUpdate() {
    ///     net.Step(sensors);
    ///     if (net.TryGetOutputs(actions)) Apply(actions);
    ///     net.ReportError(0, DistanceToGoal());
    /// }
    /// void OnDestroy() => net.Dispose();
    /// </code>
    /// Runs on the GPU when compute shaders are available (else, or with preferCpu, on the CPU). On the GPU, outputs
    /// arrive one step late through <see cref="TryGetOutputs"/>, or can stay on the GPU via <see cref="OutputBuffer"/>.
    /// </summary>
    public sealed class ManifoldNet : IDisposable
    {
        private static bool _loggedFallback;
        private readonly IMnnBackend _backend;

        private static readonly System.Collections.Generic.List<ManifoldNet> _live = new();

        static ManifoldNet()
        {
            // GPU buffers must be released before scripts reload or the app quits, or they leak.
            Application.quitting += DisposeAll;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
#endif
        }

        private static void DisposeAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
                _live[i].Dispose();
        }

        /// <summary>Every network that hasn't been disposed (for the debug window).</summary>
        public static System.Collections.Generic.IReadOnlyList<ManifoldNet> Live => _live;

        /// <summary>What the debug window calls it.</summary>
        public string Name = "Manifold Network";

        /// <summary>Labels for the inputs and outputs, shown in the debug window (optional).</summary>
        public string[] InputLabels, OutputLabels;

        public MnnSettings Settings { get; }
        public int InstanceCount { get; }
        public int InputCount => Settings.inputCount;
        public int OutputCount => Settings.outputCount;
        public bool UsingGpu { get; }

        public ManifoldNet(MnnConfig config, int instanceCount = 1, MnnBrain brain = null, bool preferCpu = false)
            : this(config != null ? config.Settings : brain != null ? brain.Settings : new MnnSettings(), instanceCount, brain, preferCpu) { }

        public ManifoldNet(MnnSettings settings, int instanceCount = 1, MnnBrain brain = null, bool preferCpu = false)
        {
            Settings = settings.Clone();
            Settings.Validate();
            InstanceCount = Math.Max(1, instanceCount);

            if (!preferCpu && MnnGpuBackend.Supported)
            {
                _backend = new MnnGpuBackend(Settings, InstanceCount);
                UsingGpu = true;
            }
            else
            {
                if (!preferCpu && !_loggedFallback)
                {
                    _loggedFallback = true;
                    Debug.Log("[Manifold Network] Compute shaders aren't available here; running on the CPU.");
                }
                _backend = new MnnCpuBackend(Settings, InstanceCount);
            }

            if (brain != null && !brain.IsEmpty)
                LoadBrain(brain);
            _live.Add(this);
        }

        /// <summary>
        /// The heads' positions of one instance (H × D floats), read now. On the GPU this waits for it: for the debug
        /// window and tools; drawing should use <see cref="HeadPositions"/>.
        /// </summary>
        public void GetHeadPositionsImmediate(int instance, float[] destination)
        {
            int count = Settings.headCount * Settings.spaceDimensions;
            if (_backend is MnnCpuBackend cpu)
            {
                cpu.Cpu.HeadPositions(instance).CopyTo(destination);
                return;
            }
            var all = new float[HeadPositions.count];
            HeadPositions.GetData(all);
            Array.Copy(all, instance * HeadStride, destination, 0, count);
        }

        /// <summary>The value spaces: point positions (H × P × D) and values (H × P × N).</summary>
        public void GetValueSpaces(out float[] points, out float[] values)
        {
            var reference = new MnnCpu(Settings, 1);
            points = reference.Points.ToArray();
            if (Settings.trainableValueSpaces)
            {
                var layout = new WeightLayout(Settings);
                values = new float[layout.ValuesCount];
                Array.Copy(GetParameters(), layout.ValuesOffset, values, 0, values.Length);
            }
            else
                values = reference.FixedValues.ToArray();
        }

        /// <summary>Learning on or off. Off, every instance runs the plain weights and nothing changes.</summary>
        public bool Training
        {
            get => _backend.Training;
            set => _backend.Training = value;
        }

        /// <summary>For tests and editor tools: GPU results read back immediately instead of a frame later.</summary>
        public bool Synchronous
        {
            get => _backend is MnnGpuBackend gpu && gpu.Synchronous;
            set { if (_backend is MnnGpuBackend gpu) gpu.Synchronous = value; }
        }

        /// <summary>One step for every instance. inputs.Length = InstanceCount × InputCount.</summary>
        public void Step(float[] inputs) => _backend.Step(inputs, default, default);

        /// <summary>
        /// Supervised: also trains toward targets (InstanceCount × OutputCount), each weighted by targetWeights (same
        /// size; 0 = no answer for that output; null = all 1).
        /// </summary>
        public void Step(float[] inputs, float[] targets, float[] targetWeights = null)
            => _backend.Step(inputs, targets, targetWeights);

        public void Step(ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets = default, ReadOnlySpan<float> targetWeights = default)
            => _backend.Step(inputs, targets, targetWeights);

        /// <summary>Population and sequential training: how wrong the last step of one instance was (add up freely).</summary>
        public void ReportError(int instance, float error) => _backend.ReportError(instance, error);

        /// <summary>The latest outputs (InstanceCount × OutputCount). On the GPU, last step's, and false until the first arrives.</summary>
        public bool TryGetOutputs(float[] destination) => _backend.TryGetOutputs(destination);

        /// <summary>Waits for this step's outputs. Stalls the GPU: tests and editor tools only.</summary>
        public void GetOutputsImmediate(float[] destination) => _backend.GetOutputsImmediate(destination);

        /// <summary>The outputs, kept on the GPU for other GPU work (animation, particles) with no readback.</summary>
        public GraphicsBuffer OutputBuffer => _backend.OutputBuffer;

        public MnnStats Stats => _backend.Stats;
        public ReadOnlySpan<float> HeadUsage => _backend.HeadUsage;
        public ReadOnlySpan<float> ErrorRing => _backend.ErrorRing;
        public int ErrorRingIndex => _backend.ErrorRingIndex;

        /// <summary>Head positions on the GPU (for drawing): instance i's H × D floats start at i × HeadStride.</summary>
        public GraphicsBuffer HeadPositions => _backend.HeadPositions;
        public int HeadStride => _backend.HeadStride;

        /// <summary>Forgets the past: heads back at rest (one instance, or all with -1).</summary>
        public void ResetState(int instance = -1) => _backend.ResetState(instance);

        /// <summary>One instance's memory, for replaying from that moment later. Arrives a frame later on the GPU.</summary>
        public void CaptureState(int instance, Action<MnnState> done) => _backend.CaptureState(instance, done);

        public void RestoreState(int instance, MnnState state) => _backend.RestoreState(instance, state);

        /// <summary>
        /// Trains on a recorded stretch (inputs and targets per step, weighted per output) starting from a captured
        /// memory, without touching the live instances. False if nothing was learned (not training, no weights).
        /// </summary>
        public bool Practice(MnnState start, ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights, int steps)
            => _backend.Practice(start, inputs, targets, weights, steps);

        /// <summary>A copy of the current weights.</summary>
        public float[] GetParameters() => _backend.GetParameters();

        /// <summary>Replaces the weights (same layout only).</summary>
        public void SetParameters(ReadOnlySpan<float> parameters) => _backend.SetParameters(parameters);

        /// <summary>What it has learned, as a new brain asset.</summary>
        public MnnBrain SaveBrain() => MnnBrain.Create(Settings, _backend.GetParameters(), _backend.Updates);

        /// <summary>Saves into an existing brain asset.</summary>
        public void SaveBrain(MnnBrain into)
        {
            into.Settings = Settings.Clone();
            into.Parameters = _backend.GetParameters();
            into.LayoutSignature = Settings.LayoutSignature;
            into.Updates += _backend.Updates;
        }

        public void LoadBrain(MnnBrain brain)
        {
            if (!brain.Fits(Settings))
                throw new ArgumentException($"The brain \"{brain.name}\" doesn't fit this network ({brain.LayoutSignature} vs {Settings.LayoutSignature}).");
            _backend.SetParameters(brain.Parameters);
        }

        public bool Disposed { get; private set; }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            _live.Remove(this);
            _backend.Dispose();
        }
    }

    /// <summary>Copying, crossbreeding and mutating weights between brains with the same layout.</summary>
    public static class MnnBreeding
    {
        public enum MixStyle
        {
            /// <summary>A weighted average of every weight.</summary>
            Blend,
            /// <summary>Whole parts from one parent or the other: the master, each head, the translator.</summary>
            SwapParts,
            /// <summary>Each weight from one parent or the other, by coin flip.</summary>
            PerWeight,
        }

        /// <summary>
        /// A child of two parents' weights. <paramref name="amount"/> is how much comes from B (0-1): the blend
        /// factor, or the chance each part or weight comes from B.
        /// </summary>
        public static float[] Mix(MnnSettings settings, float[] a, float[] b, MixStyle style, float amount, uint seed)
        {
            if (a.Length != b.Length)
                throw new ArgumentException("Only brains with the same layout can be mixed.");
            var child = new float[a.Length];
            amount = Mathf.Clamp01(amount);
            switch (style)
            {
                case MixStyle.Blend:
                    for (int i = 0; i < a.Length; i++)
                        child[i] = a[i] + (b[i] - a[i]) * amount;
                    break;

                case MixStyle.PerWeight:
                    for (int i = 0; i < a.Length; i++)
                        child[i] = MnnHash.Unit(MnnHash.Hash(seed, 7, (uint)i, 0, 0)) < amount ? b[i] : a[i];
                    break;

                case MixStyle.SwapParts:
                    var layout = new WeightLayout(settings);
                    Array.Copy(a, child, a.Length);
                    uint part = 0;
                    void Take(int start, int end)
                    {
                        if (MnnHash.Unit(MnnHash.Hash(seed, 7, part++, 1, 0)) < amount)
                            Array.Copy(b, start, child, start, end - start);
                    }
                    Take(layout.Master.Start, layout.Master.End);
                    foreach (var slave in layout.Slaves)
                        Take(slave.Start, slave.End);
                    Take(layout.Translator.Start, layout.Translator.End);
                    if (layout.ValuesCount > 0)
                        Take(layout.ValuesOffset, layout.Count);
                    break;
            }
            return child;
        }

        /// <summary>Adds a little random noise to every weight (strength is its typical size).</summary>
        public static void Mutate(float[] parameters, float strength, uint seed)
        {
            for (int i = 0; i < parameters.Length; i++)
                parameters[i] += strength * MnnHash.Noise(MnnHash.Hash(seed, 8, (uint)i, 0, 0));
        }
    }
}
