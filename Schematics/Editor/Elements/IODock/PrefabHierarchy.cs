using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The IO Dock's Prefab tab: the prefab's GameObjects, each with the Schematic Behaviors attached to it, and under each
/// Behavior its Variables (above) and Signals. Every row can be dragged into the graph to make a node, and clicked to
/// jump between the graph nodes that use it. GameObjects can be dragged in too, as Prefab Object nodes.
/// </summary>
public class PrefabHierarchy : VisualElement
{
    private readonly GameObject _prefab;
    private readonly SchematicObject _root;
    private readonly string _assetPath;
    private readonly SchematicGraphEditorWindow _window;

    // Every row by its DockItem key, so the graph can reveal the row a node uses.
    private readonly Dictionary<string, VisualElement> _rows = new();

    private static Texture2D _inputIcon, _outputIcon, _variableIcon;
    private static Texture2D InputIcon => _inputIcon ??= Resources.Load<Texture2D>("Icons/Input");
    private static Texture2D OutputIcon => _outputIcon ??= Resources.Load<Texture2D>("Icons/Output");
    internal static Texture2D VariableIcon => _variableIcon ??= MakeDot(new Color(0.55f, 0.85f, 1f));

    public PrefabHierarchy(GameObject prefab, SchematicGraphEditorWindow window)
    {
        _prefab = prefab;
        _window = window;
        _assetPath = AssetDatabase.GetAssetPath(prefab);
        _root = prefab != null ? prefab.GetComponent<SchematicObject>() : null;

        AddToClassList("behavior-dock");
        Rebuild();
    }

    public void Rebuild()
    {
        Clear();
        _rows.Clear();

        if (_root == null)
        {
            Add(new HelpBox("This prefab has no Schematic Object component, so it has no Schematic Behaviors.", HelpBoxMessageType.Warning));
            if (_prefab != null)
                Add(new Button(() =>
                {
                    var contents = PrefabUtility.LoadPrefabContents(_assetPath);
                    contents.AddComponent<SchematicObject>();
                    Save(contents);
                }) { text = "Add Schematic Object" });
            return;
        }

        Add(CreateObjectFoldout(_prefab.transform));
    }

    #region GameObjects

    private VisualElement CreateObjectFoldout(Transform transform)
    {
        string path = PathOf(transform);
        var foldout = new Foldout
        {
            text = transform.name,
            value = SessionState.GetBool(FoldoutKey("go", path), transform == _prefab.transform),
        };
        foldout.AddToClassList("dock-object");
        foldout.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == foldout)
                SessionState.SetBool(FoldoutKey("go", path), evt.newValue);
        });

        // Drag the GameObject itself into the graph.
        var item = new DockItem { Kind = DockItem.ItemKind.GameObject, Path = path, GameObject = transform.gameObject };
        var toggle = foldout.Q<Toggle>();
        var handle = CreateHandle(EditorGUIUtility.IconContent("GameObject Icon").image, "Drag into the graph for a Prefab Object node");
        MakeDraggable(handle, item, click: null);
        toggle.Add(handle);
        _rows[item.Key] = toggle;

        toggle.AddManipulator(new ContextualMenuManipulator(evt =>
        {
            evt.menu.AppendAction("Add Child", _ => AddChild(path));
            evt.menu.AppendAction("Rename", _ => Rename(toggle, path, transform.name));
            if (transform != _prefab.transform)
                evt.menu.AppendAction("Delete", _ => DeleteObject(path));
            evt.menu.AppendAction("Select in Project", _ => Selection.activeObject = transform.gameObject);
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
            if (GUI.Button(rect, content, EditorStyles.miniButton))
                SchematicBehaviorsHeaderGUI.ShowAddBehaviorDropdown(rect, _root, target, _ => _window.RedrawIODock());
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
            evt.menu.AppendAction("Remove Behavior", _ =>
            {
                SchematicBehaviorsHeaderGUI.RemoveWithUndo(_root, target, attachment);
                _window.RedrawIODock();
            });
            if (behaviorType != null)
                evt.menu.AppendAction("Edit Script", _ => OpenScript(behaviorType));
        }));

        if (behaviorType == null || metadata == null)
            return section;

        var behaviorItem = new DockItem { Kind = DockItem.ItemKind.Behavior, Path = path, Behavior = behaviorType, GameObject = target };
        var handle = CreateHandle(EditorGUIUtility.IconContent("cs Script Icon").image, "Drag into the graph for a Behavior node with all its Variables and methods");
        MakeDraggable(handle, behaviorItem, () => _window.FocusNodes(behaviorItem));
        toggle.Add(handle);
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

        // The per-object values, as the Inspector shows them.
        if (metadata.Variables.Count > 0)
        {
            var values = new Foldout { text = "Values", value = false };
            values.AddToClassList("dock-values");
            values.Add(new IMGUIContainer(() => SchematicBehaviorsHeaderGUI.DrawVariablesGUI(_root, target, attachment)));
            section.Add(values);
        }

        if (metadata.Variables.Count > 0)
        {
            section.Add(SectionLabel("Variables"));
            foreach (var variable in metadata.Variables)
            {
                var item = new DockItem
                {
                    Kind = DockItem.ItemKind.Variable, Path = path, Behavior = behaviorType, GameObject = target,
                    Name = variable.Name, ValueType = variable.Type.Type,
                };
                section.Add(CreateRow(item, VariableIcon, $"{ObjectNames.NicifyVariableName(variable.Name)}", TypeName(item.ValueType), "Variable: drag in to Get or Set it; click to find its nodes"));
            }
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
                section.Add(CreateRow(item, icon, ObjectNames.NicifyVariableName(signal.Name), string.Join(", ", parameters),
                    $"{direction} Signal: drag in to Send it or react to it; click to find its nodes"));
            }
        }

        return section;
    }

    private VisualElement CreateRow(DockItem item, Texture icon, string label, string detail, string tooltip)
    {
        var row = new VisualElement { tooltip = tooltip };
        row.AddToClassList("dock-row");
        row.AddToClassList("dock-row-" + item.Kind.ToString().ToLowerInvariant());
        // The IO Dock stylesheet's Signal container look, shared by Variables (with their own tint).
        row.AddToClassList("event-container");
        if (item.Kind == DockItem.ItemKind.Signal)
            row.AddToClassList(item.Direction == SignalDirection.Output ? "event-container-output" : "event-container-input");
        else
            row.AddToClassList("event-container-variable");
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        if (item.Kind == DockItem.ItemKind.Variable)
        {
            var chip = new VisualElement();
            chip.AddToClassList("dock-type-chip");
            chip.style.width = 4;
            chip.style.height = 14;
            chip.style.marginRight = 4;
            chip.style.backgroundColor = ColorOf(item.ValueType);
            row.Add(chip);
        }

        var image = new Image { image = icon };
        image.style.width = 16;
        image.style.height = 16;
        image.style.marginRight = 4;
        row.Add(image);

        var name = new Label(label);
        name.AddToClassList("dock-row-name");
        name.AddToClassList("event-name");
        row.Add(name);

        var info = new Label(detail);
        info.AddToClassList("dock-row-detail");
        info.style.flexGrow = 1;
        info.style.unityTextAlign = TextAnchor.MiddleRight;
        info.style.color = new Color(0.65f, 0.65f, 0.65f);
        row.Add(info);

        if (item.Signal == null && item.Kind == DockItem.ItemKind.Signal)
        {
            row.SetEnabled(false);
            row.tooltip = "This Signal has no asset yet. Use Tools/Remedy/Schematics/Refresh Behavior Metadata.";
        }

        MakeDraggable(row, item, () => _window.FocusNodes(item));
        _rows[item.Key] = row;
        return row;
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

    internal static void Flash(VisualElement element)
    {
        var original = element.style.backgroundColor;
        float t = 0f;
        IVisualElementScheduledItem item = null;
        item = element.schedule.Execute(() =>
        {
            t += 0.06f;
            element.style.backgroundColor = Color.Lerp(new Color(0f, 0.55f, 0.6f, 0.9f), new Color(0, 0, 0, 0), t);
            if (t >= 1f)
            {
                element.style.backgroundColor = original;
                item.Pause();
            }
        }).Every(16);
    }

    #endregion

    #region Helpers

    // A press that moves starts a drag; one that doesn't is a click.
    private static void MakeDraggable(VisualElement element, DockItem item, Action click)
    {
        Vector2 start = default;
        bool pressed = false;

        element.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            pressed = true;
            start = evt.mousePosition;
            evt.StopPropagation();
        });
        element.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (!pressed || (evt.mousePosition - start).sqrMagnitude < 16f) return;
            pressed = false;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DockItem.DragKey, item);
            DragAndDrop.objectReferences = item.Signal != null ? new UnityEngine.Object[] { item.Signal }
                                         : item.GameObject != null ? new UnityEngine.Object[] { item.GameObject }
                                         : Array.Empty<UnityEngine.Object>();
            DragAndDrop.StartDrag(item.DisplayName);
            evt.StopPropagation();
        });
        element.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (!pressed) return;
            pressed = false;
            click?.Invoke();
            evt.StopPropagation();
        });
        element.RegisterCallback<MouseLeaveEvent>(_ => pressed = false);
    }

    private static Image CreateHandle(Texture icon, string tooltip)
    {
        var handle = new Image { image = icon, tooltip = tooltip };
        handle.AddToClassList("dock-drag-handle");
        handle.style.width = 16;
        handle.style.height = 16;
        handle.style.marginLeft = 6;
        return handle;
    }

    private static Label SectionLabel(string text)
    {
        var label = new Label(text);
        label.AddToClassList("dock-section-label");
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = 4;
        label.style.color = new Color(0.75f, 0.75f, 0.75f);
        return label;
    }

    internal static Color ColorOf(Type type)
    {
        if (type != null && SchematicGraphEditorWindow.ColorLookupValueType.TryGetValue(type, out var color))
            return color;
        if (type != null && type.IsEnum) return new Color(0.4f, 0.8f, 0.7f);
        if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)) return new Color(0.95f, 0.85f, 0.4f);
        return Color.gray;
    }

    private static string TypeName(Type type)
    {
        if (type == null) return "?";
        if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "List<" + TypeName(type.GetGenericArguments()[0]) + ">";
        return type.Name switch { "Single" => "float", "Int32" => "int", "Boolean" => "bool", "String" => "string", _ => type.Name };
    }

    private string PathOf(Transform transform) => AnimationUtility.CalculateTransformPath(transform, _prefab.transform);

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

    private static Texture2D MakeDot(Color color)
    {
        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        float radius = size * 0.5f - 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                // A ring with a dimmer centre reads as "a value" next to the Signal arrows.
                var c = d < radius - 3f ? color * new Color(1, 1, 1, 0.45f) : color;
                c.a *= a;
                texture.SetPixel(x, y, c);
            }
        texture.Apply();
        return texture;
    }

    #endregion

    #region Prefab editing

    private void AddChild(string parentPath)
    {
        var contents = PrefabUtility.LoadPrefabContents(_assetPath);
        var parent = string.IsNullOrEmpty(parentPath) ? contents.transform : contents.transform.Find(parentPath);
        if (parent != null)
            new GameObject("New Child").transform.SetParent(parent, false);
        Save(contents);
    }

    private void DeleteObject(string path)
    {
        var contents = PrefabUtility.LoadPrefabContents(_assetPath);
        var target = contents.transform.Find(path);
        if (target != null)
        {
            // Drop the Behaviors attached to it (and its children) from the root's list first.
            var root = contents.GetComponent<SchematicObject>();
            if (root != null)
                foreach (var t in target.GetComponentsInChildren<Transform>(true))
                    root.Behaviors.Remove(t.gameObject);
            UnityEngine.Object.DestroyImmediate(target.gameObject);
        }
        Save(contents);
    }

    private void Rename(VisualElement anchor, string path, string current)
    {
        Add(new InlineIdentifierEditor(popupRect: anchor.WorldBoundToScreen(),
                                       identifierType: ListIdentifierType.Name,
                                       getOriginalValue: () => current,
                                       onFinishedEditting: (bool changed, object value) =>
                                       {
                                           if (!changed || value == null) return;
                                           var contents = PrefabUtility.LoadPrefabContents(_assetPath);
                                           var target = string.IsNullOrEmpty(path) ? contents.transform : contents.transform.Find(path);
                                           if (target != null)
                                               target.name = value.ToString();
                                           Save(contents);
                                       }));
    }

    private void Save(GameObject contents)
    {
        PrefabUtility.SaveAsPrefabAsset(contents, _assetPath);
        PrefabUtility.UnloadPrefabContents(contents);
        _window.RedrawIODock();
    }

    #endregion
}
