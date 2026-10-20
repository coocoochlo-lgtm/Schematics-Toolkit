// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using System.IO;
using UnityEngine;


namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Destroy"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/GameObject"", ""description"": ""Starts a flow when the object is destroyed (or returned to its pool)."", ""use_for"": [""death effects"", ""dropping loot"", ""scoring when something is destroyed""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Path = "Events/GameObject"), Tags("Object")]
    public class OnDestroy : SchematicEventNode
    { }
}