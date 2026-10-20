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
            // Identity masks, the imitation memory with trajectory credit, and familiarity-scaled exploration (with the
            // plan grid and memory bank, so familiarity has both sources).
            Learners(TrainingMode.Supervised),
        };

        private static MnnSettings Learners(TrainingMode mode) => new()
        {
            inputCount = 3, outputCount = 2, headCount = 4, pointsPerSpace = 96, trainingMode = mode,
            wfcEnabled = true, wfcAxis = WfcAxis.Grid, typeCount = 4, planColumns = 3, memoryEnabled = true, memorySlots = 8,
            identityMasks = true, imitationEnabled = true, imitationSlots = 32, familiarityExploration = true,
            trajectoryCredit = true, creditSteps = 4,
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
        public void ForwardMatchesCpu([Values(0, 1, 2, 3, 4)] int config)
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
        public void TrainingMatchesCpu([Values(0, 1, 2, 3, 4)] int config)
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
            CompareLearners(gpu, cpu);
        }

        // Reward-only training (sequential, errors reported after each step) with identity masks and imitation.
        [Test]
        public void RewardTrainingMatchesCpu()
        {
            RequireGpu();
            var settings = Learners(TrainingMode.Sequential);
            settings.reviveSteps = int.MaxValue;
            using var gpu = new ManifoldNet(settings, 1) { Synchronous = true };
            var cpu = new MnnCpu(settings, 1);
            var outputs = new float[settings.outputCount];
            for (int step = 0; step < 100 * settings.CopiesPerWindow; step++)
            {
                var inputs = Inputs(step, 1, settings.inputCount);
                var targets = Inputs(step + 3, 1, settings.outputCount);
                gpu.Step(inputs);
                cpu.Step(inputs);
                gpu.GetOutputsImmediate(outputs);
                float gpuError = 0f, cpuError = 0f;
                for (int n = 0; n < settings.outputCount; n++)
                {
                    gpuError += (outputs[n] - targets[n]) * (outputs[n] - targets[n]);
                    cpuError += (cpu.Outputs[n] - targets[n]) * (cpu.Outputs[n] - targets[n]);
                }
                Assert.AreEqual(cpuError, gpuError, 1e-3f, $"step {step}: outputs drifted apart");
                // Both report the CPU's error, so a tiny output difference can't split their scores.
                gpu.ReportError(0, cpuError);
                cpu.ReportError(0, cpuError);
            }
            var weights = gpu.GetParameters();
            float worst = 0f;
            for (int i = 0; i < weights.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(weights[i] - cpu.Parameters[i]));
            Assert.AreEqual(cpu.Stats.Updates, gpu.Stats.Updates, "Update counts differ");
            Assert.Less(worst, 1e-3f, $"Largest weight difference after 100 reward updates: {worst}");
            CompareLearners(gpu, cpu);
        }

        private static void CompareLearners(ManifoldNet gpu, MnnCpu cpu)
        {
            if (cpu.Settings.identityMasks)
            {
                var masks = gpu.GetIdentityMasks();
                Assert.AreEqual(cpu.IdentityMasks.Length, masks.Length);
                for (int i = 0; i < masks.Length; i++)
                    Assert.AreEqual(cpu.IdentityMasks[i], masks[i], 1e-3f, "identity masks differ");
            }
            if (cpu.Settings.imitationEnabled)
            {
                var bank = gpu.GetImitationBank();
                Assert.AreEqual(cpu.ImitationBank.Length, bank.Length);
                int written = 0;
                for (int i = 0; i < bank.Length; i++)
                {
                    Assert.AreEqual(cpu.ImitationBank[i], bank[i], 1e-3f, $"imitation memories differ at {i}");
                    if ((i + 1) % cpu.Settings.ImitationStride == 0 && bank[i] > 0.5f) written++;
                }
                Assert.Greater(written, 0, "imitation memory was written");
            }
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
