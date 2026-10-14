// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Scene Unloaded"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow when a scene unloads."", ""use_for"": [""saving progress"", ""cleanup between levels""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[Node(Path = "Events"), Tags("Runtime")]
public class OnSceneUnloaded : SchematicEventNode
{ }