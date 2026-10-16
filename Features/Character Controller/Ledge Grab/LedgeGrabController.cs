using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.CharacterControllers.LedgeGrab
{
    /// <summary>
    /// Hangs from a ledge in front of the character, and clambers up it on jump. Input comes from the Motion Context's Signals.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Ledge Grab Controller"", ""kind"": ""behavior"", ""menu"": ""Movement/3D/Ledge Grab Controller"", ""description"": ""Hangs from a ledge in front of the character, and clambers up it on jump. Input comes from the Motion Context's Signals."", ""variables"": [""Properties"", ""FacingRotation"", ""CurrentWallSide""], ""signals"": [""OnClamber"", ""OnLedgeGrabExitted""], ""use_for"": [""grabbing and climbing ledges""]}")]
    [RequireComponent(typeof(Box3D.Hybrid.Box3DBody))]
    [RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
    [SchematicBehaviorPath("Movement/3D/Ledge Grab Controller")]
    public class LedgeGrabController : SchematicBehavior
    {
        [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.MoveInput))]
        public readonly SignalHandle<Vector2> MoveInput;
        [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.JumpInput))]
        public readonly SignalHandle<bool> JumpInput;
        [SchematicDoc(@"{""name"": ""OnClamber"", ""kind"": ""signal"", ""behavior"": ""Ledge Grab Controller"", ""direction"": ""output"", ""description"": ""Sent by Ledge Grab Controller when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle OnClamber;
        [SchematicDoc(@"{""name"": ""OnLedgeGrabExitted"", ""kind"": ""signal"", ""behavior"": ""Ledge Grab Controller"", ""direction"": ""output"", ""description"": ""Sent by Ledge Grab Controller when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle OnLedgeGrabExitted;

        [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Ledge Grab Controller"", ""type"": ""LedgeGrabControllerProperties"", ""default"": ""null"", ""description"": ""Properties (LedgeGrabControllerProperties) of Ledge Grab Controller.""}")]
        public readonly VariableHandle<LedgeGrabControllerProperties> Properties = (LedgeGrabControllerProperties)null;

        [SchematicDoc(@"{""name"": ""FacingRotation"", ""kind"": ""variable"", ""behavior"": ""Ledge Grab Controller"", ""type"": ""Quaternion"", ""default"": ""Quaternion.identity"", ""description"": ""Facing Rotation (Quaternion) of Ledge Grab Controller.""}")]
        public readonly VariableHandle<Quaternion> FacingRotation = Quaternion.identity;

        public enum WallSide { Left, Right }
        [SchematicDoc(@"{""name"": ""CurrentWallSide"", ""kind"": ""variable"", ""behavior"": ""Ledge Grab Controller"", ""type"": ""WallSide"", ""default"": ""WallSide.Left"", ""description"": ""Current Wall Side (WallSide) of Ledge Grab Controller.""}")]
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
