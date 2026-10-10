using System;
using System.Collections.Concurrent;
using UnityEngine;

[Serializable]
public struct SerializableType : ISerializationCallbackReceiver, IEquatable<SerializableType>
{
    // Shared by every instance, so each unique type is only resolved once.
    private static readonly ConcurrentDictionary<string, Type> _typeByName = new();
    private static readonly ConcurrentDictionary<Type, string> _nameByType = new();

    [SerializeField] private string typeName;
    [NonSerialized] private Type cachedType;

    public SerializableType(Type type)
    {
        cachedType = type;
        typeName = type == null ? null : _nameByType.GetOrAdd(type, t => t.AssemblyQualifiedName);
        if (type != null) _typeByName.TryAdd(typeName, type);
    }

    public Type Type
    {
        get
        {
            if (cachedType != null) return cachedType; // fast path: plain field read
            return Resolve(typeName);                  // safety net for default/odd cases
        }
        set => this = new SerializableType(value);
    }

    private static Type Resolve(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_typeByName.TryGetValue(name, out var t)) return t;
        t = Type.GetType(name);
        if (t != null) _typeByName[name] = t;
        return t;
    }

    // Unity calls this after loading, so the cached Type gets filled in.
    public void OnAfterDeserialize() => cachedType = Resolve(typeName);
    public void OnBeforeSerialize() { }

    public static implicit operator Type(SerializableType s) => s.Type;
    public static implicit operator SerializableType(Type type) => new SerializableType(type);

    public override string ToString() => Type?.FullName ?? "null";

    public bool Equals(SerializableType other) => Type == other.Type;

    public override bool Equals(object obj) => obj switch
    {
        SerializableType other => Equals(other),
        Type type => Type == type,
        _ => false
    };

    public override int GetHashCode() => Type?.GetHashCode() ?? 0;

    public static bool operator ==(SerializableType a, SerializableType b) => a.Equals(b);
    public static bool operator !=(SerializableType a, SerializableType b) => !a.Equals(b);
}