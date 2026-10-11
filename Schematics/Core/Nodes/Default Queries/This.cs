// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""This"", ""kind"": ""node"", ""flow"": ""query"", ""description"": ""The object running this graph."", ""outputs"": [{""name"": ""value"", ""type"": ""object""}], ""use_for"": [""passing 'myself' to other nodes and Signals""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = ""), Tags("Object")]
    public class This : SchematicGraphNode
    {
        [Output(" ")]
        public object Value;

        public override object OnRequestValue(Port port)
        {
            return GameObject;
        }
    }
}