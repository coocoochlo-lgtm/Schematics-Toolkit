using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A physics impact on an object, independent of the physics engine that produced it (the toolkit uses Box3D).
    /// Delivered to Behaviors through <see cref="SchematicBehavior.OnImpact"/>.
    /// </summary>
    public readonly struct PhysicsImpact
    {
        /// <summary>World-space impact point.</summary>
        public readonly Vector3 Point;
        /// <summary>Unit direction of the impact, pointing into this object's surface (the way it gets pushed).</summary>
        public readonly Vector3 Direction;
        /// <summary>How fast the surfaces were approaching, in m/s.</summary>
        public readonly float Speed;
        /// <summary>The other object, or null for plain static geometry.</summary>
        public readonly GameObject Other;

        /// <summary>The velocity of the impact relative to this object (Direction × Speed).</summary>
        public Vector3 RelativeVelocity => Direction * Speed;

        public PhysicsImpact(Vector3 point, Vector3 direction, float speed, GameObject other)
        {
            Point = point;
            Direction = direction;
            Speed = speed;
            Other = other;
        }
    }
}
