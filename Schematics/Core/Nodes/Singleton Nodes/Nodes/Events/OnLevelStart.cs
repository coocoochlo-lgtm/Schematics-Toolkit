// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Level Start"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow when an additive level (Level Streaming: Load Level, a Level Streaming Zone, or LevelLoader) has finished streaming in. Outputs the level name (scene or prefab name)."", ""outputs"": [{""name"": ""LevelName"", ""type"": ""string""}], ""use_for"": [""intro sequences"", ""resetting score per level""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[Node(Path = "Events"), Tags("Runtime")]
public class OnLevelStart : SchematicEventNode
{
    [Output]
    public string LevelName;
}