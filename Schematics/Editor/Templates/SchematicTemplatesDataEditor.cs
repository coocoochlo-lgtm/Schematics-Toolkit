using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// The catalog's Inspector: the usual list, plus keeping each prefab's Level Design tag in step with its
    /// template's flag, and a button to open the template chooser.
    /// </summary>
    [CustomEditor(typeof(SchematicTemplatesData))]
    public class SchematicTemplatesDataEditor : Editor
    {
        /// <summary>The menu item that opens the template chooser.</summary>
        public const string TemplateBrowserMenuPath = "Window/Remedy/Template Browser";

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Every Templates Catalog in the project is merged into the template chooser " +
                "(Window > Remedy > Template Browser, or the Schematic Objects plus tile). Ticking Is Level Design also tags the prefab's " +
                "Schematic Object with the Level Design Int Tag.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                SchematicTemplates.SyncLevelDesignTags((SchematicTemplatesData)target);
                SchematicTemplates.Refresh();
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                // The chooser lives in the Browsers assembly, so it's opened by its menu.
                if (GUILayout.Button("Open Template Browser") && !EditorApplication.ExecuteMenuItem(TemplateBrowserMenuPath))
                    CreateSchematicObjectWindow.ShowForScene();
                if (GUILayout.Button("Refresh Thumbnails"))
                    foreach (var template in ((SchematicTemplatesData)target).Templates)
                        if (template?.Prefab != null)
                            SchematicTemplateThumbnails.Invalidate(template.Prefab);
            }
        }
    }
}
