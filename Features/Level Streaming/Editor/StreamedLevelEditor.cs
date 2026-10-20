using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelStreaming.Editor
{
    [CustomEditor(typeof(StreamedLevel))]
    public class StreamedLevelEditor : UnityEditor.Editor
    {
        private int _rootCount;
        private int _objectCount;
        private bool _counted;

        private void OnEnable()
        {
            _counted = false;
            EditorApplication.hierarchyChanged += Recount;
        }

        private void OnDisable() => EditorApplication.hierarchyChanged -= Recount;

        private void Recount() => _counted = false;

        public override void OnInspectorGUI()
        {
            var level = (StreamedLevel)target;

            if (Application.isPlaying)
                DrawRuntimeState(level);
            else
                DrawContentSummary(level);

            DrawDefaultInspector();
        }

        private void DrawContentSummary(StreamedLevel level)
        {
            if (!_counted)
            {
                _counted = true;
                var roots = new List<GameObject>();
                level.CollectContentRoots(roots);
                _rootCount = 0;
                _objectCount = 0;
                var transforms = new List<Transform>();
                foreach (var root in roots)
                {
                    if (root == null || !root.activeSelf)
                        continue;
                    _rootCount++;
                    root.GetComponentsInChildren(true, transforms);
                    _objectCount += transforms.Count;
                }
            }

            EditorGUILayout.HelpBox(
                $"Streams in {_objectCount} objects under {_rootCount} top-level objects, a few per frame, when this scene " +
                "is added. The scene is saved as it is; its content is only put to sleep in builds and in Play Mode.",
                MessageType.Info);

            if (!level.gameObject.activeInHierarchy || !level.enabled)
                EditorGUILayout.HelpBox("This object (or component) is switched off, so the content won't be put to sleep.", MessageType.Warning);

            var scene = level.gameObject.scene;
            if (scene.IsValid() && CountInScene(scene) > 1)
                EditorGUILayout.HelpBox("This scene has more than one Streamed Level; only the first is used.", MessageType.Warning);

            if (scene.IsValid() && !string.IsNullOrEmpty(scene.path) && SceneUtility.GetBuildIndexByScenePath(scene.path) < 0)
                EditorGUILayout.HelpBox("This scene isn't in the Build Profile's scene list: it loads in the editor, but a build can't load it.", MessageType.None);
        }

        private static void DrawRuntimeState(StreamedLevel level)
        {
            string state = !level.IsPrepared ? "Not prepared (woke all at once)"
                : level.IsStreamedIn ? "Streamed in"
                : level.IsStreaming ? "Streaming in"
                : "Waiting";
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            EditorGUI.ProgressBar(rect, level.Progress, $"{state}: {level.ObjectCount} objects");
        }

        private static int CountInScene(Scene scene)
        {
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
                count += root.GetComponentsInChildren<StreamedLevel>(true).Length;
            return count;
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying && ((StreamedLevel)target).IsStreaming;

        [MenuItem("GameObject/Level Streaming/Streamed Level", false, 22)]
        private static void CreateStreamedLevel()
        {
            var scene = Selection.activeGameObject != null ? Selection.activeGameObject.scene : SceneManager.GetActiveScene();
            if (SelectExisting(scene))
                return;
            var gameObject = new GameObject("Streamed Level");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Streamed Level");
            if (scene.IsValid() && gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(gameObject, scene);
            Undo.AddComponent<StreamedLevel>(gameObject);
            Selection.activeGameObject = gameObject;
        }

        [MenuItem("GameObject/Level Streaming/Streamed Level From Selection", false, 23)]
        private static void CreateFromSelection()
        {
            var selected = Selection.transforms;
            if (selected.Length == 0)
            {
                CreateStreamedLevel();
                return;
            }
            var scene = selected[0].gameObject.scene;
            if (SelectExisting(scene))
                return;

            var gameObject = new GameObject("Streamed Level");
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Streamed Level");
            if (scene.IsValid() && gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(gameObject, scene);
            foreach (var t in selected)
                Undo.SetTransformParent(t, gameObject.transform, "Create Streamed Level");
            var level = Undo.AddComponent<StreamedLevel>(gameObject);
            // Only what was wrapped streams; the rest of the scene wakes as usual.
            level.Content = StreamedLevel.ContentScope.ChildrenOnly;
            Selection.activeGameObject = gameObject;
        }

        private static bool SelectExisting(Scene scene)
        {
            if (!scene.IsValid())
                return false;
            foreach (var root in scene.GetRootGameObjects())
            {
                var existing = root.GetComponentInChildren<StreamedLevel>(true);
                if (existing == null)
                    continue;
                Debug.Log($"[Level Streaming] '{scene.name}' already has a Streamed Level; one per scene is enough.", existing);
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing);
                return true;
            }
            return false;
        }

        [MenuItem("GameObject/Level Streaming/Streamed Level", true)]
        private static bool CanCreate() => !Application.isPlaying && SceneManager.sceneCount > 0;

        [MenuItem("GameObject/Level Streaming/Streamed Level From Selection", true)]
        private static bool CanCreateFromSelection() => CanCreate();
    }
}
