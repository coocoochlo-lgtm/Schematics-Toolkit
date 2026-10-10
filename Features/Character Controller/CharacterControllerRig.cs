using Remedy.CharacterControllers.Hover;
using Remedy.CharacterControllers.LedgeGrab;
using Remedy.CharacterControllers.WallSlide;
using UnityEngine;

namespace Remedy.CharacterControllers
{
    /// <summary>
    /// Makes the character's renderer follow its physics body smoothly: tilting, turning, squashing with vertical speed,
    /// and posing for whichever movement controller is enabled (Wall Slide, Hover, Ledge Grab, or the default).
    /// </summary>
    [SchematicBehaviorPath("Movement/3D/Character Controller Rig")]
    [RequireComponent(typeof(Rigidbody))]
    public class CharacterControllerRig : SchematicBehavior
    {
        public readonly VariableHandle<LayerMask> CollisionLayer = (LayerMask)~0;

        [Tooltip("Vector3 Input for the Aim Direction, should be passed from the Camera Manager.")]
        [InputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> InputAim;
        [Tooltip("Boolean Inputs that determine whether to Orient toward the Aim Direction.")]
        [InputSignal, Parameter("Orient", typeof(bool))]
        public readonly SignalHandle<bool> InputOrientToAim;
        [InputSignal, Parameter("Grounded", typeof(bool))]
        public readonly SignalHandle<bool> OnGroundCast;
        [InputSignal, Parameter("Hit", typeof(RaycastHit))]
        public readonly SignalHandle<RaycastHit> OnGroundCastHit;

        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsWallSliding;
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsHovering;
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsLedgeGrabbing;
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsStrafing;
        [OutputSignal, Parameter("Value", typeof(float))]
        public readonly SignalHandle<float> VerticalVelocity;
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsOnGround;

        // References
        public readonly VariableHandle<Transform> CameraTransform = (Transform)null;
        public readonly VariableHandle<Transform> CharacterRenderer = (Transform)null;
        [Tooltip("Bone that should be placed at the Ledge Position when Ledge Grabbing.")]
        public readonly VariableHandle<Transform> LedgeGrabHandTransform = (Transform)null;
        public readonly VariableHandle<Material> ShadowDecal = (Material)null;
        public readonly VariableHandle<Transform> ShadowProjector = (Transform)null;

        // Shadow Decal
        public readonly VariableHandle<float> DecalMinScale = 1.15f;
        public readonly VariableHandle<float> DecalMaxScale = 1.35f;
        public readonly VariableHandle<float> DecalScaleGap = 5f;

        // Motion
        public readonly VariableHandle<bool> RotateCharacterWhileJumping = false;
        public readonly VariableHandle<Vector3> Offset = Vector3.zero;
        public readonly VariableHandle<float> TiltDampening = 5f;
        public readonly VariableHandle<float> TurnDamening = 15f;
        public readonly VariableHandle<float> PositionFollowSpeed = 20f;
        public readonly VariableHandle<float> ScaleSpringDampening = 10f;
        public readonly VariableHandle<float> ScaleSpringFrequency = 6f;
        public readonly VariableHandle<float> VerticalVelocityScaleFactor = 0.2f;
        public readonly VariableHandle<float> OrientToCameraSpeed = 10f;

        // State
        private readonly VariableHandle<Vector3> _position = Vector3.zero;
        private readonly VariableHandle<Vector3> _tilt = Vector3.zero;
        private readonly VariableHandle<Vector3> _baseScale = Vector3.one;
        private readonly VariableHandle<Vector3> _targetRendererPosition = Vector3.zero;
        private readonly VariableHandle<Vector3> _aimDirection = Vector3.zero;
        private readonly VariableHandle<bool> _orientToDirection = false;
        private readonly VariableHandle<bool> _isGrounded = false;
        private readonly VariableHandle<RaycastHit> _groundHit = default(RaycastHit);
        private readonly VariableHandle<float> _yVelocity = 0f;

        public bool HasDecal => ShadowDecal.Value != null;

        protected override void OnStart(BehaviorInstance instance)
        {
            InputAim.Subscribe(value => _aimDirection[instance] = value);
            InputOrientToAim.Subscribe(value => _orientToDirection[instance] = value);
            OnGroundCast.Subscribe(value =>
            {
                _isGrounded[instance] = value;
                IsOnGround.Invoke(instance, value);
            });
            OnGroundCastHit.Subscribe(hit => _groundHit[instance] = hit);
        }

        protected override void OnEnable(BehaviorInstance instance)
        {
            _position.Value = instance.Transform.position;
        }

        // Whether a movement controller on this object is attached and enabled.
        private static bool IsActive<TController>(BehaviorInstance instance) where TController : SchematicBehavior
        {
            var controller = ExecutionContext.FindInstance(instance.GameObject, typeof(TController));
            return controller != null && controller.Enabled;
        }

        protected override void OnUpdate(BehaviorInstance instance)
        {
            var renderer = CharacterRenderer.Value;
            if (renderer == null)
                return;

            var transform = instance.Transform;
            var rb = instance.GetComponent<Rigidbody>();
            var velocity = rb.linearVelocity;
            float dt = Time.deltaTime;
            float followSpeed = PositionFollowSpeed.Value;
            float turnDampening = TurnDamening.Value;

            ref var position = ref _position.Ref;
            ref var tilt = ref _tilt.Ref;
            ref var baseScale = ref _baseScale.Ref;
            ref var yVelocity = ref _yVelocity.Ref;
            var aimDirection = _aimDirection.Value;
            bool orientToDirection = _orientToDirection.Value;

            var decal = ShadowDecal.Value;
            if (decal != null)
            {
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hitInfo, 1000, CollisionLayer.Value))
                {
                    float gap = Mathf.Clamp(hitInfo.distance / DecalScaleGap.Value, 0, DecalScaleGap.Value);
                    decal.SetFloat("_Scale", Mathf.Lerp(DecalMaxScale.Value, DecalMinScale.Value, gap));
                }
            }

            float goalYScale = 1 + Mathf.Abs(velocity.y) * VerticalVelocityScaleFactor.Value;

            float delta = goalYScale - baseScale.y;
            float frequency = ScaleSpringFrequency.Value;
            yVelocity += frequency * frequency * delta * dt;
            yVelocity *= Mathf.Exp(-ScaleSpringDampening.Value * dt);
            baseScale.y += yVelocity * dt;

            Vector3 direction = transform.position - renderer.position;
            direction.y = 0;

            VerticalVelocity.Invoke(instance, velocity.y);

            if (IsActive<WallSlideController>(instance))
            {
                var wallSlide = instance.GetSchematicBehavior<WallSlideController>();

                position.x = Mathf.Lerp(position.x, transform.position.x, followSpeed * dt);
                position.z = Mathf.Lerp(position.z, transform.position.z, followSpeed * dt);
                position.y = Mathf.Lerp(position.y, transform.position.y - Offset.Value.y, followSpeed * dt);

                renderer.position = position;
                renderer.rotation = wallSlide.FacingRotation.Value;

                renderer.localScale = new Vector3(wallSlide.CurrentWallSide.Value == WallSlideController.WallSide.Left ? 1 : -1, baseScale.y, baseScale.z);

                IsWallSliding.Invoke(instance, true);
            }
            else if (IsActive<HoverController>(instance))
            {
                var localVelocity = renderer.InverseTransformDirection(velocity);

                tilt.x = localVelocity.z * 2.5f; // Forward/backward tilt (pitch)
                tilt.z = -localVelocity.x * 2.5f;  // Side-to-side tilt (roll)

                if (!orientToDirection)
                    renderer.rotation = Quaternion.Lerp(renderer.rotation, Quaternion.LookRotation(direction, renderer.up), turnDampening * dt);
                else
                    renderer.rotation = Quaternion.Slerp(renderer.rotation, Quaternion.LookRotation(aimDirection, Vector3.up), OrientToCameraSpeed.Value * dt);

                tilt.y = renderer.eulerAngles.y;

                renderer.eulerAngles = tilt;

                position.x = Mathf.Lerp(position.x, transform.position.x, followSpeed * dt);
                position.z = Mathf.Lerp(position.z, transform.position.z, followSpeed * dt);
                position.y = Mathf.Lerp(position.y, transform.position.y, 5 * dt);

                renderer.position = position;

                IsHovering.Invoke(instance, true);
                IsWallSliding.Invoke(instance, false);

                renderer.localScale = baseScale;
            }
            else if (IsActive<LedgeGrabController>(instance))
            {
                // Target position is LedgePosition - the hand's offset from the renderer, in world space
                //_targetRendererPosition = LedgeGrabController.LedgePosition - CharacterRenderer.TransformVector(handLocalOffset);
                var target = _targetRendererPosition.Value;

                // Smoothly interpolate renderer position to target
                if (Vector3.Distance(transform.position, target) < 5f)
                    position = Vector3.Lerp(renderer.position, target, followSpeed * dt);
                renderer.position = position;

                renderer.localScale = baseScale;

                IsLedgeGrabbing.Invoke(instance, true);
                IsWallSliding.Invoke(instance, false);
                IsHovering.Invoke(instance, false);
            }
            else
            {
                IsLedgeGrabbing.Invoke(instance, false);
                IsWallSliding.Invoke(instance, false);
                IsHovering.Invoke(instance, false);

                var groundHit = _groundHit.Value;
                if (groundHit.point != Vector3.zero)
                    position.y = Mathf.Lerp(position.y, groundHit.distance, followSpeed * dt);
                else
                    position.y = Mathf.Lerp(position.y, transform.position.y - Offset.Value.y, followSpeed * dt);

                position.x = Mathf.Lerp(position.x, transform.position.x, followSpeed * dt);
                position.z = Mathf.Lerp(position.z, transform.position.z, followSpeed * dt);

                renderer.position = position;

                if (Mathf.Abs(velocity.x) + Mathf.Abs(velocity.z) > 0.5f)
                {
                    tilt.x = Mathf.LerpAngle(renderer.eulerAngles.x, transform.eulerAngles.x, TiltDampening.Value * dt);
                    tilt.z = Mathf.LerpAngle(renderer.eulerAngles.z, transform.eulerAngles.z, TiltDampening.Value * dt);

                    if (!orientToDirection)
                    {
                        if (RotateCharacterWhileJumping.Value || _isGrounded.Value)
                            renderer.rotation = Quaternion.Lerp(renderer.rotation, Quaternion.LookRotation(direction, renderer.up), turnDampening * dt);
                    }
                    else
                        renderer.rotation = Quaternion.Slerp(renderer.rotation, Quaternion.LookRotation(aimDirection, Vector3.up), OrientToCameraSpeed.Value * dt);
                }
                else
                {
                    tilt.x = Mathf.LerpAngle(renderer.eulerAngles.x, 0, turnDampening * dt);
                    tilt.z = Mathf.LerpAngle(renderer.eulerAngles.z, 0, turnDampening * dt);

                    if (orientToDirection)
                        renderer.rotation = Quaternion.Slerp(renderer.rotation, Quaternion.LookRotation(aimDirection, Vector3.up), OrientToCameraSpeed.Value * dt);
                }

                tilt.y = renderer.eulerAngles.y;
                renderer.eulerAngles = tilt;

                renderer.localScale = baseScale;
            }

            var projector = ShadowProjector.Value;
            if (projector != null)
                projector.rotation = Quaternion.LookRotation(Vector3.down, Vector3.up);
        }
    }
}
