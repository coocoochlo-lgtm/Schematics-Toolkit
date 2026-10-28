// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Materials
{
    // Materials: fills that make one, a fill driven by a mask, layers that stack one over another through a mask
    // (like a layer stack in Substance Painter), and adjustments.

    [SchematicDoc(@"{""name"": ""Fill"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Materials"", ""description"": ""A material, the same all over: Color, Metallic (0 paint, plastic, stone; 1 bare metal), Roughness (0 mirror-shiny, 1 chalky) and Height (0.5 the surface; higher stands up from it, lower sinks in). Variation adds a little natural unevenness (colour and roughness wandering, at Variation Scale blobs per meter), so it doesn't look flat and printed."", ""outputs"": [{""name"": ""Material"", ""type"": ""MaterialMaps""}], ""use_for"": [""a base paint, metal, plastic, stone"", ""the top of a Layer (rust, dirt, bare metal)""]}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Fill", Path = "Materials"), Tags(MaterialNode.Tag)]
    public sealed class FillMaterialNode : MaterialNode
    {
        [Tooltip("Its colour (albedo): with no light or shine in it.")]
        [Input("Color", Editable = true)] public Color Color = new(0.6f, 0.6f, 0.6f, 1f);
        [Tooltip("0 for paint, plastic, stone, wood; 1 for bare metal. In between is rarely right.")]
        [Input("Metallic", Editable = true)] public float Metallic;
        [Tooltip("0 mirror-shiny, 0.3 polished, 0.6 satin, 1 chalky.")]
        [Input("Roughness", Editable = true)] public float Roughness = 0.5f;
        [Tooltip("0.5 is the surface: higher stands up from it, lower sinks in (the Output turns it into the normal map).")]
        [Input("Height", Editable = true)] public float Height = 0.5f;
        [Tooltip("How much colour and roughness wander, for a natural look: 0 none, 0.3 subtle, 1 blotchy.")]
        [Input("Variation", Editable = true)] public float Variation = 0.2f;
        [Tooltip("The variation's blobs per meter.")]
        [Input("Variation Scale", Editable = true)] public float VariationScale = 6f;
        [Input("Seed", Editable = true)] public float Seed;
        [Output(MaterialPort)] [NonSerialized] public MaterialMaps Material;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var color = c.ColorValue(this, "Color", Color);
            float metallic = Mathf.Clamp01(c.Number(this, "Metallic", Metallic));
            float roughness = Mathf.Clamp01(c.Number(this, "Roughness", Roughness));
            float height = Mathf.Clamp01(c.Number(this, "Height", Height));
            float variation = Mathf.Clamp01(c.Number(this, "Variation", Variation));
            float scale = Mathf.Max(0.01f, c.Number(this, "Variation Scale", VariationScale));
            uint seed = MaskMath.Seed(c.Number(this, "Seed", Seed), 51);
            return () =>
            {
                var maps = MaterialMaps.Fill(c.Width, c.Height, color, metallic, roughness, height);
                if (variation <= 0f)
                    return maps;
                var surface = c.Surface;
                float s = MaskMath.Scale(surface, scale);
                int period = MaskMath.Period(surface, scale);
                var offset = surface.Tiling ? new Vector3(0.5f, 0f, 0.5f) : Vector3.zero;
                Color.RGBToHSV(color, out float h, out float sat, out float val);
                maps.ForEach(i =>
                {
                    var p = (surface.Position[i] + offset) * s;
                    float n1 = MaterialNoise.Fractal(p, seed, 4, 0.5f, period, period) - 0.5f;
                    float n2 = MaterialNoise.Fractal(p * 2f, seed + 99u, 3, 0.5f, period * 2, period * 2) - 0.5f;
                    var varied = Color.HSVToRGB(Mathf.Repeat(h + n2 * variation * 0.04f, 1f), Mathf.Clamp01(sat * (1f + n2 * variation * 0.4f)), Mathf.Clamp01(val * (1f + n1 * variation * 0.5f)));
                    varied.a = 1f;
                    maps.BaseColor[i] = varied;
                    maps.Roughness[i] = Mathf.Clamp01(roughness + n1 * variation * 0.25f);
                });
                return maps;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Mask Fill"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Materials"", ""description"": ""A material painted by a mask: where it's 0 it's the Low settings, where it's 1 the High ones, blending between. Wire Noise in for a two-tone stone, Cells for tiles of different shades, a gradient for a fade; Height Low and High turn the mask into bumps."", ""inputs"": [{""name"": ""Mask"", ""type"": ""MaskMap""}], ""outputs"": [{""name"": ""Material"", ""type"": ""MaterialMaps""}], ""use_for"": [""colour from a pattern (stone, wood, camo)"", ""bumps and dents from a mask (Height Low and High)""]}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Mask Fill", Path = "Materials"), Tags(MaterialNode.Tag)]
    public sealed class MaskFillMaterialNode : MaterialNode
    {
        [Tooltip("What picks between Low (0) and High (1) at each spot.")]
        [Input("Mask", typeof(MaskMap), Editable = true)] public float Mask = 0.5f;
        [Input("Color Low", Editable = true)] public Color ColorLow = new(0.35f, 0.33f, 0.3f, 1f);
        [Input("Color High", Editable = true)] public Color ColorHigh = new(0.7f, 0.68f, 0.63f, 1f);
        [Input("Roughness Low", Editable = true)] public float RoughnessLow = 0.6f;
        [Input("Roughness High", Editable = true)] public float RoughnessHigh = 0.6f;
        [Input("Metallic Low", Editable = true)] public float MetallicLow;
        [Input("Metallic High", Editable = true)] public float MetallicHigh;
        [Tooltip("The height where the mask is 0 (0.5 is the surface).")]
        [Input("Height Low", Editable = true)] public float HeightLow = 0.5f;
        [Tooltip("The height where the mask is 1 (0.5 is the surface).")]
        [Input("Height High", Editable = true)] public float HeightHigh = 0.5f;
        [Output(MaterialPort)] [NonSerialized] public MaterialMaps Material;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var mask = c.Mask(this, "Mask", Mask);
            var colorLow = c.ColorValue(this, "Color Low", ColorLow);
            var colorHigh = c.ColorValue(this, "Color High", ColorHigh);
            float roughLow = c.Number(this, "Roughness Low", RoughnessLow), roughHigh = c.Number(this, "Roughness High", RoughnessHigh);
            float metalLow = c.Number(this, "Metallic Low", MetallicLow), metalHigh = c.Number(this, "Metallic High", MetallicHigh);
            float heightLow = c.Number(this, "Height Low", HeightLow), heightHigh = c.Number(this, "Height High", HeightHigh);
            return () =>
            {
                var m = mask();
                var maps = new MaterialMaps(c.Width, c.Height);
                maps.ForEach(i =>
                {
                    float t = Mathf.Clamp01(m.Values[i]);
                    var color = Color.Lerp(colorLow, colorHigh, t);
                    color.a = 1f;
                    maps.BaseColor[i] = color;
                    maps.Roughness[i] = Mathf.Clamp01(Mathf.Lerp(roughLow, roughHigh, t));
                    maps.Metallic[i] = Mathf.Clamp01(Mathf.Lerp(metalLow, metalHigh, t));
                    maps.HeightMap[i] = Mathf.Clamp01(Mathf.Lerp(heightLow, heightHigh, t));
                    maps.Occlusion[i] = 1f;
                });
                return maps;
            };
        }
    }

    public enum LayerHeight { Blend, Add, Highest }

    [SchematicDoc(@"{""name"": ""Layer"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Materials"", ""description"": ""Puts the Top material over the Base where Mask says (1 Top, 0 Base), at Opacity: a layer in a layer stack. Chain Layers for paint over metal, then rust in the chips, then dirt in the creases. Height Blend lets the Top's own height decide where it shows at the mask's soft edges (Contrast: how crisply), so rust creeps into the low spots first and paint wears off the high spots first. Height says what happens to the height: Blend (Top's replaces Base's), Add (Top's bumps go on Base's: scratches, dents) or Highest. The channel toggles keep the Base's colour, roughness, metallic or height where they're off (a gloss-only layer: just Roughness)."", ""inputs"": [{""name"": ""Base"", ""type"": ""MaterialMaps""}, {""name"": ""Top"", ""type"": ""MaterialMaps""}, {""name"": ""Mask"", ""type"": ""MaskMap""}, {""name"": ""Opacity"", ""type"": ""float""}], ""outputs"": [{""name"": ""Material"", ""type"": ""MaterialMaps""}], ""use_for"": [""stacking materials (paint over metal, rust, dirt)"", ""edge wear and grime through smart masks""]}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Layer", Path = "Materials"), Tags(MaterialNode.Tag)]
    public sealed class LayerMaterialNode : MaterialNode
    {
        [Input("Base", Editable = false)] [NonSerialized] public MaterialMaps Base;
        [Input("Top", Editable = false)] [NonSerialized] public MaterialMaps Top;
        [Tooltip("Where Top goes: 1 Top, 0 Base. Wire in a mask (Edge Wear, Grime, Noise, a Paint node's mask), or type a number for everywhere.")]
        [Input("Mask", typeof(MaskMap), Editable = true)] public float Mask = 1f;
        [Tooltip("How much of Top shows at all: 0 none, 1 fully.")]
        [Input("Opacity", Editable = true)] public float Opacity = 1f;
        [Tooltip("Let Top's height decide where it shows at the mask's soft edges: low spots first (rust, dirt) or high spots (with a raised Top).")]
        [Editable] public bool HeightBlend;
        [Tooltip("How crisply Height Blend cuts (1 soft, 8 sharp).")]
        [Editable] public float Contrast = 4f;
        [Tooltip("Blend: Top's height replaces Base's. Add: Top's bumps go on top of Base's (scratches, dents, drips). Highest: whichever stands higher.")]
        [Editable] public LayerHeight Height = LayerHeight.Blend;
        [Tooltip("Top's colour shows.")]
        [Editable] public bool AffectColor = true;
        [Tooltip("Top's roughness shows.")]
        [Editable] public bool AffectRoughness = true;
        [Tooltip("Top's metallic shows.")]
        [Editable] public bool AffectMetallic = true;
        [Tooltip("Top's height shows.")]
        [Editable] public bool AffectHeight = true;
        [Output(MaterialPort)] [NonSerialized] public MaterialMaps Material;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var baseMaterial = c.Material(this, "Base");
            var top = c.Material(this, "Top");
            var mask = c.Mask(this, "Mask", Mask);
            float opacity = Mathf.Clamp01(c.Number(this, "Opacity", Opacity));
            bool heightBlend = HeightBlend;
            float contrast = Mathf.Max(0.1f, Contrast);
            var heightMode = Height;
            bool color = AffectColor, rough = AffectRoughness, metal = AffectMetallic, height = AffectHeight;
            return () =>
            {
                var a = baseMaterial();
                var b = top();
                var m = mask();
                var maps = a.Clone();
                maps.ForEach(i =>
                {
                    float t = Mathf.Clamp01(m.Values[i]);
                    if (heightBlend)
                        t = Mathf.Clamp01((t - 0.5f) * contrast + 0.5f + (b.HeightMap[i] - a.HeightMap[i]) * contrast * 2f) * Mathf.Clamp01(t * 4f);
                    t *= opacity;
                    if (t <= 0f)
                        return;
                    if (color)
                        maps.BaseColor[i] = Color.Lerp(a.BaseColor[i], b.BaseColor[i], t);
                    if (rough)
                        maps.Roughness[i] = Mathf.Lerp(a.Roughness[i], b.Roughness[i], t);
                    if (metal)
                        maps.Metallic[i] = Mathf.Lerp(a.Metallic[i], b.Metallic[i], t);
                    if (height)
                        maps.HeightMap[i] = heightMode switch
                        {
                            LayerHeight.Add => Mathf.Clamp01(a.HeightMap[i] + (b.HeightMap[i] - 0.5f) * t),
                            LayerHeight.Highest => Mathf.Lerp(a.HeightMap[i], Mathf.Max(a.HeightMap[i], b.HeightMap[i]), t),
                            _ => Mathf.Lerp(a.HeightMap[i], b.HeightMap[i], t),
                        };
                    maps.Occlusion[i] = Mathf.Lerp(a.Occlusion[i], b.Occlusion[i], t);
                });
                return maps;
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Adjust"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Materials"", ""description"": ""Tweaks a material: Hue turns its colours round the colour wheel (-0.5 to 0.5), Saturation and Brightness scale them (1 as it is), Roughness and Metallic add to them, and Height Scale strengthens or flattens its bumps (1 as it is, 0 flat). With a Mask, only where it's light."", ""inputs"": [{""name"": ""In"", ""type"": ""MaterialMaps""}, {""name"": ""Mask"", ""type"": ""MaskMap""}], ""outputs"": [{""name"": ""Material"", ""type"": ""MaterialMaps""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Adjust", Path = "Materials"), Tags(MaterialNode.Tag)]
    public sealed class AdjustMaterialNode : MaterialNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MaterialMaps In;
        [Tooltip("Where it applies: 1 fully, 0 not at all. Everywhere if nothing's wired in.")]
        [Input("Mask", typeof(MaskMap), Editable = true)] public float Mask = 1f;
        [Tooltip("Turns the colours round the colour wheel: -0.5 to 0.5 (0 as it is).")]
        [Input("Hue", Editable = true)] public float Hue;
        [Tooltip("1 as it is, 0 grey, 2 twice as vivid.")]
        [Input("Saturation", Editable = true)] public float Saturation = 1f;
        [Tooltip("1 as it is, 0.5 half as bright.")]
        [Input("Brightness", Editable = true)] public float Brightness = 1f;
        [Tooltip("Added to the roughness (-1 to 1).")]
        [Input("Roughness", Editable = true)] public float Roughness;
        [Tooltip("Added to the metallic (-1 to 1).")]
        [Input("Metallic", Editable = true)] public float Metallic;
        [Tooltip("1 as it is, 0 flat, 2 twice as bumpy.")]
        [Input("Height Scale", Editable = true)] public float HeightScale = 1f;
        [Output(MaterialPort)] [NonSerialized] public MaterialMaps Material;

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var input = c.Material(this, "In");
            var mask = c.Mask(this, "Mask", Mask);
            float hue = c.Number(this, "Hue", Hue), saturation = c.Number(this, "Saturation", Saturation), brightness = c.Number(this, "Brightness", Brightness);
            float roughness = c.Number(this, "Roughness", Roughness), metallic = c.Number(this, "Metallic", Metallic), heightScale = c.Number(this, "Height Scale", HeightScale);
            return () =>
            {
                var source = input();
                var m = mask();
                var maps = source.Clone();
                maps.ForEach(i =>
                {
                    float t = Mathf.Clamp01(m.Values[i]);
                    if (t <= 0f)
                        return;
                    Color.RGBToHSV(source.BaseColor[i], out float h, out float s, out float v);
                    var adjusted = Color.HSVToRGB(Mathf.Repeat(h + hue, 1f), Mathf.Clamp01(s * saturation), Mathf.Clamp01(v * brightness));
                    adjusted.a = 1f;
                    maps.BaseColor[i] = Color.Lerp(source.BaseColor[i], adjusted, t);
                    maps.Roughness[i] = Mathf.Clamp01(source.Roughness[i] + roughness * t);
                    maps.Metallic[i] = Mathf.Clamp01(source.Metallic[i] + metallic * t);
                    maps.HeightMap[i] = Mathf.Clamp01(Mathf.Lerp(source.HeightMap[i], 0.5f + (source.HeightMap[i] - 0.5f) * heightScale, t));
                });
                return maps;
            };
        }
    }
}
