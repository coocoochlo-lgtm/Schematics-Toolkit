using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.CharacterControllers.LedgeGrab
{
    /// <summary>
    /// Hangs from a ledge in front of the character, and clambers up it on jump. Input comes from the Motion Context's Signals.
    /// </summary>
    [RequireComponent(typeof(Box3D.Hybrid.Box3DBody))]
    [RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
    [SchematicBehaviorPath("Movement/3D/Ledge Grab Controller")]
    public class LedgeGrabController : SchematicBehavior
    {
        [OutputSignal]
        public readonly SignalHandle OnClamber;
        [OutputSignal]
        public readonly SignalHandle OnLedgeGrabExitted;

        public readonly VariableHandle<LedgeGrabControllerProperties> Properties = (LedgeGrabControllerProperties)null;

        public readonly VariableHandle<Quaternion> FacingRotation = Quaternion.identity;

        public enum WallSide { Left, Right }
        public readonly VariableHandle<WallSide> CurrentWallSide = WallSide.Left;

        private readonly VariableHandle<RaycastHit> _lastWallHit = default(RaycastHit);
        private readonly VariableHandle<List<Vector3>> _clamberPoints = new List<Vector3> { Vector3.zero, Vector3.zero };

        protected override void OnStart()
        {
            var instance = Self;
            var motion = instance.GetSchematicBehavior<CharacterMotionContext>();
            motion?.JumpInput.Subscribe(instance, value => Jump(instance, value));
        }

        protected override void OnEnable()
        {
            var properties = Properties.Value;
            var motion = Self.GetSchematicBehavior<CharacterMotionContext>();
            var raycast = Self.GetSchematicBehavior<CharacterRaycastContext>();
            if (properties == null || motion == null || raycast == null)
                return;

            motion.WallCheckInMoveDirection();
            if (raycast.WallCastResult.Value.TryGetClosestHit(out RaycastHit hit))
                StickOnWall(Self, properties, motion, raycast, hit);
        }

        protected override void OnDisable()
        {
            OnLedgeGrabExitted.Invoke(Self);
        }

        public void Jump(BehaviorInstance instance, bool value)
        {
            var properties = Properties[instance];
            var motion = instance.GetSchematicBehavior<CharacterMotionContext>();
            if (!value || properties == null || motion == null)
                return;

            var position = instance.Transform.position;
            var points = _clamberPoints[instance];
            points[0] = position + properties.ClamberHeight * Vector3.up;
            points[1] = (position + properties.ClamberHeight * Vector3.up) + -_lastWallHit[instance].normal * properties.ClamberForward;
            motion.FollowSpringPath(position.y, points);
            OnClamber.Invoke(instance);
        }

        private void StickOnWall(BehaviorInstance instance, LedgeGrabControllerProperties properties, CharacterMotionContext motion, CharacterRaycastContext raycast, RaycastHit hit)
        {
            _lastWallHit.Value = hit;
            var transform = instance.Transform;

            var facingDir = -hit.normal;

            FacingRotation.Value = Quaternion.LookRotation(facingDir, Vector3.up);

            float side = Vector3.Dot(facingDir, motion.HorizontalDirection);
            CurrentWallSide.Value = side > 0 ? WallSide.Left : WallSide.Right;

            var raycastProperties = raycast.Properties.Value;
            if (raycastProperties == null)
                return;

            var ledgeResult = RaycastUtility.SphereCast(transform, transform.position, facingDir, properties.LedgeCheckRadius, raycastProperties.CollisionMask, -1, raycastProperties.MaxGroundSlopeAngle);

            if (hit.distance < properties.GapFromWall)
            {
                if (ledgeResult.TryGetClosestHit(out RaycastHit ledgeHit))
                {
                    var ledgeProjectedPosition = Vector3.ProjectOnPlane(transform.position, ledgeHit.normal);
                    var wallProjectedPosition = Vector3.ProjectOnPlane(transform.position, hit.normal);

                    ledgeProjectedPosition.y = 0;
                    wallProjectedPosition.x = 0;
                    wallProjectedPosition.y = 0;

                    var actualPosition = ledgeProjectedPosition + wallProjectedPosition;

                    motion.TeleportToPosition(actualPosition + hit.normal * properties.GapFromWall);
                }
            }
        }
    }
}
