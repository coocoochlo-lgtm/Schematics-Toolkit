// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Get Child"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Schematic"", ""description"": ""A child Transform of this object."", ""outputs"": [{""name"": ""Child"", ""type"": ""Transform""}], ""use_for"": [""referring to part of the object""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Schematic"), Tags("Default")]
    public class GetChild : SchematicGraphNode
    {
        [Output]
        public Transform Child;

        public override object OnRequestValue(Port port)
        {
            return Child;
        }
    }
}