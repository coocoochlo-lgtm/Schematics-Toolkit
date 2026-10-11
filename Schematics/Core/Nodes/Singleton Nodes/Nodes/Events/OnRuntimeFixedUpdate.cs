// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Runtime Fixed Update"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow every physics step."", ""use_for"": [""game-wide physics logic""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[Node(Path = "Events"), Tags("Runtime")]
public class OnRuntimeFixedUpdate : SchematicEventNode
{ }