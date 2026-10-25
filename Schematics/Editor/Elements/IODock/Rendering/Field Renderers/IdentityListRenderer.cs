using Remedy.Framework;
using System.Collections;
using System;
using System.Linq;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

[FieldRendererTarget(typeof(IdentityListRendererAttribute))]
public class IdentityListContainerRenderer : FieldRenderer
{
    private IdentityListRendererAttribute _listAttr;

    private VisualElement _container;
    private LazyListFoldout _containerFoldout;

    private Color _borderShade = new Color(0, 0, 0, 0.25f);

    // Which items are open, kept across redraws (adding, removing or renaming an item redraws the list).
    private static readonly HashSet<string> _openItems = new();
    // The item just added, whose name is typed in place once it's drawn (-1: none).
    private int _renameIndex = -1;

    public IdentityListContainerRenderer(SchematicGraphEditorWindow window, UnityEngine.Object obj, object parent, object target, FieldOrPropertyInfo fieldInfo, CustomFieldRendererAttribute attr, string path, List<Action> onModified) : base(window, obj, parent, target, fieldInfo, attr, path, onModified)
    {
        // Changes are made straight on the object's current data (see ChangeList), so all that's left when this list or a
        // list inside it changes is making sure the object is saved. (This used to write the list as it was when it was
        // drawn back into the field: after Unity had reloaded the data, e.g. once an Input Action's binding was edited,
        // that put back old copies and lost the bindings and the Inputs' Signals.)
        _onModified.Add(() =>
        {
            if (_object != null)
                EditorUtility.SetDirty(_object);
            AttemptPrefabRefresh();
        });

        _listAttr = (IdentityListRendererAttribute)_attr;

        _container = new VisualElement();
        Add(_container);

        Redraw();
        SignalRenderer.DrawDelayedEvents();
    }

    private string ListPath => ManagedPath.Combine(_path, _field.Name);

    private string ItemKey(string elementPath) => (_object != null ? _object.GetHashCode() : 0) + ":" + elementPath;

    protected override void Redraw() => Redraw(false);

    private void Redraw(bool expanded)
    {
        if (_listAttr == null || _container == null) return;

        // Always drawn from the object's current list, not a copy kept from an earlier draw.
        var current = CurrentList();
        if (current != null)
            _value = current;
        if (_value == null)
            _value = Array.CreateInstance(ManagedPath.ElementType(_field.MemberType) ?? typeof(object), 0);

        _container.Clear();

        /// <summary>
        /// Creates a Lazy Loaded List Foldout with add/remove functionality
        /// </summary>
        /// <param name="collection">The collection to create this editor for</param>
        /// <param name="title">Title for the main foldout</param>
        /// <param name="onItemAdded">Callback when add button is clicked - should return the newly created item</param>
        /// <param name="onItemRemoved">Callback when remove button is clicked on an item</param>
        /// <param name="createItem">Factory for creating each actual Item Instance, passing an Action to call when Creation is complete</param>
        /// <param name="getItemName">Gets the name of the Item from the class that implements the List</param>
        _containerFoldout = new LazyListFoldout(collection: _value,
                                                    className: _listAttr.FoldoutTitle,
                                                    title: _listAttr.FoldoutTitle,

                                                    // List Modified
                                                    onItemAdded: (object item) =>
                                                    {
                                                        ChangeList("Add " + ItemLabel, items => { items.Add(item); return true; });
                                                        Redraw(true);
                                                    },
                                                    onItemRemoved: (object item) =>
                                                    {
                                                        ChangeList("Remove " + ItemLabel, items => items.Remove(item));
                                                        Redraw(true);
                                                    },

                                                    // Setup Item/VisualElements
                                                    createItemInstance: (Action<object> onCreationFinished) =>
                                                    {
                                                        var elementType = ManagedPath.ElementType(_field.MemberType) ?? _value.GetElementType();
                                                        var newItem = Activator.CreateInstance(elementType);
                                                        var identifier = IdentifierMember(elementType);

                                                        // A unique starting name ("New Map", "New Map 2"...), then typed over in place
                                                        // in the new item's header once it's drawn.
                                                        if (identifier != null && identifier.MemberType == typeof(string))
                                                        {
                                                            identifier.SetValue(newItem, UniqueName());
                                                            _renameIndex = _listAttr.Renamable ? (CurrentList()?.Count ?? 0) : -1;
                                                        }
                                                        onCreationFinished?.Invoke(newItem);
                                                    },
                                                    createItemContent: (int index, object item) =>
                                                    {
                                                        return CreateItemContent(index, item);
                                                    },
                                                    getItemName: (object item) =>
                                                    {
                                                        return GetItemName(item);
                                                    },
                                                    itemLabelClicked: (MouseDownEvent evt, object item, Action onComplete) =>
                                                    {
                                                        if (evt.clickCount == 2 && evt.button == 0)
                                                        {
                                                            int index = IndexOf(item);
                                                            if (index >= 0)
                                                                ChangeValue(PathManager.GetArrayPath(_path, _field, index), item, _containerFoldout, _ => onComplete?.Invoke());
                                                        }
                                                    },
                                                    startExpanded: expanded);


        _container.Add(_containerFoldout);
    }

    private string ItemLabel => string.IsNullOrEmpty(_listAttr.ItemLabel) ? "Item" : _listAttr.ItemLabel;

    private string GetItemName(object item)
    {
        var identifier = item != null ? IdentifierMember(item.GetType()) : null;
        var name = identifier?.GetValue(item)?.ToString();
        if (string.IsNullOrEmpty(name))
            name = "(unnamed)";
        if (!string.IsNullOrEmpty(_listAttr.ItemLabel))
            return _listAttr.ItemLabel + ": " + name;
        return name;
    }

    /// <summary>
    /// Each item is its own foldout: its header shows its name (double-click to rename) and a remove button, and its
    /// fields (e.g. an Input's binding and Signal) are inside it.
    /// </summary>
    private VisualElement CreateItemContent(int index, object item)
    {
        var elementPath = PathManager.GetArrayPath(_path, _field, index);
        // The item as the object holds it now (the list drawn may be an older copy).
        var current = ManagedPath.Resolve(_object, elementPath);
        if (current == null || current.GetType() != item?.GetType())
            current = item;

        string key = ItemKey(elementPath);
        var itemFoldout = new LazyFoldout(startExpanded: _openItems.Contains(key))
        {
            text = GetItemName(current)
        };
        itemFoldout.style.borderBottomWidth = 1;
        itemFoldout.style.borderBottomColor = _borderShade;

        itemFoldout.RegisterValueChangedCallback(evt =>
        {
            // Foldouts inside the item (its Signal's parameters...) send their changes up through this one too.
            if (evt.target != itemFoldout) return;
            if (evt.newValue) _openItems.Add(key);
            else _openItems.Remove(key);
        });

        var toggle = itemFoldout.Q<Toggle>();
        var header = toggle?.Children().FirstOrDefault();
        if (header != null)
        {
            header.AddToClassList("list-item-header");
            header.style.flexGrow = 1;

            var removeButton = new Button(() =>
            {
                ChangeList("Remove " + ItemLabel, items =>
                {
                    if (index < 0 || index >= items.Count) return false;
                    items.RemoveAt(index);
                    return true;
                });
                _openItems.Remove(key);
                Redraw(true);
            })
            {
                text = "-",
                tooltip = "Remove this " + ItemLabel + "."
            };
            removeButton.AddToClassList("list-button-remove");
            removeButton.RegisterCallback<MouseEnterEvent>(evt => removeButton.AddToClassList("list-button-remove-hover"));
            removeButton.RegisterCallback<MouseLeaveEvent>(evt => removeButton.RemoveFromClassList("list-button-remove-hover"));
            header.Add(removeButton);
        }

        // Double-click the item's title to rename it, as in the Prefab Hierarchy: read with pointer events on the title
        // label (the foldout's toggle takes mouse clicks for itself), and only when the attribute allows renaming.
        if (toggle != null && CanRename(current))
        {
            VisualElement title = toggle.Q<Label>();
            if (title == null)
                title = toggle;
            title.tooltip = "Double-click to rename.";
            title.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2)
                    return;
                evt.StopPropagation();
                ChangeValue(elementPath, current, toggle, newName => itemFoldout.text = newName);
            }, TrickleDown.TrickleDown);

            // Just added: its name typed in place right away.
            if (index == _renameIndex)
            {
                _renameIndex = -1;
                _openItems.Add(key);
                toggle.schedule.Execute(() => ChangeValue(elementPath, current, toggle, newName => itemFoldout.text = newName)).ExecuteLater(1);
            }
        }

        (bool drawFields, VisualElement fieldsContainer) = IODockRegistry.RenderMultipleFields(_window, _object, current, elementPath, onModified: _onModified);
        if (drawFields && fieldsContainer != null)
            itemFoldout.Add(fieldsContainer);

        return itemFoldout;
    }

    // Renames an item: a name is typed in place, in its header (the popup only to pick from a dropdown's options, or
    // where there's no header to type into).
    private void ChangeValue(string elementPath, object item, VisualElement anchor, Action<string> onItemEditted)
    {
        var title = anchor as Label ?? anchor?.Q<Label>();
        if (_listAttr.IdentifierType == ListIdentifierType.Name && title != null && title.parent != null)
        {
            var target = ManagedPath.Resolve(_object, elementPath) ?? item;
            string current = IdentifierMember(target.GetType())?.GetValue(target)?.ToString() ?? "";
            Remedy.Schematics.Templates.InlineRename.Begin(title, current, name => ApplyRename(elementPath, item, name, onItemEditted));
            return;
        }
        var identifierEditor = new InlineIdentifierEditor(popupRect: anchor.WorldBoundToScreen(),
                                                            identifierType: _listAttr.IdentifierType,
                                                            getOriginalValue: () =>
                                                            {
                                                                var target = ManagedPath.Resolve(_object, elementPath) ?? item;
                                                                return _listAttr.IdentifierType == ListIdentifierType.Name ? IdentifierMember(target.GetType())?.GetValue(target) : target;
                                                            },
                                                            onFinishedEditting: (bool change, object newValue) =>
                                                            {
                                                                if (newValue == null || (newValue is string name && string.IsNullOrWhiteSpace(name)))
                                                                    return;

                                                                // The popup reports from its OnDisable; the data is changed right after, as in the Prefab Hierarchy.
                                                                EditorApplication.delayCall += () => ApplyRename(elementPath, item, newValue, onItemEditted);
                                                            },
                                                            popupOptions: GetPopupOptions());
    }

    // Items are renamable when the attribute allows it and they have the identifier field it names.
    private bool CanRename(object item)
        => _listAttr != null && _listAttr.Renamable && item != null && IdentifierMember(item.GetType()) != null;

    private void ApplyRename(string elementPath, object item, object newValue, Action<string> onItemEditted)
    {
        // Renamed on the object's current data, so the change is kept.
        var target = ManagedPath.Resolve(_object, elementPath);
        if (target == null || target.GetType() != item.GetType())
            target = item;
        var identifier = IdentifierMember(target.GetType());
        if (identifier == null)
            return;

        if (newValue is string text)
            newValue = text.Trim();
        if (Equals(identifier.GetValue(target), newValue))
            return;

        if (_object != null)
            Undo.RecordObject(_object, "Rename " + ItemLabel);
        identifier.SetValue(target, newValue);
        OnModified();
        onItemEditted?.Invoke(GetItemName(target));
    }

    /// <summary>
    /// The list as the object holds it right now (looked up by its property path), or the field's value when it can't be
    /// looked up that way.
    /// </summary>
    private IList CurrentList()
    {
        if (_object != null && ManagedPath.Resolve(_object, ListPath) is IList live)
            return live;
        try { return _field.GetValue(LiveParent()) as IList; }
        catch { return null; }
    }

    // The object holding this list right now: found by its path from the Unity object when possible.
    private object LiveParent()
    {
        object resolved = string.IsNullOrEmpty(_path) ? _object : ManagedPath.Resolve(_object, _path);
        if (resolved != null && HasMember(resolved.GetType(), _field.Name))
            return resolved;
        return _parent;
    }

    private int IndexOf(object item)
    {
        var list = CurrentList();
        if (list == null) return -1;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], item))
                return i;
        return -1;
    }

    /// <summary>
    /// Changes the object's current list (recorded for Undo and saved), instead of writing back a copy of it.
    /// </summary>
    private void ChangeList(string undoName, Func<List<object>, bool> change)
    {
        var live = CurrentList();
        var items = live != null ? live.Cast<object>().ToList() : new List<object>();
        if (!change(items))
            return;

        var elementType = ManagedPath.ElementType(_field.MemberType);
        if (elementType == null)
            return;

        object collection;
        if (_field.MemberType.IsArray)
        {
            var array = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++)
                array.SetValue(items[i], i);
            collection = array;
        }
        else
        {
            var list = (IList)Activator.CreateInstance(_field.MemberType);
            foreach (var item in items)
                list.Add(item);
            collection = list;
        }

        if (_object != null)
            Undo.RecordObject(_object, undoName);
        _field.SetValue(LiveParent(), collection);
        _value = collection;
        OnModified();
    }

    private string UniqueName()
    {
        var taken = new HashSet<string>();
        var list = CurrentList();
        if (list != null)
            foreach (var existing in list)
                if (existing != null && IdentifierMember(existing.GetType())?.GetValue(existing) is string name)
                    taken.Add(name);

        string wanted = "New " + ItemLabel;
        for (int i = 2; taken.Contains(wanted); i++)
            wanted = "New " + ItemLabel + " " + i;
        return wanted;
    }

    private FieldOrPropertyInfo IdentifierMember(Type type)
        => type != null && HasMember(type, _listAttr.Identifier) ? type.GetFieldOrProperty(_listAttr.Identifier) : null;

    private static bool HasMember(Type type, string name)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        return !string.IsNullOrEmpty(name) && (type.GetField(name, flags) != null || type.GetProperty(name, flags) != null);
    }

    private AnyCollection GetPopupOptions()
    {
        if (_listAttr.Options == null) return null;

        var optionsProp = _parent.GetType().GetFieldOrProperty(_listAttr.Options);

        if (optionsProp == null) return null;

        var optionsObj = optionsProp.GetValue(_parent);

        // Search for list options
        if (_listAttr.Options.StartsWith("./"))
        {
            optionsObj = _object.GetType().GetProperty(_listAttr.Options.Replace("./", ""))?.GetValue(_object);
        }
        if (optionsObj == null)
        {
            optionsObj = _parent.GetType().GetMethod(_listAttr.Options)?.Invoke(_parent, null);
        }

        // create popupfield
        if (optionsObj != null)
        {
            var enumerable = optionsObj as IEnumerable;
            var optionList = enumerable?.Cast<object>().ToList();

            var defaultValue = optionList.FirstOrDefault();

            if (optionList != null && optionList.Count > 0)
            {
                return new AnyCollection(optionList);
            }
        }
        return null;
    }
}
