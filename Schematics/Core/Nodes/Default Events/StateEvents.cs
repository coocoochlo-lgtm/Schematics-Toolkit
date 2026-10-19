using BlueGraph;
using System;

namespace Remedy.Schematics
{
    /// <summary>In a state's graph: runs when the state is entered.</summary>
    [SchematicDoc(@"{""name"": ""On State Enter"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/State Machine"", ""description"": ""In a state's graph: runs when the state is entered."", ""use_for"": [""set things up when a state begins (play an animation, start a timer)""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Only inside a state's graph.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On State Enter", Path = "Events/State Machine"), Tags("Object", "State")]
    public class OnStateEnter : SchematicEventNode { }

    /// <summary>In a state's graph: runs every frame while the state (or one of its children) is active.</summary>
    [SchematicDoc(@"{""name"": ""On State Update"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/State Machine"", ""description"": ""In a state's graph: runs every frame while the state (or one of its children) is active."", ""use_for"": [""per-frame logic that should only run in one state""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Only inside a state's graph.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On State Update", Path = "Events/State Machine"), Tags("Object", "State")]
    public class OnStateUpdate : SchematicEventNode { }

    /// <summary>In a state's graph: runs when the state is left.</summary>
    [SchematicDoc(@"{""name"": ""On State Exit"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/State Machine"", ""description"": ""In a state's graph: runs when the state is left."", ""use_for"": [""clean up when leaving a state""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Only inside a state's graph.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On State Exit", Path = "Events/State Machine"), Tags("Object", "State")]
    public class OnStateExit : SchematicEventNode { }
}
