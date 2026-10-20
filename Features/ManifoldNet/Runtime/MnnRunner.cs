using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The optional MonoBehaviour driver: owns one <see cref="ManifoldNet"/> made from a config (and optionally a
    /// trained brain) and steps it every FixedUpdate. Fill <see cref="Inputs"/> (and <see cref="Targets"/> to teach)
    /// from <see cref="BeforeStep"/> or any script, read <see cref="Outputs"/> (one step late on the GPU), and call
    /// <see cref="ReportError"/> when the game knows how it did. Handy for samples and quick experiments; a game with
    /// many agents usually steps one batched network itself.
    /// </summary>
    [AddComponentMenu("Remedy/Manifold Network Runner")]
    public sealed class MnnRunner : MonoBehaviour
    {
        public MnnConfig Config;
        [Tooltip("Optional: start from these learned weights (and memories).")]
        public MnnBrain Brain;
        [Tooltip("Independent copies of the network that step together (e.g. one per agent).")]
        [Min(1)] public int Instances = 1;
        [Tooltip("Run on the CPU even where compute shaders exist. For a single small network the CPU is often faster.")]
        public bool PreferCpu;
        public bool Training = true;
        [Tooltip("Teach toward Targets every step (supervised). Off: only reported errors train it.")]
        public bool UseTargets;

        /// <summary>Instances × InputCount; fill before each step.</summary>
        [NonSerialized] public float[] Inputs = Array.Empty<float>();
        /// <summary>Instances × OutputCount; the right answers when <see cref="UseTargets"/> is on.</summary>
        [NonSerialized] public float[] Targets = Array.Empty<float>();
        /// <summary>Instances × OutputCount: the latest outputs (last step's on the GPU).</summary>
        [NonSerialized] public float[] Outputs = Array.Empty<float>();

        /// <summary>Raised right before each step: fill <see cref="Inputs"/> here.</summary>
        public event Action<MnnRunner> BeforeStep;
        /// <summary>Raised after each step with fresh <see cref="Outputs"/> (when they've arrived).</summary>
        public event Action<MnnRunner> AfterStep;

        public ManifoldNet Net { get; private set; }

        private float _lastStep = -1f;

        private void OnEnable()
        {
            if (Config == null && Brain == null)
            {
                Debug.LogWarning("[Manifold Network] The runner has no config.", this);
                return;
            }
            Net = new ManifoldNet(Config, Instances, Brain, PreferCpu) { Name = name, Training = Training };
            int count = Math.Max(1, Instances);
            Inputs = new float[count * Net.InputCount];
            Targets = new float[count * Net.OutputCount];
            Outputs = new float[count * Net.OutputCount];
            _lastStep = -1f;
        }

        private void FixedUpdate()
        {
            if (Net == null || Net.Disposed)
                return;
            BeforeStep?.Invoke(this);
            float now = Time.fixedTime;
            Net.DeltaTime = _lastStep < 0f ? 0f : now - _lastStep;
            _lastStep = now;
            Net.Training = Training;
            if (UseTargets) Net.Step(Inputs, Targets);
            else Net.Step(Inputs);
            if (Net.TryGetOutputs(Outputs))
                AfterStep?.Invoke(this);
        }

        /// <summary>How wrong the last step of one instance was (population and sequential training).</summary>
        public void ReportError(int instance, float error) => Net?.ReportError(instance, error);

        private void OnDisable()
        {
            Net?.Dispose();
            Net = null;
        }
    }
}
