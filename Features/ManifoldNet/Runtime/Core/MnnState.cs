using System;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// One instance's recurrent memory: where its heads are, how they're moving and where they're being pulled, what
    /// they've passed through, what it last said, and its shared plan (when the WFC grid is on). Training snapshots it
    /// at the start of every evaluation window so nudged copies can't overwrite the real network's memory.
    /// </summary>
    [Serializable]
    public sealed class MnnState
    {
        /// <summary>H × D each: position p, velocity v (per nominal tick), the Hopfield attractor from the last sample, and the master's last target.</summary>
        public float[] Positions, Velocities, Attractors, Targets;
        /// <summary>Each head's last liquid gate g (for the debug view; recomputed every step).</summary>
        public float[] Gates;
        /// <summary>H × N: what each head read this step (s), and its running average (the trail).</summary>
        public float[] Samples, Trails;
        public float[] Feedback, PreviousInputs;
        /// <summary>The WFC plan grid, H × F cells (cell = head × F + column): a type spread (T floats), the committed type (-1 = undecided) and its age.</summary>
        public float[] PlanSpread;
        public int[] PlanType, PlanAge;
        /// <summary>The last step's memory key (the master's last hidden activations) and recall weights per slot.</summary>
        public float[] MemoryKey, MemoryAttention;
        public uint Step;

        public MnnState(MnnSettings s)
        {
            int hd = s.headCount * s.spaceDimensions;
            Positions = new float[hd];
            Velocities = new float[hd];
            Attractors = new float[hd];
            Targets = new float[hd];
            Gates = new float[s.headCount];
            Samples = new float[s.headCount * s.outputCount];
            Trails = new float[s.headCount * s.outputCount];
            Feedback = new float[s.outputCount];
            PreviousInputs = new float[s.inputCount];
            PlanSpread = new float[s.PlanCells * (s.wfcEnabled ? s.typeCount : 0)];
            PlanType = new int[s.PlanCells];
            PlanAge = new int[s.PlanCells];
            MemoryKey = new float[s.memoryEnabled ? s.hiddenWidth : 0];
            MemoryAttention = new float[s.memoryEnabled ? s.memorySlots : 0];
        }

        public void CopyFrom(MnnState other)
        {
            Array.Copy(other.Positions, Positions, Positions.Length);
            Array.Copy(other.Velocities, Velocities, Velocities.Length);
            Array.Copy(other.Attractors, Attractors, Attractors.Length);
            Array.Copy(other.Targets, Targets, Targets.Length);
            Array.Copy(other.Gates, Gates, Gates.Length);
            Array.Copy(other.Samples, Samples, Samples.Length);
            Array.Copy(other.Trails, Trails, Trails.Length);
            Array.Copy(other.Feedback, Feedback, Feedback.Length);
            Array.Copy(other.PreviousInputs, PreviousInputs, PreviousInputs.Length);
            Array.Copy(other.PlanSpread, PlanSpread, Math.Min(PlanSpread.Length, other.PlanSpread.Length));
            Array.Copy(other.PlanType, PlanType, Math.Min(PlanType.Length, other.PlanType.Length));
            Array.Copy(other.PlanAge, PlanAge, Math.Min(PlanAge.Length, other.PlanAge.Length));
            Array.Copy(other.MemoryKey, MemoryKey, Math.Min(MemoryKey.Length, other.MemoryKey.Length));
            Array.Copy(other.MemoryAttention, MemoryAttention, Math.Min(MemoryAttention.Length, other.MemoryAttention.Length));
            Step = other.Step;
        }

        /// <summary>
        /// Every value as one flat array, in the GPU staging order: positions, velocities, attractors, targets, gates,
        /// samples, trails, feedback, previous inputs, plan spreads, plan types, plan ages, memory key, memory attention, step.
        /// </summary>
        public int FlatLength => Positions.Length * 4 + Gates.Length + Samples.Length + Trails.Length + Feedback.Length + PreviousInputs.Length
            + PlanSpread.Length + PlanType.Length * 2 + MemoryKey.Length + MemoryAttention.Length + 1;

        public void Pack(float[] data)
        {
            int k = 0;
            void Put(float[] a) { Array.Copy(a, 0, data, k, a.Length); k += a.Length; }
            Put(Positions); Put(Velocities); Put(Attractors); Put(Targets); Put(Gates); Put(Samples); Put(Trails); Put(Feedback); Put(PreviousInputs);
            Put(PlanSpread);
            for (int i = 0; i < PlanType.Length; i++) data[k++] = BitConverter.Int32BitsToSingle(PlanType[i]);
            for (int i = 0; i < PlanAge.Length; i++) data[k++] = BitConverter.Int32BitsToSingle(PlanAge[i]);
            Put(MemoryKey); Put(MemoryAttention);
            data[k] = BitConverter.Int32BitsToSingle(unchecked((int)Step));
        }

        public void Unpack(float[] data)
        {
            int k = 0;
            void Get(float[] a) { Array.Copy(data, k, a, 0, a.Length); k += a.Length; }
            Get(Positions); Get(Velocities); Get(Attractors); Get(Targets); Get(Gates); Get(Samples); Get(Trails); Get(Feedback); Get(PreviousInputs);
            Get(PlanSpread);
            for (int i = 0; i < PlanType.Length; i++) PlanType[i] = BitConverter.SingleToInt32Bits(data[k++]);
            for (int i = 0; i < PlanAge.Length; i++) PlanAge[i] = BitConverter.SingleToInt32Bits(data[k++]);
            Get(MemoryKey); Get(MemoryAttention);
            Step = unchecked((uint)BitConverter.SingleToInt32Bits(data[k]));
        }
    }

    /// <summary>Read-only training statistics.</summary>
    public struct MnnStats
    {
        public float ErrorTrace, ErrorBaseline, Alpha;
        public int Updates, Revivals, Rollbacks;
        public uint Steps;
        /// <summary>How many times a surprise wiped the plan grid's future (WFC on).</summary>
        public int PlanResets;
        /// <summary>Memory writes so far (new slots and merges).</summary>
        public int MemoryWrites;
        /// <summary>Instance 0's last recall confidence c (memory on).</summary>
        public float MemoryConfidence;
        /// <summary>Instance 0's last imitation confidence c (imitation on).</summary>
        public float ImitationConfidence;
        /// <summary>The instances' mean familiarity last step (imitation or familiarity exploration on).</summary>
        public float Familiarity;
        /// <summary>Imitation writes so far (CPU only; the GPU makes them on the device and doesn't count them).</summary>
        public int ImitationWrites;
    }
}
