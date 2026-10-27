using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Remedy.Meshes
{
    /// <summary>
    /// Mesh Graphs in the editor: making them (an Output with a Box wired in), a live 3D preview in the corner of the
    /// graph window (remade in the background as you edit, with each LOD and its triangle count), and baking the model
    /// to assets (meshes and a prefab with its LOD Group).
    /// </summary>
    [InitializeOnLoad]
    internal static class MeshGraphEditor
    {
        static MeshGraphEditor()
        {
            // Mesh Graph nodes only show in Mesh Graphs.
            DefaultSearchProvider.ExclusiveTags.Add(MeshNode.Tag);
            DefaultSearchProvider.ExclusiveTags.Add(MeshGraph.OutputOnlyTag);

            SchematicGraphEditorWindow.CanvasLoaded += (window, canvas) =>
            {
                canvas.Q("mesh-graph-preview")?.RemoveFromHierarchy();
                if (window.SchematicGraph is MeshGraph graph)
                    canvas.Add(new MeshPreviewPanel(graph));
            };

            // An edit to a Mesh Graph: a new version, so renderers in the scene and the preview make it again.
            var counts = new Dictionary<MeshGraph, int>();
            double next = 0;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup < next)
                    return;
                next = EditorApplication.timeSinceStartup + 0.25;
                foreach (var graph in Resources.FindObjectsOfTypeAll<MeshGraph>())
                {
                    int dirty = EditorUtility.GetDirtyCount(graph);
                    if (counts.TryGetValue(graph, out int seen) && seen != dirty)
                    {
                        graph.Touch();
                        MeshGraphGenerator.Invalidate(graph);
                        EditorApplication.QueuePlayerLoopUpdate();
                    }
                    counts[graph] = dirty;
                }
            };
        }

        #region Making

        [MenuItem("Assets/Create/Schematics/Mesh Graph", priority = 83)]
        private static void Create()
        {
            string folder = "Assets";
            var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(selected))
                folder = AssetDatabase.IsValidFolder(selected) ? selected : Path.GetDirectoryName(selected).Replace('\\', '/');
            var graph = CreateIn(folder);
            Selection.activeObject = graph;
            EditorGUIUtility.PingObject(graph);
        }

        /// <summary>A new Mesh Graph in a folder, with a Box wired into its Output. How the Project Manager makes them.</summary>
        [SchematicTypeCreator(typeof(MeshGraph))]
        public static MeshGraph CreateIn(string folder)
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New Mesh Graph.asset");
            var graph = ScriptableObject.CreateInstance<MeshGraph>();
            AssetDatabase.CreateAsset(graph, path);

            var output = NewNode<MeshOutputNode>("Output", new Vector2(500, 0));
            var box = NewNode<MeshBoxNode>("Box", new Vector2(150, 0));
            if (output != null && box != null)
            {
                graph.AddNode(box);
                graph.AddNode(output);
                var from = box.GetPort(MeshNode.MeshPort);
                var to = output.GetPort(MeshNode.MeshPort);
                if (from != null && to != null)
                    graph.AddEdge(from, to);
            }
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            return graph;
        }

        private static T NewNode<T>(string name, Vector2 position) where T : Node
        {
            var node = NodeReflection.GetNodeType(typeof(T))?.CreateInstance() as T;
            if (node == null)
                return null;
            node.Name = name;
            node.Position = position;
            return node;
        }

        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        #endregion

        #region Baking

        /// <summary>
        /// Saves a Mesh Graph's model (with its default Parameters) as assets next to it: its meshes (LOD0 and the
        /// LODs) in one asset, and a prefab showing them with a LOD Group. Returns the prefab.
        /// </summary>
        public static async Task<GameObject> Bake(MeshGraph graph)
        {
            var generated = await MeshGraphGenerator.Generate(graph);
            if (generated == null || generated.Levels.Length == 0)
                return null;
            try
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(graph)).Replace('\\', '/');
                string meshPath = $"{folder}/{graph.name} Meshes.asset";
                string prefabPath = $"{folder}/{graph.name}.prefab";

                // The meshes: LOD0 as the asset, the rest inside it. Remade in place, so prefabs using them update.
                var existing = AssetDatabase.LoadAllAssetsAtPath(meshPath);
                var meshes = new Mesh[generated.Levels.Length];
                for (int i = 0; i < meshes.Length; i++)
                {
                    string name = $"{graph.name} LOD{i}";
                    Mesh mesh = null;
                    foreach (var asset in existing)
                        if (asset is Mesh m && m.name == name)
                            mesh = m;
                    if (mesh == null)
                    {
                        mesh = new Mesh { name = name };
                        if (i == 0 && existing.Length == 0)
                            AssetDatabase.CreateAsset(mesh, meshPath);
                        else
                            AssetDatabase.AddObjectToAsset(mesh, meshPath);
                    }
                    generated.Data[i].WriteTo(mesh);
                    mesh.name = name;
                    EditorUtility.SetDirty(mesh);
                    meshes[i] = mesh;
                }
                AssetDatabase.SaveAssets();

                // The prefab: LOD0 on the root, the others as children, and the LOD Group.
                var root = new GameObject(graph.name);
                try
                {
                    var material = DefaultMaterial();
                    var renderers = new Renderer[meshes.Length];
                    for (int i = 0; i < meshes.Length; i++)
                    {
                        var holder = i == 0 ? root : new GameObject($"LOD{i}");
                        if (i > 0)
                            holder.transform.SetParent(root.transform, false);
                        holder.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                        var renderer = holder.AddComponent<MeshRenderer>();
                        var materials = new Material[Mathf.Max(1, meshes[i].subMeshCount)];
                        for (int m = 0; m < materials.Length; m++)
                            materials[m] = material;
                        renderer.sharedMaterials = materials;
                        renderers[i] = renderer;
                    }
                    if (meshes.Length > 1 || generated.ScreenHeights[generated.ScreenHeights.Length - 1] > 0f)
                    {
                        var group = root.AddComponent<LODGroup>();
                        var heights = generated.ScreenHeights;
                        var lods = new LOD[meshes.Length];
                        for (int i = 0; i < lods.Length; i++)
                        {
                            float height = i < lods.Length - 1 ? heights[Mathf.Min(i, heights.Length - 1)] : heights[heights.Length - 1];
                            if (i > 0) height = Mathf.Min(height, lods[i - 1].screenRelativeTransitionHeight - 0.001f);
                            lods[i] = new LOD(Mathf.Max(0f, height), new[] { renderers[i] });
                        }
                        group.SetLODs(lods);
                        group.RecalculateBounds();
                    }
                    // An existing prefab keeps its own materials and anything added to it.
                    var old = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (old != null)
                        return UpdatePrefab(prefabPath, meshes, root);
                    return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }
            finally
            {
                MeshGraphGenerator.Release(generated);
            }
        }

        // The meshes are updated in place (same assets), so an existing prefab already shows the new model; only its
        // LOD Group needs the new level count.
        private static GameObject UpdatePrefab(string prefabPath, Mesh[] meshes, GameObject fresh)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var oldGroup = contents.GetComponent<LODGroup>();
                var freshGroup = fresh.GetComponent<LODGroup>();
                int levels = oldGroup != null ? oldGroup.lodCount : 1;
                if (levels != meshes.Length)
                {
                    // Rebuilt from scratch: the level count changed.
                    PrefabUtility.UnloadPrefabContents(contents);
                    contents = null;
                    return PrefabUtility.SaveAsPrefabAsset(fresh, prefabPath);
                }
                if (oldGroup != null && freshGroup != null)
                {
                    var lods = oldGroup.GetLODs();
                    var freshLods = freshGroup.GetLODs();
                    for (int i = 0; i < lods.Length && i < freshLods.Length; i++)
                        lods[i].screenRelativeTransitionHeight = freshLods[i].screenRelativeTransitionHeight;
                    oldGroup.SetLODs(lods);
                    oldGroup.RecalculateBounds();
                }
                return PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                if (contents != null)
                    PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>The render pipeline's default material (or the built-in one).</summary>
        internal static Material DefaultMaterial()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            var material = pipeline != null ? pipeline.defaultMaterial : null;
            return material != null ? material : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        }

        #endregion
    }
}
