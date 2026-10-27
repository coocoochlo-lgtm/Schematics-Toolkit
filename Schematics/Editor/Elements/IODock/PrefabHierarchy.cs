using Remedy.Framework;
using Remedy.Schematics;
using SchematicAssets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A custom Unity UI Toolkit element that displays a prefab's GameObjects and their Schematic Behaviors in a foldout hierarchy 
/// with interactive input/output docking for visual programming.
/// </summary>
/// <remarks>
/// Designed for use in editor extensions like <see cref="SchematicGraphEditorWindow"/>. 
/// Provides prefab visualization with Behavior tabs, docking interfaces, and undo/redo support.<br/>
/// The Behaviors of each GameObject come from the prefab root's <see cref="SchematicObject"/> (its Behaviors list).<br/>
/// <br/>
/// Features:<br/>
/// - Foldout hierarchy with persistent state<br/>
/// - Behavior tabs with add/remove support<br/>
/// - I/O docking via <see cref="IODockRegistry"/><br/>
/// - Selection synchronization with Unity's inspector<br/>
/// - Lazy loading for performance optimization<br/>
/// - Cached Behavior and type data<br/>
/// </remarks>
public class PrefabHierarchy : VisualElement
{
    private GameObject _prefab;
    private string _assetPath;
    SerializableDictionary<Transform, SerializableDictionary<SerializableType, VisualElement>> evMap = new();

    // Caching structures
    private static List<Type> _availableBehaviorTypes;
    private static bool _behaviorTypesInitialized = false;

    // One instance of each Behavior type, for the IO Dock's reflection (Behaviors aren't Unity Objects).
    private static readonly Dictionary<Type, SchematicBehavior> _behaviorInstances = new();

    // The window's "Show Component Names" toggle is gone; tabs show their icon only, as by default, or the name when the
    // Behavior has no icon of its own.
    private const bool HideTabLabels = true;

    private SchematicGraphEditorWindow _schematicWindow;

    /// <summary>
    /// Creates a new prefab hierarchy view.
    /// </summary>
    /// <param name="prefab">Prefab root GameObject to display.</param>
    /// <param name="editorWindow">Editor window hosting the view, used for state and selection handling.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="prefab"/> or <paramref name="editorWindow"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="prefab"/> is not a valid prefab instance.</exception>
    public PrefabHierarchy(GameObject prefab, EditorWindow editorWindow)
    {
        _prefab = prefab;
        _assetPath = AssetDatabase.GetAssetPath(prefab);

        if (editorWindow is SchematicGraphEditorWindow asSchematicWindow)
        {
            _schematicWindow = asSchematicWindow;
        }

        // No reimport here: a forced reimport throws away whatever of the prefab is only in memory (e.g. the graph link the
        // window sets), and resetting the Prefab Mode stage discards its unsaved changes.
        RebuildHierarchy(_prefab.transform);

        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            _watch ??= schedule.Execute(Watch).Every(WatchMs);
            _watch.Resume();
            Selection.selectionChanged += UpdateHighlight;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            UpdateHighlight();
        });
        RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            _watch?.Pause();
            Selection.selectionChanged -= UpdateHighlight;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        });
    }


    public void RebuildHierarchy(Transform transform = null)
    {
        Clear();
        _objectHeaders.Clear();
        // Copies of list and class Variables are made again from the prefab (but those with an edit still waiting).
        foreach (var key in _boxedEdits.Keys.ToList())
            if (!_pendingEdits.ContainsKey(key))
            {
                if (_boxedEdits[key].holder != null)
                    UnityEngine.Object.DestroyImmediate(_boxedEdits[key].holder);
                _boxedEdits.Remove(key);
            }

        // Always the prefab asset: every change is made through the load-edit-save below.
        if (_prefab == null)
            _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
        _shown = _prefab;
        if (_shown == null)
            return;

        Add(new HierarchyTreeView<Transform>(new[] { _shown.transform },
            tf => tf.GetComponentsInImmediateChildren<Transform>(),
            PathOf,
            tf => CreateGameObjectHeader(tf, _schematicWindow),
            _assetPath + "|Prefab Tree"));

        _signature = Signature();
    }

    // Into or out of Play Mode: drawn again, the Variables showing live values or starting ones.
    private void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
            RebuildHierarchy();
    }

    #region Following changes made elsewhere

    // The object the hierarchy shows: the prefab asset.
    private GameObject _shown;

    private const long WatchMs = 500;
    private IVisualElementScheduledItem _watch;
    private int _signature;

    // The tab open in each row of tabs (by prefab, object, Behavior and level), so a rebuild doesn't fold them up.
    private static readonly Dictionary<string, string> _openTabs = new();

    private string TabsKey(string path, string level) => $"{_assetPath}|{path}|{level}";

    // Opens the tab that was open last time (or <paramref name="first"/> the first time), and remembers what's opened.
    private static void RememberOpen(TabView tabs, string key, string first = null)
    {
        if (_openTabs.TryGetValue(key, out var open))
        {
            if (open != null && tabs.HasTab(open))
                tabs.SelectTab(open);
        }
        else if (first != null && tabs.HasTab(first))
            tabs.SelectTab(first);
        tabs.SelectionChanged += id => _openTabs[key] = id;
    }

    private string PathOf(Transform tf) => _shown != null && tf != null ? _shown.transform.GetRelativePath(tf) : "";

    // A child just added, whose name is typed in place once its row is drawn (null: none).
    private static string _renamePath;

    /// <summary>
    /// Twice a second: only redraws (never edits) when the prefab asset's objects or attached Behaviors changed
    /// elsewhere, e.g. a Prefab Mode save. Values aren't watched, so typing in a field never rebuilds.
    /// </summary>
    private void Watch()
    {
        if (_editing || panel == null || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        if (_shown == null || Signature() != _signature)
            RebuildHierarchy();
    }

    // A cheap fingerprint of the shown hierarchy (names, order) and of which Behaviors are attached where.
    private int Signature()
    {
        if (_shown == null)
            return 0;
        unchecked
        {
            int hash = 17;
            foreach (var t in _shown.GetComponentsInChildren<Transform>(true))
            {
                hash = hash * 31 + (t.name?.GetHashCode() ?? 0);
                hash = hash * 31 + t.GetSiblingIndex();
                hash = hash * 31 + t.childCount;
            }
            var root = _shown.GetComponent<SchematicObject>();
            if (root != null && root.Behaviors != null)
                foreach (var entry in root.Behaviors)
                {
                    hash = hash * 31 + (entry.Key != null ? entry.Key.GetHashCode() : 7);
                    if (entry.Value?.Items == null)
                        continue;
                    foreach (var attachment in entry.Value.Items)
                        hash = hash * 31 + (attachment?.Metadata != null ? attachment.Metadata.GetHashCode() : 3);
                }
            return hash;
        }
    }

    #endregion

    /// <summary>Creates the caller-specific header for one node in the shared hierarchy tree.</summary>
    private VisualElement CreateGameObjectHeader(Transform tf, SchematicGraphEditorWindow schematicWindow)
    {
        if (tf == null)
            return null;
        var itemContainer = new VisualElement();
        itemContainer.style.flexDirection = FlexDirection.Column;
        itemContainer.style.width = Length.Percent(100);
        var objectHeader = new VisualElement();
        objectHeader.AddToClassList("list-toggle-container");
        objectHeader.AddToClassList("dock-object-header");
        objectHeader.style.flexDirection = FlexDirection.Row;
        objectHeader.style.alignItems = Align.Center;
        objectHeader.style.width = Length.Percent(100);
        var icon = new Image
        {
            image = EditorGUIUtility.IconContent("GameObject Icon").image as Texture2D,
            scaleMode = ScaleMode.ScaleToFit,
        };
        icon.style.width = icon.style.height = 16;
        icon.style.marginRight = 4;
        objectHeader.Add(icon);
        var label = new Label(tf.name);
        label.AddToClassList("list-label");
        label.style.flexGrow = 1;
        objectHeader.Add(label);

        var objectPath = PathOf(tf);
        var objectItem = new DockItem { Kind = DockItem.ItemKind.GameObject, Path = objectPath, GameObject = tf.gameObject };
        foreach (var handle in new VisualElement[] { label, icon })
            DockRows.MakeDragSource(handle, objectItem, () => SelectObject(objectPath), stopPress: true);
        icon.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        label.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.clickCount != 2)
                return;
            evt.StopPropagation();
            RenameObject(tf, label);
        });
        label.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        if (_renamePath != null && objectPath == _renamePath)
        {
            _renamePath = null;
            label.schedule.Execute(() => RenameObject(tf, label)).ExecuteLater(1);
        }

        var addChild = new Button(() => AddChildObject(tf)) { text = "+", tooltip = "Add a child GameObject" };
        addChild.AddToClassList("list-button-add");
        addChild.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        addChild.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        objectHeader.Add(addChild);
        if (tf != _shown.transform)
        {
            var removeObject = new Button(() => DeleteObject(tf)) { text = "×", tooltip = "Delete this GameObject" };
            removeObject.AddToClassList("hierarchy-remove-button");
            removeObject.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            removeObject.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            objectHeader.Add(removeObject);
        }
        objectHeader.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
        {
            evt.menu.AppendAction("Add Child", _ => AddChildObject(tf));
            evt.menu.AppendAction("Rename", _ => RenameObject(tf, label));
            if (tf != _shown.transform)
                evt.menu.AppendAction("Delete Object", _ => DeleteObject(tf));
        });

        _objectHeaders.Add((objectHeader, objectPath));
        objectHeader.EnableInClassList("dock-object-selected", SelectedPath() == objectPath);
        objectHeader.EnableInClassList("dock-object-header--selected", SelectedPath() == objectPath);
        itemContainer.Add(objectHeader);
        var componentRow = new VisualElement();
        componentRow.AddToClassList("component-row");
        componentRow.Add(CreateFoldoutContent(tf, schematicWindow));
        itemContainer.Add(componentRow);
        return itemContainer;
    }

    private void AddChildObject(Transform parentTransform)
    {
        if (parentTransform == null)
            return;
        var parentPath = PathOf(parentTransform);
        string name = "GameObject";
        for (int n = 2; parentTransform.Find(name) != null; n++)
            name = "GameObject " + n;
        _renamePath = string.IsNullOrEmpty(parentPath) ? name : parentPath + "/" + name;
        EditorApplication.delayCall += () => EditPrefab("Add Child", (root, _) =>
        {
            var parent = FindInCopy(root, parentPath);
            if (parent == null)
                return;
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
        });
    }

    private void DeleteObject(Transform transform)
    {
        if (transform == null || transform == _shown.transform)
            return;
        if (!EditorUtility.DisplayDialog("Delete Object",
            $"Delete {transform.name}{(transform.childCount > 0 ? " and everything under it" : "")}, with its Schematic Behaviors?",
            "Delete", "Cancel"))
            return;
        var path = PathOf(transform);
        EditPrefab("Delete Object", (root, _) =>
        {
            var target = FindInCopy(root, path);
            if (target == null || target == root.transform)
                return;
            var schematic = root.GetComponent<SchematicObject>();
            if (schematic != null)
                foreach (var child in target.GetComponentsInChildren<Transform>(true))
                    schematic.Behaviors.Remove(child.gameObject);
            GameObject.DestroyImmediate(target.gameObject);
        });
    }

    // An object's Behaviors, as a row of tabs (each with its script's icon) that open like a dropdown: click one to show
    // it under the row, click it again to fold it away. Drag a tab into the graph for a reference to the Behavior.
    private VisualElement CreateFoldoutContent(Transform tf, SchematicGraphEditorWindow schematicWindow)
    {
        var behaviorsOnObject = GetBehaviors(tf.gameObject);
        string path = PathOf(tf);

        var container = new VisualElement();
        container.AddToClassList("component-tabs");
        // (It sits in the object's foldout header: clicks in it don't fold the object.)
        StopClicks(container);

        TabView tabView = new("", "components", true, HideTabLabels, collapsible: true);

        foreach (var attachment in behaviorsOnObject)
        {
          // One bad Behavior (a renamed script, a throwing renderer) doesn't hide the others.
          try
          {
            var type = attachment.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
            var typeName = type != null ? ObjectNames.NicifyVariableName(type.Name)
                         : attachment.Metadata != null ? $"{attachment.Metadata.name} (Missing Script)" : "(Missing Behavior)";
            var (icon, ownIcon) = type != null ? GetBehaviorIcon(type) : (null, false);

            // Tab ids must be unique.
            while (tabView.HasTab(typeName))
                typeName += " ";

            // Its own icon alone; the generic script icon with its name beside it, so they can be told apart.
            var tab = tabView.AddTab(typeName, icon, showLabel: !ownIcon);
            tab.UseFactory(() =>
            {
                try
                {
                    return CreateTabContent(attachment, type, tf, schematicWindow);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, _shown);
                    return new HelpBox($"{typeName.Trim()} couldn't be drawn: {exception.Message}", HelpBoxMessageType.Error);
                }
            });

            // Right-click the tab to remove the Behavior from this GameObject.
            var behaviorType = type;
            var behaviorAttachment = attachment;
            tab.Button.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Remove Behavior", _ => RemoveBehavior(behaviorType, behaviorAttachment, tf));
            }));
            if (type != null)
                DockRows.MakeDragSource(tab.Button, new DockItem { Kind = DockItem.ItemKind.Behavior, Behavior = type, Path = path });
          }
          catch (Exception exception)
          {
              Debug.LogException(exception, _shown);
          }
        }

        // "+": adds a Behavior.
        CreateAddBehaviorButton(tabView, tf);

        RememberOpen(tabView, TabsKey(path, "components"));
        container.Add(tabView);
        return container;
    }

    // Presses and clicks inside go no further (to a foldout header around it).
    private static void StopClicks(VisualElement element)
    {
        element.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<MouseUpEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
    }

    /// <summary>
    /// A Behavior tab's content: its name, then its Variables, Signals and Properties (and anything it adds itself) as a
    /// row of tabs of their own, each made only when it's first opened, and folding away like the Behavior tabs.
    /// </summary>
    private VisualElement CreateTabContent(BehaviorAttachment attachment, Type type, Transform tf, SchematicGraphEditorWindow schematicWindow)
    {
        var dock = new VisualElement();
        var metadata = attachment.Metadata;

        var label = new Label(type != null ? ObjectNames.NicifyVariableName(type.Name) : metadata != null ? $"{metadata.name} (Missing Script)" : "(Missing Behavior)");
        label.AddToClassList("dock-behavior-name");
        dock.Add(label);

        if (type == null || metadata == null)
            return dock;

        string path = PathOf(tf);
        var reflection = BehaviorReflection.Get(type);
        bool hasVariables = metadata.Variables.Any(v => v != null && (reflection == null || !reflection.IsPropertiesVariable(v.Name)));
        var propertiesVariables = GetPropertiesVariables(type, metadata);
        // Anything the Behavior adds itself (BehaviorDockSections: the Brain's slots, a vehicle's seats).
        var extra = BehaviorDockSections.Create(new BehaviorDockContext
        {
            Prefab = _prefab, Target = tf.gameObject, Path = path, BehaviorType = type, Attachment = attachment, Window = schematicWindow,
            Read = (variable, valueType) => CurrentValue(path, type, attachment, metadata.FindVariable(variable), valueType),
            Write = (variable, valueType, value) => QueueEdit(path, type, variable, valueType, value),
        });

        var sections = new TabView("", "sections", false, hideTabLabels: true, collapsible: true);
        sections.SelectionChanged += open => sections.Caption = open;
        if (hasVariables)
            sections.AddTab("Variables", SchematicTabIcons.Get("dock-variables"), showLabel: false)
                .UseFactory(() => CreateVariablesSection(attachment, type, tf, metadata, schematicWindow) ?? new VisualElement());
        if (metadata.Signals != null && metadata.Signals.Count > 0)
            sections.AddTab("Signals", SchematicTabIcons.Get("dock-signals"), showLabel: false).UseFactory(() =>
            {
                var (draw, dockElement) = RenderBehaviorDock(type, schematicWindow, metadata);
                SignalRenderer.DrawDelayedEvents();
                return draw && dockElement != null ? dockElement : new Label("No Signals to show.") { style = { opacity = 0.6f } };
            });
        if (propertiesVariables.Count > 0)
            sections.AddTab("Properties", SchematicTabIcons.Get("dock-properties"), showLabel: false)
                .UseFactory(() => CreatePropertiesSection(attachment, type, propertiesVariables, tf));
        if (extra != null)
            sections.AddTab("More", SchematicTabIcons.Get("dock-more"), showLabel: false).Add(extra);

        if (sections.TabCount == 0)
            return dock;
        // The first time: the first section open.
        RememberOpen(sections, TabsKey(path, type.FullName + "|sections"), hasVariables ? "Variables" : null);
        if (sections.SelectedTab == null && !_openTabs.ContainsKey(TabsKey(path, type.FullName + "|sections")))
            foreach (var first in new[] { "Signals", "Properties", "More" })
                if (sections.HasTab(first))
                {
                    sections.SelectTab(first);
                    break;
                }
        sections.Caption = sections.SelectedTab;
        dock.Add(sections);
        return dock;
    }

    #region Variables

    /// <summary>
    /// A Behavior's Variables (but its Properties, which have their own foldout): one row each, which drags into the
    /// graph as a Get or Set node, with this object's value on it (edited here, saved into the prefab shortly after).
    /// </summary>
    private VisualElement CreateVariablesSection(BehaviorAttachment attachment, Type type, Transform tf, BehaviorMetadata metadata, SchematicGraphEditorWindow window)
    {
        var reflection = BehaviorReflection.Get(type);
        string path = PathOf(tf);
        VisualElement section = null;
        foreach (var variable in metadata.Variables)
        {
            if (variable == null || (reflection != null && reflection.IsPropertiesVariable(variable.Name)))
                continue;
            if (section == null)
            {
                section = new VisualElement();
                // Leaving (a redraw, another prefab) saves what's waiting.
                section.RegisterCallback<DetachFromPanelEvent>(_ => FlushEdits());
            }
            var item = new DockItem
            {
                Kind = DockItem.ItemKind.Variable, Path = path, Behavior = type, GameObject = tf.gameObject,
                Name = variable.Name, ValueType = variable.Type.Type,
            };
            var row = DockRows.Build(item, VariableIcon, ObjectNames.NicifyVariableName(variable.Name),
                DockRows.TypeName(item.ValueType) + (variable.Networked ? "  ⇅" : ""),
                "Variable: drag the name into the graph to Get or Set it; click it to find its nodes. The field is this object's starting value.",
                () =>
                {
                    window?.FocusNodes(item);
                    ScopeItemInspector.Show(item);
                });
            var definition = variable;
            // In Play Mode: the live value of the object the graph is on (edited live, as the Inspector's are).
            if (Application.isPlaying)
            {
                IMGUIContainer live = null;
                live = new IMGUIContainer(() => DrawLiveVariable(path, type, definition, live));
                live.style.flexGrow = 1;
                live.style.flexShrink = 1;
                live.style.minWidth = 60;
                row.Add(live);
                section.Add(row);
                continue;
            }
            // Arrays, lists and classes: edited right under the row, at full width, with Unity's own drawers (a list's
            // add, remove and reorder; each item by its name).
            // Lists of items that hold Signals (a Weapon Holder's Weapon Inputs): each item's fields, and its Signals as
            // Signal rows to drag into the graph (to send them to the Behavior), as before it was a Behavior.
            if (variable.Type.Type != null && HoldsSignals(variable.Type.Type))
            {
                section.Add(row);
                section.Add(SignalItemsBody(path, type, attachment, definition));
                continue;
            }
            if (variable.Type.Type != null && SchematicValue.IsBoxable(variable.Type.Type))
            {
                section.Add(row);
                var body = new IMGUIContainer(() => DrawBoxedValue(path, type, attachment, definition));
                body.style.marginLeft = 22;
                body.style.marginRight = 4;
                body.style.marginBottom = 3;
                section.Add(body);
                continue;
            }
            // A native field where there is one (much cheaper than an IMGUI field per row), with the button back to the
            // default; IMGUI for the rest.
            if (NativeVariableField(path, type, attachment, definition) is { } native)
                row.Add(native);
            else
            {
                var field = new IMGUIContainer(() => DrawVariableField(path, type, attachment, definition));
                field.style.flexGrow = 1;
                field.style.flexShrink = 1;
                field.style.minWidth = 60;
                row.Add(field);
            }
            section.Add(row);
        }
        return section;
    }

    // A Variable value waiting to be written to the prefab: shown straight away, written with the next load-edit-save
    // once typing pauses (each save reimports the prefab, so not on every keystroke).
    private sealed class PendingEdit
    {
        public string Path;
        public Type Behavior;
        public string Variable;
        public Type ValueType;
        public object Value;
        public bool Revert;
    }

    private readonly Dictionary<string, PendingEdit> _pendingEdits = new();
    private IVisualElementScheduledItem _flush;
    private const long FlushDelayMs = 1200;

    private static string EditKey(string path, Type behavior, string variable) => $"{path}|{behavior?.FullName}|{variable}";

    private void QueueEdit(string path, Type behavior, string variable, Type valueType, object value, bool revert = false)
    {
        _pendingEdits[EditKey(path, behavior, variable)] = new PendingEdit
        {
            Path = path, Behavior = behavior, Variable = variable, ValueType = valueType, Value = value, Revert = revert,
        };
        // Restarted by every change, so typing finishes before anything is written.
        _flush?.Pause();
        _flush = schedule.Execute(FlushEdits);
        _flush.ExecuteLater(FlushDelayMs);
    }

    private void FlushEdits()
    {
        _flush?.Pause();
        if (_pendingEdits.Count == 0 || string.IsNullOrEmpty(_assetPath))
            return;
        var edits = _pendingEdits.Values.ToList();
        _pendingEdits.Clear();
        EditPrefab("Set Schematic Variable", (root, live) =>
        {
            var schematic = root.GetComponent<SchematicObject>();
            if (schematic == null)
                return;
            foreach (var edit in edits)
            {
                var target = FindInCopy(root, edit.Path);
                var attachment = target != null ? schematic.FindAttachment(edit.Behavior, target.gameObject) : null;
                if (attachment == null)
                    continue;
                if (edit.Revert)
                    attachment.Overrides.RemoveAll(o => o.Name == edit.Variable);
                else
                    attachment.SetOverride(edit.Variable, SchematicValue.IsBoxable(edit.ValueType) ? RemapToCopy(edit.Value, root) : ToCopy(edit.Value, root), edit.ValueType);
            }
        });
    }

    // Copies of array, list and class Variables being edited here (by EditKey), with Unity's drawers on them.
    private readonly Dictionary<string, (DockBoxedValue holder, SerializedObject serialized)> _boxedEdits = new();

    // An array, list or class Variable: this object's value in Unity's own drawers (a copy; each change is queued to be
    // written to the prefab like any other), with a button back to the Behavior's default when it has its own.
    #region Lists of items with Signals

    // The item type of an array or List whose items (plain classes) hold Signals; null for anything else.
    private static Type SignalItemType(Type type)
    {
        var element = type.IsArray ? type.GetElementType()
                    : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0] : null;
        if (element == null || element.IsPrimitive || element == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(element))
            return null;
        foreach (var field in element.GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (IODockRegistry.IsSignalType(field.FieldType))
                return element;
        return null;
    }

    private static bool HoldsSignals(Type type) => SignalItemType(type) != null;

    /// <summary>
    /// A list Variable whose items hold Signals (a Weapon Holder's Weapon Inputs), edited in place: each item's fields
    /// with Unity's drawers, and each of its Signals as a Signal row, to drag into the graph (Send: the graph sends it to
    /// the Behavior, which reacts as the item says). An item's missing Signal is made for it, with the prefab's other
    /// Schematic assets. "+" adds an item, "−" removes one; saved into the prefab as the other list Variables are.
    /// </summary>
    private VisualElement SignalItemsBody(string path, Type behavior, BehaviorAttachment attachment, VariableDefinition definition)
    {
        var type = definition.Type.Type;
        var itemType = SignalItemType(type);
        var body = new VisualElement();
        body.AddToClassList("dock-signal-items");
        body.style.marginLeft = 22;
        body.style.marginRight = 4;
        body.style.marginBottom = 3;
        // (Leaving saves what's waiting, as the Variables section does.)
        body.RegisterCallback<DetachFromPanelEvent>(_ => FlushEdits());

        void Commit(DockBoxedValue holder) => QueueEdit(path, behavior, definition.Name, type, holder.Box?.Boxed);

        // The list with one item added or one removed (-1: none), as the Variable's own kind of list.
        object Changed(IList items, int remove, bool add)
        {
            var list = new List<object>();
            for (int i = 0; items != null && i < items.Count; i++)
                if (i != remove)
                    list.Add(items[i]);
            if (add)
                list.Add(Activator.CreateInstance(itemType));
            if (type.IsArray)
            {
                var array = Array.CreateInstance(itemType, list.Count);
                for (int i = 0; i < list.Count; i++)
                    array.SetValue(list[i], i);
                return array;
            }
            var typed = (IList)Activator.CreateInstance(type);
            foreach (var item in list)
                typed.Add(item);
            return typed;
        }

        void Build()
        {
            body.Clear();
            var edit = BoxedEdit(path, behavior, attachment, definition);
            var holder = edit.holder;
            var items = holder.Box?.Boxed as IList;
            if (EnsureSignals(items, path, behavior, definition))
            {
                edit.serialized.Update();
                Commit(holder);
            }

            for (int i = 0; items != null && i < items.Count; i++)
            {
                int index = i;
                var item = items[i];
                if (item == null)
                    continue;
                string itemPath = $"{DockBoxedValue.ValuePath}.Array.data[{i}]";

                var card = new VisualElement();
                card.AddToClassList("dock-signal-item");

                var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                var number = new Label($"{ObjectNames.NicifyVariableName(itemType.Name)} {i + 1}");
                number.AddToClassList("dock-signal-item-title");
                number.style.flexGrow = 1;
                header.Add(number);
                var remove = new Button(() =>
                {
                    holder.Box.Boxed = Changed(holder.Box.Boxed as IList, index, false);
                    edit.serialized.Update();
                    Commit(holder);
                    Build();
                }) { text = "−", tooltip = "Remove this item." };
                remove.AddToClassList("list-button-remove");
                remove.style.position = Position.Relative;
                header.Add(remove);
                card.Add(header);

                // Its fields, editable (Socket, Event...), and its Signals as Signal rows, in the order they're declared.
                var (draw, rows) = IODockRegistry.RenderMultipleFields(_schematicWindow, holder, item, itemPath,
                    onModified: new List<Action> { () => { edit.serialized.Update(); Commit(holder); } }, plainFields: true);
                SignalRenderer.DrawDelayedEvents();
                if (draw && rows != null)
                    card.Add(rows);
                body.Add(card);
            }

            var add = new Button(() =>
            {
                holder.Box ??= SchematicBox.Create(type, null);
                holder.Box.Boxed = Changed(holder.Box.Boxed as IList, -1, true);
                edit.serialized.Update();
                Commit(holder);
                Build();
            }) { text = "+ " + ObjectNames.NicifyVariableName(itemType.Name), tooltip = "Add an item (its Signal is made for it)." };
            add.AddToClassList("identity-list-add");
            body.Add(add);
        }

        Build();
        return body;
    }

    // Gives every item's empty Signal fields a Signal of their own (kept with the prefab's other Schematic assets, one
    // per Behavior, object, Variable and item). True if any was given one.
    private bool EnsureSignals(IList items, string path, Type behavior, VariableDefinition definition)
    {
        if (items == null || _prefab == null)
            return false;
        bool assigned = false;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null)
                continue;
            foreach (var field in item.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(SignalData).IsAssignableFrom(field.FieldType) || field.GetValue(item) as SignalData != null)
                    continue;
                string name = $"{ObjectNames.NicifyVariableName(definition.Name)} {i + 1}";
                var signal = SchematicAssetManager.Create(_prefab, PropertiesScope(behavior, path) + "_" + definition.Name + "_" + i,
                    field.Name, name, field.FieldType) as SignalData;
                if (signal == null)
                    continue;
                field.SetValue(item, signal);
                assigned = true;
            }
        }
        return assigned;
    }

    #endregion

    // The editable copy of a list or class Variable's value for this object (made once, kept while it's being edited).
    private (DockBoxedValue holder, SerializedObject serialized) BoxedEdit(string path, Type behavior, BehaviorAttachment attachment, VariableDefinition definition)
    {
        var type = definition.Type.Type;
        string key = EditKey(path, behavior, definition.Name);
        if (!_boxedEdits.TryGetValue(key, out var edit) || edit.holder == null)
        {
            var holder = ScriptableObject.CreateInstance<DockBoxedValue>();
            // Hidden and never saved, but editable: HideAndDontSave would also make it NotEditable, and every field drawn
            // from it read-only.
            holder.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            // (A copy made by Unity's serializer, so editing it never touches the prefab's own data in memory.)
            var source = ScriptableObject.CreateInstance<DockBoxedValue>();
            source.hideFlags = HideFlags.HideAndDontSave;
            source.Box = SchematicBox.Create(type, CurrentValue(path, behavior, attachment, definition, type));
            EditorUtility.CopySerializedManagedFieldsOnly(source, holder);
            UnityEngine.Object.DestroyImmediate(source);
            holder.Box ??= SchematicBox.Create(type, null);
            edit = (holder, new SerializedObject(holder));
            _boxedEdits[key] = edit;
        }
        return edit;
    }

    private void DrawBoxedValue(string path, Type behavior, BehaviorAttachment attachment, VariableDefinition definition)
    {
        var type = definition.Type.Type;
        if (type == null)
            return;
        string key = EditKey(path, behavior, definition.Name);
        var edit = BoxedEdit(path, behavior, attachment, definition);

        _pendingEdits.TryGetValue(key, out var pending);
        bool overridden = pending != null ? !pending.Revert : attachment.FindOverride(definition.Name) != null;
        edit.serialized.Update();
        var property = edit.serialized.FindProperty(DockBoxedValue.ValuePath);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (property != null)
                    EditorGUILayout.PropertyField(property, new GUIContent(overridden ? "Value" : "Value (default)",
                        "This object's value: add, remove and edit items here; it's saved into the prefab shortly after."), true);
                else
                    EditorGUILayout.LabelField("(Unity can't draw this type)");
            }
            using (new EditorGUI.DisabledScope(!overridden))
                if (GUILayout.Button(new GUIContent("↺", "Back to the Behavior's default"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    QueueEdit(path, behavior, definition.Name, type, null, revert: true);
                    _boxedEdits.Remove(key);
                    GUIUtility.ExitGUI();
                }
        }
        if (edit.serialized.ApplyModifiedPropertiesWithoutUndo())
            QueueEdit(path, behavior, definition.Name, type, edit.holder.Box?.Boxed);
    }

    // Values inside an array, list or class can hold references to the prefab's own objects too (a Socket's Transform):
    // each is pointed at the matching object in the loaded copy, however deep.
    private object RemapToCopy(object value, GameObject copy, int depth = 0)
    {
        if (value == null || depth > 8)
            return value;
        if (value is UnityEngine.Object)
            return ToCopy(value, copy);
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string || type.IsValueType && !type.IsSerializable)
            return value;
        if (value is System.Collections.IList list)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !(list[i] is string) && !list[i].GetType().IsPrimitive)
                    list[i] = RemapToCopy(list[i], copy, depth + 1);
            return value;
        }
        if (!type.IsSerializable)
            return value;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in type.GetFields(flags))
        {
            if (field.IsNotSerialized || field.IsInitOnly || (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null))
                continue;
            var inner = field.GetValue(value);
            if (inner == null || inner is string || field.FieldType.IsPrimitive || field.FieldType.IsEnum)
                continue;
            var mapped = RemapToCopy(inner, copy, depth + 1);
            if (!ReferenceEquals(mapped, inner) || type.IsValueType)
                field.SetValue(value, mapped);
        }
        return value;
    }

    // A reference to one of the prefab's own objects has to point at the matching object in the loaded copy.
    private object ToCopy(object value, GameObject copy)
    {
        Transform source = value is GameObject gameObject ? gameObject.transform : value is Component component ? component.transform : null;
        if (source == null)
            return value;
        Transform root = _shown != null && source.root == _shown.transform.root ? _shown.transform
                       : _prefab != null && source.root == _prefab.transform ? _prefab.transform : null;
        if (root == null)
            return value;
        var match = FindInCopy(copy, AnimationUtility.CalculateTransformPath(source, root));
        if (match == null)
            return null;
        return value is GameObject ? (object)match.gameObject : match.GetComponent(value.GetType());
    }

    // This object's value of a Variable: waiting to be saved, else its override, else the Behavior's default.
    private object CurrentValue(string path, Type behavior, BehaviorAttachment attachment, VariableDefinition definition, Type type)
    {
        if (definition == null)
            return null;
        if (_pendingEdits.TryGetValue(EditKey(path, behavior, definition.Name), out var pending))
            return pending.Revert ? definition.Default?.Get(type) : pending.Value;
        var entry = attachment.FindOverride(definition.Name);
        return entry != null ? entry.Value.Get(type) : definition.Default?.Get(type);
    }

    // A Variable's value as a native field and the button back to the default (null when there's no native field for its type).
    private VisualElement NativeVariableField(string path, Type behavior, BehaviorAttachment attachment, VariableDefinition definition)
    {
        var type = definition.Type.Type;
        if (type == null || SchematicValue.IsBoxable(type))
            return null;
        Button revert = null;
        var field = DockValueFields.Create(type, CurrentValue(path, behavior, attachment, definition, type), value =>
        {
            QueueEdit(path, behavior, definition.Name, type, value);
            revert?.SetEnabled(true);
        });
        if (field == null)
            return null;
        var holder = new VisualElement();
        holder.style.flexDirection = FlexDirection.Row;
        holder.style.flexGrow = 1;
        holder.style.flexShrink = 1;
        holder.style.minWidth = 60;
        holder.Add(field);
        revert = new Button { text = "↺", tooltip = "Back to the Behavior's default" };
        revert.style.width = 20;
        revert.style.marginLeft = 2;
        revert.style.paddingLeft = revert.style.paddingRight = 0;
        revert.clicked += () =>
        {
            QueueEdit(path, behavior, definition.Name, type, null, revert: true);
            DockValueFields.Show(field, definition.Default?.Get(type));
            revert.SetEnabled(false);
        };
        _pendingEdits.TryGetValue(EditKey(path, behavior, definition.Name), out var pending);
        revert.SetEnabled(pending != null ? !pending.Revert : attachment.FindOverride(definition.Name) != null);
        holder.Add(revert);
        return holder;
    }

    // A Variable's live value on the object the graph is on (redrawn at the Inspector's Refresh Rate), or why there's none.
    private void DrawLiveVariable(string path, Type behavior, VariableDefinition definition, IMGUIContainer container)
    {
        var (instance, index) = LiveVariable(path, behavior, definition.Name);
        if (instance == null)
        {
            EditorGUILayout.LabelField(new GUIContent("(no live object)", "Select a spawned instance of this object to see its values."), EditorStyles.miniLabel);
            return;
        }
        var runtime = instance.Runtime;
        var valueType = runtime.Behavior.Variables[index].ValueType;
        if (SchematicValueField.Draw(GUIContent.none, valueType, runtime.GetVariableBoxed(instance, index), out var value))
            runtime.SetVariableBoxed(instance, index, value);
        InspectorRefresh.Request(container);
    }

    // The live Behavior on the object the graph is on (at a path in it), and its Variable's index; (null, -1) if none.
    private (BehaviorInstance instance, int index) LiveVariable(string path, Type behavior, string name)
    {
        var followed = _schematicWindow != null && _schematicWindow.FollowedInstance != null ? _schematicWindow.FollowedInstance
                     : SchematicGraphEditorWindow.Follower != null ? SchematicGraphEditorWindow.Follower.FollowedInstance : null;
        var root = followed != null ? followed.GetComponent<SchematicObject>() : null;
        if (root == null || !root.IsSpawned)
            return (null, -1);
        var target = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
        if (target == null)
            return (null, -1);
        foreach (var instance in root.BehaviorInstances)
        {
            if (instance == null || !instance.IsSpawned || instance.GameObject != target.gameObject || instance.Behavior == null || instance.Behavior.GetType() != behavior)
                continue;
            var variables = instance.Runtime.Behavior.Variables;
            for (int i = 0; i < variables.Count; i++)
                if (variables[i].Name == name)
                    return (instance, i);
        }
        return (null, -1);
    }

    // The field on a Variable's row: this object's value (or the default), with a button back to the default.
    private void DrawVariableField(string path, Type behavior, BehaviorAttachment attachment, VariableDefinition definition)
    {
        var type = definition.Type.Type;
        if (type == null)
            return;
        // Arrays, lists and classes are edited in Prefab Mode, where Unity's own drawers save them safely.
        if (SchematicValue.IsBoxable(type))
        {
            if (GUILayout.Button(new GUIContent("Edit in Prefab…", "Opens the prefab; edit this value in the Inspector."), EditorStyles.miniButton))
                AssetDatabase.OpenAsset(_prefab);
            return;
        }
        _pendingEdits.TryGetValue(EditKey(path, behavior, definition.Name), out var pending);
        bool overridden = pending != null ? !pending.Revert : attachment.FindOverride(definition.Name) != null;
        var current = CurrentValue(path, behavior, attachment, definition, type);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (SchematicValueField.Draw(GUIContent.none, type, current, out var value))
                QueueEdit(path, behavior, definition.Name, type, value);
            using (new EditorGUI.DisabledScope(!overridden))
                if (GUILayout.Button(new GUIContent("↺", "Back to the Behavior's default"), EditorStyles.miniButton, GUILayout.Width(20)))
                    QueueEdit(path, behavior, definition.Name, type, null, revert: true);
        }
    }

    #endregion

    /// <summary>
    /// Renders a Behavior through the IO Dock: a renderer registered for the Behavior type (<see cref="IODockTargetAttribute"/>)
    /// gets the Behavior's Metadata asset as its target. Otherwise it's drawn as the <see cref="DefaultIODockRenderer"/> draws
    /// a component, except that the fields come from an instance of the Behavior (Behaviors aren't Unity Objects), with
    /// the Metadata asset as the serialized object that owns them (it holds the Behavior's Signal assets).
    /// </summary>
    private (bool, VisualElement) RenderBehaviorDock(Type type, SchematicGraphEditorWindow schematicWindow, BehaviorMetadata metadata)
    {
        var renderer = IODockRegistry.GetComponentRenderer(type);
        if (renderer != null && renderer.GetType() != typeof(DefaultIODockRenderer))
            return IODockRegistry.RenderObjectDock(type, schematicWindow, metadata, _prefab.transform);

        var behavior = GetBehaviorInstance(type);
        if (behavior == null)
            return (false, null);

        var componentScrollView = new VisualElement
        {
            name = "IO Dock Scrollview",
            style =
            {
                flexDirection = FlexDirection.Column
            }
        };
        componentScrollView.contentContainer.style.flexDirection = FlexDirection.Column;

        (bool draw, VisualElement customFieldContainer) = IODockRegistry.RenderMultipleFields(schematicWindow, metadata, behavior, displayObjectName: true);

        if (customFieldContainer != null)
            componentScrollView.Add(customFieldContainer);

        componentScrollView.style.marginTop = 0;
        componentScrollView.style.marginBottom = 0;

        return (draw, componentScrollView);
    }

    /// <summary>
    /// Gets the Behavior's Properties Variables (its <see cref="SchematicPropertiesAttribute"/> ScriptableObjects).
    /// </summary>
    private List<VariableDefinition> GetPropertiesVariables(Type type, BehaviorMetadata metadata)
    {
        var properties = new List<VariableDefinition>();
        var reflection = BehaviorReflection.Get(type);
        if (reflection == null || metadata == null)
            return properties;

        foreach (var variable in metadata.Variables)
        {
            if (variable == null || !reflection.IsPropertiesVariable(variable.Name))
                continue;
            var variableType = variable.Type.Type;
            if (variableType != null && typeof(ScriptableObject).IsAssignableFrom(variableType))
                properties.Add(variable);
        }
        return properties;
    }

    /// <summary>
    /// The Properties tab: each Properties asset of the Behavior (named, when there's more than one).
    /// </summary>
    private VisualElement CreatePropertiesSection(BehaviorAttachment attachment, Type type, List<VariableDefinition> propertiesVariables, Transform tf)
    {
        var section = new VisualElement();
        foreach (var variable in propertiesVariables)
        {
            if (propertiesVariables.Count > 1)
                section.Add(DockRows.SectionLabel(ObjectNames.NicifyVariableName(variable.Name)));
            section.Add(CreatePropertiesPanel(attachment, type, variable, tf));
        }
        return section;
    }

    /// <summary>
    /// A Properties asset, edited in place: its fields split into tabs by their [Header]s (each tab's fields made only
    /// when it's opened, so a long one stays quick), or the asset's own Inspector when it has a custom one. Right-click
    /// to reset it; Open shows it in an Inspector window of its own.
    /// </summary>
    private VisualElement CreatePropertiesPanel(BehaviorAttachment attachment, Type type, VariableDefinition propertiesVariable, Transform tf)
    {
        var propertiesType = propertiesVariable.Type.Type;
        var variableName = propertiesVariable.Name;

        var panel = new VisualElement { style = { width = Length.Percent(100), flexGrow = 0, flexShrink = 0 } };

        panel.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("Reset Properties", _ =>
            {
                // This object's own Properties asset (with the prefab's other Schematic assets), back to its defaults.
                var tfPath = PathOf(tf);
                var properties = SchematicAssetManager.Create(_prefab, PropertiesScope(type, tfPath), variableName, variableName, propertiesType);
                if (properties == null)
                    return;
                var fresh = ScriptableObject.CreateInstance(propertiesType);
                var name = properties.name;
                Undo.RecordObject(properties, "Reset Properties");
                EditorUtility.CopySerialized(fresh, properties);
                properties.name = name;
                UnityEngine.Object.DestroyImmediate(fresh);
                EditorUtility.SetDirty(properties);
                AssetDatabase.SaveAssetIfDirty(properties);

                if (GetProperties(attachment, propertiesVariable) != properties)
                    SetPropertiesOverride(type, tfPath, variableName, properties, propertiesType);
            });
        }));

        var propertiesObject = GetProperties(attachment, propertiesVariable);

        if (propertiesObject == null && propertiesType != null && !propertiesType.IsAbstract)
        {
            // Stored with the prefab's other Schematic assets, one per Behavior (and GameObject) that uses it.
            var tfPath = PathOf(tf);
            propertiesObject = SchematicAssetManager.Create(_prefab, PropertiesScope(type, tfPath), variableName, variableName, propertiesType);
            if (propertiesObject != null)
                SetPropertiesOverride(type, tfPath, variableName, propertiesObject, propertiesType);
        }

        if (propertiesObject == null)
            return panel;

        if (!HasCustomEditor(propertiesObject))
        {
            panel.Add(CreateHeaderTabs(propertiesObject, TabsKey(PathOf(tf), $"{type.FullName}|{variableName}|headers")));
            return panel;
        }

        // The asset's own Inspector, made once and kept (made again every redraw, it forgot its own foldouts at once
        // and piled up editors).
        Editor propertiesEditor = null;
        IMGUIContainer container = null;
        container = new IMGUIContainer(() =>
        {
            if (propertiesObject == null) return;
            if (propertiesEditor == null || propertiesEditor.target != propertiesObject)
                Editor.CreateCachedEditor(propertiesObject, null, ref propertiesEditor);
            propertiesEditor?.OnInspectorGUI();
            if (Application.isPlaying)
                InspectorRefresh.Request(container);
        });
        container.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            if (propertiesEditor != null)
                UnityEngine.Object.DestroyImmediate(propertiesEditor);
            propertiesEditor = null;
        });

        panel.Add(container);
        return panel;
    }

    // Whether an asset is drawn by an Inspector of its own (kept as it is), rather than Unity's default one (split by
    // header here). Per type, found once.
    private static readonly Dictionary<Type, bool> _customEditors = new();

    private static bool HasCustomEditor(UnityEngine.Object asset)
    {
        var type = asset.GetType();
        if (_customEditors.TryGetValue(type, out var custom))
            return custom;
        var editor = Editor.CreateEditor(asset);
        // (NaughtyAttributes' Inspector is registered for every object: that's the default one too, not the asset's own.)
        var editorName = editor != null ? editor.GetType().Name : null;
        custom = editorName != null && editorName != "GenericInspector" && editorName != "NaughtyInspector";
        if (editor != null)
            UnityEngine.Object.DestroyImmediate(editor);
        _customEditors[type] = custom;
        return custom;
    }

    /// <summary>
    /// An asset's fields, as tabs by their [Header]s (fields before the first header under "General"), each made when
    /// opened; just the fields when there are no headers. Bound to the asset, so they follow it (in Play Mode too).
    /// </summary>
    private static VisualElement CreateHeaderTabs(UnityEngine.Object asset, string key)
    {
        var serialized = new SerializedObject(asset);
        var groups = new List<(string header, List<string> paths)>();
        var iterator = serialized.GetIterator();
        for (bool enter = true; iterator.NextVisible(enter); enter = false)
        {
            if (iterator.propertyPath == "m_Script")
                continue;
            var header = HeaderOf(asset.GetType(), iterator.name);
            if (header != null || groups.Count == 0)
                groups.Add((header ?? "General", new List<string>()));
            groups[groups.Count - 1].paths.Add(iterator.propertyPath);
        }

        VisualElement Fields(List<string> paths)
        {
            var fields = new VisualElement();
            foreach (var propertyPath in paths)
            {
                var property = serialized.FindProperty(propertyPath);
                if (property != null)
                    fields.Add(new PropertyField(property));
            }
            fields.Bind(serialized);
            return fields;
        }

        if (groups.Count <= 1)
            return groups.Count == 1 ? Fields(groups[0].paths) : new VisualElement();

        var tabs = new TabView("", "headers", topScrollBar: true, hideTabLabels: false, collapsible: true);
        foreach (var (header, paths) in groups)
        {
            var id = header;
            while (tabs.HasTab(id))
                id += " ";
            var groupPaths = paths;
            tabs.AddTab(id).UseFactory(() => Fields(groupPaths));
        }
        RememberOpen(tabs, key, groups[0].header);
        return tabs;
    }

    // A field's [Header] (the last, nearest one when there are several), looked for up the class's bases.
    private static string HeaderOf(Type type, string fieldName)
    {
        for (var t = type; t != null && t != typeof(ScriptableObject); t = t.BaseType)
        {
            var field = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
                continue;
            var headers = field.GetCustomAttributes(typeof(HeaderAttribute), false);
            return headers.Length > 0 ? ((HeaderAttribute)headers[headers.Length - 1]).header : null;
        }
        return null;
    }

    /// <summary>
    /// This object's Properties asset for a Behavior: its own (the attachment's override), or the Behavior's default.
    /// </summary>
    private static ScriptableObject GetProperties(BehaviorAttachment attachment, VariableDefinition variable)
    {
        var type = variable.Type.Type;
        var entry = attachment.FindOverride(variable.Name);
        if (entry?.Value != null && entry.Value.Get(type) is ScriptableObject own)
            return own;
        return variable.Default?.Get(type) as ScriptableObject;
    }

    /// <summary>
    /// Creates the remove Behavior button.
    /// </summary>
    private Button CreateRemoveBehaviorButton(Type type, Transform tf, GameObjectFoldout goFoldout)
    {
        var removeComponentButton = new Button(() =>
        {
            RemoveBehavior(type, null, tf);
        })
        {
            text = "-"
        };

        removeComponentButton.style.minWidth = 10;
        removeComponentButton.style.minHeight = 10;
        removeComponentButton.style.maxWidth = 10;
        removeComponentButton.style.maxHeight = 10;
        removeComponentButton.style.backgroundColor = SchematicGraphEditorWindow.ColorLookupFunctional["Remove"];
        removeComponentButton.RegisterCallback<MouseEnterEvent>(evt => removeComponentButton.style.backgroundColor = SchematicGraphEditorWindow.ColorLookupFunctional["RemoveHighlight"]);
        removeComponentButton.RegisterCallback<MouseLeaveEvent>(evt => removeComponentButton.style.backgroundColor = SchematicGraphEditorWindow.ColorLookupFunctional["Remove"]);

        return removeComponentButton;
    }

    /// <summary>
    /// Removes a Behavior from a GameObject: from the root Schematic Object's list for it (in the Prefab Mode copy, with
    /// Undo, as the Inspector does; otherwise in the prefab asset).
    /// </summary>
    private void RemoveBehavior(Type type, BehaviorAttachment attachment, Transform tf)
    {
        var tfPath = PathOf(tf);
        var metadata = attachment?.Metadata;
        EditPrefab("Remove Schematic Behavior", (root, live) =>
        {
            var target = FindInCopy(root, tfPath);
            var schematic = root.GetComponent<SchematicObject>();
            if (target == null || schematic == null || !schematic.Behaviors.TryGetValue(target.gameObject, out var list) || list == null)
                return;

            if (live)
            {
                foreach (var present in list.Items.ToList())
                    if (type != null ? present?.Metadata != null && present.Metadata.BehaviorType.Type == type
                                     : present == null || present.Metadata == metadata)
                        SchematicBehaviorsHeaderGUI.RemoveWithUndo(schematic, target.gameObject, present);
                return;
            }

            if (type != null)
                list.Items.RemoveAll(a => a?.Metadata != null && a.Metadata.BehaviorType.Type == type);
            else
                // A Behavior whose script is missing: the attachment with the same Metadata (or none).
                list.Items.RemoveAll(a => a == null || a.Metadata == metadata);

            if (list.Items.Count == 0)
                schematic.Behaviors.Remove(target.gameObject);
        });
    }

    /// <summary>
    /// Creates the "Add Behavior" button with cached Behavior type list.
    /// </summary>
    private void CreateAddBehaviorButton(TabView tabView, Transform tf)
    {
        var addButton = tabView.AddActionButton("+", "Add a Behavior", () =>
        {
            // Initialize Behavior types cache if needed
            if (!_behaviorTypesInitialized)
            {
                _availableBehaviorTypes = BuildAvailableBehaviorTypesList();
                _behaviorTypesInitialized = true;
            }

            var attached = new HashSet<Type>();
            foreach (var attachment in GetBehaviors(tf.gameObject))
                if (attachment.Metadata != null && attachment.Metadata.BehaviorType.Type != null)
                    attached.Add(attachment.Metadata.BehaviorType.Type);

            var menu = new GenericMenu();

            foreach (var type in _availableBehaviorTypes)
            {
                if (attached.Contains(type))
                    continue;

                string menuPath = BehaviorDropdown.GetMenuPath(type);

                menu.AddItem(new GUIContent(menuPath), false, () =>
                {
                    AddBehavior(PathOf(tf), type);
                });
            }

            menu.ShowAsContext();
        });
        addButton.AddToClassList("tab-add-component");
    }

    /// <summary>
    /// Attaches a Behavior to the GameObject at a path in the prefab (as the Inspector's add does: required Behaviors and
    /// components, OnSetup), in the prefab asset. Also used by the assistant.
    /// </summary>
    internal void AddBehavior(string tfPath, Type type)
    {
        // Metadata first: attaching needs it, and generating it touches the asset database.
        if (type == null || BehaviorMetadataGenerator.GetOrCreate(type) == null)
            return;

        EditPrefab("Add Schematic Behavior", (root, live) =>
        {
            var target = FindInCopy(root, tfPath);
            if (target == null)
                return;

            var schematic = root.GetComponent<SchematicObject>();
            if (schematic == null)
            {
                schematic = live ? Undo.AddComponent<SchematicObject>(root) : root.AddComponent<SchematicObject>();
                schematic.AssignObjectID(SchematicObjectIDs.For(_assetPath));
            }

            // The Inspector's own add on the Prefab Mode copy (Undo); the same attach on the loaded contents otherwise.
            if (live)
                SchematicBehaviorsHeaderGUI.AttachWithUndo(schematic, target.gameObject, type);
            else
                schematic.AttachBehavior(type, target.gameObject);
        });
    }

    private static string PropertiesScope(Type type, string tfPath)
        => type.Name + (string.IsNullOrEmpty(tfPath) ? "" : "_" + tfPath.Replace('/', '_'));

    /// <summary>Points a Behavior's Properties Variable at an asset, for this GameObject.</summary>
    private void SetPropertiesOverride(Type type, string tfPath, string variableName, ScriptableObject properties, Type propertiesType)
    {
        EditPrefab("Set Properties", (root, live) =>
        {
            var target = FindInCopy(root, tfPath);
            var schematic = root.GetComponent<SchematicObject>();
            var attachment = target != null && schematic != null ? schematic.FindAttachment(type, target.gameObject) : null;
            if (attachment == null)
                return;
            if (live)
                Undo.RecordObject(schematic, "Set Properties");
            attachment.SetOverride(variableName, properties, propertiesType);
        });
    }

    /// <summary>Renames a GameObject of the prefab (double-click its name): typed into its name, in place.</summary>
    private void RenameObject(Transform tf, VisualElement anchor)
    {
        if (tf == null)
            return;
        var tfPath = PathOf(tf);
        if (string.IsNullOrEmpty(tfPath))
            return; // The root is named after the prefab file.
        var current = tf.name;

        void Apply(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == current)
                return;
            // (After the field is gone: the dock is drawn again for the change.)
            EditorApplication.delayCall += () => EditPrefab("Rename", (root, live) =>
            {
                var target = FindInCopy(root, tfPath);
                if (target == null)
                    return;
                if (live)
                    Undo.RecordObject(target.gameObject, "Rename");
                target.name = name;
            });
        }

        var label = anchor as Label ?? anchor?.Q<Label>(className: "list-label");
        if (label != null && label.parent != null)
        {
            Remedy.Schematics.Templates.InlineRename.Begin(label, current, Apply);
            return;
        }
        new InlineIdentifierEditor(popupRect: anchor.WorldBoundToScreen(),
                                   identifierType: ListIdentifierType.Name,
                                   getOriginalValue: () => current,
                                   onFinishedEditting: (bool change, object newValue) => Apply(newValue?.ToString()));
    }

    /// <summary>
    /// Builds a cached list of available Behavior types that can be added.
    /// </summary>
    private static List<Type> BuildAvailableBehaviorTypesList()
    {
        var allTypes = new List<Type>();

        foreach (var type in BehaviorMetadataGenerator.BehaviorTypes)
        {
            if (type == null) continue;
            if (type.GetCustomAttribute<SchematicBehaviorPathAttribute>()?.Hidden == true) continue;

            allTypes.Add(type);
        }

        allTypes.Sort((a, b) => string.CompareOrdinal(BehaviorDropdown.GetMenuPath(a), BehaviorDropdown.GetMenuPath(b)));

        return allTypes;
    }

    /// <summary>
    /// The Behaviors attached to a GameObject of the prefab, from the root Schematic Object.
    /// </summary>
    private List<BehaviorAttachment> GetBehaviors(GameObject gameObject)
    {
        var behaviors = new List<BehaviorAttachment>();
        var root = _shown != null ? _shown.GetComponent<SchematicObject>() : null;
        if (root == null || gameObject == null)
            return behaviors;

        if (!root.Behaviors.TryGetValue(gameObject, out var list) || list == null)
        {
            // The stored key may be another copy of this same object (e.g. the one in Prefab Mode or a scene, or the copy
            // a save was made from): match it by its path in its own prefab instead.
            string path = PathOf(gameObject.transform);
            foreach (var entry in root.Behaviors)
            {
                var key = entry.Key;
                if (key == null || entry.Value == null)
                    continue;
                var keyRoot = key.transform.root;
                if (keyRoot.GetRelativePath(key.transform) == path && key.name == gameObject.name)
                {
                    list = entry.Value;
                    break;
                }
            }

            if (list == null && root.Behaviors.Count > 0 && gameObject == _shown)
                Debug.LogWarning($"[Schematics] The Prefab tab found no Behaviors for '{gameObject.name}', but its Schematic Object has " +
                                 $"{root.Behaviors.Count} Behavior entr{(root.Behaviors.Count == 1 ? "y" : "ies")} keyed by: " +
                                 string.Join(", ", root.Behaviors.Keys.Select(k => k == null ? "<missing object>"
                                     : $"'{k.name}' ({(EditorUtility.IsPersistent(k) ? "asset" : k.scene.IsValid() ? "scene " + k.scene.name : "not saved")})")),
                                 _shown);
        }

        if (list?.Items != null)
            foreach (var attachment in list.Items)
                if (attachment != null)
                    behaviors.Add(attachment);

        return behaviors;
    }

    /// <summary>
    /// One instance of a Behavior type (cached), for reflecting over its fields.
    /// </summary>
    private static SchematicBehavior GetBehaviorInstance(Type type)
    {
        if (_behaviorInstances.TryGetValue(type, out var behavior))
            return behavior;

        try
        {
            behavior = SchematicBehavior.Create(type);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            behavior = null;
        }

        _behaviorInstances[type] = behavior;
        return behavior;
    }

    // Behavior icons by type, found once (finding the script is a search of the project).
    private static readonly Dictionary<Type, (Texture icon, bool own)> _behaviorIcons = new();

    /// <summary>
    /// The Behavior script's icon: its own (<c>own</c>), or else the generic C# script icon (for which the tab shows the
    /// Behavior's name too).
    /// </summary>
    private static (Texture icon, bool own) GetBehaviorIcon(Type type)
    {
        if (_behaviorIcons.TryGetValue(type, out var found))
            return found;
        MonoScript script = null;
        foreach (var guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            var candidate = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.GetClass() == type)
            {
                script = candidate;
                break;
            }
        }
        Texture own = script != null ? EditorGUIUtility.GetIconForObject(script) : null;
        found = own != null ? (own, true)
              : (script != null ? AssetPreview.GetMiniThumbnail(script) : EditorGUIUtility.IconContent("cs Script Icon").image, false);
        _behaviorIcons[type] = found;
        return found;
    }

    #region Selecting objects

    // Every object row's header and its object's path, for the highlight on the selected one.
    private readonly List<(VisualElement header, string path)> _objectHeaders = new();

    // Where the objects are selected: Prefab Mode's copy when it's open on this prefab, the instance the graph is on,
    // else the prefab asset itself.
    private GameObject SelectionRoot()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == _assetPath && stage.prefabContentsRoot != null)
            return stage.prefabContentsRoot;
        if (_schematicWindow != null && _schematicWindow.FollowedInstance != null)
            return _schematicWindow.FollowedInstance;
        return _shown;
    }

    private void SelectObject(string path)
    {
        var root = SelectionRoot();
        if (root == null)
            return;
        var target = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
        if (target != null)
            Selection.activeGameObject = target.gameObject;
    }

    // The selected object's path in the prefab, if it's one of the objects shown (in any of the places above); else null.
    private string SelectedPath()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
            return null;
        foreach (var root in new[] { SelectionRoot(), _shown })
            if (root != null && (selected.transform == root.transform || selected.transform.IsChildOf(root.transform)))
                return root.transform.GetRelativePath(selected.transform);
        return null;
    }

    private void UpdateHighlight()
    {
        var selected = SelectedPath();
        foreach (var (header, path) in _objectHeaders)
        {
            var isSelected = selected != null && path == selected;
            header.EnableInClassList("dock-object-selected", isSelected);
            header.EnableInClassList("dock-object-header--selected", isSelected);
        }
    }

    #endregion

    private static bool TabExists(TabView tabView, string tabId)
    {
        try
        {
            return tabView[tabId] != null;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private static Transform FindInCopy(GameObject copy, string tfPath)
        => string.IsNullOrEmpty(tfPath) ? copy.transform : copy.transform.Find(tfPath);

    #region Editing the prefab

    // True while a save is in flight (the redraw watcher waits for it).
    private static bool _editing;

    /// <summary>
    /// Makes a change to the prefab with the original load-edit-save: load the prefab's contents, change them, save
    /// them over the asset, reimport, rebuild. The edit gets the loaded root (and false: it's never the Prefab Mode copy).
    /// </summary>
    private void EditPrefab(string undoName, Action<GameObject, bool> edit)
    {
        if (edit == null)
            return;

        var copy = PrefabUtility.LoadPrefabContents(_assetPath);
        try
        {
            KeepSchematicLink(copy);
            edit(copy, false);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            PrefabUtility.UnloadPrefabContents(copy);
            return;
        }
        ApplyChangesToPrefabAsset(copy);
    }

    /// <summary>
    /// The loaded copy comes from the file on disk. Its Schematic Object keeps the link to its graph, and its ObjectID,
    /// even if the file never had them: the graph window links the prefab's Schematic Object to its graph in memory
    /// only, and the save below would otherwise write the file's empty reference over it (and the reimport would
    /// then drop the in-memory one too).
    /// </summary>
    private void KeepSchematicLink(GameObject copy)
    {
        var schematic = copy != null ? copy.GetComponent<SchematicObject>() : null;
        if (schematic == null)
            return;

        if (schematic.SchematicGraph == null)
        {
            var inMemory = _prefab != null ? _prefab.GetComponent<SchematicObject>() : null;
            SchematicGraph graph = inMemory != null ? inMemory.SchematicGraph : null;
            if (!IsObjectGraph(graph))
                graph = _schematicWindow != null ? _schematicWindow.SchematicGraph : null;
            if (IsObjectGraph(graph))
                schematic.Assign(graph);
        }

        if (schematic.ObjectID == 0)
            schematic.AssignObjectID(SchematicObjectIDs.For(_assetPath));
    }

    // The prefab's own graph: not a Singleton's, and not the graph of one of its State Machine's states (those link to
    // the prefab too).
    private bool IsObjectGraph(SchematicGraph graph)
    {
        if (graph == null || graph is SingletonGraph)
            return false;
        if (graph.Prefab != null && _prefab != null && graph.Prefab != _prefab)
            return false;
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(StateMachineDefinition)))
        {
            var definition = AssetDatabase.LoadAssetAtPath<StateMachineDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition != null && definition.States != null && definition.States.Any(state => state != null && state.Graph == graph))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Applies changes to the prefab asset and triggers rebuild if needed.
    /// </summary>
    private async void ApplyChangesToPrefabAsset(GameObject copy)
    {
        _editing = true;
        PrefabUtility.SaveAsPrefabAsset(copy, _assetPath);
        PrefabUtility.UnloadPrefabContents(copy);

        AssetDatabase.Refresh();

        await EditorUtilities.NextEditorFrame();

        PrefabRefreshUtility.ReimportAndResetPrefab(_prefab);
        _editing = false;
        RebuildHierarchy(_prefab.transform);
    }

    #endregion

    #region Used by the graph window and its minimap

    internal static Texture2D VariableIcon => DockRows.VariableIcon;

    internal static Color ColorOf(Type type) => DockRows.ColorOf(type);

    /// <summary>
    /// Shows the row for a dock item. This hierarchy has no per-item rows (Signals are drawn in the Behavior tabs), so it
    /// reports false and the window leaves the dock as it is.
    /// </summary>
    public bool Reveal(string key) => false;

    private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> _flashing = new();

    /// <summary>Briefly highlights an element (the graph window uses it for its Graph tab rows and nodes).</summary>
    internal static void Flash(VisualElement element)
    {
        // A second click mid-flash restarts it instead of starting another one that would remember the
        // half-faded colour as "original" and leave it on for good.
        if (_flashing.TryGetValue(element, out var running))
            running.Pause();

        float t = 0f;
        IVisualElementScheduledItem item = null;
        item = element.schedule.Execute(() =>
        {
            t += 0.06f;
            if (t >= 1f)
            {
                // Back to whatever the stylesheet says.
                element.style.backgroundColor = StyleKeyword.Null;
                item.Pause();
                _flashing.Remove(element);
                return;
            }
            element.style.backgroundColor = Color.Lerp(new Color(0f, 0.55f, 0.6f, 0.9f), new Color(0f, 0.55f, 0.6f, 0f), t);
        }).Every(16);
        _flashing[element] = item;
    }

    #endregion
}
