using NUnit.Framework;
using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Tests
{
    /// <summary>The optional modules on the CPU reference: the WFC plan grid (W1-W3) and the episodic memory bank (E1), and saving them (M6).</summary>
    public class ModuleTests
    {
        [Test]
        public void PlanGridStaysFiniteAndCommits([Values(WfcAxis.Heads, WfcAxis.Time, WfcAxis.Grid)] WfcAxis axis)
        {
            // Tiny dispersion: most types have no points near a head, so committed heads often fall back.
            var s = new MnnSettings { inputCount = 2, outputCount = 2, headCount = 6, pointsPerSpace = 48, dispersion = 0.05f, wfcEnabled = true, wfcAxis = axis, sineGain = 4f };
            var off = s.Clone();
            off.wfcEnabled = false;
            var net = new MnnCpu(s);
            var plain = new MnnCpu(off);
            int committed = 0;
            float difference = 0f;
            for (int t = 0; t < 1000; t++)
            {
                var x = new[] { MathF.Sin(t * 0.2f), MathF.Cos(t * 0.13f) };
                var y = new[] { MathF.Sin(t * 0.2f + 1f), (t / 10) % 2 == 0 ? 1f : -1f };
                net.Step(x, y);
                plain.Step(x, y);
                Assert.IsTrue(float.IsFinite(net.Outputs[0]) && float.IsFinite(net.Outputs[1]), $"step {t}");
                var state = net.PeekState(0);
                foreach (var q in state.PlanSpread) Assert.IsTrue(float.IsFinite(q) && q >= 0f);
                foreach (var type in state.PlanType)
                {
                    Assert.Less(type, s.typeCount);
                    if (type >= 0) committed++;
                }
                difference = Mathf.Max(difference, Mathf.Abs(net.Outputs[0] - plain.Outputs[0]));
            }
            Assert.Greater(committed, 0, "cells commit");
            Assert.Greater(difference, 1e-3f, "the toggle changes behaviour");
        }

        [Test]
        public void PlanRollsLeft()
        {
            var s = new MnnSettings { inputCount = 1, outputCount = 1, headCount = 2, wfcEnabled = true, wfcAxis = WfcAxis.Time, planColumns = 4, planTickSteps = 3, wfcCollapseBudget = 8 };
            var net = new MnnCpu(s) { Training = false };
            net.Step(new[] { 0.2f });
            net.Step(new[] { 0.2f });
            var before = net.CaptureState(0);
            net.Step(new[] { 0.2f });   // the third tick rolls the grid
            var after = net.PeekState(0);
            // Collapses happen before the roll, and the budget commits every cell on the first tick, so the roll is
            // a clean shift: each column takes the next one's type, and the horizon starts undecided.
            for (int h = 0; h < 2; h++)
            {
                for (int col = 0; col < 3; col++)
                    Assert.AreEqual(before.PlanType[h * 4 + col + 1], after.PlanType[h * 4 + col]);
                Assert.AreEqual(-1, after.PlanType[h * 4 + 3]);
            }
        }

        [Test]
        public void MemoryOnlyTheRealNetworkWrites()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 2, memoryEnabled = true, memorySlots = 8 };
            var net = new MnnCpu(s);
            var x = new[] { 0.4f, -0.2f };
            var y = new[] { 0.9f, -0.9f };
            for (int t = 0; t < 30; t++) net.Step(x, y);   // 17 copies per step, none of them write
            Assert.AreEqual(0, Used(net, s));
            net.Remember(0, y, 2f);
            for (int t = 0; t < 30; t++) net.Step(x, y);
            Assert.AreEqual(1, Used(net, s));
            Assert.AreEqual(1, net.Stats.MemoryWrites);
            Assert.Greater(net.Stats.MemoryConfidence, 0.99f, "the stored moment is recalled");
        }

        [Test]
        public void MemoryMergesNearIdenticalMoments()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 2, memoryEnabled = true, memorySlots = 8 };
            var net = new MnnCpu(s) { Training = false };
            var x = new[] { 0.4f, -0.2f };
            for (int t = 0; t < 60; t++) net.Step(x);
            net.Remember(0, new[] { 0.9f, -0.9f }, 1f);
            net.Step(x);
            net.Remember(0, new[] { 0.9f, -0.9f }, 1f);
            net.Step(x);
            Assert.AreEqual(1, Used(net, s));
            Assert.AreEqual(2, net.Stats.MemoryWrites);
            Assert.AreEqual(2f, net.MemoryBank[s.hiddenWidth + s.outputCount], 0.01f, "strengths add up");
        }

        [Test]
        public void MemoryReplacesTheWeakestWhenFull()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 1, memoryEnabled = true, memorySlots = 2, mergeThreshold = 2f };
            var net = new MnnCpu(s) { Training = false };
            net.Step(new[] { 0.1f, 0.2f });
            net.Remember(0, new[] { 1f }, 3f); net.Step(new[] { -0.5f, 0.9f });
            net.Remember(0, new[] { 1f }, 0.5f); net.Step(new[] { 0.7f, -0.3f });
            net.Remember(0, new[] { 1f }, 2f); net.Step(new[] { 0.0f, 0.0f });
            Assert.AreEqual(2, Used(net, s));
            int stride = s.MemoryStride, strength = s.hiddenWidth + s.outputCount;
            float a = net.MemoryBank[strength], b = net.MemoryBank[stride + strength];
            Assert.AreEqual(3f, Mathf.Max(a, b), 0.01f, "the strong memory stays");
            Assert.AreEqual(2f, Mathf.Min(a, b), 0.01f, "the weak one was replaced");
        }

        [Test]
        public void BrainRoundTripsWithRulebooksAndMemory()
        {
            var s = new MnnSettings { inputCount = 2, outputCount = 2, wfcEnabled = true, memoryEnabled = true };
            using var a = new ManifoldNet(s, preferCpu: true);
            for (int t = 0; t < 60; t++)
            {
                a.Step(new[] { Mathf.Sin(t), 1f }, new[] { 0.5f, -0.5f });
                if (t % 20 == 0) a.Remember(0, new[] { 0.5f, -0.5f }, 1f);
            }
            var brain = a.SaveBrain();
            using var b = new ManifoldNet(s, brain: brain, preferCpu: true);
            CollectionAssert.AreEqual(a.GetParameters(), b.GetParameters());
            CollectionAssert.AreEqual(a.GetMemoryBank(), b.GetMemoryBank());
            CollectionAssert.AreEqual(a.GetRulebooks(), b.GetRulebooks());
            Assert.Greater(a.GetRulebooks().Length, 0);
            CollectionAssert.AreEqual(a.GetPointTypes(), b.GetPointTypes(), "point types come from the seed");
            UnityEngine.Object.DestroyImmediate(brain);
        }

        private static int Used(MnnCpu net, MnnSettings s)
        {
            int used = 0, stride = s.MemoryStride;
            for (int i = 0; i < net.MemoryBank.Length / stride; i++)
                if (net.MemoryBank[i * stride + stride - 1] > 0.5f) used++;
            return used;
        }
    }
}
