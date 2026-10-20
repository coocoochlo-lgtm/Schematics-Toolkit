// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Start"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow when the game starts. Outputs the level name."", ""outputs"": [{""name"": ""LevelName"", ""type"": ""string""}], ""use_for"": [""game-wide setup"", ""starting music""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Runtime events belong in singleton graphs.""}")]
[NodeColor(NodeColors.Event)]
[Node(Path = "Events"), Tags("Runtime")]
public class OnStart : SchematicEventNode
{
    [Output]
    public string LevelName;
}