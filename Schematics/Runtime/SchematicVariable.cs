public readonly struct VariableHandle<T>
{
    internal readonly int CachedVariableID;
    
    public VariableHandle(SchematicBehavior behavior)
    {
        CachedVariableID = ExecutionContext.GetCacheID();
    }

    public VariableHandle(SchematicBehavior behavior, T value)
    {
        CachedVariableID = ExecutionContext.GetCacheID();
        ExecutionContext.SetVariable(CachedVariableID, value);
    }

    public T Value
    {
        get => ExecutionContext
            .GetVariable(CachedVariableID)
            .GetValue<T>();

        set => ExecutionContext
            .SetVariable(CachedVariableID, value);
    }

    public static implicit operator T(VariableHandle<T> variable)
        => variable.Value;
}