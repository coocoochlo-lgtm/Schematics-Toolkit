// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Cast To Game Object"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Casts"", ""description"": ""Treats a value (Transform, component) as its GameObject."", ""inputs"": [{""name"": ""Value"", ""type"": ""object""}], ""outputs"": [{""name"": ""Casted"", ""type"": ""T""}], ""use_for"": [""getting the object from a collider or component""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Casts"), Tags("Default")]
    public class CastToGameObject : Cast<GameObject>
    {
        // Cast<GameObject> already turns a Transform or any other component into its GameObject.
    }
}