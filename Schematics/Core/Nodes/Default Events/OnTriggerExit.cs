// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Trigger Exit"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/Collision"", ""description"": ""Starts a flow when another collider leaves this object's trigger. Outputs the collider that left."", ""outputs"": [{""name"": ""collider"", ""type"": ""Collider""}], ""use_for"": [""leaving zones"", ""stopping an effect when the player walks away""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Path = "Events/Collision"), Tags("Object")]
    public class OnTriggerExit : SchematicEventNode
    {
        [Output]
        public Collider collider;
    }
}