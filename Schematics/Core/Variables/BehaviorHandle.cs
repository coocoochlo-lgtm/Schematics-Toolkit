using System;
using System.Runtime.CompilerServices;

/// <summary>
/// Another Behavior on the same GameObject, the Behavior counterpart to a cached <c>GetComponent</c>. Declare it as a
/// field, with no initializer (it's bound when the Behavior is created, like a <see cref="SignalHandle"/>):
/// <code>private readonly BehaviorHandle&lt;CharacterRaycastContext&gt; _raycast;</code>
/// Then, for any instance of your Behavior:
/// <list type="bullet">
/// <item><c>_raycast[instance]</c> / <c>_raycast.Instance</c>: the sibling's own <see cref="BehaviorInstance"/> (its row in
/// the other Behavior), for that Behavior's <c>handle[instance]</c> Variables and its instance overloads. Null if the
/// object has no such Behavior, or it isn't spawned.</item>
/// <item><c>_raycast.Select(instance)</c> / <c>_raycast.Selected</c>: the other Behavior with the sibling selected, so its
/// <c>.Value</c> Variables and selected-instance methods act on this object. The drop-in replacement for
/// <c>Self.GetSchematicBehavior&lt;T&gt;()</c>. Null if missing.</item>
/// <item><c>_raycast.Has(instance)</c>: whether the object has one. <c>_raycast.Behavior</c>: the other Behavior's one shared
/// system object, with nothing selected.</item>
/// </list>
/// <c>Instance</c> and <c>Selected</c> use your Behavior's selected instance (<see cref="SchematicBehavior.Self"/> inside
/// lifecycle methods).
/// </summary>
/// <remarks>
/// Every read is O(1) and allocation free: each <see cref="BehaviorInstance"/> keeps a reference to a table of the
/// Behaviors on its GameObject, indexed by a per-type id, so there's no lookup by GameObject, no scan and no type check
/// (a short scan only happens when the first attached Behavior of that type isn't spawned, e.g. mid-despawn).
/// The table is filled when Behaviors are attached (the object's first spawn, or <c>AddBehavior</c>), and reads check that
/// the sibling is spawned right now, so a despawned sibling reads as null and a Behavior added at runtime shows up at once.
/// A base type finds a derived Behavior, as with <c>GetSchematicBehavior</c>.<br/>
/// A Behavior's rows belong to that Behavior only. <c>_raycast[instance]</c> is a row of the Raycast Context, so
/// <c>raycast.IsGrounded[_raycast[instance]]</c> is right by construction, while <c>raycast.IsGrounded[instance]</c> (your
/// own row) throws.
/// <code>
/// [RequireBehavior(typeof(CharacterMotionContext))]
/// public class Glider : SchematicBehavior
/// {
///     private readonly BehaviorHandle&lt;CharacterMotionContext&gt; _motion;
///
///     protected override void OnFixedUpdate()
///     {
///         var motion = _motion.Selected;            // this object's Motion Context, selected
///         if (motion == null) return;
///         motion.ApplyVerticalForce(-1f);
///         bool kinematic = motion.IsKinematic[_motion.Instance];   // or index its Variables explicitly
///     }
/// }
/// </code>
/// </remarks>
public readonly struct BehaviorHandle<TBehavior> : IBehaviorHandle where TBehavior : SchematicBehavior
{
    private readonly BehaviorLink _link;

    // Only called through reflection (SchematicBehavior.BindBehaviorHandles), so kept from code stripping.
    [UnityEngine.Scripting.Preserve]
    internal BehaviorHandle(BehaviorLink link) { _link = link; }

    /// <summary>
    /// True for a field of a Behavior (bound when it was created). An unbound handle still works with an explicit
    /// instance (<c>handle[instance]</c>, <see cref="Select"/>, <see cref="Has"/>), but not with <see cref="Instance"/> or
    /// <see cref="Selected"/>, which need to know whose selection to follow.
    /// </summary>
    public bool IsBound => _link != null;

    /// <summary>
    /// The <typeparamref name="TBehavior"/> system: there is one per type, shared by every object. Nothing is selected by
    /// this. Null if <typeparamref name="TBehavior"/> is abstract.<br/>
    /// A sibling found through a base type belongs to the derived Behavior's system, not this one: where a derived Behavior
    /// may be attached, use the system <see cref="Select"/> returns, or <c>(TBehavior)handle[instance].Behavior</c>.
    /// </summary>
    public TBehavior Behavior
    {
        get
        {
            var link = _link;
            if (link != null && link.System != null)
                return (TBehavior)link.System;

            var behavior = typeof(TBehavior).IsAbstract ? null : (TBehavior)ExecutionContext.GetRuntime(typeof(TBehavior)).Behavior;
            if (link != null)
                link.System = behavior;
            return behavior;
        }
    }

    /// <summary>
    /// The <typeparamref name="TBehavior"/> instance on <paramref name="owner"/>'s GameObject (its row in that Behavior),
    /// or null if there is none or it isn't spawned. <paramref name="owner"/> can be an instance of any Behavior.
    /// </summary>
    public BehaviorInstance this[BehaviorInstance owner]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Sibling(owner);
    }

    /// <summary>The <typeparamref name="TBehavior"/> instance on the GameObject of your Behavior's selected instance, or null.</summary>
    public BehaviorInstance Instance => Sibling(OwnerCursor.Instance);

    /// <summary>True if <paramref name="owner"/>'s GameObject has a spawned <typeparamref name="TBehavior"/>.</summary>
    public bool Has(BehaviorInstance owner) => Sibling(owner) != null;

    /// <summary>
    /// Selects the <typeparamref name="TBehavior"/> instance on <paramref name="owner"/>'s GameObject and returns the
    /// Behavior, so its <c>.Value</c> Variables and methods act on that object; null if there is none. Like
    /// <c>GetSchematicBehavior</c>, the selection stays until something selects another instance of that Behavior.
    /// </summary>
    public TBehavior Select(BehaviorInstance owner) => SelectSibling(owner);

    /// <summary><see cref="Select"/> for your Behavior's selected instance (<c>Self</c> inside lifecycle methods).</summary>
    public TBehavior Selected => SelectSibling(OwnerCursor.Instance);

    private BehaviorCursor OwnerCursor
        => _link?.Cursor ?? throw new InvalidOperationException(
            $"This BehaviorHandle<{typeof(TBehavior).Name}> isn't bound to a Behavior, so it has no selection to follow. " +
            "Only BehaviorHandle fields of a SchematicBehavior are bound; pass the instance explicitly: handle[instance] or handle.Select(instance).");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static BehaviorInstance Sibling(BehaviorInstance owner)
    {
        if (owner == null)
            return null;
        var siblings = owner.Siblings;
        return siblings != null ? siblings.Find(BehaviorTypeId<TBehavior>.Id) : null;
    }

    internal static TBehavior SelectSibling(BehaviorInstance owner)
    {
        var sibling = Sibling(owner);
        if (sibling == null)
            return null;
        var behavior = sibling.Behavior;
        behavior.Selected = sibling;
        return (TBehavior)behavior;
    }
}

/// <summary>Marks <see cref="BehaviorHandle{TBehavior}"/> fields, which are bound when their Behavior is created.</summary>
internal interface IBehaviorHandle { }

/// <summary>What a bound <see cref="BehaviorHandle{TBehavior}"/> shares with its owning Behavior.</summary>
internal sealed class BehaviorLink
{
    // The owning Behavior's cursor, for Instance/Selected.
    public readonly BehaviorCursor Cursor;
    // The target Behavior's shared system, resolved on first use (not at bind time: two Behaviors may hold handles to
    // each other, and creating one's Runtime while the other is being created would recurse).
    public SchematicBehavior System;

    public BehaviorLink(BehaviorCursor cursor) { Cursor = cursor; }
}
