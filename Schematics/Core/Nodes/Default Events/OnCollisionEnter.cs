// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Collision Enter"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/Collision"", ""description"": ""Starts a flow when this object bumps into something solid. Outputs the other collider, its object, Rigidbody and Transform, the impulse and the relative velocity."", ""outputs"": [{""name"": ""OtherCollider"", ""type"": ""Collider""}, {""name"": ""Impulse"", ""type"": ""Vector3""}, {""name"": ""RelativeVelocity"", ""type"": ""Vector3""}, {""name"": ""OtherRigidbody"", ""type"": ""Rigidbody""}, {""name"": ""OtherArticulationBody"", ""type"": ""ArticulationBody""}, {""name"": ""OtherTransform"", ""type"": ""Transform""}, {""name"": ""OtherGameObject"", ""type"": ""GameObject""}], ""use_for"": [""impact damage"", ""landing sounds"", ""breakable objects"", ""projectiles hitting things""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
    [Serializable]
    [Node(Path = "Events/Collision"), Tags("Object")]
    public class OnCollisionEnter : SchematicEventNode
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