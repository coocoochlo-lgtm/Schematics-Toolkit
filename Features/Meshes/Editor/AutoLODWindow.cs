using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Auto LOD: gives any model LODs. Select prefabs or objects in a scene and press Generate: every mesh on them gets
    /// simpler copies (made in the background), saved as assets, shown as LOD1 and LOD2 children under a LOD Group.
    /// Run it again to remake them with new settings.
    /// </summary>
    internal sealed class AutoLODWindow : EditorWindow
    {
        private float _lod1 = 0.5f, _lod2 = 0.2f;
        private float _lod0Until = 0.5f, _lod1Until = 0.2f, _cullBelow = 0.01f;
        private bool _working;
        private string _status = "";

        [MenuItem("Tools/Remedy/Meshes/Auto LOD")]
        private static void Open() => GetWindow<AutoLODWindow>("Auto LOD").minSize = new Vector2(300, 220);

        [MenuItem("GameObject/Remedy/Generate LODs", false, 30)]
        private static void GenerateFromHierarchy()
        {
            var window = GetWindow<AutoLODWindow>("Auto LOD");
            window.GenerateForSelection();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Simpler copies, by share of triangles", EditorStyles.boldLabel);
            _lod1 = EditorGUILayout.Slider(new GUIContent("LOD1 Quality", "LOD1's share of the triangles (0.5: half)."), _lod1, 0.02f, 1f);
            _lod2 = EditorGUILayout.Slider(new GUIContent("LOD2 Quality", "LOD2's share (0: no LOD2)."), _lod2, 0f, 1f);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("When they show (fraction of screen height)", EditorStyles.boldLabel);
            _lod0Until = EditorGUILayout.Slider("LOD0 Until", _lod0Until, 0.01f, 1f);
            _lod1Until = EditorGUILayout.Slider("LOD1 Until", _lod1Until, 0.005f, 1f);
            _cullBelow = EditorGUILayout.Slider(new GUIContent("Cull Below", "Hidden below this (0: never)."), _cullBelow, 0f, 0.2f);
            EditorGUILayout.Space();

            int count = Targets().Count;
            using (new EditorGUI.DisabledScope(_working || count == 0))
                if (GUILayout.Button(count == 0 ? "Select prefabs or scene objects" : $"Generate LODs for {count} object(s)", GUILayout.Height(28)))
                    GenerateForSelection();
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void OnSelectionChange() => Repaint();

        private static List<GameObject> Targets()
            => Selection.gameObjects.Where(g => g != null && g.GetComponentsInChildren<MeshFilter>(true).Length > 0).ToList();

        private async void GenerateForSelection()
        {
            if (_working)
                return;
            var targets = Targets();
            if (targets.Count == 0)
                return;
            _working = true;
            int done = 0;
            try
            {
                foreach (var target in targets)
                {
                    EditorUtility.DisplayProgressBar("Auto LOD", $"Simplifying {target.name}...", done / (float)targets.Count);
                    if (PrefabUtility.IsPartOfPrefabAsset(target))
                    {
                        string path = AssetDatabase.GetAssetPath(target);
                        var contents = PrefabUtility.LoadPrefabContents(path);
                        try
                        {
                            await Generate(contents, Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileNameWithoutExtension(path));
                            PrefabUtility.SaveAsPrefabAsset(contents, path);
                        }
                        finally
                        {
                            PrefabUtility.UnloadPrefabContents(contents);
                        }
                    }
                    else
                    {
                        Undo.RegisterFullObjectHierarchyUndo(target, "Generate LODs");
                        await Generate(target, "Assets/Content/Meshes/LODs", target.name);
                        EditorUtility.SetDirty(target);
                    }
                    done++;
                }
                _status = $"Gave {done} object(s) LODs.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                _working = false;
                Repaint();
            }
        }

        // Every mesh under a root (not already a LOD child) gets its LODs, kept in one asset per root.
        private async Task Generate(GameObject root, string folder, string assetName)
        {
            MeshGraphEditor.EnsureFolder(folder);
            string assetPath = $"{folder}/{assetName} LODs.asset";
            var qualities = _lod2 > 0f ? new[] { _lod1, Mathf.Min(_lod2, _lod1) } : new[] { _lod1 };
            var existing = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Mesh>().ToList();
            bool created = existing.Count > 0;

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null || filter.name.StartsWith("LOD") && filter.transform.parent != null && filter.transform.parent.GetComponent<LODGroup>() != null)
                    continue;
                var source = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null)
                    continue;

                var levels = await AutoLOD.Make(source, qualities);

                // Saved (or updated in place, so everything using them updates).
                var meshes = new Mesh[levels.Length];
                for (int i = 0; i < levels.Length; i++)
                {
                    string name = $"{filter.name} {source.name} LOD{i + 1}";
                    var mesh = existing.FirstOrDefault(m => m.name == name);
                    if (mesh == null)
                    {
                        mesh = levels[i];
                        mesh.name = name;
                        if (!created)
                        {
                            AssetDatabase.CreateAsset(mesh, assetPath);
                            created = true;
                        }
                        else
                            AssetDatabase.AddObjectToAsset(mesh, assetPath);
                        existing.Add(mesh);
                    }
                    else
                    {
                        EditorUtility.CopySerialized(levels[i], mesh);
                        mesh.name = name;
                        DestroyImmediate(levels[i]);
                    }
                    EditorUtility.SetDirty(mesh);
                    meshes[i] = mesh;
                }

                // Children showing them, and the LOD Group.
                var renderers = new List<Renderer> { renderer };
                for (int i = 0; i < meshes.Length; i++)
                {
                    string childName = $"LOD{i + 1}";
                    var child = filter.transform.Find(childName);
                    if (child == null)
                    {
                        child = new GameObject(childName).transform;
                        child.SetParent(filter.transform, false);
                    }
                    var childFilter = child.GetComponent<MeshFilter>();
                    if (childFilter == null) childFilter = child.gameObject.AddComponent<MeshFilter>();
                    childFilter.sharedMesh = meshes[i];
                    var childRenderer = child.GetComponent<MeshRenderer>();
                    if (childRenderer == null) childRenderer = child.gameObject.AddComponent<MeshRenderer>();
                    childRenderer.sharedMaterials = renderer.sharedMaterials;
                    childRenderer.shadowCastingMode = renderer.shadowCastingMode;
                    renderers.Add(childRenderer);
                }
                var group = filter.GetComponent<LODGroup>();
                if (group == null)
                    group = filter.gameObject.AddComponent<LODGroup>();
                var heights = new[] { _lod0Until, _lod1Until, _cullBelow };
                var lods = new LOD[renderers.Count];
                for (int i = 0; i < lods.Length; i++)
                {
                    float height = i < lods.Length - 1 ? heights[Mathf.Min(i, 1)] : _cullBelow;
                    if (i > 0) height = Mathf.Min(height, lods[i - 1].screenRelativeTransitionHeight - 0.001f);
                    lods[i] = new LOD(Mathf.Max(0f, height), new[] { renderers[i] });
                }
                group.SetLODs(lods);
                group.RecalculateBounds();
            }
            AssetDatabase.SaveAssets();
        }
    }
}
