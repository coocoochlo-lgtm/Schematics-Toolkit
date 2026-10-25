using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public partial class SchematicGraphEditorWindow
{
    /// <summary>Opened for Prefab Mode (reused when that prefab opens again). Serialized, so it survives script reloads.</summary>
    [SerializeField] internal bool OpenedForPrefabStage;
}

namespace Remedy.Schematics
{
    /// <summary>
    /// Opening a Schematic Object in Prefab Mode also opens its Schematic Graph, as a tab docked beside the Scene view,
    /// so the prefab and its graph are one click apart. The tab stays open after leaving Prefab Mode (in Play Mode it
    /// shows the graph's flow on a running instance), and opening the prefab again brings it back to the front rather
    /// than opening another. The Hierarchy's Scene | Graph bar switches between them. Turn the
    /// automatic tab off under Tools > Remedy > Schematics > Open Graph with Prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class PrefabGraphTab
    {
        private const string EnabledPref = "Remedy.Schematics.PrefabGraphTab";
        private const string MenuPath = "Tools/Remedy/Schematics/Open Graph with Prefab";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        static PrefabGraphTab()
        {
            PrefabStage.prefabStageOpened += OnStageOpened;
        }

        [MenuItem(MenuPath, false, 30)]
        private static void ToggleEnabled() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>The graph of the Schematic Object open in Prefab Mode, or null.</summary>
        public static SchematicGraph CurrentGraph
        {
            get
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                var root = stage != null ? stage.prefabContentsRoot : null;
                return root != null && root.TryGetComponent(out SchematicObject schematic) ? schematic.SchematicGraph : null;
            }
        }

        private static void OnStageOpened(PrefabStage stage)
        {
            if (!Enabled)
                return;
            // Wait a tick: the stage's contents and the Scene view are still being set up.
            EditorApplication.delayCall += () =>
            {
                // Graph tabs from other prefabs stay open: closing or leaving a prefab doesn't close its graph.
                var graph = CurrentGraph;
                if (graph == null)
                    return;
                var window = Show(graph, focus: false);
                // The prefab stays in front; the graph is a tab away.
                if (window != null && SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Focus();
            };
        }


        /// <summary>
        /// Shows a graph's window: the one already open for it, else a new one docked beside the Scene view.
        /// </summary>
        public static SchematicGraphEditorWindow Show(SchematicGraph graph, bool focus = true)
        {
            if (graph == null)
                return null;
            // The Graph tab, when it's open: it moves to this graph (unless it's locked) rather than another tab opening.
            var follower = SchematicGraphEditorWindow.Follower;
            if (follower != null)
            {
                if (!follower.FollowLocked)
                {
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    var root = stage != null ? stage.prefabContentsRoot : null;
                    bool stageRoot = root != null && root.TryGetComponent(out SchematicObject schematic) && schematic.SchematicGraph == graph;
                    follower.Follow(graph, stageRoot ? root : null, graph.Prefab);
                }
                if (focus)
                    follower.Focus();
                return follower;
            }
            // Any window already on this graph, even one whose canvas a script reload dropped (it's loaded again).
            var window = Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>().FirstOrDefault(w => w != null && w.SchematicGraph == graph);
            if (window == null)
            {
                window = EditorWindow.CreateWindow<SchematicGraphEditorWindow>(typeof(SceneView));
                window.SchematicGraph = graph;
                window.OpenedForPrefabStage = true;
                window.Load(graph);
            }
            else if (window.Canvas == null)
                window.Load(graph);
            if (focus)
                window.Focus();
            return window;
        }
    }

    /// <summary>
    /// A "Scene | Graph" bar in the Hierarchy window, between the Prefab Mode header and the object list, while a
    /// Schematic Object is open in Prefab Mode. Unity has no public way to add to the Hierarchy, so the bar is placed by
    /// finding the Hierarchy's own list element (Unity 6's UI Toolkit Hierarchy); where that isn't found (an older or
    /// changed Hierarchy) there's no bar, and the graph button on the object's row still opens the graph.
    /// </summary>
    [InitializeOnLoad]
    public static class PrefabGraphHierarchyBar
    {
        private const string BarName = "schematics-prefab-graph-bar";
        private static double _nextCheck;
        private static bool _graphChosen;

        static PrefabGraphHierarchyBar()
        {
            EditorApplication.update += Update;
            PrefabStage.prefabStageOpened += _ => { _graphChosen = false; _nextCheck = 0; };
            PrefabStage.prefabStageClosing += _ => _nextCheck = 0;
        }

        /// <summary>Whether a bar is showing in some Hierarchy window right now.</summary>
        public static bool Showing { get; private set; }

        // Hierarchy windows can open, close or rebuild their UI at any time, so this checks a few times a second.
        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck)
                return;
            _nextCheck = EditorApplication.timeSinceStartup + 0.4;

            var graph = PrefabGraphTab.CurrentGraph;
            // Follow the tab actually in use (clicking the tabs themselves counts too).
            var focused = EditorWindow.focusedWindow;
            if (focused is SceneView)
                _graphChosen = false;
            else if (focused is SchematicGraphEditorWindow graphWindow && graph != null && graphWindow.SchematicGraph == graph)
                _graphChosen = true;
            bool showing = false;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                // Unity 6.3's Hierarchy is "HierarchyWindow"; earlier versions call it "SceneHierarchyWindow".
                string typeName = window != null ? window.GetType().Name : null;
                if (typeName != "HierarchyWindow" && typeName != "SceneHierarchyWindow")
                    continue;
                var root = window.rootVisualElement;
                if (root == null)
                    continue;
                var bar = root.Q(BarName);
                if (graph == null)
                {
                    bar?.RemoveFromHierarchy();
                    continue;
                }
                if (bar == null)
                {
                    var list = root.Q(className: "hierarchy");
                    if (list?.parent == null)
                        continue;
                    bar = Build();
                    list.parent.Insert(list.parent.IndexOf(list), bar);
                }
                Refresh(bar);
                showing = true;
            }
            Showing = showing;
        }

        private static VisualElement Build()
        {
            var bar = new VisualElement { name = BarName };
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.flexShrink = 0;
            bar.style.height = 22;
            bar.style.paddingLeft = bar.style.paddingRight = 4;
            bar.style.paddingTop = bar.style.paddingBottom = 2;
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);

            bar.Add(Segment("scene", "Scene", "Edit the prefab in the Scene view.", () =>
            {
                _graphChosen = false;
                SceneView.lastActiveSceneView?.Focus();
            }));
            bar.Add(Segment("graph", "Graph", "Edit this prefab's Schematic Graph (a tab beside the Scene view).", () =>
            {
                _graphChosen = true;
                PrefabGraphTab.Show(PrefabGraphTab.CurrentGraph);
            }));
            return bar;
        }

        private static Button Segment(string name, string text, string tooltip, System.Action click)
        {
            var button = new Button(click) { name = name, text = text, tooltip = tooltip };
            button.style.flexGrow = 1;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.marginTop = button.style.marginBottom = 0;
            return button;
        }

        // The chosen side is highlighted (a graph window that's open but not chosen stays a tab away).
        private static void Refresh(VisualElement bar)
        {
            var accent = EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);
            foreach (var button in bar.Query<Button>().ToList())
            {
                bool on = button.name == "graph" ? _graphChosen : !_graphChosen;
                button.style.backgroundColor = on ? accent : new StyleColor(StyleKeyword.Null);
                button.style.color = on ? Color.white : new StyleColor(StyleKeyword.Null);
                button.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            }
        }
    }
}
