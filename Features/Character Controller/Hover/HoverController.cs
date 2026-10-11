using UnityEngine;

namespace Remedy.CharacterControllers.Hover
{
    /// <summary>
    /// Flight: rises while there's Stamina, then hovers and plummets. Input comes from the Motion Context's Signals.
    /// </summary>
    [RequireComponent(typeof(Box3D.Hybrid.Box3DBody))]
    [RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
    [SchematicBehaviorPath("Movement/3D/Flight and Hover Controller")]
    public class HoverController : SchematicBehavior
    {
        public readonly VariableHandle<HoverControllerProperties> Properties = (HoverControllerProperties)null;

        public readonly VariableHandle<float> CurrentVerticalSpeed = 0f;
        public readonly VariableHandle<float> RotationVelocity = 0f;

        // state variables
        private readonly VariableHandle<Vector3> _moveInput = Vector3.zero;
        private readonly VariableHandle<float> _currentHoverTime = 0f;
        private readonly VariableHandle<float> _currentFallSpeed = 0f;

        protected override void OnStart()
        {
            var instance = Self;
            var motion = instance.GetSchematicBehavior<CharacterMotionContext>();
            if (motion == null)
                return;

            // The Motion Context's input Signals, heard here for the same object. Skipped while this is disabled.
            motion.MoveInput.Subscribe(instance, value => Move(instance, value));
            motion.JumpInput.Subscribe(instance, value => Jump(instance, value));
        }

        protected override void OnEnable()
        {
            var motion = Self.GetSchematicBehavior<CharacterMotionContext>();
            if (motion != null)
                motion.IsKinematic.Value = false;

            _currentHoverTime.Value = 0f;
            _currentFallSpeed.Value = 0f;
        }

        protected override void OnFixedUpdate()
        {
            var properties = Properties.Value;
            var motion = Self.GetSchematicBehavior<CharacterMotionContext>();
            if (properties == null || motion == null)
                return;

            if (motion.CurrentStamina > properties.staminaUseRate * Time.fixedDeltaTime)
            {
                motion.ApplyVerticalForce(properties.RiseSpeed);
            }
            else
            {
                ref var hoverTime = ref _currentHoverTime.Ref;
                ref var fallSpeed = ref _currentFallSpeed.Ref;
                if (hoverTime < properties.HoverTime)
                {
                    motion.ApplyVerticalForce(-properties.HoverFallSpeed, true);
                    hoverTime += Time.fixedDeltaTime;
                    fallSpeed = 0f;
                }
                else
                {
                    if (fallSpeed < properties.PlummetTerminalSpeed)
                        fallSpeed += properties.PlummetAcceleration;

                    motion.ApplyVerticalForce(-fallSpeed, true);
                }
            }

            motion.ApplyCappedHorizontalForce(_moveInput.Value, properties.HorizontalSpeed, properties.HorizontalAcceleration, properties.HorizontalDeceration);
        }

        public void Jump(BehaviorInstance instance, bool value)
        {
            _moveInput.For(instance).y = value ? 1 : 0;
        }

        public void Move(BehaviorInstance instance, Vector2 value)
        {
            if (value != default)
            {
                ref var input = ref _moveInput.For(instance);
                input.x = value.x;
                input.z = value.y;
            }
        }
    }
}
