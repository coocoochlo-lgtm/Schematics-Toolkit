using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// Opening a Schematic Object in Prefab Mode also opens its Schematic Graph, as a tab docked beside the Scene view,
    /// so the prefab and its graph are one click apart. Leaving Prefab Mode closes the tab again (only tabs opened this
    /// way; a graph window you opened yourself stays). Turn it off under Tools > Remedy > Schematics > Open Graph with Prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class PrefabGraphTab
    {
        private const string EnabledPref = "Remedy.Schematics.PrefabGraphTab";
        private const string MenuPath = "Tools/Remedy/Schematics/Open Graph with Prefab";

        // Graph windows opened for a prefab stage, so leaving it closes only those.
        private static readonly List<SchematicGraphEditorWindow> _opened = new();

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        static PrefabGraphTab()
        {
            PrefabStage.prefabStageOpened += OnStageOpened;
            PrefabStage.prefabStageClosing += OnStageClosing;
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
                var graph = CurrentGraph;
                CloseOpened(except: graph);
                if (graph == null)
                    return;
                var window = Show(graph, focus: false);
                // The prefab stays in front; the graph is a tab away.
                if (window != null && SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Focus();
            };
        }

        private static void OnStageClosing(PrefabStage stage)
        {
            // Closing happens before the next stage (a parent prefab, or the scene) opens; that one reopens its own.
            EditorApplication.delayCall += () =>
            {
                if (PrefabStageUtility.GetCurrentPrefabStage() == null)
                    CloseOpened(except: null);
            };
        }

        /// <summary>
        /// Shows a graph's window: the one already open for it, else a new one docked beside the Scene view.
        /// </summary>
        public static SchematicGraphEditorWindow Show(SchematicGraph graph, bool focus = true)
        {
            if (graph == null)
                return null;
            var window = SchematicGraphEditorWindow.FindOpen(graph);
            if (window == null)
            {
                window = EditorWindow.CreateWindow<SchematicGraphEditorWindow>(typeof(SceneView));
                window.SchematicGraph = graph;
                window.Load(graph);
                _opened.Add(window);
            }
            if (focus)
                window.Focus();
            return window;
        }

        private static void CloseOpened(SchematicGraph except)
        {
            for (int i = _opened.Count - 1; i >= 0; i--)
            {
                var window = _opened[i];
                if (window != null && except != null && window.SchematicGraph == except)
                    continue;
                _opened.RemoveAt(i);
                if (window != null)
                    window.Close();
            }
        }
    }

    /// <summary>
    /// "Scene | Graph" in the Scene view while a Schematic Object is open in Prefab Mode: switches between the prefab and
    /// its graph's tab.
    /// </summary>
    [Overlay(typeof(SceneView), "schematics-prefab-graph", "Schematic Prefab", true)]
    public sealed class PrefabGraphOverlay : Overlay, ITransientOverlay
    {
        public bool visible => PrefabGraphTab.CurrentGraph != null;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;

            var scene = new Button(() => SceneView.lastActiveSceneView?.Focus())
            {
                text = "Scene",
                tooltip = "The prefab (you're here).",
            };
            scene.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(scene);

            root.Add(new Button(() => PrefabGraphTab.Show(PrefabGraphTab.CurrentGraph))
            {
                text = "Graph",
                tooltip = "This prefab's Schematic Graph (opens it as a tab beside the Scene view).",
            });
            return root;
        }
    }
}
