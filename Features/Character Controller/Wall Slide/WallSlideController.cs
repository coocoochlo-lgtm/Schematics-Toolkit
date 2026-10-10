using UnityEngine;

namespace Remedy.CharacterControllers.WallSlide
{
    /// <summary>
    /// Slides down walls the character moves into, and jumps off them. Input comes from the Motion Context's Signals.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
    [SchematicBehaviorPath("Movement/3D/Wall Slide Controller")]
    public class WallSlideController : SchematicBehavior
    {
        [OutputSignal]
        public readonly SignalHandle OnWallJump;
        [OutputSignal, Parameter("Sliding", typeof(bool))]
        public readonly SignalHandle<bool> OnWallSlideExitted;

        public readonly VariableHandle<WallSlideControllerProperties> Properties = (WallSlideControllerProperties)null;

        public readonly VariableHandle<Quaternion> FacingRotation = Quaternion.identity;

        public enum WallSide { Left, Right }
        public readonly VariableHandle<WallSide> CurrentWallSide = WallSide.Left;

        private readonly VariableHandle<RaycastHit> _lastWallHit = default(RaycastHit);

        protected override void OnStart(BehaviorInstance instance)
        {
            var motion = instance.GetSchematicBehavior<CharacterMotionContext>();
            motion?.JumpInput.Subscribe(instance, value => Jump(instance, value));
        }

        protected override void OnFixedUpdate(BehaviorInstance instance)
        {
            var properties = Properties.Value;
            var motion = instance.GetSchematicBehavior<CharacterMotionContext>();
            var raycast = instance.GetSchematicBehavior<CharacterRaycastContext>();
            if (properties == null || motion == null || raycast == null)
                return;

            motion.ApplyVerticalForce(-properties.WallSlideSpeed, true);

            motion.WallCheckInMoveDirection();
            if (raycast.WallCastResult.Value.TryGetClosestHit(out RaycastHit hit))
                StickOnWall(instance, properties, motion, hit);
            else
                OnWallSlideExitted.Invoke(instance, false);
        }

        public void Jump(BehaviorInstance instance, bool value)
        {
            var properties = Properties[instance];
            var motion = instance.GetSchematicBehavior<CharacterMotionContext>();
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
