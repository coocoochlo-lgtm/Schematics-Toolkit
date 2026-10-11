using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork
{
    /// <summary>What ManifoldNet drives: the GPU kernels, or the CPU reference.</summary>
    internal interface IMnnBackend : IDisposable
    {
        bool Training { get; set; }
        int Updates { get; }
        MnnStats Stats { get; }

        void Step(ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights);
        void ReportError(int instance, float error);
        bool TryGetOutputs(float[] destination);
        void GetOutputsImmediate(float[] destination);
        GraphicsBuffer OutputBuffer { get; }

        void ResetState(int instance);
        void CaptureState(int instance, Action<MnnState> done);
        void RestoreState(int instance, MnnState state);
        bool Practice(MnnState start, ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights, int steps);

        float[] GetParameters();
        void SetParameters(ReadOnlySpan<float> parameters);

        /// <summary>Head positions on the GPU: instance i's heads (H × D floats) start at i × HeadStride.</summary>
        GraphicsBuffer HeadPositions { get; }
        int HeadStride { get; }

        ReadOnlySpan<float> HeadUsage { get; }
        ReadOnlySpan<float> ErrorRing { get; }
        int ErrorRingIndex { get; }
    }

    /// <summary>The CPU reference behind the same interface; GPU buffers are filled only when something asks.</summary>
    internal sealed class MnnCpuBackend : IMnnBackend
    {
        public readonly MnnCpu Cpu;
        private readonly int _instances;
        private GraphicsBuffer _outputBuffer, _headBuffer;
        private readonly float[] _headScratch;

        public MnnCpuBackend(MnnSettings settings, int instances)
        {
            Cpu = new MnnCpu(settings, instances);
            _instances = instances;
            _headScratch = new float[instances * settings.headCount * settings.spaceDimensions];
        }

        public bool Training { get => Cpu.Training; set => Cpu.Training = value; }
        public int Updates => Cpu.Stats.Updates;
        public MnnStats Stats => Cpu.Stats;

        public void Step(ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights) => Cpu.Step(inputs, targets, weights);
        public void ReportError(int instance, float error) => Cpu.ReportError(instance, error);

        public bool TryGetOutputs(float[] destination)
        {
            Cpu.Outputs.CopyTo(destination);
            return true;
        }

        public void GetOutputsImmediate(float[] destination) => Cpu.Outputs.CopyTo(destination);

        public GraphicsBuffer OutputBuffer
        {
            get
            {
                _outputBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, Cpu.Outputs.Length, sizeof(float));
                _outputBuffer.SetData(Cpu.Outputs.ToArray());
                return _outputBuffer;
            }
        }

        public void ResetState(int instance) => Cpu.ResetState(instance);
        public void CaptureState(int instance, Action<MnnState> done) => done(Cpu.CaptureState(instance));
        public void RestoreState(int instance, MnnState state) => Cpu.RestoreState(instance, state);
        public bool Practice(MnnState start, ReadOnlySpan<float> inputs, ReadOnlySpan<float> targets, ReadOnlySpan<float> weights, int steps)
            => Cpu.Practice(start, inputs, targets, weights, steps);

        public float[] GetParameters() => (float[])Cpu.Parameters.Clone();
        public void SetParameters(ReadOnlySpan<float> parameters) => Cpu.SetParameters(parameters);

        public GraphicsBuffer HeadPositions
        {
            get
            {
                int stride = HeadStride;
                _headBuffer ??= new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(1, _headScratch.Length), sizeof(float));
                for (int i = 0; i < _instances; i++)
                    Cpu.HeadPositions(i).CopyTo(new Span<float>(_headScratch, i * stride, stride));
                _headBuffer.SetData(_headScratch);
                return _headBuffer;
            }
        }

        public int HeadStride => Cpu.Settings.headCount * Cpu.Settings.spaceDimensions;
        public ReadOnlySpan<float> HeadUsage => Cpu.HeadUsage;
        public ReadOnlySpan<float> ErrorRing => Cpu.ErrorRing;
        public int ErrorRingIndex => Cpu.ErrorRingIndex;

        public void Dispose()
        {
            _outputBuffer?.Release();
            _headBuffer?.Release();
            _outputBuffer = null;
            _headBuffer = null;
        }
    }
}
