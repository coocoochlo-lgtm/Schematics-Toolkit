using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// 3D noise for Smart Materials, read at each texel's place on the mesh, so patterns run straight across UV seams.
    /// Each can wrap round every <c>period</c> cells on X and Z (0: never), which is how flat materials tile. Any thread.
    /// </summary>
    public static class MaterialNoise
    {
        private static uint Hash(int x, int y, int z, uint seed)
        {
            unchecked
            {
                uint h = seed * 0x9E3779B1u;
                h ^= (uint)x * 0x85EBCA77u;
                h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE3Du;
                h = (h << 13) | (h >> 19);
                h ^= (uint)z * 0x27D4EB2Fu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return h;
            }
        }

        private static int Wrap(int value, int period) => period > 0 ? ((value % period) + period) % period : value;

        /// <summary>A random number from 0 to 1 for a cell.</summary>
        public static float Random01(int x, int y, int z, uint seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / 16777216f;

        private static float Gradient(int x, int y, int z, uint seed, float fx, float fy, float fz)
        {
            // One of the 12 edge directions of a cube.
            switch (Hash(x, y, z, seed) % 12u)
            {
                case 0: return fx + fy;
                case 1: return -fx + fy;
                case 2: return fx - fy;
                case 3: return -fx - fy;
                case 4: return fx + fz;
                case 5: return -fx + fz;
                case 6: return fx - fz;
                case 7: return -fx - fz;
                case 8: return fy + fz;
                case 9: return -fy + fz;
                case 10: return fy - fz;
                default: return -fy - fz;
            }
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Smooth gradient (Perlin) noise, about -1 to 1.</summary>
        public static float Perlin(Vector3 p, uint seed, int periodX = 0, int periodZ = 0)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            int xa = Wrap(x0, periodX), xb = Wrap(x0 + 1, periodX);
            int za = Wrap(z0, periodZ), zb = Wrap(z0 + 1, periodZ);
            float u = Fade(fx), v = Fade(fy), w = Fade(fz);
            float n000 = Gradient(xa, y0, za, seed, fx, fy, fz);
            float n100 = Gradient(xb, y0, za, seed, fx - 1f, fy, fz);
            float n010 = Gradient(xa, y0 + 1, za, seed, fx, fy - 1f, fz);
            float n110 = Gradient(xb, y0 + 1, za, seed, fx - 1f, fy - 1f, fz);
            float n001 = Gradient(xa, y0, zb, seed, fx, fy, fz - 1f);
            float n101 = Gradient(xb, y0, zb, seed, fx - 1f, fy, fz - 1f);
            float n011 = Gradient(xa, y0 + 1, zb, seed, fx, fy - 1f, fz - 1f);
            float n111 = Gradient(xb, y0 + 1, zb, seed, fx - 1f, fy - 1f, fz - 1f);
            float x00 = Mathf.Lerp(n000, n100, u), x10 = Mathf.Lerp(n010, n110, u);
            float x01 = Mathf.Lerp(n001, n101, u), x11 = Mathf.Lerp(n011, n111, u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
        }

        /// <summary>
        /// Layers of Perlin noise (fractal), 0 to 1: <paramref name="octaves"/> layers, each twice as fine and
        /// <paramref name="roughness"/> as strong as the last.
        /// </summary>
        public static float Fractal(Vector3 p, uint seed, int octaves, float roughness, int periodX = 0, int periodZ = 0)
        {
            float sum = 0f, amplitude = 1f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Perlin(p, seed + (uint)o * 1013u, periodX, periodZ) * amplitude;
                total += amplitude;
                amplitude *= roughness;
                p *= 2f;
                periodX *= 2;
                periodZ *= 2;
            }
            return Mathf.Clamp01(sum / Mathf.Max(1e-5f, total) * 0.75f + 0.5f);
        }

        /// <summary>
        /// Cells (Voronoi): the distance to the nearest scattered point (<paramref name="f1"/>) and the next nearest
        /// (<paramref name="f2"/>), and a random number for the nearest point's cell. <paramref name="jitter"/> 0 is a
        /// grid, 1 fully scattered.
        /// </summary>
        public static void Cells(Vector3 p, uint seed, float jitter, out float f1, out float f2, out float cellValue, int periodX = 0, int periodZ = 0)
        {
            int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y), cz = Mathf.FloorToInt(p.z);
            f1 = f2 = float.MaxValue;
            cellValue = 0f;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int x = cx + dx, y = cy + dy, z = cz + dz;
                int hx = Wrap(x, periodX), hz = Wrap(z, periodZ);
                var point = new Vector3(
                    x + 0.5f + (Random01(hx, y, hz, seed) - 0.5f) * jitter,
                    y + 0.5f + (Random01(hx, y, hz, seed + 7u) - 0.5f) * jitter,
                    z + 0.5f + (Random01(hx, y, hz, seed + 13u) - 0.5f) * jitter);
                float d = (point - p).magnitude;
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    cellValue = Random01(hx, y, hz, seed + 29u);
                }
                else if (d < f2)
                    f2 = d;
            }
        }
    }
}
