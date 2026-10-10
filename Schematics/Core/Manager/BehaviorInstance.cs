using System.Collections.Generic;
using Remedy.Schematics;
using UnityEngine;

/// <summary>
/// One GameObject's membership in a <see cref="BehaviorRuntime"/>: what a Behavior's lifecycle methods receive.
/// Created once per Schematic Object and reused every time it is pulled from the pool.
/// </summary>
public sealed class BehaviorInstance
{
    /// <summary>The Schematic Object this instance belongs to.</summary>
    public SchematicObject Owner { get; }
    /// <summary>The GameObject the Behavior was attached to: the Schematic Object's root or one of its children.</summary>
    public GameObject GameObject { get; }
    public Transform Transform { get; }
    public BehaviorRuntime Runtime { get; }
    public SchematicBehavior Behavior => Runtime.Behavior;

    /// <summary>The UID of the owning Schematic Object. Changes every time it is spawned.</summary>
    public int UID => Owner.UID;

    /// <summary>
    /// While false, the Runtime skips every lifecycle method for this instance.
    /// Use <see cref="SetEnabled"/> to change it, so OnEnable/OnDisable are called.
    /// </summary>
    public bool Enabled { get; internal set; }

    /// <summary>True while the instance is spawned and being processed by its Runtime.</summary>
    public bool IsSpawned => Row >= 0 && !Removing;

    /// <summary>The Inspector configuration this instance was created from.</summary>
    public BehaviorAttachment Attachment { get; }

    // Index into the Runtime's instance list and Variable columns. -1 while despawned.
    internal int Row = -1;
    internal bool Created;
    internal bool Started;
    internal bool Removing;

    // Components looked up through GetComponent, kept for the object's whole life (pooling included).
    private Component[] _componentCache = System.Array.Empty<Component>();

    // Signal subscriptions this instance made, released when it despawns.
    private readonly List<SignalSubscription> _subscriptions = new();

    // Attachment overrides resolved to column indices the first time the instance spawns.
    private int[] _overrideColumns;
    private SchematicValue[] _overrideValues;

    internal BehaviorInstance(SchematicObject owner, GameObject gameObject, BehaviorRuntime runtime, BehaviorAttachment attachment)
    {
        Owner = owner;
        GameObject = gameObject;
        Transform = gameObject.transform;
        Runtime = runtime;
        Attachment = attachment;
    }

    public void Enable() => Runtime.SetEnabled(this, true);
    public void Disable() => Runtime.SetEnabled(this, false);
    public void SetEnabled(bool enabled) => Runtime.SetEnabled(this, enabled);

    /// <summary>
    /// Reference to this instance's value of a Variable.
    /// </summary>
    public ref T Get<T>(VariableHandle<T> variable) => ref variable.For(this);

    /// <summary>
    /// Gets a component on this instance's GameObject, cached for the object's whole life.
    /// Use this instead of storing components in Variables, which are reset every spawn.
    /// </summary>
    public T GetComponent<T>() where T : Component
    {
        for (int i = 0; i < _componentCache.Length; i++)
            if (_componentCache[i] is T cached)
                return cached;

        if (!GameObject.TryGetComponent(out T component))
            return null;

        System.Array.Resize(ref _componentCache, _componentCache.Length + 1);
        _componentCache[_componentCache.Length - 1] = component;
        return component;
    }

    /// <summary>
    /// Gets another Behavior on this instance's GameObject, with this object selected. Null if it isn't attached here.
    /// </summary>
    public TBehavior GetSchematicBehavior<TBehavior>() where TBehavior : SchematicBehavior
        => GameObject.GetSchematicBehavior<TBehavior>();

    internal void TrackSubscription(SignalSubscription subscription) => _subscriptions.Add(subscription);

    internal void ReleaseSubscriptions()
    {
        for (int i = 0; i < _subscriptions.Count; i++)
            _subscriptions[i].Dispose();
        _subscriptions.Clear();
    }

    internal void ApplyOverrides(IVariableColumn[] columns)
    {
        if (_overrideColumns == null)
            ResolveOverrides(columns);

        for (int i = 0; i < _overrideColumns.Length; i++)
            columns[_overrideColumns[i]].Apply(Row, _overrideValues[i]);
    }

    private void ResolveOverrides(IVariableColumn[] columns)
    {
        var count = Attachment?.Overrides?.Count ?? 0;
        var indices = new System.Collections.Generic.List<int>(count);
        var values = new System.Collections.Generic.List<SchematicValue>(count);

        for (int i = 0; i < count; i++)
        {
            var entry = Attachment.Overrides[i];
            int column = Runtime.IndexOfVariable(entry.Name);
            if (column < 0 || entry.Value == null)
                continue;
            indices.Add(column);
            values.Add(entry.Value);
        }

        _overrideColumns = indices.ToArray();
        _overrideValues = values.ToArray();
    }

    public override string ToString() => $"{Behavior.GetType().Name} on {(GameObject != null ? GameObject.name : "<destroyed>")} (UID {UID})";
}
