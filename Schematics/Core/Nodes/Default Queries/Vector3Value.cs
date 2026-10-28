// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Vector 3"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""Creates a new Vector3, either from input Floats or new values defined in the Node"", ""inputs"": [{""name"": ""x"", ""type"": ""float""}, {""name"": ""y"", ""type"": ""float""}, {""name"": ""z"", ""type"": ""float""}], ""outputs"": [{""name"": ""value"", ""type"": ""Vector3""}], ""use_for"": [""Creating a Vector3 value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Values"), Tags("Default")]
    public class Vector3Value : SchematicGraphNode
    {
        [Tooltip("The X part (left/right).")]
        [Input("x", Editable = true)]
        public float X;

        [Tooltip("The Y part (up/down).")]
        [Input("y", Editable = true)]
        public float Y;

        [Tooltip("The Z part (forward/back).")]
        [Input("z", Editable = true)]
        public float Z;

        [Output("value")]
        public UnityEngine.Vector3 Value;

        public override object OnRequestValue(Port port)
        {
            // Locals, not the fields: the fields are the numbers typed on the node.
            return new UnityEngine.Vector3(
                GetInputValue("x", X),
                GetInputValue("y", Y),
                GetInputValue("z", Z));
        }
    }
}   