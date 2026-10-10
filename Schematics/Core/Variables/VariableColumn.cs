using System;

/// <summary>
/// Storage for one Variable across every instance of a Behavior Runtime. Row N belongs to the Runtime's Nth instance.
/// </summary>
internal interface IVariableColumn
{
    Type ValueType { get; }
    void Resize(int capacity);
    void Move(int from, int to);
    void Reset(int row);
    void Clear(int row);
    void Apply(int row, SchematicValue value);
    void SetDefault(SchematicValue value);
    object GetBoxed(int row);
    void SetBoxed(int row, object value);
}

internal sealed class VariableColumn<T> : IVariableColumn
{
    public T[] Values;
    public T Default;
    public readonly BehaviorCursor Cursor;

    public Type ValueType => typeof(T);

    // Arrays, lists and serializable classes are copied per spawn, so instances never share (or edit) one default.
    private static readonly bool _copyDefault = SchematicValue.IsBoxable(typeof(T));

    public VariableColumn(T defaultValue, int capacity, BehaviorCursor cursor)
    {
        Default = defaultValue;
        Values = new T[capacity];
        Cursor = cursor;
    }

    public void Resize(int capacity) => Array.Resize(ref Values, capacity);
    public void Move(int from, int to) => Values[to] = Values[from];
    public void Reset(int row) => Values[row] = _copyDefault ? SchematicCloner.Copy(Default) : Default;
    // Drops references so despawned rows don't keep objects alive.
    public void Clear(int row) => Values[row] = default;
    public void Apply(int row, SchematicValue value) => Values[row] = value.Get<T>();
    public void SetDefault(SchematicValue value) => Default = value.Get<T>();
    public object GetBoxed(int row) => Values[row];
    public void SetBoxed(int row, object value) => Values[row] = value is T typed ? typed : default;
}
