using System;

/// <summary>
/// A Variable declared by a <see cref="SchematicBehavior"/>, as seen by the Runtime.
/// The serialized, Inspector-facing version of this lives in <see cref="BehaviorMetadata.Variables"/>.
/// </summary>
public abstract class BehaviorVariable
{
    public int Index { get; }
    public string Name { get; internal set; }
    public abstract Type ValueType { get; }
    public abstract object BoxedDefault { get; }

    protected BehaviorVariable(int index)
    {
        Index = index;
    }

    internal abstract IVariableColumn Column { get; }
}

public sealed class BehaviorVariable<T> : BehaviorVariable
{
    public T Default { get; }

    public override Type ValueType => typeof(T);
    public override object BoxedDefault => Default;

    internal readonly VariableColumn<T> TypedColumn;
    internal override IVariableColumn Column => TypedColumn;

    internal BehaviorVariable(int index, T defaultValue, BehaviorCursor cursor) : base(index)
    {
        Default = defaultValue;
        TypedColumn = new VariableColumn<T>(defaultValue, BehaviorRuntime.InitialCapacity, cursor);
    }
}
