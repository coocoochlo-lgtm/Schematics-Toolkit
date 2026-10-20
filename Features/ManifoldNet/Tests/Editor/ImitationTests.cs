using NUnit.Framework;
using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Tests
{
    /// <summary>
    /// Identity masks, the self-imitation memory, familiarity-scaled exploration and trajectory credit, on the CPU
    /// reference (MnnCpu.Imitation.cs).
    /// </summary>
    public class ImitationTests
    {
        private static MnnSettings Reward(bool identity, bool imitation) => new()
        {
            inputCount = 2, outputCount = 2, headCount = 4, pointsPerSpace = 64,
            trainingMode = TrainingMode.Sequential,
            identityMasks = identity,
            imitationEnabled = imitation, imitationSlots = 16, imitationKeyFeedback = false,
        };

        [Test]
        public void SimHashIsLocal()
        {
            var s = new MnnSettings { inputCount = 3, outputCount = 1, imitationKeyFeedback = false };
            var projection = MnnCpu.BuildImitationProjection(s);
            var random = new System.Random(5);
            float Next() => (float)random.NextDouble() * 2f - 1f;
            double near = 0, mid = 0, far = 0;
            const int trials = 500;
            for (int t = 0; t < trials; t++)
            {
                var a = new[] { Next(), Next(), Next() };
                var b = new[] { a[0] + 0.02f, a[1] - 0.02f, a[2] };
                var c = new[] { a[0] + 0.3f, a[1] - 0.3f, a[2] + 0.3f };
                var d = new[] { Next(), Next(), Next() };
                MnnCpu.SimHash(projection, a, out uint aLo, out uint aHi);
                MnnCpu.SimHash(projection, b, out uint bLo, out uint bHi);
                MnnCpu.SimHash(projection, c, out uint cLo, out uint cHi);
                MnnCpu.SimHash(projection, d, out uint dLo, out uint dHi);
                near += MnnCpu.Hamming(aLo, aHi, bLo, bHi);
                mid += MnnCpu.Hamming(aLo, aHi, cLo, cHi);
                far += MnnCpu.Hamming(aLo, aHi, dLo, dHi);
            }
            near /= trials; mid /= trials; far /= trials;
            Assert.Less(near, 2.0, "a tiny move flips almost no bits");
            Assert.Less(near, mid, "closer situations share more bits");
            Assert.Less(mid, far, "closer situations share more bits");
            Assert.Greater(far, 12.0, "unrelated situations differ in many bits");

            MnnCpu.SimHash(projection, new[] { 0.1f, 0.2f, 0.3f }, out uint lo1, out uint hi1);
            MnnCpu.SimHash(projection, new[] { 0.1f, 0.2f, 0.3f }, out uint lo2, out uint hi2);
            Assert.AreEqual(lo1, lo2);
            Assert.AreEqual(hi1, hi2);
        }

        [Test]
        public void ImitationBlendLeansTowardTheRememberedOutput()
        {
            // At no confidence it's the reported error; at full confidence, closer to the remembered output is better.
            Assert.AreEqual(0.8f, MnnCpu.ImitationBlend(0.8f, 0f, 1f, 0.3f), 1e-6f);
            Assert.Less(MnnCpu.ImitationBlend(0.5f, 0.6f, 1f, 0.01f), MnnCpu.ImitationBlend(0.5f, 0.6f, 1f, 0.9f));
            Assert.AreEqual(0.4f * 0.5f + 0.6f * 2f * 0.25f, MnnCpu.ImitationBlend(0.5f, 0.6f, 2f, 0.25f), 1e-6f);
        }

        [Test]
        public void ImitationWritesOnlyBetterThanBaselineAttempts()
        {
            var s = Reward(false, true);
            var net = new MnnCpu(s);
            var x = new[] { 0.3f, -0.4f };
            // A baseline of 1.
            for (int t = 0; t < 20; t++) { net.Step(x); net.ReportError(0, 1f); }
            net.Step(x);
            Assert.AreEqual(0, net.Stats.ImitationWrites, "average and worse steps write nothing");

            // Worse than usual: nothing.
            net.ReportError(0, 3f);
            net.Step(x);
            Assert.AreEqual(0, net.Stats.ImitationWrites);
            Assert.AreEqual(0, UsedSlots(net, s));

            // Better than usual: what it just did is remembered.
            float o0 = net.Outputs[0], o1 = net.Outputs[1];
            net.ReportError(0, 0f);
            net.Step(x);
            Assert.AreEqual(1, net.Stats.ImitationWrites);
            Assert.AreEqual(1, UsedSlots(net, s));
            int slot = FirstUsed(net, s);
            var bank = net.ImitationBank;
            Assert.AreEqual(o0, bank[slot * s.ImitationStride], 1e-6f, "the good attempt is stored as is");
            Assert.AreEqual(o1, bank[slot * s.ImitationStride + 1], 1e-6f);
            Assert.Greater(bank[slot * s.ImitationStride + 2], 0f, "quality is the (positive) advantage");
        }

        [Test]
        public void ImitationPullsTheRememberedOutputTowardTheGoodAttempt()
        {
            var s = Reward(false, true);
            var net = new MnnCpu(s);
            var x = new[] { 0.3f, -0.4f };
            for (int t = 0; t < 20; t++) { net.Step(x); net.ReportError(0, 1f); }
            net.Step(x);
            net.ReportError(0, 0f);   // remembered
            net.Step(x);
            int slot = FirstUsed(net, s);
            for (int round = 0; round < 10; round++)
            {
                float before = net.ImitationBank[slot * s.ImitationStride];
                float attempt = net.Outputs[0];
                net.ReportError(0, 0f);
                net.Step(x);
                Assert.AreEqual(1, UsedSlots(net, s), "the same situation merges into its entry");
                float after = net.ImitationBank[slot * s.ImitationStride];
                if (Mathf.Abs(attempt - before) > 1e-5f)
                {
                    Assert.Greater(Mathf.Abs(attempt - before), Mathf.Abs(attempt - after) - 1e-7f, "moves toward the attempt");
                    Assert.AreEqual(Math.Sign(attempt - before), Math.Sign(after - before), "in the attempt's direction");
                }
            }
        }

        [Test]
        public void EveryImitationEntryFadesEachStep()
        {
            var s = Reward(false, true);
            s.imitationHalfLife = 1f;
            var net = new MnnCpu(s);
            var a = new[] { 0.9f, 0.9f };
            var b = new[] { -0.9f, -0.9f };
            for (int t = 0; t < 20; t++) { net.Step(t % 2 == 0 ? a : b); net.ReportError(0, 1f); }
            net.Step(a); net.ReportError(0, 0f);
            net.Step(b); net.ReportError(0, 0f);
            net.Step(a);
            Assert.AreEqual(2, UsedSlots(net, s), "two different situations, two entries");

            float fade = MathF.Exp(-s.TickDt / s.imitationHalfLife * 0.6931472f);
            var before = net.ImitationBank.ToArray();
            net.ReportError(0, 2f);   // a bad step: no writes, only fading
            net.Step(a);
            var after = net.ImitationBank;
            int stride = s.ImitationStride, n = s.outputCount;
            for (int slot = 0; slot < s.imitationSlots; slot++)
            {
                if (before[slot * stride + n + 2] < 0.5f) continue;
                Assert.AreEqual(before[slot * stride + n] * fade, after[slot * stride + n], 1e-6f, $"slot {slot} quality fades");
                Assert.AreEqual(before[slot * stride + n + 1] * fade, after[slot * stride + n + 1], 1e-6f, $"slot {slot} familiarity fades");
            }
        }

        [Test]
        public void TrajectoryCreditMarksTheStepsBeforeAGoodOne()
        {
            var s = Reward(false, true);
            s.imitationKeyFeedback = false;
            s.trajectoryCredit = true;
            s.creditSteps = 4;
            s.imitationMergeBits = 0;
            var net = new MnnCpu(s);
            var inputs = new[] { new[] { 0.9f, -0.9f }, new[] { -0.9f, 0.9f }, new[] { 0.1f, 0.8f }, new[] { -0.6f, -0.2f } };
            for (int t = 0; t < 20; t++) { net.Step(inputs[t % 4]); net.ReportError(0, 1f); }
            for (int t = 0; t < 4; t++) { net.Step(inputs[t]); net.ReportError(0, 1f); }
            net.ReportError(0, -1f);   // the last step was great: it and the three before it are credited
            net.Step(inputs[0]);
            Assert.AreEqual(4, net.Stats.ImitationWrites);
            Assert.AreEqual(4, UsedSlots(net, s), "four different situations");
            int stride = s.ImitationStride, n = s.outputCount;
            float strongest = 0f, weakest = float.MaxValue;
            for (int slot = 0; slot < s.imitationSlots; slot++)
            {
                if (net.ImitationBank[slot * stride + n + 2] < 0.5f) continue;
                strongest = Mathf.Max(strongest, net.ImitationBank[slot * stride + n]);
                weakest = Mathf.Min(weakest, net.ImitationBank[slot * stride + n]);
            }
            Assert.AreEqual(strongest * s.creditDecay * s.creditDecay * s.creditDecay, weakest, 1e-4f, "credit fades per step back");
        }

        [Test]
        public void IdentityFollowsBitsThatBeatTheBaseline()
        {
            var s = Reward(true, false);
            s.identityRate = 0.2f;
            var net = new MnnCpu(s);
            var x = new[] { 0.3f, -0.4f };
            for (int t = 0; t < 20; t++) { net.Step(x); net.ReportError(0, 1f); }

            for (int round = 0; round < 6; round++)
            {
                net.Step(x);
                var soft = (float[])net.IdentityMasks.Clone();
                var hard = net.IdentityExpression.ToArray();
                bool good = round % 2 == 0;
                net.ReportError(0, good ? 0f : 2f);
                net.Step(x);
                var now = net.IdentityMasks;
                for (int j = 0; j < soft.Length; j++)
                {
                    float before = Mathf.Abs(hard[j] - soft[j]), after = Mathf.Abs(hard[j] - now[j]);
                    if (before < 1e-6f) continue;
                    if (good) Assert.Less(after, before, $"a good step pulls identity {j} toward what it expressed");
                    else Assert.Greater(after, before - 1e-7f, $"a bad step pushes identity {j} away from what it expressed");
                    Assert.IsTrue(now[j] >= 0f && now[j] <= 1f);
                }
            }
        }

        [Test]
        public void IdentityMasksAreSeededPerInstanceAndChangeTheOutputs()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 3, headCount = 4, identityMasks = true };
            var net = new MnnCpu(s, 2) { Training = false };
            int per = s.headCount * s.IdentityWidth;
            Assert.AreEqual(2 * per, net.IdentityMasks.Length);
            bool differ = false;
            for (int j = 0; j < per; j++) differ |= net.IdentityMasks[j] != net.IdentityMasks[per + j];
            Assert.IsTrue(differ, "each instance gets its own identity");
            Assert.AreEqual(new MnnCpu(s, 2).IdentityMasks, net.IdentityMasks, "from the seed");

            var off = s.Clone();
            off.identityMasks = false;
            var plain = new MnnCpu(off) { Training = false };
            float difference = 0f;
            for (int t = 0; t < 50; t++)
            {
                var x = new[] { MathF.Sin(t * 0.3f), MathF.Cos(t * 0.2f), MathF.Sin(t * 0.3f), MathF.Cos(t * 0.2f) };
                net.Step(x);
                plain.Step(x.AsSpan(0, 2));
                difference = Mathf.Max(difference, Mathf.Abs(net.Outputs[0] - plain.Outputs[0]));
            }
            Assert.Greater(difference, 1e-4f, "the identity reshapes what heads read");
        }

        [Test]
        public void NewFeaturesOffLeaveTheLayoutAlone()
        {
            var s = new MnnSettings();
            var on = s.Clone();
            on.imitationEnabled = true;
            on.familiarityExploration = true;
            on.trajectoryCredit = true;
            Assert.AreEqual(s.LayoutSignature, on.LayoutSignature, "imitation keeps the weights' layout");
            Assert.AreEqual(new WeightLayout(s).Count, new WeightLayout(on).Count);
            on.identityMasks = true;
            Assert.AreNotEqual(s.LayoutSignature, on.LayoutSignature, "identity projections are weights");
            Assert.AreEqual(new WeightLayout(s).Count + s.headCount * s.outputCount * s.outputCount, new WeightLayout(on).Count);
        }

        [Test]
        public void FamiliarityShrinksExploration()
        {
            // Supervised, one fixed situation: imitation remembers it, familiarity rises, and so the nudges shrink.
            var s = new MnnSettings { inputCount = 2, outputCount = 1, headCount = 4, imitationEnabled = true, familiarityExploration = true, familiarExploreScale = 0.2f };
            var net = new MnnCpu(s);
            var x = new[] { 0.5f, -0.5f };
            float best = 0f;
            for (int t = 0; t < 400; t++)
            {
                net.Step(x, new[] { 0.25f });
                best = Mathf.Max(best, net.Stats.Familiarity);
            }
            Assert.Greater(best, 0.2f, "a well-remembered situation is familiar");
            Assert.IsTrue(float.IsFinite(net.Outputs[0]));
        }

        [Test]
        public void StepsAllocateNothing([Values(TrainingMode.Supervised, TrainingMode.Sequential)] TrainingMode mode)
        {
            var s = new MnnSettings
            {
                inputCount = 3, outputCount = 2, headCount = 6, pointsPerSpace = 128, trainingMode = mode,
                wfcEnabled = true, memoryEnabled = true, identityMasks = true, imitationEnabled = true,
                familiarityExploration = true, trajectoryCredit = true,
            };
            var net = new MnnCpu(s);
            var x = new float[3];
            var y = new float[2];
            void Run(int steps, int from)
            {
                for (int t = from; t < from + steps; t++)
                {
                    x[0] = MathF.Sin(t * 0.1f); x[1] = MathF.Cos(t * 0.07f); x[2] = t % 20 == 0 ? 1f : 0f;
                    y[0] = x[1]; y[1] = -x[0];
                    if (mode == TrainingMode.Supervised) net.Step(x, y);
                    else
                    {
                        net.Step(x);
                        float e = (net.Outputs[0] - y[0]) * (net.Outputs[0] - y[0]);
                        net.ReportError(0, e);
                    }
                }
            }
            Run(200, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(200, 200);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "bytes allocated over 200 steps");
        }

        [Test]
        public void BrainKeepsTheIdentities()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 2, headCount = 4, identityMasks = true, trainingMode = TrainingMode.Population };
            using var a = new ManifoldNet(s, 3, preferCpu: true);
            for (int t = 0; t < 200; t++)
            {
                a.Step(new[] { Mathf.Sin(t), 1f, 0f, 1f, Mathf.Cos(t), 0f });
                for (int i = 0; i < 3; i++) a.ReportError(i, (t * 7 + i * 3) % 5 * 0.2f);
            }
            var brain = a.SaveBrain();
            Assert.AreEqual(3 * s.headCount * s.outputCount, brain.IdentityMasks.Length);
            using var b = new ManifoldNet(s, 3, brain: brain, preferCpu: true);
            CollectionAssert.AreEqual(a.GetIdentityMasks(), b.GetIdentityMasks());
            using var c = new ManifoldNet(s, 5, brain: brain, preferCpu: true);
            var masks = c.GetIdentityMasks();
            int per = s.headCount * s.outputCount;
            for (int j = 0; j < per; j++)
                Assert.AreEqual(brain.IdentityMasks[j], masks[3 * per + j], "more instances repeat the saved ones");
            UnityEngine.Object.DestroyImmediate(brain);
        }

        private static int UsedSlots(MnnCpu net, MnnSettings s)
        {
            int used = 0;
            for (int slot = 0; slot < s.imitationSlots; slot++)
                if (net.ImitationBank[slot * s.ImitationStride + s.outputCount + 2] > 0.5f) used++;
            return used;
        }

        private static int FirstUsed(MnnCpu net, MnnSettings s)
        {
            for (int slot = 0; slot < s.imitationSlots; slot++)
                if (net.ImitationBank[slot * s.ImitationStride + s.outputCount + 2] > 0.5f) return slot;
            return -1;
        }
    }
}
