// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Vector 3"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""Creates a new Vector3, either from input Floats or new values defined in the Node"", ""inputs"": [{""name"": ""x"", ""type"": ""float""}, {""name"": ""y"", ""type"": ""float""}, {""name"": ""z"", ""type"": ""float""}], ""outputs"": [{""name"": ""value"", ""type"": ""Vector3""}], ""use_for"": [""Creating a Vector3 value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class Vector3Value : SchematicGraphNode
    {
        [Input("x", Editable = true)]
        public float X;

        [Input("y", Editable = true)]
        public float Y;

        [Input("z", Editable = true)]
        public float Z;

        [Output("value")]
        public UnityEngine.Vector3 Value;

        public override object OnRequestValue(Port port)
        {
            X = GetInputValue<float>("x", X);
            Y = GetInputValue<float>("y", Y);
            Z = GetInputValue<float>("z", Z);

            Value = new UnityEngine.Vector3(X, Y, Z);

            SetOutputValue(nameof(Value), Value);
            return Value;
        }
    }
}   