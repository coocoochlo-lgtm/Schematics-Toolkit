using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The parameters under a Signal in the IO Dock (a Behavior's Signals, graph Signals in the Graph tab): a dot each, in
/// its type's colour, in a line under the Signal's name; hovered, a dot stretches out into a pill holding "name: type"
/// (the type dimmer). Where they can be edited: double-click the name to rename it, click the type for the list of
/// types, right-click a pill to remove it, "+" to add one (its type first, then its name). It doesn't store anything itself: it reads the current list and
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

    private readonly VisualElement _dots;
    private readonly List<(VisualElement pill, Label name, Label type)> _pills = new();
    private Label _add;
    // Renaming one: it stays stretched out.
    private int _pinned = -1;

    // A pill's size folded (a dot) and stretched out (as wide as its words, up to this).
    private const float Dot = 9f, PillHeight = 14f, PillMaxWidth = 260f;

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

        _dots = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, flexWrap = Wrap.Wrap, flexShrink = 1 } };
        Add(_dots);
        Refresh();

        // Undo / Redo of a parameter edit: show the restored list.
        RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Refresh);
        RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Refresh);
    }

    /// <summary>Redraws the pills from the current parameters.</summary>
    public void Refresh()
    {
        _dots.Clear();
        _pills.Clear();
        var parameters = _read?.Invoke() ?? new List<(string, Type)>();
        for (int i = 0; i < parameters.Count; i++)
            _dots.Add(Pill(parameters[i].name, parameters[i].type, i));

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
        if (_pinned >= parameters.Count)
            _pinned = -1;
        if (_pinned >= 0)
            Stretch(_pinned, true);
    }

    // A parameter: a dot in its type's colour that stretches out into a pill holding "name: type" while hovered.
    private VisualElement Pill(string name, Type type, int index)
    {
        var color = DockRows.ColorOf(type);
        float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
        var ink = luminance > 0.6f ? new Color(0.08f, 0.08f, 0.08f) : Color.white;

        var pill = new VisualElement();
        pill.AddToClassList("dock-signal-param-pill");
        pill.style.backgroundColor = color;
        pill.style.flexDirection = FlexDirection.Row;
        pill.style.alignItems = Align.Center;
        pill.style.flexShrink = 0;
        pill.style.overflow = Overflow.Hidden;
        pill.style.borderTopLeftRadius = pill.style.borderTopRightRadius = pill.style.borderBottomLeftRadius = pill.style.borderBottomRightRadius = PillHeight * 0.5f;
        pill.style.marginRight = 3;
        pill.style.marginTop = pill.style.marginBottom = 1;

        var nameLabel = new Label(name + ":");
        nameLabel.AddToClassList("dock-signal-param-name");
        nameLabel.style.color = ink;
        var typeLabel = new Label(TypeLabel(type));
        typeLabel.AddToClassList("dock-signal-param-type");
        typeLabel.style.color = new Color(ink.r, ink.g, ink.b, 0.6f);
        pill.Add(nameLabel);
        pill.Add(typeLabel);
        _pills.Add((pill, nameLabel, typeLabel));
        SetStretched(pill, nameLabel, typeLabel, false);

        pill.RegisterCallback<MouseEnterEvent>(_ => Stretch(index, true));
        pill.RegisterCallback<MouseLeaveEvent>(_ => Stretch(index, false));

        if (_namesEditable)
        {
            nameLabel.tooltip = "Double-click to rename it (also its pin's name on the nodes).";
            nameLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                evt.StopPropagation();
                Rename(index);
            });
        }
        if (_typesEditable)
        {
            typeLabel.tooltip = "Click to change the type of value it carries.";
            typeLabel.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                ShowTypeMenu(index, typeLabel);
            });
        }
        if (_editable)
            pill.AddManipulator(new ContextualMenuManipulator(evt =>
                evt.menu.AppendAction("Remove Parameter", _ =>
                {
                    var edited = _read();
                    if (index >= edited.Count) return;
                    edited.RemoveAt(index);
                    Write(edited, null, null);
                })));
        return pill;
    }

    // Stretches a pill out (or folds it back to a dot, unless it's being renamed).
    private void Stretch(int index, bool stretched)
    {
        if (index < 0 || index >= _pills.Count)
            return;
        if (!stretched && _pinned == index)
            return;
        var (pill, name, type) = _pills[index];
        SetStretched(pill, name, type, stretched);
    }

    private static void SetStretched(VisualElement pill, Label name, Label type, bool stretched)
    {
        pill.style.maxWidth = stretched ? PillMaxWidth : Dot;
        pill.style.minWidth = Dot;
        pill.style.height = stretched ? PillHeight : Dot;
        name.style.opacity = type.style.opacity = stretched ? 1f : 0f;
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

    private void Rename(int index)
    {
        var parameters = _read();
        if (index >= parameters.Count || index >= _pills.Count) return;
        _pinned = index;
        Stretch(index, true);
        string old = parameters[index].name;
        Remedy.Schematics.Templates.InlineRename.Begin(_pills[index].name, old, wanted =>
        {
            var edited = _read();
            if (index >= edited.Count) return;
            wanted = Unique(wanted, edited.Where((_, i) => i != index).Select(p => p.name));
            edited[index] = (wanted, edited[index].type);
            Write(edited, old, wanted);
        }, () =>
        {
            _pinned = -1;
            if (panel != null) Stretch(index, false);
        });
    }

    private void ShowTypeMenu(int index, VisualElement under)
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
        menu.DropDown(under.worldBound);
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
