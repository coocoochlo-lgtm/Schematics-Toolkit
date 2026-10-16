// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Cast To Mono Behaviour"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Casts"", ""description"": ""Treats a value as a script component."", ""inputs"": [{""name"": ""Value"", ""type"": ""object""}], ""outputs"": [{""name"": ""Casted"", ""type"": ""T""}], ""use_for"": [""reaching plain Unity scripts""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Casts"), Tags("Default")]
    public class CastToMonoBehaviour : Cast<MonoBehaviour>
    { }
}