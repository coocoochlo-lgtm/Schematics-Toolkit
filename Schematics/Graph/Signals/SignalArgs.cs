using Remedy.Framework;
using System;
using UnityEngine;

/// <summary>
/// Converts Signal arguments to and from <see cref="Union"/> where a Signal crosses into or out of a Schematic Graph.
/// Code-to-code Signals pass their values directly and never come through here.
/// </summary>
/// <remarks>
/// Union's constructors store bools, ints and floats inside a Vector4, while <see cref="Union.GetValue{T}"/> reads raw
/// memory. Converting by <see cref="Union.Type"/> keeps both directions consistent with the Union constructors that
/// graph nodes use.
/// </remarks>
public static class SignalArgs
{
    public static Union ToUnion<T>(T value)
    {
        switch (value)
        {
            case null: return Union.Null;
            case bool b: return new Union(b);
            case int i: return new Union(i);
            case float f: return new Union(f);
            case Vector2 v2: return new Union(v2);
            case Vector3 v3: return new Union(v3);
            case Vector4 v4: return new Union(v4);
            case Color c: return new Union(c);
            case Color32 c32: return new Union(c32);
            case Quaternion q: return new Union(q);
            case LayerMask m: return new Union(m);
            case string s: return new Union(s);
            case GameObject g: return new Union(g);
            case Component c: return new Union(c);
            case Union u: return u;
        }

        var union = new Union { Type = Union.ValueType.Object };
        union.SetValue(value);
        return union;
    }

    public static T FromUnion<T>(Union union)
    {
        var type = typeof(T);
        if (type == typeof(Union)) return (T)(object)union;

        switch (union.Type)
        {
            // Stored by the Union constructors as a Vector4.
            case Union.ValueType.Bool:
            case Union.ValueType.Int:
            case Union.ValueType.Float:
            case Union.ValueType.Vector2:
            case Union.ValueType.Vector3:
            case Union.ValueType.Vector4:
            case Union.ValueType.Color:
            case Union.ValueType.Color32:
            case Union.ValueType.Quaternion:
            case Union.ValueType.LayerMask:
                return FromVector(union.GetValue<Vector4>(), type) is T converted ? converted : default;
            case Union.ValueType.Null:
                return default;
        }

        return union.GetValue<T>();
    }

    /// <summary>
    /// The non-generic <see cref="FromUnion{T}"/>, for values whose type is only known at runtime (Behavior Variables
    /// and method parameters reached through graph nodes).
    /// </summary>
    public static object FromUnion(Union union, Type type)
    {
        if (type == null || type == typeof(Union)) return union;

        switch (union.Type)
        {
            case Union.ValueType.Bool:
            case Union.ValueType.Int:
            case Union.ValueType.Float:
            case Union.ValueType.Vector2:
            case Union.ValueType.Vector3:
            case Union.ValueType.Vector4:
            case Union.ValueType.Color:
            case Union.ValueType.Color32:
            case Union.ValueType.Quaternion:
            case Union.ValueType.LayerMask:
                return FromVector(union.GetValue<Vector4>(), type) ?? DefaultOf(type);
            case Union.ValueType.Null:
                return DefaultOf(type);
        }

        var value = union.GetValue<object>();
        if (value != null && type.IsInstanceOfType(value))
            return value;
        // A GameObject where a component is wanted, or the other way around.
        if (value is GameObject gameObject && typeof(Component).IsAssignableFrom(type))
            return gameObject.GetComponent(type);
        if (value is Component component && type == typeof(GameObject))
            return component.gameObject;
        return DefaultOf(type);
    }

    private static object DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static object FromVector(Vector4 v, Type type)
    {
        if (type == typeof(bool)) return v.x != 0f;
        if (type == typeof(int)) return (int)v.x;
        if (type == typeof(float)) return v.x;
        if (type == typeof(Vector2)) return (Vector2)v;
        if (type == typeof(Vector3)) return (Vector3)v;
        if (type == typeof(Vector4)) return v;
        if (type == typeof(Color)) return (Color)v;
        if (type == typeof(Color32)) return (Color32)(Color)v;
        if (type == typeof(Quaternion)) return new Quaternion(v.x, v.y, v.z, v.w);
        if (type == typeof(LayerMask)) return (LayerMask)(int)v.x;
        return null;
    }

    public static Union.ValueType ValueTypeOf(Type type)
    {
        if (type != null && Union.ReverseTypeLookup.TryGetValue(type, out var valueType))
            return valueType;
        if (type != null && typeof(Component).IsAssignableFrom(type))
            return Union.ValueType.Component;
        return Union.ValueType.Object;
    }
}
