using System;

internal interface IVariableHandle
{
    int Index { get; }
}

/// <summary>
/// A per-instance Variable of a <see cref="SchematicBehavior"/>. Declare it as a field with a default value:
/// <code>private readonly VariableHandle&lt;float&gt; _speed = 5f;</code>
/// Inside a lifecycle method, <see cref="Value"/> reads and writes the instance currently being processed.
/// Anywhere else, index it with the instance you want: <c>_speed[instance]</c>.
/// </summary>
/// <remarks>
/// Declare handles as fields. A handle exposed through a property can still be read, but C# won't let you
/// assign <c>.Value</c> on it; write through <see cref="Ref"/> instead (<c>Ticks.Ref++</c>).
/// </remarks>
public readonly struct VariableHandle<T> : IVariableHandle
{
    // Index + 1, so a field left without an initializer (default) reads as "undefined" instead of aliasing Variable 0.
    private readonly int _id;

    internal VariableHandle(int index)
    {
        _id = index + 1;
    }

    public int Index => _id - 1;
    public bool IsDefined => _id != 0;

    /// <summary>
    /// The value for the instance currently running a lifecycle method.
    /// </summary>
    public T Value
    {
        get => Ref;
        set => Ref = value;
    }

    /// <summary>
    /// Direct reference to the current instance's value, for editing struct members in place: <c>_velocity.Ref.y = 0;</c>
    /// </summary>
    public ref T Ref
    {
        get
        {
            var instance = ExecutionContext.CurrentInstance;
            if (instance == null)
                throw new InvalidOperationException(
                    "VariableHandle.Value can only be used inside a Schematic Behavior lifecycle method. " +
                    "Elsewhere, use handle[instance].");
            return ref For(instance);
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
        if (_id == 0)
            throw new InvalidOperationException("This VariableHandle was never defined. Give the field a default value, e.g. `= 0f;`.");
        if (instance == null || instance.Row < 0)
            throw new InvalidOperationException("The Behavior Instance is not currently spawned.");

        return ref ((VariableColumn<T>)instance.Runtime.Columns[_id - 1]).Values[instance.Row];
    }

    public static implicit operator VariableHandle<T>(T defaultValue)
        => SchematicBehavior.DefineVariable(defaultValue);

    public static implicit operator T(VariableHandle<T> variable)
        => variable.Value;
}
