// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Materials
{
    // Generators: masks made from nothing but where each texel is on the mesh. Noise is read in 3D at the texel's
    // place, so it runs straight across UV seams; on a flat material it wraps round so the texture tiles.

    internal static class MaskMath
    {
        public static uint Seed(float seed, int salt = 0) => unchecked((uint)Mathf.RoundToInt(seed * 1000f) * 2654435761u + (uint)salt * 40503u + 1u);

        public static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Pushes values away from 0.5 (above 1) or towards it (below).</summary>
        public static float Contrast(float v, float contrast) => Mathf.Clamp01((v - 0.5f) * contrast + 0.5f);

        /// <summary>The cells across a tiling material's 1 m square at this many per meter (0: no wrapping).</summary>
        public static int Period(MaterialSurface surface, float cellsPerMeter) => surface.Tiling ? Mathf.Max(1, Mathf.RoundToInt(cellsPerMeter)) : 0;

        /// <summary>The scale used: whole cells across a tiling material, so it wraps cleanly.</summary>
        public static float Scale(MaterialSurface surface, float cellsPerMeter) => surface.Tiling ? Mathf.Max(1, Mathf.RoundToInt(cellsPerMeter)) : cellsPerMeter;
    }

    [SchematicDoc(@"{""name"": ""Noise"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Generators"", ""description"": ""Cloudy noise, 0 to 1: Scale blobs per meter, Octaves layers of finer detail, each Roughness times as strong (0.5 soft, 0.8 gritty). Stretch squashes it along an axis (X 4 makes streaks along X: brushed metal, wood grain). Contrast sharpens it towards black and white. Read in 3D on the model, so it runs across UV seams. Seed picks a different pattern."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""breaking up wear and grime"", ""colour and roughness variation"", ""streaks (with Stretch)""]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Noise", Path = "Generators"), Tags(MaterialNode.Tag)]
    public sealed class NoiseMaskNode : MaterialNode
    {
        [Tooltip("Blobs per meter: higher is finer.")]
        [Input("Scale", Editable = true)] public float Scale = 8f;
        [Tooltip("Layers of finer and finer detail (1 to 8).")]
        [Input("Octaves", Editable = true)] public int Octaves = 4;
        [Tooltip("How strong each finer layer is next to the last: 0.3 soft, 0.5 natural, 0.8 gritty.")]
        [Input("Roughness", Editable = true)] public float Roughness = 0.5f;
        [Tooltip("Squashes it along each axis: (4, 1, 1) makes streaks running along X.")]
        [Input("Stretch", Editable = true)] public Vector3 Stretch = Vector3.one;
        [Tooltip("Pushes it towards black and white (above 1) or grey (below).")]
        [Input("Contrast", Editable = true)] public float Contrast = 1f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float scale = Mathf.Max(0.001f, c.Number(this, "Scale", Scale));
            int octaves = Mathf.Clamp(c.Integer(this, "Octaves", Octaves), 1, 8);
            float roughness = Mathf.Clamp(c.Number(this, "Roughness", Roughness), 0f, 1f);
            var stretch = c.Vector(this, "Stretch", Stretch);
            float contrast = c.Number(this, "Contrast", Contrast);
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed));
            return () =>
            {
                var surface = c.Surface;
                var mask = new MaskMap(c.Width, c.Height);
                // Along each axis: whole cells across a tiling material.
                float sx = MaskMath.Scale(surface, scale * Mathf.Max(0.001f, 1f / Mathf.Max(0.001f, Mathf.Abs(stretch.x))));
                float sy = scale / Mathf.Max(0.001f, Mathf.Abs(stretch.y));
                float sz = MaskMath.Scale(surface, scale * Mathf.Max(0.001f, 1f / Mathf.Max(0.001f, Mathf.Abs(stretch.z))));
                int px = surface.Tiling ? Mathf.RoundToInt(sx) : 0, pz = surface.Tiling ? Mathf.RoundToInt(sz) : 0;
                var offset = new Vector3(0.5f, 0f, 0.5f);
                mask.Fill(i =>
                {
                    var p = surface.Position[i] + (surface.Tiling ? offset : Vector3.zero);
                    float v = MaterialNoise.Fractal(new Vector3(p.x * sx, p.y * sy, p.z * sz), seed, octaves, roughness, px, pz);
                    return MaskMath.Contrast(v, contrast);
                });
                return mask;
            };
        }
    }

    public enum CellsMode { Cells, Distance, Bumps, Cracks }

    [SchematicDoc(@"{""name"": ""Cells"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Generators"", ""description"": ""Cell patterns (Voronoi), Scale cells per meter. Mode Cells gives each cell its own grey (tiles, stones, patches); Distance is dark at each cell's middle and light at its edge; Bumps the other way round (pebbles, blisters); Cracks draws lines Width wide where cells meet (dried mud, crazed paint). Jitter 0 lines the cells up in a grid, 1 scatters them."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""stones, tiles and patches"", ""cracks"", ""pitting and blisters""]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Cells", Path = "Generators"), Tags(MaterialNode.Tag)]
    public sealed class CellsMaskNode : MaterialNode
    {
        [Tooltip("Cells: a grey per cell. Distance: dark middles. Bumps: light middles. Cracks: lines where cells meet.")]
        [Editable] public CellsMode Mode = CellsMode.Cells;
        [Tooltip("Cells per meter: higher is smaller cells.")]
        [Input("Scale", Editable = true)] public float Scale = 10f;
        [Tooltip("0 lines the cells up in a grid; 1 scatters them.")]
        [Input("Jitter", Editable = true)] public float Jitter = 1f;
        [Tooltip("How wide the cracks are, as a share of a cell (Cracks).")]
        [Input("Width", Editable = true)] public float Width = 0.05f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var mode = Mode;
            float scale = Mathf.Max(0.001f, c.Number(this, "Scale", Scale));
            float jitter = Mathf.Clamp01(c.Number(this, "Jitter", Jitter));
            float width = Mathf.Max(0.0001f, c.Number(this, "Width", Width));
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed));
            return () =>
            {
                var surface = c.Surface;
                var mask = new MaskMap(c.Width, c.Height);
                float s = MaskMath.Scale(surface, scale);
                int period = MaskMath.Period(surface, scale);
                var offset = surface.Tiling ? new Vector3(0.5f, 0f, 0.5f) : Vector3.zero;
                mask.Fill(i =>
                {
                    MaterialNoise.Cells((surface.Position[i] + offset) * s, seed, jitter, out float f1, out float f2, out float cell, period, period);
                    return mode switch
                    {
                        CellsMode.Distance => Mathf.Clamp01(f1),
                        CellsMode.Bumps => Mathf.Clamp01(1f - f1),
                        CellsMode.Cracks => 1f - MaskMath.Smoothstep(width * 0.5f, width, f2 - f1),
                        _ => cell,
                    };
                });
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Scratches"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Generators"", ""description"": ""Thin straight scratches scattered over the model: Scale per meter, each Length long (a share of the space between them) and Width wide, Amount of them (0 none, 1 everywhere). Direction makes them run one way (with Spread 0) or any way (Spread 1)."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""scuffed metal and plastic"", ""wear on floors and handles""]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Scratches", Path = "Generators"), Tags(MaterialNode.Tag)]
    public sealed class ScratchesMaskNode : MaterialNode
    {
        [Tooltip("How many per meter (the space between them).")]
        [Input("Scale", Editable = true)] public float Scale = 12f;
        [Tooltip("How many of the places get one: 0 none, 1 all.")]
        [Input("Amount", Editable = true)] public float Amount = 0.5f;
        [Tooltip("How long each is, as a share of the space between them (up to 2).")]
        [Input("Length", Editable = true)] public float Length = 1.2f;
        [Tooltip("How wide each is, as a share of the space between them.")]
        [Input("Width", Editable = true)] public float Width = 0.02f;
        [Tooltip("The way they mostly run.")]
        [Input("Direction", Editable = true)] public Vector3 Direction = Vector3.right;
        [Tooltip("0: all along Direction. 1: any way at all.")]
        [Input("Spread", Editable = true)] public float Spread = 1f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float scale = Mathf.Max(0.001f, c.Number(this, "Scale", Scale));
            float amount = Mathf.Clamp01(c.Number(this, "Amount", Amount));
            float length = Mathf.Clamp(c.Number(this, "Length", Length), 0.01f, 2f);
            float width = Mathf.Max(0.0005f, c.Number(this, "Width", Width));
            var direction = c.Vector(this, "Direction", Direction);
            direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.right;
            float spread = Mathf.Clamp01(c.Number(this, "Spread", Spread));
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed));
            return () =>
            {
                var surface = c.Surface;
                var mask = new MaskMap(c.Width, c.Height);
                float s = MaskMath.Scale(surface, scale);
                int period = MaskMath.Period(surface, scale);
                var offset = surface.Tiling ? new Vector3(0.5f, 0f, 0.5f) : Vector3.zero;
                mask.Fill(i =>
                {
                    var p = (surface.Position[i] + offset) * s;
                    int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y), cz = Mathf.FloorToInt(p.z);
                    float best = 0f;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int x = cx + dx, y = cy + dy, z = cz + dz;
                        int hx = period > 0 ? ((x % period) + period) % period : x;
                        int hz = period > 0 ? ((z % period) + period) % period : z;
                        if (MaterialNoise.Random01(hx, y, hz, seed) > amount)
                            continue;
                        var centre = new Vector3(x + MaterialNoise.Random01(hx, y, hz, seed + 1u), y + MaterialNoise.Random01(hx, y, hz, seed + 2u), z + MaterialNoise.Random01(hx, y, hz, seed + 3u));
                        var random = new Vector3(MaterialNoise.Random01(hx, y, hz, seed + 4u) - 0.5f, MaterialNoise.Random01(hx, y, hz, seed + 5u) - 0.5f, MaterialNoise.Random01(hx, y, hz, seed + 6u) - 0.5f);
                        var along = Vector3.Lerp(direction, random.normalized, spread).normalized;
                        float half = length * 0.5f * (0.5f + MaterialNoise.Random01(hx, y, hz, seed + 7u));
                        var a = centre - along * half;
                        var ab = along * (half * 2f);
                        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                        float d = (p - (a + ab * t)).magnitude;
                        // Thinner towards its ends.
                        float w = width * (1f - Mathf.Abs(t - 0.5f) * 1.2f);
                        if (d < w)
                            best = Mathf.Max(best, (1f - d / w) * (0.6f + 0.4f * MaterialNoise.Random01(hx, y, hz, seed + 8u)));
                    }
                    return best;
                });
                return mask;
            };
        }
    }

    public enum GradientAxis { X, Y, Z }

    [SchematicDoc(@"{""name"": ""Gradient"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Generators"", ""description"": ""A ramp across the model along an axis, in its bounds: 0 at From and 1 at To (0 to 1 from its bottom to its top on Y, say). From 0.3, To 0: light at the bottom fading out by a third of the way up, for dirt splashed up from the ground."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""dirt at the bottom, sun-bleaching at the top"", ""a colour fade along a model""]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Gradient", Path = "Generators"), Tags(MaterialNode.Tag)]
    public sealed class GradientMaskNode : MaterialNode
    {
        [Tooltip("The axis it runs along (the mesh's own).")]
        [Editable] public GradientAxis Axis = GradientAxis.Y;
        [Tooltip("Where it's 0, across the model's bounds (0 its low side, 1 its high).")]
        [Input("From", Editable = true)] public float From;
        [Tooltip("Where it's 1, across the model's bounds.")]
        [Input("To", Editable = true)] public float To = 1f;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            int axis = (int)Axis;
            float from = c.Number(this, "From", From);
            float to = c.Number(this, "To", To);
            return () =>
            {
                var surface = c.Surface;
                var bounds = surface.Bounds;
                float min = bounds.min[axis], size = Mathf.Max(1e-6f, bounds.size[axis]);
                var mask = new MaskMap(c.Width, c.Height);
                mask.Fill(i =>
                {
                    float t = (surface.Position[i][axis] - min) / size;
                    return Mathf.Abs(to - from) < 1e-6f ? (t >= from ? 1f : 0f) : Mathf.Clamp01((t - from) / (to - from));
                });
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Facing"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Generators"", ""description"": ""How much the surface faces Direction: 1 facing it square on, 0 side-on or away. Spread widens it (1: anything not facing away), Softness blurs its edge. Up (0, 1, 0) picks out tops for dust, snow and moss."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""dust, snow and moss on top"", ""sun-bleaching on one side""]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Facing", Path = "Generators"), Tags(MaterialNode.Tag)]
    public sealed class FacingMaskNode : MaterialNode
    {
        [Tooltip("The way it picks out (the mesh's own space): up is (0, 1, 0).")]
        [Input("Direction", Editable = true)] public Vector3 Direction = Vector3.up;
        [Tooltip("How far round from Direction still counts: 0 only facing it square on, 1 anything not facing away.")]
        [Input("Spread", Editable = true)] public float Spread = 0.4f;
        [Tooltip("How soft its edge is.")]
        [Input("Softness", Editable = true)] public float Softness = 0.2f;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var direction = c.Vector(this, "Direction", Direction);
            direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up;
            float spread = Mathf.Clamp01(c.Number(this, "Spread", Spread));
            float softness = Mathf.Max(0.001f, c.Number(this, "Softness", Softness));
            return () =>
            {
                var surface = c.Surface;
                var mask = new MaskMap(c.Width, c.Height);
                float edge = 1f - spread;
                mask.Fill(i => MaskMath.Smoothstep(edge - softness * 0.5f, edge + softness * 0.5f, Vector3.Dot(surface.Normal[i], direction)));
                return mask;
            };
        }
    }
}
