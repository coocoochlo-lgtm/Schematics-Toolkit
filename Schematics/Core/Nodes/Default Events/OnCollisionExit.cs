// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Collision Exit"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/Collision"", ""description"": ""Starts a flow when this object stops touching something solid. Outputs the other object and the contact details."", ""outputs"": [{""name"": ""OtherCollider"", ""type"": ""Collider""}, {""name"": ""Impulse"", ""type"": ""Vector3""}, {""name"": ""RelativeVelocity"", ""type"": ""Vector3""}, {""name"": ""OtherRigidbody"", ""type"": ""Rigidbody""}, {""name"": ""OtherArticulationBody"", ""type"": ""ArticulationBody""}, {""name"": ""OtherTransform"", ""type"": ""Transform""}, {""name"": ""OtherGameObject"", ""type"": ""GameObject""}], ""use_for"": [""leaving a platform"", ""ending a contact effect""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Path = "Events/Collision"), Tags("Object")]
    public class OnCollisionExit : SchematicEventNode
    {
        [Output]
        public Collider OtherCollider;
        [Output]
        public Vector3 Impulse;
        [Output]
        public Vector3 RelativeVelocity;
        [Output]
        public Rigidbody OtherRigidbody;
        [Output]
        public ArticulationBody OtherArticulationBody;
        [Output]
        public Transform OtherTransform;
        [Output]
        public GameObject OtherGameObject;
    }
}