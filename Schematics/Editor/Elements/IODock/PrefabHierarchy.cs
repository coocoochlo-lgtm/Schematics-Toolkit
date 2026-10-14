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

        RebuildHierarchy(_prefab.transform);

        PrefabRefreshUtility.ReimportAndResetPrefab(prefab);

        Add(container);
    }


    public void RebuildHierarchy(Transform transform)
    {
        Clear();
        var goFoldout = CreateGameObjectFoldout(transform, _schematicWindow, _editorWindow, _includeSelf);
        Add(goFoldout);
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
                                                        var tfPath = _prefab.transform.GetRelativePath((Transform)item);
                                                        var copy = PrefabUtility.LoadPrefabContents(_assetPath);

                                                        var tfCopy = copy.transform.Find(tfPath);
                                                        if (tfCopy != null)
                                                        {
                                                            // Its Behaviors (and its children's) go with it.
                                                            var rootCopy = copy.GetComponent<SchematicObject>();
                                                            if (rootCopy != null)
                                                                foreach (var t in tfCopy.GetComponentsInChildren<Transform>(true))
                                                                    rootCopy.Behaviors.Remove(t.gameObject);

                                                            GameObject.DestroyImmediate(tfCopy.gameObject);
                                                            ApplyChangesToPrefabAsset(copy);
                                                        }
                                                    },

                                                    // Setup Item/VisualElements
                                                    createItemInstance: (Action<object> onCreationFinished) =>
                                                    {
                                                        var childObject = new GameObject().transform;

                                                        Add(new InlineIdentifierEditor(popupRect: childListContainer.Q<Toggle>().WorldBoundToScreen(),
                                                                                        identifierType: ListIdentifierType.Name,
                                                                                        getOriginalValue: () => childObject.name,
                                                                                        onFinishedEditting: (bool success, object value) =>
                                                                                        {
                                                                                            childObject.name = value.ToString();

                                                                                            var tfPath = _prefab.transform.GetRelativePath(tf);
                                                                                            var copy = PrefabUtility.LoadPrefabContents(_assetPath);

                                                                                            var tfCopy = copy.transform.Find(tfPath);
                                                                                            if (tfCopy != null)
                                                                                            {
                                                                                                childObject.SetParent(tfCopy, false);
                                                                                                ApplyChangesToPrefabAsset(copy);
                                                                                            }

                                                                                            onCreationFinished?.Invoke(tfCopy);
                                                                                        }));
                                                    },

                                                    createItemContent: (int index, object item) =>
                                                    {
                                                        var objectContainer = CreateGameObjectFoldout(((Transform)item), schematicWindow, editorWindow, includeSelf);

                                                        var toggle = objectContainer.Q<Toggle>();
                                                        //var toggleParent = toggle.parent;

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

                                                        componentRow.Add(CreateFoldoutContent(((Transform)item), schematicWindow));

                                                        componentRow.Children().ElementAt(0).style.width = Length.Percent(100);

                                                        foreach(var child in toggle.Children().ToList())
                                                        {
                                                            newToggle.Add(child);
                                                        }

                                                        headerContainer.Add(newToggle);
                                                        headerContainer.Add(componentRow);

                                                        toggle.Add(headerContainer);

                                                        //toggleParent.Insert(0, headerContainer);

                                                        return objectContainer;
                                                    },

                                                    getItemName: (object item) =>
                                                    {
                                                        return ((Transform)item) == null ? "" : ((Transform)item).name;
                                                    },

                                                    itemLabelClicked: (MouseDownEvent evt, object item, Action onItemEditted) =>
                                                    {
                                                        if (evt.clickCount == 2 && evt.button == 0)
                                                        {
                                                            var identifierEditor = new InlineIdentifierEditor(popupRect: childListContainer.Q<Toggle>().WorldBoundToScreen(),
                                                                                                                identifierType: ListIdentifierType.Name,
                                                                                                                getOriginalValue: () => { return ((Transform)item).name; },
                                                                                                                onFinishedEditting: (bool change, object newValue) =>
                                                                                                                {
                                                                                                                    var childObject = ((Transform)item);

                                                                                                                    var tfPath = _prefab.transform.GetRelativePath(childObject);
                                                                                                                    var copy = PrefabUtility.LoadPrefabContents(_assetPath);

                                                                                                                    var tfCopy = copy.transform.Find(tfPath);
                                                                                                                    if (tfCopy != null)
                                                                                                                    {
                                                                                                                        tfCopy.name = newValue.ToString();
                                                                                                                        ApplyChangesToPrefabAsset(copy);
                                                                                                                    }

                                                                                                                    onItemEditted?.Invoke();
                                                                                                                });
                                                        }
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
                var type = attachment.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                var typeName = type != null ? ObjectNames.NicifyVariableName(type.Name)
                             : attachment.Metadata != null ? $"{attachment.Metadata.name} (Missing Script)" : "(Missing Behavior)";
                var icon = type != null ? GetBehaviorIcon(type) : null;

                // Tab ids must be unique.
                while (TabExists(tabView, typeName))
                    typeName += " ";

                var tab = tabView.AddTab(typeName, icon);
                tab.UseFactory(() => CreateTabContent(attachment, type, tf, schematicWindow));

                // Right-click the tab to remove the Behavior from this GameObject.
                var behaviorType = type;
                var behaviorAttachment = attachment;
                tab.Button.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction("Remove Behavior", _ => RemoveBehavior(behaviorType, behaviorAttachment, tf));
                }));

                compCount++;
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
                var newProperties = ScriptableObject.CreateInstance(propertiesType);
                newProperties.name = "Properties";
                AssetDatabase.AddObjectToAsset(newProperties, _assetPath);
                AssetDatabase.SaveAssets();

                attachment.SetOverride(variableName, newProperties, propertiesType);

                var copy = PrefabUtility.LoadPrefabContents(_assetPath);
                var tfPath = _prefab.transform.GetRelativePath(tf);
                var tfCopy = FindInCopy(copy, tfPath);
                var attachmentCopy = tfCopy != null ? copy.GetComponent<SchematicObject>()?.FindAttachment(type, tfCopy.gameObject) : null;
                if (attachmentCopy != null)
                {
                    attachmentCopy.SetOverride(variableName, newProperties, propertiesType);
                    ApplyChangesToPrefabAsset(copy);
                }
                else
                    PrefabUtility.UnloadPrefabContents(copy);
            });
        }));

        var propertiesObject = GetProperties(attachment, propertiesVariable);

        if (propertiesObject == null && propertiesType != null && !propertiesType.IsAbstract)
        {
            // Stored with the prefab's other Schematic assets, one per Behavior (and GameObject) that uses it.
            var tfPath = _prefab.transform.GetRelativePath(tf);
            string scope = type.Name + (string.IsNullOrEmpty(tfPath) ? "" : "_" + tfPath.Replace('/', '_'));
            propertiesObject = SchematicAssetManager.Create(_prefab, scope, variableName, variableName, propertiesType);

            var copy = PrefabUtility.LoadPrefabContents(_assetPath);

            var tfCopy = FindInCopy(copy, tfPath);
            var attachmentCopy = tfCopy != null ? copy.GetComponent<SchematicObject>()?.FindAttachment(type, tfCopy.gameObject) : null;
            if (attachmentCopy != null && propertiesObject != null)
            {
                attachmentCopy.SetOverride(variableName, propertiesObject, propertiesType);
                ApplyChangesToPrefabAsset(copy);
            }
            else
                PrefabUtility.UnloadPrefabContents(copy);
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
    /// Removes a Behavior from a GameObject: from the root Schematic Object's list for it, in the prefab asset.
    /// </summary>
    private void RemoveBehavior(Type type, BehaviorAttachment attachment, Transform tf)
    {
        var tfPath = _prefab.transform.GetRelativePath(tf);
        var copy = PrefabUtility.LoadPrefabContents(_assetPath);
        var tfCopy = FindInCopy(copy, tfPath);
        var rootCopy = copy.GetComponent<SchematicObject>();
        if (tfCopy != null && rootCopy != null && rootCopy.Behaviors.TryGetValue(tfCopy.gameObject, out var list) && list != null)
        {
            if (type != null)
                list.Items.RemoveAll(a => a?.Metadata != null && a.Metadata.BehaviorType.Type == type);
            else
            {
                // A Behavior whose script is missing: the attachment with the same Metadata (or none).
                var metadata = attachment?.Metadata;
                list.Items.RemoveAll(a => a == null || a.Metadata == metadata);
            }

            if (list.Items.Count == 0)
                rootCopy.Behaviors.Remove(tfCopy.gameObject);
            ApplyChangesToPrefabAsset(copy);
        }
        else
            PrefabUtility.UnloadPrefabContents(copy);
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
                    AddBehavior(_prefab.transform.GetRelativePath(tf), type);
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

        var copy = PrefabUtility.LoadPrefabContents(_assetPath);
        var tfCopy = FindInCopy(copy, tfPath);

        if (tfCopy != null)
        {
            var rootCopy = copy.GetComponent<SchematicObject>();
            if (rootCopy == null)
            {
                rootCopy = copy.AddComponent<SchematicObject>();
                rootCopy.AssignObjectID(SchematicObjectIDs.For(_assetPath));
            }

            rootCopy.AttachBehavior(type, tfCopy.gameObject);
            ApplyChangesToPrefabAsset(copy);
        }
        else
            PrefabUtility.UnloadPrefabContents(copy);
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
        var root = _prefab != null ? _prefab.GetComponent<SchematicObject>() : null;
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

    /// <summary>
    /// Applies changes to the prefab asset and triggers rebuild if needed.
    /// </summary>
    private async void ApplyChangesToPrefabAsset(GameObject copy)
    {
        PrefabUtility.SaveAsPrefabAsset(copy, _assetPath);
        PrefabUtility.UnloadPrefabContents(copy);

        AssetDatabase.Refresh();

        await EditorUtilities.NextEditorFrame();

        PrefabRefreshUtility.ReimportAndResetPrefab(_prefab);
        RebuildHierarchy(_prefab.transform);
    }

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
