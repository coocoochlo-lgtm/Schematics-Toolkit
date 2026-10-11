// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Assemblies Loaded"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow once the game's code has loaded, the earliest point."", ""use_for"": [""registering systems""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[Node(Path = "Events"), Tags("Runtime")]
public class OnAssembliesLoaded : SchematicEventNode
{ }