using NUnit.Framework;
using System;

namespace Remedy.ManifoldNetwork.Tests
{
    /// <summary>
    /// Milestone M0 and the core math on the CPU reference: torus wrap, wrapped attractor offsets, soft-sampling
    /// weights, nearest-mode IDs, the liquid rate, real-time stepping, the hash, and the direction of a training update.
    /// These run anywhere (no GPU needed).
    /// </summary>
    public class MathTests
    {
        [Test]
        public void TorusWrap()
        {
            Assert.AreEqual(-0.8f, MnnCpu.Wrap(1.2f), 1e-5f);
            Assert.AreEqual(0.8f, MnnCpu.Wrap(-1.2f), 1e-5f);
            Assert.AreEqual(-1f, MnnCpu.Wrap(1f));
            Assert.AreEqual(-0.1f, MnnCpu.WrapDelta(1.9f), 1e-5f, "the short way round");
            Assert.AreEqual(0.1f, MnnCpu.WrapDelta(-1.9f), 1e-5f, "the short way round");
        }

        [Test]
        public void HashIsDeterministic()
        {
            // Fixed values: if these change, saved brains and the GPU kernels no longer agree with the CPU.
            Assert.AreEqual(738272812u, MnnHash.Hash(1234, 4, 7, 3, 99));
            Assert.AreEqual(MnnHash.Hash(5, 6, 7, 8, 9), MnnHash.Hash(5, 6, 7, 8, 9));
            Assert.AreNotEqual(MnnHash.Hash(5, 6, 7, 8, 9), MnnHash.Hash(5, 6, 7, 8, 10));

            double mean = 0, square = 0;
            const int count = 100000;
            for (int i = 0; i < count; i++)
            {
                float n = MnnCpu.NoiseAt(1234, 5, 2, i);
                mean += n;
                square += n * n;
            }
            mean /= count;
            Assert.AreEqual(0.0, mean, 0.02, "noise mean");
            Assert.AreEqual(1.0, square / count - mean * mean, 0.02, "noise variance");
        }

        [Test]
        public void SameSeedSameOutputs()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 2, wfcEnabled = true, memoryEnabled = true };
            var a = new MnnCpu(s);
            var b = new MnnCpu(s);
            for (int t = 0; t < 300; t++)
            {
                var input = new[] { MathF.Sin(t * 0.1f), MathF.Cos(t * 0.07f) };
                var target = new[] { input[1], input[0] };
                a.Step(input, target);
                b.Step(input, target);
                if (t % 50 == 0) { a.Remember(0, target, 1f); b.Remember(0, target, 1f); }
                Assert.AreEqual(a.Outputs[0], b.Outputs[0]);
                Assert.AreEqual(a.Outputs[1], b.Outputs[1]);
            }
        }

        [Test]
        public void LiquidRate()
        {
            float full = MnnCpu.LiquidRate(0.05f, 0.2f, 0f, false);
            float half = MnnCpu.LiquidRate(0.025f, 0.2f, 0f, false);
            Assert.AreEqual(full, 1f - (1f - half) * (1f - half), 1e-6f, "two half steps blend exactly like one full step");
            Assert.AreEqual(1f - MathF.Exp(-0.05f / 0.2f), full, 1e-6f);
            Assert.Greater(MnnCpu.LiquidRate(0.05f, 0.2f, 3f, true), MnnCpu.LiquidRate(0.05f, 0.2f, -3f, true), "the gate speeds a head up");
            Assert.AreEqual(0f, MnnCpu.LiquidRate(0f, 0.2f, 1f, true), "no time, no change");
            Assert.AreEqual(MnnCpu.Softplus(0f), MathF.Log(2f), 1e-6f);
            Assert.AreEqual(50f, MnnCpu.Softplus(50f), 1e-4f, "softplus doesn't overflow");
        }

        // A head parked on the seam of the torus (x = -1 = +1), with points on both sides.
        private static (MnnCpu net, MnnState state) Parked()
        {
            var s = new MnnSettings { inputCount = 1, outputCount = 1, headCount = 1, spaceDimensions = 1, pointsPerSpace = 64, dispersion = 0.1f, pull = 0f, settle = 0f };
            var net = new MnnCpu(s) { Training = false };
            var start = net.CaptureState(0);
            start.Positions[0] = -1f;
            start.Attractors[0] = -1f;
            net.RestoreState(0, start);
            net.Step(new[] { 0f });
            return (net, net.PeekState(0));
        }

        [Test]
        public void AttractorUsesWrappedOffsets()
        {
            var (net, state) = Parked();
            const float sigma = 0.1f;
            float total = 0f, offset = 0f, naive = 0f;
            int left = 0, right = 0;
            for (int j = 0; j < 64; j++)
            {
                float x = net.Points[j];
                float d = MnnCpu.WrapDelta(x - state.Positions[0]);
                if (d * d > 9f * sigma * sigma) continue;
                float w = MathF.Exp(-d * d / (2f * sigma * sigma));
                total += w; offset += w * d; naive += w * x;
                if (x > 0f) right++; else left++;
            }
            Assume.That(left > 0 && right > 0, "the seed should put points on both sides of the seam");
            float expected = MnnCpu.Wrap(state.Positions[0] + offset / (total + 1e-6f));
            Assert.AreEqual(0f, MnnCpu.WrapDelta(state.Attractors[0] - expected), 1e-5f);
            Assert.Greater(MathF.Abs(MnnCpu.WrapDelta(naive / total - expected)), 0.3f, "an unwrapped average would land far from the seam");
        }

        [Test]
        public void SoftSamplingWeightsSumToOne()
        {
            var (net, state) = Parked();
            const float sigma = 0.1f;
            float sample = 0f, total = 0f;
            for (int j = 0; j < 64; j++)
            {
                float d = MnnCpu.WrapDelta(net.Points[j] - state.Positions[0]);
                if (d * d > 9f * sigma * sigma) continue;
                float w = MathF.Exp(-d * d / (2f * sigma * sigma));
                sample += w * net.FixedValues[j];
                total += w;
            }
            Assert.AreEqual(sample / (total + 1e-6f), state.Samples[0], 1e-5f);
        }

        [Test]
        public void NearestModeReadsTheNearestPoint()
        {
            var s = new MnnSettings { inputCount = 1, outputCount = 3, headCount = 2, pointsPerSpace = 32, samplingMode = SamplingMode.Nearest };
            var net = new MnnCpu(s) { Training = false };
            for (int t = 0; t < 50; t++)
            {
                net.Step(new[] { MathF.Sin(t * 0.3f) });
                var state = net.PeekState(0);
                for (int h = 0; h < 2; h++)
                {
                    int best = 0;
                    float bestDistance = float.MaxValue;
                    for (int j = 0; j < 32; j++)
                    {
                        float d2 = 0f;
                        for (int d = 0; d < 3; d++)
                        {
                            float delta = MnnCpu.WrapDelta(net.Points[(h * 32 + j) * 3 + d] - state.Positions[h * 3 + d]);
                            d2 += delta * delta;
                        }
                        if (d2 < bestDistance) { bestDistance = d2; best = j; }
                    }
                    for (int n = 0; n < 3; n++)
                        Assert.AreEqual(net.FixedValues[(h * 32 + best) * 3 + n], state.Samples[h * 3 + n]);
                }
            }
        }

        [Test]
        public void SoftSamplingNeverNaNs()
        {
            var s = new MnnSettings { inputCount = 1, outputCount = 2, headCount = 2, pointsPerSpace = 1, dispersion = 0.001f };
            var net = new MnnCpu(s) { Training = false };
            for (int t = 0; t < 50; t++) net.Step(new float[] { t });
            Assert.IsTrue(float.IsFinite(net.Outputs[0]) && float.IsFinite(net.Outputs[1]));
        }

        [Test]
        public void SameRealTimeSameSpotAt20And60Hz()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 2, headCount = 4 };
            foreach (var input in new[] { new[] { 0.5f, -0.3f }, new[] { -0.8f, 0.9f } })
            {
                var slow = new MnnCpu(s) { Training = false };
                var fast = new MnnCpu(s) { Training = false };
                for (int t = 0; t < 20 * 10; t++) slow.Step(input, default, default, 1f / 20f);
                for (int t = 0; t < 60 * 10; t++) fast.Step(input, default, default, 1f / 60f);
                var a = slow.PeekState(0).Positions;
                var b = fast.PeekState(0).Positions;
                for (int i = 0; i < a.Length; i++)
                    Assert.Less(MathF.Abs(MnnCpu.WrapDelta(a[i] - b[i])), 0.02f * 2f * MnnSettings.SpaceRadius, "within 2% of the space");
            }
        }

        [Test]
        public void UpdatePointsDownhill([Values(PerturbMode.Antithetic, PerturbMode.OneSided)] PerturbMode mode)
        {
            // The first update, with the blueprint's plain step, must lower the true one-step error for a small step
            // along it and raise it the other way: the noise and the sign per copy agree between nudges and update.
            var s = new MnnSettings
            {
                inputCount = 2, outputCount = 2, balanceLambda = 0f, updateRule = UpdateRule.Momentum, learningRate = 0.01f,
                perturbMode = mode, perturbSigma = 0.005f, bestWeightsRollback = false,
            };
            var x = new[] { 0.3f, -0.6f };
            var y = new[] { 0.5f, -0.25f };
            var net = new MnnCpu(s);
            var before = (float[])net.Parameters.Clone();
            net.Step(x, y);
            net.Step(x, y);   // closes the first window and applies its update
            var delta = new float[before.Length];
            for (int i = 0; i < delta.Length; i++) delta[i] = net.Parameters[i] - before[i];

            float Error(float scale)
            {
                var probe = new MnnCpu(s) { Training = false };
                var weights = new float[before.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = before[i] + scale * delta[i];
                probe.SetParameters(weights);
                probe.Step(x);
                float e0 = probe.Outputs[0] - y[0], e1 = probe.Outputs[1] - y[1];
                return (e0 * e0 + e1 * e1) / 2f;
            }
            Assert.Less(Error(0.1f), Error(0f));
            Assert.Less(Error(0f), Error(-0.1f));
        }

        [Test]
        public void SpatialGridMatchesBruteForce([Values(SamplingMode.Soft, SamplingMode.Nearest)] SamplingMode mode)
        {
            var plain = new MnnSettings { inputCount = 2, outputCount = 2, headCount = 3, pointsPerSpace = 1024, dispersion = 0.1f, samplingMode = mode };
            var gridded = plain.Clone();
            gridded.spatialGrid = true;
            Assert.IsTrue(gridded.UsesSpatialGrid);
            var a = new MnnCpu(plain) { Training = false };
            var b = new MnnCpu(gridded) { Training = false };
            for (int t = 0; t < 200; t++)
            {
                var x = new[] { MathF.Sin(t * 0.1f), MathF.Cos(t * 0.05f) };
                a.Step(x);
                b.Step(x);
                Assert.AreEqual(a.Outputs[0], b.Outputs[0], 1e-4f);
            }
        }
    }
}
