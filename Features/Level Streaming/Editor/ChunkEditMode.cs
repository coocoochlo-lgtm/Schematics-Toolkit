using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelStreaming.Editor
{
    /// <summary>
    /// Editing one level chunk on its own: double-click a chunk's volume in the Scene view. Everything outside the chunk
    /// is greyed out and can't be selected, and new objects are put into the chunk. Esc (or the overlay's Exit button,
    /// or double-clicking outside the chunk) goes back to normal.
    /// </summary>
    [InitializeOnLoad]
    public static class ChunkEditMode
    {
        /// <summary>The chunk being edited, or null.</summary>
        public static LevelChunk Active { get; private set; }

        /// <summary>Colour everything outside the chunk is tinted with while editing it.</summary>
        public static Color GreyTint = new Color(0.32f, 0.32f, 0.34f, 1f);

        private static readonly Dictionary<Renderer, MaterialPropertyBlock> _savedBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        private static readonly List<Renderer> _greyed = new List<Renderer>();
        private static readonly List<GameObject> _unpickable = new List<GameObject>();
        private static readonly HashSet<GameObject> _knownRoots = new HashSet<GameObject>();

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        static ChunkEditMode()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.playModeStateChanged += _ => Exit();
            AssemblyReloadEvents.beforeAssemblyReload += Exit;
            EditorSceneManager.sceneClosing += (_, _) => Exit();
        }

        public static void Enter(LevelChunk chunk)
        {
            Exit();
            if (chunk == null)
                return;
            Active = chunk;
            GreyOutOthers(chunk);
            DisablePickingOutside(chunk);
            RememberRoots();
            Selection.activeGameObject = chunk.gameObject;
            SceneView.RepaintAll();
        }

        public static void Exit()
        {
            if (Active == null && _greyed.Count == 0 && _unpickable.Count == 0)
                return;

            foreach (var renderer in _greyed)
            {
                if (renderer == null) continue;
                renderer.SetPropertyBlock(_savedBlocks.TryGetValue(renderer, out var block) ? block : null);
            }
            _greyed.Clear();
            _savedBlocks.Clear();

            var visibility = SceneVisibilityManager.instance;
            foreach (var gameObject in _unpickable)
                if (gameObject != null)
                    visibility.EnablePicking(gameObject, true);
            _unpickable.Clear();

            _knownRoots.Clear();
            Active = null;
            SceneView.RepaintAll();
        }

        // Tints every renderer outside the chunk with a property block (nothing is saved into the scene).
        private static void GreyOutOthers(LevelChunk chunk)
        {
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer == null || renderer.transform.IsChildOf(chunk.transform) || EditorUtility.IsPersistent(renderer))
                    continue;
                if (renderer.HasPropertyBlock())
                {
                    var saved = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(saved);
                    _savedBlocks[renderer] = saved;
                }
                var block = new MaterialPropertyBlock();
                if (_savedBlocks.TryGetValue(renderer, out var existing))
                    renderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, GreyTint);
                block.SetColor(ColorProperty, GreyTint);
                renderer.SetPropertyBlock(block);
                _greyed.Add(renderer);
            }
        }

        // Every scene root (and anything else) outside the chunk can't be picked; the chunk's own branch can.
        private static void DisablePickingOutside(LevelChunk chunk)
        {
            var visibility = SceneVisibilityManager.instance;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    DisablePickingOutside(root.transform, chunk.transform, visibility);
            }
        }

        private static void DisablePickingOutside(Transform node, Transform chunk, SceneVisibilityManager visibility)
        {
            if (node == chunk)
                return;
            if (!chunk.IsChildOf(node))
            {
                // Entirely outside the chunk.
                if (!visibility.IsPickingDisabled(node.gameObject, false))
                {
                    visibility.DisablePicking(node.gameObject, true);
                    _unpickable.Add(node.gameObject);
                }
                return;
            }
            // An ancestor of the chunk: itself not pickable, its other children handled one by one.
            if (!visibility.IsPickingDisabled(node.gameObject, false))
            {
                visibility.DisablePicking(node.gameObject, false);
                _unpickable.Add(node.gameObject);
            }
            for (int i = 0; i < node.childCount; i++)
                DisablePickingOutside(node.GetChild(i), chunk, visibility);
        }

        private static void RememberRoots()
        {
            _knownRoots.Clear();
            var scene = Active.gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
                _knownRoots.Add(root);
        }

        // New top-level objects made while editing a chunk go into it.
        private static void OnHierarchyChanged()
        {
            if (Active == null || Application.isPlaying)
                return;
            var scene = Active.gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (_knownRoots.Contains(root) || root == Active.gameObject || Active.transform.IsChildOf(root.transform))
                    continue;
                _knownRoots.Add(root);
                Undo.SetTransformParent(root.transform, Active.transform, "Add To Chunk");
            }
        }

        private static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            var chunks = Object.FindObjectsByType<LevelChunk>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (e.type == EventType.Repaint)
            {
                foreach (var chunk in chunks)
                    DrawVolume(chunk, chunk == Active, Active != null && chunk != Active);
            }

            // Double-click a volume to edit that chunk; double-click outside the edited one to stop.
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && !e.alt)
            {
                var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                var hit = Nearest(chunks, ray);
                if (Active != null && hit != Active)
                {
                    Exit();
                    if (hit != null) Enter(hit);
                    e.Use();
                }
                else if (Active == null && hit != null)
                {
                    Enter(hit);
                    e.Use();
                }
            }

            if (Active != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Exit();
                e.Use();
            }

            if (Active != null)
                DrawOverlay(view);
        }

        private static void DrawVolume(LevelChunk chunk, bool active, bool dimmed)
        {
            var color = chunk.Color;
            if (dimmed) color.a *= 0.25f;
            using (new Handles.DrawingScope(color, chunk.transform.localToWorldMatrix))
            {
                Handles.DrawWireCube(chunk.Center, chunk.Size);
                if (active)
                {
                    // A thicker outline for the chunk being edited.
                    Handles.DrawWireCube(chunk.Center, chunk.Size * 1.002f);
                    Handles.DrawWireCube(chunk.Center, chunk.Size * 0.998f);
                }
            }
            if (!dimmed)
                Handles.Label(chunk.transform.TransformPoint(chunk.Center + Vector3.up * chunk.Size.y * 0.5f), chunk.name, EditorStyles.whiteBoldLabel);
        }

        private static LevelChunk Nearest(LevelChunk[] chunks, Ray ray)
        {
            LevelChunk best = null;
            float bestDistance = float.MaxValue;
            foreach (var chunk in chunks)
            {
                // The ray in the chunk's own space, against its box.
                var t = chunk.transform;
                var local = new Ray(t.InverseTransformPoint(ray.origin), t.InverseTransformDirection(ray.direction));
                var box = new Bounds(chunk.Center, chunk.Size);
                if ((box.Contains(local.origin) || box.IntersectRay(local, out _)) )
                {
                    float distance = (chunk.WorldBounds.center - ray.origin).sqrMagnitude;
                    // Innermost first when the camera is inside a volume.
                    if (box.Contains(local.origin)) distance = chunk.Size.sqrMagnitude * 0.0001f;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = chunk;
                    }
                }
            }
            return best;
        }

        private static void DrawOverlay(SceneView view)
        {
            Handles.BeginGUI();
            var rect = new Rect(10, 10, 330, 54);
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));
            GUILayout.Label($"Editing chunk: {Active.name}", EditorStyles.boldLabel);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("New objects go into it.", EditorStyles.miniLabel);
                if (GUILayout.Button("Fit Volume", EditorStyles.miniButton, GUILayout.Width(70)))
                    LevelChunkEditor.FitVolume(Active);
                if (GUILayout.Button("Exit (Esc)", EditorStyles.miniButton, GUILayout.Width(70)))
                    Exit();
            }
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
