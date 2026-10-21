// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Start"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""Starts a flow once at the start: in a singleton graph, when the game starts; in an object's graph, when the object starts (right after On Create). Outputs the level name."", ""outputs"": [{""name"": ""LevelName"", ""type"": ""string""}], ""use_for"": [""game-wide setup"", ""starting music"", ""an object's looping sound or effect""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[NodeColor(NodeColors.Event)]
[Node(Path = "Events"), Tags("Runtime", "Object")]
public class OnStart : SchematicEventNode
{
    [Output]
    public string LevelName;
}