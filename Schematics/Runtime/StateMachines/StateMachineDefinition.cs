using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A hierarchical state machine for a Schematic Object: states (which can hold child states), what each state turns
/// on and off among the object's Behaviors, the transitions out of it (on a Signal, or when conditions hold), and a
/// Schematic Graph of its own that runs while the state is active.<br/>
/// Edited in the State Machine window (as a spreadsheet, or as a graph) and run by the <see cref="StateMachine"/> Behavior.
/// </summary>
[CreateAssetMenu(menuName = "Remedy Toolkit/Schematics/State Machine", fileName = "New State Machine")]
public class StateMachineDefinition : ScriptableObject
{
    [Tooltip("The prefab this State Machine was made for. Used by the editor to list its Behaviors and Signals.")]
    public GameObject Prefab;

    public List<StateDefinition> States = new();

    [Tooltip("The top-level state entered when the object spawns.")]
    public string InitialState;

    // Built on first use: parent and children by index, so the runtime never searches by id.
    [NonSerialized] private int[] _parents;
    [NonSerialized] private int[][] _children;
    [NonSerialized] private int[] _defaultChildren;
    [NonSerialized] private Dictionary<string, int> _indexById;
    [NonSerialized] private Dictionary<string, int> _indexByName;
    [NonSerialized] private int _initial = -1;

    public int Count => States.Count;
    public int InitialIndex { get { Build(); return _initial; } }

    public int ParentOf(int index) { Build(); return index >= 0 && index < _parents.Length ? _parents[index] : -1; }
    public int[] ChildrenOf(int index) { Build(); return index >= 0 && index < _children.Length ? _children[index] : Array.Empty<int>(); }
    public int DefaultChildOf(int index) { Build(); return index >= 0 && index < _defaultChildren.Length ? _defaultChildren[index] : -1; }

    public int IndexOf(string id) { Build(); return id != null && _indexById.TryGetValue(id, out int i) ? i : -1; }
    public int IndexOfName(string name) { Build(); return name != null && _indexByName.TryGetValue(name, out int i) ? i : -1; }

    public StateDefinition Find(string id)
    {
        int index = IndexOf(id);
        return index >= 0 ? States[index] : null;
    }

    /// <summary>The states at the top level (no parent), in order.</summary>
    public IEnumerable<StateDefinition> Roots()
    {
        foreach (var state in States)
            if (string.IsNullOrEmpty(state.Parent) || Find(state.Parent) == null)
                yield return state;
    }

    /// <summary>How deep a state is: 0 for top-level states.</summary>
    public int DepthOf(int index)
    {
        int depth = 0;
        for (int parent = ParentOf(index); parent >= 0 && depth < 64; parent = ParentOf(parent))
            depth++;
        return depth;
    }

    /// <summary>True if <paramref name="ancestor"/> is the state itself or one of its parents.</summary>
    public bool IsSelfOrAncestor(int ancestor, int index)
    {
        for (int i = index; i >= 0; i = ParentOf(i))
            if (i == ancestor) return true;
        return false;
    }

    /// <summary>Call after editing the states, so the runtime lookups are rebuilt.</summary>
    public void MarkChanged() => _parents = null;

    private void OnValidate() => MarkChanged();

    private void Build()
    {
        if (_parents != null && _parents.Length == States.Count)
            return;

        int count = States.Count;
        _indexById = new Dictionary<string, int>(count);
        _indexByName = new Dictionary<string, int>(count);
        for (int i = 0; i < count; i++)
        {
            var state = States[i];
            if (string.IsNullOrEmpty(state.Id))
                state.Id = Guid.NewGuid().ToString("N");
            _indexById[state.Id] = i;
            if (!string.IsNullOrEmpty(state.Name) && !_indexByName.ContainsKey(state.Name))
                _indexByName[state.Name] = i;
        }

        _parents = new int[count];
        var children = new List<int>[count];
        for (int i = 0; i < count; i++)
        {
            int parent = string.IsNullOrEmpty(States[i].Parent) ? -1 : (_indexById.TryGetValue(States[i].Parent, out int p) ? p : -1);
            if (parent == i) parent = -1;
            _parents[i] = parent;
        }
        // Break any accidental cycles by detaching the state that closes them.
        for (int i = 0; i < count; i++)
        {
            int steps = 0;
            for (int p = _parents[i]; p >= 0; p = _parents[p])
                if (++steps > count || p == i) { _parents[i] = -1; break; }
        }
        for (int i = 0; i < count; i++)
            if (_parents[i] >= 0)
                (children[_parents[i]] ??= new List<int>()).Add(i);

        _children = new int[count][];
        _defaultChildren = new int[count];
        for (int i = 0; i < count; i++)
        {
            _children[i] = children[i]?.ToArray() ?? Array.Empty<int>();
            int preferred = string.IsNullOrEmpty(States[i].DefaultChild) ? -1 : (_indexById.TryGetValue(States[i].DefaultChild, out int d) ? d : -1);
            _defaultChildren[i] = preferred >= 0 && _parents[preferred] == i ? preferred : (_children[i].Length > 0 ? _children[i][0] : -1);
        }

        _initial = !string.IsNullOrEmpty(InitialState) && _indexById.TryGetValue(InitialState, out int initial) ? initial : -1;
        if (_initial < 0)
            for (int i = 0; i < count; i++)
                if (_parents[i] < 0) { _initial = i; break; }
    }
}

[Serializable]
public class StateDefinition
{
    [HideInInspector] public string Id = Guid.NewGuid().ToString("N");
    public string Name = "State";
    [Tooltip("The id of the parent state. Empty for a top-level state.")]
    public string Parent = "";
    [Tooltip("The child state entered when this state is entered. Empty for its first child.")]
    public string DefaultChild = "";

    [Tooltip("Runs while this state (or one of its children) is active: On State Enter / Update / Exit, and its Signal nodes.")]
    public SchematicGraph Graph;

    [Tooltip("Behaviors turned on or off while this state is active. Child states inherit unless they say otherwise.")]
    public List<BehaviorToggle> Behaviors = new();

    [Tooltip("Checked in order; the first that fires wins. A parent's transitions apply in all of its children too.")]
    public List<StateTransition> Transitions = new();

    [HideInInspector] public Vector2 Position;
    public Color Color = new Color(0.25f, 0.45f, 0.65f);

    public BehaviorToggle FindToggle(Type behavior, string path)
    {
        foreach (var toggle in Behaviors)
            if (toggle.Behavior.Type == behavior && (toggle.Path ?? "") == (path ?? ""))
                return toggle;
        return null;
    }
}

public enum ToggleMode
{
    Inherit,
    Enable,
    Disable,
}

/// <summary>A Behavior (by type and GameObject path in the prefab) that a state turns on or off.</summary>
[Serializable]
public class BehaviorToggle
{
    public SerializableType Behavior;
    public string Path = "";
    public ToggleMode Mode = ToggleMode.Inherit;
}

public enum TransitionTrigger
{
    [Tooltip("When a Signal reaches this object.")]
    Signal,
    [Tooltip("As soon as every condition holds (checked each frame).")]
    Conditions,
}

[Serializable]
public class StateTransition
{
    [Tooltip("The id of the state to go to.")]
    public string Target = "";
    public TransitionTrigger Trigger = TransitionTrigger.Conditions;
    [Tooltip("For Signal transitions: the Signal that triggers it.")]
    public SignalData Signal;
    [Tooltip("All must hold. For Signal transitions they're checked when the Signal arrives.")]
    public List<StateCondition> Conditions = new();
    [Tooltip("The transition can't fire until the state has been active this long (seconds).")]
    public float MinTimeInState;
}

public enum ConditionKind
{
    [Tooltip("Compares a Behavior Variable.")]
    Variable,
    [Tooltip("Seconds since the state that owns the transition was entered.")]
    TimeInState,
    [Tooltip("Whether a Behavior is enabled.")]
    BehaviorEnabled,
}

public enum CompareOp
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    IsTrue,
    IsFalse,
}

[Serializable]
public class StateCondition
{
    public ConditionKind Kind = ConditionKind.Variable;
    public SerializableType Behavior;
    public string Path = "";
    public string Variable = "";
    public CompareOp Op = CompareOp.Equal;
    public SchematicValue Value = new();
    public float Seconds;
}
