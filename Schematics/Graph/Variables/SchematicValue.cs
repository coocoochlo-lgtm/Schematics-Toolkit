using System;
using UnityEngine;

/// <summary>
/// An Inspector-editable value for a Behavior Variable default. Kept separate from <see cref="Remedy.Framework.Union"/>
/// because each kind of data gets its own serialized field, so Unity can save object references safely.
/// Common types are stored directly; anything else Unity can serialize (arrays, lists, classes with object
/// references) is stored in a <see cref="SchematicBox"/>. Only used when spawning instances, never per frame.
/// </summary>
[Serializable]
public class SchematicValue
{
    [SerializeField] private long _integer;
    [SerializeField] private Vector4 _vector;
    [SerializeField] private string _string;
    [SerializeField] private UnityEngine.Object _object;
    // Everything else Unity can serialize.
    [SerializeReference] private SchematicBox _boxed;

    public SchematicValue() { }

    public SchematicValue(object value, Type type = null)
    {
        Set(value, type);
    }

    /// <summary>The serialized path of the boxed value, for drawing it with a PropertyField.</summary>
    public const string BoxedValuePath = nameof(_boxed) + ".Value";

    public static bool IsSupported(Type type) => IsDirect(type) || IsBoxable(type);

    /// <summary>Types stored in their own field and drawn by the toolkit's value fields.</summary>
    public static bool IsDirect(Type type)
    {
        if (type == null) return false;
        return type == typeof(bool) || type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double)
            || type == typeof(string) || type.IsEnum
            || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)
            || type == typeof(Vector2Int) || type == typeof(Vector3Int)
            || type == typeof(Color) || type == typeof(Quaternion)
            || type == typeof(LayerMask)
            || typeof(UnityEngine.Object).IsAssignableFrom(type);
    }

    /// <summary>
    /// Anything else Unity serializes: arrays and lists of serializable elements, and [Serializable] classes/structs.
    /// Stored in a <see cref="SchematicBox"/> and drawn with Unity's own property drawers.
    /// </summary>
    public static bool IsBoxable(Type type)
    {
        if (type == null || IsDirect(type) || type.IsAbstract || type.IsInterface || typeof(Delegate).IsAssignableFrom(type))
            return false;
        if (type.IsArray)
            return type.GetArrayRank() == 1 && IsElementSerializable(type.GetElementType());
        if (type.IsGenericType)
            return type.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>) && IsElementSerializable(type.GetGenericArguments()[0]);
        return type.IsSerializable && !type.IsPrimitive;
    }

    private static bool IsElementSerializable(Type element)
        => IsDirect(element) || (!element.IsArray && !element.IsGenericType && IsBoxable(element));

    public void Set(object value, Type type = null)
    {
        _integer = 0;
        _vector = default;
        _string = null;
        _object = null;
        _boxed = null;
        type ??= value?.GetType();

        switch (value)
        {
            case null: break;
            case bool b: _integer = b ? 1 : 0; break;
            case int i: _integer = i; break;
            case long l: _integer = l; break;
            case float f: _vector.x = f; break;
            case double d: _vector.x = (float)d; break;
            case string s: _string = s; break;
            case Enum e: _integer = Convert.ToInt64(e); break;
            case Vector2 v2: _vector = v2; break;
            case Vector3 v3: _vector = v3; break;
            case Vector4 v4: _vector = v4; break;
            case Vector2Int v2i: _vector = new Vector4(v2i.x, v2i.y); break;
            case Vector3Int v3i: _vector = new Vector4(v3i.x, v3i.y, v3i.z); break;
            case Color c: _vector = c; break;
            case Quaternion q: _vector = new Vector4(q.x, q.y, q.z, q.w); break;
            case LayerMask m: _integer = m.value; break;
            case UnityEngine.Object o: _object = o; break;
            default:
                if (IsBoxable(type))
                    _boxed = SchematicBox.Create(type, value);
                break;
        }
    }

    public object Get(Type type)
    {
        if (type == null) return null;
        if (type == typeof(bool)) return _integer != 0;
        if (type == typeof(int)) return (int)_integer;
        if (type == typeof(long)) return _integer;
        if (type == typeof(float)) return _vector.x;
        if (type == typeof(double)) return (double)_vector.x;
        if (type == typeof(string)) return _string;
        if (type.IsEnum) return Enum.ToObject(type, _integer);
        if (type == typeof(Vector2)) return (Vector2)_vector;
        if (type == typeof(Vector3)) return (Vector3)_vector;
        if (type == typeof(Vector4)) return _vector;
        if (type == typeof(Vector2Int)) return new Vector2Int(Mathf.RoundToInt(_vector.x), Mathf.RoundToInt(_vector.y));
        if (type == typeof(Vector3Int)) return new Vector3Int(Mathf.RoundToInt(_vector.x), Mathf.RoundToInt(_vector.y), Mathf.RoundToInt(_vector.z));
        if (type == typeof(Color)) return (Color)_vector;
        if (type == typeof(Quaternion)) return new Quaternion(_vector.x, _vector.y, _vector.z, _vector.w);
        if (type == typeof(LayerMask)) return (LayerMask)(int)_integer;
        if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return _object != null && type.IsInstanceOfType(_object) ? _object : null;
        if (IsBoxable(type))
        {
            // A copy, so each spawn starts from the saved value without sharing or changing it.
            if (_boxed != null && _boxed.ValueType == type)
                return SchematicCloner.Copy(_boxed.Boxed);
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    public T Get<T>() => Get(typeof(T)) is T value ? value : default;

    public SchematicValue Clone()
    {
        return new SchematicValue
        {
            _integer = _integer,
            _vector = _vector,
            _string = _string,
            _object = _object,
            _boxed = _boxed != null ? SchematicBox.Create(_boxed.ValueType, SchematicCloner.Copy(_boxed.Boxed)) : null,
        };
    }
}
