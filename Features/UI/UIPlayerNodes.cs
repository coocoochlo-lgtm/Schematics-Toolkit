// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.UI
{
    // Events for a UI a player drives with their own controller (a possessed UI: see SchematicUIGraph's Player Input).

    [SchematicDoc(@"{""name"": ""On UI Cancel"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/UI"", ""description"": ""In a UI graph: starts a flow when the player who possesses this UI presses its Cancel input (the graph's Player Input). Outputs that player's controller."", ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""going back a menu"", ""closing a pause menu and giving the player their character back""], ""notes"": ""Starts a flow. Only while a controller possesses the UI.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On UI Cancel", Path = "Events/UI"), Tags("UI")]
    public sealed class OnUICancel : SchematicEventNode
    {
        [Output("Controller")] public Controller Controller;
    }

    [SchematicDoc(@"{""name"": ""On UI Selection Changed"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/UI"", ""description"": ""In a UI graph: starts a flow when the player who possesses this UI moves its selection to another element. Outputs that element's name (from UI Builder)."", ""outputs"": [{""name"": ""Element Name"", ""type"": ""string""}], ""use_for"": [""showing a description of the hovered item"", ""a sound when the selection moves""], ""notes"": ""Starts a flow. Only while a controller possesses the UI.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On UI Selection Changed", Path = "Events/UI"), Tags("UI")]
    public sealed class OnUISelectionChanged : SchematicEventNode
    {
        [Output("Element Name")] public string ElementName;
    }
}
