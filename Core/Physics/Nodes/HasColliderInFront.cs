// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Box3DPhysics;
using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Remedy.Schematics
{
    /// <summary>
    /// True when a sphere cast from the object along its forward axis hits something (with the given tag, if set)
    /// within the distance. Uses Box3D; moved here from Schematics Core, so existing graphs keep it.
    /// </summary>
    [Serializable]
    [Node(Path = "Collider"), Tags("Object")]
    [MovedFrom(false, "Remedy.Schematics", "Remedy.Schematics.Core", "HasColliderForward")]
    public class HasColliderForward : SchematicConditionalNode
    {
        [Input("Object")]
        public GameObject Object;
        [Input("Other Tag", Editable = true)]
        public string Tag = "";
        [Input("Distance", Editable = true)]
        public float Distance = 5.0f;
        [Input("Radius", Editable = true)]
        public float Radius = 1.0f;

        [Output("Other GameObject")]
        public GameObject OtherGameObject;

        public override bool Condition()
        {
            // (These used to read a port named "OtherTag", which doesn't exist, and the distance was ignored.)
            Tag = GetInputValue("Other Tag", Tag);
            Distance = GetInputValue("Distance", Distance);
            Radius = GetInputValue("Radius", Radius);

            var target = GetInputValue("Object", Object);
            if (target == null)
                return false;

            var transform = target.transform;
            var self = target.GetComponentInParent<Box3D.Hybrid.Box3DBody>();
            if (SchematicPhysics.SphereCast(transform.position, Radius, transform.forward, out _, out var other, Distance, ~0, self)
                && other != null && (Tag == "" || other.CompareTag(Tag)))
            {
                OtherGameObject = other;
                SetOutputValue("Other GameObject", other);
                return true;
            }
            return false;
        }

        public override object OnRequestValue(Port port)
        {
            if (port.Name == "Other GameObject")
                return OtherGameObject;

            return base.OnRequestValue(port);
        }
    }
}
