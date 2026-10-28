// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Meshes
{
    // Mesh Graphs as functions: a Function node calls another Mesh Graph (its Parameters become the node's pins, each
    // with its own control), Mesh Inputs take the meshes it's given, and its Output is what it gives back. Functions
    // build on functions: a face on a head on a body.

    /// <summary>
    /// A mesh the graph takes when it's called as a function (a Function node's pin of the same name). Used on its own,
    /// the graph has nothing given, so Preview stands in: wire in something to look at while making it.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Mesh Input"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Functions"", ""description"": ""A mesh this graph takes when another Mesh Graph calls it as a function: the Function node calling it gets a Mesh pin named Name, and what's wired there comes out here. When the graph isn't being called (opened on its own, or on a renderer), what's wired into Preview comes out instead, so there's something to see while making it."", ""inputs"": [{""name"": ""Preview"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}], ""use_for"": [""a function that changes a mesh it's given (a face on a head, a bevel, a damage pass)""]}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Mesh Input", Path = "Functions"), Tags(MeshNode.Tag)]
    public sealed class MeshInputNode : MeshNode
    {
        [Editable] public string InputName = "Mesh";
        [Tooltip("What it should be given, for whoever (or whatever AI) calls the graph.")]
        [TextArea(1, 3)]
        public string Description = "";
        [Input("Preview", Editable = false)] [NonSerialized] public MeshData Preview;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler compiler)
            => compiler.Input(InputName, out var given) ? given : compiler.IsCall ? MeshOp.Empty : compiler.Mesh(this, "Preview");
    }

    /// <summary>
    /// Calls another Mesh Graph as a function: its Mesh Inputs and Parameters are this node's pins (each Parameter with
    /// the control it was given: a slider, a toggle, a dropdown, set right on the node when nothing's wired in), and
    /// its model comes out of Mesh.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Function"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Functions"", ""description"": ""Calls another Mesh Graph (Graph) as a function and gives back its model. Its Mesh Inputs and Parameters become pins here: wire meshes and numbers in, or set the Parameters right on the node with the controls they were given (sliders, toggles, dropdowns). Graphs marked In Library are in the node search under Functions; dropping a Mesh Graph into a Mesh Graph makes one. Double-click to open the graph it calls."", ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}], ""use_for"": [""reusing a model or a modelling step across graphs"", ""a library of parts (a head, a limb, a window) with sliders for their features"", ""macros built from functions""]}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Function", Path = "Functions"), Tags(MeshNode.Tag)]
    [AssetNode(typeof(MeshGraph), Title = "{0}", Field = nameof(Calls))]
    public sealed class MeshFunctionNode : MeshNode, IDynamicPorts
    {
        [Editable("Graph")] public MeshGraph Calls;
        [SerializeField, HideInInspector] private List<FunctionArgument> _arguments = new();
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        /// <summary>The values set on the node for the graph's Parameters (by name).</summary>
        public List<FunctionArgument> Arguments => _arguments ??= new();

        /// <summary>The pin for one of the graph's Parameters or Mesh Inputs (named after it, unless that's Mesh).</summary>
        public static string PinFor(string name) => string.Equals(name, MeshPort, StringComparison.OrdinalIgnoreCase) ? name + " In" : name;

        /// <summary>The graph's Parameter behind a pin, if it is one.</summary>
        public bool ParameterFor(string pin, out GraphParameter parameter)
        {
            parameter = default;
            if (Calls == null)
                return false;
            foreach (var p in Calls.GraphParameters)
                if (PinFor(p.Name) == pin)
                {
                    parameter = p;
                    return true;
                }
            return false;
        }

        /// <summary>The value a Parameter has on this node when nothing's wired into its pin.</summary>
        public float ValueOf(GraphParameter parameter) => FunctionArguments.Get(Arguments, parameter);

        public void SetValue(GraphParameter parameter, float value) => FunctionArguments.Set(Arguments, parameter, value);

        /// <summary>A pin per Mesh Input of the graph, then one per Parameter, in the graph's order.</summary>
        public bool SyncPorts()
        {
            var wanted = new List<(string, Type)>();
            if (Calls != null)
            {
                foreach (var input in Calls.FunctionInputs)
                    wanted.Add((PinFor(input.Name), typeof(MeshData)));
                foreach (var parameter in Calls.GraphParameters)
                    wanted.Add((PinFor(parameter.Name), typeof(float)));
            }
            return DynamicPorts.SyncInputs(this, wanted, port => port.Direction == PortDirection.Input);
        }

        public override void OnValidate()
        {
            base.OnValidate();
            if (Calls != null && (string.IsNullOrEmpty(Name) || Name == "Function"))
                Name = Calls.name;
            if (SyncPorts())
                DynamicPorts.Notify(this);
        }

        public override MeshOp Compile(MeshCompiler compiler)
        {
            if (Calls == null)
                return MeshOp.Empty;
            var parameters = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in Calls.GraphParameters)
            {
                float value = compiler.Number(this, PinFor(parameter.Name), ValueOf(parameter));
                parameters[parameter.Name] = parameter.UI != null ? parameter.UI.Clamp(value) : value;
            }
            var inputs = new Dictionary<string, MeshOp>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in Calls.FunctionInputs)
                inputs[input.Name] = compiler.Mesh(this, PinFor(input.Name));
            return compiler.Call(this, Calls, parameters, inputs);
        }
    }
}
