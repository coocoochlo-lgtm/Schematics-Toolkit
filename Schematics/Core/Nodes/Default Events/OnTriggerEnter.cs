// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Trigger Enter"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/Collision"", ""description"": ""Starts a flow when another collider enters this object's trigger. Outputs the collider that entered."", ""outputs"": [{""name"": ""collider"", ""type"": ""Collider""}], ""use_for"": [""pickups"", ""pressure plates"", ""entering zones"", ""detecting the player nearby""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. The object needs a trigger collider.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Path = "Events/Collision"), Tags("Object")]
    public class OnTriggerEnter : SchematicEventNode
    {
        [Output]
        public Collider collider;
    }
}