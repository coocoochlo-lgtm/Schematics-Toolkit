using UnityEditor;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// The Mesh Graph Renderer's Inspector: its graph's Parameters as fields (with their defaults until changed), and
    /// what the model came out as.
    /// </summary>
    [CustomEditor(typeof(MeshGraphRenderer))]
    [CanEditMultipleObjects]
    internal sealed class MeshGraphRendererEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MeshGraphRenderer.Graph)));
            var renderer = (MeshGraphRenderer)target;

            if (renderer.Graph != null && !serializedObject.isEditingMultipleObjects)
            {
                var parameters = renderer.Graph.ParameterList;
                if (parameters.Count > 0)
                {
                    EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    foreach (var (name, defaultValue) in parameters)
                    {
                        float value = defaultValue;
                        bool set = false;
                        foreach (var entry in renderer.Parameters)
                            if (string.Equals(entry.Name, name, System.StringComparison.OrdinalIgnoreCase))
                            {
                                value = entry.Value;
                                set = true;
                            }
                        EditorGUI.BeginChangeCheck();
                        var label = new GUIContent(set ? name : $"{name} (default)", $"The graph's \"{name}\" Parameter. Default {defaultValue}.");
                        float changed = EditorGUILayout.FloatField(label, value);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(renderer, "Change Mesh Parameter");
                            renderer.SetParameter(name, changed);
                            EditorUtility.SetDirty(renderer);
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MeshGraphRenderer.Materials)), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MeshGraphRenderer.UseLODs)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MeshGraphRenderer.Shadows)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MeshGraphRenderer.Collider)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(MeshGraphRenderer.Batch)));
            serializedObject.ApplyModifiedProperties();

            var generated = renderer.Generated;
            if (generated != null && !serializedObject.isEditingMultipleObjects)
            {
                var counts = new System.Text.StringBuilder();
                for (int i = 0; i < generated.Data.Count; i++)
                    counts.Append(i == 0 ? "" : "   ").Append($"LOD{i}: {generated.Data[i].TriangleCount:N0} tris");
                EditorGUILayout.HelpBox(counts.ToString(), MessageType.None);
            }
            if (renderer.Graph != null && GUILayout.Button("Open Graph"))
                AssetDatabase.OpenAsset(renderer.Graph);
        }
    }
}
