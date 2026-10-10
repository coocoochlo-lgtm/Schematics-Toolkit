using BlueGraph;
using BlueGraph.Editor;
using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A custom Unity editor window for editing flow graphs and schematics.
/// Provides a visual interface for creating and managing schematic graphs with drag-and-drop functionality,
/// event management, and real-time validation of flow connections.
/// </summary>
public class SchematicGraphEditorWindow : GraphEditorWindow
{
    internal string GlobalPath
    {
        get
        {
            var assetPath = AssetDatabase.GetAssetPath(SchematicEditorData.Instance);
            var path = Path.GetDirectoryName(assetPath) + "\\Global Events\\";
            return path;
        }
    }

    #region Editing Objects
    /// <summary>
    /// The schematic scope that contains the graph being edited.
    /// This is the main container for the schematic data.
    /// </summary>
    /*[HideInInspector]
    public SchematicScope SchematicScope;*/
    /// <summary>
    /// Gets the schematic graph from the current schematic scope.
    /// Returns null if no schematic scope is assigned.
    /// </summary>
    /*public SchematicGraph SchematicGraph => SchematicScope == null ? null : SchematicScope.Graph;*/
    public SchematicGraph SchematicGraph;
    /// <summary>
    /// Gets the prefab associated with the current schematic graph.
    /// This prefab represents the GameObject hierarchy that the schematic operates on.
    /// </summary>
    public GameObject Prefab => SchematicGraph.Prefab;
    
    #endregion

    #region Object Properties
    private string _prefabPath;
    /// <summary>
    /// Unique identifier for the current schematic, used for persistent UI state.
    /// </summary>
    internal string SchematicGUID;
    #endregion

    #region UI Elements
    /// <summary>
    /// Main UI container that provides the sidebar and content layout structure.
    /// </summary>
    private SidebarAndContent _sidebarAndContent;
    private TabView _sidebarTabs;
    #endregion

    #region UI Properties
    /// <summary>
    /// A Lookup Table for Types
    /// </summary>
    internal static readonly IReadOnlyDictionary<Type, Color> ColorLookupValueType = new Dictionary<Type, Color>
    {
    { typeof(bool),       new Color(0.9f, 0.3f, 0.3f) }, // red (conditions)
    { typeof(int),        new Color(0.2f, 0.6f, 1f)   }, // blue (whole numbers)
    { typeof(float),      new Color(0.3f, 0.8f, 0.3f) }, // green (continuous values)
    { typeof(string),     new Color(0.8f, 0.5f, 0.8f) }, // purple (text)

    // Unity common types
    { typeof(Vector2),    new Color(1f,   0.7f, 0.2f) }, // orange (2D vectors)
    { typeof(Vector3),    new Color(1f,   0.5f, 0.2f) }, // deeper orange (3D vectors)
    { typeof(Vector4),    new Color(1f,   0.4f, 0.2f) }, // similar but distinct
    { typeof(Quaternion), new Color(0.7f, 0.4f, 1f)   }, // violet (rotations)
    { typeof(RaycastHit), new Color(0.3f, 0.9f, 0.9f) }, // teal (physics result)

    // Unity objects
    { typeof(GameObject), new Color(1f,   1f,  0.4f)  }, // yellow (scene objects)
    { typeof(Transform),  new Color(0.9f, 0.9f, 0.3f) }, // yellow-green
    { typeof(Material),   new Color(0.9f, 0.6f, 0.2f) }, // bronze-like
    { typeof(Texture),    new Color(0.6f, 0.4f, 0.9f) }, // purple-blue

    // Misc
    { typeof(Color),      Color.white                 }, // white (represents itself)
    { typeof(object),     Color.gray                  }  // fallback / unknown
    };

    internal static readonly IReadOnlyDictionary<string, Color> ColorLookupFunctional = new Dictionary<string, Color>()
    {
    { "Remove",             new Color(1, 0, 0, 0.1f)                        },
    { "RemoveHighlight",    Color.darkRed                                   },
    { "Create",             Color.cyan * new Color(0.8f, 0.8f, 0.8f, 0.1f)  },
    { "CreateHighlight",    Color.cyan * new Color(0.8f, 0.8f, 0.8f, 0.25f) },
    };

    internal static readonly IReadOnlyDictionary<int, Dictionary<bool, Color>> ColorLookupHierarchichal = new Dictionary<int, Dictionary<bool, Color>>()
    {
        { // 1st Layer
            0, 
            new() 
            {
                { // %2 == 0
                    false,
                    Color.gray3 * new Color(0.65f,0.65f,0.65f,1f)
                },
                { // %2 == 1
                    true,
                    Color.gray3 * new Color(0.7f,0.7f,0.7f,1f)
                }
            } 
        },

        { // 2nd Layer
            1,
            new()
            {
                { // %2 == 0
                    false,
                    Color.gray3 * new Color(0.9f,0.9f,0.9f,1f)
                },
                { // %2 == 1
                    true,
                    Color.gray3 * new Color(0.85f,0.85f,0.85f,1f)
                }
            }
        }
    };
    #endregion


    #region Caches
    /// <summary>
    /// Static collection of all editor windows that need to be saved.
    /// </summary>
    private static List<SchematicGraphEditorWindow> _windowsToSave;
    public static List<SchematicGraphEditorWindow> WindowsToSave => _windowsToSave ??= new();

    /// <summary>The Prefab tab's dock, for revealing rows from the graph.</summary>
    private PrefabHierarchy _dock;
    private SchematicMinimap _minimap;
    private CanvasMotion _motion;

    // Which node FocusNodes showed last for each dock item, so repeated clicks step through them.
    private readonly Dictionary<string, int> _focusIndex = new();

    internal static ProcessProfiler Profiler = new ProcessProfiler();

    private static List<(string name, ScriptableObject data, Type type)> _gs;
    private static List<(string name, ScriptableObject data, Type type)> _globals
    {
        get
        {
            if (_gs != null)
                return _gs;

            var results = new List<(string name, ScriptableObject data, Type type)>();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                Type[] types;
                try
                {
                    types = assemblies[a].GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }

                if (types == null)
                    continue;

                for (int t = 0; t < types.Length; t++)
                {
                    var type = types[t];
                    if (type == null)
                        continue;

                    // Attribute check
                    if (type.GetCustomAttribute<SchematicGlobalObjectAttribute>() == null)
                        continue;

                    // Static Instance property lookup
                    var prop = type.GetProperty(
                        "Instance",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                    if (prop == null)
                        continue;

                    var value = prop.GetValue(null);
                    if (value is ScriptableObject so)
                    {
                        var attr = type.GetCustomAttribute<SchematicGlobalObjectAttribute>();
                        var displayName = attr.Name;

                        results.Add((displayName, so, type));
                    }
                }
            }


            _gs = results.ToList();
            return _gs;
        }
    }

    private static VisualElement _dg;
    private static VisualElement _cachedGlobalDraw
    {
        get
        {
            if (_dg != null) return _dg;

            VisualElement globalContainer = new();

            foreach (var instance in _globals)
            {
                var objFoldout = new Foldout()
                {
                    text = instance.name
                };

                var path = AssetDatabase.GetAssetPath(SchematicEditorData.Instance) + "/Global Events/" + instance.name + "/";
                (bool draw, VisualElement dock) = IODockRegistry.RenderObjectDock(instance.type, _currentWindow, instance.data, null, path);

                if (draw)
                {
                    objFoldout.Add(dock);
                    globalContainer.Add(objFoldout);
                }
            }

            _dg = globalContainer;

            return _dg;
        }
    }

    private static SchematicGraphEditorWindow _currentWindow;

    #endregion

    private bool _updatedPortParams = false;

    /// <summary>
    /// Loads and initializes a graph in the editor window.
    /// Sets up the UI, event management, caches, and callbacks required for graph editing.
    /// </summary>
    /// <param name="graph">The graph to load and display in the editor</param>
    public override void Load(Graph graph)
    {
        _currentWindow = this;

        const string loadProcess = "Load Graph";
        Profiler.StartTracking(loadProcess);

        if (Prefab != null)
            SchematicGraph.PrefabGUID = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(Prefab));
        else
        {
            var loadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SchematicGraph.PrefabGUID));
            if (loadedPrefab != null)
            {
                SchematicGraph.Prefab = loadedPrefab;
            }
        }

        _sidebarAndContent = new SidebarAndContent("schematic-snc");

        _sidebarAndContent.styleSheets.Add(Resources.Load<StyleSheet>("IODock"));

        _sidebarAndContent.SidebarContainer.parent.parent.AddToClassList("iodock");

        _sidebarTabs = new("scope");

        _sidebarTabs.AddTab("Global");
        _sidebarTabs.AddTab("Prefab");
        _sidebarTabs.AddTab("Graph");

        _sidebarAndContent.SidebarContainer.Add(_sidebarTabs);

        base.Load(graph);

        _sidebarAndContent.contentContainer.Add(Canvas);

        var canvasOverlay = new VisualElement()
        {
            style =
            {
                position = Position.Absolute,
                left = 0,
                right = 0,
                top = 0,
                bottom = 0
            }
        };
        canvasOverlay.AddToClassList("canvas-overlay");

        canvasOverlay.pickingMode = PickingMode.Ignore;

        Canvas.Add(canvasOverlay);

        WindowsToSave.Add(this);
        /*_processed.Clear();*/

        if (SchematicGraph != null)
        {
            var path = AssetDatabase.GetAssetPath(SchematicGraph);
            SchematicGUID = AssetDatabase.AssetPathToGUID(path);
            RedrawIODock();
        }
        else
            Debug.LogError("The SchematicScope for this Schematic Prefab does not exist.", this);

        // Graph Canvas
        Canvas.StretchToParentSize();
        Canvas.style.flexGrow = 1;
        Canvas.style.position = Position.Relative;

        rootVisualElement.Add(_sidebarAndContent);

        // Callbacks
        Canvas.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        Canvas.RegisterCallback<DragPerformEvent>(OnDragPerform);

        if (_prefabPath == null)
            _prefabPath = AssetDatabase.GetAssetPath(Prefab);

        Application.focusChanged += Reload;
        EditorApplication.projectChanged += Canvas.Reload;
        AssemblyReloadEvents.afterAssemblyReload += Canvas.Reload;

        var alreadyUpdated = _updatedPortParams;
        _updatedPortParams = true;

        SyncNodePorts();

        // Clicking a Signal or Variable node reveals it in the IO Dock.
        Canvas.RegisterCallback<MouseUpEvent>(OnCanvasMouseUp, TrickleDown.TrickleDown);

        _motion = new CanvasMotion(Canvas);
        _minimap = new SchematicMinimap(Canvas, _motion);
        Canvas.Add(_minimap);

        if (!alreadyUpdated) Reload();

        Profiler.StopTracking(loadProcess);

        if (Prefab != null && Prefab.TryGetComponent(out SchematicObject schematicObject))
            schematicObject.SchematicGraph = SchematicGraph;
    }


    private void OnDisable()
    {
        if(Canvas != null)
        {
            Application.focusChanged -= Reload;
            EditorApplication.projectChanged -= Canvas.Reload;
            AssemblyReloadEvents.afterAssemblyReload -= Canvas.Reload;
        }
    }

    private void Reload(bool val = false)
    {
        Canvas.Reload();
    }


    /// <summary>
    /// Redraws the Input/Output Event dock in the sidebar.
    /// Refreshes the prefab hierarchy display, component toggles, and schematic foldouts.
    /// This method is called whenever the UI needs to be updated to reflect changes in the graph or prefab.
    /// </summary>
    internal void RedrawIODock(bool refreshGlobals = false)
    {
        var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(_prefabPath);
        if (prefabAsset != null)
        {
            SchematicGraph.Prefab = prefabAsset;
        }

        SchematicGraph.Prefab = Prefab;

        if (refreshGlobals) _dg = null;

        RedrawGlobalIO();
        RedrawPrefabIO();
        RedrawGraphIO();
    }

    private void RedrawGlobalIO()
    {
        _sidebarTabs["Global"].Clear();
        _sidebarTabs["Global"].Set(_cachedGlobalDraw);
    }

    private void RedrawPrefabIO()
    {
        _sidebarTabs["Prefab"].Clear();
        _dock = null;
        if (SchematicGraph is SingletonGraph singleton)
        {
            _sidebarTabs["Prefab"].Set(new IMGUIContainer(() => DrawSingletonPanel(singleton)));
            return;
        }
        if (Prefab != null)
        {
            _dock = new PrefabHierarchy(Prefab, this);
            _sidebarTabs["Prefab"].Set(_dock);
        }
    }

    /// <summary>
    /// A Singleton Graph has no prefab: its object is created when the game starts. This panel shows how, and its State Machine.
    /// </summary>
    private void DrawSingletonPanel(SingletonGraph singleton)
    {
        EditorGUILayout.HelpBox("This Singleton gets its own object when the game starts (kept across scenes), registered automatically: no Resources folder needed.", MessageType.None);

        EditorGUI.BeginChangeCheck();
        bool spawn = EditorGUILayout.Toggle(new GUIContent("Spawn On Start", "Create this Singleton's object when the game starts."), singleton.SpawnOnStart);
        int order = EditorGUILayout.IntField(new GUIContent("Order", "Singletons are created in ascending order."), singleton.Order);
        var stateMachine = (StateMachineDefinition)EditorGUILayout.ObjectField("State Machine", singleton.StateMachine, typeof(StateMachineDefinition), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(singleton, "Edit Singleton");
            singleton.SpawnOnStart = spawn;
            singleton.Order = order;
            singleton.StateMachine = stateMachine;
            if (stateMachine != null && stateMachine.Prefab == null && stateMachine.Singleton == null)
            {
                Undo.RecordObject(stateMachine, "Edit Singleton");
                stateMachine.Singleton = singleton;
                EditorUtility.SetDirty(stateMachine);
            }
            EditorUtility.SetDirty(singleton);
            SingletonGraphRegistry.Sync();
        }

        if (GUILayout.Button(singleton.StateMachine != null ? "Open State Machine" : "Create State Machine"))
            StateMachineWindow.Open(StateMachineEditorUtility.DefinitionFor(singleton, create: true));

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            var host = singleton.Host;
            using (new EditorGUI.DisabledScope(host == null))
                if (GUILayout.Button(host != null ? $"Select {host.name}" : "Not running"))
                    Selection.activeGameObject = host.gameObject;
            var machine = host != null ? host.gameObject.GetSchematicBehavior<StateMachine>() : null;
            if (machine != null)
                EditorGUILayout.LabelField("Current State", machine.CurrentState.Value);
        }
    }

    /// <summary>
    /// The Graph tab: every node in this graph that sends, hears, reads or writes something, grouped by what it uses.
    /// Click one to jump to it.
    /// </summary>
    private void RedrawGraphIO()
    {
        _sidebarTabs["Graph"].Clear();
        if (SchematicGraph == null)
            return;

        var content = new VisualElement();
        var groups = new SortedDictionary<string, List<Node>>();
        foreach (var node in SchematicGraph.Nodes)
        {
            string label = DescribeUse(node);
            if (label == null) continue;
            if (!groups.TryGetValue(label, out var list))
                groups[label] = list = new List<Node>();
            list.Add(node);
        }

        if (groups.Count == 0)
            content.Add(new Label("No Signal, Variable or Behavior nodes yet. Drag them in from the Prefab tab."));

        foreach (var group in groups)
        {
            var foldout = new Foldout { text = $"{group.Key}  ({group.Value.Count})", value = true };
            foreach (var node in group.Value)
            {
                var target = node;
                var button = new Button(() => FocusNode(target)) { text = node.Name };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                foldout.Add(button);
            }
            content.Add(foldout);
        }

        _sidebarTabs["Graph"].Set(content);
    }

    private static string DescribeUse(Node node) => node switch
    {
        SendSignalNode send when send.Signal != null => "Signal: " + send.Signal.name,
        OnSignalReceivedNode receive when receive.Signal != null => "Signal: " + receive.Signal.name,
        GetBehaviorVariableNode get => "Variable: " + get.Variable + " (" + get.Target.Describe() + ")",
        SetBehaviorVariableNode set => "Variable: " + set.Variable + " (" + set.Target.Describe() + ")",
        BehaviorReferenceNode behavior => "Behavior: " + behavior.Target.Describe(),
        PrefabObjectNode obj => "Object: " + (string.IsNullOrEmpty(obj.Path) ? "(root)" : obj.Path),
        _ => null,
    };

    #region Navigation

    /// <summary>The dock item a node uses, or null.</summary>
    internal static string KeyOf(Node node) => node switch
    {
        SendSignalNode send => send.Signal != null ? DockItem.SignalKey(send.Signal) : null,
        OnSignalReceivedNode receive => receive.Signal != null ? DockItem.SignalKey(receive.Signal) : null,
        GetBehaviorVariableNode get => DockItem.VariableKey(get.Target.Type, get.Target.Path, get.Variable),
        SetBehaviorVariableNode set => DockItem.VariableKey(set.Target.Type, set.Target.Path, set.Variable),
        BehaviorReferenceNode behavior => DockItem.BehaviorKey(behavior.Target.Type, behavior.Target.Path),
        PrefabObjectNode obj => DockItem.ObjectKey(obj.Path),
        _ => null,
    };

    /// <summary>
    /// Selects and frames the next node in the graph that uses the item. Clicking the same item again steps to the next one.
    /// </summary>
    internal void FocusNodes(DockItem item)
    {
        string key = item.Key;
        var matches = new List<NodeView>();
        foreach (var view in Canvas.Query<NodeView>().ToList())
            if (view.Target != null && KeyOf(view.Target) == key)
                matches.Add(view);

        if (matches.Count == 0)
        {
            ShowNotification(new GUIContent($"No nodes use {item.DisplayName} yet. Drag it into the graph."), 1.5);
            return;
        }

        matches.Sort((a, b) => a.GetPosition().position.y != b.GetPosition().position.y
            ? a.GetPosition().position.y.CompareTo(b.GetPosition().position.y)
            : a.GetPosition().position.x.CompareTo(b.GetPosition().position.x));

        _focusIndex.TryGetValue(key, out int index);
        index %= matches.Count;
        _focusIndex[key] = index + 1;

        FocusView(matches[index]);
        if (matches.Count > 1)
            ShowNotification(new GUIContent($"{item.DisplayName}: {index + 1} of {matches.Count}"), 1.0);
    }

    internal void FocusNode(Node node)
    {
        foreach (var view in Canvas.Query<NodeView>().ToList())
            if (view.Target == node)
            {
                FocusView(view);
                return;
            }
    }

    private void FocusView(NodeView view)
    {
        Canvas.ClearSelection();
        Canvas.AddToSelection(view);
        if (_motion != null) _motion.Frame(view.GetPosition());
        else Canvas.FrameSelection();
        PrefabHierarchy.Flash(view);
    }

    private void OnCanvasMouseUp(MouseUpEvent evt)
    {
        if (evt.button != 0 || _dock == null)
            return;

        // After the canvas has updated its selection.
        Canvas.schedule.Execute(() =>
        {
            if (Canvas.selection.Count != 1 || !(Canvas.selection[0] is NodeView view) || view.Target == null)
                return;
            string key = KeyOf(view.Target);
            if (key == null)
                return;
            _sidebarTabs.SelectTab("Prefab");
            _dock.Reveal(key);
        });
    }

    #endregion

    /// <summary>
    /// Handles drag-and-drop update events over the canvas.
    /// </summary>
    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        if (DragAndDrop.GetGenericData(DockItem.DragKey) is DockItem || DragAndDrop.objectReferences.Any(o => o is SignalData))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.StopPropagation();
        }
    }

    /// <summary>
    /// Handles items dropped onto the canvas: IO Dock rows (Behaviors, Variables, Signals, GameObjects) and Signal assets.
    /// </summary>
    private void OnDragPerform(DragPerformEvent evt)
    {
        Vector2 dropPosition = evt.mousePosition;

        if (DragAndDrop.GetGenericData(DockItem.DragKey) is DockItem item)
        {
            DragAndDrop.AcceptDrag();
            DragAndDrop.SetGenericData(DockItem.DragKey, null);
            evt.StopPropagation();

            switch (item.Kind)
            {
                case DockItem.ItemKind.Signal: DropSignal(item.Signal, item.Direction, dropPosition); break;
                case DockItem.ItemKind.Variable: ShowVariableDropMenu(item, dropPosition); break;
                case DockItem.ItemKind.Behavior: CreateBehaviorNode(item, dropPosition); break;
                case DockItem.ItemKind.GameObject: CreatePrefabObjectNode(item, dropPosition); break;
            }
            return;
        }

        bool accepted = false;
        foreach (var obj in DragAndDrop.objectReferences)
        {
            if (obj is SignalData signal)
            {
                DropSignal(signal, DirectionOf(signal), dropPosition);
                accepted = true;
            }
        }

        if (accepted)
        {
            DragAndDrop.AcceptDrag();
            evt.StopPropagation();
        }
    }

    /// <summary>
    /// A Signal's direction says what the graph can do with it: an Input Signal is one the Behavior listens to, so the
    /// graph sends it; an Output Signal is one the Behavior sends, so the graph reacts to it. Only Signals without a
    /// direction (e.g. global ones) ask.
    /// </summary>
    private void DropSignal(SignalData signal, SignalDirection direction, Vector2 position)
    {
        if (signal == null)
            return;
        switch (direction)
        {
            case SignalDirection.Input: CreateInvokeNode(signal, position); break;
            case SignalDirection.Output: CreateOnInvokeNode(signal, position); break;
            default: ShowEventDropMenu(signal, position); break;
        }
    }

    /// <summary>The direction a Behavior declared for this Signal asset; Unspecified for Signals that aren't a Behavior's.</summary>
    internal static SignalDirection DirectionOf(SignalData signal)
    {
        if (signal != null && AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(signal)) is BehaviorMetadata metadata)
            foreach (var definition in metadata.Signals)
                if (definition.Asset == signal)
                    return definition.Direction;
        return SignalDirection.Unspecified;
    }

    private void ShowEventDropMenu(SignalData evtAsset, Vector2 position)
    {
        if (evtAsset == null)
            return;

        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent($"Action ► Send {evtAsset.name}"), false, () => CreateInvokeNode(evtAsset, position));
        menu.AddItem(new GUIContent($"Event ► On {evtAsset.name} Received"), false, () => CreateOnInvokeNode(evtAsset, position));
        menu.ShowAsContext();
    }

    private void ShowVariableDropMenu(DockItem item, Vector2 position)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent($"Query ► Get {item.Name}"), false, () => CreateVariableNode(item, position, set: false));
        menu.AddItem(new GUIContent($"Action ► Set {item.Name}"), false, () => CreateVariableNode(item, position, set: true));
        menu.ShowAsContext();
    }

    private T NewNode<T>() where T : Node => (T)NodeReflection.GetNodeTypes()[typeof(T).Name].CreateInstance();

    private void CreateInvokeNode(SignalData signalAsset, Vector2 position)
    {
        var node = NewNode<SendSignalNode>();
        node.Signal = signalAsset;
        node.Name = "Send " + signalAsset.name;
        SyncSignalPorts(node, signalAsset, PortDirection.Input);
        Canvas.AddNodeFromSearch(node, this.position.position + position, null);
        RedrawGraphIO();
    }

    private void CreateOnInvokeNode(SignalData signalAsset, Vector2 position)
    {
        var node = NewNode<OnSignalReceivedNode>();
        node.Signal = signalAsset;
        node.Name = "On " + signalAsset.name;
        SyncSignalPorts(node, signalAsset, PortDirection.Output);
        Canvas.AddNodeFromSearch(node, this.position.position + position, null);
        RedrawGraphIO();
    }

    private void CreateVariableNode(DockItem item, Vector2 position, bool set)
    {
        BehaviorNodeBase node;
        if (set)
        {
            var setter = NewNode<SetBehaviorVariableNode>();
            setter.Variable = item.Name;
            setter.Name = "Set " + ObjectNames.NicifyVariableName(item.Name);
            node = setter;
        }
        else
        {
            var getter = NewNode<GetBehaviorVariableNode>();
            getter.Variable = item.Name;
            getter.Name = "Get " + ObjectNames.NicifyVariableName(item.Name);
            node = getter;
        }
        node.Target = new BehaviorTarget(item.Behavior, item.Path);
        SyncBehaviorNodePorts(node);
        Canvas.AddNodeFromSearch(node, this.position.position + position, null);
        RedrawGraphIO();
    }

    private void CreateBehaviorNode(DockItem item, Vector2 position)
    {
        var node = NewNode<BehaviorReferenceNode>();
        node.Target = new BehaviorTarget(item.Behavior, item.Path);
        node.Name = ObjectNames.NicifyVariableName(item.Behavior.Name);
        node.RebuildPorts();
        Canvas.AddNodeFromSearch(node, this.position.position + position, null);
        RedrawGraphIO();
    }

    private void CreatePrefabObjectNode(DockItem item, Vector2 position)
    {
        var node = NewNode<PrefabObjectNode>();
        node.Path = item.Path;
        node.Name = item.GameObject != null ? item.GameObject.name : "Prefab Object";
        Canvas.AddNodeFromSearch(node, this.position.position + position, null);
        RedrawGraphIO();
    }

    #region Port syncing

    /// <summary>
    /// Brings dynamic pins up to date with the code (Signal parameters, Variable types, Behavior members), so renaming
    /// or retyping something in a Behavior doesn't leave stale pins behind.
    /// </summary>
    private void SyncNodePorts()
    {
        if (SchematicGraph == null)
            return;

        bool changed = false;
        foreach (var node in SchematicGraph.Nodes)
        {
            switch (node)
            {
                case SendSignalNode send when send.Signal != null:
                    changed |= SyncSignalPorts(send, send.Signal, PortDirection.Input);
                    break;
                case OnSignalReceivedNode receive when receive.Signal != null:
                    changed |= SyncSignalPorts(receive, receive.Signal, PortDirection.Output);
                    break;
                case BehaviorReferenceNode behavior:
                    behavior.RebuildPorts();
                    changed = true;
                    break;
                case BehaviorNodeBase variableNode:
                    changed |= SyncBehaviorNodePorts(variableNode);
                    break;
            }
        }

        if (changed)
        {
            EditorUtility.SetDirty(SchematicGraph);
            Canvas.Reload();
        }
    }

    private static bool SyncSignalPorts(Node node, SignalData signal, PortDirection direction)
    {
        var parameters = SignalParameters(signal);
        var wanted = new HashSet<string>();
        bool changed = false;

        foreach (var (name, type) in parameters)
        {
            wanted.Add(name);
            var existing = node.GetPort(name);
            if (existing != null && existing.Type == type && existing.Direction == direction)
                continue;
            if (existing != null)
                node.RemovePort(existing);
            node.AddPort(new Port
            {
                Type = type,
                Name = name,
                Capacity = direction == PortDirection.Input ? PortCapacity.Single : PortCapacity.Multiple,
                Direction = direction
            });
            changed = true;
        }

        foreach (var port in node.Ports.Values.Where(p => p.Type != typeof(ActionPort) && !wanted.Contains(p.Name)).ToList())
        {
            node.RemovePort(port);
            changed = true;
        }
        return changed;
    }

    private static bool SyncBehaviorNodePorts(BehaviorNodeBase node)
    {
        string variable = node is GetBehaviorVariableNode get ? get.Variable : node is SetBehaviorVariableNode set ? set.Variable : null;
        if (variable == null)
            return false;

        var type = BehaviorReflection.Get(node.Target.Type)?.FindVariableType(variable) ?? typeof(object);
        var direction = node is SetBehaviorVariableNode ? PortDirection.Input : PortDirection.Output;
        string portName = node is SetBehaviorVariableNode ? SetBehaviorVariableNode.ValuePort : GetBehaviorVariableNode.ValuePort;

        var existing = node.GetPort(portName);
        if (existing != null && existing.Type == type && existing.Direction == direction)
            return false;
        node.EnsurePort(portName, type, direction);
        node.RemovePortsExcept(new[] { portName });
        return true;
    }

    /// <summary>
    /// A Signal's parameters with their real types: from the Behavior Metadata that owns the Signal asset when there
    /// is one, otherwise from the asset's own parameter list.
    /// </summary>
    internal static List<(string name, Type type)> SignalParameters(SignalData signal)
    {
        var result = new List<(string, Type)>();
        if (signal == null)
            return result;

        if (AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(signal)) is BehaviorMetadata metadata)
            foreach (var definition in metadata.Signals)
                if (definition.Asset == signal)
                {
                    foreach (var parameter in definition.Parameters)
                        result.Add((parameter.Name, parameter.Type.Type ?? typeof(object)));
                    return result;
                }

        foreach (var parameter in signal.Parameters.Contents)
        {
            Union.TypeLookup.TryGetValue(parameter.Value.Type, out var type);
            result.Add((parameter.Name, type ?? typeof(object)));
        }
        return result;
    }

    #endregion
}
