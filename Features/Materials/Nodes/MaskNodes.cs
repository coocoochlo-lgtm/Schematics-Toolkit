// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Materials
{
    // Masks from the mesh's shape (baked once per mesh and kept), smart masks built on them (edge wear, grime, dust),
    // and the tools to shape and mix masks.

    [SchematicDoc(@"{""name"": ""Ambient Occlusion"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Mesh"", ""description"": ""How open each spot is: 1 out in the open, darker in creases, corners and under overhangs, where light (and cleaning) can't reach. Distance is how far it looks for parts in the way (a share of the model's size); Rays how many directions it tries (more is smoother, slower). Contrast deepens it. Baked once for the mesh and kept. White is open: Levels with Invert gives a mask for the dark places."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""grime and dirt in hidden places"", ""darkening crevices""]}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Ambient Occlusion", Path = "Mesh"), Tags(MaterialNode.Tag)]
    public sealed class OcclusionMaskNode : MaterialNode
    {
        [Tooltip("How far it looks for parts in the way, as a share of the model's size.")]
        [Editable] public float Distance = 0.15f;
        [Tooltip("Directions tried per texel: more is smoother but slower (8 to 128).")]
        [Editable] public int Rays = 32;
        [Tooltip("Deepens the dark (above 1) or lightens it (below).")]
        [Input("Contrast", Editable = true)] public float Contrast = 1f;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float distance = Distance;
            int rays = Rays;
            float contrast = Mathf.Max(0.01f, c.Number(this, "Contrast", Contrast));
            return () =>
            {
                var ao = c.Surface.AmbientOcclusion(distance, rays);
                var mask = new MaskMap(c.Width, c.Height);
                mask.Fill(i => Mathf.Pow(Mathf.Clamp01(ao[i]), contrast));
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Thickness"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Mesh"", ""description"": ""How thin the model is at each spot: 1 where it's thin (ears, fins, leaf edges, cloth hems), 0 where it's thick. Distance is how deep it looks (a share of the model's size). Good for light glowing through (subsurface) and for wear on thin edges."", ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""subsurface glow on thin parts"", ""translucency and fraying""]}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Thickness", Path = "Mesh"), Tags(MaterialNode.Tag)]
    public sealed class ThicknessMaskNode : MaterialNode
    {
        [Tooltip("How deep it looks into the model, as a share of its size.")]
        [Editable] public float Distance = 0.25f;
        [Tooltip("Directions tried per texel (8 to 128).")]
        [Editable] public int Rays = 24;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float distance = Distance;
            int rays = Rays;
            return () =>
            {
                var thin = c.Surface.Thinness(distance, rays);
                var mask = new MaskMap(c.Width, c.Height);
                Array.Copy(thin, mask.Values, Mathf.Min(thin.Length, mask.Values.Length));
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Curvature"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Mesh"", ""description"": ""Where the model's surface turns. Edges is light along corners that stick out (where paint chips and metal gets polished), Cavities along creases that dip in (where dirt collects). Edges sharper than Sharp Angle degrees are lines Width meters wide; gently rounded shapes (a subdivided mesh) add their bend, times Smooth. Baked once for the mesh and kept."", ""outputs"": [{""name"": ""Edges"", ""type"": ""MaskMap""}, {""name"": ""Cavities"", ""type"": ""MaskMap""}], ""use_for"": [""edge wear and chipped paint"", ""dirt in creases"", ""highlighting a model's shape""]}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Curvature", Path = "Mesh"), Tags(MaterialNode.Tag)]
    public sealed class CurvatureMaskNode : MaterialNode
    {
        [Tooltip("How wide (meters) the lines along sharp edges are.")]
        [Editable] public float Width = 0.01f;
        [Tooltip("Edges at least this sharp (degrees between the faces) count fully; from half of it they fade in.")]
        [Editable] public float SharpAngle = 30f;
        [Tooltip("How much gently rounded surfaces count (a subdivided shape has no sharp edges): 0 none.")]
        [Editable] public float Smooth = 0.5f;
        [Output("Edges")] [NonSerialized] public MaskMap Edges;
        [Output("Cavities")] [NonSerialized] public MaskMap Cavities;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float width = Width, angle = SharpAngle, smooth = Smooth;
            bool cavities = output == "Cavities";
            return () =>
            {
                var (edges, dips) = c.Surface.Curvature(width, angle, smooth);
                var mask = new MaskMap(c.Width, c.Height);
                Array.Copy(cavities ? dips : edges, mask.Values, mask.Values.Length);
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Edge Wear"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Smart Masks"", ""description"": ""Where a model's edges are worn through: along the corners that stick out, broken up by noise so it chips rather than running in neat lines. Amount is how far the wear spreads in from the edges (0 none, 1 all of Width); Width the widest it can get (meters); Breakup how much noise eats into it, at Breakup Scale per meter; Softness its edge. Wire it into a Layer's Mask with bare metal (or a lighter paint) on top."", ""inputs"": [{""name"": ""Amount"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""chipped paint on metal"", ""worn edges on wood and stone"", ""polished edges""]}")]
    [Serializable]
    [NodeColor(NodeColors.Condition)]
    [Node(Name = "Edge Wear", Path = "Smart Masks"), Tags(MaterialNode.Tag)]
    public sealed class EdgeWearMaskNode : MaterialNode
    {
        [Tooltip("How far the wear spreads in from the edges: 0 none, 1 the whole Width.")]
        [Input("Amount", Editable = true)] public float Amount = 0.5f;
        [Tooltip("The widest it can get, in from an edge (meters).")]
        [Editable] public float Width = 0.02f;
        [Tooltip("How much noise eats into it (0: clean lines, 1: patchy).")]
        [Input("Breakup", Editable = true)] public float Breakup = 0.6f;
        [Tooltip("The breakup noise's blobs per meter.")]
        [Input("Breakup Scale", Editable = true)] public float BreakupScale = 25f;
        [Tooltip("How soft the wear's edge is (0 crisp chips, 0.3 soft).")]
        [Input("Softness", Editable = true)] public float Softness = 0.05f;
        [Tooltip("Edges at least this sharp (degrees) wear fully.")]
        [Editable] public float SharpAngle = 30f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float amount = Mathf.Clamp01(c.Number(this, "Amount", Amount));
            float width = Mathf.Max(0.0005f, Width);
            float breakup = Mathf.Clamp01(c.Number(this, "Breakup", Breakup));
            float scale = Mathf.Max(0.01f, c.Number(this, "Breakup Scale", BreakupScale));
            float softness = Mathf.Max(0.002f, c.Number(this, "Softness", Softness));
            float angle = SharpAngle;
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed), 11);
            return () =>
            {
                var surface = c.Surface;
                var (edges, _) = surface.Curvature(width, angle, 1f);
                float s = MaskMath.Scale(surface, scale);
                int period = MaskMath.Period(surface, scale);
                var offset = surface.Tiling ? new Vector3(0.5f, 0f, 0.5f) : Vector3.zero;
                var mask = new MaskMap(c.Width, c.Height);
                float threshold = 1f - amount;
                mask.Fill(i =>
                {
                    float e = edges[i];
                    if (e <= 0f || amount <= 0f)
                        return 0f;
                    float n = MaterialNoise.Fractal((surface.Position[i] + offset) * s, seed, 4, 0.55f, period, period);
                    float v = e - (1f - n) * breakup;
                    return MaskMath.Smoothstep(threshold - softness, threshold + softness, v);
                });
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Grime"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Smart Masks"", ""description"": ""Where dirt builds up: in creases and cavities, in the dark places light (and cleaning) can't reach, and splashed up from the bottom, broken up by noise. Amount is how much (0 none, 1 everywhere); Cavity, Occlusion and Bottom how much each kind of place collects (Bottom Height is how far up the splashes reach, a share of the model's height); Breakup how much noise eats into it. Wire it into a Layer's Mask with a dark, rough material on top."", ""inputs"": [{""name"": ""Amount"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""dirt and soot"", ""rust streaks in crevices"", ""moss at the base of rocks""]}")]
    [Serializable]
    [NodeColor(NodeColors.Condition)]
    [Node(Name = "Grime", Path = "Smart Masks"), Tags(MaterialNode.Tag)]
    public sealed class GrimeMaskNode : MaterialNode
    {
        [Tooltip("How much grime: 0 none, 1 everywhere.")]
        [Input("Amount", Editable = true)] public float Amount = 0.5f;
        [Tooltip("How much creases and dips collect.")]
        [Editable] public float Cavity = 1f;
        [Tooltip("How much dark, hidden places (ambient occlusion) collect.")]
        [Editable] public float Occlusion = 1f;
        [Tooltip("How much is splashed up from the bottom.")]
        [Editable] public float Bottom = 0.5f;
        [Tooltip("How far up the splashes reach, as a share of the model's height.")]
        [Editable] public float BottomHeight = 0.3f;
        [Tooltip("How much noise eats into it.")]
        [Input("Breakup", Editable = true)] public float Breakup = 0.5f;
        [Tooltip("The breakup noise's blobs per meter.")]
        [Input("Breakup Scale", Editable = true)] public float BreakupScale = 10f;
        [Tooltip("How soft its edge is.")]
        [Input("Softness", Editable = true)] public float Softness = 0.25f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float amount = Mathf.Clamp01(c.Number(this, "Amount", Amount));
            float cavity = Cavity, occlusion = Occlusion, bottom = Bottom, bottomHeight = Mathf.Max(0.01f, BottomHeight);
            float breakup = Mathf.Clamp01(c.Number(this, "Breakup", Breakup));
            float scale = Mathf.Max(0.01f, c.Number(this, "Breakup Scale", BreakupScale));
            float softness = Mathf.Max(0.002f, c.Number(this, "Softness", Softness));
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed), 23);
            return () =>
            {
                var surface = c.Surface;
                var (_, dips) = surface.Curvature(Mathf.Max(0.005f, surface.Size * 0.02f), 30f, 1f);
                var ao = occlusion > 0f ? surface.AmbientOcclusion(0.15f, 24) : null;
                float min = surface.Bounds.min.y, height = Mathf.Max(1e-5f, surface.Bounds.size.y);
                float s = MaskMath.Scale(surface, scale);
                int period = MaskMath.Period(surface, scale);
                var offset = surface.Tiling ? new Vector3(0.5f, 0f, 0.5f) : Vector3.zero;
                var mask = new MaskMap(c.Width, c.Height);
                float threshold = 1f - amount;
                mask.Fill(i =>
                {
                    if (amount <= 0f)
                        return 0f;
                    float g = dips[i] * cavity;
                    if (ao != null)
                        g = Mathf.Max(g, (1f - ao[i]) * 1.5f * occlusion);
                    if (bottom > 0f && !surface.Tiling)
                        g = Mathf.Max(g, (1f - Mathf.Clamp01((surface.Position[i].y - min) / height / bottomHeight)) * bottom);
                    float n = MaterialNoise.Fractal((surface.Position[i] + offset) * s, seed, 4, 0.5f, period, period);
                    float v = Mathf.Clamp01(g) + (n - 0.5f) * breakup;
                    return MaskMath.Smoothstep(threshold - softness, threshold + softness, v + amount * 0.5f);
                });
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Dust"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Smart Masks"", ""description"": ""Where dust settles: on surfaces facing up (or Direction), less where they're exposed and steep, broken up by noise. Amount is how much (0 none, 1 thick everywhere it can settle). Also good for snow, sand and moss."", ""inputs"": [{""name"": ""Amount"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""dust on ledges and tops"", ""snow, sand and moss""]}")]
    [Serializable]
    [NodeColor(NodeColors.Condition)]
    [Node(Name = "Dust", Path = "Smart Masks"), Tags(MaterialNode.Tag)]
    public sealed class DustMaskNode : MaterialNode
    {
        [Tooltip("How much: 0 none, 1 thick everywhere it can settle.")]
        [Input("Amount", Editable = true)] public float Amount = 0.5f;
        [Tooltip("The way it falls from (up is (0, 1, 0)).")]
        [Input("Direction", Editable = true)] public Vector3 Direction = Vector3.up;
        [Tooltip("How much noise eats into it.")]
        [Input("Breakup", Editable = true)] public float Breakup = 0.4f;
        [Tooltip("The breakup noise's blobs per meter.")]
        [Input("Breakup Scale", Editable = true)] public float BreakupScale = 6f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            float amount = Mathf.Clamp01(c.Number(this, "Amount", Amount));
            var direction = c.Vector(this, "Direction", Direction);
            direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.up;
            float breakup = Mathf.Clamp01(c.Number(this, "Breakup", Breakup));
            float scale = Mathf.Max(0.01f, c.Number(this, "Breakup Scale", BreakupScale));
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed), 37);
            return () =>
            {
                var surface = c.Surface;
                float s = MaskMath.Scale(surface, scale);
                int period = MaskMath.Period(surface, scale);
                var offset = surface.Tiling ? new Vector3(0.5f, 0f, 0.5f) : Vector3.zero;
                var mask = new MaskMap(c.Width, c.Height);
                float threshold = 1f - amount;
                mask.Fill(i =>
                {
                    if (amount <= 0f)
                        return 0f;
                    float facing = Mathf.Clamp01(Vector3.Dot(surface.Normal[i], direction));
                    float n = MaterialNoise.Fractal((surface.Position[i] + offset) * s, seed, 5, 0.55f, period, period);
                    float v = facing * facing + (n - 0.5f) * breakup;
                    return MaskMath.Smoothstep(threshold - 0.15f, threshold + 0.15f, v);
                });
                return mask;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Levels"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Masks"", ""description"": ""Reshapes a mask, like Levels in a paint program: what's at In Low and below goes to Out Low, what's at In High and above to Out High, with Gamma bending the middle (below 1 darker, above lighter). Pull In Low and In High close together for a hard edge. Invert flips it (black to white)."", ""inputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Levels", Path = "Masks"), Tags(MaterialNode.Tag)]
    public sealed class LevelsMaskNode : MaterialNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MaskMap In;
        [Tooltip("This and below become Out Low.")]
        [Input("In Low", Editable = true)] public float InLow;
        [Tooltip("This and above become Out High.")]
        [Input("In High", Editable = true)] public float InHigh = 1f;
        [Tooltip("Bends the middle: below 1 darker, above 1 lighter.")]
        [Input("Gamma", Editable = true)] public float Gamma = 1f;
        [Input("Out Low", Editable = true)] public float OutLow;
        [Input("Out High", Editable = true)] public float OutHigh = 1f;
        [Tooltip("Flip it: black to white.")]
        [Editable] public bool Invert;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var input = c.Mask(this, "In", 0f);
            float inLow = c.Number(this, "In Low", InLow), inHigh = c.Number(this, "In High", InHigh);
            float gamma = Mathf.Max(0.01f, c.Number(this, "Gamma", Gamma));
            float outLow = c.Number(this, "Out Low", OutLow), outHigh = c.Number(this, "Out High", OutHigh);
            bool invert = Invert;
            return () =>
            {
                var source = input();
                var mask = new MaskMap(source.Width, source.Height);
                float range = Mathf.Max(1e-5f, inHigh - inLow);
                mask.Fill(i =>
                {
                    float v = Mathf.Clamp01((source.Values[i] - inLow) / range);
                    v = Mathf.Pow(v, 1f / gamma);
                    if (invert)
                        v = 1f - v;
                    return Mathf.Lerp(outLow, outHigh, v);
                });
                return mask;
            };
        }
    }

    public enum MaskBlend { Multiply, Add, Subtract, Lighten, Darken, Screen, Overlay, Difference }

    [SchematicDoc(@"{""name"": ""Blend Masks"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Masks"", ""description"": ""Mixes two masks: Multiply (only where both are: wear only on the top), Add, Subtract (A minus B: wear, but not where it's dusty), Lighten (either), Darken (both), Screen (a softer either), Overlay (B adds contrast to A), Difference. Opacity is how much of the mix is used over A as it was."", ""inputs"": [{""name"": ""A"", ""type"": ""MaskMap""}, {""name"": ""B"", ""type"": ""MaskMap""}], ""outputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Blend Masks", Path = "Masks"), Tags(MaterialNode.Tag)]
    public sealed class BlendMasksNode : MaterialNode
    {
        [Tooltip("The first mask (or a number, the same everywhere).")]
        [Input("A", typeof(MaskMap), Editable = true)] public float A;
        [Tooltip("The second mask (or a number, the same everywhere).")]
        [Input("B", typeof(MaskMap), Editable = true)] public float B = 1f;
        [Tooltip("How they mix: Multiply, Add, Subtract (A minus B), Lighten, Darken, Screen, Overlay, Difference.")]
        [Editable] public MaskBlend Mode = MaskBlend.Multiply;
        [Tooltip("How much of the mix is used over A as it was: 0 just A, 1 the whole mix.")]
        [Input("Opacity", Editable = true)] public float Opacity = 1f;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var a = c.Mask(this, "A", A);
            var b = c.Mask(this, "B", B);
            var mode = Mode;
            float opacity = Mathf.Clamp01(c.Number(this, "Opacity", Opacity));
            return () =>
            {
                var ma = a();
                var mb = b();
                var mask = new MaskMap(ma.Width, ma.Height);
                mask.Fill(i =>
                {
                    float x = ma.Values[i], y = mb.Values[i];
                    float v = mode switch
                    {
                        MaskBlend.Add => x + y,
                        MaskBlend.Subtract => x - y,
                        MaskBlend.Lighten => Mathf.Max(x, y),
                        MaskBlend.Darken => Mathf.Min(x, y),
                        MaskBlend.Screen => 1f - (1f - x) * (1f - y),
                        MaskBlend.Overlay => x < 0.5f ? 2f * x * y : 1f - 2f * (1f - x) * (1f - y),
                        MaskBlend.Difference => Mathf.Abs(x - y),
                        _ => x * y,
                    };
                    return Mathf.Lerp(x, Mathf.Clamp01(v), opacity);
                });
                return mask;
            };
        }
    }
}
