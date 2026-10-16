// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using System.IO;
using UnityEngine;


namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Create"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/GameObject"", ""description"": ""Starts a flow once when the object is created (spawned)."", ""use_for"": [""initial setup"", ""spawn effects"", ""randomizing starting values""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
    [Serializable]
    [Node(Path = "Events/GameObject"), Tags("Object")]
    public class OnCreate : SchematicEventNode
    { }
}