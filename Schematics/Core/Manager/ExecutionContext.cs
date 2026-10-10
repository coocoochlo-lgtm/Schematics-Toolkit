using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns every <see cref="BehaviorRuntime"/> and every spawned Schematic Object, looked up by UID.
/// When a Schematic Object spawns, the Execution Context finds (or creates) the Runtime for each of its Behaviors
/// and hands the instances over; the <see cref="SchematicsManager"/> pumps the update loops.
/// </summary>
/// <example>
/// <code>
/// ExecutionContext.Pick(uid).Disable();                // every Behavior on the object
/// ExecutionContext.Pick(uid).Enable&lt;Hover&gt;();          // one Behavior
/// </code>
/// </example>
public static class ExecutionContext
{
    private static readonly Dictionary<Type, BehaviorRuntime> _runtimesByType = new();
    private static readonly List<BehaviorRuntime> _runtimes = new();
    private static readonly Dictionary<int, SchematicObject> _objects = new();
    // Every GameObject that has Behaviors attached (roots and children), for GetSchematicBehavior.
    private static readonly Dictionary<GameObject, List<BehaviorInstance>> _instancesByGameObject = new();
    private static int _nextUID = 1;

    /// <summary>
    /// The instance whose lifecycle method is currently running. <see cref="VariableHandle{T}.Value"/> reads from it.
    /// </summary>
    public static BehaviorInstance CurrentInstance { get; internal set; }

    /// <summary>Every Runtime, in update order.</summary>
    public static IReadOnlyList<BehaviorRuntime> Runtimes => _runtimes;

    /// <summary>Every spawned Schematic Object, by UID.</summary>
    public static IReadOnlyDictionary<int, SchematicObject> Objects => _objects;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Needed when Domain Reload is turned off in the Enter Play Mode settings.
        _runtimesByType.Clear();
        _runtimes.Clear();
        _objects.Clear();
        _instancesByGameObject.Clear();
        _nextUID = 1;
        CurrentInstance = null;
    }

    #region Runtimes

    public static BehaviorRuntime GetRuntime<TBehavior>() where TBehavior : SchematicBehavior
        => GetRuntime(typeof(TBehavior));

    /// <summary>
    /// Gets the Runtime for a Behavior type, creating it if this is the first time it is needed.
    /// </summary>
    public static BehaviorRuntime GetRuntime(Type behaviorType)
    {
        if (_runtimesByType.TryGetValue(behaviorType, out var runtime))
            return runtime;
        return CreateRuntime(behaviorType, BehaviorMetadata.Get(behaviorType));
    }

    public static BehaviorRuntime GetRuntime(BehaviorMetadata metadata)
    {
        var behaviorType = metadata != null ? metadata.BehaviorType.Type : null;
        if (behaviorType == null)
            return null;

        if (_runtimesByType.TryGetValue(behaviorType, out var runtime))
            return runtime;
        return CreateRuntime(behaviorType, metadata);
    }

    public static bool TryGetRuntime(Type behaviorType, out BehaviorRuntime runtime)
        => _runtimesByType.TryGetValue(behaviorType, out runtime);

    private static BehaviorRuntime CreateRuntime(Type behaviorType, BehaviorMetadata metadata)
    {
        var runtime = new BehaviorRuntime(SchematicBehavior.Create(behaviorType), metadata);
        _runtimesByType.Add(behaviorType, runtime);

        // Stable insertion keeps creation order between Behaviors with the same ExecutionOrder.
        int index = _runtimes.Count;
        while (index > 0 && _runtimes[index - 1].Behavior.ExecutionOrder > runtime.Behavior.ExecutionOrder)
            index--;
        _runtimes.Insert(index, runtime);

        return runtime;
    }

    #endregion

    #region Instances

    /// <summary>
    /// Spawns a Schematic Object into the Runtimes of its Behaviors and gives it a new UID.
    /// Called by the pool, or by the object itself when it was instantiated directly or placed in a scene.
    /// </summary>
    internal static void Register(SchematicObject schematicObject)
    {
        if (schematicObject.UID != 0)
            return;

        schematicObject.UID = _nextUID++;
        _objects.Add(schematicObject.UID, schematicObject);

        var instances = schematicObject.GetOrCreateBehaviorInstances();

        // Every Behavior gets its data before any callbacks run, so OnStart can read sibling Behaviors' Variables.
        for (int i = 0; i < instances.Count; i++)
            instances[i].Runtime.AddRow(instances[i]);
        for (int i = 0; i < instances.Count; i++)
            instances[i].Runtime.RunCreate(instances[i]);
        for (int i = 0; i < instances.Count; i++)
            instances[i].Runtime.RunStart(instances[i]);
    }

    /// <summary>
    /// Despawns a Schematic Object from its Runtimes. Its UID stops being valid.
    /// </summary>
    internal static void Unregister(SchematicObject schematicObject)
    {
        int uid = schematicObject.UID;
        if (uid == 0)
            return;

        var instances = schematicObject.BehaviorInstances;
        for (int i = instances.Count - 1; i >= 0; i--)
            instances[i].Runtime.Remove(instances[i]);

        _objects.Remove(uid);
        schematicObject.UID = 0;
    }

    /// <summary>
    /// Picks a spawned Schematic Object by UID, to enable, disable or destroy it.
    /// </summary>
    public static SchematicHandle Pick(int uid) => new SchematicHandle(uid);

    /// <summary>
    /// Finds the spawned instance of a Behavior attached to a GameObject, or null if that Behavior doesn't control it.
    /// </summary>
    public static BehaviorInstance FindInstance(GameObject gameObject, Type behaviorType)
    {
        if (gameObject == null || !_instancesByGameObject.TryGetValue(gameObject, out var instances))
            return null;

        for (int i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            if (instance.IsSpawned && behaviorType.IsInstanceOfType(instance.Behavior))
                return instance;
        }
        return null;
    }

    internal static void Track(BehaviorInstance instance)
    {
        if (!_instancesByGameObject.TryGetValue(instance.GameObject, out var instances))
        {
            instances = new List<BehaviorInstance>(2);
            _instancesByGameObject.Add(instance.GameObject, instances);
        }
        instances.Add(instance);
    }

    /// <summary>
    /// Forgets a destroyed Schematic Object's GameObjects.
    /// </summary>
    internal static void Untrack(SchematicObject schematicObject)
    {
        var instances = schematicObject.BehaviorInstances;
        for (int i = 0; i < instances.Count; i++)
        {
            var gameObject = instances[i].GameObject;
            if (!_instancesByGameObject.TryGetValue(gameObject, out var tracked))
                continue;
            tracked.Remove(instances[i]);
            if (tracked.Count == 0)
                _instancesByGameObject.Remove(gameObject);
        }
    }

    public static bool Contains(int uid) => _objects.ContainsKey(uid);

    public static bool TryGetObject(int uid, out SchematicObject schematicObject)
        => _objects.TryGetValue(uid, out schematicObject);

    public static SchematicObject GetObject(int uid)
        => _objects.TryGetValue(uid, out var schematicObject) ? schematicObject : null;

    /// <summary>
    /// Enables or disables Behaviors on the Schematic Object with the given UID.
    /// </summary>
    /// <param name="behaviorType">Only this Behavior. Null for all of them.</param>
    /// <param name="target">Only Behaviors attached to this GameObject. Null for the root and all children.</param>
    /// <returns>False if no spawned object has that UID.</returns>
    public static bool SetEnabled(int uid, bool enabled, Type behaviorType = null, GameObject target = null)
    {
        if (!_objects.TryGetValue(uid, out var schematicObject))
            return false;

        schematicObject.SetBehaviorsEnabled(enabled, behaviorType, target);
        return true;
    }

    public static bool Enable(int uid) => SetEnabled(uid, true);
    public static bool Disable(int uid) => SetEnabled(uid, false);
    public static bool Enable<TBehavior>(int uid) where TBehavior : SchematicBehavior => SetEnabled(uid, true, typeof(TBehavior));
    public static bool Disable<TBehavior>(int uid) where TBehavior : SchematicBehavior => SetEnabled(uid, false, typeof(TBehavior));

    #endregion

    #region Loops

    internal static void Update()
    {
        for (int i = 0; i < _runtimes.Count; i++)
            _runtimes[i].Update();
    }

    internal static void LateUpdate()
    {
        for (int i = 0; i < _runtimes.Count; i++)
            _runtimes[i].LateUpdate();
    }

    internal static void FixedUpdate()
    {
        for (int i = 0; i < _runtimes.Count; i++)
            _runtimes[i].FixedUpdate();
    }

    #endregion
}
