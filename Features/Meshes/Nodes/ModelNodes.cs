// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    // Hand-made models in a Mesh Graph: a Model node brings in a mesh or an FBX (its material slots, UVs, vertex
    // colours and shape keys kept), so a modelled base can be reshaped, cut up, dressed and LODded like any made shape.

    /// <summary>
    /// A modelled mesh brought into the graph: a Mesh asset, or a model (FBX or prefab), every mesh in it placed as it is
    /// there. Keeps its material slots, UVs, vertex colours and shape keys.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Model"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""Brings a hand-made model into the graph: drop a Mesh asset or a whole model (an FBX, a prefab) into Source. A model's meshes are all brought in, placed as they are in it (relative to its root). Material slots, UVs, vertex colours and shape keys (blend shapes) come with it: a Blend Shape node can dial a shape in, Select Colour can pick out parts painted in the modelling app. Separate Slots gives each part of a model its own material slots after the last part's (otherwise each part's first submesh shares slot 0, and so on)."", ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}], ""use_for"": [""a hand-modelled base mesh (a head, a hull, a rock) reshaped by Parameters"", ""kitbashing modelled parts with made ones"", ""LODs for a modelled asset""], ""notes"": ""In a build, the mesh must have Read/Write enabled in its import settings (the editor can read it either way).""}")]
    [Serializable]
    [Node(Name = "Model", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshModelNode : MeshNode
    {
        [Tooltip("A Mesh asset, or a model (FBX or prefab): every mesh in it is brought in, placed as it is there.")]
        [Editable] public UnityEngine.Object Source;
        [Tooltip("A model's parts each get material slots of their own, after the last part's (otherwise their first submeshes share slot 0, their second slot 1...).")]
        [Editable] public bool SeparateSlots = true;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override void OnValidate()
        {
            base.OnValidate();
            Warning = Source == null ? "No Source: drop a Mesh or a model (FBX, prefab) in."
                : Source is not UnityEngine.Mesh && Source is not UnityEngine.GameObject && Source is not UnityEngine.Component ? $"{Source.name} isn't a mesh or a model."
                : null;
        }

        public override MeshOp Compile(MeshCompiler c)
        {
            // Read now, on the main thread (Unity meshes can only be read there).
            var data = Read(Source, SeparateSlots, c, this);
            return new MeshOp(() => data);
        }

        /// <summary>A mesh or model as mesh data (main thread). Problems go to the compiler's warnings.</summary>
        public static MeshData Read(UnityEngine.Object source, bool separateSlots, MeshCompiler c = null, SchematicGraphNode node = null)
        {
            var result = new MeshData();
            switch (source)
            {
                case UnityEngine.Mesh mesh:
                    if (Readable(mesh, c, node))
                        result = MeshData.FromMesh(mesh);
                    break;
                case UnityEngine.GameObject model:
                    AppendModel(result, model.transform, separateSlots, c, node);
                    break;
                case UnityEngine.Component component:
                    AppendModel(result, component.transform, separateSlots, c, node);
                    break;
            }
            return result;
        }

        private static void AppendModel(MeshData result, Transform root, bool separateSlots, MeshCompiler c, SchematicGraphNode node)
        {
            var toRoot = root.worldToLocalMatrix;
            var parts = new List<(UnityEngine.Mesh mesh, Matrix4x4 matrix)>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null)
                    parts.Add((filter.sharedMesh, toRoot * filter.transform.localToWorldMatrix));
            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh != null)
                    parts.Add((skinned.sharedMesh, toRoot * skinned.transform.localToWorldMatrix));
            if (parts.Count == 0)
                c?.Warnings.Add($"\"{node?.Name}\": {root.name} has no meshes in it.");
            foreach (var (mesh, matrix) in parts)
            {
                if (!Readable(mesh, c, node))
                    continue;
                var part = MeshData.FromMesh(mesh);
                if (separateSlots && result.VertexCount > 0)
                {
                    // Its slots after the ones already there.
                    int first = result.Slots.Count;
                    var moved = new List<List<int>>();
                    for (int s = 0; s < first; s++)
                        moved.Add(new List<int>());
                    moved.AddRange(part.Slots);
                    part.Slots.Clear();
                    part.Slots.AddRange(moved);
                }
                result.Append(part, matrix, matrix != Matrix4x4.identity);
            }
        }

        private static bool Readable(UnityEngine.Mesh mesh, MeshCompiler c, SchematicGraphNode node)
        {
            if (mesh.isReadable || Application.isEditor)
                return true;
            c?.Warnings.Add($"\"{node?.Name}\": {mesh.name} can't be read in a build: tick Read/Write in its import settings.");
            return false;
        }
    }

    /// <summary>Dials one of the mesh's shape keys (blend shapes, from a modelled mesh) in by Weight.</summary>
    [SchematicDoc(@"{""name"": ""Blend Shape"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Deform"", ""description"": ""Moves the mesh by one of its shape keys (blend shapes, made in the modelling app and brought in by a Model node): Weight 0 is none of it, 1 the full shape (Unity's 100), 0.5 halfway; past 1 or below 0 pushes it further, or the other way. Wire a Parameter into Weight to make the shape a slider: a jaw width, a belly, a dent. Only Selected scales it by the selection, to use just part of a shape. Shape keys come through nodes that move vertices (Grab, Transform, Edit Vertices...) but not ones that rebuild the mesh (Subdivide, Boolean...): put Blend Shapes before those."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Weight"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}], ""use_for"": [""character and prop variation from shapes sculpted by hand"", ""a Parameter slider driving a modelled shape""]}")]
    [Serializable]
    [Node(Name = "Blend Shape", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshBlendShapeNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("The shape key's name, as it was in the modelling app (any case).")]
        [Editable] public string Shape = "";
        [Tooltip("How much of the shape: 0 none, 1 the full shape (Unity's 100), 0.5 halfway. Past 1 pushes it further; below 0 the other way.")]
        [Input("Weight", Editable = true)] public float Weight = 1f;
        [Tooltip("Scale it by the selection: only what's selected takes the shape.")]
        [Editable] public bool OnlySelected;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override void OnValidate()
        {
            base.OnValidate();
            Warning = string.IsNullOrWhiteSpace(Shape) ? "No Shape named: type the shape key's name." : null;
        }

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float weight = c.Number(this, "Weight", Weight);
            string name = Shape;
            bool onlySelected = OnlySelected;
            string node = Name;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                var shape = mesh.BlendShape(name);
                if (shape == null)
                {
                    var names = mesh.BlendShapes.ConvertAll(s => s.Name);
                    c.RunWarning(names.Count == 0
                        ? $"\"{node}\": the mesh coming in has no shape keys (a node before it may have rebuilt it)."
                        : $"\"{node}\": there's no shape key \"{name}\" (it has: {string.Join(", ", names)}).");
                    return mesh;
                }
                if (!shape.Fits(mesh.VertexCount))
                {
                    c.RunWarning($"\"{node}\": a node before it rebuilt the mesh, so \"{name}\" no longer fits it. Put Blend Shape earlier.");
                    return mesh;
                }
                bool selection = onlySelected && mesh.HasSelection;
                for (int i = 0; i < shape.Deltas.Count; i++)
                {
                    float w = selection ? weight * mesh.Selection[i] : weight;
                    mesh.Positions[i] += shape.Deltas[i] * w;
                    if (i < shape.NormalDeltas.Count && i < mesh.Normals.Count)
                        mesh.Normals[i] = MeshData.Direction(mesh.Normals[i] + shape.NormalDeltas[i] * w, mesh.Normals[i]);
                }
                return mesh;
            });
        }
    }

    public enum ColourChannel { Colour, Red, Green, Blue, Alpha }

    /// <summary>Selects vertices by their vertex colour: a channel's strength, or closeness to a colour.</summary>
    [SchematicDoc(@"{""name"": ""Select Colour"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects vertices by their vertex colour (painted in the modelling app, or by a Colour node). Channel Colour picks those close to Match: within Tolerance (0 exact, 1 anything), fading out over Falloff past it. Red, Green, Blue or Alpha picks by that channel's strength: above Threshold, fading in over Falloff below it, so paint a mask into one channel and select by it. Mode mixes it with the selection already there."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Match"", ""type"": ""Color""}, {""name"": ""Tolerance"", ""type"": ""float""}, {""name"": ""Threshold"", ""type"": ""float""}, {""name"": ""Falloff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}], ""use_for"": [""parts marked by colour in the modelling app (paint the horns red, select red)"", ""a painted mask driving a deform""]}")]
    [Serializable]
    [Node(Name = "Select Colour", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectColourNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("Colour: pick vertices close to Match. Red, Green, Blue, Alpha: pick by how strong that channel is.")]
        [Editable] public ColourChannel Channel = ColourChannel.Colour;
        [Tooltip("The colour to pick (Channel Colour).")]
        [Input("Match", Editable = true)] public Color Match = Color.red;
        [Tooltip("How different from Match a colour can be and still be fully picked: 0 exactly it, 0.2 near it, 1 anything.")]
        [Input("Tolerance", Editable = true)] public float Tolerance = 0.15f;
        [Tooltip("For a channel: how strong it must be to be fully picked (0 to 1).")]
        [Input("Threshold", Editable = true)] public float Threshold = 0.5f;
        [Tooltip("How softly the pick fades out past Tolerance (or below Threshold): 0 is a hard edge.")]
        [Input("Falloff", Editable = true)] public float Falloff = 0.1f;
        [Tooltip("How it combines with what's already selected: Replace, Add to it, Subtract from it, or Intersect (keep only what's in both).")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var channel = Channel;
            var match = c.ColorValue(this, "Match", Match);
            float tolerance = Mathf.Max(0f, c.Number(this, "Tolerance", Tolerance));
            float threshold = c.Number(this, "Threshold", Threshold);
            float falloff = Mathf.Max(0f, c.Number(this, "Falloff", Falloff));
            var mode = Mode;
            string node = Name;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                if (!mesh.HasColors)
                    c.RunWarning($"\"{node}\": the mesh has no vertex colours (every vertex counts as white).");
                MeshSelection.Apply(mesh, i =>
                {
                    var colour = mesh.HasColors ? mesh.Colors[i] : Color.white;
                    if (channel == ColourChannel.Colour)
                    {
                        // Distance in RGB, scaled so 1 is as far as two colours can be.
                        float d = new Vector3(colour.r - match.r, colour.g - match.g, colour.b - match.b).magnitude / 1.7320508f;
                        float past = d - tolerance;
                        return past <= 0f ? 1f : falloff > 0f ? 1f - Mathf.Clamp01(past / falloff) : 0f;
                    }
                    float v = channel switch
                    {
                        ColourChannel.Red => colour.r,
                        ColourChannel.Green => colour.g,
                        ColourChannel.Blue => colour.b,
                        _ => colour.a,
                    };
                    float below = threshold - v;
                    return below <= 0f ? 1f : falloff > 0f ? 1f - Mathf.Clamp01(below / falloff) : 0f;
                }, mode);
                return mesh;
            });
        }
    }
}
