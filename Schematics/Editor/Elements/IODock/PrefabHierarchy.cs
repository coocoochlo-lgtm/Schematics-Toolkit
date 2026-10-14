using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The IO Dock's Prefab tab: the prefab's GameObjects, each with the Schematic Behaviors attached to it, and under each
/// Behavior its Variables (above) and Signals. Every row can be dragged into the graph to make a node, and clicked to
/// jump between the graph nodes that use it. GameObjects can be dragged in too, as Prefab Object nodes.
/// <para>
/// Laid out like Unity's Hierarchy: each GameObject's block is shaded a little lighter or darker than the one before
/// it, its header lights up under the mouse, and the selected object's header is highlighted (selecting it also selects
/// it in Unity, so the Inspector shows it too).
/// </para>
/// <para>
/// While the prefab is open in Prefab Mode, the dock shows and edits that open copy, the same objects the Inspector
/// shows, through the Inspector's own code (with Undo); Prefab Mode saves it. Otherwise it edits the prefab asset with
/// a load-edit-save of the file. Either way it redraws itself when the prefab changes elsewhere (the Inspector, Undo,
/// a Prefab Mode save).
/// </para>
/// </summary>
public class PrefabHierarchy : VisualElement
{
    private readonly GameObject _prefab;
    private readonly SchematicObject _root;
    private readonly string _assetPath;
    private readonly SchematicGraphEditorWindow _window;

    // What's shown and edited: the open Prefab Mode copy's root when the prefab is open there (live), else the asset.
    private readonly GameObject _shown;
    private readonly bool _live;

    // The GameObject blocks in hierarchy order (for the alternating shading), and the selected one's path.
    private readonly List<Foldout> _objectFoldouts = new();
    private readonly Dictionary<string, Toggle> _headers = new();
    private string _selectedPath;

    // Redraws when the prefab changes from elsewhere (see Watch).
    private int _signature;
    private IVisualElementScheduledItem _watch;

    // Every row by its DockItem key, so the graph can reveal the row a node uses.
    private readonly Dictionary<string, VisualElement> _rows = new();

    private static Texture2D InputIcon => DockRows.InputIcon;
    private static Texture2D OutputIcon => DockRows.OutputIcon;
    internal static Texture2D VariableIcon => DockRows.VariableIcon;

    public PrefabHierarchy(GameObject prefab, SchematicGraphEditorWindow window)
    {
        _prefab = prefab;
        _window = window;
        _assetPath = AssetDatabase.GetAssetPath(prefab);
        var live = LiveRoot(_assetPath);
        _live = live != null;
        _shown = _live ? live : prefab;
        _root = _shown != null ? _shown.GetComponent<SchematicObject>() : null;
        PrefabSaveTrace.Step(0, _live ? "dock built (on the open Prefab Mode copy)" : "dock built", _assetPath, dockObject: prefab);

        AddToClassList("behavior-dock");
        AddToClassList("prefab-hierarchy");
        var styleSheet = Resources.Load<StyleSheet>("PrefabHierarchy");
        if (styleSheet != null)
            styleSheets.Add(styleSheet);
        RegisterCallback<MouseLeaveEvent>(_ => FlushPendingEdits());
        RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            FlushPendingEdits();
            Selection.selectionChanged -= OnUnitySelectionChanged;
            _watch?.Pause();
        });
        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            Selection.selectionChanged -= OnUnitySelectionChanged;
            Selection.selectionChanged += OnUnitySelectionChanged;
            _signature = Signature();
            _watch ??= schedule.Execute(Watch).Every(WatchMs);
            _watch.Resume();
            OnUnitySelectionChanged();
        });
        // IMGUI fields can ask for a lot of width; never let the dock grow over the graph.
        style.overflow = Overflow.Hidden;
        style.minWidth = 0;
        style.flexShrink = 1;
        Rebuild();
    }

    public void Rebuild()
    {
        Clear();
        _rows.Clear();
        _objectFoldouts.Clear();
        _headers.Clear();

        if (_root == null)
        {
            Add(new HelpBox("This prefab has no Schematic Object component, so it has no Schematic Behaviors.", HelpBoxMessageType.Warning));
            if (_live && _shown != null)
                Add(new Button(() =>
                {
                    var added = Undo.AddComponent<SchematicObject>(_shown);
                    if (added != null)
                        added.AssignObjectID(SchematicObjectIDs.For(_assetPath));
                    MarkLiveDirty();
                    _window.RedrawIODock();
                }) { text = "Add Schematic Object" });
            else if (_prefab != null)
                Add(new Button(() =>
                {
                    var contents = PrefabUtility.LoadPrefabContents(_assetPath);
                    contents.AddComponent<SchematicObject>().AssignObjectID(SchematicObjectIDs.For(_assetPath));
                    Save(contents);
                }) { text = "Add Schematic Object" });
            return;
        }

        if (_live)
        {
            var note = new Label("Editing the copy open in Prefab Mode");
            note.AddToClassList("prefab-hierarchy-note");
            note.tooltip = "Changes here go into the open Prefab Mode copy (with Undo), and Prefab Mode saves them into the prefab.";
            Add(note);
        }

        Add(CreateObjectFoldout(_shown.transform));
        Restripe();
        ShowSelected();
    }

    #region Live (Prefab Mode) and watching for changes

    private const long WatchMs = 500;

    // The root of the prefab's copy open in Prefab Mode, if it's open there.
    private static GameObject LiveRoot(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return null;
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        return stage != null && stage.assetPath == assetPath ? stage.prefabContentsRoot : null;
    }

    // Prefab Mode saves (by itself or with Save) what's changed in its scene.
    private void MarkLiveDirty()
    {
        if (!_live || _shown == null)
            return;
        if (_root != null)
            EditorUtility.SetDirty(_root);
        if (_shown.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(_shown.scene);
    }

    /// <summary>
    /// Twice a second: redraws when the prefab's objects or Behaviors changed from elsewhere (the Inspector, Undo, a
    /// Prefab Mode save reimporting the asset), and starts over on the right copy when Prefab Mode opens or closes.
    /// Values aren't watched: their fields draw the current value anyway.
    /// </summary>
    private void Watch()
    {
        if (_editing || EditorApplication.isCompiling || EditorApplication.isUpdating || panel == null)
            return;

        bool liveNow = LiveRoot(_assetPath) != null;
        if (liveNow != _live || (_live && _shown == null) || (_shown != null && _shown.GetComponent<SchematicObject>() != _root))
        {
            // Not with value edits still waiting to be written into the file: they go first.
            if (_pendingEdits.Count > 0 || _queuedEdits.Count > 0)
                return;
            _watch?.Pause();
            _window.RedrawIODock();
            return;
        }

        int signature = Signature();
        if (signature == _signature)
            return;
        _signature = signature;
        Rebuild();
    }

    // A cheap fingerprint of the hierarchy (names, order) and of which Behaviors are attached where.
    private int Signature()
    {
        if (_shown == null)
            return 0;
        unchecked
        {
            int hash = 17;
            foreach (var transform in _shown.GetComponentsInChildren<Transform>(true))
            {
                hash = hash * 31 + (transform.name?.GetHashCode() ?? 0);
                hash = hash * 31 + transform.GetSiblingIndex();
                hash = hash * 31 + transform.childCount;
            }
            var root = _shown.GetComponent<SchematicObject>();
            if (root != null)
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

    #region Rows: shading and selection

    // Each GameObject's block takes the next shade, top to bottom, counting only the ones that can be seen (like
    // Unity's Hierarchy rows), so the shading stays alternating as objects are folded and unfolded.
    private void Restripe()
    {
        int index = 0;
        foreach (var foldout in _objectFoldouts)
        {
            bool visible = true;
            for (var parent = foldout.parent; parent != null && parent != this; parent = parent.parent)
                if (parent is Foldout outer && _objectFoldouts.Contains(outer) && !outer.value)
                {
                    visible = false;
                    break;
                }
            if (!visible)
                continue;
            foldout.EnableInClassList("dock-object--odd", index % 2 == 1);
            index++;
        }
    }

    private void Select(string path, bool selectInUnity)
    {
        _selectedPath = path;
        ShowSelected();
        if (!selectInUnity || _shown == null)
            return;
        var target = Find(_shown, path);
        if (target != null && Selection.activeGameObject != target.gameObject)
            Selection.activeGameObject = target.gameObject;
    }

    private void ShowSelected()
    {
        foreach (var pair in _headers)
            pair.Value.EnableInClassList("dock-object-header--selected", pair.Key == _selectedPath);
    }

    // Selecting one of the prefab's objects anywhere (Hierarchy, Scene view, Project) highlights it here too.
    private void OnUnitySelectionChanged()
    {
        var selected = Selection.activeGameObject;
        if (selected == null || _shown == null)
            return;
        var transform = selected.transform;
        if (transform != _shown.transform && !transform.IsChildOf(_shown.transform))
            return;
        string path = PathOf(transform);
        if (path == _selectedPath)
            return;
        _selectedPath = path;
        ShowSelected();
    }

    #endregion

    #region GameObjects

    private VisualElement CreateObjectFoldout(Transform transform)
    {
        string path = PathOf(transform);
        var foldout = new Foldout
        {
            text = transform.name,
            value = SessionState.GetBool(FoldoutKey("go", path), transform == _shown.transform),
        };
        foldout.AddToClassList("dock-object");
        _objectFoldouts.Add(foldout);
        foldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == foldout)
            {
                SessionState.SetBool(FoldoutKey("go", path), evt.newValue);
                Restripe();
            }
        });

        // Drag the GameObject itself into the graph.
        var item = new DockItem { Kind = DockItem.ItemKind.GameObject, Path = path, GameObject = transform.gameObject };
        var toggle = foldout.Q<Toggle>();
        toggle.AddToClassList("dock-object-header");
        _headers[path] = toggle;
        var handle = CreateHandle(EditorGUIUtility.IconContent("GameObject Icon").image, "Drag into the graph for a Prefab Object node");
        MakeDraggable(handle, item, click: null);
        toggle.Add(handle);
        toggle.userData ??= item;
        _rows[item.Key] = toggle;

        toggle.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0)
                return;
            if (evt.clickCount == 2)
            {
                Rename(toggle, path, transform.name);
                evt.StopPropagation();
                return;
            }
            // Selected here and in Unity, so the Inspector shows the same object.
            Select(path, selectInUnity: true);
        });
        toggle.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("Add Child", _ => AddChild(path));
            evt.menu.AppendAction("Rename", _ => Rename(toggle, path, transform.name));
            if (transform != _shown.transform)
                evt.menu.AppendAction("Delete", _ => DeleteObject(path));
            evt.menu.AppendAction(_live ? "Select" : "Select in Project", _ => Selection.activeObject = transform.gameObject);
        }));

        // Behaviors attached here.
        if (_root.Behaviors.TryGetValue(transform.gameObject, out var list) && list != null)
            foreach (var attachment in list.Items)
                if (attachment != null)
                    foldout.Add(CreateBehaviorSection(transform.gameObject, path, attachment));

        // Drawn with IMGUI: the Behavior dropdown positions itself from the GUI that opened it.
        var target = transform.gameObject;
        var addButton = new IMGUIContainer(() =>
        {
            var content = new GUIContent("+ Add Behavior");
            var rect = GUILayoutUtility.GetRect(content, EditorStyles.miniButton);
            if (!GUI.Button(rect, content, EditorStyles.miniButton))
                return;
            // Prefab Mode open: exactly what the Inspector's button does, on the same objects (with Undo).
            if (_live)
                SchematicBehaviorsHeaderGUI.ShowAddBehaviorDropdown(rect, _root, target, _ => AfterLiveEdit());
            else
                SchematicBehaviorsHeaderGUI.ShowBehaviorDropdown(rect, AttachedTypes(target), type => AddBehavior(path, type));
        });
        addButton.AddToClassList("dock-add-behavior");
        foldout.Add(addButton);

        foreach (Transform child in transform)
            foldout.Add(CreateObjectFoldout(child));

        return foldout;
    }

    #endregion

    #region Behaviors

    private VisualElement CreateBehaviorSection(GameObject target, string path, BehaviorAttachment attachment)
    {
        var metadata = attachment.Metadata;
        var behaviorType = metadata != null ? metadata.BehaviorType.Type : null;
        string title = behaviorType != null ? ObjectNames.NicifyVariableName(behaviorType.Name)
                     : metadata != null ? $"{metadata.name} (Missing Script)" : "(Missing Behavior)";

        var section = new Foldout { text = title, value = SessionState.GetBool(FoldoutKey("behavior", path + behaviorType?.FullName), true) };
        section.AddToClassList("dock-behavior");
        section.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == section)
                SessionState.SetBool(FoldoutKey("behavior", path + behaviorType?.FullName), evt.newValue);
        });

        var toggle = section.Q<Toggle>();
        toggle.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            if (behaviorType != null)
                evt.menu.AppendAction("Remove Behavior", _ => RemoveBehavior(path, behaviorType));
            if (behaviorType != null)
                evt.menu.AppendAction("Edit Script", _ => OpenScript(behaviorType));
        }));

        if (behaviorType == null || metadata == null)
            return section;

        var behaviorItem = new DockItem { Kind = DockItem.ItemKind.Behavior, Path = path, Behavior = behaviorType, GameObject = target };
        var handle = CreateHandle(EditorGUIUtility.IconContent("cs Script Icon").image, "Drag into the graph for a Behavior node with all its Variables and methods");
        MakeDraggable(handle, behaviorItem, () => _window.FocusNodes(behaviorItem));
        toggle.Add(handle);
        toggle.userData ??= behaviorItem;
        _rows[behaviorItem.Key] = toggle;

        if (behaviorType == typeof(StateMachine))
        {
            var open = new Button(() =>
            {
                var definition = StateMachineEditorUtility.DefinitionFor(_root, target, create: true);
                if (definition != null)
                    StateMachineWindow.Open(definition);
            }) { text = "Open State Machine", tooltip = "The State Manager and State Graph for this object (created if it has none)." };
            section.Add(open);
        }

        var reflection = BehaviorReflection.Get(behaviorType);
        var properties = new List<VariableDefinition>();

        // Variables, each with this object's value right on its row.
        bool anyVariable = false;
        foreach (var variable in metadata.Variables)
        {
            if (reflection != null && reflection.IsPropertiesVariable(variable.Name))
            {
                properties.Add(variable);
                continue;
            }
            if (!anyVariable)
            {
                section.Add(SectionLabel("Variables"));
                anyVariable = true;
            }

            var item = new DockItem
            {
                Kind = DockItem.ItemKind.Variable, Path = path, Behavior = behaviorType, GameObject = target,
                Name = variable.Name, ValueType = variable.Type.Type,
            };
            var row = CreateRow(item, VariableIcon, ObjectNames.NicifyVariableName(variable.Name), TypeName(item.ValueType) + (variable.Networked ? "  ⇅" : ""),
                "Variable: drag the name into the graph to Get or Set it; click it to find its nodes. The field is this object's starting value.");
            string variableName = variable.Name;
            var definition = variable;
            var field = new IMGUIContainer(() =>
            {
                // Prefab Mode open: the Inspector's own field for it (Undo, Prefab Mode saves it).
                if (_live)
                    SchematicBehaviorsHeaderGUI.DrawVariableGUI(_root, target, attachment, definition.Name);
                else
                    DrawVariableField(path, behaviorType, attachment, definition);
            });
            ConstrainIMGUI(field);
            field.AddToClassList("dock-variable-value");
            row.Add(field);
            section.Add(row);
        }

        if (metadata.Signals.Count > 0)
        {
            section.Add(SectionLabel("Signals"));
            foreach (var signal in metadata.Signals)
            {
                var item = new DockItem
                {
                    Kind = DockItem.ItemKind.Signal, Path = path, Behavior = behaviorType, GameObject = target,
                    Name = signal.Name, Signal = signal.Asset, Direction = signal.Direction,
                };
                var parameters = new List<string>();
                foreach (var parameter in signal.Parameters)
                    parameters.Add($"{TypeName(parameter.Type.Type)} {parameter.Name}");
                var icon = signal.Direction == SignalDirection.Output ? OutputIcon : InputIcon;
                string direction = signal.Direction == SignalDirection.Output ? "Output" : signal.Direction == SignalDirection.Input ? "Input" : "";
                var row = CreateRow(item, icon, ObjectNames.NicifyVariableName(signal.Name), string.Join(", ", parameters),
                    $"{direction} Signal: drag in to Send it or react to it; click to find its nodes");
                section.Add(row);
            }
        }

        // Anything the Behavior adds itself (BehaviorDockSections).
        var extra = BehaviorDockSections.Create(new BehaviorDockContext
        {
            Prefab = _prefab, Target = target, Path = path, BehaviorType = behaviorType, Attachment = attachment, Window = _window,
            Read = (variable, type) =>
            {
                if (_pendingEdits.TryGetValue(EditKey(path, behaviorType, variable), out var pending))
                    return pending.Revert ? metadata.FindVariable(variable)?.Default?.Get(type) : pending.Value;
                var entry = attachment.FindOverride(variable);
                return entry != null ? entry.Value.Get(type) : metadata.FindVariable(variable)?.Default?.Get(type);
            },
            Write = (variable, type, value) => QueueEdit(path, behaviorType, variable, type, value),
        });
        if (extra != null)
            section.Add(extra);

        // Properties last: the Behavior's tuning asset, created for this object if it has none, edited in place.
        if (properties.Count > 0)
            section.Add(CreatePropertiesFoldout(target, path, behaviorType, attachment, properties));

        return section;
    }

    private VisualElement CreateRow(DockItem item, Texture icon, string label, string detail, string tooltip)
    {
        var row = DockRows.Build(item, icon, label, detail, tooltip, () =>
        {
            _window.FocusNodes(item);
            ScopeItemInspector.Show(item);
        });
        _rows[item.Key] = row;
        return row;
    }

    private Foldout CreatePropertiesFoldout(GameObject target, string path, Type behaviorType, BehaviorAttachment attachment, List<VariableDefinition> properties)
    {
        var foldout = new Foldout { text = "Properties", value = SessionState.GetBool(FoldoutKey("properties", path + behaviorType.FullName), false) };
        foldout.AddToClassList("dock-properties");
        foldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == foldout)
                SessionState.SetBool(FoldoutKey("properties", path + behaviorType.FullName), evt.newValue);
        });

        foreach (var variable in properties)
        {
            var type = variable.Type.Type;
            string variableName = variable.Name;

            // Every object gets its own Properties asset, as before; made the first time the dock shows it.
            string createKey = $"{_assetPath}|{path}|{behaviorType.FullName}|{variableName}";
            if (CurrentProperties(attachment, variable) == null && type != null && !type.IsAbstract && !Application.isPlaying && _propertiesAttempted.Add(createKey))
                EditorApplication.delayCall += () => CreateProperties(target, path, behaviorType, attachment, variableName, type);

            UnityEditor.Editor editor = null;
            var container = new IMGUIContainer(() =>
            {
                var current = _pendingEdits.TryGetValue(EditKey(path, behaviorType, variableName), out var pendingProperties) && !pendingProperties.Revert
                    ? pendingProperties.Value as ScriptableObject
                    : CurrentProperties(attachment, variable);
                if (properties.Count > 1)
                    EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(variableName), EditorStyles.boldLabel);

                // The asset itself, swappable for another one.
                EditorGUI.BeginChangeCheck();
                var picked = EditorGUILayout.ObjectField(current, type, false) as ScriptableObject;
                if (EditorGUI.EndChangeCheck())
                {
                    QueueEdit(path, behaviorType, variableName, type, picked);
                    current = picked;
                }

                if (current == null)
                {
                    EditorGUILayout.LabelField("Creating…", EditorStyles.miniLabel);
                    return;
                }

                UnityEditor.Editor.CreateCachedEditor(current, null, ref editor);
                editor.OnInspectorGUI();
            });
            ConstrainIMGUI(container);
            foldout.Add(container);
        }
        return foldout;
    }

    private static ScriptableObject CurrentProperties(BehaviorAttachment attachment, VariableDefinition variable)
    {
        var type = variable.Type.Type;
        var entry = attachment.FindOverride(variable.Name);
        if (entry?.Value != null && entry.Value.Get(type) is ScriptableObject own)
            return own;
        return variable.Default?.Get(type) as ScriptableObject;
    }

    // Each Properties asset is attempted once per editor session, so a failure can't retry in a loop.
    private static readonly HashSet<string> _propertiesAttempted = new();

    private void CreateProperties(GameObject target, string path, Type behaviorType, BehaviorAttachment attachment, string variableName, Type type)
    {
        if (_root == null || attachment.FindOverride(variableName)?.Value?.Get(type) != null)
            return;

        // Not during Play Mode: give up for now (the dock tries again next time it's drawn).
        if (Application.isPlaying)
        {
            _propertiesAttempted.Remove($"{_assetPath}|{path}|{behaviorType.FullName}|{variableName}");
            return;
        }

        // Not in the middle of a compile, import or prefab save: come back once things are quiet.
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || _editing)
        {
            EditorApplication.delayCall += () => CreateProperties(target, path, behaviorType, attachment, variableName, type);
            return;
        }

        try
        {
            CreatePropertiesNow(path, behaviorType, attachment, variableName, type);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, _prefab);
        }
    }

    private void CreatePropertiesNow(string path, Type behaviorType, BehaviorAttachment attachment, string variableName, Type type)
    {

        // Stored with the prefab's other Schematic assets, one per Behavior (and GameObject) that uses it.
        string scope = behaviorType.Name + (string.IsNullOrEmpty(path) ? "" : "_" + path.Replace('/', '_'));
        var asset = SchematicAssets.SchematicAssetManager.Create(_prefab, scope, variableName, variableName, type);
        if (asset == null)
            return;

        QueueEdit(path, behaviorType, variableName, type, asset);
        FlushPendingEdits();
    }

    // IMGUI asks for the width it would like; inside the dock it must take what it's given.
    private static void ConstrainIMGUI(IMGUIContainer container)
    {
        container.style.flexGrow = 1;
        container.style.flexShrink = 1;
        container.style.minWidth = 0;
        container.style.overflow = Overflow.Hidden;
    }

    private HashSet<Type> AttachedTypes(GameObject target)
    {
        var attached = new HashSet<Type>();
        if (_root.Behaviors.TryGetValue(target, out var list) && list != null)
            foreach (var attachment in list.Items)
                if (attachment?.Metadata != null && attachment.Metadata.BehaviorType.Type != null)
                    attached.Add(attachment.Metadata.BehaviorType.Type);
        return attached;
    }

    #endregion

    #region Navigation

    /// <summary>
    /// Opens the foldouts down to the row for the given item, scrolls to it and flashes it. False if it isn't in this dock.
    /// </summary>
    public bool Reveal(string key)
    {
        if (!_rows.TryGetValue(key, out var row))
            return false;

        for (var parent = row.parent; parent != null; parent = parent.parent)
            if (parent is Foldout foldout)
                foldout.value = true;

        row.schedule.Execute(() =>
        {
            row.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(row);
            Flash(row);
        }).StartingIn(30);
        return true;
    }

    private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> _flashing = new();

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

    #region Helpers

    private static void MakeDraggable(VisualElement element, DockItem item, Action click) => DockRows.MakeDraggable(element, item, click);

    private static Image CreateHandle(Texture icon, string tooltip)
    {
        var handle = new Image { image = icon, tooltip = tooltip };
        handle.AddToClassList("dock-drag-handle");
        handle.style.width = 16;
        handle.style.height = 16;
        handle.style.marginLeft = 6;
        return handle;
    }

    private static Label SectionLabel(string text) => DockRows.SectionLabel(text);

    internal static Color ColorOf(Type type) => DockRows.ColorOf(type);

    private static string TypeName(Type type) => DockRows.TypeName(type);

    private string PathOf(Transform transform) => AnimationUtility.CalculateTransformPath(transform, _shown.transform);

    private string FoldoutKey(string kind, string path) => $"SchematicDock.{_assetPath}.{kind}.{path}";

    private static void OpenScript(Type type)
    {
        foreach (var guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type)
            {
                AssetDatabase.OpenAsset(script);
                return;
            }
        }
    }

    #endregion

    #region Prefab editing

    // Structural edits (children, Behaviors) go through the prefab's contents, as the original dock did: load it,
    // change the copy, save it over the asset, then reimport and redraw.
    private void EditContents(Action<GameObject> edit)
    {
        if (edit != null)
            _queuedEdits.Add(edit);

        // Busy (another save still finishing, a compile, or the import a save just started): the edit waits its turn
        // instead of being dropped, and goes in with the next save.
        if (_editing || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            ScheduleRetry();
            return;
        }

        _editing = true;

        var edits = _queuedEdits.ToArray();
        _queuedEdits.Clear();
        // Value edits waiting to be written ride along with this save.
        var pending = new List<PendingEdit>(_pendingEdits.Values);
        _pendingEdits.Clear();
        _flushScheduled?.Pause();

        int trace = PrefabSaveTrace.Begin($"dock edit ({edits.Length} change(s), {pending.Count} value(s))", _assetPath);
        PrefabSaveTrace.Step(trace, "1 before load", _assetPath, dockObject: _prefab);

        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(_assetPath);
            PrefabSaveTrace.Step(trace, "2 loaded copy", _assetPath, dockObject: _prefab, contents: contents);
            ApplyEdits(contents, pending);
            foreach (var queued in edits)
                queued(contents);
            PrefabSaveTrace.Step(trace, "3 copy edited", _assetPath, dockObject: _prefab, contents: contents);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (contents != null)
                PrefabUtility.UnloadPrefabContents(contents);
            Finish();
            return;
        }
        Save(contents, trace);
    }

    private readonly List<Action<GameObject>> _queuedEdits = new();
    private bool _retryScheduled;

    // Tries again on the next editor update, until the prefab is free.
    private void ScheduleRetry()
    {
        if (_retryScheduled)
            return;
        _retryScheduled = true;
        EditorApplication.delayCall += () =>
        {
            _retryScheduled = false;
            if (_queuedEdits.Count > 0 || _pendingEdits.Count > 0)
                EditContents(null);
        };
    }

    // One load-edit-save at a time; saving direct edits waits until it's done.
    private static bool _editing;

    private static void Finish()
    {
        _editing = false;
    }

    #region Value edits

    // A Variable value (or Properties asset) waiting to be written to the prefab. Shown in the dock straight away,
    // written with the next load-edit-save: after a short pause, when the mouse leaves the dock, or with a structural
    // edit. The prefab asset in memory is never modified directly.
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
    private IVisualElementScheduledItem _flushScheduled;
    private const long FlushDelayMs = 1500;

    private static string EditKey(string path, Type behavior, string variable) => $"{path}|{behavior?.FullName}|{variable}";

    private void QueueEdit(string path, Type behavior, string variable, Type valueType, object value, bool revert = false)
    {
        if (_live)
        {
            ApplyLive(path, behavior, variable, valueType, value, revert);
            return;
        }

        _pendingEdits[EditKey(path, behavior, variable)] = new PendingEdit
        {
            Path = path, Behavior = behavior, Variable = variable, ValueType = valueType, Value = value, Revert = revert,
        };

        // Restarted by every change, so typing finishes before anything is written.
        _flushScheduled?.Pause();
        _flushScheduled = schedule.Execute(FlushPendingEdits);
        _flushScheduled.ExecuteLater(FlushDelayMs);
    }

    // Prefab Mode open: a value goes straight into the open copy, with Undo, as the Inspector does.
    private void ApplyLive(string path, Type behavior, string variable, Type valueType, object value, bool revert)
    {
        var target = _shown != null ? Find(_shown, path) : null;
        var attachment = target != null && _root != null ? _root.FindAttachment(behavior, target.gameObject) : null;
        if (attachment == null)
            return;
        Undo.RecordObject(_root, revert ? "Revert Schematic Variable" : "Set Schematic Variable");
        if (revert)
            attachment.Overrides.RemoveAll(o => o.Name == variable);
        else
            attachment.SetOverride(variable, value, valueType);
        MarkLiveDirty();
    }

    private void FlushPendingEdits()
    {
        _flushScheduled?.Pause();
        if (_pendingEdits.Count == 0 || string.IsNullOrEmpty(_assetPath))
            return;
        EditContents(null);
    }

    private void ApplyEdits(GameObject contents, List<PendingEdit> edits)
    {
        if (edits.Count == 0)
            return;
        var root = contents.GetComponent<SchematicObject>();
        if (root == null)
            return;

        foreach (var edit in edits)
        {
            var target = Find(contents, edit.Path);
            var attachment = target != null ? root.FindAttachment(edit.Behavior, target.gameObject) : null;
            if (attachment == null)
                continue;

            if (edit.Revert)
                attachment.Overrides.RemoveAll(o => o.Name == edit.Variable);
            else
                attachment.SetOverride(edit.Variable, ToContents(edit.Value, contents), edit.ValueType);
        }
    }

    // A reference to one of the prefab's own objects has to point at the matching object in the loaded copy.
    private object ToContents(object value, GameObject contents)
    {
        Transform source = value is GameObject gameObject ? gameObject.transform : value is Component component ? component.transform : null;
        if (source == null || _prefab == null || source.root != _prefab.transform)
            return value;

        var match = Find(contents, AnimationUtility.CalculateTransformPath(source, _prefab.transform));
        if (match == null)
            return null;
        return value is GameObject ? (object)match.gameObject : match.GetComponent(value.GetType());
    }

    // The field on a Variable's row: this object's value (or the default), edited through pending edits.
    private void DrawVariableField(string path, Type behaviorType, BehaviorAttachment attachment, VariableDefinition definition)
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

        _pendingEdits.TryGetValue(EditKey(path, behaviorType, definition.Name), out var pending);
        var entry = attachment.FindOverride(definition.Name);
        bool overridden = pending != null ? !pending.Revert : entry != null;
        object current = pending != null && !pending.Revert ? pending.Value
                       : entry != null && pending == null ? entry.Value.Get(type)
                       : definition.Default?.Get(type);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (SchematicValueField.Draw(GUIContent.none, type, current, out var newValue))
                QueueEdit(path, behaviorType, definition.Name, type, newValue);

            using (new EditorGUI.DisabledScope(!overridden))
                if (GUILayout.Button(new GUIContent("↺", "Back to the Behavior's default"), EditorStyles.miniButton, GUILayout.Width(20)))
                    QueueEdit(path, behaviorType, definition.Name, type, null, revert: true);
        }
    }

    #endregion

    private static Transform Find(GameObject contents, string path)
        => string.IsNullOrEmpty(path) ? contents.transform : contents.transform.Find(path);

    private void AddChild(string parentPath)
    {
        if (_live)
        {
            var parent = _shown != null ? Find(_shown, parentPath) : null;
            if (parent == null)
                return;
            var child = new GameObject("New Child");
            child.transform.SetParent(parent, false);   // into Prefab Mode's scene, with its parent
            Undo.RegisterCreatedObjectUndo(child, "Add Child");
            AfterLiveEdit();
            return;
        }

        EditContents(contents =>
        {
            var parent = Find(contents, parentPath);
            if (parent != null)
                new GameObject("New Child").transform.SetParent(parent, false);
        });
    }

    private void DeleteObject(string path)
    {
        if (_live)
        {
            var target = _shown != null ? Find(_shown, path) : null;
            if (target == null || target == _shown.transform)
                return;
            if (_root != null)
            {
                Undo.RecordObject(_root, "Delete Object");
                foreach (var t in target.GetComponentsInChildren<Transform>(true))
                    _root.Behaviors.Remove(t.gameObject);
            }
            Undo.DestroyObjectImmediate(target.gameObject);
            AfterLiveEdit();
            return;
        }

        EditContents(contents =>
        {
            var target = Find(contents, path);
            if (target == null) return;
            // Drop the Behaviors attached to it (and its children) from the root's list first.
            var root = contents.GetComponent<SchematicObject>();
            if (root != null)
                foreach (var t in target.GetComponentsInChildren<Transform>(true))
                    root.Behaviors.Remove(t.gameObject);
            UnityEngine.Object.DestroyImmediate(target.gameObject);
        });
    }

    private void Rename(VisualElement anchor, string path, string current)
    {
        Add(new InlineIdentifierEditor(popupRect: anchor.WorldBoundToScreen(),
                                       identifierType: ListIdentifierType.Name,
                                       getOriginalValue: () => current,
                                       onFinishedEditting: (bool changed, object value) =>
                                       {
                                           string name = value?.ToString();
                                           if (string.IsNullOrWhiteSpace(name) || name == current) return;
                                           if (_live)
                                           {
                                               var live = _shown != null ? Find(_shown, path) : null;
                                               if (live == null) return;
                                               Undo.RecordObject(live.gameObject, "Rename");
                                               live.name = name;
                                               AfterLiveEdit();
                                               return;
                                           }
                                           EditContents(contents =>
                                           {
                                               var target = Find(contents, path);
                                               if (target != null)
                                                   target.name = name;
                                           });
                                       }));
    }

    internal void AddBehavior(string path, Type behaviorType)
    {
        // Prefab Mode open: the Inspector's own add (required Behaviors and components, OnSetup, Undo).
        if (_live)
        {
            var target = _shown != null && _root != null ? Find(_shown, path) : null;
            if (target == null)
                return;
            SchematicBehaviorsHeaderGUI.AttachWithUndo(_root, target.gameObject, behaviorType);
            AfterLiveEdit();
            return;
        }

        // Metadata first: attaching needs it, and generating it touches the asset database.
        if (BehaviorMetadataGenerator.GetOrCreate(behaviorType) == null)
            return;

        EditContents(contents =>
        {
            var root = contents.GetComponent<SchematicObject>();
            var target = Find(contents, path);
            if (root != null && target != null)
                root.AttachBehavior(behaviorType, target.gameObject);
        });
    }

    private void RemoveBehavior(string path, Type behaviorType)
    {
        if (_live)
        {
            var target = _shown != null && _root != null ? Find(_shown, path) : null;
            var attachment = target != null ? _root.FindAttachment(behaviorType, target.gameObject) : null;
            if (attachment == null)
                return;
            SchematicBehaviorsHeaderGUI.RemoveWithUndo(_root, target.gameObject, attachment);
            AfterLiveEdit();
            return;
        }

        EditContents(contents =>
        {
            var root = contents.GetComponent<SchematicObject>();
            var target = Find(contents, path);
            if (root == null || target == null || !root.Behaviors.TryGetValue(target.gameObject, out var list) || list == null)
                return;
            list.Items.RemoveAll(a => a?.Metadata != null && a.Metadata.BehaviorType.Type == behaviorType);
            if (list.Items.Count == 0)
                root.Behaviors.Remove(target.gameObject);
        });
    }

    // After an edit of the open Prefab Mode copy: marked for Prefab Mode to save, and redrawn.
    private void AfterLiveEdit()
    {
        MarkLiveDirty();
        _signature = Signature();
        Rebuild();
    }

    private async void Save(GameObject contents, int trace = 0)
    {
        // Always finishes, even if a step throws: an unfinished save would hold every later save back.
        try
        {
            PrefabUtility.SaveAsPrefabAsset(contents, _assetPath);
            PrefabSaveTrace.Step(trace, "4 saved copy over file", _assetPath, dockObject: _prefab, contents: contents);
            PrefabUtility.UnloadPrefabContents(contents);

            AssetDatabase.Refresh();
            PrefabSaveTrace.Step(trace, "5 after Refresh", _assetPath, dockObject: _prefab);

            await EditorUtilities.NextEditorFrame();
            PrefabSaveTrace.Step(trace, "6 next frame", _assetPath, dockObject: _prefab);

            PrefabRefreshUtility.ReimportAndResetPrefab(_prefab);
            PrefabSaveTrace.Step(trace, "7 after ReimportAndResetPrefab", _assetPath, dockObject: _prefab);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            Finish();
        }
        _window.RedrawIODock();
        PrefabSaveTrace.Step(trace, "8 after dock redraw", _assetPath, dockObject: _window.Prefab);
    }

    #endregion
}
