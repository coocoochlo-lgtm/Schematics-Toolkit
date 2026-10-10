using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs a <see cref="StateMachineDefinition"/> on its Schematic Object: enters the initial state, turns the object's
/// Behaviors on and off as states change, fires transitions on Signals or when their conditions hold, and runs each
/// active state's graph (On State Enter / Update / Exit, plus its Signal nodes).<br/>
/// States nest: being in "Grounded" also means being in its parent "Alive", whose transitions (e.g. to "Dead") and
/// Behavior settings apply too, unless the child overrides them.
/// </summary>
[SchematicBehaviorPath("Logic/State Machine")]
public class StateMachine : SchematicBehavior
{
    [Tooltip("Goes to the state with this name (top-level or nested).")]
    [InputSignal, Parameter("State", typeof(string))]
    public readonly SignalHandle<string> SetState;

    [Tooltip("Sent after every state change, with the name of the state now active (the innermost one).")]
    [OutputSignal, Parameter("State", typeof(string))]
    public readonly SignalHandle<string> OnStateChanged;

    public readonly VariableHandle<StateMachineDefinition> Definition = (StateMachineDefinition)null;

    /// <summary>The innermost active state's name.</summary>
    public readonly VariableHandle<string> CurrentState = "";

    private readonly VariableHandle<MachineState> _runtime = (MachineState)null;

    // Runs before the Behaviors it switches, so a state change applies in the same frame.
    public override int ExecutionOrder => -500;

    /// <summary>Per-instance state, created when the object spawns.</summary>
    private sealed class MachineState
    {
        public StateMachineDefinition Definition;
        public int Leaf = -1;
        public bool[] Active;
        public float[] EnteredAt;
        public int Pending = -1;
        public bool Changing;
        public readonly Dictionary<(Type, string), BehaviorInstance> Behaviors = new();
        public readonly Dictionary<StateCondition, (BehaviorInstance instance, int index, Type type)> Variables = new();
        public readonly List<int> Path = new();
    }

    #region Lifecycle

    protected override void OnStart(BehaviorInstance instance)
    {
        SetState.Subscribe(name => GoTo(instance, name));

        var definition = Definition.Value;
        if (definition == null)
            return;

        for (int s = 0; s < definition.Count; s++)
        {
            int stateIndex = s;
            var state = definition.States[s];

            // Signal transitions: heard while their state is active.
            for (int t = 0; t < state.Transitions.Count; t++)
            {
                var transition = state.Transitions[t];
                if (transition.Trigger != TransitionTrigger.Signal || transition.Signal == null)
                    continue;
                transition.Signal.Subscribe(instance, () =>
                {
                    var runtime = _runtime[instance];
                    if (runtime == null || !runtime.Active[stateIndex])
                        return;
                    if (Passes(instance, runtime, stateIndex, transition))
                        ChangeState(instance, runtime, runtime.Definition.IndexOf(transition.Target));
                });
            }

            // The state graph's "On Signal Received" nodes, live while the state is active.
            if (state.Graph == null)
                continue;
            var root = instance.Owner.gameObject;
            foreach (var node in state.Graph.FlowOnInvokeCache)
            {
                if (node == null || node.Signal == null)
                    continue;
                var receiver = node;
                bool invoking = false;
                node.Signal.Subscribe(instance.UID, new GraphSignalListener(args =>
                {
                    var runtime = _runtime[instance];
                    if (invoking || runtime == null || !runtime.Active[stateIndex] || !instance.Enabled)
                        return;
                    try { invoking = true; receiver.Trigger(root, args); }
                    finally { invoking = false; }
                }));
            }
        }
    }

    protected override void OnEnable(BehaviorInstance instance)
    {
        var definition = Definition.Value;
        if (definition == null || definition.Count == 0)
            return;

        var runtime = new MachineState
        {
            Definition = definition,
            Active = new bool[definition.Count],
            EnteredAt = new float[definition.Count],
        };
        _runtime.Value = runtime;
        ChangeState(instance, runtime, definition.InitialIndex);
    }

    protected override void OnUpdate(BehaviorInstance instance)
    {
        var runtime = _runtime.Value;
        if (runtime == null || runtime.Leaf < 0)
            return;

        var definition = runtime.Definition;
        BuildPath(definition, runtime.Leaf, runtime.Path);
        var root = instance.Owner.gameObject;

        // Outermost first: a parent's transition (e.g. Alive -> Dead) beats its children's.
        for (int p = runtime.Path.Count - 1; p >= 0; p--)
        {
            int stateIndex = runtime.Path[p];
            var transitions = definition.States[stateIndex].Transitions;
            for (int t = 0; t < transitions.Count; t++)
            {
                var transition = transitions[t];
                if (transition.Trigger != TransitionTrigger.Conditions || !Passes(instance, runtime, stateIndex, transition))
                    continue;
                ChangeState(instance, runtime, definition.IndexOf(transition.Target));
                return;
            }
        }

        for (int p = runtime.Path.Count - 1; p >= 0; p--)
            definition.States[runtime.Path[p]].Graph?.TriggerEvent<OnStateUpdate>(root);
    }

    protected override void OnDisable(BehaviorInstance instance)
    {
        var runtime = _runtime.Value;
        if (runtime == null || runtime.Leaf < 0)
            return;

        // Leave every active state, innermost first.
        var root = instance.Owner.gameObject;
        BuildPath(runtime.Definition, runtime.Leaf, runtime.Path);
        foreach (int stateIndex in runtime.Path)
        {
            runtime.Active[stateIndex] = false;
            runtime.Definition.States[stateIndex].Graph?.TriggerEvent<OnStateExit>(root);
        }
        runtime.Leaf = -1;
        _runtime.Value = null;
    }

    #endregion

    #region API

    /// <summary>Goes to the named state on the selected object. False if there's no such state.</summary>
    public bool GoTo(string stateName) => GoTo(Selected, stateName);

    public bool GoTo(BehaviorInstance instance, string stateName)
    {
        var runtime = _runtime[instance];
        if (runtime == null)
            return false;
        int index = runtime.Definition.IndexOfName(stateName);
        if (index < 0)
        {
            Debug.LogWarning($"State Machine on {instance.GameObject.name} has no state named '{stateName}'.", instance.GameObject);
            return false;
        }
        ChangeState(instance, runtime, index);
        return true;
    }

    /// <summary>True if the named state is active on the selected object (itself or as the parent of the active one).</summary>
    public bool IsInState(string stateName) => IsInState(Selected, stateName);

    public bool IsInState(BehaviorInstance instance, string stateName)
    {
        var runtime = _runtime[instance];
        int index = runtime != null ? runtime.Definition.IndexOfName(stateName) : -1;
        return index >= 0 && runtime.Active[index];
    }

    #endregion

    #region Transitions

    private void ChangeState(BehaviorInstance instance, MachineState runtime, int target)
    {
        var definition = runtime.Definition;
        if (target < 0 || target >= definition.Count)
            return;

        // A state change from inside an enter/exit graph finishes the current one first.
        if (runtime.Changing)
        {
            runtime.Pending = target;
            return;
        }

        runtime.Changing = true;
        try
        {
            int guard = 0;
            while (target >= 0 && guard++ < 16)
            {
                Switch(instance, runtime, target);
                target = runtime.Pending;
                runtime.Pending = -1;
            }
        }
        finally
        {
            runtime.Changing = false;
        }
    }

    private void Switch(BehaviorInstance instance, MachineState runtime, int target)
    {
        var definition = runtime.Definition;
        var root = instance.Owner.gameObject;

        // Entering a parent enters its default child, all the way down.
        int newLeaf = target;
        for (int child = definition.DefaultChildOf(newLeaf), depth = 0; child >= 0 && depth < 64; child = definition.DefaultChildOf(child), depth++)
            newLeaf = child;

        // States both paths share stay active, unless the target itself is one of them: then it's re-entered.
        int keep = -1;
        if (runtime.Leaf >= 0)
        {
            keep = definition.IsSelfOrAncestor(target, runtime.Leaf) ? definition.ParentOf(target) : CommonAncestor(definition, runtime.Leaf, newLeaf);
        }

        // Exit, innermost first.
        if (runtime.Leaf >= 0)
            for (int s = runtime.Leaf; s >= 0 && s != keep; s = definition.ParentOf(s))
            {
                runtime.Active[s] = false;
                definition.States[s].Graph?.TriggerEvent<OnStateExit>(root);
            }

        // Enter, outermost first.
        var entering = runtime.Path;
        entering.Clear();
        for (int s = newLeaf; s >= 0 && s != keep; s = definition.ParentOf(s))
            entering.Add(s);
        runtime.Leaf = newLeaf;
        float now = Time.time;
        for (int i = entering.Count - 1; i >= 0; i--)
        {
            int s = entering[i];
            runtime.Active[s] = true;
            runtime.EnteredAt[s] = now;
        }

        ApplyBehaviorToggles(instance, runtime);

        for (int i = entering.Count - 1; i >= 0; i--)
            definition.States[entering[i]].Graph?.TriggerEvent<OnStateEnter>(root);

        string name = definition.States[newLeaf].Name;
        CurrentState[instance] = name;
        OnStateChanged.Invoke(instance, name);
    }

    private bool Passes(BehaviorInstance instance, MachineState runtime, int stateIndex, StateTransition transition)
    {
        if (string.IsNullOrEmpty(transition.Target))
            return false;
        float timeInState = Time.time - runtime.EnteredAt[stateIndex];
        if (timeInState < transition.MinTimeInState)
            return false;

        foreach (var condition in transition.Conditions)
            if (!Holds(instance, runtime, condition, timeInState))
                return false;

        // A conditional transition with nothing to check would fire every frame.
        return transition.Trigger == TransitionTrigger.Signal || transition.Conditions.Count > 0 || transition.MinTimeInState > 0f;
    }

    private bool Holds(BehaviorInstance instance, MachineState runtime, StateCondition condition, float timeInState)
    {
        switch (condition.Kind)
        {
            case ConditionKind.TimeInState:
                return Compare(timeInState, condition.Seconds, condition.Op);

            case ConditionKind.BehaviorEnabled:
            {
                var target = ResolveBehavior(instance, runtime, condition.Behavior.Type, condition.Path);
                bool enabled = target != null && target.Enabled;
                return condition.Op == CompareOp.IsFalse || condition.Op == CompareOp.NotEqual ? !enabled : enabled;
            }

            default:
            {
                if (!runtime.Variables.TryGetValue(condition, out var resolved) || resolved.instance == null || !resolved.instance.IsSpawned)
                {
                    var target = ResolveBehavior(instance, runtime, condition.Behavior.Type, condition.Path);
                    int index = target != null ? target.Runtime.IndexOfVariable(condition.Variable) : -1;
                    var type = BehaviorReflection.Get(condition.Behavior.Type)?.FindVariableType(condition.Variable);
                    resolved = (index >= 0 ? target : null, index, type);
                    runtime.Variables[condition] = resolved;
                }
                if (resolved.instance == null)
                    return false;

                var value = resolved.instance.Runtime.GetVariableBoxed(resolved.instance, resolved.index);
                return CompareValues(value, condition, resolved.type);
            }
        }
    }

    private static bool CompareValues(object value, StateCondition condition, Type type)
    {
        switch (condition.Op)
        {
            case CompareOp.IsTrue: return value is bool b && b || (value is UnityEngine.Object o ? o != null : value != null && !(value is bool));
            case CompareOp.IsFalse: return !(value is bool b2 && b2 || (value is UnityEngine.Object o2 ? o2 != null : value != null && !(value is bool)));
        }

        object expected = type != null && SchematicValue.IsDirect(type) ? condition.Value.Get(type) : null;
        if (TryNumber(value, out double a) && TryNumber(expected, out double b3))
            return Compare(a, b3, condition.Op);

        bool equal = value is UnityEngine.Object uo || expected is UnityEngine.Object
            ? (value as UnityEngine.Object) == (expected as UnityEngine.Object)
            : Equals(value, expected);
        return condition.Op == CompareOp.NotEqual ? !equal : condition.Op == CompareOp.Equal && equal;
    }

    private static bool TryNumber(object value, out double number)
    {
        switch (value)
        {
            case int i: number = i; return true;
            case float f: number = f; return true;
            case double d: number = d; return true;
            case long l: number = l; return true;
            case Enum e: number = Convert.ToInt64(e); return true;
            default: number = 0; return false;
        }
    }

    private static bool Compare(double a, double b, CompareOp op) => op switch
    {
        CompareOp.Equal => Math.Abs(a - b) < 1e-6,
        CompareOp.NotEqual => Math.Abs(a - b) >= 1e-6,
        CompareOp.Less => a < b,
        CompareOp.LessOrEqual => a <= b,
        CompareOp.Greater => a > b,
        CompareOp.GreaterOrEqual => a >= b,
        CompareOp.IsTrue => a != 0,
        CompareOp.IsFalse => a == 0,
        _ => false,
    };

    #endregion

    #region Behavior toggles

    // Every Behavior any state mentions is set from the innermost active state that says Enable or Disable; one that
    // no active state mentions is left as it is.
    private void ApplyBehaviorToggles(BehaviorInstance instance, MachineState runtime)
    {
        var definition = runtime.Definition;
        var decided = new Dictionary<(Type, string), bool>();

        for (int s = runtime.Leaf; s >= 0; s = definition.ParentOf(s))
            foreach (var toggle in definition.States[s].Behaviors)
            {
                if (toggle.Mode == ToggleMode.Inherit || toggle.Behavior.Type == null)
                    continue;
                var key = (toggle.Behavior.Type, toggle.Path ?? "");
                if (!decided.ContainsKey(key))
                    decided[key] = toggle.Mode == ToggleMode.Enable;
            }

        foreach (var entry in decided)
        {
            if (entry.Key.Item1 == typeof(StateMachine))
                continue;
            var target = ResolveBehavior(instance, runtime, entry.Key.Item1, entry.Key.Item2);
            if (target != null && target.Enabled != entry.Value)
                target.SetEnabled(entry.Value);
        }
    }

    private static BehaviorInstance ResolveBehavior(BehaviorInstance instance, MachineState runtime, Type type, string path)
    {
        if (type == null)
            return null;
        var key = (type, path ?? "");
        if (runtime.Behaviors.TryGetValue(key, out var cached) && cached != null && cached.IsSpawned)
            return cached;

        var rootTransform = instance.Owner.transform;
        var target = string.IsNullOrEmpty(path) ? rootTransform : rootTransform.Find(path);
        var found = target != null ? ExecutionContext.FindInstance(target.gameObject, type) : null;
        runtime.Behaviors[key] = found;
        return found;
    }

    #endregion

    private static void BuildPath(StateMachineDefinition definition, int leaf, List<int> path)
    {
        path.Clear();
        for (int s = leaf; s >= 0 && path.Count < 64; s = definition.ParentOf(s))
            path.Add(s);
    }

    private static int CommonAncestor(StateMachineDefinition definition, int a, int b)
    {
        for (int x = a; x >= 0; x = definition.ParentOf(x))
            if (definition.IsSelfOrAncestor(x, b))
                return x;
        return -1;
    }
}
