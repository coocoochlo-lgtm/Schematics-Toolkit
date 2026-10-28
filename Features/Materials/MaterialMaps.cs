using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// A grey map: one number per texel, usually 0 to 1 (a mask: where rust goes, how worn an edge is). What flows
    /// along a Smart Material's Mask wires. Row 0 is the bottom of the texture (V 0), like Unity's textures.
    /// </summary>
    public sealed class MaskMap
    {
        public readonly int Width, Height;
        public readonly float[] Values;

        public MaskMap(int width, int height)
        {
            Width = width;
            Height = height;
            Values = new float[width * height];
        }

        /// <summary>
        /// Lets a number (a Parameter) wire into a mask input, where it's the same everywhere. (Only for the graph's
        /// wiring: the nodes read the number itself.)
        /// </summary>
        public static implicit operator MaskMap(float value) => Constant(1, 1, value);

        public static MaskMap Constant(int width, int height, float value)
        {
            var map = new MaskMap(width, height);
            if (value != 0f)
                Array.Fill(map.Values, value);
            return map;
        }

        public MaskMap Clone()
        {
            var copy = new MaskMap(Width, Height);
            Array.Copy(Values, copy.Values, Values.Length);
            return copy;
        }

        /// <summary>Sets every texel from a function of its index, rows in parallel.</summary>
        public void Fill(Func<int, float> value)
        {
            int width = Width;
            var values = Values;
            Parallel.For(0, Height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                    values[row + x] = value(row + x);
            });
        }
    }

    /// <summary>
    /// A material, texel by texel: base colour, metallic, roughness, height and occlusion. What flows along a Smart
    /// Material's Material wires; the Output bakes it into textures. Colours are as picked (sRGB); height is 0 to 1
    /// with 0.5 the surface itself (above it raised, below it sunk in), turned into a normal map when baked.
    /// </summary>
    public sealed class MaterialMaps
    {
        public readonly int Width, Height;
        public readonly Color[] BaseColor;
        public readonly float[] Metallic, Roughness, HeightMap, Occlusion;

        public MaterialMaps(int width, int height)
        {
            Width = width;
            Height = height;
            int count = width * height;
            BaseColor = new Color[count];
            Metallic = new float[count];
            Roughness = new float[count];
            HeightMap = new float[count];
            Occlusion = new float[count];
        }

        public int Count => BaseColor.Length;

        /// <summary>The same material everywhere.</summary>
        public static MaterialMaps Fill(int width, int height, Color color, float metallic, float roughness, float heightValue = 0.5f)
        {
            var maps = new MaterialMaps(width, height);
            Array.Fill(maps.BaseColor, color);
            Array.Fill(maps.Metallic, metallic);
            Array.Fill(maps.Roughness, roughness);
            Array.Fill(maps.HeightMap, heightValue);
            Array.Fill(maps.Occlusion, 1f);
            return maps;
        }

        /// <summary>A plain grey material (what an input with nothing wired in gives).</summary>
        public static MaterialMaps Default(int width, int height) => Fill(width, height, new Color(0.5f, 0.5f, 0.5f, 1f), 0f, 0.5f);

        public MaterialMaps Clone()
        {
            var copy = new MaterialMaps(Width, Height);
            Array.Copy(BaseColor, copy.BaseColor, Count);
            Array.Copy(Metallic, copy.Metallic, Count);
            Array.Copy(Roughness, copy.Roughness, Count);
            Array.Copy(HeightMap, copy.HeightMap, Count);
            Array.Copy(Occlusion, copy.Occlusion, Count);
            return copy;
        }

        /// <summary>Runs <paramref name="texel"/> on every texel, rows in parallel.</summary>
        public void ForEach(Action<int> texel)
        {
            int width = Width;
            Parallel.For(0, Height, y =>
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                    texel(row + x);
            });
        }
    }

    /// <summary>
    /// What's been painted by hand on a Paint node, at its own resolution: colour (with how much of it covers), and
    /// roughness and metallic (each with its coverage), a height offset (0.5 is none), and a mask to drive layers with.
    /// Stored as three textures next to the graph (see <see cref="PaintNode"/>).
    /// </summary>
    public sealed class PaintLayer
    {
        public readonly int Size;
        /// <summary>The colour painted, and (alpha) how much of it covers what's under.</summary>
        public readonly Color[] Colour;
        public readonly float[] Roughness, RoughnessCover, Metallic, MetallicCover;
        /// <summary>Raised (above 0.5) or sunk (below) by this much.</summary>
        public readonly float[] HeightOffset;
        public readonly float[] Mask;

        public PaintLayer(int size)
        {
            Size = size;
            int count = size * size;
            Colour = new Color[count];
            Roughness = new float[count];
            RoughnessCover = new float[count];
            Metallic = new float[count];
            MetallicCover = new float[count];
            HeightOffset = new float[count];
            Array.Fill(HeightOffset, 0.5f);
            Mask = new float[count];
        }

        public PaintLayer Clone()
        {
            var copy = new PaintLayer(Size);
            Array.Copy(Colour, copy.Colour, Colour.Length);
            Array.Copy(Roughness, copy.Roughness, Roughness.Length);
            Array.Copy(RoughnessCover, copy.RoughnessCover, RoughnessCover.Length);
            Array.Copy(Metallic, copy.Metallic, Metallic.Length);
            Array.Copy(MetallicCover, copy.MetallicCover, MetallicCover.Length);
            Array.Copy(HeightOffset, copy.HeightOffset, HeightOffset.Length);
            Array.Copy(Mask, copy.Mask, Mask.Length);
            return copy;
        }

        public void CopyFrom(PaintLayer other)
        {
            if (other == null || other.Size != Size)
                return;
            Array.Copy(other.Colour, Colour, Colour.Length);
            Array.Copy(other.Roughness, Roughness, Roughness.Length);
            Array.Copy(other.RoughnessCover, RoughnessCover, RoughnessCover.Length);
            Array.Copy(other.Metallic, Metallic, Metallic.Length);
            Array.Copy(other.MetallicCover, MetallicCover, MetallicCover.Length);
            Array.Copy(other.HeightOffset, HeightOffset, HeightOffset.Length);
            Array.Copy(other.Mask, Mask, Mask.Length);
        }

        /// <summary>Whether anything's been painted.</summary>
        public bool IsEmpty
        {
            get
            {
                for (int i = 0; i < Mask.Length; i++)
                    if (Colour[i].a > 0f || RoughnessCover[i] > 0f || MetallicCover[i] > 0f || HeightOffset[i] != 0.5f || Mask[i] > 0f)
                        return false;
                return true;
            }
        }

        // Stored as three textures: colour (RGB, A its cover); surface (R roughness, G its cover, B metallic, A its
        // cover); and height and mask (R height offset, G mask).

        /// <summary>The three textures' pixels (colour, surface, height and mask).</summary>
        public (Color32[] colour, Color32[] surface, Color32[] heightMask) ToPixels()
        {
            int count = Size * Size;
            var colour = new Color32[count];
            var surface = new Color32[count];
            var heightMask = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                colour[i] = Colour[i];
                surface[i] = new Color(Roughness[i], RoughnessCover[i], Metallic[i], MetallicCover[i]);
                heightMask[i] = new Color(HeightOffset[i], Mask[i], 0f, 1f);
            }
            return (colour, surface, heightMask);
        }

        /// <summary>Reads it back from the three textures' pixels (any of them null: that part left blank).</summary>
        public static PaintLayer FromPixels(int size, Color[] colour, Color[] surface, Color[] heightMask)
        {
            var layer = new PaintLayer(size);
            int count = size * size;
            for (int i = 0; i < count; i++)
            {
                if (colour != null && i < colour.Length)
                    layer.Colour[i] = colour[i];
                if (surface != null && i < surface.Length)
                {
                    layer.Roughness[i] = surface[i].r;
                    layer.RoughnessCover[i] = surface[i].g;
                    layer.Metallic[i] = surface[i].b;
                    layer.MetallicCover[i] = surface[i].a;
                }
                if (heightMask != null && i < heightMask.Length)
                {
                    layer.HeightOffset[i] = heightMask[i].r;
                    layer.Mask[i] = heightMask[i].g;
                }
            }
            return layer;
        }
    }

    /// <summary>Reading maps of one size at another (bilinear, clamped).</summary>
    public static class MapSampling
    {
        /// <summary>Where texel <paramref name="index"/> of a <paramref name="width"/>×<paramref name="height"/> map falls in a <paramref name="size"/>² one.</summary>
        public static float Sample(float[] values, int size, int width, int height, int index)
        {
            if (size == width && size == height)
                return values[index];
            float u = ((index % width) + 0.5f) / width * size - 0.5f;
            float v = ((index / width) + 0.5f) / height * size - 0.5f;
            int x0 = Mathf.Clamp((int)Mathf.Floor(u), 0, size - 1), y0 = Mathf.Clamp((int)Mathf.Floor(v), 0, size - 1);
            int x1 = Mathf.Min(x0 + 1, size - 1), y1 = Mathf.Min(y0 + 1, size - 1);
            float fx = Mathf.Clamp01(u - x0), fy = Mathf.Clamp01(v - y0);
            float a = Mathf.Lerp(values[y0 * size + x0], values[y0 * size + x1], fx);
            float b = Mathf.Lerp(values[y1 * size + x0], values[y1 * size + x1], fx);
            return Mathf.Lerp(a, b, fy);
        }

        public static Color Sample(Color[] values, int size, int width, int height, int index)
        {
            if (size == width && size == height)
                return values[index];
            float u = ((index % width) + 0.5f) / width * size - 0.5f;
            float v = ((index / width) + 0.5f) / height * size - 0.5f;
            int x0 = Mathf.Clamp((int)Mathf.Floor(u), 0, size - 1), y0 = Mathf.Clamp((int)Mathf.Floor(v), 0, size - 1);
            int x1 = Mathf.Min(x0 + 1, size - 1), y1 = Mathf.Min(y0 + 1, size - 1);
            float fx = Mathf.Clamp01(u - x0), fy = Mathf.Clamp01(v - y0);
            var a = Color.Lerp(values[y0 * size + x0], values[y0 * size + x1], fx);
            var b = Color.Lerp(values[y1 * size + x0], values[y1 * size + x1], fx);
            return Color.Lerp(a, b, fy);
        }
    }
}
