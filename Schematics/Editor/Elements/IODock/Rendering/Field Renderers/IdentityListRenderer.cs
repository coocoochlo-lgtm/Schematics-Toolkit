using Remedy.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

/// <summary>
/// Draws a list (array or List) whose items are named by one of their fields, like Remedy Input's Input Maps and
/// their Inputs: each item shows its name, editable in place (it applies on Enter or when you click away), a ✕ to
/// remove it, and its own fields under it (shown when you open it). "Add …" under the list adds one.
/// Every change looks the list up again from the object that owns it and is recorded for Undo, so it never writes
/// back an outdated copy of the data (which used to wipe the Input Actions' bindings).
/// </summary>
[FieldRendererTarget(typeof(IdentityListRendererAttribute))]
public class IdentityListContainerRenderer : FieldRenderer
{
    private IdentityListRendererAttribute _listAttr;
    private string _listPath;
    private Foldout _foldout;
    private VisualElement _items;

    public IdentityListContainerRenderer(SchematicGraphEditorWindow window, UnityEngine.Object obj, object parent, object target, FieldOrPropertyInfo fieldInfo, CustomFieldRendererAttribute attr, string path, List<Action> onModified) : base(window, obj, parent, target, fieldInfo, attr, path, onModified)
    {
    }

    private string ItemLabel => string.IsNullOrEmpty(_listAttr.ItemLabel) ? "Item" : _listAttr.ItemLabel;
    private string Title => string.IsNullOrEmpty(_listAttr.FoldoutTitle) ? ObjectNames.NicifyVariableName(_field.Name) : _listAttr.FoldoutTitle;
    private string StateKey(string path) => $"IODockList.{(_object != null ? _object.GetHashCode() : 0)}.{path}";

    protected override void Redraw()
    {
        _listAttr = _attr as IdentityListRendererAttribute;
        if (_listAttr == null)
            return;
        _listPath = ManagedPath.Combine(_path, _field.Name);

        Clear();
        _foldout = new Foldout { text = Title, value = SessionState.GetBool(StateKey(_listPath), true) };
        _foldout.AddToClassList("identity-list");
        _foldout.style.marginTop = 2;
        _foldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == _foldout)
                SessionState.SetBool(StateKey(_listPath), evt.newValue);
        });

        _items = new VisualElement();
        _foldout.Add(_items);

        var add = new Button(AddItem) { text = $"+ Add {ItemLabel}", tooltip = $"Add a new {ItemLabel}." };
        add.style.alignSelf = Align.FlexStart;
        add.style.height = 18;
        add.style.fontSize = 10;
        add.style.marginTop = 2;
        _foldout.Add(add);

        Add(_foldout);
        RebuildItems();

        // Undo / Redo of adding, removing or renaming items: show the restored list.
        RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
        RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= OnUndoRedo);
    }

    // Only when the list itself changed (count or names), so undoing a binding edit doesn't rebuild the Input Actions.
    private string _shape;
    private void OnUndoRedo()
    {
        string shape = Shape();
        if (shape != _shape)
            RebuildItems();
    }

    private string Shape()
    {
        var list = CurrentList();
        if (list == null) return "";
        var names = new List<string>();
        foreach (var item in list)
            names.Add(item == null ? "" : Member(item.GetType(), _listAttr.Identifier)?.GetValue(item)?.ToString() ?? "");
        return list.Count + "|" + string.Join("|", names);
    }

    private IList CurrentList() => ManagedPath.Resolve(_object, _listPath) as IList;

    private void RebuildItems()
    {
        _shape = Shape();
        _items.Clear();
        var list = CurrentList();
        if (list == null || list.Count == 0)
        {
            var hint = new Label($"No {Title} yet.");
            hint.style.fontSize = 10;
            hint.style.color = new Color(0.6f, 0.6f, 0.6f);
            hint.style.marginLeft = 4;
            _items.Add(hint);
            return;
        }
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null)
                _items.Add(ItemElement(i, list[i]));
    }

    private VisualElement ItemElement(int index, object item)
    {
        string elementPath = $"{_listPath}.Array.data[{index}]";

        var box = new VisualElement();
        box.AddToClassList("identity-list-item");
        box.style.marginTop = 2;
        box.style.marginBottom = 2;
        box.style.paddingLeft = 4;
        box.style.paddingRight = 2;
        box.style.paddingTop = 2;
        box.style.paddingBottom = 2;
        box.style.borderLeftWidth = 2;
        box.style.borderLeftColor = _listAttr.Depth % 2 == 0 ? new Color(0.45f, 0.55f, 0.9f, 0.6f) : new Color(0.9f, 0.6f, 0.35f, 0.6f);
        box.style.backgroundColor = new Color(0, 0, 0, 0.08f + 0.05f * _listAttr.Depth);
        box.style.borderTopLeftRadius = box.style.borderBottomLeftRadius = 3;

        var content = new VisualElement();
        content.style.marginLeft = 8;
        // Open by default: drawing an item is what gives Signal fields their Signal asset.
        bool open = SessionState.GetBool(StateKey(elementPath), true);
        bool rendered = false;

        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;

        var expand = new Button { text = open ? "▾" : "▸", tooltip = "Show or hide its settings." };
        expand.style.width = 18;
        expand.style.height = 18;
        expand.style.paddingLeft = expand.style.paddingRight = 0;
        expand.style.marginLeft = 0;
        expand.style.backgroundColor = Color.clear;
        expand.style.borderLeftWidth = expand.style.borderRightWidth = expand.style.borderTopWidth = expand.style.borderBottomWidth = 0;
        header.Add(expand);

        var label = new Label(ItemLabel);
        label.style.minWidth = 40;
        label.style.color = new Color(0.7f, 0.7f, 0.7f);
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.Add(label);

        header.Add(IdentifierField(elementPath, item));

        var remove = new Button(() => RemoveItem(index)) { text = "✕", tooltip = $"Remove this {ItemLabel}." };
        remove.style.width = 20;
        remove.style.height = 18;
        remove.style.paddingLeft = remove.style.paddingRight = 0;
        header.Add(remove);

        box.Add(header);
        box.Add(content);

        // Its fields are only built when it's first opened (Input Actions draw an IMGUI tree each).
        void Show(bool visible)
        {
            expand.text = visible ? "▾" : "▸";
            content.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible && !rendered)
            {
                rendered = true;
                var current = ManagedPath.Resolve(_object, elementPath) ?? item;
                var (draw, fields) = IODockRegistry.RenderMultipleFields(_window, _object, current, elementPath, onModified: _onModified);
                if (draw && fields != null)
                    content.Add(fields);
            }
        }
        expand.clicked += () =>
        {
            open = !open;
            SessionState.SetBool(StateKey(elementPath), open);
            Show(open);
        };
        Show(open);
        return box;
    }

    // The item's name: a text field (or a dropdown, for lists that pick from options), applied on Enter / click away.
    private VisualElement IdentifierField(string elementPath, object item)
    {
        var member = Member(item.GetType(), _listAttr.Identifier);
        object value = member?.GetValue(item);

        var options = _listAttr.IdentifierType == ListIdentifierType.Dropdown ? GetPopupOptions() : null;
        if (options != null && options.Count > 0)
        {
            var current = options.Contains(value) ? value : options[0];
            var popup = new PopupField<object>(options, current, o => o?.ToString() ?? "(none)", o => o?.ToString() ?? "(none)");
            popup.style.flexGrow = 1;
            popup.RegisterValueChangedCallback(evt => SetIdentifier(elementPath, evt.newValue));
            return popup;
        }

        var field = new TextField { value = value?.ToString() ?? "", isDelayed = true, tooltip = $"The {ItemLabel}'s name. Press Enter or click away to apply." };
        field.style.flexGrow = 1;
        field.style.flexShrink = 1;
        field.style.minWidth = 40;
        field.RegisterValueChangedCallback(evt =>
        {
            string name = evt.newValue?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                field.SetValueWithoutNotify(evt.previousValue);
                return;
            }
            SetIdentifier(elementPath, name);
        });
        return field;
    }

    private void SetIdentifier(string elementPath, object value)
    {
        var item = ManagedPath.Resolve(_object, elementPath);
        var member = (item != null ? Member(item.GetType(), _listAttr.Identifier) : null);
        if (member == null)
            return;
        Undo.RecordObject(_object, $"Rename {ItemLabel}");
        member.SetValue(item, value);
        EditorUtility.SetDirty(_object);
        _shape = Shape(); // so undoing the rename is seen as a change
        OnModified();
        AttemptPrefabRefresh();
    }

    private void AddItem()
    {
        var list = CurrentList();
        var elementType = ManagedPath.ElementType(_field.MemberType);
        if (elementType == null)
            return;

        object item;
        try { item = Activator.CreateInstance(elementType); }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return;
        }

        // A unique starting name ("New Map", "New Map 2"...), so it can be told apart straight away.
        var member = Member(elementType, _listAttr.Identifier);
        if (member != null && member.MemberType == typeof(string))
        {
            var taken = new HashSet<string>();
            if (list != null)
                foreach (var existing in list)
                    if (existing != null && member.GetValue(existing) is string name)
                        taken.Add(name);
            string wanted = "New " + ItemLabel;
            for (int i = 2; taken.Contains(wanted); i++)
                wanted = $"New {ItemLabel} {i}";
            member.SetValue(item, wanted);
        }

        Undo.RecordObject(_object, $"Add {ItemLabel}");
        int index;
        if (_field.MemberType.IsArray)
        {
            int count = list?.Count ?? 0;
            var array = Array.CreateInstance(elementType, count + 1);
            for (int i = 0; i < count; i++)
                array.SetValue(list[i], i);
            array.SetValue(item, count);
            if (!ManagedPath.Assign(_object, _listPath, array))
                return;
            index = count;
        }
        else
        {
            if (list == null)
            {
                list = (IList)Activator.CreateInstance(_field.MemberType);
                if (!ManagedPath.Assign(_object, _listPath, list))
                    return;
            }
            index = list.Add(item);
        }
        EditorUtility.SetDirty(_object);

        // Open the new one, ready to fill in.
        SessionState.SetBool(StateKey($"{_listPath}.Array.data[{index}]"), true);
        OnModified();
        AttemptPrefabRefresh();
        RebuildItems();
    }

    private void RemoveItem(int index)
    {
        var list = CurrentList();
        if (list == null || index < 0 || index >= list.Count)
            return;

        int remaining = list.Count - 1;
        Undo.RecordObject(_object, $"Remove {ItemLabel}");
        if (_field.MemberType.IsArray)
        {
            var elementType = ManagedPath.ElementType(_field.MemberType);
            var array = Array.CreateInstance(elementType, list.Count - 1);
            for (int i = 0, j = 0; i < list.Count; i++)
                if (i != index)
                    array.SetValue(list[i], j++);
            ManagedPath.Assign(_object, _listPath, array);
        }
        else
            list.RemoveAt(index);
        EditorUtility.SetDirty(_object);

        // The open/closed state of the items after it moves up with them.
        for (int i = index; i < remaining; i++)
            SessionState.SetBool(StateKey($"{_listPath}.Array.data[{i}]"), SessionState.GetBool(StateKey($"{_listPath}.Array.data[{i + 1}]"), true));

        OnModified();
        AttemptPrefabRefresh();
        RebuildItems();
    }

    // GetFieldOrProperty throws when there's no such member; here that just means "none".
    private static FieldOrPropertyInfo Member(Type type, string name)
    {
        if (type == null || string.IsNullOrEmpty(name)) return null;
        try { return type.GetFieldOrProperty(name); }
        catch (ArgumentException) { return null; }
    }

    private List<object> GetPopupOptions()
    {
        if (string.IsNullOrEmpty(_listAttr.Options))
            return null;
        try
        {
            object optionsObj = null;
            if (_listAttr.Options.StartsWith("./"))
                optionsObj = _object.GetType().GetProperty(_listAttr.Options.Replace("./", ""))?.GetValue(_object);
            else
            {
                var owner = ManagedPath.Resolve(_object, _path) ?? _parent;
                optionsObj = (owner != null ? Member(owner.GetType(), _listAttr.Options)?.GetValue(owner) : null)
                          ?? owner?.GetType().GetMethod(_listAttr.Options)?.Invoke(owner, null);
            }
            return (optionsObj as IEnumerable)?.Cast<object>().ToList();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return null;
        }
    }
}
