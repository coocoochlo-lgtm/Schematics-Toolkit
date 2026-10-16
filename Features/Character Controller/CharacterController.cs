using UnityEngine;

/// <summary>
/// Physics based character movement: rides at a hover height above the ground, jumps with a buffer and hang time,
/// and falls with extra gravity. Movement itself is applied through the <see cref="CharacterMotionContext"/> and ground
/// detection comes from the <see cref="CharacterRaycastContext"/>; both are attached along with it.<br/>
/// Input arrives through the Motion Context's MoveInput and JumpInput Signals.
/// </summary>
[SchematicDoc(@"{""name"": ""Physics Based Character Controller"", ""kind"": ""behavior"", ""menu"": ""Movement/3D/Default Character Controller"", ""description"": ""Physics based character movement: rides at a hover height above the ground, jumps with a buffer and hang time, and falls with extra gravity. Movement itself is applied through the CharacterMotionContext and ground detection comes from the CharacterRaycastContext; both are attached along with it. Input arrives through the Motion Context's MoveInput and JumpInput Signals."", ""variables"": [""Properties""], ""signals"": [""MoveToPosition""], ""use_for"": [""the main player character's running and jumping"", ""platformer movement""], ""notes"": ""Needs Character Motion Context and Character Raycast Context on the same object.""}")]
[SchematicBehaviorPath("Movement/3D/Default Character Controller")]
[RequireComponent(typeof(Box3D.Hybrid.Box3DBody))]
[RequireBehavior(typeof(CharacterMotionContext), typeof(CharacterRaycastContext))]
public class PhysicsBasedCharacterController : SchematicBehavior
{
    [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.MoveInput))]
    public readonly SignalHandle<Vector2> MoveInput;
    [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.JumpInput))]
    public readonly SignalHandle<bool> JumpInput;
    [SchematicDoc(@"{""name"": ""MoveToPosition"", ""kind"": ""signal"", ""behavior"": ""Physics Based Character Controller"", ""direction"": ""input"", ""params"": [{""name"": ""Position"", ""type"": ""Vector3""}], ""description"": ""Tells Physics Based Character Controller to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
    [InputSignal]
    [Parameter("Position", typeof(Vector3))]
    public readonly SignalHandle<Vector3> MoveToPosition;

    /// <summary>
    /// Tuning values, assigned per object in the Inspector.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Physics Based Character Controller"", ""type"": ""CharacterControllerProperties"", ""default"": ""null"", ""description"": ""Tuning values, assigned per object in the Inspector.""}")]
    public readonly VariableHandle<CharacterControllerProperties> Properties = (CharacterControllerProperties)null;

    // State
    private readonly VariableHandle<Vector3> _moveInput = default(Vector3);
    private readonly VariableHandle<bool> _prevGrounded = false;
    private readonly VariableHandle<bool> _isJumping = false;
    private readonly VariableHandle<float> _jumpResetTime = 0f;
    private readonly VariableHandle<float> _jumpBufferTime = 0f;
    private readonly VariableHandle<float> _jumpArcY = 0f;
    private readonly VariableHandle<bool> _reachedJumpArc = false;
    private readonly VariableHandle<float> _tempHangTimeDuration = -1f; // > 0 = use this instead of default Hang Time
    private readonly VariableHandle<float> _currentHangTime = 0f;
    private readonly VariableHandle<float> _jumpStartGroundHeight = 0f;
    private readonly VariableHandle<float> _groundY = 0f;
    private readonly VariableHandle<bool> _hadJumped = false;
    private readonly VariableHandle<bool> _canJump = true;
    private readonly VariableHandle<bool> _enableGravity = true;

    // Siblings on the same GameObject (attached along with this by [RequireBehavior]).
    private readonly BehaviorHandle<CharacterMotionContext> _motion;
    private readonly BehaviorHandle<CharacterRaycastContext> _raycast;

    /// <summary>
    /// The Properties of the controller attached to the given GameObject, for systems that need to predict its motion.
    /// </summary>
    public static bool TryGetProperties(GameObject gameObject, out CharacterControllerProperties properties)
    {
        var controller = gameObject.GetSchematicBehavior<PhysicsBasedCharacterController>();
        properties = controller != null ? controller.Properties.Value : null;
        return properties != null;
    }

    protected override void OnStart()
    {
        var instance = Self;
        var motion = _motion.Select(instance);
        if (motion == null)
            return;

        // The Motion Context's input Signals, heard here for the same object. Skipped while this is disabled.
        motion.MoveInput.Subscribe(instance, value =>
        {
            ref var input = ref _moveInput.For(instance);
            input.x = value.x;
            input.z = value.y;
        });

        motion.JumpInput.Subscribe(instance, value =>
        {
            _moveInput.For(instance).y = value ? 1 : 0;

            if (value)
            {
                if (_canJump[instance])
                {
                    _jumpBufferTime[instance] = 0f;
                    _canJump[instance] = false;
                }
            }
            else
                _canJump[instance] = true;
        });

        MoveToPosition.Subscribe(position =>
        {
            _motion.Select(instance)?.TeleportToPosition(position);
        });
    }

    protected override void OnEnable()
    {
        var motion = _motion.Selected;
        if (motion != null)
            motion.IsKinematic.Value = false;

        _canJump.Value = true;

        var properties = Properties.Value;
        if (properties != null)
        {
            _currentHangTime.Value = properties.HangTimeDuration;
            _jumpBufferTime.Value = properties.JumpBuffer;
        }
    }

    protected override void OnFixedUpdate()
    {
        var properties = Properties.Value;
        var motion = _motion.Selected;
        var raycast = _raycast.Selected;
        if (properties == null || motion == null || raycast == null)
            return;

        HandleGroundRide(properties, motion, raycast);
        motion.ApplyCappedHorizontalForce(_moveInput, properties.MoveSpeed, properties.Acceleration, properties.Deceleration);
        HandleJump(properties, motion, raycast);
    }

    private void HandleGroundRide(CharacterControllerProperties properties, CharacterMotionContext motion, CharacterRaycastContext raycast)
    {
        if (raycast.IsGrounded.Value && !_isJumping)
        {
            _hadJumped.Value = false;
            _groundY.Value = raycast.GroundPosition.Value.y;
            _prevGrounded.Value = true;
            _reachedJumpArc.Value = false;
            motion.SpringToVertically(_groundY, properties.RideHeight, properties.RideSpringStrength, properties.HangTimeDamper, properties.MaxHoverForce);
        }
        else
        {
            float hangTimeDuration = _tempHangTimeDuration > 0 ? _tempHangTimeDuration : properties.HangTimeDuration;

            if (_enableGravity && !(_currentHangTime < hangTimeDuration && _hadJumped) && !raycast.IsGrounded.Value)
                motion.ApplyGravity();

            _prevGrounded.Value = false;
        }
    }

    private void HandleJump(CharacterControllerProperties properties, CharacterMotionContext motion, CharacterRaycastContext raycast)
    {
        float dt = Time.fixedDeltaTime;
        float hangTimeDuration = _tempHangTimeDuration > 0 ? _tempHangTimeDuration : properties.HangTimeDuration;

        if (_moveInput.Value.y == 0)
        {
            if (_prevGrounded)
                _jumpResetTime.Value += dt;

            if (_isJumping)
            {
                _jumpBufferTime.Value = properties.JumpBuffer;
                _isJumping.Value = false;
            }
        }

        if (!_isJumping && _prevGrounded)
        {
            _jumpResetTime.Value += dt;

            if (_jumpResetTime > properties.JumpResetDelay && _jumpBufferTime < properties.JumpBuffer)
            {
                _isJumping.Value = true;
                _prevGrounded.Value = false;
                _jumpResetTime.Value = 0f;
                _jumpArcY.Value = raycast.GroundPosition.Value.y + properties.JumpHeight;

                if (motion.Velocity.y < properties.JumpSpeed)
                    motion.ApplyVerticalForce(properties.JumpSpeed);

                _jumpStartGroundHeight.Value = raycast.GroundPosition.Value.y;
                _reachedJumpArc.Value = false;

                _hadJumped.Value = true;
                _enableGravity.Value = false;
            }
        }
        else if (_isJumping)
        {
            Vector3 predictedPos = motion.PredictNextPosition();

            if (predictedPos.y < _jumpArcY)
            {
                if (motion.Velocity.y < properties.JumpSpeed)
                    motion.ApplyVerticalForce(properties.JumpSpeed);
            }
            else
            {
                _isJumping.Value = false;
                _currentHangTime.Value = 0f;
                _reachedJumpArc.Value = true;
            }
        }
        else if (_currentHangTime < hangTimeDuration && _hadJumped)
        {
            if (_reachedJumpArc)
                motion.SpringToVertically(_jumpStartGroundHeight, properties.JumpHeight, properties.RideSpringStrength, properties.HangTimeDamper, properties.MaxHoverForce);

            _currentHangTime.Value += dt;
        }
        else if (!raycast.IsGrounded.Value)
        {
            motion.ApplyVerticalForce(-properties.FallSpeed);
            _enableGravity.Value = true;
        }

        _jumpBufferTime.Value += dt;
    }

    protected override void OnCollisionEnter(Collision collision)
    {
        var properties = Properties.Value;
        if (properties == null)
            return;

        // Hitting something overhead ends the hang time.
        foreach (var contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.down) > 0.5f)
            {
                _currentHangTime.Value = properties.HangTimeDuration;
                break;
            }
        }
    }
}
