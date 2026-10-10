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

    /// <summary>
    /// Selects one of this Behavior's instances, so its Variables and methods act on it. Returns this Behavior.
    /// </summary>
    public SchematicBehavior Select(BehaviorInstance instance)
    {
        if (instance != null && instance.Behavior != this)
            throw new ArgumentException($"{instance} belongs to a different Behavior.", nameof(instance));
        Selected = instance;
        return this;
    }

    private BehaviorVariable[] _variables = Array.Empty<BehaviorVariable>();

    /// <summary>
    /// The Variables this Behavior declared, in declaration order. The index of each matches its <see cref="VariableHandle{T}"/>.
    /// </summary>
    public IReadOnlyList<BehaviorVariable> Variables => _variables;

    private BehaviorSignal[] _signals = Array.Empty<BehaviorSignal>();

    /// <summary>
    /// The Signals this Behavior has: one per <c>SignalHandle</c> field, in declaration order. The fields need no
    /// initializer; they're bound when the Behavior is created.
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

    /// <summary>
    /// Called in the editor when this Behavior is attached to a GameObject through the Inspector, the counterpart to a
    /// MonoBehaviour's Reset. Create child objects and set this object's Variable values through the context.
    /// </summary>
    protected internal virtual void OnSetup(BehaviorSetupContext context) { }

    /// <summary>
    /// Runs a Behavior type's <see cref="OnSetup"/> for a newly attached object. Called by the editor.
    /// </summary>
    public static void RunSetup(Type behaviorType, GameObject target, Remedy.Schematics.BehaviorAttachment attachment)
    {
        var behavior = Create(behaviorType);
        if (behavior.Overrides(nameof(OnSetup)))
            behavior.OnSetup(new BehaviorSetupContext(target, attachment, behavior));
    }

    /// <summary>
    /// Draws Gizmos for one object this Behavior is attached to, in Edit Mode as well as Play Mode.
    /// Read Variables through <see cref="BehaviorGizmoContext.Get{T}"/>.
    /// </summary>
    protected internal virtual void OnDrawGizmos(BehaviorGizmoContext context) { }

    /// <summary>Unity physics messages from the instance's GameObject, while it is enabled.</summary>
    protected internal virtual void OnCollisionEnter(BehaviorInstance instance, Collision collision) { }
    protected internal virtual void OnCollisionExit(BehaviorInstance instance, Collision collision) { }
    protected internal virtual void OnTriggerEnter(BehaviorInstance instance, Collider other) { }
    protected internal virtual void OnTriggerExit(BehaviorInstance instance, Collider other) { }
    protected internal virtual void OnCollisionStay(BehaviorInstance instance, Collision collision) { }
    protected internal virtual void OnTriggerStay(BehaviorInstance instance, Collider other) { }

    /// <summary>
    /// Creates a Behavior of the given type and collects the Variables its field initializers declared.
    /// </summary>
    public static SchematicBehavior Create(Type behaviorType)
    {
        if (behaviorType == null || behaviorType.IsAbstract || !typeof(SchematicBehavior).IsAssignableFrom(behaviorType))
            throw new ArgumentException($"{behaviorType} is not a concrete {nameof(SchematicBehavior)}.", nameof(behaviorType));

        var pending = _pendingVariables ??= new List<BehaviorVariable>();
        pending.Clear();

        var cursor = new BehaviorCursor();
        var previousCursor = _constructingCursor;
        _constructingCursor = cursor;

        SchematicBehavior behavior;
        try
        {
            behavior = (SchematicBehavior)Activator.CreateInstance(behaviorType, nonPublic: true);
            behavior._variables = pending.ToArray();
            behavior.Cursor = cursor;
        }
        finally
        {
            pending.Clear();
            _constructingCursor = previousCursor;
        }

        behavior.ResolveVariableNames();
        behavior.BindSignals();
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

    // Fills every SignalHandle field with its Signal. Runs once per Behavior type, when its Runtime is created.
    private void BindSignals()
    {
        var signals = new List<BehaviorSignal>();
        var types = new List<Type>();
        for (var type = GetType(); type != null && type != typeof(SchematicBehavior); type = type.BaseType)
            types.Insert(0, type); // base class fields first

        foreach (var type in types)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!typeof(ISignalHandle).IsAssignableFrom(field.FieldType))
                    continue;

                var parameterTypes = field.FieldType.IsGenericType ? field.FieldType.GetGenericArguments() : Type.EmptyTypes;
                var signal = new BehaviorSignal(signals.Count, parameterTypes, Cursor) { Name = CleanFieldName(field.Name) };
                var handle = Activator.CreateInstance(field.FieldType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { signal }, null);
                field.SetValue(this, handle);
                signals.Add(signal);
            }
        }

        _signals = signals.ToArray();
    }

    /// <summary>
    /// The component types a Behavior asks for with <c>[RequireComponent]</c>. Unity only enforces that attribute on
    /// MonoBehaviours, so the toolkit adds them itself when a Behavior is attached.
    /// </summary>
    public static IEnumerable<Type> GetRequiredComponents(Type behaviorType)
    {
        foreach (RequireComponent attribute in behaviorType.GetCustomAttributes(typeof(RequireComponent), true))
        {
            if (attribute.m_Type0 != null) yield return attribute.m_Type0;
            if (attribute.m_Type1 != null) yield return attribute.m_Type1;
            if (attribute.m_Type2 != null) yield return attribute.m_Type2;
        }
    }

    /// <summary>
    /// The Behaviors a Behavior asks for with <see cref="RequireBehaviorAttribute"/>, including the ones those ask for,
    /// dependencies first.
    /// </summary>
    public static List<Type> GetRequiredBehaviors(Type behaviorType)
    {
        var result = new List<Type>();
        Collect(behaviorType, new HashSet<Type> { behaviorType });
        return result;

        void Collect(Type type, HashSet<Type> visiting)
        {
            foreach (RequireBehaviorAttribute attribute in type.GetCustomAttributes(typeof(RequireBehaviorAttribute), true))
                foreach (var required in attribute.Behaviors)
                {
                    if (required == null || !typeof(SchematicBehavior).IsAssignableFrom(required) || required.IsAbstract)
                        continue;
                    if (!visiting.Add(required) || result.Contains(required))
                        continue;
                    Collect(required, visiting);
                    result.Add(required);
                }
        }
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
    }

    // Auto-property backing fields look like "<Speed>k__BackingField".
    internal static string CleanFieldName(string name)
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
