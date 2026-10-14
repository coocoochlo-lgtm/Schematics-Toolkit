using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The parameters under a Signal in the IO Dock (graph Signals in the Graph tab, Remedy Input's Signals in the
/// Global tab): each one's name and type, a ✕ to remove it, and an "Add Parameter…" button under the last one.
/// Names commit when you press Enter or click away. It doesn't store anything itself: it reads the current list and
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
    private readonly VisualElement _rows;
    private readonly Button _add;
    private TextField _firstName;

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
        style.marginLeft = 18;
        style.marginTop = 2;
        style.marginBottom = 2;

        _rows = new VisualElement();
        Add(_rows);

        if (_editable)
        {
            _add = new Button(ShowAddMenu) { text = "+ Add Parameter…", tooltip = "Add a parameter: pick its type, then name it." };
            _add.AddToClassList("signal-parameter-add");
            _add.style.alignSelf = Align.FlexStart;
            _add.style.height = 18;
            _add.style.fontSize = 10;
            _add.style.marginLeft = 0;
            Add(_add);
        }
        Refresh();

        // Undo / Redo of a parameter edit: show the restored list.
        RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Refresh);
        RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Refresh);
    }

    /// <summary>Redraws the rows from the current parameters.</summary>
    public void Refresh()
    {
        _rows.Clear();
        _firstName = null;
        var parameters = _read?.Invoke() ?? new List<(string, Type)>();
        for (int i = 0; i < parameters.Count; i++)
            _rows.Add(Row(parameters, i));

        if (parameters.Count == 0 && _add == null)
        {
            var none = new Label("No parameters");
            none.style.fontSize = 10;
            none.style.color = new Color(0.6f, 0.6f, 0.6f);
            _rows.Add(none);
        }

        if (_add != null)
        {
            _add.SetEnabled(parameters.Count < _max);
            _add.tooltip = parameters.Count < _max ? "Add a parameter: pick its type, then name it." : $"This Signal can't have more than {_max} parameter(s).";
        }
    }

    /// <summary>Puts the cursor in the first parameter's name, or on "Add Parameter…" when there are none.</summary>
    public void FocusFirst()
    {
        if (_firstName != null) _firstName.Focus();
        else _add?.Focus();
    }

    private VisualElement Row(List<(string name, Type type)> parameters, int index)
    {
        var (name, type) = parameters[index];
        var row = new VisualElement();
        row.AddToClassList("event-parameter-container");
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 1;

        var chip = new VisualElement();
        chip.style.width = 4;
        chip.style.height = 14;
        chip.style.marginRight = 4;
        chip.style.backgroundColor = DockRows.ColorOf(type);
        row.Add(chip);

        if (_typesEditable)
        {
            var typeButton = new Button { text = TypeLabel(type) + " ▾", tooltip = "The type of value this parameter carries." };
            typeButton.style.width = 84;
            typeButton.style.height = 18;
            typeButton.style.fontSize = 10;
            typeButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            typeButton.clicked += () =>
            {
                var menu = new GenericMenu();
                foreach (var (path, choice) in _types)
                {
                    var picked = choice;
                    menu.AddItem(new GUIContent(path), choice == type, () =>
                    {
                        if (picked == type) return;
                        var edited = _read();
                        if (index >= edited.Count) return;
                        edited[index] = (edited[index].name, picked);
                        Write(edited, null, null);
                    });
                }
                menu.DropDown(typeButton.worldBound);
            };
            row.Add(typeButton);
        }
        else
        {
            var typeLabel = new Label(TypeLabel(type));
            typeLabel.style.width = 70;
            typeLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
            row.Add(typeLabel);
        }

        if (_namesEditable)
        {
            var nameField = new TextField { value = name, isDelayed = true, tooltip = "The parameter's name (also its pin's name on the nodes). Press Enter or click away to apply." };
            nameField.style.flexGrow = 1;
            nameField.style.flexShrink = 1;
            nameField.style.minWidth = 40;
            nameField.style.marginLeft = 2;
            nameField.RegisterValueChangedCallback(evt =>
            {
                string wanted = evt.newValue?.Trim();
                var edited = _read();
                if (index >= edited.Count) return;
                string old = edited[index].name;
                if (string.IsNullOrEmpty(wanted) || wanted == old)
                {
                    nameField.SetValueWithoutNotify(old);
                    return;
                }
                wanted = Unique(wanted, edited.Where((_, i) => i != index).Select(p => p.name));
                edited[index] = (wanted, edited[index].type);
                Write(edited, old, wanted);
            });
            _firstName ??= nameField;
            row.Add(nameField);
        }
        else
        {
            var nameLabel = new Label(name);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.marginLeft = 2;
            row.Add(nameLabel);
        }

        if (_editable)
        {
            var remove = new Button(() =>
            {
                var edited = _read();
                if (index >= edited.Count) return;
                edited.RemoveAt(index);
                Write(edited, null, null);
            }) { text = "✕", tooltip = "Remove this parameter (its pin goes from the nodes that use the Signal)." };
            remove.style.width = 20;
            remove.style.height = 18;
            remove.style.paddingLeft = 0;
            remove.style.paddingRight = 0;
            row.Add(remove);
        }
        return row;
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
                Write(edited, null, null);
                // Straight into naming it.
                schedule.Execute(() => _rows.Query<TextField>().ToList().LastOrDefault()?.Focus());
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
