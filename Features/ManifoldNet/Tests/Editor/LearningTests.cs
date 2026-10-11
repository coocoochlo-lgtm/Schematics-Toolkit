using NUnit.Framework;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Tests
{
    /// <summary>Does it learn? The CPU checks run anywhere; the GPU ones where compute shaders exist.</summary>
    public class LearningTests
    {
        private static double SinePrediction(ManifoldNet net, int steps)
        {
            var output = new float[1];
            double tail = 0;
            int tailSteps = steps / 10;
            for (int t = 0; t < steps; t++)
            {
                float x = Mathf.Sin(t * 0.1f), y = Mathf.Sin(t * 0.1f + 0.3f);
                net.Step(new[] { x }, new[] { y });
                net.GetOutputsImmediate(output);
                if (t >= steps - tailSteps) tail += (output[0] - y) * (output[0] - y);
            }
            return tail / tailSteps;
        }

        private static double Untrained(MnnSettings settings)
        {
            var probe = new MnnCpu(settings) { Training = false };
            double sum = 0;
            for (int t = 0; t < 500; t++)
            {
                float x = Mathf.Sin(t * 0.1f), y = Mathf.Sin(t * 0.1f + 0.3f);
                probe.Step(new[] { x });
                sum += (probe.Outputs[0] - y) * (probe.Outputs[0] - y);
            }
            return sum / 500;
        }

        [Test]
        public void SinePredictionLearnsOnCpu()
        {
            var settings = new MnnSettings { inputCount = 1, outputCount = 1 };
            using var net = new ManifoldNet(settings, preferCpu: true);
            double end = SinePrediction(net, 10000);
            Assert.Less(end, 0.2 * Untrained(settings), $"Error at the end: {end}");
        }

        [Test]
        public void SinePredictionLearnsOnGpu()
        {
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore("Compute shaders aren't available on this machine.");
            var settings = new MnnSettings { inputCount = 1, outputCount = 1 };
            using var net = new ManifoldNet(settings) { Synchronous = true };
            double end = SinePrediction(net, 10000);
            Assert.Less(end, 0.2 * Untrained(settings), $"Error at the end: {end}");
        }

        [Test]
        public void BrainSavesAndLoadsExactly()
        {
            var settings = new MnnSettings { inputCount = 2, outputCount = 2 };
            using var a = new ManifoldNet(settings, preferCpu: true);
            for (int t = 0; t < 50; t++) a.Step(new[] { Mathf.Sin(t), 1f }, new[] { 0.5f, -0.5f });
            var brain = a.SaveBrain();
            using var b = new ManifoldNet(settings, brain: brain, preferCpu: true);
            CollectionAssert.AreEqual(a.GetParameters(), b.GetParameters());
            Object.DestroyImmediate(brain);
        }

        [Test]
        public void MixingNeedsTheSameLayout()
        {
            var a = new MnnSettings { inputCount = 2 };
            var b = new MnnSettings { inputCount = 3 };
            var pa = new MnnCpu(a).Parameters;
            var pb = new MnnCpu(b).Parameters;
            Assert.Throws<System.ArgumentException>(() => MnnBreeding.Mix(a, pa, pb, MnnBreeding.MixStyle.Blend, 0.5f, 1));
            var child = MnnBreeding.Mix(a, pa, new MnnCpu(new MnnSettings { inputCount = 2, seed = 7 }).Parameters, MnnBreeding.MixStyle.SwapParts, 0.5f, 3);
            Assert.AreEqual(pa.Length, child.Length);
        }
    }
}
