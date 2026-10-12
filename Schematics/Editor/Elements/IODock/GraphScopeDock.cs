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

        Add(Header("Signals", "Add a Signal to this graph: send it to an object, and react to it in any graph that object runs.", () =>
        {
            var signal = GraphScopeEditing.AddSignal(_graph);
            ScopeItemInspector.Show(SignalItem(_graph, signal));
        }));
        if (_graph.Signals.Count == 0)
            Add(Hint("None yet. Press + to add one, then drag it into the graph to Send it or react to it."));
        foreach (var signal in _graph.Signals.ToList())
            if (signal != null)
                Add(SignalRow(_graph, signal, editable: true));

        Add(OtherGraphs());
    }

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

    private VisualElement SignalRow(SchematicGraph graph, GraphSignal signal, bool editable)
    {
        var item = SignalItem(graph, signal);
        string detail = DockRows.Parameters(signal.Parameters.Where(p => p != null).Select(p => (p.Name, ReferenceEquals(p.Type, null) ? null : p.Type.Type)));
        var row = DockRows.Build(item, DockRows.InputIcon, signal.Name, detail,
            "Graph Signal: drag it in to Send it or react to it; click it to find its nodes and edit it in the Inspector."
            + (string.IsNullOrEmpty(signal.Description) ? "" : "\n\n" + signal.Description),
            () => { _window.FocusNodes(item); ScopeItemInspector.Show(item); });
        if (signal.Asset != null)
            _rows[item.Key] = row;

        if (editable)
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Rename", _ => Rename(row, signal.Name, name => GraphScopeEditing.RenameSignal(graph, signal, name)));
                evt.menu.AppendAction("Edit Parameters…", _ => ScopeItemInspector.Show(item));
                evt.menu.AppendAction("Delete", _ => GraphScopeEditing.DeleteSignal(graph, signal));
            }));
        return row;
    }

    public static DockItem VariableItem(SchematicGraph graph, GraphVariable variable) => new()
    {
        Kind = DockItem.ItemKind.GraphVariable, Graph = graph, VariableId = variable.Id, Name = variable.Name, ValueType = variable.ValueType,
    };

    public static DockItem SignalItem(SchematicGraph graph, GraphSignal signal) => new()
    {
        Kind = DockItem.ItemKind.Signal, Graph = graph, Signal = signal.Asset, Name = signal.Name, Direction = SignalDirection.Unspecified,
    };

    #endregion

    #region Other graphs

    // Graphs whose Variables and Signals this graph uses, remembered per graph (in this project, on this machine).
    private string PinnedKey => "SchematicGraphScope.Pinned." + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_graph));

    private List<SchematicGraph> Pinned()
    {
        var graphs = new List<SchematicGraph>();
        foreach (var guid in EditorPrefs.GetString(PinnedKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            if (AssetDatabase.LoadAssetAtPath<SchematicGraph>(AssetDatabase.GUIDToAssetPath(guid)) is { } graph && graph != _graph)
                graphs.Add(graph);
        // Graphs this one already uses something from count too.
        foreach (var node in _graph.Nodes)
            if (node is GraphVariableNodeBase variable && variable.Owner != null && variable.Owner != _graph && !graphs.Contains(variable.Owner))
                graphs.Add(variable.Owner);
        return graphs;
    }

    private void SavePinned(IEnumerable<SchematicGraph> graphs)
        => EditorPrefs.SetString(PinnedKey, string.Join(";", graphs.Select(g => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(g)))));

    private VisualElement OtherGraphs()
    {
        var foldout = new Foldout { text = "From Other Graphs", value = SessionState.GetBool(PinnedKey + ".open", true) };
        foldout.AddToClassList("dock-object");
        foldout.RegisterValueChangedCallback(evt => { if (evt.target == foldout) SessionState.SetBool(PinnedKey + ".open", evt.newValue); });

        var pinned = Pinned();
        foreach (var other in pinned)
        {
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
                section.Add(SignalRow(other, signal, editable: false));
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

    private void Rename(VisualElement anchor, string current, Action<string> rename)
    {
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
