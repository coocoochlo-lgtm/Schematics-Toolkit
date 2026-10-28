using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The IO Dock's Graph tab: the graph's own Variables and Signals (every object running the graph has its own values,
/// and its own Signal routing), the Variables and Signals of other graphs you've pinned here, and the nodes that use
/// things. Rows look and drag like the Prefab tab's; click one to find its nodes and edit it in the Inspector.
/// </summary>
public class GraphScopeDock : VisualElement
{
    private readonly SchematicGraphEditorWindow _window;
    private readonly SchematicGraph _graph;
    private readonly Dictionary<string, VisualElement> _rows;

    public GraphScopeDock(SchematicGraphEditorWindow window, SchematicGraph graph, Dictionary<string, VisualElement> rows)
    {
        _window = window;
        _graph = graph;
        _rows = rows;
        AddToClassList("behavior-dock");
        style.overflow = Overflow.Hidden;
        style.minWidth = 0;
        style.flexShrink = 1;
        Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        if (_graph == null)
            return;

        // This graph's own.
        Add(Header("Variables", "Add a Variable to this graph: each object running it gets its own value.", AddVariableMenu));
        if (_graph.Variables.Count == 0)
            Add(Hint("None yet. Press + to add one, then drag it into the graph to Get or Set it."));
        foreach (var variable in _graph.Variables.ToList())
            if (variable != null)
                Add(VariableRow(_graph, variable, editable: true));

        // Signals are directional: other graphs send this graph's Inputs (it reacts to them) and react to its Outputs
        // (it sends them). Old Signals without a direction get one from how the graph uses them.
        GraphScopeEditing.MigrateDirections(_graph);
        AddSignalSection(GraphSignalDirection.Input, "Input Signals",
            "Add an Input Signal: other graphs (and Behaviors) send it to an object running this graph, and this graph reacts to it.",
            "None yet. Press + to add one: other graphs send it, and this graph reacts to it (drag it in for On Received).");
        AddSignalSection(GraphSignalDirection.Output, "Output Signals",
            "Add an Output Signal: this graph sends it, and other graphs react to it to extend what happens.",
            "None yet. Press + to add one: this graph sends it (drag it in for Send), and other graphs react to it.");

        if (_graph.CallableAsFunction)
            Add(FunctionSection());

        Add(OtherGraphs());
    }

    #region Function

    // The graph as a function: whether it's in the node search's Functions, where, and what it's for (its description,
    // uses and notes, which the assistant reads too), with what calling it takes and gives.
    private VisualElement FunctionSection()
    {
        var section = new VisualElement();
        var label = DockRows.SectionLabel("Function");
        label.tooltip = "Call this graph from other graphs of its kind like a node: its Parameters become the calling node's pins, each with the control set on its Parameter node (a slider, a toggle, a dropdown).";
        section.Add(label);
        var graph = _graph;
        var fields = new IMGUIContainer(() =>
        {
            var info = graph.Function;
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 72;
            EditorGUI.BeginChangeCheck();
            bool inLibrary = EditorGUILayout.Toggle(new GUIContent("In Library", "Offer it in the node search of graphs of its kind, under Functions."), info.InLibrary);
            string path = info.Path;
            using (new EditorGUI.DisabledScope(!inLibrary))
                path = EditorGUILayout.TextField(new GUIContent("Path", "Its folder under Functions in the node search (Characters/Face)."), info.Path ?? "");
            EditorGUILayout.LabelField(new GUIContent("Description", "What it does: shown with it, and given to the assistant."));
            string description = EditorGUILayout.TextArea(graph.Description ?? "", EditorStyles.textArea, GUILayout.MinHeight(34));
            EditorGUILayout.LabelField(new GUIContent("Use For", "What it's good for, one use per line."));
            string useFor = EditorGUILayout.TextArea(info.UseFor ?? "", EditorStyles.textArea, GUILayout.MinHeight(28));
            EditorGUILayout.LabelField(new GUIContent("Notes", "Anything else worth knowing when using it."));
            string notes = EditorGUILayout.TextArea(info.Notes ?? "", EditorStyles.textArea, GUILayout.MinHeight(20));
            EditorGUIUtility.labelWidth = labelWidth;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(graph, "Edit Function");
                info.InLibrary = inLibrary;
                info.Path = path;
                graph.Description = description;
                info.UseFor = useFor;
                info.Notes = notes;
                GraphScopeEditing.Commit(graph, structural: false, redraw: false);
            }

            // A Function Graph's inputs and outputs (On Called's and Return's pins, and its Call nodes').
            if (graph is FunctionGraph function)
            {
                PinList(function, function.Function.Inputs, "Inputs", "What a call gives it: On Called's pins, and the Call node's inputs.");
                PinList(function, function.Function.Outputs, "Outputs", "What it hands back: Return's pins, and the Call node's outputs.");
            }

            // What calling it takes and gives.
            var takes = graph.FunctionInputs.Select(i => i.Name)
                .Concat(graph.GraphParameters.Select(p => p.UI != null && p.UI.Control != ParameterControl.Number ? $"{p.Name} ({ObjectNames.NicifyVariableName(p.UI.Control.ToString()).ToLowerInvariant()})" : p.Name))
                .ToList();
            var gives = graph.FunctionOutputs.Select(o => o.Name).ToList();
            EditorGUILayout.LabelField(takes.Count == 0 ? "Takes nothing (add Parameters to give it pins)." : "Takes: " + string.Join(", ", takes), EditorStyles.wordWrappedMiniLabel);
            if (gives.Count > 0)
                EditorGUILayout.LabelField("Gives: " + string.Join(", ", gives), EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(new GUIContent("Copy Docs", "Copy its documentation (the form built-in nodes use) to the clipboard."), EditorStyles.miniButton))
                EditorGUIUtility.systemCopyBuffer = GraphFunctions.Doc(graph);
        });
        fields.style.marginLeft = 6;
        fields.style.marginRight = 4;
        section.Add(fields);
        return section;
    }

    // One list of a Function Graph's pins, as rows: name, type, and for numbers their control (with its range or
    // options, and a description, on a second line).
    private static void PinList(FunctionGraph graph, List<FunctionPin> pins, string title, string tooltip)
    {
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(new GUIContent(title, tooltip), EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("+", "Add one"), EditorStyles.miniButton, GUILayout.Width(22)))
            {
                Undo.RecordObject(graph, "Add Function Pin");
                string name = title == "Inputs" ? "Input" : "Output";
                int n = 1;
                while (pins.Any(p => p != null && p.Name == name + (n == 1 ? "" : " " + n)))
                    n++;
                pins.Add(new FunctionPin { Name = name + (n == 1 ? "" : " " + n) });
                CommitFunction(graph);
                GUIUtility.ExitGUI();
            }
        }
        var types = GraphScopeEditing.VariableTypes;
        var typeNames = types.Select(t => t.path).ToArray();
        for (int i = 0; i < pins.Count; i++)
        {
            var pin = pins[i];
            if (pin == null)
                continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField(pin.Name ?? "", GUILayout.MinWidth(60));
                int typeIndex = Array.FindIndex(types, t => t.type == pin.ValueType);
                int newType = EditorGUILayout.Popup(typeIndex, typeNames, GUILayout.Width(70));
                if (EditorGUI.EndChangeCheck())
                {
                    name = name.Trim();
                    string old = pin.Name;
                    bool clash = pins.Any(p => p != null && p != pin && p.Name == name);
                    Undo.RecordObject(graph, "Edit Function Pin");
                    if (!string.IsNullOrEmpty(name) && !clash)
                        pin.Name = name;
                    if (newType >= 0 && newType != typeIndex)
                        pin.Type = types[newType].type;
                    CommitFunction(graph, old, pin.Name);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("−", "Remove it (and its wires)"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    Undo.RecordObject(graph, "Remove Function Pin");
                    pins.RemoveAt(i);
                    CommitFunction(graph);
                    GUIUtility.ExitGUI();
                }
            }
            if (!pin.HasInlineValue)
                continue;
            pin.UI ??= new ParameterUI();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                EditorGUI.BeginChangeCheck();
                var control = (ParameterControl)EditorGUILayout.EnumPopup(pin.UI.Control, GUILayout.Width(92));
                float min = pin.UI.Min, max = pin.UI.Max;
                string options = pin.UI.Options;
                if (control == ParameterControl.Slider || control == ParameterControl.WholeNumber)
                {
                    min = EditorGUILayout.FloatField(min, GUILayout.MinWidth(30));
                    max = EditorGUILayout.FloatField(max, GUILayout.MinWidth(30));
                }
                else if (control == ParameterControl.Dropdown)
                    options = EditorGUILayout.DelayedTextField(options ?? "", GUILayout.MinWidth(60));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(graph, "Edit Function Pin");
                    pin.UI.Control = control;
                    pin.UI.Min = min;
                    pin.UI.Max = max;
                    pin.UI.Options = options;
                    CommitFunction(graph);
                }
            }
        }
        if (pins.Count == 0)
            EditorGUILayout.LabelField("None. Press + to add one.", EditorStyles.miniLabel);
    }

    private static void CommitFunction(FunctionGraph graph, string renamedFrom = null, string renamedTo = null)
    {
        GraphScopeEditing.Commit(graph, structural: false, redraw: false);
        FunctionGraphEditor.SyncNodes(graph, renamedFrom, renamedTo);
    }

    #endregion

    #region Rows

    private VisualElement VariableRow(SchematicGraph graph, GraphVariable variable, bool editable)
    {
        var item = VariableItem(graph, variable);
        string detail = DockRows.TypeName(variable.ValueType) + (variable.Networked ? "  ⇅" : "");
        var row = DockRows.Build(item, DockRows.GraphVariableIcon, variable.Name, detail,
            "Graph Variable: drag the name into the graph to Get or Set it; click it to find its nodes and edit it in the Inspector."
            + (string.IsNullOrEmpty(variable.Description) ? "" : "\n\n" + variable.Description),
            () => { _window.FocusNodes(item); ScopeItemInspector.Show(item); });
        _rows[item.Key] = row;

        if (editable)
        {
            // The starting value, right on the row.
            var type = variable.ValueType;
            if (type != null && !SchematicValue.IsBoxable(type))
            {
                var field = new IMGUIContainer(() =>
                {
                    if (SchematicValueField.Draw(GUIContent.none, type, variable.Default?.Get(type), out var value))
                        GraphScopeEditing.SetVariableDefault(graph, variable, value);
                });
                field.style.flexGrow = 1;
                field.style.flexShrink = 1;
                field.style.minWidth = 0;
                field.style.overflow = Overflow.Hidden;
                field.AddToClassList("dock-variable-value");
                row.Add(field);
            }

            RenameOnDoubleClick(row, variable.Name, name => GraphScopeEditing.RenameVariable(graph, variable, name));
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Rename", _ => Rename(row, variable.Name, name => GraphScopeEditing.RenameVariable(graph, variable, name)));
                foreach (var (path, t) in GraphScopeEditing.VariableTypes)
                {
                    var choice = t;
                    evt.menu.AppendAction("Type/" + path, _ => GraphScopeEditing.SetVariableType(graph, variable, choice),
                        variable.ValueType == t ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                }
                evt.menu.AppendAction("Network Sync", _ =>
                {
                    Undo.RecordObject(graph, "Network Sync");
                    variable.Networked = !variable.Networked;
                    GraphScopeEditing.Commit(graph, structural: false);
                }, variable.Networked ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("Duplicate", _ => GraphScopeEditing.DuplicateVariable(graph, variable));
                evt.menu.AppendAction("Delete", _ => GraphScopeEditing.DeleteVariable(graph, variable));
            }));
        }
        return row;
    }

    private void AddSignalSection(GraphSignalDirection direction, string title, string tooltip, string hint)
    {
        Add(Header(title, tooltip, () =>
        {
            var signal = GraphScopeEditing.AddSignal(_graph, direction: direction);
            ScopeItemInspector.Show(SignalItem(_graph, signal));
        }));
        var signals = _graph.Signals.Where(s => s != null && (direction == GraphSignalDirection.Input
            ? s.Direction != GraphSignalDirection.Output : s.Direction == GraphSignalDirection.Output)).ToList();
        if (signals.Count == 0)
            Add(Hint(hint));
        foreach (var signal in signals)
            Add(SignalCard(_graph, signal, editable: true));
    }

    // A Signal's row with, for this graph's own, its parameters (and "Add Parameter…") under it.
    private VisualElement SignalCard(SchematicGraph graph, GraphSignal signal, bool editable)
    {
        var card = new VisualElement();
        card.AddToClassList("graph-signal-card");
        var row = SignalRow(graph, signal, editable, out var item);
        card.Add(row);
        if (!editable)
            return card;

        var parameters = new SignalParameterList(
            () => GraphScopeEditing.SignalParameterList(signal),
            (list, from, to) => GraphScopeEditing.SetSignalParameters(graph, signal, list, from, to),
            GraphScopeEditing.VariableTypes);
        card.Add(parameters);

        RenameOnDoubleClick(row, signal.Name, name => GraphScopeEditing.RenameSignal(graph, signal, name));
        row.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("Rename", _ => Rename(row, signal.Name, name => GraphScopeEditing.RenameSignal(graph, signal, name)));
            evt.menu.AppendAction("Direction/Input (other graphs send it, this graph reacts)", _ => GraphScopeEditing.SetSignalDirection(graph, signal, GraphSignalDirection.Input),
                signal.Direction != GraphSignalDirection.Output ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("Direction/Output (this graph sends it, other graphs react)", _ => GraphScopeEditing.SetSignalDirection(graph, signal, GraphSignalDirection.Output),
                signal.Direction == GraphSignalDirection.Output ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            // Parameters are edited right under the row; this jumps there (and shows the Signal in the Inspector too).
            evt.menu.AppendAction("Edit Parameters…", _ =>
            {
                ScopeItemInspector.Show(item);
                parameters.schedule.Execute(parameters.FocusFirst);
            });
            evt.menu.AppendAction("Add Parameter…", _ => parameters.schedule.Execute(() =>
            {
                var list = GraphScopeEditing.SignalParameterList(signal);
                if (list.Count >= SignalParameterList.MaxParameters) return;
                list.Add((SignalParameterList.Unique("Value", list.Select(p => p.name)), typeof(float)));
                GraphScopeEditing.SetSignalParameters(graph, signal, list);
            }), GraphScopeEditing.SignalParameterList(signal).Count < SignalParameterList.MaxParameters ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Delete", _ => GraphScopeEditing.DeleteSignal(graph, signal));
        }));
        return card;
    }

    private VisualElement SignalRow(SchematicGraph graph, GraphSignal signal, bool editable, out DockItem item)
    {
        item = SignalItem(graph, signal, _graph);
        var dockItem = item;
        bool output = signal.Direction == GraphSignalDirection.Output;
        // This graph's own list their parameters (editable) under the row instead.
        string detail = editable ? "" : DockRows.Parameters(signal.Parameters.Where(p => p != null).Select(p => (p.Name, p.Type.Type)));
        bool own = graph == _graph;
        string use = output
            ? (own ? "Output Signal: this graph sends it (drag it in for Send); other graphs react to it." : "Output Signal of " + graph.name + ": drag it in to react to it (On Received).")
            : (own ? "Input Signal: other graphs send it; this graph reacts to it (drag it in for On Received)." : "Input Signal of " + graph.name + ": drag it in to send it (Send, with the object in To).");
        var row = DockRows.Build(item, DockRows.SignalIcon, signal.Name, detail,
            use + " Click it to find its nodes and edit it in the Inspector."
            + (string.IsNullOrEmpty(signal.Description) ? "" : "\n\n" + signal.Description),
            () => { _window.FocusNodes(dockItem); ScopeItemInspector.Show(dockItem); });
        // Coloured by what the Signal is, whichever node dragging it makes here.
        row.RemoveFromClassList("event-container-input");
        row.RemoveFromClassList("event-container-output");
        row.AddToClassList(output ? "event-container-output" : "event-container-input");
        // Its triangle by which way it goes for this graph: its own Outputs and others' Inputs are sent from here (▶);
        // its own Inputs and others' Outputs come in (◀).
        DockRows.SetSignalMark(row, intoGraph: own != output);
        // This graph's own: its "Send"/"Receive" switches its direction.
        if (editable && own)
            DockRows.MakeDirectionToggle(row, () => GraphScopeEditing.SetSignalDirection(graph, signal,
                signal.Direction == GraphSignalDirection.Output ? GraphSignalDirection.Input : GraphSignalDirection.Output));
        if (signal.Asset != null)
            _rows[item.Key] = row;
        return row;
    }

    public static DockItem VariableItem(SchematicGraph graph, GraphVariable variable) => new()
    {
        Kind = DockItem.ItemKind.GraphVariable, Graph = graph, VariableId = variable.Id, Name = variable.Name, ValueType = variable.ValueType,
    };

    /// <summary>
    /// A graph Signal as a dock item. Dragged into <paramref name="viewer"/>, it makes the node that graph may use: in
    /// the graph that declares it, On Received for an Input and Send for an Output; in any other graph, the reverse.
    /// (A dock item's Direction is the Behavior's point of view: Input means "the graph sends it".)
    /// </summary>
    public static DockItem SignalItem(SchematicGraph graph, GraphSignal signal, SchematicGraph viewer = null) => new()
    {
        Kind = DockItem.ItemKind.Signal, Graph = graph, Signal = signal.Asset, Name = signal.Name,
        Direction = DragDirection(graph, signal, viewer ?? graph),
        DirectionFromDropTarget = true, // dropped into another graph window, it goes the other way
    };

    internal static SignalDirection DragDirection(SchematicGraph owner, GraphSignal signal, SchematicGraph viewer)
    {
        if (signal == null || signal.Direction == GraphSignalDirection.Unspecified)
            return SignalDirection.Unspecified;
        bool send = viewer == owner ? signal.Direction == GraphSignalDirection.Output : signal.Direction == GraphSignalDirection.Input;
        return send ? SignalDirection.Input : SignalDirection.Output;
    }

    #endregion

    #region Other graphs

    // Graphs whose Variables and Signals this graph uses, remembered per graph (in this project, on this machine).
    private string PinnedKey => "SchematicGraphScope.Pinned." + _graph.Uid;

    private List<SchematicGraph> Pinned()
    {
        var graphs = new List<SchematicGraph>();
        foreach (var uid in EditorPrefs.GetString(PinnedKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            if (SchematicUids.Find<SchematicGraph>(uid) is { } graph && graph != _graph)
                graphs.Add(graph);
        // Graphs this one already uses something from count too.
        foreach (var node in _graph.Nodes)
            if (node is GraphVariableNodeBase variable && variable.Owner != null && variable.Owner != _graph && !graphs.Contains(variable.Owner))
                graphs.Add(variable.Owner);
        return graphs;
    }

    private void SavePinned(IEnumerable<SchematicGraph> graphs)
        => EditorPrefs.SetString(PinnedKey, string.Join(";", graphs.Select(g => g.Uid)));

    private VisualElement OtherGraphs()
    {
        var foldout = new Foldout { text = "From Other Graphs", value = SessionState.GetBool(PinnedKey + ".open", true) };
        foldout.AddToClassList("dock-object");
        foldout.RegisterValueChangedCallback(evt => { if (evt.target == foldout) SessionState.SetBool(PinnedKey + ".open", evt.newValue); });

        var pinned = Pinned();
        foreach (var other in pinned)
        {
            GraphScopeEditing.MigrateDirections(other);
            var section = new Foldout { text = other.name, value = true };
            section.AddToClassList("dock-behavior");
            var target = other;
            section.Q<Toggle>().AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Open Graph", _ => AssetDatabase.OpenAsset(target));
                evt.menu.AppendAction("Remove From This List", _ =>
                {
                    SavePinned(Pinned().Where(g => g != target));
                    Rebuild();
                });
            }));
            foreach (var variable in other.Variables.Where(v => v != null))
                section.Add(VariableRow(other, variable, editable: false));
            foreach (var signal in other.Signals.Where(s => s != null))
                section.Add(SignalCard(other, signal, editable: false));
            if (other.Variables.Count == 0 && other.Signals.Count == 0)
                section.Add(Hint("This graph has no Variables or Signals of its own."));
            foldout.Add(section);
        }

        var picker = new UnityEditor.UIElements.ObjectField("Add Graph") { objectType = typeof(SchematicGraph), allowSceneObjects = false };
        picker.tooltip = "Pick another graph to use its Variables and Signals here (wire an object into Of / To to reach that object's).";
        picker.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue is SchematicGraph graph && graph != _graph)
            {
                SavePinned(Pinned().Append(graph).Distinct());
                Rebuild();
            }
        });
        foldout.Add(picker);
        return foldout;
    }

    #endregion

    #region Helpers

    private VisualElement Header(string text, string tooltip, Action add)
    {
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        var label = DockRows.SectionLabel(text);
        label.style.flexGrow = 1;
        header.Add(label);
        var button = new Button(add) { text = "+", tooltip = tooltip };
        button.style.width = 20;
        button.style.height = 16;
        button.style.marginTop = 4;
        header.Add(button);
        return header;
    }

    private void AddVariableMenu()
    {
        var menu = new GenericMenu();
        foreach (var (path, type) in GraphScopeEditing.VariableTypes)
        {
            var choice = type;
            menu.AddItem(new GUIContent(path), false, () =>
            {
                var variable = GraphScopeEditing.AddVariable(_graph, choice);
                ScopeItemInspector.Show(VariableItem(_graph, variable));
            });
        }
        menu.ShowAsContext();
    }

    private static Label Hint(string text)
    {
        var label = new Label(text);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 10;
        label.style.color = new Color(0.6f, 0.6f, 0.6f);
        label.style.marginLeft = 8;
        label.style.marginRight = 4;
        return label;
    }

    // Double-click a row's name to rename it (in place).
    private void RenameOnDoubleClick(VisualElement row, string current, Action<string> rename)
    {
        var name = row.Q<Label>(className: "dock-row-name");
        name?.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.clickCount != 2)
                return;
            evt.StopPropagation();
            Rename(row, current, rename);
        }, TrickleDown.TrickleDown);
    }

    // Renames a row's item: typed into its name, in place (a popup where there's no name to type into).
    private void Rename(VisualElement anchor, string current, Action<string> rename)
    {
        var name = anchor?.Q<Label>(className: "dock-row-name");
        if (name != null && name.parent != null)
        {
            Remedy.Schematics.Templates.InlineRename.Begin(name, current, value => rename(value));
            return;
        }
        Add(new InlineIdentifierEditor(popupRect: anchor.WorldBoundToScreen(),
                                       identifierType: ListIdentifierType.Name,
                                       getOriginalValue: () => current,
                                       onFinishedEditting: (bool changed, object value) =>
                                       {
                                           string name = value?.ToString();
                                           if (!string.IsNullOrWhiteSpace(name) && name != current)
                                               rename(name);
                                       }));
    }

    #endregion
}
