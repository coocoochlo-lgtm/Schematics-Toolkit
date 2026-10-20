using UnityEngine;

namespace Remedy.CharacterControllers.WallSlide
{
    /// <summary>
    /// Slides down walls the character moves into, and jumps off them. Input comes from the Motion Context's Signals.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Wall Slide Controller"", ""kind"": ""behavior"", ""menu"": ""Movement/3D/Wall Slide Controller"", ""description"": ""Slides down walls the character moves into, and jumps off them. Input comes from the Motion Context's Signals."", ""variables"": [""Properties"", ""FacingRotation"", ""CurrentWallSide""], ""signals"": [""OnWallJump"", ""OnWallSlideExitted""], ""use_for"": [""wall sliding and wall jumps""]}")]
    [RequireComponent(typeof(Box3D.Hybrid.Box3DBody))]
    [RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
    [SchematicBehaviorPath("Movement/3D/Wall Slide Controller")]
    public class WallSlideController : SchematicBehavior
    {
        [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.MoveInput))]
        public readonly SignalHandle<Vector2> MoveInput;
        [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.JumpInput))]
        public readonly SignalHandle<bool> JumpInput;
        [SchematicDoc(@"{""name"": ""OnWallJump"", ""kind"": ""signal"", ""behavior"": ""Wall Slide Controller"", ""direction"": ""output"", ""description"": ""Sent by Wall Slide Controller when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle OnWallJump;
        [SchematicDoc(@"{""name"": ""OnWallSlideExitted"", ""kind"": ""signal"", ""behavior"": ""Wall Slide Controller"", ""direction"": ""output"", ""params"": [{""name"": ""Sliding"", ""type"": ""bool""}], ""description"": ""Sent by Wall Slide Controller when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Sliding", typeof(bool))]
        public readonly SignalHandle<bool> OnWallSlideExitted;

        [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Wall Slide Controller"", ""type"": ""WallSlideControllerProperties"", ""default"": ""null"", ""description"": ""Properties (WallSlideControllerProperties) of Wall Slide Controller.""}")]
        public readonly VariableHandle<WallSlideControllerProperties> Properties = (WallSlideControllerProperties)null;

        [SchematicDoc(@"{""name"": ""FacingRotation"", ""kind"": ""variable"", ""behavior"": ""Wall Slide Controller"", ""type"": ""Quaternion"", ""default"": ""Quaternion.identity"", ""description"": ""Facing Rotation (Quaternion) of Wall Slide Controller.""}")]
        public readonly VariableHandle<Quaternion> FacingRotation = Quaternion.identity;

        public enum WallSide { Left, Right }
        [SchematicDoc(@"{""name"": ""CurrentWallSide"", ""kind"": ""variable"", ""behavior"": ""Wall Slide Controller"", ""type"": ""WallSide"", ""default"": ""WallSide.Left"", ""description"": ""Current Wall Side (WallSide) of Wall Slide Controller.""}")]
        public readonly VariableHandle<WallSide> CurrentWallSide = WallSide.Left;

        private readonly VariableHandle<RaycastHit> _lastWallHit = default(RaycastHit);

        private readonly BehaviorHandle<CharacterMotionContext> _motion;
        private readonly BehaviorHandle<CharacterRaycastContext> _raycast;

        protected override void OnStart()
        {
            var instance = Self;
            var motion = _motion.Select(instance);
            motion?.JumpInput.Subscribe(instance, value => Jump(instance, value));
        }

        protected override void OnFixedUpdate()
        {
            var properties = Properties.Value;
            var motion = _motion.Selected;
            var raycast = _raycast.Selected;
            // The Raycast Context's own row for this object, for reading its Variables (this instance is a row of the Wall Slide Controller).
            var raycastInstance = _raycast.Instance;
            if (properties == null || motion == null || raycast == null)
                return;

            motion.ApplyVerticalForce(-properties.WallSlideSpeed, true);

            motion.WallCheckInMoveDirection();
            if (raycast.WallCastResult[raycastInstance].TryGetClosestHit(out RaycastHit hit))
                StickOnWall(Self, properties, motion, hit);
            else
                OnWallSlideExitted.Invoke(Self, false);
        }

        public void Jump(BehaviorInstance instance, bool value)
        {
            var properties = Properties[instance];
            var motion = _motion.Select(instance);
            if (!value || properties == null || motion == null)
                return;

            var wallHit = _lastWallHit[instance];
            var lateralVelocity = Vector3.ProjectOnPlane(motion.MoveDirection, wallHit.normal);
            Vector3 jumpDir = (lateralVelocity * properties.JumpLateralInputInfluence + wallHit.normal).normalized;
            motion.ApplyForce(jumpDir * properties.JumpHorizontalForce + Vector3.up * properties.JumpVerticalForce, true);

            OnWallJump.Invoke(instance);
        }

        private void StickOnWall(BehaviorInstance instance, WallSlideControllerProperties properties, CharacterMotionContext motion, RaycastHit hit)
        {
            _lastWallHit.Value = hit;
            var transform = instance.Transform;

            if (hit.distance < properties.GapFromWall)
            {
                var projectedTransform = Vector3.ProjectOnPlane(transform.position, hit.normal);
                motion.TeleportToPosition(projectedTransform + hit.normal * properties.GapFromWall);
            }

            var facingDir = Vector3.ProjectOnPlane(motion.HorizontalDirection, hit.normal);

            if (facingDir.sqrMagnitude < 0.001f)
                facingDir = Vector3.ProjectOnPlane(-transform.right, hit.normal);

            facingDir.Normalize();

            FacingRotation.Value = Quaternion.LookRotation(facingDir, Vector3.up);

            float side = Vector3.Dot(facingDir, motion.HorizontalDirection);
            CurrentWallSide.Value = side > 0 ? WallSide.Left : WallSide.Right;
        }
    }
}
