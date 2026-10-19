// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Cast To Float"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Casts"", ""description"": ""Treats a value as a number."", ""inputs"": [{""name"": ""Value"", ""type"": ""object""}], ""outputs"": [{""name"": ""Casted"", ""type"": ""T""}], ""use_for"": [""using a value from a list or Signal in math""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Casts"), Tags("Default")]
    public class CastToFloat : Cast<float>
    { }
}