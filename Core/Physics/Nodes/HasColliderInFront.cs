// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using Remedy.Box3DPhysics;
using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace Remedy.Schematics
{
    /// <summary>
    /// True when a sphere cast from the object along its forward axis hits something (with the given Int Tag, if set; with nothing connected to Object, from the object running the graph)
    /// within the distance. Uses Box3D; moved here from Schematics Core, so existing graphs keep it.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Has Collider Forward"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Collider"", ""description"": ""True when a sphere cast from the object along its forward axis hits something (with the given Int Tag, if set; with nothing connected to Object, from the object running the graph) within the distance. Uses Box3D; moved here from Schematics Core, so existing graphs keep it."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Other Tag"", ""type"": ""IntTag""}, {""name"": ""Distance"", ""type"": ""float""}, {""name"": ""Radius"", ""type"": ""float""}], ""outputs"": [{""name"": ""Other GameObject"", ""type"": ""GameObject""}], ""use_for"": [""detecting walls or enemies ahead"", ""AI obstacle checks""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [NodeColor(NodeColors.Condition)]
    [Node(Path = "Collider"), Tags("Object")]
    [MovedFrom(false, "Remedy.Schematics", "Remedy.Schematics.Core", "HasColliderForward")]
    public class HasColliderForward : SchematicConditionalNode
    {
        public const string OtherPort = "Other GameObject";

        [Input("Object")]
        public GameObject Object;
        [Input("Other Tag", Editable = true)]
        public IntTag OtherTag = IntTags.Untagged;
        [Input("Distance", Editable = true)]
        public float Distance = 5.0f;
        [Input("Radius", Editable = true)]
        public float Radius = 1.0f;

        [Output(OtherPort)]
        public GameObject OtherGameObject;

        // The Unity tag name older versions of this node saved; moved onto OtherTag by name.
        [SerializeField, HideInInspector, FormerlySerializedAs("Tag")]
        private string _legacyTag;

        public override void OnValidate()
        {
            MigrateLegacyTag(ref _legacyTag, ref OtherTag);
            EnsureDataPort("Other Tag", typeof(IntTag), PortDirection.Input);
            base.OnValidate();
        }

        public override bool Condition() => Cast() != null;

        public override object OnRequestValue(Port port)
        {
            if (port.Name == OtherPort)
                return Cast();
            return base.OnRequestValue(port);
        }

        /// <summary>What the sphere cast hits (with the tag, when one is set), or null.</summary>
        private GameObject Cast()
        {
            if (_legacyTag != null)
                MigrateLegacyTag(ref _legacyTag, ref OtherTag);

            // Locals rather than the fields: the fields are the values typed on the node. With nothing connected to
            // Object, the object running the graph.
            var tag = GetInputValue("Other Tag", OtherTag);
            float distance = GetInputValue("Distance", Distance);
            float radius = GetInputValue("Radius", Radius);
            var target = GetInputValue("Object", Object);
            if (target == null)
                target = RunningInstance;
            if (target == null)
                return null;

            var transform = target.transform;
            var self = target.GetComponentInParent<Box3D.Hybrid.Box3DBody>();
            if (SchematicPhysics.SphereCast(transform.position, radius, transform.forward, out _, out var other, distance, ~0, self)
                && other != null && (!tag.IsTagged || other.HasIntTag(tag)))
                return other;
            return null;
        }
    }
}
