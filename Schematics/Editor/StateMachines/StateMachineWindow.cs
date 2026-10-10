using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Edits a <see cref="StateMachineDefinition"/> two ways: the State Manager (a spreadsheet of states against the
/// prefab's Behaviors, showing what each state turns on or off) and the State Graph (states and their transitions,
/// one level of the hierarchy at a time; double-click a state to go inside it). Both share the panel below, where the
/// selected state's transitions read like an event sheet: "when [Signal] / if [conditions] → go to [state]".
/// </summary>
public class StateMachineWindow : EditorWindow
{
    [SerializeField] private StateMachineDefinition _definition;
    [SerializeField] private string _selected;
    [SerializeField] private bool _graphMode;
    [SerializeField] private string _graphScope = ""; // the parent whose children the graph shows ("" = top level)

    private Vector2 _sheetScroll, _inspectorScroll;
    private VisualElement _main;
    private StateGraphView _graph;
    private List<StateMachineEditorUtility.BehaviorColumn> _columns = new();
    private List<StateMachineEditorUtility.VariableChoice> _variables = new();
    private List<(string label, SignalData signal)> _signals = new();

    public StateMachineDefinition Definition => _definition;
    public string Selected => _selected;

    [MenuItem("Tools/Remedy/Schematics/State Machine")]
    public static StateMachineWindow ShowWindow() => GetWindow<StateMachineWindow>("State Machine");

    public static void Open(StateMachineDefinition definition)
    {
        var window = ShowWindow();
        window.SetDefinition(definition);
    }

    [OnOpenAsset(1)]
    private static bool OnOpenAsset(int instanceId, int line)
    {
        if (EditorUtility.InstanceIDToObject(instanceId) is StateMachineDefinition definition)
        {
            Open(definition);
            return true;
        }
        return false;
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += Refresh;
        BuildUI();
    }

    private void OnDisable() => Undo.undoRedoPerformed -= Refresh;

    public void SetDefinition(StateMachineDefinition definition)
    {
        _definition = definition;
        _selected = definition != null && definition.States.Count > 0 ? definition.States[0].Id : null;
        _graphScope = "";
        Refresh();
    }

    public void Select(string stateId)
    {
        _selected = stateId;
        Repaint();
        _graph?.SyncSelection();
    }

    public void SetScope(string parentId)
    {
        _graphScope = parentId ?? "";
        Refresh();
    }

    public string Scope => _graphScope;

    public void Refresh()
    {
        if (_definition != null)
        {
            _definition.MarkChanged();
            _columns = StateMachineEditorUtility.Columns(_definition.Prefab);
            _variables = StateMachineEditorUtility.Variables(_definition.Prefab);
            _signals = StateMachineEditorUtility.Signals(_definition.Prefab);
        }
        BuildMain();
        Repaint();
    }

    #region Layout

    private void BuildUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Column;
        rootVisualElement.Add(new IMGUIContainer(DrawToolbar));

        _main = new VisualElement { style = { flexGrow = 1, minHeight = 120 } };
        rootVisualElement.Add(_main);

        var inspector = new IMGUIContainer(DrawInspector) { style = { height = 300, borderTopWidth = 1, borderTopColor = new Color(0, 0, 0, 0.5f) } };
        rootVisualElement.Add(inspector);

        Refresh();
    }

    private void BuildMain()
    {
        if (_main == null)
            return;
        _main.Clear();
        _graph = null;

        if (_definition == null)
        {
            _main.Add(new IMGUIContainer(() =>
                EditorGUILayout.HelpBox("Pick a State Machine asset above, or use \"Open State Machine\" on a State Machine Behavior in the IO Dock.", MessageType.Info)));
            return;
        }

        if (_graphMode)
        {
            _graph = new StateGraphView(this);
            _graph.StretchToParentSize();
            _main.Add(_graph);
        }
        else
            _main.Add(new IMGUIContainer(DrawSheet) { style = { flexGrow = 1 } });
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUI.BeginChangeCheck();
            var definition = (StateMachineDefinition)EditorGUILayout.ObjectField(_definition, typeof(StateMachineDefinition), false, GUILayout.Width(220));
            if (EditorGUI.EndChangeCheck())
                SetDefinition(definition);

            if (_definition != null)
            {
                EditorGUI.BeginChangeCheck();
                var prefab = (GameObject)EditorGUILayout.ObjectField(_definition.Prefab, typeof(GameObject), false, GUILayout.Width(180));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_definition, "Set State Machine Prefab");
                    _definition.Prefab = prefab;
                    Changed();
                    Refresh();
                }
            }

            GUILayout.Space(8);
            bool graph = GUILayout.Toggle(_graphMode, "State Graph", EditorStyles.toolbarButton);
            bool sheet = GUILayout.Toggle(!_graphMode, "State Manager", EditorStyles.toolbarButton);
            if (graph != _graphMode || sheet == _graphMode)
            {
                _graphMode = graph && !(_graphMode && sheet);
                BuildMain();
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_definition == null))
            {
                if (GUILayout.Button("+ State", EditorStyles.toolbarButton))
                    AddState(_graphMode ? _graphScope : "");
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                    Refresh();
            }
        }
    }

    #endregion

    #region State Manager (spreadsheet)

    private const float NameWidth = 200, InitialWidth = 34, GraphWidth = 64, CellWidth = 64, TransitionsWidth = 260, RowHeight = 20;

    private void DrawSheet()
    {
        if (_definition == null)
            return;

        _sheetScroll = EditorGUILayout.BeginScrollView(_sheetScroll);

        // Header.
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("State", EditorStyles.miniBoldLabel, GUILayout.Width(NameWidth));
            GUILayout.Label(new GUIContent("Start", "The state entered first (for a child state: the one its parent enters)."), EditorStyles.miniBoldLabel, GUILayout.Width(InitialWidth));
            GUILayout.Label("Graph", EditorStyles.miniBoldLabel, GUILayout.Width(GraphWidth));
            foreach (var column in _columns)
                GUILayout.Label(new GUIContent(Shorten(column.Label, 10), column.Label + "\nClick a cell: inherit → on → off"), EditorStyles.miniBoldLabel, GUILayout.Width(CellWidth));
            GUILayout.Label("Transitions", EditorStyles.miniBoldLabel, GUILayout.Width(TransitionsWidth));
        }

        foreach (int index in TreeOrder())
            DrawRow(index);

        if (_columns.Count == 0 && _definition.Prefab != null)
            EditorGUILayout.HelpBox("The prefab has no other Schematic Behaviors to switch yet.", MessageType.None);
        if (_definition.Prefab == null)
            EditorGUILayout.HelpBox("Set the prefab (toolbar) to list its Behaviors, Variables and Signals.", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(int index)
    {
        var state = _definition.States[index];
        int depth = _definition.DepthOf(index);
        var rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
        if (state.Id == _selected)
            EditorGUI.DrawRect(rowRect, new Color(0.24f, 0.48f, 0.9f, 0.25f));

        // Name, indented by depth, with the state's color.
        var nameRect = GUILayoutUtility.GetRect(NameWidth, RowHeight, GUILayout.Width(NameWidth));
        EditorGUI.DrawRect(new Rect(nameRect.x + depth * 14, nameRect.y + 3, 3, nameRect.height - 6), state.Color);
        var fieldRect = new Rect(nameRect.x + depth * 14 + 6, nameRect.y + 1, nameRect.width - depth * 14 - 6, RowHeight - 2);
        EditorGUI.BeginChangeCheck();
        string newName = EditorGUI.DelayedTextField(fieldRect, state.Name);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_definition, "Rename State");
            state.Name = newName;
            Changed();
        }

        // Start state (top level) / default child (nested).
        var parent = _definition.ParentOf(index);
        bool isStart = parent < 0 ? _definition.InitialIndex == index : _definition.DefaultChildOf(parent) == index;
        bool start = GUILayout.Toggle(isStart, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(InitialWidth));
        if (start && !isStart)
        {
            Undo.RecordObject(_definition, "Set Start State");
            if (parent < 0) _definition.InitialState = state.Id;
            else _definition.States[parent].DefaultChild = state.Id;
            Changed();
        }

        if (state.Graph != null)
        {
            if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(GraphWidth)))
                AssetDatabase.OpenAsset(state.Graph);
        }
        else if (GUILayout.Button("Create", EditorStyles.miniButton, GUILayout.Width(GraphWidth)))
            AssetDatabase.OpenAsset(StateMachineEditorUtility.CreateStateGraph(_definition, state));

        foreach (var column in _columns)
            DrawCell(index, state, column);

        GUILayout.Label(new GUIContent(Summary(state), "Edit below"), EditorStyles.miniLabel, GUILayout.Width(TransitionsWidth));
        EditorGUILayout.EndHorizontal();

        var e = Event.current;
        if (e.type == EventType.MouseDown && rowRect.Contains(e.mousePosition))
        {
            Select(state.Id);
            if (e.button == 1)
                RowMenu(index).ShowAsContext();
            e.Use();
        }
    }

    private void DrawCell(int index, StateDefinition state, StateMachineEditorUtility.BehaviorColumn column)
    {
        var toggle = state.FindToggle(column.Type, column.Path);
        var mode = toggle?.Mode ?? ToggleMode.Inherit;
        var inherited = mode == ToggleMode.Inherit ? InheritedMode(index, column) : mode;

        string text = mode switch { ToggleMode.Enable => "On", ToggleMode.Disable => "Off", _ => inherited switch { ToggleMode.Enable => "(on)", ToggleMode.Disable => "(off)", _ => "–" } };
        var color = mode switch
        {
            ToggleMode.Enable => new Color(0.35f, 0.8f, 0.4f),
            ToggleMode.Disable => new Color(0.9f, 0.35f, 0.3f),
            _ => new Color(0.6f, 0.6f, 0.6f),
        };

        var previous = GUI.color;
        GUI.color = color;
        string tooltip = $"{column.Label} in {state.Name}: {mode}" + (mode == ToggleMode.Inherit ? $" (from parents: {inherited})" : "");
        if (GUILayout.Button(new GUIContent(text, tooltip), EditorStyles.miniButton, GUILayout.Width(CellWidth)))
        {
            Undo.RecordObject(_definition, "Toggle Behavior In State");
            if (toggle == null)
            {
                toggle = new BehaviorToggle { Behavior = column.Type, Path = column.Path };
                state.Behaviors.Add(toggle);
            }
            toggle.Mode = (ToggleMode)(((int)toggle.Mode + 1) % 3);
            if (toggle.Mode == ToggleMode.Inherit)
                state.Behaviors.Remove(toggle);
            Changed();
        }
        GUI.color = previous;
    }

    private ToggleMode InheritedMode(int index, StateMachineEditorUtility.BehaviorColumn column)
    {
        for (int p = _definition.ParentOf(index); p >= 0; p = _definition.ParentOf(p))
        {
            var toggle = _definition.States[p].FindToggle(column.Type, column.Path);
            if (toggle != null && toggle.Mode != ToggleMode.Inherit)
                return toggle.Mode;
        }
        return ToggleMode.Inherit;
    }

    private GenericMenu RowMenu(int index)
    {
        var state = _definition.States[index];
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Add Child State"), false, () => AddState(state.Id));
        menu.AddItem(new GUIContent("Add Sibling State"), false, () => AddState(state.Parent));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Move Up"), false, () => Move(index, -1));
        menu.AddItem(new GUIContent("Move Down"), false, () => Move(index, 1));
        foreach (var other in _definition.States)
            if (other != state && !_definition.IsSelfOrAncestor(index, _definition.IndexOf(other.Id)))
            {
                var target = other;
                menu.AddItem(new GUIContent("Move Into/" + target.Name), false, () => Reparent(state, target.Id));
            }
        if (!string.IsNullOrEmpty(state.Parent))
            menu.AddItem(new GUIContent("Move Into/(top level)"), false, () => Reparent(state, ""));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Delete"), false, () => Delete(state));
        return menu;
    }

    #endregion

    #region Selected state: transitions as an event sheet

    private void DrawInspector()
    {
        var state = _definition != null ? _definition.Find(_selected) : null;
        if (state == null)
        {
            EditorGUILayout.HelpBox("Select a state to edit its transitions.", MessageType.None);
            return;
        }

        _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(state.Name, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var color = EditorGUILayout.ColorField(GUIContent.none, state.Color, false, false, false, GUILayout.Width(40));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_definition, "State Color");
                state.Color = color;
                Changed();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Transition", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                Undo.RecordObject(_definition, "Add Transition");
                state.Transitions.Add(new StateTransition());
                Changed();
            }
        }

        if (state.Transitions.Count == 0)
            EditorGUILayout.LabelField("No transitions. A parent's transitions still apply here.", EditorStyles.miniLabel);

        for (int t = 0; t < state.Transitions.Count; t++)
        {
            if (DrawTransition(state, state.Transitions[t], t))
                break; // the list changed
        }

        EditorGUILayout.EndScrollView();
    }

    // Returns true if the transition was removed.
    private bool DrawTransition(StateDefinition state, StateTransition transition, int index)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var trigger = (TransitionTrigger)EditorGUILayout.EnumPopup(transition.Trigger, GUILayout.Width(90));
                SignalData signal = transition.Signal;
                if (trigger == TransitionTrigger.Signal)
                {
                    GUILayout.Label("When", GUILayout.Width(36));
                    signal = SignalPopup(transition.Signal);
                    GUILayout.Label("arrives", GUILayout.Width(44));
                }
                else
                    GUILayout.Label(transition.Conditions.Count == 0 && transition.MinTimeInState <= 0 ? "(add a condition)" : "When all hold", GUILayout.Width(110));

                GUILayout.FlexibleSpace();
                GUILayout.Label("→ go to", GUILayout.Width(48));
                var target = StatePopup(transition.Target);
                GUILayout.Label(new GUIContent("after", "Seconds the state must have been active."), GUILayout.Width(32));
                float minTime = EditorGUILayout.FloatField(transition.MinTimeInState, GUILayout.Width(40));
                GUILayout.Label("s", GUILayout.Width(10));

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_definition, "Edit Transition");
                    transition.Trigger = trigger;
                    transition.Signal = signal;
                    transition.Target = target;
                    transition.MinTimeInState = Mathf.Max(0, minTime);
                    Changed();
                }

                if (GUILayout.Button("▲", EditorStyles.miniButtonLeft, GUILayout.Width(22)) && index > 0)
                {
                    Undo.RecordObject(_definition, "Reorder Transitions");
                    (state.Transitions[index - 1], state.Transitions[index]) = (state.Transitions[index], state.Transitions[index - 1]);
                    Changed();
                    return true;
                }
                if (GUILayout.Button("✕", EditorStyles.miniButtonRight, GUILayout.Width(22)))
                {
                    Undo.RecordObject(_definition, "Remove Transition");
                    state.Transitions.RemoveAt(index);
                    Changed();
                    return true;
                }
            }

            for (int c = 0; c < transition.Conditions.Count; c++)
                if (DrawCondition(transition, transition.Conditions[c], c))
                    return true;

            if (GUILayout.Button("+ Condition", EditorStyles.miniButton, GUILayout.Width(90)))
            {
                Undo.RecordObject(_definition, "Add Condition");
                var condition = new StateCondition();
                if (_variables.Count > 0)
                {
                    condition.Behavior = _variables[0].Behavior;
                    condition.Path = _variables[0].Path;
                    condition.Variable = _variables[0].Variable;
                }
                transition.Conditions.Add(condition);
                Changed();
            }
        }
        return false;
    }

    private bool DrawCondition(StateTransition transition, StateCondition condition, int index)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(18);
            GUILayout.Label(index == 0 ? "if" : "and", GUILayout.Width(26));

            EditorGUI.BeginChangeCheck();
            var kind = (ConditionKind)EditorGUILayout.EnumPopup(condition.Kind, GUILayout.Width(110));
            Type behavior = condition.Behavior.Type;
            string path = condition.Path, variable = condition.Variable;
            Type valueType = null;
            var op = condition.Op;
            object value = null;
            float seconds = condition.Seconds;

            switch (kind)
            {
                case ConditionKind.Variable:
                {
                    int current = _variables.FindIndex(v => v.Behavior == behavior && v.Path == (path ?? "") && v.Variable == variable);
                    var labels = _variables.Select(v => v.Label).ToArray();
                    int picked = EditorGUILayout.Popup(current, labels, GUILayout.MinWidth(160));
                    if (picked >= 0 && picked < _variables.Count)
                    {
                        behavior = _variables[picked].Behavior;
                        path = _variables[picked].Path;
                        variable = _variables[picked].Variable;
                        valueType = _variables[picked].ValueType;
                    }
                    op = (CompareOp)EditorGUILayout.Popup((int)op, OpLabels, GUILayout.Width(60));
                    if (op != CompareOp.IsTrue && op != CompareOp.IsFalse && valueType != null)
                    {
                        if (SchematicValue.IsDirect(valueType))
                            SchematicValueField.Draw(GUIContent.none, valueType, condition.Value.Get(valueType), out value);
                        else
                            GUILayout.Label("(compare with Equal to null)", EditorStyles.miniLabel);
                    }
                    break;
                }
                case ConditionKind.TimeInState:
                    op = (CompareOp)EditorGUILayout.Popup((int)op, OpLabels, GUILayout.Width(60));
                    seconds = EditorGUILayout.FloatField(seconds, GUILayout.Width(60));
                    GUILayout.Label("seconds", GUILayout.Width(52));
                    break;
                case ConditionKind.BehaviorEnabled:
                {
                    int current = _columns.FindIndex(c => c.Type == behavior && c.Path == (path ?? ""));
                    int picked = EditorGUILayout.Popup(current, _columns.Select(c => c.Label).ToArray(), GUILayout.MinWidth(160));
                    if (picked >= 0 && picked < _columns.Count)
                    {
                        behavior = _columns[picked].Type;
                        path = _columns[picked].Path;
                    }
                    int enabled = EditorGUILayout.Popup(op == CompareOp.IsFalse ? 1 : 0, new[] { "is on", "is off" }, GUILayout.Width(60));
                    op = enabled == 1 ? CompareOp.IsFalse : CompareOp.IsTrue;
                    break;
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_definition, "Edit Condition");
                condition.Kind = kind;
                condition.Behavior = behavior;
                condition.Path = path ?? "";
                condition.Variable = variable;
                condition.Op = op;
                condition.Seconds = seconds;
                if (value != null && valueType != null)
                    condition.Value.Set(value, valueType);
                Changed();
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                Undo.RecordObject(_definition, "Remove Condition");
                transition.Conditions.RemoveAt(index);
                Changed();
                return true;
            }
        }
        return false;
    }

    private static readonly string[] OpLabels = { "=", "≠", "<", "≤", ">", "≥", "is true", "is false" };

    private SignalData SignalPopup(SignalData current)
    {
        var labels = new List<string> { "(other Signal asset)" };
        labels.AddRange(_signals.Select(s => s.label));
        int index = _signals.FindIndex(s => s.signal == current);
        int picked = EditorGUILayout.Popup(index + 1, labels.ToArray(), GUILayout.MinWidth(140));
        if (picked > 0)
            return _signals[picked - 1].signal;
        // Any other Signal asset, e.g. a global input Signal.
        return (SignalData)EditorGUILayout.ObjectField(index >= 0 ? null : current, typeof(SignalData), false, GUILayout.Width(120));
    }

    private string StatePopup(string current)
    {
        var order = TreeOrder().ToList();
        var labels = order.Select(i => new string(' ', _definition.DepthOf(i) * 2) + _definition.States[i].Name).ToArray();
        int index = order.IndexOf(_definition.IndexOf(current));
        int picked = EditorGUILayout.Popup(index, labels, GUILayout.Width(130));
        return picked >= 0 && picked < order.Count ? _definition.States[order[picked]].Id : current;
    }

    #endregion

    #region Editing

    public IEnumerable<int> TreeOrder()
    {
        var visited = new HashSet<int>();
        for (int i = 0; i < _definition.States.Count; i++)
            if (_definition.ParentOf(i) < 0)
                foreach (int index in Walk(i, visited))
                    yield return index;
    }

    private IEnumerable<int> Walk(int index, HashSet<int> visited)
    {
        if (!visited.Add(index))
            yield break;
        yield return index;
        foreach (int child in _definition.ChildrenOf(index))
            foreach (int descendant in Walk(child, visited))
                yield return descendant;
    }

    public StateDefinition AddState(string parentId, Vector2 position = default)
    {
        Undo.RecordObject(_definition, "Add State");
        var state = new StateDefinition
        {
            Name = UniqueName("State"),
            Parent = parentId ?? "",
            Position = position,
            Color = Color.HSVToRGB(UnityEngine.Random.value, 0.45f, 0.65f),
        };
        _definition.States.Add(state);
        if (string.IsNullOrEmpty(_definition.InitialState) && string.IsNullOrEmpty(parentId))
            _definition.InitialState = state.Id;
        Changed();
        Select(state.Id);
        Refresh();
        return state;
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        for (int i = 2; _definition.States.Any(s => s.Name == name); i++)
            name = baseName + " " + i;
        return name;
    }

    private void Move(int index, int direction)
    {
        var state = _definition.States[index];
        int other = index + direction;
        while (other >= 0 && other < _definition.States.Count && _definition.States[other].Parent != state.Parent)
            other += direction;
        if (other < 0 || other >= _definition.States.Count)
            return;
        Undo.RecordObject(_definition, "Move State");
        (_definition.States[index], _definition.States[other]) = (_definition.States[other], _definition.States[index]);
        Changed();
        Refresh();
    }

    public void Reparent(StateDefinition state, string parentId)
    {
        Undo.RecordObject(_definition, "Move State");
        state.Parent = parentId ?? "";
        Changed();
        Refresh();
    }

    public void Delete(StateDefinition state)
    {
        if (!EditorUtility.DisplayDialog("Delete State", $"Delete '{state.Name}' and its child states? Transitions into them are removed too. Their graphs stay in the project.", "Delete", "Cancel"))
            return;

        Undo.RecordObject(_definition, "Delete State");
        int index = _definition.IndexOf(state.Id);
        var removed = new HashSet<string>();
        for (int i = 0; i < _definition.States.Count; i++)
            if (_definition.IsSelfOrAncestor(index, i))
                removed.Add(_definition.States[i].Id);

        _definition.States.RemoveAll(s => removed.Contains(s.Id));
        foreach (var other in _definition.States)
        {
            other.Transitions.RemoveAll(t => removed.Contains(t.Target));
            if (removed.Contains(other.DefaultChild)) other.DefaultChild = "";
        }
        if (removed.Contains(_definition.InitialState)) _definition.InitialState = "";
        if (removed.Contains(_selected)) _selected = null;
        if (removed.Contains(_graphScope)) _graphScope = "";
        Changed();
        Refresh();
    }

    public void Changed()
    {
        _definition.MarkChanged();
        EditorUtility.SetDirty(_definition);
        _graph?.Rebuild();
        Repaint();
    }

    private string Summary(StateDefinition state)
    {
        if (state.Transitions.Count == 0)
            return "";
        return string.Join(", ", state.Transitions.Select(t =>
        {
            string target = _definition.Find(t.Target)?.Name ?? "?";
            string when = t.Trigger == TransitionTrigger.Signal ? (t.Signal != null ? "on " + t.Signal.name : "on ?")
                         : string.Join(" & ", t.Conditions.Select(Describe));
            return $"→ {target}" + (string.IsNullOrEmpty(when) ? "" : $" ({when})");
        }));
    }

    public string Describe(StateCondition condition) => condition.Kind switch
    {
        ConditionKind.TimeInState => $"time {OpLabels[(int)condition.Op]} {condition.Seconds}s",
        ConditionKind.BehaviorEnabled => $"{condition.Behavior.Type?.Name} {(condition.Op == CompareOp.IsFalse ? "off" : "on")}",
        _ => condition.Op == CompareOp.IsTrue || condition.Op == CompareOp.IsFalse
            ? $"{condition.Variable} {OpLabels[(int)condition.Op]}"
            : $"{condition.Variable} {OpLabels[(int)condition.Op]} {ValueText(condition)}",
    };

    private string ValueText(StateCondition condition)
    {
        var type = _variables.FirstOrDefault(v => v.Behavior == condition.Behavior.Type && v.Variable == condition.Variable).ValueType;
        return type != null && SchematicValue.IsDirect(type) ? condition.Value.Get(type)?.ToString() ?? "null" : "?";
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 1) + "…";

    #endregion
}
