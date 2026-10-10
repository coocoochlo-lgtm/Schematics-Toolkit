using Remedy.Framework;
using UnityEngine;

/// <summary>
/// Ground and wall detection for a character, shared by the Motion Context and the movement controllers.
/// </summary>
[SchematicBehaviorPath("Movement/3D/Character Raycast Context")]
public class CharacterRaycastContext : SchematicBehavior
{
    [Tooltip("Casts for a wall in the given direction.")]
    [InputSignal, Parameter("Direction", typeof(Vector3))]
    public readonly SignalHandle<Vector3> CheckForWall;
    [Tooltip("Sent when a wall cast hits, passing the point that was hit.")]
    [OutputSignal, Parameter("Point", typeof(Vector3))]
    public readonly SignalHandle<Vector3> OnHitWall;

    public readonly VariableHandle<CharacterRaycastProperties> Properties = (CharacterRaycastProperties)null;

    public readonly VariableHandle<bool> IsGrounded = false;
    public readonly VariableHandle<Vector3> GroundPosition = Vector3.zero;
    public readonly VariableHandle<Vector3> WallPosition = Vector3.zero;

    public readonly VariableHandle<RaycastUtility.RaycastResult> GroundCastResult = default(RaycastUtility.RaycastResult);
    public readonly VariableHandle<RaycastUtility.RaycastResult> WallCastResult = default(RaycastUtility.RaycastResult);

    private readonly VariableHandle<Vector3> _dirToWall = Vector3.zero;
    private readonly VariableHandle<Vector3> _wallNorm = Vector3.zero;

    protected override void OnStart(BehaviorInstance instance)
    {
        CheckForWall.Subscribe(direction => PerformWallCast(instance, direction));
    }

    /// <summary>Casts for ground below the selected character.</summary>
    public void PerformGroundCast() => PerformGroundCast(Selected);

    public void PerformGroundCast(BehaviorInstance instance)
    {
        var properties = Properties[instance];
        if (properties == null) return;

        var transform = instance.Transform;
        var result = RaycastUtility.SphereCast(transform, transform.position, Vector3.down, properties.GroundCheckSphereRadius, properties.GroundCheckDistance, properties.CollisionMask, -1f, properties.MaxGroundSlopeAngle, properties.GroundCheckDistance);
        GroundCastResult[instance] = result;

        if (result.TryGetClosestHit(out RaycastHit hit))
            GroundPosition[instance] = hit.point;

        IsGrounded[instance] = result.HasHit;
    }

    /// <summary>Casts for a wall from the selected character in a direction.</summary>
    public void PerformWallCast(Vector3 direction) => PerformWallCast(Selected, direction);

    public void PerformWallCast(BehaviorInstance instance, Vector3 direction)
    {
        var properties = Properties[instance];
        if (properties == null) return;

        var transform = instance.Transform;
        var result = RaycastUtility.SphereCast(transform, transform.position, direction, properties.WallCheckSphereRadius, properties.WallCheckDistance, properties.CollisionMask, properties.MinWallAngle, properties.MaxWallAngle, properties.WallCheckDistance);
        WallCastResult[instance] = result;

        if (result.TryGetClosestHit(out RaycastHit hit))
        {
            WallPosition[instance] = hit.point;
            _dirToWall[instance] = direction;
            _wallNorm[instance] = hit.normal;
            OnHitWall.Invoke(instance, hit.point);
        }
    }

    protected override void OnDrawGizmos(BehaviorGizmoContext context)
    {
        if (context.Instance == null)
            return;

        var position = context.Transform.position;

        if (context.Get(GroundCastResult).HasHit)
        {
            CustomGizmos.DrawCircleArrow(
                position,
                Vector3.down,
                Mathf.Abs(Mathf.Abs(context.Get(GroundPosition).y) - Mathf.Abs(position.y)),
                new Color(1, 0, 0, 0.5f)
            );
        }

        // Draw hit point if wall detected
        if (context.Get(WallCastResult).HasHit)
        {
            CustomGizmos.DrawCircleArrow(
                position,
                context.Get(_dirToWall),
                Vector3.Distance(position, context.Get(WallPosition)),
                new Color(1, 1, 0, 0.5f),
                normal: context.Get(_wallNorm)
            );
        }
    }
}
