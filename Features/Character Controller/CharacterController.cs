using Remedy.Framework;
using Remedy.Schematics.Utils;
using UnityEngine;

[SchematicComponent("Movement/3D/Default Character Controller")]
[RequireComponent(typeof(CharacterMotionContext))]
[RequireComponent(typeof(CharacterRaycastContext))]
public class PhysicsBasedCharacterController : SchematicMonoBehavior
{
    [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.MoveInput))]
    static public Signal<Vector2> MoveInput;
    [SchematicEventLink(typeof(CharacterMotionContext), nameof(CharacterMotionContext.JumpInput))]
    static public Signal<bool> JumpInput;
    [InputSignal]
    [Parameter("Position", typeof(Vector3))]
    static public Signal<Vector3> MoveToPosition;

    [SchematicProperties]
    public CharacterControllerProperties Properties;

    // Cached
    private bool _cached = false;
    private CharacterRaycastContext _raycastContext;
    private CharacterMotionContext _motionContext;
    private float _dt;

    // State vars
    private readonly VariableHandle<Vector3> _moveInput = default;
    private readonly VariableHandle<bool> _prevGrounded = false;
    private readonly VariableHandle<bool> _isJumping = false;
    private readonly VariableHandle<float> _jumpResetTime = 0f;
    private readonly VariableHandle<float> _jumpBufferTime = 0f;
    private readonly VariableHandle<float> _jumpArcY = 0f;
    private readonly VariableHandle<bool> _reachedJumpArc = false;
    private readonly VariableHandle<float> _tempHangTimeDuration = -1; // > 0 = use this instead of default Hang Time
    private readonly VariableHandle<float> _currentHangTime = 0f;
    private readonly VariableHandle<float> _jumpStartGroundHeight;
    private readonly VariableHandle<float> _groundY = 0;
    private readonly VariableHandle<bool> _hadJumped = false;
    private readonly VariableHandle<bool> _canJump = true;
    private readonly VariableHandle<bool> _enableGravity = true;

    private void OnEnable()
    {
        _cached = false;
        Cache();

        _motionContext.IsKinematic = false;
        _canJump = true;

        _currentHangTime = Properties.HangTimeDuration;
        _jumpBufferTime = Properties.JumpBuffer;

        MoveInput.Subscribe(this, (Vector2 value) =>
        {
            _moveInput.x = value.x;
            _moveInput.z = value.y;
        });

        JumpInput.Subscribe(this, (bool value) =>
        {
            _moveInput.y = value ? 1 : 0;

            if (value)
            {
                if (_canJump)
                {
                    _jumpBufferTime = 0f;
                    _canJump = false;
                }
            }
            else
                _canJump = true;
        });
    }

    private void Cache()
    {
        if(!_cached)
        {
            _raycastContext = gameObject.GetComponent<CharacterRaycastContext>();
            _motionContext = gameObject.GetComponent<CharacterMotionContext>();
            _dt = Time.fixedDeltaTime;
            _cached = true;
        }
    }

    private void FixedUpdate()
    {
        HandleGroundRide();
        _motionContext.ApplyCappedHorizontalForce(_moveInput, Properties.MoveSpeed, Properties.Acceleration, Properties.Deceleration);
        HandleJump();
        _moveInput.Value = new Vector3(5, 5, 5);
    }

    private void HandleGroundRide()
    {
        if (_raycastContext.IsGrounded && !_isJumping)
        {
            _hadJumped = false;
            _groundY = _raycastContext.GroundPosition.y;
            _prevGrounded = true;
            _reachedJumpArc = false;
            _motionContext.SpringToVertically(_groundY, Properties.RideHeight, Properties.RideSpringStrength, Properties.HangTimeDamper, Properties.MaxHoverForce);
        }
        else
        {
            float hangTimeDuration = _tempHangTimeDuration > 0 ? _tempHangTimeDuration : Properties.HangTimeDuration;

            if (_enableGravity && !(_currentHangTime < hangTimeDuration && _hadJumped) && !_raycastContext.IsGrounded)
                _motionContext.ApplyGravity();

            _prevGrounded = false;
        }
    }

    private void HandleJump()
    {
        float hangTimeDuration = _tempHangTimeDuration > 0 ? _tempHangTimeDuration : Properties.HangTimeDuration;

        if (_moveInput.y == 0)
        {
            if(_prevGrounded)
                _jumpResetTime += _dt;

            if(_isJumping)
            {
                _jumpBufferTime = Properties.JumpBuffer;
                _isJumping = false;
            }
        }

        if (!_isJumping && _prevGrounded)
        {
            _jumpResetTime += _dt;

            if (_jumpResetTime > Properties.JumpResetDelay && _jumpBufferTime < Properties.JumpBuffer)
            {
                _isJumping = true;
                _prevGrounded = false;
                _jumpResetTime = 0f;
                _jumpArcY = _raycastContext.GroundPosition.y + Properties.JumpHeight;

                if(_motionContext.Velocity.y < Properties.JumpSpeed)
                    _motionContext.ApplyVerticalForce(Properties.JumpSpeed);

                _jumpStartGroundHeight = _raycastContext.GroundPosition.y;
                _reachedJumpArc = false;

                _hadJumped = true;
                _enableGravity = false;
            }
        }
        else if(_isJumping)
        {
            Vector3 predictedPos = _motionContext.PredictNextPosition();

            if (predictedPos.y < _jumpArcY)
            {
                if (_motionContext.Velocity.y < Properties.JumpSpeed)
                    _motionContext.ApplyVerticalForce(Properties.JumpSpeed);
            }
            else
            {
                _isJumping = false;
                _currentHangTime = 0f;
                _reachedJumpArc = true;
            }
        }
        else if (_currentHangTime < hangTimeDuration && _hadJumped)
        {
            if(_reachedJumpArc)
                _motionContext.SpringToVertically(_jumpStartGroundHeight, Properties.JumpHeight, Properties.RideSpringStrength, Properties.HangTimeDamper, Properties.MaxHoverForce);

            _currentHangTime += _dt;
        }
        else if(!_raycastContext.IsGrounded)
        {
            _motionContext.ApplyVerticalForce(-Properties.FallSpeed);
            _enableGravity = true;
        }

        _jumpBufferTime += _dt;
    }


    private void OnCollisionEnter(Collision collision)
    {
        foreach (var contact in collision.contacts)
        {
            if (Vector3.Dot(contact.normal, Vector3.down) > 0.5f)
            {
                _currentHangTime = Properties.HangTimeDuration; 
                break;
            }
        }
    }
}
