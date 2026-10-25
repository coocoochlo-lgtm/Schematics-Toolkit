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
    /// than opening another. Turn the automatic tab off under Tools > Remedy > Schematics > Open Graph with Prefab.
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
        /// Shows a graph in its type's window (for an object's graph, the Graph tab, which moves to it); the prefab open
        /// in Prefab Mode is the instance it's on when it's that graph's.
        /// </summary>
        public static SchematicGraphEditorWindow Show(SchematicGraph graph, bool focus = true)
        {
            if (graph == null)
                return null;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var root = stage != null ? stage.prefabContentsRoot : null;
            bool stageRoot = root != null && root.TryGetComponent(out SchematicObject schematic) && schematic.SchematicGraph == graph;
            return SchematicGraphEditorWindow.OpenInTypeWindow(graph, newWindow: false, focus: focus, instance: stageRoot ? root : null);
        }
    }
}
