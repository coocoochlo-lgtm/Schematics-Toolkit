using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

/// <summary>
/// Deep-copies serializable data (arrays, lists, [Serializable] classes and structs) so each spawned instance gets its
/// own copy of a per-object default. Unity objects are shared by reference, as serialization would. Spawn-time only.
/// </summary>
public static class SchematicCloner
{
    public static T Copy<T>(T value) => (T)Copy((object)value, new Dictionary<object, object>(ReferenceComparer.Instance));

    public static object Copy(object value) => Copy(value, new Dictionary<object, object>(ReferenceComparer.Instance));

    private static object Copy(object value, Dictionary<object, object> copies)
    {
        if (value == null)
            return null;

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || value is UnityEngine.Object || value is Delegate || value is Type)
            return value;

        if (!type.IsValueType && copies.TryGetValue(value, out var existing))
            return existing;

        if (value is Array array)
        {
            var copy = Array.CreateInstance(type.GetElementType(), array.Length);
            copies[value] = copy;
            for (int i = 0; i < array.Length; i++)
                copy.SetValue(Copy(array.GetValue(i), copies), i);
            return copy;
        }

        if (value is IList list && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var copy = (IList)Activator.CreateInstance(type, list.Count);
            copies[value] = copy;
            foreach (var item in list)
                copy.Add(Copy(item, copies));
            return copy;
        }

        // [Serializable] classes and structs: copy every instance field.
        var clone = type.IsValueType ? value : FormatterServices.GetUninitializedObject(type);
        if (!type.IsValueType)
            copies[value] = clone;

        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                field.SetValue(clone, Copy(field.GetValue(value), copies));
        }
        return clone;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
