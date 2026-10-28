using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// A Mesh Graph: a 3D model built from nodes. Shapes (box, sphere, cylinder, lathe...) run through modifiers
    /// (bend, twist, noise, mirror, array, scatter...) and merges into the Output, which also makes its LODs: simpler
    /// copies shown further away. Parameters (a tree's height, a rock's seed) are set per object, so one graph makes a
    /// whole family of models.
    /// </summary>
    /// <remarks>
    /// Made by a <see cref="MeshGraphRenderer"/> (or baked to a mesh asset in the editor). The graph is read on the
    /// main thread into a plan (<see cref="MeshCompiler"/>), and the plan, the shapes and the LODs are worked out on a
    /// background thread, so building a model never stalls a frame. Objects asking for the same graph and Parameters
    /// share one mesh.
    /// </remarks>
    [SchematicType("Meshes", Order = 35, Icon = "SchematicTab:mesh", NewAssetName = "New Mesh Graph", DefaultFolder = Folder)]
    [CreateAssetMenu(fileName = "New Mesh Graph", menuName = "Remedy Toolkit/Schematics/Mesh Graph")]
    [IncludeTags(MeshNode.Tag)]
    [RequireNode(typeof(MeshOutputNode), "Output", 500, 0)]
    public sealed class MeshGraph : SchematicGraph
    {
        /// <summary>Exclusive search tag for the required Output node (not offered in the node search).</summary>
        public const string OutputOnlyTag = "Mesh Graph Required Output";

        /// <summary>Where new Mesh Graphs go.</summary>
        public const string Folder = "Assets/Content/Meshes";

        public override string Title => "MESH";

        [NonSerialized] private int _version;

        /// <summary>
        /// Changes whenever the graph does (an edit in the editor), or any graph it calls as a function does:
        /// generated meshes from before are stale.
        /// </summary>
        public int Version => VersionWith(0);

        // Its own version mixed with those of the graphs it calls (a few calls deep: a graph calling itself is
        // refused when it's built anyway). Only in the editor: in a game nothing's edited, and renderers ask every frame.
        private int VersionWith(int depth)
        {
            int version = _version;
#if UNITY_EDITOR
            if (depth > 8)
                return version;
            unchecked
            {
                foreach (var node in Nodes)
                    if (node is MeshFunctionNode call && call.Calls != null && call.Calls != this)
                        version = version * 31 + call.Calls.VersionWith(depth + 1);
            }
#endif
            return version;
        }

        /// <summary>Its Parameters, with how they're shown where it's called as a function, for Set Graph Parameters, the Mesh Graph Renderer and Function nodes.</summary>
        public override IReadOnlyList<GraphParameter> GraphParameters
            => ParameterNodes.Select(p => new GraphParameter(p.ParameterName.Trim(), typeof(float), p.Default, p.UI)).ToList();

        /// <summary>The Parameter node declaring each of its Parameters (the first, where two share a name).</summary>
        public List<MeshParameterNode> ParameterNodes
        {
            get
            {
                var found = new List<MeshParameterNode>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in Nodes)
                    if (node is MeshParameterNode parameter && !string.IsNullOrWhiteSpace(parameter.ParameterName) && names.Add(parameter.ParameterName.Trim()))
                        found.Add(parameter);
                return found;
            }
        }

        /// <summary>The graph's Parameters (each name once), with their defaults.</summary>
        public List<(string name, float value)> ParameterList
            => ParameterNodes.Select(p => (p.ParameterName.Trim(), p.Default)).ToList();

        /// <summary>The meshes it takes when called as a function: its Mesh Input nodes (each name once).</summary>
        public override IReadOnlyList<GraphPinInfo> FunctionInputs
        {
            get
            {
                var found = new List<GraphPinInfo>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in Nodes)
                    if (node is MeshInputNode input && !string.IsNullOrWhiteSpace(input.InputName) && names.Add(input.InputName.Trim()))
                        found.Add(new GraphPinInfo(input.InputName.Trim(), typeof(MeshData), input.Description));
                return found;
            }
        }

        /// <summary>Mesh Graphs can be called from other Mesh Graphs with a Function node.</summary>
        public override bool CallableAsFunction => true;

        /// <summary>What it gives back when called as a function: its model.</summary>
        public override IReadOnlyList<GraphPinInfo> FunctionOutputs
            => new[] { new GraphPinInfo(MeshNode.MeshPort, typeof(MeshData), "The model (what's wired into its Output).") };

        /// <summary>The graph's Output node.</summary>
        public MeshOutputNode Output
        {
            get
            {
                foreach (var node in Nodes)
                    if (node is MeshOutputNode output)
                        return output;
                return null;
            }
        }

        public override void OnGraphValidate()
        {
            base.OnGraphValidate();
            _version++;
            bool removedDuplicate = false;
            foreach (var duplicate in Nodes.OfType<MeshOutputNode>().Skip(1).ToArray())
            {
                RemoveNode(duplicate);
                removedDuplicate = true;
            }
            if (removedDuplicate)
                CacheNodesByType();
        }

        /// <summary>Marks the graph changed (the editor does when a node is edited).</summary>
        public void Touch() => _version++;
    }

    /// <summary>
    /// A node in a Mesh Graph. It turns into a step of the plan (<see cref="Compile"/>, main thread), which makes its
    /// mesh on a background thread.
    /// </summary>
    [Serializable]
    [NodeColor(NodeColors.Object)]
    public abstract class MeshNode : SchematicGraphNode
    {
        /// <summary>The tag every Mesh Graph node has: Mesh Graphs only offer nodes with it.</summary>
        public const string Tag = "Mesh";

        /// <summary>The name of a mesh output.</summary>
        public const string MeshPort = "Mesh";

        /// <summary>This node's step (main thread): read its settings and inputs now, make the mesh later.</summary>
        public abstract MeshOp Compile(MeshCompiler compiler);

        // Mesh nodes aren't run by Schematic flows.
        public override object OnRequestValue(Port port) => null;
    }

    /// <summary>
    /// The finished model, and its LODs: simpler copies (LOD1, LOD2) swapped in as it gets smaller on screen, and
    /// culled below Cull Below.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Output"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh"", ""description"": ""The finished model. With Generate LODs on, it also makes LOD1 and LOD2: copies with LOD1 Quality and LOD2 Quality of its triangles (0.5: half), shown once the model is smaller on screen than LOD0 Until and LOD1 Until (fractions of the screen's height), and hidden below Cull Below (0: never)."", ""inputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Output", Path = "Output", Deletable = false), Tags(MeshGraph.OutputOnlyTag)]
    public sealed class MeshOutputNode : MeshNode
    {
        [Input(MeshPort, Editable = false)] [NonSerialized] public MeshData Mesh;

        [Editable, Tooltip("Make simpler copies for when the model is far away.")]
        public bool GenerateLODs = true;
        [Editable, Tooltip("LOD1's share of the triangles (0.5: half).")]
        public float LOD1Quality = 0.5f;
        [Editable, Tooltip("LOD2's share of the triangles.")]
        public float LOD2Quality = 0.2f;
        [Editable, Tooltip("LOD0 shows while the model is taller than this fraction of the screen.")]
        public float LOD0Until = 0.5f;
        [Editable, Tooltip("LOD1 shows while it's taller than this; LOD2 below.")]
        public float LOD1Until = 0.2f;
        [Editable, Tooltip("Hidden once smaller than this fraction of the screen (0: never hidden).")]
        public float CullBelow = 0.01f;
        [Editable, Tooltip("Hard edges: where faces meet at more than this many degrees, the edge stays sharp (applied to the finished model; -1 keeps the normals the nodes made).")]
        public float SmoothAngle = -1f;

        public override MeshOp Compile(MeshCompiler compiler) => compiler.Mesh(this, MeshPort);
    }

    /// <summary>
    /// A number set per object (the Mesh Graph Renderer's Parameters, or Set Graph Parameters), or by whatever calls
    /// the graph as a function, where <see cref="UI"/> says how it's shown: a slider, a toggle, a dropdown.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Parameter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh"", ""description"": ""A number each object sets for itself, by Name: a tree's height, a rock's seed, a fence's length. It's Default until set (on the Mesh Graph Renderer, or with Set Graph Parameters, which rebuilds the model). Wire it into any number on a node. When the graph is called as a function (a Function node in another Mesh Graph), each Parameter is a pin on that node with its own control there: set Control (Number, Slider, Whole Number, Toggle, Dropdown), Min, Max, Options, Group and Description in the Inspector."", ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Parameter", Path = "Values"), Tags(MeshNode.Tag)]
    public sealed class MeshParameterNode : MeshNode
    {
        [Editable] public string ParameterName = "Size";
        [Editable] public float Default = 1f;
        [Tooltip("How it's shown on Function nodes calling this graph: its control, range, group and description.")]
        public ParameterUI UI = new();
        [Output("Value")] public float Value;

        public override MeshOp Compile(MeshCompiler compiler) => MeshOp.Empty;

        public override object OnRequestValue(Port port)
            => MeshCompiler.Current != null ? MeshCompiler.Current.Parameter(ParameterName, Default) : Default;
    }

    /// <summary>A random number, the same every time for the same Seed (and different for each object given its own).</summary>
    [SchematicDoc(@"{""name"": ""Random"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh"", ""description"": ""A number between Min and Max, picked from Seed: the same seed always gives the same number, so a model only changes when its seed does. Wire a Parameter into Seed for a different one per object."", ""inputs"": [{""name"": ""Seed"", ""type"": ""float""}, {""name"": ""Min"", ""type"": ""float""}, {""name"": ""Max"", ""type"": ""float""}], ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Random", Path = "Values"), Tags(MeshNode.Tag)]
    public sealed class MeshRandomNode : MeshNode
    {
        [Input("Seed", Editable = true)] public float Seed;
        [Input("Min", Editable = true)] public float Min;
        [Input("Max", Editable = true)] public float Max = 1f;
        [Editable, Tooltip("Tells several Random nodes with one Seed apart.")] public int Salt;
        [Output("Value")] public float Value;

        public override MeshOp Compile(MeshCompiler compiler) => MeshOp.Empty;

        public override object OnRequestValue(Port port)
        {
            var compiler = MeshCompiler.Current;
            float seed = compiler != null ? compiler.Number(this, "Seed", Seed) : Seed;
            float min = compiler != null ? compiler.Number(this, "Min", Min) : Min;
            float max = compiler != null ? compiler.Number(this, "Max", Max) : Max;
            unchecked
            {
                uint h = (uint)Mathf.RoundToInt(seed * 1000f) * 2654435761u ^ (uint)Salt * 40503u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return Mathf.Lerp(min, max, (h & 0xFFFFFF) / (float)0xFFFFFF);
            }
        }
    }
}
