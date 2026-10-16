using System;
using System.Runtime.CompilerServices;

/// <summary>
/// A per-instance Variable of a <see cref="SchematicBehavior"/>. Declare it as a field with a default value:
/// <code>private readonly VariableHandle&lt;float&gt; _speed = 5f;</code>
/// <see cref="Value"/> reads and writes the owning Behavior's <see cref="SchematicBehavior.Selected"/> instance: the one
/// being processed inside a lifecycle method, or the one picked with <c>GetSchematicBehavior</c>.
/// Index it to target a specific instance instead: <c>_speed[instance]</c>.
/// </summary>
/// <remarks>
/// The handle points straight at its value array and at the Behavior's cursor, so a read is: cursor row, then array.
/// For the hottest code, take a reference once and reuse it for the rest of the callback:
/// <code>ref var velocity = ref _velocity.Ref;  velocity.y -= gravity;  velocity *= drag;</code>
/// (Don't hold that reference across a spawn of the same Behavior, which can resize the arrays.)<br/>
/// Declare handles as fields. A handle exposed through a property can still be read, but C# won't let you
/// assign <c>.Value</c> on it; write through <see cref="Ref"/> instead (<c>Ticks.Ref++</c>).
/// </remarks>
public readonly struct VariableHandle<T> : IVariableHandle
{
    private readonly VariableColumn<T> _column;
    private readonly int _index;

    internal VariableHandle(VariableColumn<T> column, int index)
    {
        _column = column;
        _index = index;
    }

    public int Index => _column != null ? _index : -1;
    public bool IsDefined => _column != null;

    /// <summary>
    /// The value for the owning Behavior's selected instance.
    /// </summary>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Ref;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Ref = value;
    }

    /// <summary>
    /// Direct reference to the selected instance's value, for editing struct members in place: <c>_velocity.Ref.y = 0;</c>
    /// </summary>
    public ref T Ref
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var column = _column;
            if (column == null) ThrowUndefined();
            int row = column.Cursor.Row;
            if (row < 0) ThrowNoSelection();
            return ref column.Values[row];
        }
    }

    /// <summary>
    /// The value for a specific instance.
    /// </summary>
    public T this[BehaviorInstance instance]
    {
        get => For(instance);
        set => For(instance) = value;
    }

    /// <summary>
    /// Direct reference to a specific instance's value.
    /// </summary>
    public ref T For(BehaviorInstance instance)
    {
        var column = _column;
        if (column == null) ThrowUndefined();
        if (instance == null || instance.Row < 0)
            throw new InvalidOperationException("The Behavior Instance is not currently spawned.");
        if (instance.Runtime != column.Cursor.Runtime)
            throw new InvalidOperationException($"{instance} belongs to a different Behavior than this Variable.");
        return ref column.Values[instance.Row];
    }

    /// <summary>
    /// This Variable for every row of the Runtime, for Behaviors that want to loop over their data themselves.
    /// Row i belongs to <c>Runtime[i]</c>; check <see cref="BehaviorInstance.IsSpawned"/> if you call this mid-update.
    /// </summary>
    public Span<T> All
    {
        get
        {
            var column = _column;
            if (column == null) ThrowUndefined();
            var runtime = column.Cursor.Runtime;
            return new Span<T>(column.Values, 0, runtime != null ? runtime.Count : 0);
        }
    }

    /// <summary>
    /// The value a Schematic Object (typically a prefab that isn't spawned) is configured to spawn with for this
    /// Variable: its override, then the Metadata default, then the default in code.
    /// </summary>
    /// <param name="target">The GameObject the Behavior is attached to. Null for the first one found.</param>
    public T DefaultFor(Remedy.Schematics.SchematicObject schematicObject, UnityEngine.GameObject target = null)
    {
        var column = _column;
        if (column == null) ThrowUndefined();
        var behavior = column.Cursor.Runtime?.Behavior;
        if (behavior == null || schematicObject == null)
            return default;

        var attachment = schematicObject.FindAttachment(behavior.GetType(), target);
        return BehaviorDefaults.Resolve(this, attachment, behavior);
    }

    public static implicit operator VariableHandle<T>(T defaultValue)
        => SchematicBehavior.DefineVariable(defaultValue);

    public static implicit operator T(VariableHandle<T> variable)
        => variable.Value;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUndefined()
        => throw new InvalidOperationException("This VariableHandle was never defined. Give the field a default value, e.g. `= 0f;`.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoSelection()
        => throw new InvalidOperationException(
            "No instance is selected. VariableHandle.Value works inside lifecycle methods, or after picking an " +
            "object with GetSchematicBehavior. Otherwise use handle[instance].");
}

internal interface IVariableHandle
{
    int Index { get; }
}
