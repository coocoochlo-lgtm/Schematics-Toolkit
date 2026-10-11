using Box3D;
using Box3D.Hybrid;
using UnityEngine;

namespace Remedy.Box3DPhysics
{
    /// <summary>
    /// Rigidbody-style helpers for Box3D bodies, safe to call before the native body exists (the component creates it
    /// in Awake, which may run after a Behavior first touches it): calls that need the live body do nothing until then.
    /// </summary>
    public static class PhysicsBodies
    {
        public static bool IsLive(this Box3DBody body) => body != null && body.Body.IsValid;

        public static Vector3 GetVelocity(this Box3DBody body)
            => body.IsLive() ? (Vector3)body.Body.LinearVelocity : Vector3.zero;

        public static void SetVelocity(this Box3DBody body, Vector3 velocity)
        {
            if (!body.IsLive()) return;
            var native = body.Body;
            native.LinearVelocity = velocity;
        }

        public static bool IsKinematic(this Box3DBody body) => body != null && body.BodyType == Box3DBodyType.Kinematic;

        /// <summary>Switches between kinematic (moved by its Transform) and dynamic (moved by the solver).</summary>
        public static void SetKinematic(this Box3DBody body, bool kinematic)
        {
            if (body == null) return;
            var type = kinematic ? Box3DBodyType.Kinematic : Box3DBodyType.Dynamic;
            if (body.BodyType != type)
                body.BodyType = type;
        }

        /// <summary>Stops the body rotating from collisions (like RigidbodyConstraints.FreezeRotation).</summary>
        public static void FreezeRotation(this Box3DBody body)
        {
            if (!body.IsLive()) return;
            body.Body.SetMotionLocks(new MotionLocks { AngularX = true, AngularY = true, AngularZ = true });
        }

        /// <summary>The body's mass (1 until the native body exists).</summary>
        public static float GetMass(this Box3DBody body) => body.IsLive() ? body.Body.GetMass() : 1f;

        public static void SetGravityScale(this Box3DBody body, float scale)
        {
            if (body.IsLive()) body.Body.SetGravityScale(scale);
        }

        public static void WakeUp(this Box3DBody body)
        {
            if (body.IsLive()) body.Body.SetAwake(true);
        }

        public static void Sleep(this Box3DBody body)
        {
            if (body.IsLive()) body.Body.SetAwake(false);
        }

        /// <summary>Moves the body to a position: kinematic bodies follow their Transform, dynamic ones are teleported.</summary>
        public static void Teleport(this Box3DBody body, Vector3 position)
        {
            if (body == null) return;
            if (body.IsKinematic())
                body.transform.position = position;
            else
                body.Position = position;
        }

        public static void TeleportRotation(this Box3DBody body, Quaternion rotation)
        {
            if (body == null) return;
            if (body.IsKinematic())
                body.transform.rotation = rotation;
            else
                body.Rotation = rotation;
        }
    }
}
