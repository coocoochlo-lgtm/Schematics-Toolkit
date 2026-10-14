using Remedy.Framework;
using Remedy.Schematics;
using SchematicAssets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
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

    // Lazy loading tracking
    private SchematicGraphEditorWindow _schematicWindow;
    private EditorWindow _editorWindow;
    private bool _includeSelf;
    private List<LazyListFoldout> _foldouts = new();

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

        // Initialize caching structures
        var container = new VisualElement();

        if (editorWindow is SchematicGraphEditorWindow asSchematicWindow)
        {
            _schematicWindow = asSchematicWindow;
        }

        // No reimport here: a forced reimport throws away whatever of the prefab is only in memory (e.g. the graph link the
        // window sets), and resetting the Prefab Mode stage discards its unsaved changes.
        RebuildHierarchy(_prefab.transform);

        Add(container);

        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            _watch ??= schedule.Execute(Watch).Every(WatchMs);
            _watch.Resume();
        });
        RegisterCallback<DetachFromPanelEvent>(_ => _watch?.Pause());
    }


    public void RebuildHierarchy(Transform transform = null)
    {
        Clear();
        _foldouts.Clear();

        // While the prefab is open in Prefab Mode, its open copy is what's shown and edited (as the Inspector does):
        // Prefab Mode saves it. Otherwise the prefab asset.
        var live = LiveRoot();
        if (_prefab == null)
            _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
        _shown = live != null ? live : _prefab;
        _shownIsLive = live != null;
        if (_shown == null)
            return;

        var goFoldout = CreateGameObjectFoldout(_shown.transform, _schematicWindow, _editorWindow, _includeSelf);
        // The root object's Behaviors get the same tab row as its children's.
        AddComponentRow(goFoldout, _shown.transform, _schematicWindow);
        RestoreExpanded(goFoldout, _shown.transform);
        Add(goFoldout);

        _signature = Signature();
    }

    #region Prefab Mode, and following changes made elsewhere

    // The object the hierarchy shows: the Prefab Mode copy when the prefab is open there, otherwise the asset.
    private GameObject _shown;
    private bool _shownIsLive;

    private const long WatchMs = 500;
    private IVisualElementScheduledItem _watch;
    private int _signature;

    // Expanded objects (by path), so a rebuild doesn't fold everything up.
    private readonly HashSet<string> _expanded = new();
    private readonly HashSet<string> _expandedComponents = new();

    private GameObject LiveRoot()
    {
        if (string.IsNullOrEmpty(_assetPath))
            return null;
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        return stage != null && stage.assetPath == _assetPath ? stage.prefabContentsRoot : null;
    }

    private string PathOf(Transform tf) => _shown != null && tf != null ? _shown.transform.GetRelativePath(tf) : "";

    /// <summary>
    /// Twice a second: rebuilds when the prefab's objects or attached Behaviors changed elsewhere (the Inspector, Undo,
    /// a Prefab Mode save), or when Prefab Mode opened or closed on it. Values aren't watched (their fields show the
    /// current value anyway), so typing in a field never rebuilds.
    /// </summary>
    private void Watch()
    {
        if (_editing || panel == null || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        var live = LiveRoot();
        bool changedStage = (live != null) != _shownIsLive || (live != null && live != _shown) || _shown == null;
        if (changedStage || Signature() != _signature)
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

    // Re-expands what was expanded before a rebuild, and keeps track of what the user expands.
    private void RestoreExpanded(VisualElement objectContainer, Transform tf)
    {
        var path = PathOf(tf);

        if (objectContainer.Q<LazyListFoldout>() is LazyListFoldout objectFoldout)
        {
            objectFoldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != objectFoldout) return;
                if (evt.newValue) _expanded.Add(path); else _expanded.Remove(path);
            });
            if (_expanded.Contains(path))
            {
                objectFoldout.RenderContent(true);
                objectFoldout.SetValueWithoutNotify(true);
            }
        }

        if (objectContainer.Q<LazyFoldout>(className: "component-foldout") is LazyFoldout components)
        {
            components.RegisterValueChangedCallback(evt =>
            {
                if (evt.target != components) return;
                if (evt.newValue) _expandedComponents.Add(path); else _expandedComponents.Remove(path);
            });
            if (_expandedComponents.Contains(path))
            {
                components.RenderContent();
                components.SetValueWithoutNotify(true);
            }
        }
    }

    #endregion

    /// <summary>
    /// Puts an object's Behavior tab row ("components") under its foldout's header.
    /// </summary>
    private void AddComponentRow(VisualElement objectContainer, Transform tf, SchematicGraphEditorWindow schematicWindow)
    {
        var toggle = objectContainer.Q<Toggle>();
        if (toggle == null)
            return;

        var headerContainer = new VisualElement()
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                width =  Length.Percent(100)
            }
        };
        headerContainer.AddToClassList("child-header");

        var newToggle = new VisualElement()
        {
            style =
            {
                flexDirection = FlexDirection.Row
            }
        };
        var componentRow = new VisualElement()
        {
            style =
            {
                flexDirection = FlexDirection.Row
            }
        };

        componentRow.Add(CreateFoldoutContent(tf, schematicWindow));

        componentRow.Children().ElementAt(0).style.width = Length.Percent(100);

        foreach (var child in toggle.Children().ToList())
        {
            newToggle.Add(child);
        }

        headerContainer.Add(newToggle);
        headerContainer.Add(componentRow);

        toggle.Add(headerContainer);
    }

    /// <summary>
    /// Creates a GameObjectFoldout with buttons for a given Transform.
    /// </summary>
    private VisualElement CreateGameObjectFoldout(Transform tf, SchematicGraphEditorWindow schematicWindow, EditorWindow editorWindow, bool includeSelf)
    {
        if (tf == null) return null;

        var childListContainer = new VisualElement();

        var childListFoldout = new LazyListFoldout(collection: tf.GetComponentsInImmediateChildren<Transform>(),
                                                    className: "game-object",
                                                    title: tf.name,

                                                    onItemAdded: (object item) =>
                                                    {
                                                    },

                                                    onItemRemoved: (object item) =>
                                                    {
                                                        var tfPath = PathOf((Transform)item);
                                                        EditPrefab("Delete Object", (root, live) =>
                                                        {
                                                            var target = FindInCopy(root, tfPath);
                                                            if (target == null || target == root.transform)
                                                                return;

                                                            // Its Behaviors (and its children's) go with it.
                                                            var schematic = root.GetComponent<SchematicObject>();
                                                            if (schematic != null)
                                                            {
                                                                if (live) Undo.RecordObject(schematic, "Delete Object");
                                                                foreach (var t in target.GetComponentsInChildren<Transform>(true))
                                                                    schematic.Behaviors.Remove(t.gameObject);
                                                            }

                                                            if (live) Undo.DestroyObjectImmediate(target.gameObject);
                                                            else GameObject.DestroyImmediate(target.gameObject);
                                                        });
                                                    },

                                                    // Setup Item/VisualElements
                                                    createItemInstance: (Action<object> onCreationFinished) =>
                                                    {
                                                        var parentPath = PathOf(tf);

                                                        new InlineIdentifierEditor(popupRect: childListContainer.Q<Toggle>().WorldBoundToScreen(),
                                                                                        identifierType: ListIdentifierType.Name,
                                                                                        getOriginalValue: () => "GameObject",
                                                                                        onFinishedEditting: (bool success, object value) =>
                                                                                        {
                                                                                            var name = value?.ToString();
                                                                                            if (string.IsNullOrWhiteSpace(name))
                                                                                                return;

                                                                                            // The popup reports from its OnDisable; the prefab is edited afterwards.
                                                                                            EditorApplication.delayCall += () => EditPrefab("Add Child", (root, live) =>
                                                                                            {
                                                                                                var parent = FindInCopy(root, parentPath);
                                                                                                if (parent == null)
                                                                                                    return;
                                                                                                var child = new GameObject(name);
                                                                                                child.transform.SetParent(parent, false);
                                                                                                if (live) Undo.RegisterCreatedObjectUndo(child, "Add Child");
                                                                                            });
                                                                                        });
                                                    },

                                                    createItemContent: (int index, object item) =>
                                                    {
                                                        var childTf = (Transform)item;
                                                        var objectContainer = CreateGameObjectFoldout(childTf, schematicWindow, editorWindow, includeSelf);

                                                        // Double-click the object's name to rename it.
                                                        var nameLabel = objectContainer.Q<Label>(className: "list-label");
                                                        nameLabel?.RegisterCallback<PointerDownEvent>(evt =>
                                                        {
                                                            if (evt.button != 0 || evt.clickCount != 2)
                                                                return;
                                                            evt.StopPropagation();
                                                            RenameObject(childTf, nameLabel);
                                                        });

                                                        AddComponentRow(objectContainer, childTf, schematicWindow);
                                                        RestoreExpanded(objectContainer, childTf);

                                                        return objectContainer;
                                                    },

                                                    getItemName: (object item) =>
                                                    {
                                                        return ((Transform)item) == null ? "" : ((Transform)item).name;
                                                    },

                                                    itemLabelClicked: (MouseDownEvent evt, object item, Action onItemEditted) =>
                                                    {
                                                        if (evt.clickCount == 2 && evt.button == 0)
                                                            RenameObject((Transform)item, childListContainer);
                                                    },
                                                    getListIcon: () => { return EditorGUIUtility.IconContent("GameObject Icon").image as Texture2D; } );


        _foldouts.Add(childListFoldout);

        childListContainer.Add(childListFoldout);

        return childListContainer;
    }

    private VisualElement CreateFoldoutContent(Transform tf, SchematicGraphEditorWindow schematicWindow)
    {
        var behaviorsOnObject = GetBehaviors(tf.gameObject);

        var foldout = new LazyFoldout(() =>
        {
            var container = new VisualElement();
            var ioGroup = new VisualElement();

            TabView tabView = new("", "components", true, HideTabLabels);

            int compCount = 0;
            foreach (var attachment in behaviorsOnObject)
            {
              // One bad Behavior (a renamed script, a throwing renderer) doesn't hide the others.
              try
              {
                var type = attachment.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                var typeName = type != null ? ObjectNames.NicifyVariableName(type.Name)
                             : attachment.Metadata != null ? $"{attachment.Metadata.name} (Missing Script)" : "(Missing Behavior)";
                var icon = type != null ? GetBehaviorIcon(type) : null;

                // Tab ids must be unique.
                while (TabExists(tabView, typeName))
                    typeName += " ";

                var tab = tabView.AddTab(typeName, icon);
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

                compCount++;
              }
              catch (Exception exception)
              {
                  Debug.LogException(exception, _shown);
              }
            }

            // Add "+" tab for adding Behaviors
            CreateAddBehaviorButton(tabView, tf);

            container.Add(tabView);

            return container;
        })
        {
            text = "components"
        };

        foldout.AddToClassList("component-foldout");

        return foldout;
    }

    /// <summary>
    /// Creates the actual content for a Behavior tab.
    /// </summary>
    private VisualElement CreateTabContent(BehaviorAttachment attachment, Type type, Transform tf, SchematicGraphEditorWindow schematicWindow)
    {
        var dock = new VisualElement();

        if (type != null && evMap.ContainsKey(tf) && evMap[tf].ContainsKey(type))
        {
            return evMap[tf][type];
        }

        var metadata = attachment.Metadata;

        var label = new Label(type != null ? type.Name : metadata != null ? $"{metadata.name} (Missing Script)" : "(Missing Behavior)");
        dock.Add(label);

        if (type == null || metadata == null)
            return dock;

        var (draw, dockElement) = RenderBehaviorDock(type, schematicWindow, metadata);
        SignalRenderer.DrawDelayedEvents();

        var propertiesVariables = GetPropertiesVariables(type, metadata);

        if (draw || propertiesVariables.Count > 0)
        {
            if (dockElement == null)
                dockElement = new VisualElement();

            foreach (var propertiesVariable in propertiesVariables)
            {
                var propertiesFoldout = CreatePropertiesFoldout(attachment, type, propertiesVariable, tf);
                dockElement.Add(propertiesFoldout);
            }

            dock.Add(dockElement);
        }

        return dock;
    }

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
    /// Creates a foldout for Behavior properties.
    /// </summary>
    private Foldout CreatePropertiesFoldout(BehaviorAttachment attachment, Type type, VariableDefinition propertiesVariable, Transform tf)
    {
        var propertiesType = propertiesVariable.Type.Type;
        var variableName = propertiesVariable.Name;

        var propertiesFoldout = new Foldout()
        {
            text = variableName == "Properties" ? "Properties" : ObjectNames.NicifyVariableName(variableName),
            value = false,
            style =
            {
                width = Length.Percent(100),
                flexGrow = 0,
                flexShrink = 0
            }
        };

        propertiesFoldout.AddManipulator(new ContextualMenuManipulator(evt =>
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

        var container = new IMGUIContainer(() =>
        {
            if (propertiesObject == null) return;

            var soEditor = Editor.CreateEditor(propertiesObject);
            soEditor.OnInspectorGUI();
        });

        propertiesFoldout.Add(container);
        return propertiesFoldout;
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
        tabView.AddTab("+").UseFactory(() =>
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

            return null;
        });

        var addTabButton = tabView;

        addTabButton.AddToClassList("tab-add-component");
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

    /// <summary>Renames a GameObject of the prefab (double-click its name).</summary>
    private void RenameObject(Transform tf, VisualElement anchor)
    {
        if (tf == null)
            return;
        var tfPath = PathOf(tf);
        if (string.IsNullOrEmpty(tfPath))
            return; // The root is named after the prefab file.
        var current = tf.name;

        new InlineIdentifierEditor(popupRect: anchor.WorldBoundToScreen(),
                                   identifierType: ListIdentifierType.Name,
                                   getOriginalValue: () => current,
                                   onFinishedEditting: (bool change, object newValue) =>
                                   {
                                       var name = newValue?.ToString();
                                       if (string.IsNullOrWhiteSpace(name) || name == current)
                                           return;

                                       // The popup reports from its OnDisable; the prefab is edited afterwards.
                                       EditorApplication.delayCall += () => EditPrefab("Rename", (root, live) =>
                                       {
                                           var target = FindInCopy(root, tfPath);
                                           if (target == null)
                                               return;
                                           if (live)
                                               Undo.RecordObject(target.gameObject, "Rename");
                                           target.name = name;
                                       });
                                   });
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

        if (root.Behaviors.TryGetValue(gameObject, out var list) && list?.Items != null)
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

    /// <summary>
    /// The Behavior script's own icon, if it has one (otherwise the tab shows the Behavior's name).
    /// </summary>
    private static Texture GetBehaviorIcon(Type type)
    {
        foreach (var guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script != null && script.GetClass() == type)
                return EditorGUIUtility.GetIconForObject(script);
        }
        return null;
    }

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

    // One load-edit-save at a time; edits made meanwhile wait and go in together with the next one.
    private static bool _editing;
    private readonly List<Action<GameObject, bool>> _queuedEdits = new();
    private bool _retryScheduled;

    /// <summary>
    /// Makes a change to the prefab. Open in Prefab Mode: on its open copy, with Undo (the same objects the Inspector
    /// edits; Prefab Mode saves them). Otherwise with the load-edit-save of the prefab file: load its contents, change
    /// them, save them over the asset, reimport, rebuild. The edit gets the root to change and whether it's the
    /// Prefab Mode copy.
    /// </summary>
    private void EditPrefab(string undoName, Action<GameObject, bool> edit)
    {
        var live = LiveRoot();
        if (live != null)
        {
            try
            {
                edit(live, true);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, live);
            }
            var schematic = live.GetComponent<SchematicObject>();
            if (schematic != null)
                EditorUtility.SetDirty(schematic);
            if (live.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(live.scene);
            RebuildHierarchy();
            return;
        }

        if (edit != null)
            _queuedEdits.Add(edit);

        if (_editing || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            ScheduleRetry();
            return;
        }
        if (_queuedEdits.Count == 0)
            return;

        _editing = true;
        var edits = _queuedEdits.ToArray();
        _queuedEdits.Clear();

        int trace = PrefabSaveTrace.Begin($"dock edit ({edits.Length} change(s))", _assetPath);
        GameObject copy = null;
        try
        {
            copy = PrefabUtility.LoadPrefabContents(_assetPath);
            PrefabSaveTrace.Step(trace, "loaded copy", _assetPath, dockObject: _prefab, contents: copy);
            KeepSchematicLink(copy);
            foreach (var queued in edits)
                queued(copy, false);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (copy != null)
                PrefabUtility.UnloadPrefabContents(copy);
            _editing = false;
            return;
        }
        ApplyChangesToPrefabAsset(copy, trace);
    }

    private void ScheduleRetry()
    {
        if (_retryScheduled)
            return;
        _retryScheduled = true;
        EditorApplication.delayCall += () =>
        {
            _retryScheduled = false;
            if (_queuedEdits.Count > 0)
                EditPrefab(null, null);
        };
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
    /// Saves the edited copy over the prefab asset, reimports it and rebuilds.
    /// </summary>
    private async void ApplyChangesToPrefabAsset(GameObject copy, int trace = 0)
    {
        try
        {
            PrefabUtility.SaveAsPrefabAsset(copy, _assetPath);
            PrefabSaveTrace.Step(trace, "saved copy over file", _assetPath, dockObject: _prefab, contents: copy);
            PrefabUtility.UnloadPrefabContents(copy);

            AssetDatabase.Refresh();

            await EditorUtilities.NextEditorFrame();

            if (_prefab == null)
                _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
            PrefabRefreshUtility.ReimportAndResetPrefab(_prefab);
            PrefabSaveTrace.Step(trace, "after reimport", _assetPath, dockObject: _prefab);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            _editing = false;
        }

        RebuildHierarchy();

        if (_queuedEdits.Count > 0)
            ScheduleRetry();
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
