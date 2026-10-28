using BlueGraph;
using Remedy.Meshes;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// A Smart Material: a material made from nodes, like Substance, baked to textures for one mesh (or as a flat,
    /// tiling material). Fills and generators make materials and masks; mesh bakes (ambient occlusion, edges, cavities,
    /// thickness) know the model's shape, so wear lands on its edges and grime in its creases; Layers stack materials
    /// through masks; Paint nodes add what's painted by hand on the model. The Output bakes it all into packed
    /// textures and a material.
    /// </summary>
    /// <remarks>
    /// Like Mesh Graphs, it's read on the main thread into a plan (<see cref="MaterialCompiler"/>) and worked out texel
    /// by texel on background threads, so the editor's preview never stalls.
    /// </remarks>
    [SchematicType("Materials", Order = 36, Icon = "SchematicTab:material", NewAssetName = "New Smart Material", DefaultFolder = Folder)]
    [CreateAssetMenu(fileName = "New Smart Material", menuName = "Remedy Toolkit/Schematics/Smart Material")]
    [IncludeTags(MaterialNode.Tag)]
    [RequireNode(typeof(MaterialOutputNode), "Output", 600, 0)]
    public sealed class SmartMaterialGraph : SchematicGraph
    {
        /// <summary>Exclusive search tag for the required Output node (not offered in the node search).</summary>
        public const string OutputOnlyTag = "Smart Material Required Output";

        /// <summary>Where new Smart Materials go.</summary>
        public const string Folder = "Assets/Content/Materials";

        public override string Title => "MATERIAL";

        [NonSerialized] private int _version;

        /// <summary>Changes whenever the graph does, or the Mesh Graph it's made for: made textures from before are stale.</summary>
        public int Version
        {
            get
            {
                unchecked
                {
                    int version = _version;
                    if (Output != null && Output.Mesh is MeshGraph mesh)
                        version = version * 31 + mesh.Version;
                    return version;
                }
            }
        }

        public override IReadOnlyList<GraphParameter> GraphParameters
            => ParameterNodes.Select(p => new GraphParameter(p.ParameterName.Trim(), typeof(float), p.Default, p.UI)).ToList();

        /// <summary>The Parameter node declaring each of its Parameters (the first, where two share a name).</summary>
        public List<MaterialParameterNode> ParameterNodes
        {
            get
            {
                var found = new List<MaterialParameterNode>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in Nodes)
                    if (node is MaterialParameterNode parameter && !string.IsNullOrWhiteSpace(parameter.ParameterName) && names.Add(parameter.ParameterName.Trim()))
                        found.Add(parameter);
                return found;
            }
        }

        /// <summary>The graph's Output node.</summary>
        public MaterialOutputNode Output
        {
            get
            {
                foreach (var node in Nodes)
                    if (node is MaterialOutputNode output)
                        return output;
                return null;
            }
        }

        public override void OnGraphValidate()
        {
            base.OnGraphValidate();
            _version++;
            bool removedDuplicate = false;
            foreach (var duplicate in Nodes.OfType<MaterialOutputNode>().Skip(1).ToArray())
            {
                RemoveNode(duplicate);
                removedDuplicate = true;
            }
            if (removedDuplicate)
                CacheNodesByType();
        }

        /// <summary>Marks the graph changed (the editor does when a node is edited, or something's painted).</summary>
        public void Touch() => _version++;
    }

    /// <summary>
    /// A node in a Smart Material. For each of its outputs it makes a step of the plan (<see cref="Compile"/>, main
    /// thread: settings read now), which works its map out later on a background thread.
    /// </summary>
    [Serializable]
    [NodeColor(NodeColors.Object)]
    public abstract class MaterialNode : SchematicGraphNode
    {
        /// <summary>The tag every Smart Material node has: Smart Materials only offer nodes with it.</summary>
        public const string Tag = "Smart Material";

        public const string MaterialPort = "Material";
        public const string MaskPort = "Mask";

        /// <summary>
        /// The work making one of its outputs (named <paramref name="output"/>): a <see cref="MaterialMaps"/> for a
        /// Material output, a <see cref="MaskMap"/> for a Mask. Shared by everything wired to it, so never changed after.
        /// </summary>
        public abstract Func<object> Compile(MaterialCompiler compiler, string output);

        // Smart Material nodes aren't run by Schematic flows.
        public override object OnRequestValue(Port port) => null;
    }

    /// <summary>How the Output packs roughness, metallic and occlusion into textures.</summary>
    public enum MaterialPacking
    {
        /// <summary>Whatever this project's render pipeline reads: HDRP's Mask Map, or Unity's Metallic map plus Occlusion.</summary>
        Auto,
        /// <summary>HDRP's Mask Map: R metallic, G occlusion, B detail mask, A smoothness.</summary>
        HDRPMaskMap,
        /// <summary>URP and Built-in: a Metallic map (R metallic, A smoothness) and a separate Occlusion map.</summary>
        UnityMetallic,
        /// <summary>One texture: R occlusion, G roughness, B metallic (Unreal, glTF).</summary>
        ORM,
        /// <summary>A grey texture for each.</summary>
        Separate,
    }

    /// <summary>The finished material, and how it's baked: the mesh, the size, the packing.</summary>
    [SchematicDoc(@"{""name"": ""Output"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material"", ""description"": ""The finished material, and how it's baked. Mesh is what it's made for (a Mesh Graph, a Mesh, or a model: an FBX or prefab): its UVs lay the textures out, and the mesh bakes (ambient occlusion, edges, cavities, thickness) read its shape. With no Mesh it's a flat, tiling material. Bake (in the preview) writes Base Color, Normal (from the height, Height Depth meters deep), and roughness, metallic and occlusion packed as Packing says, at Resolution, plus a material using them. Ambient Occlusion multiplies the mesh's own shading into the occlusion map. Padding spreads colour past the UV islands' edges so seams don't show at a distance."", ""inputs"": [{""name"": ""Material"", ""type"": ""MaterialMaps""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Output", Path = "Output", Deletable = false), Tags(SmartMaterialGraph.OutputOnlyTag)]
    public sealed class MaterialOutputNode : MaterialNode
    {
        [Input(MaterialPort, Editable = false)] [NonSerialized] public MaterialMaps Material;

        [Editable, Tooltip("What it's made for: a Mesh Graph, a Mesh, or a model (FBX, prefab). Its UVs lay out the textures. None: a flat material that tiles.")]
        public UnityEngine.Object Mesh;
        [Editable, Tooltip("The baked textures' size in pixels (256 to 4096).")]
        public int Resolution = 1024;
        [Editable, Tooltip("The size the preview works at: smaller is quicker to update.")]
        public int PreviewResolution = 512;
        [Editable, Tooltip("How roughness, metallic and occlusion go into textures. Auto: whatever this project's render pipeline uses.")]
        public MaterialPacking Packing = MaterialPacking.Auto;
        [Editable, Tooltip("How deep the height map goes, 0 to 1 (meters): deeper makes the normal map's bumps stronger.")]
        public float HeightDepth = 0.005f;
        [Editable, Tooltip("Multiply the mesh's own shading (light blocked by its other parts) into the occlusion map.")]
        public bool AmbientOcclusion = true;
        [Editable, Tooltip("How far (a share of the mesh's size) the occlusion looks for parts blocking the light.")]
        public float OcclusionDistance = 0.15f;
        [Editable, Tooltip("Pixels of colour spread past the UV islands' edges, so seams don't show when the texture's smaller on screen.")]
        public int Padding = 8;
        [Editable, Tooltip("Also write the height map as its own texture (for parallax or tessellation).")]
        public bool WriteHeight;

        public override void OnValidate()
        {
            base.OnValidate();
            Resolution = SnapSize(Resolution);
            PreviewResolution = SnapSize(PreviewResolution);
            Warning = Mesh != null && Mesh is not MeshGraph && Mesh is not UnityEngine.Mesh && Mesh is not UnityEngine.GameObject && Mesh is not UnityEngine.Component
                ? $"{Mesh.name} isn't a Mesh Graph, a mesh or a model."
                : null;
        }

        /// <summary>A size rounded to a power of two from 64 to 4096.</summary>
        public static int SnapSize(int size) => Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, size)), 64, 4096);

        public override Func<object> Compile(MaterialCompiler compiler, string output)
        {
            var material = compiler.Material(this, MaterialPort);
            return () => material();
        }
    }

    /// <summary>A number each object sets for itself (or that's set where the graph's used), by name.</summary>
    [SchematicDoc(@"{""name"": ""Parameter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material"", ""description"": ""A named number: wire it into any number on a node (a rust amount, a colour's brightness, a seed) to tune the material from one place. It's Default until set."", ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Parameter", Path = "Values"), Tags(MaterialNode.Tag)]
    public sealed class MaterialParameterNode : MaterialNode
    {
        [Tooltip("The name it's known by.")]
        [Editable] public string ParameterName = "Wear";
        [Tooltip("Its value until something sets it.")]
        [Editable] public float Default = 0.5f;
        [Tooltip("How it's shown where it's set: its control, range, group and description.")]
        public ParameterUI UI = new();
        [Output("Value")] public float Value;

        public override Func<object> Compile(MaterialCompiler compiler, string output) => null;

        public override object OnRequestValue(Port port)
            => MaterialCompiler.Current != null ? MaterialCompiler.Current.Parameter(ParameterName, Default) : Default;
    }
}
