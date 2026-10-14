using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies forces, springs and gravity to a character's Box3D body. Movement controllers on the same GameObject drive it
/// through its methods (on the selected instance): <c>Self.GetSchematicBehavior&lt;CharacterMotionContext&gt;().ApplyForce(...)</c>.
/// </summary>
[SchematicDoc(@"{""name"": ""Character Motion Context"", ""kind"": ""behavior"", ""menu"": ""Movement/3D/Character Motion Context"", ""description"": ""Applies forces, springs and gravity to a character's Box3D body. Movement controllers on the same GameObject drive it through its methods (on the selected instance): Self.GetSchematicBehavior<CharacterMotionContext>().ApplyForce(...)."", ""variables"": [""Properties"", ""OnPushed"", ""SpringPathFinished"", ""IsKinematic"", ""CameraTransform""], ""signals"": [""JumpInput"", ""MoveInput"", ""StaminaUpdated"", ""SetDirectionOfGravity"", ""OnJump"", ""OnMoveOnGround"", ""OnFall""], ""use_for"": [""applying movement forces to a character""], ""notes"": ""Required by the movement controllers; send its input Signals to move.""}")]
[SchematicBehaviorPath("Movement/3D/Character Motion Context")]
[RequireComponent(typeof(Box3DBody))]
[RequireComponent(typeof(Box3DSphereShape))]
[RequireComponent(typeof(SchematicImpactRelay))]
public class CharacterMotionContext : SchematicBehavior
{
    // IO
    [SchematicDoc(@"{""name"": ""JumpInput"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""input"", ""params"": [{""name"": ""Value"", ""type"": ""bool""}], ""description"": ""Tells Character Motion Context to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
    [InputSignal]
    [Parameter("Value", typeof(bool))]
    public readonly SignalHandle<bool> JumpInput;
    [SchematicDoc(@"{""name"": ""MoveInput"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""input"", ""params"": [{""name"": ""Value"", ""type"": ""Vector2""}], ""description"": ""Tells Character Motion Context to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
    [InputSignal]
    [Parameter("Value", typeof(Vector2))]
    public readonly SignalHandle<Vector2> MoveInput;
    [SchematicDoc(@"{""name"": ""StaminaUpdated"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""input"", ""params"": [{""name"": ""Amount"", ""type"": ""float""}], ""description"": ""Tells Character Motion Context to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
    [InputSignal]
    [Parameter("Amount", typeof(float))]
    public readonly SignalHandle<float> StaminaUpdated;
    [SchematicDoc(@"{""name"": ""SetDirectionOfGravity"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""input"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Tells Character Motion Context to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
    [InputSignal]
    [Parameter("Direction", typeof(Vector3))]
    public readonly SignalHandle<Vector3> SetDirectionOfGravity;

    [SchematicDoc(@"{""name"": ""OnJump"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""output"", ""description"": ""Sent by Character Motion Context when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
    [OutputSignal]
    [NetworkSync(SignalSender.OwnerToAuthority)]
    public readonly SignalHandle OnJump;
    [SchematicDoc(@"{""name"": ""OnMoveOnGround"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""output"", ""params"": [{""name"": ""Velocity"", ""type"": ""Vector3""}], ""description"": ""Sent by Character Motion Context when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
    [OutputSignal]
    [Parameter("Velocity", typeof(Vector3))]
    public readonly SignalHandle<Vector3> OnMoveOnGround;
    [SchematicDoc(@"{""name"": ""OnFall"", ""kind"": ""signal"", ""behavior"": ""Character Motion Context"", ""direction"": ""output"", ""description"": ""Sent by Character Motion Context when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
    [OutputSignal]
    public readonly SignalHandle OnFall;

    //Data
    [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Character Motion Context"", ""type"": ""CharacterMotionProperties"", ""default"": ""null"", ""description"": ""Properties (CharacterMotionProperties) of Character Motion Context.""}")]
    public readonly VariableHandle<CharacterMotionProperties> Properties = (CharacterMotionProperties)null;

    /// <summary>Raised with the relative velocity of a collision that pushed the character.</summary>
    [SchematicDoc(@"{""name"": ""OnPushed"", ""kind"": ""variable"", ""behavior"": ""Character Motion Context"", ""type"": ""Action<Vector3>"", ""default"": ""null"", ""description"": ""Raised with the relative velocity of a collision that pushed the character.""}")]
    public readonly VariableHandle<Action<Vector3>> OnPushed = (Action<Vector3>)null;
    /// <summary>Raised each time a point of a spring path is reached.</summary>
    [SchematicDoc(@"{""name"": ""SpringPathFinished"", ""kind"": ""variable"", ""behavior"": ""Character Motion Context"", ""type"": ""Action"", ""default"": ""null"", ""description"": ""Raised each time a point of a spring path is reached.""}")]
    public readonly VariableHandle<Action> SpringPathFinished = (Action)null;

    [SchematicDoc(@"{""name"": ""IsKinematic"", ""kind"": ""variable"", ""behavior"": ""Character Motion Context"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Is Kinematic (bool) of Character Motion Context.""}")]
    public readonly VariableHandle<bool> IsKinematic = true;
    [SchematicDoc(@"{""name"": ""CameraTransform"", ""kind"": ""variable"", ""behavior"": ""Character Motion Context"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Camera Transform (Transform) of Character Motion Context.""}")]
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
    // The Box3D body is created in its own Awake, which may come after OnCreate: it's set up on the first physics step.
    private readonly VariableHandle<bool> _bodyConfigured = false;

    private static float DeltaTime => Time.fixedDeltaTime;

    // Lifecycle
    protected override void OnCreate()
    {
        // Friction and bounce from the Physics Material, applied to the Box3D shape.
        var properties = Properties.Value;
        var shape = Self.GetComponent<Box3DSphereShape>();
        if (properties != null && properties.PhysicsMaterial != null && shape != null)
        {
            shape.SetFriction(properties.PhysicsMaterial.dynamicFriction);
            shape.SetRestitution(properties.PhysicsMaterial.bounciness);
        }
    }

    // Upright and gravity-free: this context applies gravity itself, along its own gravity direction.
    private void ConfigureBody(Box3DBody body)
    {
        if (_bodyConfigured.Value || !body.IsLive())
            return;
        body.FreezeRotation();
        body.SetGravityScale(0f);
        _bodyConfigured.Value = true;
    }

    protected override void OnStart()
    {
        var instance = Self;
        MoveInput.Subscribe(value =>
        {
            ref var input = ref _moveInput.For(instance);
            input.x = value.x;
            input.z = value.y;
        });

        StaminaUpdated.Subscribe(value => _currentStamina[instance] = value);
        SetDirectionOfGravity.Subscribe(value => _worldDirection[instance] = value);
    }

    protected override void OnEnable()
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

    protected override void OnFixedUpdate()
    {
        var properties = Properties.Value;
        if (properties == null)
            return;

        var body = Self.GetComponent<Box3DBody>();
        ConfigureBody(body);
        var raycast = Self.GetSchematicBehavior<CharacterRaycastContext>();
        body.SetKinematic(IsKinematic.Value);

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

                if (Vector3.Distance(Self.Transform.position, point) < properties.SpringEpsilon)
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
            HandleWallSlidingMovement(Self, properties, raycast);

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
            body.WakeUp();
            body.SetVelocity(velocity);
            OnMoveOnGround.Invoke(Self, horizontalVelocity);
        }
        else
        {
            body.Sleep();
            wallProjectedVelocity = Vector3.zero;
        }

        // reset forces
        forcesCount = 0;
        Array.Clear(forces, 0, forces.Length);
    }

    // Box3D impacts (through the Schematic Impact Relay) push the character.
    protected override void OnImpact(PhysicsImpact impact)
    {
        var properties = Properties.Value;
        Vector3 push = impact.RelativeVelocity;
        if (properties != null && _canBePushed.Value && push.sqrMagnitude > properties.CollisionPushVelocityThreshold)
        {
            OnPushed.Value?.Invoke(push);
            _velocity.Value += push * properties.CollisionPushInfluence;
        }
    }

    // public api (acts on the selected instance)

    /// <summary>
    /// Moves the Character to a specific position and makes its body kinematic so that it holds the exact position without forces acting on it.
    /// </summary>
    /// <param name="position">The position to move to.</param>
    /// <param name="canBePushed">Whether collisions from other objects can push the Character out of the locked position.</param>
    public void SetPositionLocked(Vector3 position, bool canBePushed = true)
    {
        var body = Selected.GetComponent<Box3DBody>();
        body.SetKinematic(true);
        body.Teleport(position);
        _canBePushed.Value = canBePushed;
    }

    public void SetRotationLocked(Quaternion rotation)
    {
        Selected.GetComponent<Box3DBody>().TeleportRotation(rotation);
    }

    /// <summary>
    /// Applies the given force to the character
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
        var body = Selected.GetComponent<Box3DBody>();
        return Selected.Transform.position + body.GetVelocity() * DeltaTime * DeltaTime;
    }

    public void ApplyGravity()
    {
        _velocity.Value += RotateByGravityDirection(SchematicPhysics.Gravity * DeltaTime);
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
    /// Iterates through an array of positions to spring the character along a path
    /// </summary>
    public void FollowSpringPath(float startPositionY, List<Vector3> path)
    {
        _springStartY.Value = startPositionY;
        _springPathIndex.Value = 0;
        _springPath.Value = path;
    }

    /// <summary>
    /// Springs the character toward the given position with the given force
    /// </summary>
    public void SpringTo(float groundYPosition, Vector3 position, float strength, float damper, float maxForce)
    {
        SpringHorizontally(position, strength, damper, maxForce);
        SpringToVertically(groundYPosition, position.y, strength, damper, maxForce);
    }

    /// <summary>
    /// Springs the character to the given horizontal position
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
    /// Springs the character to the given vertical position
    /// </summary>
    public void SpringToVertically(float groundYPosition, float targetYPosition, float strength, float damper, float maxForce)
    {
        float heightError = (groundYPosition + targetYPosition) - Selected.Transform.position.y;
        float differenceForce = ((heightError * strength) - (Velocity.y * damper));

        ApplyVerticalForce(differenceForce);
    }

    public void TeleportToPosition(Vector3 position)
    {
        var body = Selected.GetComponent<Box3DBody>();
        body.SetKinematic(false);
        body.Teleport(position);
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
        var body = context.GetComponent<Box3DBody>();
        var shape = context.GetComponent<Box3DSphereShape>();
        if (body == null || shape == null)
            return;

        var position = context.Transform.position;
        Vector3 bodyVelocity = body.GetVelocity();
        Vector3 scale = context.Transform.lossyScale;

        Gizmos.color = !body.IsKinematic() ? new Color(0f, 0.3f, 1f, 0.75f) : new Color(1f, 0.3f, 0f, 0.75f);
        Gizmos.DrawSphere(position, shape.SphereRadius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

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
            CustomGizmos.DrawArrow(wallPosition, _wallProjectedVelocity[instance], bodyVelocity.magnitude, new Color(0, 1f, 0, 0.5f));
        }
        else if (bodyVelocity != Vector3.zero)
            CustomGizmos.DrawArrow(position, bodyVelocity, bodyVelocity.magnitude * 0.5f, new Color(0, 1f, 0, 0.5f));

        CustomGizmos.DrawArrow(position, _moveInput[instance], 2f, new Color(1, 1f, 1, 0.5f));
    }

}
