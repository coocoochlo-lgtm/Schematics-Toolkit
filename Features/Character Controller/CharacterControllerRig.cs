using Remedy.Box3DPhysics;
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
    [SchematicDoc(@"{""name"": ""Character Controller Rig"", ""kind"": ""behavior"", ""menu"": ""Movement/3D/Character Controller Rig"", ""description"": ""Makes the character's renderer follow its physics body smoothly: tilting, turning, squashing with vertical speed, and posing for whichever movement controller is enabled (Wall Slide, Hover, Ledge Grab, or the default)."", ""variables"": [""CollisionLayer"", ""CameraTransform"", ""CharacterRenderer"", ""LedgeGrabHandTransform"", ""ShadowDecal"", ""ShadowProjector"", ""DecalMinScale"", ""DecalMaxScale"", ""DecalScaleGap"", ""RotateCharacterWhileJumping"", ""Offset"", ""TiltDampening"", ""TurnDamening"", ""PositionFollowSpeed"", ""ScaleSpringDampening"", ""ScaleSpringFrequency"", ""VerticalVelocityScaleFactor"", ""OrientToCameraSpeed""], ""signals"": [""InputAim"", ""InputOrientToAim"", ""OnGroundCast"", ""OnGroundCastHit"", ""IsWallSliding"", ""IsHovering"", ""IsLedgeGrabbing"", ""IsStrafing"", ""VerticalVelocity"", ""IsOnGround""], ""use_for"": [""making a character's model lean, squash and turn with its movement""]}")]
    [SchematicBehaviorPath("Movement/3D/Character Controller Rig")]
    [RequireComponent(typeof(Box3D.Hybrid.Box3DBody))]
    public class CharacterControllerRig : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""CollisionLayer"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""LayerMask"", ""default"": ""~0"", ""description"": ""Collision Layer (LayerMask) of Character Controller Rig.""}")]
        public readonly VariableHandle<LayerMask> CollisionLayer = (LayerMask)~0;

        [SchematicDoc(@"{""name"": ""InputAim"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""input"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Vector3 Input for the Aim Direction, should be passed from the Camera Manager."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [Tooltip("Vector3 Input for the Aim Direction, should be passed from the Camera Manager.")]
        [InputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> InputAim;
        [SchematicDoc(@"{""name"": ""InputOrientToAim"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""input"", ""params"": [{""name"": ""Orient"", ""type"": ""bool""}], ""description"": ""Boolean Inputs that determine whether to Orient toward the Aim Direction."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [Tooltip("Boolean Inputs that determine whether to Orient toward the Aim Direction.")]
        [InputSignal, Parameter("Orient", typeof(bool))]
        public readonly SignalHandle<bool> InputOrientToAim;
        [SchematicDoc(@"{""name"": ""OnGroundCast"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""input"", ""params"": [{""name"": ""Grounded"", ""type"": ""bool""}], ""description"": ""Tells Character Controller Rig to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Grounded", typeof(bool))]
        public readonly SignalHandle<bool> OnGroundCast;
        [SchematicDoc(@"{""name"": ""OnGroundCastHit"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""input"", ""params"": [{""name"": ""Hit"", ""type"": ""RaycastHit""}], ""description"": ""Tells Character Controller Rig to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Hit", typeof(RaycastHit))]
        public readonly SignalHandle<RaycastHit> OnGroundCastHit;

        [SchematicDoc(@"{""name"": ""IsWallSliding"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""output"", ""params"": [{""name"": ""Value"", ""type"": ""bool""}], ""description"": ""Sent by Character Controller Rig when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsWallSliding;
        [SchematicDoc(@"{""name"": ""IsHovering"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""output"", ""params"": [{""name"": ""Value"", ""type"": ""bool""}], ""description"": ""Sent by Character Controller Rig when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsHovering;
        [SchematicDoc(@"{""name"": ""IsLedgeGrabbing"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""output"", ""params"": [{""name"": ""Value"", ""type"": ""bool""}], ""description"": ""Sent by Character Controller Rig when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsLedgeGrabbing;
        [SchematicDoc(@"{""name"": ""IsStrafing"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""output"", ""params"": [{""name"": ""Value"", ""type"": ""bool""}], ""description"": ""Sent by Character Controller Rig when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsStrafing;
        [SchematicDoc(@"{""name"": ""VerticalVelocity"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""output"", ""params"": [{""name"": ""Value"", ""type"": ""float""}], ""description"": ""Sent by Character Controller Rig when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Value", typeof(float))]
        public readonly SignalHandle<float> VerticalVelocity;
        [SchematicDoc(@"{""name"": ""IsOnGround"", ""kind"": ""signal"", ""behavior"": ""Character Controller Rig"", ""direction"": ""output"", ""params"": [{""name"": ""Value"", ""type"": ""bool""}], ""description"": ""Sent by Character Controller Rig when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Value", typeof(bool))]
        public readonly SignalHandle<bool> IsOnGround;

        // References
        [SchematicDoc(@"{""name"": ""CameraTransform"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Camera Transform (Transform) of Character Controller Rig.""}")]
        public readonly VariableHandle<Transform> CameraTransform = (Transform)null;
        [SchematicDoc(@"{""name"": ""CharacterRenderer"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Character Renderer (Transform) of Character Controller Rig.""}")]
        public readonly VariableHandle<Transform> CharacterRenderer = (Transform)null;
        [SchematicDoc(@"{""name"": ""LedgeGrabHandTransform"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Bone that should be placed at the Ledge Position when Ledge Grabbing.""}")]
        [Tooltip("Bone that should be placed at the Ledge Position when Ledge Grabbing.")]
        public readonly VariableHandle<Transform> LedgeGrabHandTransform = (Transform)null;
        [SchematicDoc(@"{""name"": ""ShadowDecal"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""Material"", ""default"": ""null"", ""description"": ""Shadow Decal (Material) of Character Controller Rig.""}")]
        public readonly VariableHandle<Material> ShadowDecal = (Material)null;
        [SchematicDoc(@"{""name"": ""ShadowProjector"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Shadow Projector (Transform) of Character Controller Rig.""}")]
        public readonly VariableHandle<Transform> ShadowProjector = (Transform)null;

        // Shadow Decal
        [SchematicDoc(@"{""name"": ""DecalMinScale"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""1.15f"", ""description"": ""Decal Min Scale (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> DecalMinScale = 1.15f;
        [SchematicDoc(@"{""name"": ""DecalMaxScale"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""1.35f"", ""description"": ""Decal Max Scale (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> DecalMaxScale = 1.35f;
        [SchematicDoc(@"{""name"": ""DecalScaleGap"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Decal Scale Gap (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> DecalScaleGap = 5f;

        // Motion
        [SchematicDoc(@"{""name"": ""RotateCharacterWhileJumping"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Rotate Character While Jumping (bool) of Character Controller Rig.""}")]
        public readonly VariableHandle<bool> RotateCharacterWhileJumping = false;
        [SchematicDoc(@"{""name"": ""Offset"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""Vector3"", ""default"": ""Vector3.zero"", ""description"": ""Offset (Vector3) of Character Controller Rig.""}")]
        public readonly VariableHandle<Vector3> Offset = Vector3.zero;
        [SchematicDoc(@"{""name"": ""TiltDampening"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Tilt Dampening (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> TiltDampening = 5f;
        [SchematicDoc(@"{""name"": ""TurnDamening"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""15f"", ""description"": ""Turn Damening (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> TurnDamening = 15f;
        [SchematicDoc(@"{""name"": ""PositionFollowSpeed"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""20f"", ""description"": ""Position Follow Speed (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> PositionFollowSpeed = 20f;
        [SchematicDoc(@"{""name"": ""ScaleSpringDampening"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Scale Spring Dampening (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> ScaleSpringDampening = 10f;
        [SchematicDoc(@"{""name"": ""ScaleSpringFrequency"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""6f"", ""description"": ""Scale Spring Frequency (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> ScaleSpringFrequency = 6f;
        [SchematicDoc(@"{""name"": ""VerticalVelocityScaleFactor"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""0.2f"", ""description"": ""Vertical Velocity Scale Factor (float) of Character Controller Rig.""}")]
        public readonly VariableHandle<float> VerticalVelocityScaleFactor = 0.2f;
        [SchematicDoc(@"{""name"": ""OrientToCameraSpeed"", ""kind"": ""variable"", ""behavior"": ""Character Controller Rig"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Orient To Camera Speed (float) of Character Controller Rig.""}")]
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

        protected override void OnStart()
        {
            var instance = Self;
            InputAim.Subscribe(value => _aimDirection[instance] = value);
            InputOrientToAim.Subscribe(value => _orientToDirection[instance] = value);
            OnGroundCast.Subscribe(value =>
            {
                _isGrounded[instance] = value;
                IsOnGround.Invoke(instance, value);
            });
            OnGroundCastHit.Subscribe(hit => _groundHit[instance] = hit);
        }

        protected override void OnEnable()
        {
            _position.Value = Self.Transform.position;
        }

        // Whether a movement controller on this object is attached and enabled.
        private static bool IsActive<TController>(BehaviorInstance instance) where TController : SchematicBehavior
        {
            var controller = ExecutionContext.FindInstance(instance.GameObject, typeof(TController));
            return controller != null && controller.Enabled;
        }

        protected override void OnUpdate()
        {
            var renderer = CharacterRenderer.Value;
            if (renderer == null)
                return;

            var transform = Self.Transform;
            var velocity = Self.GetComponent<Box3D.Hybrid.Box3DBody>().GetVelocity();
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
                if (SchematicPhysics.Raycast(transform.position, Vector3.down, out RaycastHit hitInfo, 1000, CollisionLayer.Value, Self.GetComponent<Box3D.Hybrid.Box3DBody>()))
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

            VerticalVelocity.Invoke(Self, velocity.y);

            if (IsActive<WallSlideController>(Self))
            {
                var wallSlide = Self.GetSchematicBehavior<WallSlideController>();

                position.x = Mathf.Lerp(position.x, transform.position.x, followSpeed * dt);
                position.z = Mathf.Lerp(position.z, transform.position.z, followSpeed * dt);
                position.y = Mathf.Lerp(position.y, transform.position.y - Offset.Value.y, followSpeed * dt);

                renderer.position = position;
                renderer.rotation = wallSlide.FacingRotation.Value;

                renderer.localScale = new Vector3(wallSlide.CurrentWallSide.Value == WallSlideController.WallSide.Left ? 1 : -1, baseScale.y, baseScale.z);

                IsWallSliding.Invoke(Self, true);
            }
            else if (IsActive<HoverController>(Self))
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

                IsHovering.Invoke(Self, true);
                IsWallSliding.Invoke(Self, false);

                renderer.localScale = baseScale;
            }
            else if (IsActive<LedgeGrabController>(Self))
            {
                // Target position is LedgePosition - the hand's offset from the renderer, in world space
                //_targetRendererPosition = LedgeGrabController.LedgePosition - CharacterRenderer.TransformVector(handLocalOffset);
                var target = _targetRendererPosition.Value;

                // Smoothly interpolate renderer position to target
                if (Vector3.Distance(transform.position, target) < 5f)
                    position = Vector3.Lerp(renderer.position, target, followSpeed * dt);
                renderer.position = position;

                renderer.localScale = baseScale;

                IsLedgeGrabbing.Invoke(Self, true);
                IsWallSliding.Invoke(Self, false);
                IsHovering.Invoke(Self, false);
            }
            else
            {
                IsLedgeGrabbing.Invoke(Self, false);
                IsWallSliding.Invoke(Self, false);
                IsHovering.Invoke(Self, false);

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
