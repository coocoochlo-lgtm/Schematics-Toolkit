// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;


namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Update"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""Starts a flow every frame while the object is active."", ""use_for"": [""continuous checks"", ""following or aiming at something"", ""timers""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Runs every frame: keep what follows it cheap.""}")]
    [Serializable]
    [Node(Path = "Events"), Tags("Default")]
    public class OnUpdate : SchematicEventNode
    {
    }
}