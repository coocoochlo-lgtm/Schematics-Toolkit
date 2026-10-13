using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Remedy.LevelStreaming.Editor
{
    [CustomEditor(typeof(LevelChunk))]
    public class LevelChunkEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle _handle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            var chunk = (LevelChunk)target;
            bool editing = ChunkEditMode.Active == chunk;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(editing ? "Stop Editing Chunk" : "Edit Chunk", GUILayout.Height(24)))
                {
                    if (editing) ChunkEditMode.Exit();
                    else ChunkEditMode.Enter(chunk);
                }
                if (GUILayout.Button("Fit Volume", GUILayout.Height(24), GUILayout.Width(90)))
                    FitVolume(chunk);
            }
            EditorGUILayout.HelpBox("Double-click the chunk's volume in the Scene view to edit it on its own.", MessageType.None);
            DrawDefaultInspector();
        }

        private void OnSceneGUI()
        {
            var chunk = (LevelChunk)target;
            using (new Handles.DrawingScope(chunk.Color, chunk.transform.localToWorldMatrix))
            {
                _handle.center = chunk.Center;
                _handle.size = chunk.Size;
                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(chunk, "Resize Chunk");
                    chunk.Center = _handle.center;
                    chunk.Size = _handle.size;
                }
            }
        }

        /// <summary>Sizes a chunk's volume around everything in it (with a little room).</summary>
        public static void FitVolume(LevelChunk chunk)
        {
            var renderers = chunk.GetComponentsInChildren<Renderer>(true);
            var colliders = chunk.GetComponentsInChildren<Collider>(true);
            bool any = false;
            var local = new Bounds();
            void Add(Bounds world)
            {
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                    var point = chunk.transform.InverseTransformPoint(Vector3.Scale(corner, world.size) + world.min);
                    if (!any) { local = new Bounds(point, Vector3.zero); any = true; }
                    else local.Encapsulate(point);
                }
            }
            foreach (var renderer in renderers) Add(renderer.bounds);
            foreach (var collider in colliders) Add(collider.bounds);
            if (!any)
            {
                for (int i = 0; i < chunk.transform.childCount; i++)
                    Add(new Bounds(chunk.transform.GetChild(i).position, Vector3.one));
            }
            if (!any)
                return;

            Undo.RecordObject(chunk, "Fit Chunk Volume");
            chunk.Center = local.center;
            chunk.Size = local.size + Vector3.one * 2f;
            SceneView.RepaintAll();
        }

        [MenuItem("GameObject/Level Streaming/Level Chunk From Selection", false, 20)]
        private static void CreateFromSelection()
        {
            var selected = Selection.transforms;
            var chunkObject = new GameObject("Level Chunk");
            Undo.RegisterCreatedObjectUndo(chunkObject, "Create Level Chunk");
            if (selected.Length > 0)
            {
                Vector3 center = Vector3.zero;
                foreach (var t in selected) center += t.position;
                chunkObject.transform.position = center / selected.Length;
                SceneManagerMove(chunkObject, selected[0].gameObject.scene);
                foreach (var t in selected)
                    Undo.SetTransformParent(t, chunkObject.transform, "Create Level Chunk");
            }
            var chunk = Undo.AddComponent<LevelChunk>(chunkObject);
            FitVolume(chunk);
            Selection.activeGameObject = chunkObject;
        }

        private static void SceneManagerMove(GameObject gameObject, UnityEngine.SceneManagement.Scene scene)
        {
            if (scene.IsValid() && gameObject.scene != scene)
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(gameObject, scene);
        }

        [MenuItem("GameObject/Level Streaming/Level Streaming Zone", false, 21)]
        private static void CreateZone()
        {
            var zone = new GameObject("Level Streaming Zone", typeof(LevelStreamingZone));
            Undo.RegisterCreatedObjectUndo(zone, "Create Level Streaming Zone");
            if (SceneView.lastActiveSceneView != null)
                zone.transform.position = SceneView.lastActiveSceneView.pivot;
            // Selected chunks become the zone's chunks.
            foreach (var chunk in Selection.GetFiltered<LevelChunk>(SelectionMode.Editable))
                zone.GetComponent<LevelStreamingZone>().Chunks.Add(chunk);
            Selection.activeGameObject = zone;
        }
    }

    [CustomEditor(typeof(LevelStreamingZone))]
    public class LevelStreamingZoneEditor : UnityEditor.Editor
    {
        private readonly BoxBoundsHandle _handle = new BoxBoundsHandle();

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var zone = (LevelStreamingZone)target;
            if (zone.Levels == null || zone.Levels.Count == 0)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Levels", EditorStyles.boldLabel);
            foreach (var level in zone.Levels)
            {
                if (level == null || !level.IsValid)
                    continue;
                if (Application.isPlaying)
                {
                    // Where each level is right now: loading, streaming in (with progress), loaded, or not loaded.
                    var handle = LevelLoader.Find(level);
                    var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                    if (handle == null)
                        EditorGUI.LabelField(rect, level.Name, "Not loaded");
                    else
                        EditorGUI.ProgressBar(rect, handle.Progress, $"{level.Name}: {handle.State}");
                }
                else if (level.Kind == LevelReference.LevelKind.Scene)
                {
                    string path = level.ResolveScenePath();
                    if (LevelReference.IsPath(path) && UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(path) < 0)
                        EditorGUILayout.HelpBox($"'{level.Name}' isn't in the Build Profile's scene list: it loads in the editor, but a build can't load it.", MessageType.Warning);
                }
            }
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        private void OnSceneGUI()
        {
            var zone = (LevelStreamingZone)target;
            using (new Handles.DrawingScope(new Color(1f, 0.85f, 0.2f), zone.transform.localToWorldMatrix))
            {
                _handle.center = zone.Center;
                _handle.size = zone.Size;
                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(zone, "Resize Zone");
                    zone.Center = _handle.center;
                    zone.Size = _handle.size;
                }
            }
        }
    }
}
