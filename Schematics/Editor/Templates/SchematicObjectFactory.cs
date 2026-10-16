using System.IO;
using BlueGraph.Editor;
using SchematicAssets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// Makes Schematic Objects without the drag-to-Project dance: a prefab with a <see cref="SchematicObject"/> on its
    /// root and its own <see cref="SchematicGraph"/>, a copy or variant of a template (with its own copy of the
    /// graph, so editing it never changes the template), or an instance of a template placed in the open scene.
    /// </summary>
    /// <remarks>
    /// Graphs live where the drag-to-Project flow puts them (<c>Assets/_Schematics/&lt;prefab GUID&gt;/SchematicGraph.asset</c>),
    /// so everything else in the toolkit finds them the same way, and renaming or moving the prefab never breaks the link.
    /// </remarks>
    public static class SchematicObjectFactory
    {
        /// <summary>Where generated templates and the default catalog go.</summary>
        public const string TemplatesFolder = "Assets/Content/Schematics/Templates";

        /// <summary>Where new prefabs from the "+" tile go by default.</summary>
        public const string CustomTemplatesFolder = TemplatesFolder + "/Custom";

        // Mirrors SchematicAssetManager's base folder (private there).
        private const string GraphsFolder = "Assets/_Schematics";
        private const string GraphFileName = "SchematicGraph";

        #region Prefabs

        /// <summary>
        /// Saves a GameObject (a temporary one, or one in a scene) as a Schematic prefab at <paramref name="prefabPath"/>:
        /// adds a Schematic Object if it has none, saves it, and gives the prefab its own graph and Object ID.
        /// Overwriting an existing prefab keeps its GUID, and so its graph.
        /// </summary>
        /// <param name="connect">Turn <paramref name="root"/> into an instance of the new prefab (for scene objects).</param>
        public static GameObject SaveAsSchematicPrefab(GameObject root, string prefabPath, bool connect = false)
        {
            if (root == null || string.IsNullOrEmpty(prefabPath))
                return null;

            EnsureFolder(Path.GetDirectoryName(prefabPath)?.Replace('\\', '/'));

            if (root.GetComponent<SchematicObject>() == null)
            {
                if (connect) Undo.AddComponent<SchematicObject>(root);
                else root.AddComponent<SchematicObject>();
            }

            var prefab = connect
                ? PrefabUtility.SaveAsPrefabAssetAndConnect(root, prefabPath, InteractionMode.UserAction)
                : PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Schematics] Couldn't save a prefab at {prefabPath}.");
                return null;
            }
            return EnsureOwnGraph(prefab);
        }

        /// <summary>A new prefab with nothing but a Schematic Object (and its graph): the "+ New Empty Template" tile.</summary>
        public static GameObject CreateEmptySchematicPrefab(string prefabPath)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                root.AddComponent<SchematicObject>();
                return SaveAsSchematicPrefab(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Makes sure a Schematic prefab has a graph of its own: creates one if it has none, and duplicates it if the
        /// one it points at belongs to another prefab (it was copied, or made from an instance of something else).
        /// Also keeps the graph's prefab link and the prefab's Object ID right. Returns the prefab.
        /// </summary>
        public static GameObject EnsureOwnGraph(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            var schematic = prefab != null ? prefab.GetComponent<SchematicObject>() : null;
            if (schematic == null || string.IsNullOrEmpty(path))
                return prefab;

            var graph = schematic.SchematicGraph;
            bool ownedByOther = graph != null && graph.Prefab != null && graph.Prefab != prefab;
            if (graph == null)
                graph = SchematicAssetManager.Create<SchematicGraph>(prefab, "", "", GraphFileName);
            else if (ownedByOther)
                graph = DuplicateGraph(graph, prefab);

            if (graph != schematic.SchematicGraph)
            {
                RemapGraphOverrides(schematic, schematic.SchematicGraph, graph);
                schematic.Assign(graph);
            }
            LinkGraph(graph, prefab);
            schematic.AssignObjectID(SchematicObjectIDs.For(path));
            EditorUtility.SetDirty(schematic);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
            return prefab;
        }

        /// <summary>
        /// A copy of <paramref name="source"/> filed under <paramref name="newOwner"/>'s graph folder (or the graph
        /// already there). Copies get their own Schematic id automatically (see SchematicUids).
        /// </summary>
        public static SchematicGraph DuplicateGraph(SchematicGraph source, GameObject newOwner)
        {
            string ownerGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(newOwner));
            string folder = $"{GraphsFolder}/{ownerGuid}";
            string target = $"{folder}/{GraphFileName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<SchematicGraph>(target);
            if (existing != null && existing != source)
                return existing;

            EnsureFolder(folder);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, target))
            {
                Debug.LogWarning($"[Schematics] Couldn't copy the graph {sourcePath}; {newOwner.name} gets an empty one instead.", newOwner);
                return SchematicAssetManager.Create<SchematicGraph>(newOwner, "", "", GraphFileName);
            }
            var copy = AssetDatabase.LoadAssetAtPath<SchematicGraph>(target);
            LinkGraph(copy, newOwner);
            return copy;
        }

        private static void LinkGraph(SchematicGraph graph, GameObject prefab)
        {
            if (graph == null) return;
            graph.Prefab = prefab;
            graph.PrefabGUID = AssetDatabase.GUIDFromAssetPath(AssetDatabase.GetAssetPath(prefab));
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssetIfDirty(graph);
        }

        // Starting values the object keeps for its graph's Variables follow it to the copy.
        private static void RemapGraphOverrides(SchematicObject schematic, SchematicGraph from, SchematicGraph to)
        {
            if (from == null || to == null || from == to) return;
            foreach (var entry in schematic.GraphOverrides)
                if (entry != null && entry.Graph == from)
                    entry.Graph = to;
        }

        #endregion

        #region From templates

        /// <summary>
        /// A new prefab in <paramref name="folder"/> made from a template: a copy (independent) or a Prefab Variant
        /// (follows changes to the template's objects), with its own copy of the template's graph either way.
        /// </summary>
        /// <param name="startRename">Select it in the Project window and start renaming it, like Unity's own Create menu.</param>
        public static GameObject CreatePrefabFromTemplate(SchematicTemplate template, string folder, bool asVariant = false, bool startRename = true)
        {
            if (template?.Prefab == null)
                return null;
            folder = string.IsNullOrEmpty(folder) ? "Assets" : folder.TrimEnd('/');
            EnsureFolder(folder);

            string templatePath = AssetDatabase.GetAssetPath(template.Prefab);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeFileName(template.DisplayName)}.prefab");
            var sourceGraph = GraphOf(template.Prefab);

            GameObject prefab;
            if (asVariant)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(template.Prefab);
                try { prefab = PrefabUtility.SaveAsPrefabAsset(instance, path); }
                finally { Object.DestroyImmediate(instance); }
            }
            else
            {
                if (!AssetDatabase.CopyAsset(templatePath, path))
                {
                    Debug.LogError($"[Schematics] Couldn't copy {templatePath} to {path}.");
                    return null;
                }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            if (prefab == null)
                return null;

            // Its own graph: a copy of the template's (or a fresh one if the template has none).
            var graph = sourceGraph != null ? DuplicateGraph(sourceGraph, prefab) : null;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                var schematic = scope.prefabContentsRoot.GetComponent<SchematicObject>();
                if (schematic == null)
                    schematic = scope.prefabContentsRoot.AddComponent<SchematicObject>();
                if (graph != null)
                {
                    RemapGraphOverrides(schematic, sourceGraph, graph);
                    schematic.Assign(graph);
                }
                schematic.AssignObjectID(SchematicObjectIDs.For(path));
            }
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            EnsureOwnGraph(prefab);
            AssetDatabase.SaveAssets();

            if (startRename)
                StartRenaming(prefab);
            return prefab;
        }

        /// <summary>
        /// Places an instance of a template in the open scene (or the prefab being edited): at <paramref name="worldPoint"/>
        /// (sitting on it), under <paramref name="parent"/> (aligned to it), or at the Scene view's pivot. Selected and undoable.
        /// </summary>
        public static GameObject InstantiateInScene(SchematicTemplate template, Transform parent = null, Vector3? worldPoint = null)
        {
            if (template?.Prefab == null)
                return null;

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (parent == null && stage != null)
                parent = stage.prefabContentsRoot.transform;

            Scene scene = parent != null ? parent.gameObject.scene
                : stage != null ? stage.scene
                : SceneManager.GetActiveScene();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(template.Prefab, scene);
            if (instance == null)
                return null;

            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(instance, parent.gameObject);
                if (worldPoint.HasValue && stage != null && parent == stage.prefabContentsRoot.transform)
                    PlaceOn(instance, worldPoint.Value);
            }
            else if (worldPoint.HasValue)
                PlaceOn(instance, worldPoint.Value);
            else if (SceneView.lastActiveSceneView != null)
                PlaceOn(instance, SceneView.lastActiveSceneView.pivot);

            instance.name = GameObjectUtility.GetUniqueNameForSibling(instance.transform.parent, template.DisplayName);
            Undo.RegisterCreatedObjectUndo(instance, $"Create {template.DisplayName}");
            Selection.activeGameObject = instance;
            return instance;
        }

        // Puts the object on a point, lifting it so its lowest renderer sits on it rather than in it.
        private static void PlaceOn(GameObject instance, Vector3 point)
        {
            instance.transform.position = point;
            if (TryGetRendererBounds(instance, out var bounds) && bounds.min.y < point.y)
                instance.transform.position += Vector3.up * (point.y - bounds.min.y);
        }

        /// <summary>The bounds of every renderer under <paramref name="root"/>. False when it has none.</summary>
        public static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        /// <summary>
        /// Turns a scene object into a template prefab at <paramref name="prefabPath"/> (the scene object becomes an
        /// instance of it) and lists it in <paramref name="catalog"/> (the default catalog when null).
        /// </summary>
        public static SchematicTemplate CreateTemplateFromSceneObject(GameObject sceneObject, string prefabPath, string category = "Custom", SchematicTemplatesData catalog = null)
        {
            if (sceneObject == null || EditorUtility.IsPersistent(sceneObject))
                return null;
            var prefab = SaveAsSchematicPrefab(sceneObject, prefabPath, connect: true);
            return prefab != null ? SchematicTemplates.Add(prefab, null, category, null, null, catalog) : null;
        }

        /// <summary>A new empty Schematic prefab at <paramref name="prefabPath"/>, listed in <paramref name="catalog"/>.</summary>
        public static SchematicTemplate CreateEmptyTemplate(string prefabPath, string category = "Custom", SchematicTemplatesData catalog = null)
        {
            var prefab = CreateEmptySchematicPrefab(prefabPath);
            return prefab != null ? SchematicTemplates.Add(prefab, null, category, null, null, catalog) : null;
        }

        /// <summary>Opens a prefab's Schematic Graph in the graph editor.</summary>
        public static void OpenGraph(GameObject prefab)
        {
            var graph = GraphOf(prefab);
            if (graph != null)
                GraphAssetHandler.OnOpenGraph(graph);
        }

        #endregion

        #region Helpers

        /// <summary>The graph a prefab's root Schematic Object runs, or null.</summary>
        public static SchematicGraph GraphOf(GameObject prefab)
        {
            if (prefab == null || !prefab.TryGetComponent(out SchematicObject schematic))
                return null;
            return schematic.SchematicGraph;
        }

        /// <summary>The folder selected in the Project window (or the folder of the selected asset); "Assets" otherwise.</summary>
        public static string SelectedProjectFolder()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) return path;
                var directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(directory)) return directory;
            }
            return "Assets";
        }

        /// <summary>Creates every missing folder along a project path ("Assets/...").</summary>
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            int slash = folder.LastIndexOf('/');
            if (slash <= 0)
                return;
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        /// <summary>Selects an asset in the Project window and starts renaming it.</summary>
        public static void StartRenaming(Object asset)
        {
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorUtility.FocusProjectWindow();
            EditorGUIUtility.PingObject(asset);
            // Twice deferred: the Project window has to show the selection before it can rename it.
            EditorApplication.delayCall += () => EditorApplication.delayCall += () =>
            {
                if (Selection.activeObject == asset)
                    EditorApplication.ExecuteMenuItem("Assets/Rename");
            };
        }

        internal static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "New Schematic Object";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        #endregion
    }
}
