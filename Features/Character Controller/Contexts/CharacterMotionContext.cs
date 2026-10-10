using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies forces, springs and gravity to a character's Rigidbody. Movement controllers on the same GameObject drive it
/// through its methods (on the selected instance): <c>instance.GetSchematicBehavior&lt;CharacterMotionContext&gt;().ApplyForce(...)</c>.
/// </summary>
[SchematicBehaviorPath("Movement/3D/Character Motion Context")]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class CharacterMotionContext : SchematicBehavior
{
    // IO
    [InputSignal]
    [Parameter("Value", typeof(bool))]
    public readonly SignalHandle<bool> JumpInput;
    [InputSignal]
    [Parameter("Value", typeof(Vector2))]
    public readonly SignalHandle<Vector2> MoveInput;
    [InputSignal]
    [Parameter("Amount", typeof(float))]
    public readonly SignalHandle<float> StaminaUpdated;
    [InputSignal]
    [Parameter("Direction", typeof(Vector3))]
    public readonly SignalHandle<Vector3> SetDirectionOfGravity;

    [OutputSignal]
    public readonly SignalHandle OnJump;
    [OutputSignal]
    [Parameter("Velocity", typeof(Vector3))]
    public readonly SignalHandle<Vector3> OnMoveOnGround;
    [OutputSignal]
    public readonly SignalHandle OnFall;

    //Data
    public readonly VariableHandle<CharacterMotionProperties> Properties = (CharacterMotionProperties)null;

    /// <summary>Raised with the relative velocity of a collision that pushed the character.</summary>
    public readonly VariableHandle<Action<Vector3>> OnPushed = (Action<Vector3>)null;
    /// <summary>Raised each time a point of a spring path is reached.</summary>
    public readonly VariableHandle<Action> SpringPathFinished = (Action)null;

    public readonly VariableHandle<bool> IsKinematic = true;
    public readonly VariableHandle<Transform> CameraTransform = (Transform)null;

    // Accessors (selected instance)
    public Vector3 Velocity => _velocity.Value;
    public Vector3 HorizontalVelocity => _horizontalVelocity.Value;
    public Vector3 HorizontalDirection => _horizontalMovementDirection.Value;
    public bool JumpPooled => _jumpPooled.Value;
    public float CurrentStamina => _currentStamina.Value;
    public Vector3 MoveDirection => _moveInput.Value;

    // state vars
    private readonly VariableHandle<Vector3> _worldDirection = Vector3.zero;
    private readonly VariableHandle<Vector3> _velocity = Vector3.zero;
    private readonly VariableHandle<Vector3> _horizontalVelocity = Vector3.zero;
    private readonly VariableHandle<Vector3> _wallProjectedVelocity = Vector3.zero;
    private readonly VariableHandle<Vector3> _wallHitPoint = Vector3.zero;
    private readonly VariableHandle<Vector3> _horizontalMovementDirection = Vector3.zero;

    private const int MaxForces = 10;
    // Copied per spawn, so every character has its own buffer.
    private readonly VariableHandle<Vector3[]> _forces = new Vector3[MaxForces];
    private readonly VariableHandle<Vector3> _overrideVelocity = Vector3.zero;
    private readonly VariableHandle<int> _forcesCount = 0;
    private readonly VariableHandle<bool> _jumpPooled = false;
    private readonly VariableHandle<float> _currentStamina = 0f;
    private readonly VariableHandle<bool> _canBePushed = false;

    private readonly VariableHandle<List<Vector3>> _springPath = (List<Vector3>)null;
    private readonly VariableHandle<int> _springPathIndex = 0;
    private readonly VariableHandle<float> _springStartY = 0f;
    private readonly VariableHandle<bool> _isResting = false;

    private readonly VariableHandle<Vector3> _moveInput = Vector3.zero;

    private static float DeltaTime => Time.fixedDeltaTime;

    // Lifecycle
    protected override void OnCreate(BehaviorInstance instance)
    {
        var rb = instance.GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.useGravity = false;

        var properties = Properties.Value;
        if (properties != null)
            instance.GetComponent<SphereCollider>().sharedMaterial = properties.PhysicsMaterial;
    }

    protected override void OnStart(BehaviorInstance instance)
    {
        MoveInput.Subscribe(value =>
        {
            ref var input = ref _moveInput.For(instance);
            input.x = value.x;
            input.z = value.y;
        });

        StaminaUpdated.Subscribe(value => _currentStamina[instance] = value);
        SetDirectionOfGravity.Subscribe(value => _worldDirection[instance] = value);
    }

    protected override void OnEnable(BehaviorInstance instance)
    {
        var camera = CameraSubsystem.Camera != null ? CameraSubsystem.Camera.transform : null;

        if (camera == null && Camera.main != null)
        {
            camera = Camera.main.transform;
            Debug.LogWarning($"MotionContext: Global camera not found, defaulting to MainCamera ({camera.name})");
        }

        if (camera == null)
            Debug.LogError("MotionContext: No camera found for motions to reference!");

        CameraTransform.Value = camera;
    }

    protected override void OnFixedUpdate(BehaviorInstance instance)
    {
        var properties = Properties.Value;
        if (properties == null)
            return;

        var rb = instance.GetComponent<Rigidbody>();
        var raycast = instance.GetSchematicBehavior<CharacterRaycastContext>();
        rb.isKinematic = IsKinematic.Value;

        ref var velocity = ref _velocity.Ref;
        ref var horizontalVelocity = ref _horizontalVelocity.Ref;
        ref var wallProjectedVelocity = ref _wallProjectedVelocity.Ref;
        ref var overrideVelocity = ref _overrideVelocity.Ref;
        ref var forcesCount = ref _forcesCount.Ref;
        var forces = _forces.Value;

        raycast?.PerformGroundCast();

        // calculate spring arc (if there is one)
        var springPath = _springPath.Value;
        if (springPath != null)
        {
            ref var index = ref _springPathIndex.Ref;
            if (index < springPath.Count)
            {
                var point = springPath[index];

                SpringTo(_springStartY.Value, point, properties.SpringStrength, properties.SpringDamper, properties.SpringMaxForce);

                if (Vector3.Distance(instance.Transform.position, point) < properties.SpringEpsilon)
                {
                    index++;
                    SpringPathFinished.Value?.Invoke();
                }
            }
            else
            {
                _springPath.Value = null;
                index = 0;
            }
        }

        if (raycast != null)
            HandleWallSlidingMovement(instance, properties, raycast);

        // apply forces
        for (int i = 0; i < forcesCount; i++)
            velocity += forces[i];

        if (overrideVelocity != Vector3.zero)
        {
            velocity = overrideVelocity;
            overrideVelocity = Vector3.zero;
        }
        if (_wallHitPoint.Value != Vector3.zero)
        {
            velocity.x = wallProjectedVelocity.x;
            velocity.z = wallProjectedVelocity.z;
        }

        horizontalVelocity.x = velocity.x;
        horizontalVelocity.z = velocity.z;

        // rest
        ref var isResting = ref _isResting.Ref;
        isResting = false;

        if (horizontalVelocity.sqrMagnitude < properties.MovementSnapThreshold) // *gringe for the magic numbers*
        {
            if (Mathf.Pow(Mathf.Abs(velocity.y) * 10, 2) < properties.MovementSnapThreshold)
                isResting = true;
        }
        else
        {
            _horizontalMovementDirection.Value = horizontalVelocity.normalized;
        }

        // apply velocity
        if (!isResting)
        {
            rb.WakeUp();
            rb.linearVelocity = velocity;
            OnMoveOnGround.Invoke(instance, horizontalVelocity);
        }
        else
        {
            rb.Sleep();
            wallProjectedVelocity = Vector3.zero;
        }

        // reset forces
        forcesCount = 0;
        Array.Clear(forces, 0, forces.Length);
    }

    protected override void OnCollisionEnter(BehaviorInstance instance, Collision collision)
    {
        var properties = Properties.Value;
        if (properties != null && _canBePushed.Value && collision.relativeVelocity.sqrMagnitude > properties.CollisionPushVelocityThreshold)
        {
            OnPushed.Value?.Invoke(collision.relativeVelocity);
            _velocity.Value += collision.relativeVelocity * properties.CollisionPushInfluence;
        }
    }

    // public api (acts on the selected instance)

    /// <summary>
    /// Moves the Character to a specific position and forces the Rigidbody to Kinematic so that it can set the exact position of the Character without forces acting on it.
    /// </summary>
    /// <param name="position">The position to move to.</param>
    /// <param name="canBePushed">Whether collisions from other objects can switch the Rigidbody out of Kinematic mode.</param>
    public void SetPositionLocked(Vector3 position, bool canBePushed = true)
    {
        var rb = Selected.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.position = position;
        _canBePushed.Value = canBePushed;
    }

    public void SetRotationLocked(Quaternion rotation)
    {
        var rb = Selected.GetComponent<Rigidbody>();
        bool isKinematic = rb.isKinematic;

        rb.isKinematic = true;
        rb.rotation = rotation;
        rb.isKinematic = isKinematic;
    }

    /// <summary>
    /// Applies the given force to the Rigidbody
    /// </summary>
    public void ApplyForce(Vector3 force, bool overrideOtherForces = false)
    {
        force = RotateByGravityDirection(force);
        AddForce(force, overrideOtherForces);
    }

    public void ApplyHorizontalForce(Vector3 force, bool overrideOtherForces = false)
    {
        force.y = 0;
        AddForce(RotateByGravityDirection(force), overrideOtherForces);
    }

    public void ApplyVerticalForce(float force, bool overrideOtherForces = false)
    {
        AddForce(RotateByGravityDirection(new Vector3(0f, force, 0f)), overrideOtherForces);
    }

    private void AddForce(Vector3 force, bool overrideOtherForces)
    {
        if (overrideOtherForces)
        {
            _overrideVelocity.Value = force;
            return;
        }

        ref var count = ref _forcesCount.Ref;
        var forces = _forces.Value;
        if (count < forces.Length)
        {
            forces[count] = force;
            count++;
        }
    }

    public Vector3 PredictNextPosition()
    {
        var rb = Selected.GetComponent<Rigidbody>();
        return rb.position + rb.linearVelocity * DeltaTime * DeltaTime;
    }

    public void ApplyGravity()
    {
        _velocity.Value += RotateByGravityDirection(Physics.gravity * DeltaTime);
    }

    /// <summary>
    /// Uses physics forces to push the character in a direction at a specific speed
    /// </summary>
    public void ApplyCappedHorizontalForce(Vector3 direction, float maxSpeed, float acceleration, float decceleration, bool applyCameraTransform = true)
    {
        float desiredSpeed = maxSpeed;
        float desiredRateOfChange = acceleration;

        if (Mathf.Abs(direction.x) + Mathf.Abs(direction.z) < 0.1f)
        {
            desiredSpeed = 0;
            desiredRateOfChange = decceleration;
        }

        if (applyCameraTransform && CameraTransform.Value != null)
            direction = TransformInputDirection(direction);

        direction.y = 0f;
        direction.Normalize();

        Vector3 desiredVel = (direction * desiredSpeed);

        Vector3 clampedVel = desiredVel.normalized * desiredSpeed;
        Vector3 excess = HorizontalVelocity - RotateByGravityDirection(clampedVel);

        Vector3 correction = -excess.normalized * desiredRateOfChange;

        ApplyHorizontalForce(correction * DeltaTime);
    }

    /// <summary>
    /// Iterates through an array of positions to spring the rigidbody in a path
    /// </summary>
    public void FollowSpringPath(float startPositionY, List<Vector3> path)
    {
        _springStartY.Value = startPositionY;
        _springPathIndex.Value = 0;
        _springPath.Value = path;
    }

    /// <summary>
    /// Springs the Rigidbody toward the given position with the given force
    /// </summary>
    public void SpringTo(float groundYPosition, Vector3 position, float strength, float damper, float maxForce)
    {
        SpringHorizontally(position, strength, damper, maxForce);
        SpringToVertically(groundYPosition, position.y, strength, damper, maxForce);
    }

    /// <summary>
    /// Springs the Rigidbody to the given HorizontalPosition
    /// </summary>
    public void SpringHorizontally(Vector3 targetPosition, float strength, float damper, float maxForce)
    {
        Vector3 horizontalPosition = Selected.Transform.position;
        horizontalPosition.y = 0;
        Vector3 delta = targetPosition - horizontalPosition;
        Vector3 springForce = (delta * strength) - (HorizontalVelocity * damper);
        springForce = Vector3.ClampMagnitude(springForce * DeltaTime, maxForce);
        ApplyHorizontalForce(springForce);
    }

    /// <summary>
    /// Springs the Rigidbody to the given vertical position
    /// </summary>
    public void SpringToVertically(float groundYPosition, float targetYPosition, float strength, float damper, float maxForce)
    {
        float heightError = (groundYPosition + targetYPosition) - Selected.Transform.position.y;
        float differenceForce = ((heightError * strength) - (Velocity.y * damper));

        ApplyVerticalForce(differenceForce);
    }

    public void TeleportToPosition(Vector3 position)
    {
        var rb = Selected.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.position = position;
        rb.isKinematic = false;
    }

    public void PoolJump()
    {
        _jumpPooled.Value = true;
    }

    public void PerformJump()
    {
        _jumpPooled.Value = false;
    }

    public Vector3 TransformInputDirection(Vector3 inputDirection)
    {
        var camera = CameraTransform.Value;
        return camera != null ? camera.TransformDirection(inputDirection) : inputDirection;
    }

    public void WallCheckInMoveDirection()
    {
        var instance = Selected;
        ref var moveInput = ref _moveInput.Ref;
        moveInput = TransformInputDirection(moveInput);
        instance.GetSchematicBehavior<CharacterRaycastContext>()?.PerformWallCast(instance, moveInput);
    }

    // internals
    private void HandleWallSlidingMovement(BehaviorInstance instance, CharacterMotionProperties properties, CharacterRaycastContext raycast)
    {
        if (!properties.SlideOnWall) return;

        WallCheckInMoveDirection();

        ref var wallProjectedVelocity = ref _wallProjectedVelocity.Ref;
        if (raycast.WallCastResult[instance].TryGetClosestHit(out var closestWallHit))
        {
            _wallHitPoint.Value = closestWallHit.point;
            wallProjectedVelocity = Vector3.ProjectOnPlane(HorizontalVelocity, -closestWallHit.normal);

            if (wallProjectedVelocity.magnitude > 0.01f)
            {
                raycast.PerformWallCast(instance, wallProjectedVelocity.normalized);
                if (raycast.WallCastResult[instance].HasHit)
                    wallProjectedVelocity = Vector3.zero;
            }
        }
        else
        {
            wallProjectedVelocity = default;
            _wallHitPoint.Value = default;
        }
    }

    // Forces are given relative to the direction of gravity.
    private Vector3 RotateByGravityDirection(Vector3 inputForce)
    {
        ref var worldDirection = ref _worldDirection.Ref;
        if (worldDirection == Vector3.zero)
            worldDirection = Vector3.forward;

        return Quaternion.LookRotation(worldDirection, Vector3.forward) * inputForce;
    }

    protected override void OnDrawGizmos(BehaviorGizmoContext context)
    {
        var rb = context.GetComponent<Rigidbody>();
        var collider = context.GetComponent<SphereCollider>();
        if (rb == null || collider == null)
            return;

        var position = context.Transform.position;

        Gizmos.color = !rb.isKinematic ? new Color(0f, 0.3f, 1f, 0.75f) : new Color(1f, 0.3f, 0f, 0.75f);
        Gizmos.DrawSphere(position + collider.center, collider.radius);

        var instance = context.Instance;
        if (instance == null || !instance.IsSpawned)
            return;

        var forces = _forces[instance];
        for (int i = 0; i < _forcesCount[instance]; i++)
        {
            if (forces[i] != Vector3.zero)
                CustomGizmos.DrawArrow(position, forces[i], forces[i].magnitude * 2, new Color(1f, 1f, 0f, 0.3f));
        }

        var properties = Properties[instance];
        var raycast = instance.GetSchematicBehavior<CharacterRaycastContext>();
        if (properties != null && properties.SlideOnWall && raycast != null && raycast.WallCastResult[instance].TryGetClosestHit(out _))
        {
            var wallPosition = raycast.WallPosition[instance];
            Gizmos.color = Color.green;
            Gizmos.DrawLine(position, wallPosition);
            CustomGizmos.DrawArrow(wallPosition, _wallProjectedVelocity[instance], rb.linearVelocity.magnitude, new Color(0, 1f, 0, 0.5f));
        }
        else if (rb.linearVelocity != Vector3.zero)
            CustomGizmos.DrawArrow(position, rb.linearVelocity, rb.linearVelocity.magnitude * 0.5f, new Color(0, 1f, 0, 0.5f));

        CustomGizmos.DrawArrow(position, _moveInput[instance], 2f, new Color(1, 1f, 1, 0.5f));
    }

}
