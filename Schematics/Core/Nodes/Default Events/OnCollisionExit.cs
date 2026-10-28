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
        [Tooltip("The other object's collider.")]
        [Output]
        public Collider OtherCollider;
        [Tooltip("How hard the hit was: the total push the contact gave (its length; bigger is harder).")]
        [Output]
        public Vector3 Impulse;
        [Tooltip("How fast the two were moving into each other.")]
        [Output]
        public Vector3 RelativeVelocity;
        [Tooltip("The other object's Rigidbody (empty if it has none).")]
        [Output]
        public Rigidbody OtherRigidbody;
        [Tooltip("The other object's Articulation Body (empty if it has none).")]
        [Output]
        public ArticulationBody OtherArticulationBody;
        [Tooltip("The other object's Transform.")]
        [Output]
        public Transform OtherTransform;
        [Tooltip("The other object.")]
        [Output]
        public GameObject OtherGameObject;
    }
}