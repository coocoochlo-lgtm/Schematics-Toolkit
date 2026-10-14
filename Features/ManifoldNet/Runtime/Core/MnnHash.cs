namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// All of the network's randomness: a PCG hash of the seed and a few indices. The same function runs in the
    /// compute shader (ManifoldNet.compute), so the CPU and GPU make identical "random" numbers without storing any.
    /// </summary>
    public static class MnnHash
    {
        // Streams keep different uses of randomness apart.
        public const uint StreamWeights = 1;
        public const uint StreamPoints = 2;
        public const uint StreamValues = 3;
        public const uint StreamNoise = 4;
        public const uint StreamHeads = 5;
        public const uint StreamRevive = 6;
        // 7 and 8: breeding (MnnBreeding).
        public const uint StreamTypes = 9;
        public const uint StreamWfc = 10;
        public const uint StreamIdentity = 11;
        public const uint StreamImitation = 12;

        public static uint Pcg(uint v)
        {
            uint state = v * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            return (word >> 22) ^ word;
        }

        public static uint Hash(uint seed, uint stream, uint a, uint b, uint c)
            => Pcg(seed ^ Pcg(stream ^ Pcg(a ^ Pcg(b ^ Pcg(c)))));

        /// <summary>Uniform in [0, 1).</summary>
        public static float Unit(uint hash) => (hash >> 8) * (1f / 16777216f);

        /// <summary>Uniform in [-1, 1).</summary>
        public static float Signed(uint hash) => Unit(hash) * 2f - 1f;

        /// <summary>Uniform with variance 1: [-√3, √3).</summary>
        public static float Noise(uint hash) => Signed(hash) * 1.7320508f;
    }
}
