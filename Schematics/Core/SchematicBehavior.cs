using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// A Schematic Behavior is closer to a System than a traditional MonoBehaviour. There is only ever one
/// instance of each Behavior type, and its <see cref="BehaviorRuntime"/> holds every Schematic Object
/// (or child object) the Behavior has been attached to through the Inspector.<br/>
/// The Runtime calls the lifecycle methods below once per instance, skipping instances whose Enabled flag is false.
/// </summary>
/// <remarks>
/// Per-instance state lives in <see cref="VariableHandle{T}"/> fields, declared with a default value:
/// <code>
/// private readonly VariableHandle&lt;float&gt; _speed = 5f;
///
/// protected override void OnUpdate(BehaviorInstance instance)
/// {
///     instance.Transform.position += Vector3.forward * _speed.Value * Time.deltaTime;
/// }
/// </code>
/// Components are best fetched with <see cref="BehaviorInstance.GetComponent{T}"/>, which caches them for the object's life;
/// Variables are reset every spawn, so they're the wrong place to cache anything.<br/>
/// Lifecycle order on spawn: <see cref="OnCreate"/> (first time the object is ever created) →
/// <see cref="OnStart"/> → <see cref="OnEnable"/>. On despawn: <see cref="OnDisable"/> → <see cref="OnDestroy"/>.
/// </remarks>
public abstract class SchematicBehavior
{
    // Filled by VariableHandle<T>'s implicit conversion while a Behavior's field initializers run.
    [ThreadStatic] private static List<BehaviorVariable> _pendingVariables;
    // Filled by DeclareSignal while a Behavior's field initializers run.
    [ThreadStatic] private static List<BehaviorSignal> _pendingSignals;

    // The cursor of the Behavior being constructed, handed to the handles its field initializers create.
    [ThreadStatic] private static BehaviorCursor _constructingCursor;

    internal BehaviorCursor Cursor { get; private set; }

    /// <summary>
    /// The instance this Behavior is currently acting on. <see cref="VariableHandle{T}.Value"/> reads and writes it.
    /// The Runtime selects each instance in turn while running lifecycle methods (and restores the previous selection
    /// afterwards); <see cref="SchematicBehaviorExtensions.GetSchematicBehavior{T}(GameObject)"/> selects one from outside.
    /// </summary>
    public BehaviorInstance Selected
    {
        get => Cursor.Instance;
        internal set => Cursor.Select(value);
    }

    private BehaviorVariable[] _variables = Array.Empty<BehaviorVariable>();

    /// <summary>
    /// The Variables this Behavior declared, in declaration order. The index of each matches its <see cref="VariableHandle{T}"/>.
    /// </summary>
    public IReadOnlyList<BehaviorVariable> Variables => _variables;

    private BehaviorSignal[] _signals = Array.Empty<BehaviorSignal>();

    /// <summary>
    /// The Signals this Behavior declared with <see cref="DeclareSignal()"/>, in declaration order.
    /// </summary>
    public IReadOnlyList<BehaviorSignal> Signals => _signals;

    /// <summary>
    /// The Runtime driving this Behavior. Null until the Execution Context creates one for it.
    /// </summary>
    public BehaviorRuntime Runtime { get; internal set; }

    /// <summary>
    /// Runtimes are updated in ascending order of this value.
    /// </summary>
    public virtual int ExecutionOrder => 0;

    /// <summary>Called once, the first time the instance's object is created (not again when reused from the pool).</summary>
    protected internal virtual void OnCreate(BehaviorInstance instance) { }
    /// <summary>Called each time the instance is spawned (fresh or from the pool), after its Variables are reset to their defaults.
    /// If the instance spawns disabled, this is deferred until it is first enabled.</summary>
    protected internal virtual void OnStart(BehaviorInstance instance) { }
    /// <summary>Called whenever the instance becomes enabled, including right after <see cref="OnStart"/>.</summary>
    protected internal virtual void OnEnable(BehaviorInstance instance) { }
    protected internal virtual void OnUpdate(BehaviorInstance instance) { }
    protected internal virtual void OnLateUpdate(BehaviorInstance instance) { }
    protected internal virtual void OnFixedUpdate(BehaviorInstance instance) { }
    /// <summary>Called whenever the instance becomes disabled, including right before it is despawned.</summary>
    protected internal virtual void OnDisable(BehaviorInstance instance) { }
    /// <summary>Called when the instance is despawned. Pooled objects go back to their pool afterwards rather than being destroyed.</summary>
    protected internal virtual void OnDestroy(BehaviorInstance instance) { }

    /// <summary>Unity physics messages from the instance's GameObject, while it is enabled.</summary>
    protected internal virtual void OnCollisionEnter(BehaviorInstance instance, Collision collision) { }
    protected internal virtual void OnCollisionExit(BehaviorInstance instance, Collision collision) { }
    protected internal virtual void OnTriggerEnter(BehaviorInstance instance, Collider other) { }
    protected internal virtual void OnTriggerExit(BehaviorInstance instance, Collider other) { }

    /// <summary>
    /// Creates a Behavior of the given type and collects the Variables its field initializers declared.
    /// </summary>
    public static SchematicBehavior Create(Type behaviorType)
    {
        if (behaviorType == null || behaviorType.IsAbstract || !typeof(SchematicBehavior).IsAssignableFrom(behaviorType))
            throw new ArgumentException($"{behaviorType} is not a concrete {nameof(SchematicBehavior)}.", nameof(behaviorType));

        var pending = _pendingVariables ??= new List<BehaviorVariable>();
        pending.Clear();
        var pendingSignals = _pendingSignals ??= new List<BehaviorSignal>();
        pendingSignals.Clear();

        var cursor = new BehaviorCursor();
        var previousCursor = _constructingCursor;
        _constructingCursor = cursor;

        SchematicBehavior behavior;
        try
        {
            behavior = (SchematicBehavior)Activator.CreateInstance(behaviorType, nonPublic: true);
            behavior._variables = pending.ToArray();
            behavior._signals = pendingSignals.ToArray();
            behavior.Cursor = cursor;
        }
        finally
        {
            pending.Clear();
            pendingSignals.Clear();
            _constructingCursor = previousCursor;
        }

        behavior.ResolveVariableNames();
        return behavior;
    }

    internal static VariableHandle<T> DefineVariable<T>(T defaultValue)
    {
        var pending = _pendingVariables ??= new List<BehaviorVariable>();
        // Handles converted outside a Behavior's construction get a throwaway cursor that is never selected.
        var definition = new BehaviorVariable<T>(pending.Count, defaultValue, _constructingCursor ?? new BehaviorCursor());
        pending.Add(definition);
        return new VariableHandle<T>(definition.TypedColumn, definition.Index);
    }

    /// <summary>
    /// Declares a Signal. Call it from a field initializer:
    /// <code>public readonly SignalHandle&lt;float&gt; Damaged = DeclareSignal&lt;float&gt;();</code>
    /// </summary>
    protected static SignalHandle DeclareSignal() => new SignalHandle(PendSignal(SignalHandle.ParameterTypes));
    protected static SignalHandle<T1> DeclareSignal<T1>() => new SignalHandle<T1>(PendSignal(SignalHandle<T1>.ParameterTypes));
    protected static SignalHandle<T1, T2> DeclareSignal<T1, T2>() => new SignalHandle<T1, T2>(PendSignal(SignalHandle<T1, T2>.ParameterTypes));
    protected static SignalHandle<T1, T2, T3> DeclareSignal<T1, T2, T3>() => new SignalHandle<T1, T2, T3>(PendSignal(SignalHandle<T1, T2, T3>.ParameterTypes));
    protected static SignalHandle<T1, T2, T3, T4> DeclareSignal<T1, T2, T3, T4>() => new SignalHandle<T1, T2, T3, T4>(PendSignal(SignalHandle<T1, T2, T3, T4>.ParameterTypes));

    private static BehaviorSignal PendSignal(Type[] parameterTypes)
    {
        var pending = _pendingSignals ??= new List<BehaviorSignal>();
        var signal = new BehaviorSignal(pending.Count, parameterTypes, _constructingCursor ?? new BehaviorCursor());
        pending.Add(signal);
        return signal;
    }

    /// <summary>
    /// Returns true if this Behavior overrides the given lifecycle method, so Runtimes can skip loops that would do nothing.
    /// </summary>
    internal bool Overrides(string methodName)
    {
        foreach (var method in GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (method.Name == methodName && method.DeclaringType != typeof(SchematicBehavior) && method.GetBaseDefinition().DeclaringType == typeof(SchematicBehavior))
                return true;
        return false;
    }

    // Names come from the fields holding each handle, so they match what the Inspector shows.
    private void ResolveVariableNames()
    {
        for (var type = GetType(); type != null && type != typeof(SchematicBehavior); type = type.BaseType)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (var field in fields)
            {
                if (typeof(ISignalHandle).IsAssignableFrom(field.FieldType))
                {
                    var signal = ((ISignalHandle)field.GetValue(this)).Signal;
                    if (signal != null && signal.Index < _signals.Length && _signals[signal.Index] == signal)
                        signal.Name = CleanFieldName(field.Name);
                    continue;
                }

                if (!field.FieldType.IsGenericType || field.FieldType.GetGenericTypeDefinition() != typeof(VariableHandle<>))
                    continue;

                var handle = (IVariableHandle)field.GetValue(this);
                if (handle.Index < 0 || handle.Index >= _variables.Length)
                    continue;

                _variables[handle.Index].Name = CleanFieldName(field.Name);
            }
        }

        for (int i = 0; i < _variables.Length; i++)
            _variables[i].Name ??= $"Variable {i}";
        for (int i = 0; i < _signals.Length; i++)
            _signals[i].Name ??= $"Signal {i}";
    }

    // Auto-property backing fields look like "<Speed>k__BackingField".
    private static string CleanFieldName(string name)
    {
        if (name.Length > 0 && name[0] == '<')
        {
            int end = name.IndexOf('>');
            if (end > 1)
                return name.Substring(1, end - 1);
        }
        return name;
    }
}
