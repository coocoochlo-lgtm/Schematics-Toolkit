// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Runtime Update"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow every frame for the whole game."", ""use_for"": [""game-wide timers"", ""global input""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Runtime events belong in singleton graphs.""}")]
[NodeColor(NodeColors.Event)]
[Node(Path = "Events"), Tags("Runtime")]
public class OnRuntimeUpdate : SchematicEventNode
{ }