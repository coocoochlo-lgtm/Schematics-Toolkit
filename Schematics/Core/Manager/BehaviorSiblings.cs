using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// A small, dense id per Behavior class (abstract bases included), so lookups by type are an array index instead of a
/// scan with <see cref="Type.IsInstanceOfType"/>. Ids are handed out the first time a type is seen and stay valid for
/// the whole domain, so they are deliberately not reset with the Execution Context (generic code caches them in
/// <see cref="BehaviorTypeId{TBehavior}"/>).
/// </summary>
internal static class BehaviorTypeIds
{
    // -1: not a SchematicBehavior class (an interface, say); lookups for it fall back to a scan.
    private static readonly Dictionary<Type, int> _ids = new();
    private static readonly List<Type> _types = new();

    public static int Count => _types.Count;

    /// <summary>The type an id was given to.</summary>
    public static Type TypeOf(int id) => _types[id];

    /// <summary>The id of a Behavior class, assigning one if needed; -1 for types that aren't Behavior classes.</summary>
    public static int Get(Type type)
    {
        if (type == null)
            return -1;
        if (_ids.TryGetValue(type, out int id))
            return id;

        if (type.IsClass && typeof(SchematicBehavior).IsAssignableFrom(type))
        {
            id = _types.Count;
            _types.Add(type);
        }
        else
            id = -1;

        _ids.Add(type, id);
        return id;
    }
}

/// <summary>The <see cref="BehaviorTypeIds"/> id of <typeparamref name="TBehavior"/>, resolved once per type.</summary>
internal static class BehaviorTypeId<TBehavior> where TBehavior : SchematicBehavior
{
    public static readonly int Id = BehaviorTypeIds.Get(typeof(TBehavior));
}

/// <summary>
/// Every Behavior Instance attached to one GameObject, with a table from Behavior type id to instance, so finding a
/// sibling Behavior is O(1). Each instance keeps a reference to the table of its GameObject
/// (<see cref="BehaviorInstance.Siblings"/>), which <see cref="BehaviorHandle{TBehavior}"/> reads directly.
/// </summary>
/// <remarks>
/// An instance is registered under its own type and every Behavior base class of it (SchematicBehavior included), so a query for a base type finds a
/// derived Behavior. Each slot holds the first instance (in attach order) of that type, spawned or not; reads check
/// <see cref="BehaviorInstance.IsSpawned"/> and, only when that one isn't spawned, scan for another spawned match, so the
/// result is always the same as the old scan (first spawned instance that is of the type).<br/>
/// The table only changes when an instance is attached (<see cref="ExecutionContext.Track"/>) or its object is destroyed
/// (<see cref="ExecutionContext.Untrack"/>); spawning, despawning and pooling never touch it. BehaviorInstances live for
/// their object's whole life (pooling included), so the references stay valid and nothing needs fixing up when rows
/// move inside a Runtime.
/// </remarks>
internal sealed class BehaviorSiblings
{
    public readonly GameObject GameObject;
    public readonly List<BehaviorInstance> Instances = new(2);
    private BehaviorInstance[] _byType = Array.Empty<BehaviorInstance>();

    public BehaviorSiblings(GameObject gameObject)
    {
        GameObject = gameObject;
    }

    public void Add(BehaviorInstance instance)
    {
        Instances.Add(instance);
        Register(instance);
    }

    public void Remove(BehaviorInstance instance)
    {
        if (!Instances.Remove(instance))
            return;
        Array.Clear(_byType, 0, _byType.Length);
        for (int i = 0; i < Instances.Count; i++)
            Register(Instances[i]);
    }

    private void Register(BehaviorInstance instance)
    {
        for (var type = instance.Behavior.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            int id = BehaviorTypeIds.Get(type);
            if (id < 0)
                continue;
            if (id >= _byType.Length)
                Array.Resize(ref _byType, Math.Max(id + 1, Math.Min(_byType.Length * 2, BehaviorTypeIds.Count)));
            // The first attached keeps the slot, matching the order of the scan.
            _byType[id] ??= instance;
        }
    }

    /// <summary>
    /// The spawned instance of the Behavior type with the given id (or of a type derived from it), or null.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BehaviorInstance Find(int typeId)
    {
        var byType = _byType;
        if ((uint)typeId >= (uint)byType.Length)
            return null;
        var instance = byType[typeId];
        if (instance == null || instance.IsSpawned)
            return instance;
        return FindSpawned(BehaviorTypeIds.TypeOf(typeId));
    }

    /// <summary>The first spawned instance whose Behavior is of the given type: the slow path, for any type.</summary>
    public BehaviorInstance FindSpawned(Type behaviorType)
    {
        for (int i = 0; i < Instances.Count; i++)
        {
            var instance = Instances[i];
            if (instance.IsSpawned && behaviorType.IsInstanceOfType(instance.Behavior))
                return instance;
        }
        return null;
    }
}

/// <summary>Compares GameObjects by reference: no native calls, and destroyed objects still find their entry.</summary>
internal sealed class GameObjectReferenceComparer : IEqualityComparer<GameObject>
{
    public static readonly GameObjectReferenceComparer Instance = new();

    public bool Equals(GameObject x, GameObject y) => ReferenceEquals(x, y);
    public int GetHashCode(GameObject obj) => RuntimeHelpers.GetHashCode(obj);
}
