// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On After First Scene Loaded"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow after the first scene loads."", ""use_for"": [""showing the main menu"", ""first-time setup""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[NodeColor(NodeColors.Event)]
[Node(Path = "Events"), Tags("Runtime")]
public class OnAfterFirstSceneLoaded : SchematicEventNode
{ }