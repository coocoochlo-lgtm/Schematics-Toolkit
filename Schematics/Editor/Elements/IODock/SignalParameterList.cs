using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The parameters under a Signal in the IO Dock (a Behavior's Signals, graph Signals in the Graph tab): a dot each, in
/// its type's colour, in a line under the Signal's name; hover one for "name: type" (the type dimmer). Where they can be
/// edited: double-click the name to rename it, click the type for the list of types, right-click a dot to remove it,
/// "+" to add one (its type first, then its name). It doesn't store anything itself: it reads the current list and
/// hands every edit to its owner, which saves it (with Undo) and re-pins the nodes that use the Signal.
/// </summary>
public sealed class SignalParameterList : VisualElement
{
    /// <summary>Saves a new parameter list. A rename names the parameter it was and is, so wires can follow it.</summary>
    public delegate void WriteHandler(List<(string name, Type type)> parameters, string renamedFrom, string renamedTo);

    /// <summary>Types a Signal parameter can carry inside a Union (Signals from code and Remedy Input).</summary>
    public static readonly (string path, Type type)[] UnionTypes =
    {
        ("bool", typeof(bool)), ("int", typeof(int)), ("float", typeof(float)), ("string", typeof(string)),
        ("Vector2", typeof(Vector2)), ("Vector3", typeof(Vector3)), ("Vector4", typeof(Vector4)),
        ("Color", typeof(Color)), ("Quaternion", typeof(Quaternion)), ("LayerMask", typeof(LayerMask)),
        ("Objects/GameObject", typeof(GameObject)), ("Objects/Transform", typeof(Transform)), ("Objects/Component", typeof(Component)),
        ("Objects/Material", typeof(Material)), ("Objects/Texture", typeof(Texture)), ("Objects/AudioClip", typeof(AudioClip)),
        ("Objects/Object", typeof(UnityEngine.Object)),
    };

    /// <summary>Signals carry up to ten parameters (or none).</summary>
    public const int MaxParameters = 10;

    private readonly Func<List<(string name, Type type)>> _read;
    private readonly WriteHandler _write;
    private readonly IReadOnlyList<(string path, Type type)> _types;
    // _editable: parameters can be added and removed. Names and types can be edited on their own.
    private readonly bool _editable, _namesEditable, _typesEditable;
    private readonly int _max;

    private readonly VisualElement _dots, _caption;
    private readonly Label _name, _type;
    private Label _add;
    private int _shown = -1;
    // Renaming one, or its type menu open: its name stays shown.
    private bool _pinned;

    public SignalParameterList(Func<List<(string name, Type type)>> read, WriteHandler write, IReadOnlyList<(string path, Type type)> types,
                               bool editable = true, bool namesEditable = true, bool typesEditable = true, int max = MaxParameters)
    {
        _read = read;
        _write = write;
        _types = types;
        _editable = editable && write != null;
        _namesEditable = write != null && namesEditable;
        _typesEditable = write != null && typesEditable;
        _max = Mathf.Clamp(max, 0, MaxParameters);

        AddToClassList("signal-parameters");
        AddToClassList("dock-signal-params");
        style.flexDirection = FlexDirection.Row;
        style.alignItems = Align.Center;
        style.minHeight = 12;

        _dots = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexShrink = 0 } };
        Add(_dots);

        _caption = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 6, display = DisplayStyle.None } };
        _name = new Label();
        _name.AddToClassList("dock-signal-param-name");
        _type = new Label();
        _type.AddToClassList("dock-signal-param-type");
        _caption.Add(_name);
        _caption.Add(_type);
        Add(_caption);

        if (_namesEditable)
        {
            _name.tooltip = "Double-click to rename it (also its pin's name on the nodes).";
            _name.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2 || _shown < 0) return;
                evt.StopPropagation();
                Rename(_shown);
            });
        }
        if (_typesEditable)
        {
            _type.tooltip = "Click to change the type of value it carries.";
            _type.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || _shown < 0) return;
                evt.StopPropagation();
                ShowTypeMenu(_shown);
            });
        }

        RegisterCallback<MouseLeaveEvent>(_ => Hide());
        Refresh();

        // Undo / Redo of a parameter edit: show the restored list.
        RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Refresh);
        RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Refresh);
    }

    /// <summary>Redraws the dots from the current parameters.</summary>
    public void Refresh()
    {
        _dots.Clear();
        var parameters = _read?.Invoke() ?? new List<(string, Type)>();
        for (int i = 0; i < parameters.Count; i++)
        {
            int index = i;
            var dot = new VisualElement();
            dot.AddToClassList("dock-signal-param-dot");
            dot.style.width = dot.style.height = 9;
            dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 4.5f;
            dot.style.marginRight = 3;
            dot.style.backgroundColor = DockRows.ColorOf(parameters[i].type);
            dot.RegisterCallback<MouseEnterEvent>(_ => Show(index));
            if (_editable)
                dot.AddManipulator(new ContextualMenuManipulator(evt =>
                    evt.menu.AppendAction("Remove Parameter", _ =>
                    {
                        var edited = _read();
                        if (index >= edited.Count) return;
                        edited.RemoveAt(index);
                        Write(edited, null, null);
                    })));
            _dots.Add(dot);
        }

        _add = null;
        if (_editable)
        {
            _add = new Label("+");
            _add.AddToClassList("dock-signal-param-add");
            bool room = parameters.Count < _max;
            _add.tooltip = room ? "Add a parameter: pick its type, then name it." : $"This Signal can't have more than {_max} parameter(s).";
            _add.SetEnabled(room);
            _add.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                ShowAddMenu();
            });
            _dots.Add(_add);
        }

        style.display = parameters.Count > 0 || _editable ? DisplayStyle.Flex : DisplayStyle.None;
        if (_shown >= parameters.Count)
        {
            _pinned = false;
            Hide();
        }
        else if (_shown >= 0)
            Show(_shown);
    }

    /// <summary>Starts renaming the first parameter, or adding one when there are none.</summary>
    public void FocusFirst()
    {
        var parameters = _read?.Invoke();
        if (parameters != null && parameters.Count > 0 && _namesEditable)
            Rename(0);
        else if (_editable)
            ShowAddMenu();
    }

    private void Show(int index)
    {
        var parameters = _read?.Invoke();
        if (parameters == null || index < 0 || index >= parameters.Count)
            return;
        _shown = index;
        var (name, type) = parameters[index];
        _name.text = name + ":";
        _name.style.color = DockRows.ColorOf(type);
        _type.text = TypeLabel(type);
        _caption.style.display = DisplayStyle.Flex;
    }

    private void Hide()
    {
        if (_pinned) return;
        _shown = -1;
        _caption.style.display = DisplayStyle.None;
    }

    private void Rename(int index)
    {
        var parameters = _read();
        if (index >= parameters.Count) return;
        Show(index);
        _pinned = true;
        string old = parameters[index].name;
        Remedy.Schematics.Templates.InlineRename.Begin(_name, old, wanted =>
        {
            var edited = _read();
            if (index >= edited.Count) return;
            wanted = Unique(wanted, edited.Where((_, i) => i != index).Select(p => p.name));
            edited[index] = (wanted, edited[index].type);
            Write(edited, old, wanted);
        }, () =>
        {
            _pinned = false;
            if (panel != null) Show(index);
        });
    }

    private void ShowTypeMenu(int index)
    {
        var parameters = _read();
        if (index >= parameters.Count) return;
        var current = parameters[index].type;
        var menu = new GenericMenu();
        foreach (var (path, choice) in _types)
        {
            var picked = choice;
            menu.AddItem(new GUIContent(path), choice == current, () =>
            {
                if (picked == current) return;
                var edited = _read();
                if (index >= edited.Count) return;
                edited[index] = (edited[index].name, picked);
                Write(edited, null, null);
            });
        }
        menu.DropDown(_type.worldBound);
    }

    private void ShowAddMenu()
    {
        var current = _read();
        if (current.Count >= _max)
            return;
        var menu = new GenericMenu();
        foreach (var (path, type) in _types)
        {
            var choice = type;
            menu.AddItem(new GUIContent(path), false, () =>
            {
                var edited = _read();
                if (edited.Count >= _max) return;
                edited.Add((Unique("Value", edited.Select(p => p.name)), choice));
                int added = edited.Count - 1;
                Write(edited, null, null);
                // Straight into naming it.
                if (panel != null && _namesEditable)
                    schedule.Execute(() => Rename(added));
            });
        }
        if (_add != null) menu.DropDown(_add.worldBound);
        else menu.ShowAsContext();
    }

    private void Write(List<(string name, Type type)> parameters, string renamedFrom, string renamedTo)
    {
        _write?.Invoke(parameters, renamedFrom, renamedTo);
        // The owner may have rebuilt the dock (and dropped this element); if not, show the new list.
        if (panel != null)
            Refresh();
    }

    private string TypeLabel(Type type)
    {
        foreach (var (path, choice) in _types)
            if (choice == type)
                return path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
        return DockRows.TypeName(type);
    }

    internal static string Unique(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken.Where(t => t != null));
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!set.Contains($"{name} {i}"))
                return $"{name} {i}";
    }
}
