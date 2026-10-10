using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="BehaviorMetadata"/> assets: behavior-wide Variable defaults and the declared Signals.
/// </summary>
[CustomEditor(typeof(BehaviorMetadata))]
public class BehaviorMetadataEditor : Editor
{
    public static string DescribeSignal(SignalDefinition signal)
    {
        var parameters = string.Join(", ", signal.Parameters.Select(p => $"{p.Type.Type?.Name ?? "?"} {p.Name}"));
        var direction = signal.Direction == SignalDirection.Unspecified ? "" : $"  [{signal.Direction}]";
        return $"{ObjectNames.NicifyVariableName(signal.Name)}({parameters}){direction}";
    }

    public override void OnInspectorGUI()
    {
        var metadata = (BehaviorMetadata)target;
        var behaviorType = metadata.BehaviorType.Type;

        EditorGUILayout.LabelField("Behavior", behaviorType != null ? behaviorType.FullName : "(Missing Script)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("Variable Defaults", EditorStyles.boldLabel);
        if (metadata.Variables.Count == 0)
            EditorGUILayout.LabelField("No Variables", EditorStyles.miniLabel);

        foreach (var definition in metadata.Variables)
        {
            var type = definition.Type.Type;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var overridden = EditorGUILayout.Toggle(definition.OverrideDefault, GUILayout.Width(16));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(metadata, "Override Behavior Default");
                    definition.OverrideDefault = overridden;
                    EditorUtility.SetDirty(metadata);
                    if (!overridden)
                        BehaviorMetadataGenerator.GetOrCreate(behaviorType); // restore the code default
                }

                using (new EditorGUI.DisabledScope(!definition.OverrideDefault))
                {
                    var label = new GUIContent(ObjectNames.NicifyVariableName(definition.Name),
                        definition.OverrideDefault ? "Overrides the default in code for every instance." : "Default from code. Tick the box to override it.");
                    if (SchematicValueField.Draw(label, type, definition.Default?.Get(type), out var newValue))
                    {
                        Undo.RecordObject(metadata, "Set Behavior Default");
                        definition.Default ??= new SchematicValue();
                        definition.Default.Set(newValue);
                        EditorUtility.SetDirty(metadata);
                    }
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Signals", EditorStyles.boldLabel);
        if (metadata.Signals.Count == 0)
            EditorGUILayout.LabelField("No Signals", EditorStyles.miniLabel);
        foreach (var signal in metadata.Signals)
            EditorGUILayout.LabelField(DescribeSignal(signal));

        EditorGUILayout.Space();
        if (behaviorType != null && GUILayout.Button("Refresh from Code"))
            BehaviorMetadataGenerator.GetOrCreate(behaviorType);
    }
}
