using NUnit.Framework;
using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Tests
{
    /// <summary>
    /// The GPU kernels against the CPU reference (the source of truth): same seed, same inputs, same outputs, and the
    /// same weights after training. If these disagree, the kernel is wrong.
    /// </summary>
    public class ParityTests
    {
        private static MnnSettings[] Configs => new[]
        {
            new MnnSettings { inputCount = 3, outputCount = 2 },
            new MnnSettings { inputCount = 2, outputCount = 3, headCount = 4, spaceDimensions = 2, hiddenLayers = 2, hiddenWidth = 12, samplingMode = SamplingMode.Nearest, liquidGates = false },
            new MnnSettings { inputCount = 4, outputCount = 2, headCount = 6, pointsPerSpace = 64, clockInputs = true, changeInputs = true, velocityReadout = true, trainableValueSpaces = true, perturbMode = PerturbMode.OneSided, updateRule = UpdateRule.Momentum },
            // The optional modules: the shared plan grid (all three rulebooks), the memory bank and the spatial grid.
            new MnnSettings { inputCount = 2, outputCount = 2, headCount = 5, pointsPerSpace = 512, dispersion = 0.12f, spatialGrid = true, wfcEnabled = true, wfcAxis = WfcAxis.Grid, typeCount = 4, planColumns = 4, memoryEnabled = true, memorySlots = 16 },
        };

        // Uneven ticks, so real-time stepping is covered too.
        private static float DeltaTime(int step) => 0.05f * (0.6f + 0.8f * ((step * 7) % 5) / 4f);

        private const float Tolerance = 1e-4f;

        private static void RequireGpu()
        {
            if (!SystemInfo.supportsComputeShaders || Resources.Load<ComputeShader>("ManifoldNet") == null)
                Assert.Ignore("Compute shaders aren't available on this machine.");
        }

        private static float[] Inputs(int step, int instances, int count)
        {
            var inputs = new float[instances * count];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = Mathf.Sin(step * 0.13f + i * 0.7f);
            return inputs;
        }

        [Test]
        public void ForwardMatchesCpu([Values(0, 1, 2, 3)] int config)
        {
            RequireGpu();
            var settings = Configs[config];
            using var gpu = new ManifoldNet(settings, 3) { Synchronous = true, Training = false };
            var cpu = new MnnCpu(settings, 3) { Training = false };
            var outputs = new float[3 * settings.outputCount];
            float worst = 0f;
            for (int step = 0; step < 1000; step++)
            {
                var inputs = Inputs(step, 3, settings.inputCount);
                gpu.DeltaTime = DeltaTime(step);
                gpu.Step(inputs);
                cpu.Step(inputs, default, default, DeltaTime(step));
                if (settings.memoryEnabled && step % 97 == 5)
                {
                    var value = Inputs(step + 11, 1, settings.outputCount);
                    gpu.Remember(step % 3, value, 1.5f);
                    cpu.Remember(step % 3, value, 1.5f);
                }
                gpu.GetOutputsImmediate(outputs);
                for (int i = 0; i < outputs.Length; i++)
                    worst = Mathf.Max(worst, Mathf.Abs(outputs[i] - cpu.Outputs[i]));
            }
            Assert.Less(worst, Tolerance, $"Largest output difference over 1000 steps: {worst}");

            if (settings.memoryEnabled)
            {
                var bank = gpu.GetMemoryBank();
                float worstSlot = 0f;
                for (int i = 0; i < bank.Length; i++) worstSlot = Mathf.Max(worstSlot, Mathf.Abs(bank[i] - cpu.MemoryBank[i]));
                Assert.Less(worstSlot, Tolerance, $"Memory banks differ by {worstSlot}");
            }
            var gpuState = gpu.PeekStateImmediate(1);
            var cpuState = cpu.PeekState(1);
            CollectionAssert.AreEqual(cpuState.PlanType, gpuState.PlanType, "plan grids differ");
            for (int i = 0; i < cpuState.Attractors.Length; i++)
                Assert.AreEqual(cpuState.Attractors[i], gpuState.Attractors[i], Tolerance, "attractors differ");
        }

        [Test]
        public void TrainingMatchesCpu([Values(0, 1, 2, 3)] int config)
        {
            RequireGpu();
            var settings = Configs[config];
            settings.reviveSteps = int.MaxValue; // revival depends on usage averages; checked separately in-game
            using var gpu = new ManifoldNet(settings, 2) { Synchronous = true };
            var cpu = new MnnCpu(settings, 2);
            for (int step = 0; step < 100 * settings.evalWindow + 1; step++)
            {
                var inputs = Inputs(step, 2, settings.inputCount);
                var targets = Inputs(step + 3, 2, settings.outputCount);
                gpu.Step(inputs, targets);
                cpu.Step(inputs, targets);
            }
            var weights = gpu.GetParameters();
            float worst = 0f;
            for (int i = 0; i < weights.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(weights[i] - cpu.Parameters[i]));
            Assert.AreEqual(cpu.Stats.Updates, gpu.Stats.Updates, "Update counts differ");
            Assert.Less(worst, Tolerance, $"Largest weight difference after 100 updates: {worst}");
        }

        [Test]
        public void PracticeMatchesCpu()
        {
            RequireGpu();
            var settings = Configs[0];
            using var gpu = new ManifoldNet(settings) { Synchronous = true };
            var cpu = new MnnCpu(settings);
            const int steps = 12;
            var inputs = Inputs(0, steps, settings.inputCount);
            var targets = Inputs(5, steps, settings.outputCount);
            var start = cpu.CaptureState(0);
            for (int r = 0; r < 10; r++)
            {
                gpu.Practice(start, inputs, targets, default, steps);
                cpu.Practice(start, inputs, targets, default, steps);
            }
            var weights = gpu.GetParameters();
            float worst = 0f;
            for (int i = 0; i < weights.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(weights[i] - cpu.Parameters[i]));
            Assert.Less(worst, Tolerance, $"Largest weight difference after practice: {worst}");
        }

        [Test]
        public void StateRoundTrips()
        {
            RequireGpu();
            var settings = Configs[3];
            using var gpu = new ManifoldNet(settings) { Synchronous = true, Training = false };
            for (int step = 0; step < 20; step++) gpu.Step(Inputs(step, 1, settings.inputCount));
            MnnState captured = null;
            gpu.CaptureState(0, s => captured = s);
            Assert.NotNull(captured);
            gpu.ResetState();
            gpu.RestoreState(0, captured);
            MnnState again = null;
            gpu.CaptureState(0, s => again = s);
            CollectionAssert.AreEqual(captured.Positions, again.Positions);
            CollectionAssert.AreEqual(captured.Attractors, again.Attractors);
            CollectionAssert.AreEqual(captured.PlanType, again.PlanType);
            CollectionAssert.AreEqual(captured.PlanSpread, again.PlanSpread);
            CollectionAssert.AreEqual(captured.MemoryKey, again.MemoryKey);
            Assert.AreEqual(captured.Step, again.Step);
        }

        [Test]
        public void ThousandInstancesStepInOneDispatch()
        {
            RequireGpu();
            using var gpu = new ManifoldNet(new MnnSettings { wfcEnabled = true, memoryEnabled = true }, 1024) { Synchronous = true };
            var inputs = new float[1024 * 8];
            var targets = new float[1024 * 4];
            Assert.DoesNotThrow(() => { for (int i = 0; i < 5; i++) gpu.Step(inputs, targets); });
        }
    }
}
