using UnityEngine;

/// <summary>
/// The Schematic Behavior counterpart to GetComponent.
/// </summary>
public static class SchematicBehaviorExtensions
{
    /// <summary>
    /// Gets the <typeparamref name="TBehavior"/> system with this GameObject's instance selected, so its
    /// Variables and methods act on this object: <c>enemy.GetSchematicBehavior&lt;Health&gt;().Current.Value -= 10;</c><br/>
    /// Returns null if the Behavior doesn't control this GameObject, or the object isn't spawned.<br/>
    /// The lookup is O(1) (a table per GameObject, indexed by Behavior type). Inside a Behavior, reach Behaviors on your
    /// own GameObject through a <see cref="BehaviorHandle{TBehavior}"/> field instead.
    /// </summary>
    /// <remarks>
    /// The selection lasts until something selects another instance of the same Behavior. Lifecycle methods restore
    /// their own selection when they return, but picking another object of your own Behavior type from inside one of
    /// your callbacks redirects your Variables to it; use <c>handle[instance]</c> for that case instead.
    /// </remarks>
    public static TBehavior GetSchematicBehavior<TBehavior>(this GameObject gameObject) where TBehavior : SchematicBehavior
    {
        var instance = ExecutionContext.FindInstance<TBehavior>(gameObject);
        if (instance == null)
            return null;

        instance.Behavior.Selected = instance;
        return (TBehavior)instance.Behavior;
    }

    public static TBehavior GetSchematicBehavior<TBehavior>(this Component component) where TBehavior : SchematicBehavior
        => component != null ? component.gameObject.GetSchematicBehavior<TBehavior>() : null;

    /// <summary>
    /// Like <see cref="GetSchematicBehavior{TBehavior}(GameObject)"/>, as a try-pattern.
    /// </summary>
    public static bool TryGetSchematicBehavior<TBehavior>(this GameObject gameObject, out TBehavior behavior) where TBehavior : SchematicBehavior
    {
        behavior = gameObject.GetSchematicBehavior<TBehavior>();
        return behavior != null;
    }
}
