using System;
using System.Linq;
using Remedy.Schematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// Graph windows, one per graph type: opening a graph shows it in its type's window (an object's graph in the Graph tab,
/// a Sound Cue in the Sound Cue window...), which moves to it, rather than opening a window per graph. Open in New Graph
/// Window (the Project window's and the Project Manager's menus) opens one that stays on its graph.
///
/// The Graph tab: a graph window that follows the selection. It shows the Schematic Graph of whatever Schematic Object is
/// selected (an instance in the scene, the prefab open in Prefab Mode, a prefab or graph in the Project window) and keeps
/// showing the last one while something else is selected; its lock keeps it on one. Its dock (Global, Prefab and Graph
/// tabs) is shown by the IO Dock window when that's open, so the canvas has the whole tab; with no IO Dock open it's in
/// the sidebar as usual. Tools > Remedy > Schematics > Graph.
/// </summary>
public partial class SchematicGraphEditorWindow
{
    /// <summary>This is the Graph tab (it follows the selection).</summary>
    [SerializeField] internal bool FollowsSelection;
    /// <summary>Locked: it stays on its object whatever is selected.</summary>
    [SerializeField] internal bool FollowLocked;
    /// <summary>The instance it follows (whose flow it shows in Play Mode), or null for a prefab or graph asset.</summary>
    [SerializeField] internal GameObject FollowedInstance;
    /// <summary>The prefab it follows (the graph's own when it came from an instance).</summary>
    [SerializeField] internal GameObject FollowedPrefab;
    /// <summary>Opened with Open in New Graph Window: it stays on its graph (never reused for another).</summary>
    [SerializeField] internal bool SeparateWindow;

    /// <summary>The dock's tabs (Global, Prefab, Graph): lent to the IO Dock while it's open.</summary>
    internal TabView DockTabs => _sidebarTabs;

    /// <summary>The Graph tab loaded a graph, or moved to another object of the same graph.</summary>
    internal static event Action<SchematicGraphEditorWindow> FollowerChanged;

    private const string EmptyName = "schematic-follow-empty";

    /// <summary>The Graph tab, if it's open.</summary>
    internal static SchematicGraphEditorWindow Follower
        => Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>().FirstOrDefault(w => w != null && w.FollowsSelection);

    [MenuItem("Tools/Remedy/Schematics/Graph", false, 20)]
    private static void OpenFollower() => EnsureFollower(focus: true);

    /// <summary>
    /// Shows a graph in its type's window: an object's graph in the Graph tab, any other kind in the one window for that
    /// kind (made beside the Scene view if there isn't one), which moves to it. <paramref name="newWindow"/>: a window
    /// of its own instead, which stays on it. <paramref name="instance"/>: the instance it's for (the Graph tab's header
    /// and flow).
    /// </summary>
    public static SchematicGraphEditorWindow OpenInTypeWindow(SchematicGraph graph, bool newWindow = false, bool focus = true, GameObject instance = null)
    {
        if (graph == null)
            return null;
        if (!newWindow)
        {
            if (IsObjectGraph(graph))
            {
                // (Asked for by name: it moves even when locked.)
                var follower = EnsureFollower(focus, applySelection: false);
                follower.Follow(graph, instance, graph.Prefab);
                return follower;
            }
            var existing = TypeWindow(graph.GetType());
            if (existing != null)
            {
                if (existing.SchematicGraph != graph || existing.Canvas == null)
                    existing.ShowGraph(graph);
                if (focus)
                    existing.Focus();
                return existing;
            }
        }
        var before = focusedWindow;
        var window = CreateWindow<SchematicGraphEditorWindow>(typeof(SceneView));
        window.SeparateWindow = newWindow;
        window.SchematicGraph = graph;
        window.Load(graph);
        if (focus)
            window.Focus();
        else if (before != null)
            before.Focus();
        return window;
    }

    /// <summary>An object's own graph (a Schematic Object's): the Graph tab's kind. Sound Cues, Timelines, Singletons... have their own windows.</summary>
    internal static bool IsObjectGraph(SchematicGraph graph) => graph != null && graph.GetType() == typeof(SchematicGraph);

    // The window for a kind of graph (not the Graph tab, nor one opened as a window of its own).
    private static SchematicGraphEditorWindow TypeWindow(Type type)
        => Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>().FirstOrDefault(w => w != null && !w.FollowsSelection && !w.SeparateWindow
            && w.SchematicGraph != null && w.SchematicGraph.GetType() == type);

    [MenuItem("Assets/Open in New Graph Window", false, 21)]
    private static void OpenSelectedInNewWindow()
    {
        var graph = GraphOfAsset(Selection.activeObject);
        if (graph != null)
            OpenInTypeWindow(graph, newWindow: true);
    }

    [MenuItem("Assets/Open in New Graph Window", true)]
    private static bool OpenSelectedInNewWindowValidate() => GraphOfAsset(Selection.activeObject) != null;

    // A graph asset, or a Schematic prefab's graph.
    private static SchematicGraph GraphOfAsset(Object asset)
        => asset is SchematicGraph graph ? graph
            : asset is GameObject prefab && prefab.TryGetComponent(out SchematicObject schematic) ? schematic.SchematicGraph : null;

    /// <summary>The Graph tab: the open one, or a new one docked beside the Scene view (in front only with <paramref name="focus"/>).</summary>
    internal static SchematicGraphEditorWindow EnsureFollower(bool focus, bool applySelection = true)
    {
        var follower = Follower;
        if (follower == null)
        {
            var before = focusedWindow;
            follower = CreateWindow<SchematicGraphEditorWindow>("Graph", typeof(SceneView));
            follower.FollowsSelection = true;
            follower.ApplyTitle();
            follower.ShowEmpty();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (applySelection)
                SchematicFollow.Apply(Selection.activeObject != null ? Selection.activeObject : stage != null ? stage.prefabContentsRoot : null, follower);
            if (!focus && before != null)
                before.Focus();
        }
        else if (focus)
            follower.Focus();
        return follower;
    }

    /// <summary>
    /// Moves the Graph tab to an object: its graph (loaded, if it's another), and the instance or prefab it's for (the
    /// header, and whose flow it shows).
    /// </summary>
    internal void Follow(SchematicGraph graph, GameObject instance, GameObject prefab)
    {
        if (graph == null)
            return;
        FollowedInstance = instance;
        FollowedPrefab = prefab;
        if (graph != SchematicGraph || Canvas == null)
            ShowGraph(graph);
        else
        {
            ApplyTitle();
            FollowerChanged?.Invoke(this);
        }
        Repaint();
    }

    /// <summary>Shows another graph in this window, everything in it made again (as a script reload does).</summary>
    internal void ShowGraph(SchematicGraph graph)
    {
        if (graph == null)
            return;
        if (Graph != null)
        {
            OnDisable(); // (what Load hooks up, taken off again)
            Graph.IsBeingEditted = false;
        }
        var assistant = Remedy.Schematics.Assistant.AssistantOverlay.Instance;
        bool assistantHere = assistant != null && assistant.parent != null && rootVisualElement.Contains(assistant);
        rootVisualElement.UnregisterCallback<KeyDownEvent>(OnDebugKey, TrickleDown.TrickleDown);
        rootVisualElement.Clear();
        _dock = null;
        _prefabPath = null;
        _updatedPortParams = false;
        _focusIndex.Clear();
        SchematicGraph = graph;
        Load(graph);
        if (assistantHere && assistant.parent == null)
            rootVisualElement.Add(assistant);
        _dockModeVersion = _modeVersion;
    }

    // Nothing to show yet: the header, and how to pick something.
    private void ShowEmpty()
    {
        if (rootVisualElement.Q(EmptyName) != null)
            return;
        rootVisualElement.Clear();
        rootVisualElement.Add(new SchematicTargetHeader());
        var hint = new Label("Select a Schematic Object (in the scene, in Prefab Mode, or a prefab or graph in the Project window) to edit its graph here.")
        {
            name = EmptyName,
        };
        hint.style.whiteSpace = WhiteSpace.Normal;
        hint.style.unityTextAlign = TextAnchor.MiddleCenter;
        hint.style.flexGrow = 1;
        hint.style.opacity = 0.6f;
        hint.style.paddingLeft = hint.style.paddingRight = 24;
        rootVisualElement.Add(hint);
    }

    // The end of Load: its tab's name and icon; for the Graph tab, the header over the canvas, and the dock lent to the
    // IO Dock if it's open.
    private void LoadedForFollow()
    {
        ApplyTitle();
        if (!FollowsSelection)
            return;
        rootVisualElement.Insert(0, new SchematicTargetHeader());
        if (SchematicIODockWindow.IsOpen)
            _sidebarAndContent?.SetSidebarShown(false);
        FollowerChanged?.Invoke(this);
    }

    /// <summary>
    /// Its tab: the icon of its kind of graph, and the name of what it's on (the Graph tab: the object; the others: the
    /// graph), with what it is in the tooltip.
    /// </summary>
    internal void ApplyTitle()
    {
        string name, tooltip;
        if (FollowsSelection)
        {
            string followed = DescribeFollowed(out string detail, out _);
            name = SchematicGraph != null ? followed : "Graph";
            tooltip = SchematicGraph != null ? $"Graph of {name} ({detail}); follows the selection" : "Graph: follows the selected Schematic Object";
        }
        else
        {
            name = SchematicGraph != null ? SchematicGraph.name : "Graph";
            tooltip = SchematicGraph != null ? $"{ObjectNames.NicifyVariableName(SchematicGraph.GetType().Name)}: {SchematicGraph.name}{(SeparateWindow ? " (its own window)" : "")}" : name;
        }
        titleContent = new GUIContent(name, SchematicTabIcons.ForGraph(FollowsSelection ? typeof(SchematicGraph) : SchematicGraph != null ? SchematicGraph.GetType() : null), tooltip);
    }

    /// <summary>The IO Dock is showing the dock: the canvas takes the whole tab.</summary>
    internal void LendDock() => _sidebarAndContent?.SetSidebarShown(false);

    /// <summary>The IO Dock closed: the dock goes back in this window's sidebar.</summary>
    internal void ReturnDock()
    {
        if (_sidebarTabs == null || _sidebarAndContent == null)
            return;
        if (_sidebarTabs.parent != _sidebarAndContent.SidebarContainer)
            _sidebarAndContent.SidebarContainer.Add(_sidebarTabs);
        _sidebarAndContent.SetSidebarShown(true);
    }

    protected override void Update()
    {
        // The Graph tab with nothing loaded (just opened, or after a script reload): its graph loaded again, or wait.
        if (FollowsSelection && Canvas == null)
        {
            if (SchematicGraph != null)
            {
                try { ShowGraph(SchematicGraph); }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    SchematicGraph = null;
                }
            }
            else
                ShowEmpty();
            return;
        }
        base.Update();
    }

    /// <summary>What the Graph tab is on, for its header (and the IO Dock's): a name, and what it is.</summary>
    internal static string DescribeFollowed(out string detail, out Object target)
    {
        var follower = Follower;
        target = null;
        detail = "select a Schematic Object";
        if (follower == null || follower.SchematicGraph == null)
            return "Nothing selected";
        if (follower.FollowedInstance != null)
        {
            var instance = follower.FollowedInstance;
            target = instance;
            detail = PrefabStageUtility.GetPrefabStage(instance) != null ? "in Prefab Mode"
                : $"instance in {instance.scene.name}{(Application.isPlaying ? " (playing)" : "")}";
            return instance.name;
        }
        if (follower.FollowedPrefab != null)
        {
            target = follower.FollowedPrefab;
            detail = "prefab";
            return follower.FollowedPrefab.name;
        }
        target = follower.SchematicGraph;
        detail = "graph";
        return follower.SchematicGraph.name;
    }

    internal static void RaiseFollowerChanged(SchematicGraphEditorWindow window) => FollowerChanged?.Invoke(window);
}

namespace Remedy.Schematics
{
    /// <summary>
    /// Along the top of the Graph tab and the IO Dock: what they're on (the instance, or the prefab or graph), click to
    /// find it; and the lock that keeps them on it whatever is selected.
    /// </summary>
    internal sealed class SchematicTargetHeader : VisualElement
    {
        private readonly Image _icon;
        private readonly Label _title, _detail;
        private readonly Button _lock;

        public SchematicTargetHeader()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            style.flexShrink = 0;
            style.height = 22;
            style.paddingLeft = 6;
            style.paddingRight = 2;
            style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            style.borderBottomWidth = 1;
            style.borderBottomColor = new Color(0f, 0f, 0f, 0.4f);

            _icon = new Image { scaleMode = ScaleMode.ScaleToFit };
            _icon.style.width = _icon.style.height = 16;
            _icon.style.marginRight = 4;
            Add(_icon);
            _title = new Label();
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.marginRight = 6;
            Add(_title);
            _detail = new Label();
            _detail.style.opacity = 0.6f;
            _detail.style.overflow = Overflow.Hidden;
            _detail.style.flexShrink = 1;
            Add(_detail);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            Add(spacer);
            _lock = new Button(ToggleLock) { tooltip = "Lock: stay on this object whatever is selected." };
            _lock.style.width = 22;
            _lock.style.height = 18;
            _lock.style.paddingLeft = _lock.style.paddingRight = 0;
            Add(_lock);

            // Click the name: find the object (in the Hierarchy or Project window).
            RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == _lock) return;
                SchematicGraphEditorWindow.DescribeFollowed(out _, out var target);
                if (target != null) EditorGUIUtility.PingObject(target);
            });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                SchematicGraphEditorWindow.FollowerChanged += OnChanged;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => SchematicGraphEditorWindow.FollowerChanged -= OnChanged);
        }

        private void OnChanged(SchematicGraphEditorWindow _) => Refresh();

        private void ToggleLock()
        {
            var follower = SchematicGraphEditorWindow.Follower;
            if (follower == null) return;
            follower.FollowLocked = !follower.FollowLocked;
            // Unlocked: back to what's selected now.
            if (!follower.FollowLocked)
                SchematicFollow.Apply(Selection.activeObject, follower);
            SchematicGraphEditorWindow.RaiseFollowerChanged(follower);
        }

        private void Refresh()
        {
            _title.text = SchematicGraphEditorWindow.DescribeFollowed(out string detail, out var target);
            _detail.text = detail;
            var icon = target is GameObject go ? (Texture)AssetPreview.GetMiniThumbnail(go) : target != null ? AssetPreview.GetMiniThumbnail(target) : null;
            _icon.image = icon;
            _icon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            var follower = SchematicGraphEditorWindow.Follower;
            bool locked = follower != null && follower.FollowLocked;
            var content = EditorGUIUtility.IconContent(locked ? "IN LockButton on" : "IN LockButton");
            _lock.text = content?.image == null ? (locked ? "L" : "U") : "";
            _lock.style.backgroundImage = content?.image as Texture2D;
            _lock.tooltip = locked ? "Locked on this object: unlock to follow the selection again." : "Lock: stay on this object whatever is selected.";
        }
    }

    /// <summary>The Graph tab follows the selection: what a selected object's graph is, and whose.</summary>
    [InitializeOnLoad]
    internal static class SchematicFollow
    {
        static SchematicFollow()
        {
            Selection.selectionChanged += () => Apply(Selection.activeObject, null);
            PrefabStage.prefabStageOpened += stage => EditorApplication.delayCall += () =>
            {
                if (stage != null && stage.prefabContentsRoot != null)
                    Apply(stage.prefabContentsRoot, null);
            };
            // In and out of Play Mode the scene's objects are made again: the selection's are found again.
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode || change == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += () => Apply(Selection.activeObject, null);
            };
        }

        /// <summary>Moves the Graph tab to what's selected, if it's (part of) a Schematic Object; otherwise it stays.</summary>
        internal static void Apply(Object selected, SchematicGraphEditorWindow follower)
        {
            follower ??= SchematicGraphEditorWindow.Follower;
            if (follower == null || follower.FollowLocked)
                return;
            if (!Resolve(selected, out var graph, out var instance, out var prefab))
                return; // something else: it keeps showing the last
            try { follower.Follow(graph, instance, prefab); }
            catch (Exception exception) { Debug.LogException(exception, follower); }
        }

        /// <summary>
        /// The graph a selection is for: a graph asset itself; or the nearest Schematic Object it's part of with a graph
        /// (in the scene or Prefab Mode: that instance; in the Project window: that prefab).
        /// </summary>
        internal static bool Resolve(Object selected, out SchematicGraph graph, out GameObject instance, out GameObject prefab)
        {
            graph = null;
            instance = null;
            prefab = null;
            // (The Graph tab is for objects' graphs: other kinds open in their own windows.)
            if (selected is SchematicGraph asset)
            {
                if (!SchematicGraphEditorWindow.IsObjectGraph(asset))
                    return false;
                graph = asset;
                prefab = asset.Prefab;
                return true;
            }
            if (selected is Component component)
                selected = component.gameObject;
            if (!(selected is GameObject gameObject))
                return false;
            for (var t = gameObject.transform; t != null; t = t.parent)
            {
                if (!t.TryGetComponent(out SchematicObject schematic) || schematic.SchematicGraph == null)
                    continue;
                if (!SchematicGraphEditorWindow.IsObjectGraph(schematic.SchematicGraph))
                    return false;
                graph = schematic.SchematicGraph;
                if (EditorUtility.IsPersistent(schematic.gameObject))
                    prefab = schematic.gameObject;
                else
                    instance = schematic.gameObject;
                if (graph.Prefab != null)
                    prefab = graph.Prefab;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The IO Dock as a window of its own (docked beside the Hierarchy): the Global, Prefab and Graph tabs of the object
    /// the Graph tab is on, with room to breathe. It shows the Graph tab's own dock (opening the Graph tab, behind the
    /// Scene view, if it isn't open), so dragging rows into the graph, finding their nodes and everything else works as
    /// in the graph window. Tools > Remedy > Schematics > IO Dock.
    /// </summary>
    public sealed class SchematicIODockWindow : EditorWindow
    {
        private VisualElement _holder;
        private Label _empty;

        /// <summary>Whether an IO Dock is open.</summary>
        internal static bool IsOpen => HasOpenInstances<SchematicIODockWindow>();

        [MenuItem("Tools/Remedy/Schematics/IO Dock", false, 21)]
        private static void Open()
        {
            // Beside the Hierarchy (Unity 6.3's is "HierarchyWindow", earlier versions' "SceneHierarchyWindow").
            var assembly = typeof(Editor).Assembly;
            var hierarchy = assembly.GetType("UnityEditor.HierarchyWindow") ?? assembly.GetType("UnityEditor.SceneHierarchyWindow");
            var window = hierarchy != null ? GetWindow<SchematicIODockWindow>("IO Dock", true, hierarchy) : GetWindow<SchematicIODockWindow>("IO Dock");
            window.Show();
        }

        private void OnEnable() => SchematicGraphEditorWindow.FollowerChanged += OnFollowerChanged;

        private void OnDisable()
        {
            SchematicGraphEditorWindow.FollowerChanged -= OnFollowerChanged;
            // Closed: the dock goes back to the Graph tab's sidebar.
            var follower = SchematicGraphEditorWindow.Follower;
            if (follower != null)
                follower.ReturnDock();
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent("IO Dock", SchematicTabIcons.Get("io-dock"), "IO Dock: the behaviors, Variables and Signals of what the Graph tab is on");
            var root = rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("IODock");
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.Add(new SchematicTargetHeader());
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.AddToClassList("iodock");
            _holder = new VisualElement();
            scroll.Add(_holder);
            root.Add(scroll);
            _empty = new Label("Select a Schematic Object to see its behaviors, Variables and Signals here.");
            _empty.style.whiteSpace = WhiteSpace.Normal;
            _empty.style.opacity = 0.6f;
            _empty.style.paddingLeft = _empty.style.paddingRight = 12;
            _empty.style.paddingTop = 12;
            // (Not while this window is still being made: opening the Graph tab then upsets the layout.)
            EditorApplication.delayCall += Take;
        }

        private void OnFollowerChanged(SchematicGraphEditorWindow _) => Take();

        // The Graph tab's dock, shown here (the Graph tab opened behind the Scene view if it isn't open).
        private void Take()
        {
            if (this == null || _holder == null)
                return;
            var follower = SchematicGraphEditorWindow.EnsureFollower(focus: false);
            var tabs = follower != null ? follower.DockTabs : null;
            if (tabs == null)
            {
                if (_empty.parent != _holder)
                {
                    _holder.Clear();
                    _holder.Add(_empty);
                }
                return;
            }
            follower.LendDock();
            if (tabs.parent != _holder)
            {
                _holder.Clear();
                _holder.Add(tabs);
            }
        }
    }

    /// <summary>
    /// Window tab icons: one per kind of graph, the IO Dock's and the State Machine window's. Drawn as SVGs in
    /// Styling/Icons~ (make_icons.py), rendered into Resources/Textures in a grey for the dark skin and a dark grey for
    /// the light one.
    /// </summary>
    public static class SchematicTabIcons
    {
        /// <summary>A tab icon by name (object, singleton, sound-cue, timeline, state-machine, io-dock), for the editor's skin.</summary>
        public static Texture2D Get(string name)
            => Resources.Load<Texture2D>($"Textures/tab-{name}{(EditorGUIUtility.isProSkin ? "" : "-light")}");

        /// <summary>The icon for a kind of graph (by type, so the kinds needn't be known here); an object's graph's for any other.</summary>
        public static Texture2D ForGraph(Type graphType)
            => Get(graphType?.Name switch
            {
                "SoundCueGraph" => "sound-cue",
                "TimelineGraph" => "timeline",
                "SingletonGraph" => "singleton",
                _ => "object",
            });
    }
}
