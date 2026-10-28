// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// Hand painting, Substance Painter style: brush strokes painted straight onto the model in its viewport (Paint...
    /// on the node), over the material coming in. Colour, roughness, metallic and height can each be painted, and a
    /// mask too, to drive Layers below it. What's painted is kept as three textures next to the graph.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Paint"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Materials"", ""description"": ""Paint by hand on the model. Click Paint... on the node to open its viewport: brush onto the model itself (left-drag), with the colour, roughness, metallic and height you pick (each can be on or off), or paint the Mask (to wire into a Layer: hand-placed rust, a painted logo's outline, where a decal goes). Ctrl erases. [ and ] change the brush size; Shift+drag changes it too. What's painted goes over the material wired into In and comes out of Material; the painted mask comes out of Mask. Strokes run straight across UV seams (they're painted in 3D). Saved as three textures next to the graph (at Resolution)."", ""inputs"": [{""name"": ""In"", ""type"": ""MaterialMaps""}], ""outputs"": [{""name"": ""Material"", ""type"": ""MaterialMaps""}, {""name"": ""Mask"", ""type"": ""MaskMap""}], ""use_for"": [""hand-painted details, colour and wear"", ""a hand-placed mask for a Layer"", ""touching up a procedural material""]}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Paint", Path = "Materials"), Tags(MaterialNode.Tag)]
    public sealed class PaintNode : MaterialNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MaterialMaps In;
        [Tooltip("The painted textures' size (square, in pixels). Changing it scales what's painted.")]
        [Editable] public int Resolution = 1024;
        [Tooltip("How much of what's painted shows: 0 none, 1 all.")]
        [Input("Opacity", Editable = true)] public float Opacity = 1f;

        /// <summary>What's painted: colour (RGB, A how much covers), surface (R roughness, G its cover, B metallic, A its cover), height (R, 0.5 none) and mask (G).</summary>
        [HideInInspector] public Texture2D ColourTexture, SurfaceTexture, HeightMaskTexture;

        [Output(MaterialPort)] [NonSerialized] public MaterialMaps Material;
        [Output(MaskPort)] [NonSerialized] public MaskMap Mask;

        /// <summary>
        /// What's painted, while it's being painted (the editor's painting window keeps it here, and saves it to the
        /// textures): read in its place.
        /// </summary>
        [NonSerialized] public PaintLayer Live;

        public override void OnValidate()
        {
            base.OnValidate();
            Resolution = MaterialOutputNode.SnapSize(Resolution);
        }

        /// <summary>What's painted: the live layer, or read from its textures (main thread: textures must be readable).</summary>
        public PaintLayer Read()
        {
            if (Live != null)
                return Live;
            int size = Resolution;
            Color[] colour = Pixels(ColourTexture, size), surface = Pixels(SurfaceTexture, size), heightMask = Pixels(HeightMaskTexture, size);
            return colour == null && surface == null && heightMask == null ? null : PaintLayer.FromPixels(size, colour, surface, heightMask);
        }

        private static Color[] Pixels(Texture2D texture, int size)
        {
            if (texture == null || !texture.isReadable || texture.width != size || texture.height != size)
                return null;
            return texture.GetPixels();
        }

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            var input = c.Material(this, "In");
            float opacity = Mathf.Clamp01(c.Number(this, "Opacity", Opacity));
            // A copy as it is now: painting goes on while the plan runs.
            var paint = Read()?.Clone();
            if (paint == null && (ColourTexture != null || SurfaceTexture != null || HeightMaskTexture != null))
                c.Warnings.Add($"\"{Name}\": its painted textures couldn't be read (they need Read/Write on, at {Resolution}×{Resolution}).");
            int width = c.Width, height = c.Height;
            if (output == MaskPort)
                return () =>
                {
                    var mask = new MaskMap(width, height);
                    if (paint != null)
                        mask.Fill(i => MapSampling.Sample(paint.Mask, paint.Size, width, height, i) * opacity);
                    return mask;
                };
            return () =>
            {
                var source = input();
                if (paint == null || opacity <= 0f)
                    return source;
                var maps = source.Clone();
                maps.ForEach(i =>
                {
                    var colour = MapSampling.Sample(paint.Colour, paint.Size, width, height, i);
                    float cover = colour.a * opacity;
                    if (cover > 0f)
                    {
                        colour.a = 1f;
                        maps.BaseColor[i] = Color.Lerp(source.BaseColor[i], colour, cover);
                    }
                    float roughCover = MapSampling.Sample(paint.RoughnessCover, paint.Size, width, height, i) * opacity;
                    if (roughCover > 0f)
                        maps.Roughness[i] = Mathf.Lerp(source.Roughness[i], MapSampling.Sample(paint.Roughness, paint.Size, width, height, i), roughCover);
                    float metalCover = MapSampling.Sample(paint.MetallicCover, paint.Size, width, height, i) * opacity;
                    if (metalCover > 0f)
                        maps.Metallic[i] = Mathf.Lerp(source.Metallic[i], MapSampling.Sample(paint.Metallic, paint.Size, width, height, i), metalCover);
                    float offset = MapSampling.Sample(paint.HeightOffset, paint.Size, width, height, i) - 0.5f;
                    if (offset != 0f)
                        maps.HeightMap[i] = Mathf.Clamp01(source.HeightMap[i] + offset * opacity);
                });
                return maps;
            };
        }
    }
}
