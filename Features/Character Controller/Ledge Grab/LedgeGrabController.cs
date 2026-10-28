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
        [Tooltip("Move input (linked to the Motion Context's): steers along the ledge, or lets go when pulled away from it.")]
        [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.MoveInput))]
        public readonly SignalHandle<Vector2> MoveInput;
        [Tooltip("Jump input (linked to the Motion Context's): clambers up onto the ledge.")]
        [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.JumpInput))]
        public readonly SignalHandle<bool> JumpInput;
        [SchematicDoc(@"{""name"": ""OnClamber"", ""kind"": ""signal"", ""behavior"": ""Ledge Grab Controller"", ""direction"": ""output"", ""description"": ""Sent when the character clambers up onto the ledge."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle OnClamber;
        [SchematicDoc(@"{""name"": ""OnLedgeGrabExitted"", ""kind"": ""signal"", ""behavior"": ""Ledge Grab Controller"", ""direction"": ""output"", ""description"": ""Sent when the character lets go of the ledge (or clambers up)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle OnLedgeGrabExitted;

        [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Ledge Grab Controller"", ""type"": ""LedgeGrabControllerProperties"", ""default"": ""null"", ""description"": ""Its settings asset: how far it reaches for ledges, how fast it shuffles and climbs.""}")]
        public readonly VariableHandle<LedgeGrabControllerProperties> Properties = (LedgeGrabControllerProperties)null;

        [SchematicDoc(@"{""name"": ""FacingRotation"", ""kind"": ""variable"", ""behavior"": ""Ledge Grab Controller"", ""type"": ""Quaternion"", ""default"": ""Quaternion.identity"", ""description"": ""The way the character faces while hanging (towards the wall).""}")]
        public readonly VariableHandle<Quaternion> FacingRotation = Quaternion.identity;

        public enum WallSide { Left, Right }
        [SchematicDoc(@"{""name"": ""CurrentWallSide"", ""kind"": ""variable"", ""behavior"": ""Ledge Grab Controller"", ""type"": ""WallSide"", ""default"": ""WallSide.Left"", ""description"": ""Which side of the character the wall is on.""}")]
        public readonly VariableHandle<WallSide> CurrentWallSide = WallSide.Left;

        private readonly VariableHandle<RaycastHit> _lastWallHit = default(RaycastHit);
        private readonly VariableHandle<List<Vector3>> _clamberPoints = new List<Vector3> { Vector3.zero, Vector3.zero };

        private readonly BehaviorHandle<CharacterMotionContext> _motion;
        private readonly BehaviorHandle<CharacterRaycastContext> _raycast;

        protected override void OnStart()
        {
            var instance = Self;
            var motion = _motion.Select(instance);
            motion?.JumpInput.Subscribe(instance, value => Jump(instance, value));
        }

        protected override void OnEnable()
        {
            var properties = Properties.Value;
            var motion = _motion.Selected;
            var raycast = _raycast.Selected;
            // The Raycast Context's own row for this object, for reading its Variables (this instance is a row of the Ledge Grab Controller).
            var raycastInstance = _raycast.Instance;
            if (properties == null || motion == null || raycast == null)
                return;

            motion.WallCheckInMoveDirection();
            if (raycast.WallCastResult[raycastInstance].TryGetClosestHit(out RaycastHit hit))
                StickOnWall(Self, properties, motion, raycast.Properties[raycastInstance], hit);
        }

        protected override void OnDisable()
        {
            OnLedgeGrabExitted.Invoke(Self);
        }

        public void Jump(BehaviorInstance instance, bool value)
        {
            var properties = Properties[instance];
            var motion = _motion.Select(instance);
            if (!value || properties == null || motion == null)
                return;

            var position = instance.Transform.position;
            var points = _clamberPoints[instance];
            points[0] = position + properties.ClamberHeight * Vector3.up;
            points[1] = (position + properties.ClamberHeight * Vector3.up) + -_lastWallHit[instance].normal * properties.ClamberForward;
            motion.FollowSpringPath(position.y, points);
            OnClamber.Invoke(instance);
        }

        private void StickOnWall(BehaviorInstance instance, LedgeGrabControllerProperties properties, CharacterMotionContext motion, CharacterRaycastProperties raycastProperties, RaycastHit hit)
        {
            _lastWallHit.Value = hit;
            var transform = instance.Transform;

            var facingDir = -hit.normal;

            FacingRotation.Value = Quaternion.LookRotation(facingDir, Vector3.up);

            float side = Vector3.Dot(facingDir, motion.HorizontalDirection);
            CurrentWallSide.Value = side > 0 ? WallSide.Left : WallSide.Right;

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
