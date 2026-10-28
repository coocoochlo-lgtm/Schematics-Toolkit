// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using UnityEngine;

[SchematicDoc(@"{""name"": ""On Level Exit"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""In a singleton graph: starts a flow when an additive level (Level Streaming) is removed again. Outputs the level name (scene or prefab name)."", ""outputs"": [{""name"": ""LevelName"", ""type"": ""string""}], ""use_for"": [""results screens"", ""saving progress""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[NodeColor(NodeColors.Event)]
[Node(Path = "Events"), Tags("Runtime")]
public class OnLevelExit : SchematicEventNode
{
    [Tooltip("The level's name (its scene or prefab name).")]
    [Output]
    public string LevelName;
    
}