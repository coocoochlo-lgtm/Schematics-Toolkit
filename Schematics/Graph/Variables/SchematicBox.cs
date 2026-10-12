using System;

/// <summary>
/// Holds a per-object Variable value of any type Unity can serialize (arrays, lists, classes with object references)
/// inside <see cref="SchematicValue"/>. Stored with [SerializeReference], so object references inside it are remapped
/// to each instantiated copy's children like any other serialized field.
/// </summary>
[Serializable]
public abstract class SchematicBox
{
    public abstract object Boxed { get; set; }
    public abstract Type ValueType { get; }

    public static SchematicBox Create(Type type, object value)
    {
        var box = (SchematicBox)Activator.CreateInstance(typeof(SchematicBox<>).MakeGenericType(type));
        box.Boxed = value;
        return box;
    }
}

[Serializable]
public sealed class SchematicBox<T> : SchematicBox
{
    public T Value;

    public override object Boxed
    {
        get => Value;
        set => Value = value is T typed ? typed : default;
    }

    public override Type ValueType => typeof(T);
}
