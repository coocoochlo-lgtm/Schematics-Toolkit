using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>One texture a Smart Material bakes: its name ending (BaseColor, Normal, MaskMap...), pixels, and how it's read.</summary>
    public sealed class BakedTexture
    {
        public string Suffix;
        public int Width, Height;
        public Color32[] Pixels;
        /// <summary>Colour (sRGB) rather than data (linear).</summary>
        public bool IsColor;
        public bool IsNormalMap;
    }

    /// <summary>
    /// Turns a made material into the textures it bakes to (any thread): base colour, a normal map from the height,
    /// and roughness, metallic and occlusion packed the way the render pipeline reads them; colour spread past the UV
    /// islands' edges so seams don't show.
    /// </summary>
    public static class MaterialBaker
    {
        public const string BaseColor = "BaseColor", Normal = "Normal", MaskMap = "MaskMap", Metallic = "MetallicSmoothness",
            Occlusion = "Occlusion", ORM = "ORM", Roughness = "Roughness", MetallicOnly = "Metallic", Height = "Height";

        /// <summary>
        /// The textures for <paramref name="result"/>, packed as <paramref name="packing"/> (not Auto: the editor picks
        /// for its pipeline). <paramref name="heightDepth"/> is how deep (meters) height 0 to 1 is.
        /// </summary>
        public static List<BakedTexture> Bake(MaterialResult result, MaterialPacking packing, float heightDepth, bool occlusionBake, float occlusionDistance, int padding, bool writeHeight)
        {
            var maps = result.Maps;
            var surface = result.Surface;
            int width = maps.Width, height = maps.Height, count = width * height;

            // Where each texel takes its values from: itself if it's on the mesh; else the nearest one that is, within
            // the padding (none: itself).
            var from = Padding(surface, padding);

            float[] ao = occlusionBake && surface != null && !surface.Tiling ? surface.AmbientOcclusion(occlusionDistance, 32) : null;
            var normals = NormalsFromHeight(maps, surface, heightDepth);

            var baseColor = new Color32[count];
            var normal = new Color32[count];
            var metallic = new float[count];
            var smoothness = new float[count];
            var occlusion = new float[count];
            var heights = new float[count];
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    int s = from[i];
                    var c = maps.BaseColor[s];
                    c.a = 1f;
                    baseColor[i] = c;
                    var n = normals[s];
                    normal[i] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                    metallic[i] = Mathf.Clamp01(maps.Metallic[s]);
                    smoothness[i] = 1f - Mathf.Clamp01(maps.Roughness[s]);
                    occlusion[i] = Mathf.Clamp01(maps.Occlusion[s] * (ao != null ? ao[s] : 1f));
                    heights[i] = Mathf.Clamp01(maps.HeightMap[s]);
                }
            });

            var textures = new List<BakedTexture>
            {
                new() { Suffix = BaseColor, Width = width, Height = height, Pixels = baseColor, IsColor = true },
                new() { Suffix = Normal, Width = width, Height = height, Pixels = normal, IsNormalMap = true },
            };
            switch (packing)
            {
                case MaterialPacking.HDRPMaskMap:
                    textures.Add(Pack(MaskMap, width, height, i => new Color(metallic[i], occlusion[i], 1f, smoothness[i])));
                    break;
                case MaterialPacking.ORM:
                    textures.Add(Pack(ORM, width, height, i => new Color(occlusion[i], 1f - smoothness[i], metallic[i], 1f)));
                    break;
                case MaterialPacking.Separate:
                    textures.Add(Pack(MetallicOnly, width, height, i => Grey(metallic[i])));
                    textures.Add(Pack(Roughness, width, height, i => Grey(1f - smoothness[i])));
                    textures.Add(Pack(Occlusion, width, height, i => Grey(occlusion[i])));
                    break;
                default:
                    textures.Add(Pack(Metallic, width, height, i => new Color(metallic[i], metallic[i], metallic[i], smoothness[i])));
                    textures.Add(Pack(Occlusion, width, height, i => Grey(occlusion[i])));
                    break;
            }
            if (writeHeight)
                textures.Add(Pack(Height, width, height, i => Grey(heights[i])));
            return textures;
        }

        private static Color Grey(float v) => new(v, v, v, 1f);

        private static BakedTexture Pack(string suffix, int width, int height, Func<int, Color> pixel)
        {
            var pixels = new Color32[width * height];
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel(y * width + x);
            });
            return new BakedTexture { Suffix = suffix, Width = width, Height = height, Pixels = pixels };
        }

        /// <summary>
        /// Each texel's source: itself where it's on the mesh, else the nearest texel on the mesh within
        /// <paramref name="padding"/> steps (grown out a ring at a time), else itself.
        /// </summary>
        public static int[] Padding(MaterialSurface surface, int padding)
        {
            int width = surface.Width, height = surface.Height, count = width * height;
            var from = new int[count];
            var done = new bool[count];
            for (int i = 0; i < count; i++)
            {
                from[i] = i;
                done[i] = surface.Triangle[i] >= 0;
            }
            if (surface.Tiling)
                return from;
            var ring = new List<int>();
            for (int step = 0; step < padding; step++)
            {
                ring.Clear();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        if (done[i])
                            continue;
                        // A neighbour already filled (sides first, then corners).
                        int source = -1;
                        if (x > 0 && done[i - 1]) source = from[i - 1];
                        else if (x < width - 1 && done[i + 1]) source = from[i + 1];
                        else if (y > 0 && done[i - width]) source = from[i - width];
                        else if (y < height - 1 && done[i + width]) source = from[i + width];
                        else if (x > 0 && y > 0 && done[i - width - 1]) source = from[i - width - 1];
                        else if (x < width - 1 && y > 0 && done[i - width + 1]) source = from[i - width + 1];
                        else if (x > 0 && y < height - 1 && done[i + width - 1]) source = from[i + width - 1];
                        else if (x < width - 1 && y < height - 1 && done[i + width + 1]) source = from[i + width + 1];
                        if (source >= 0)
                        {
                            from[i] = source;
                            ring.Add(i);
                        }
                    }
                if (ring.Count == 0)
                    break;
                foreach (int i in ring)
                    done[i] = true;
            }
            return from;
        }

        /// <summary>
        /// A tangent-space normal per texel from the height's slope: height 0 to 1 is <paramref name="depth"/> meters,
        /// measured against how far a texel step goes on the mesh. Slopes aren't taken across UV island edges (a
        /// neighbour that's somewhere else on the mesh).
        /// </summary>
        public static Vector3[] NormalsFromHeight(MaterialMaps maps, MaterialSurface surface, float depth)
        {
            int width = maps.Width, height = maps.Height;
            var normals = new Vector3[width * height];
            var h = maps.HeightMap;
            bool tiling = surface == null || surface.Tiling;
            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (surface != null && surface.Triangle[i] < 0)
                    {
                        normals[i] = Vector3.forward;
                        continue;
                    }
                    var step = surface != null ? surface.TexelLengthAt(i) : new Vector2(1f / width, 1f / height);
                    float du = Slope(x, y, 1, 0, step.x), dv = Slope(x, y, 0, 1, step.y);
                    normals[i] = new Vector3(-du * depth, -dv * depth, 1f).normalized;
                }
            });
            return normals;

            // Height change per meter along one axis, from the neighbours that are next to this texel on the mesh.
            float Slope(int x, int y, int sx, int sy, float stepLength)
            {
                int i = y * width + x;
                int a = Neighbour(x - sx, y - sy, i, stepLength), b = Neighbour(x + sx, y + sy, i, stepLength);
                int steps = (a != i ? 1 : 0) + (b != i ? 1 : 0);
                if (steps == 0)
                    return 0f;
                return (h[b] - h[a]) / (steps * Mathf.Max(1e-7f, stepLength));
            }

            int Neighbour(int x, int y, int self, float stepLength)
            {
                if (tiling)
                    return ((y % height + height) % height) * width + ((x % width + width) % width);
                if (x < 0 || y < 0 || x >= width || y >= height)
                    return self;
                int n = y * width + x;
                if (surface.Triangle[n] < 0)
                    return self;
                // Somewhere else on the mesh (across a UV island edge): not a neighbour.
                float gap = (surface.Position[n] - surface.Position[self]).sqrMagnitude;
                float limit = stepLength * 3f + surface.Size * 1e-4f;
                return gap <= limit * limit ? n : self;
            }
        }
    }
}
