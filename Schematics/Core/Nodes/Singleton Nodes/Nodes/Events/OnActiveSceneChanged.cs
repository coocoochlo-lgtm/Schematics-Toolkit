// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Active Scene Changed"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow when the active scene changes."", ""use_for"": [""switching music or lighting per level""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[NodeColor(NodeColors.Event)]
[Node(Path = "Events"), Tags("Runtime")]
public class OnActiveSceneChanged : SchematicEventNode
{ }