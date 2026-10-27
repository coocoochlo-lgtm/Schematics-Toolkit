using System.IO;
using Remedy.Schematics;
using SchematicAssets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.UI.Editor
{
    /// <summary>Creates and opens the prefab, graph and UI Builder document for a new Schematic UI.</summary>
    [InitializeOnLoad]
    internal static class SchematicUIGraphEditor
    {
        static SchematicUIGraphEditor()
        {
            SchematicGraphEditorWindow.CanvasLoaded += (window, _) =>
            {
                if (window.SchematicGraph is SchematicUIGraph { Layout: not null } graph)
                {
                    var layout = graph.Layout;
                    EditorApplication.delayCall += () =>
                    {
                        if (layout != null)
                            AssetDatabase.OpenAsset(layout);
                    };
                }
            };
        }

        [SchematicTypeCreator(typeof(SchematicUIGraph))]
        public static GameObject CreateIn(string folder)
        {
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New UI Schematic.prefab");
            string basePath = prefabPath.Substring(0, prefabPath.Length - ".prefab".Length);
            string uxmlPath = AssetDatabase.GenerateUniqueAssetPath(basePath + ".uxml");
            string panelPath = AssetDatabase.GenerateUniqueAssetPath(basePath + " Panel Settings.asset");

            File.WriteAllText(uxmlPath, "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement name=\"Root\" /></ui:UXML>");
            AssetDatabase.ImportAsset(uxmlPath, ImportAssetOptions.ForceSynchronousImport);
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = Path.GetFileNameWithoutExtension(panelPath);
            AssetDatabase.CreateAsset(panelSettings, panelPath);

            var root = new GameObject("New UI Schematic");
            var document = root.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = layout;
            root.AddComponent<SchematicUI>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null)
                return null;

            var graph = SchematicAssetManager.Create<SchematicUIGraph>(prefab, "", "", "SchematicGraph");
            if (graph == null)
                return null;
            graph.name = "SchematicGraph";
            graph.Layout = layout;
            graph.Prefab = prefab;
            graph.PrefabGUID = AssetDatabase.GUIDFromAssetPath(prefabPath);
            EditorUtility.SetDirty(graph);

            using (var editing = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var schematic = editing.prefabContentsRoot.GetComponent<SchematicUI>();
                schematic.Assign(graph);
                schematic.AssignObjectID(SchematicObjectIDs.For(prefabPath));
                var prefabDocument = editing.prefabContentsRoot.GetComponent<UIDocument>();
                prefabDocument.panelSettings = panelSettings;
                prefabDocument.visualTreeAsset = layout;
                EditorUtility.SetDirty(schematic);
                EditorUtility.SetDirty(prefabDocument);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceSynchronousImport);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            return prefab;
        }
    }
}
