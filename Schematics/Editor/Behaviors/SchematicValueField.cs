using System;
using UnityEditorInternal;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws an Inspector field for a Behavior Variable value of any type <see cref="SchematicValue"/> supports.
/// </summary>
public static class SchematicValueField
{
    /// <returns>True if the user changed the value.</returns>
    public static bool Draw(GUIContent label, Type type, object value, out object newValue)
    {
        newValue = value;

        if (!SchematicValue.IsSupported(type))
        {
            EditorGUILayout.LabelField(label, new GUIContent(value?.ToString() ?? "(set in code)"));
            return false;
        }

        EditorGUI.BeginChangeCheck();

        if (type == typeof(bool)) newValue = EditorGUILayout.Toggle(label, value is bool b && b);
        else if (type == typeof(int)) newValue = EditorGUILayout.IntField(label, value is int i ? i : 0);
        else if (type == typeof(long)) newValue = EditorGUILayout.LongField(label, value is long l ? l : 0);
        else if (type == typeof(float)) newValue = EditorGUILayout.FloatField(label, value is float f ? f : 0f);
        else if (type == typeof(double)) newValue = EditorGUILayout.DoubleField(label, value is double d ? d : 0d);
        else if (type == typeof(string)) newValue = EditorGUILayout.TextField(label, value as string ?? "");
        else if (type.IsEnum)
        {
            var current = value as Enum ?? (Enum)Enum.ToObject(type, 0);
            newValue = type.IsDefined(typeof(FlagsAttribute), false)
                ? EditorGUILayout.EnumFlagsField(label, current)
                : EditorGUILayout.EnumPopup(label, current);
        }
        else if (type == typeof(Vector2)) newValue = EditorGUILayout.Vector2Field(label, value is Vector2 v2 ? v2 : default);
        else if (type == typeof(Vector3)) newValue = EditorGUILayout.Vector3Field(label, value is Vector3 v3 ? v3 : default);
        else if (type == typeof(Vector4)) newValue = EditorGUILayout.Vector4Field(label, value is Vector4 v4 ? v4 : default);
        else if (type == typeof(Vector2Int)) newValue = EditorGUILayout.Vector2IntField(label, value is Vector2Int v2i ? v2i : default);
        else if (type == typeof(Vector3Int)) newValue = EditorGUILayout.Vector3IntField(label, value is Vector3Int v3i ? v3i : default);
        else if (type == typeof(Color)) newValue = EditorGUILayout.ColorField(label, value is Color c ? c : Color.white);
        else if (type == typeof(Quaternion))
        {
            var rotation = value is Quaternion q && q != default ? q : Quaternion.identity;
            newValue = Quaternion.Euler(EditorGUILayout.Vector3Field(label, rotation.eulerAngles));
        }
        else if (type == typeof(LayerMask))
        {
            var mask = value is LayerMask m ? m : default;
            int concatenated = InternalEditorUtility.LayerMaskToConcatenatedLayersMask(mask);
            concatenated = EditorGUILayout.MaskField(label, concatenated, InternalEditorUtility.layers);
            newValue = (LayerMask)InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(concatenated);
        }
        else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            newValue = EditorGUILayout.ObjectField(label, value as UnityEngine.Object, type, true);
        else if (SchematicValue.IsBoxable(type))
        {
            // Drawn by the caller with a PropertyField when it has a SerializedProperty for the value.
            EditorGUILayout.LabelField(label, new GUIContent(ObjectNames.NicifyVariableName(type.Name)));
        }

        return EditorGUI.EndChangeCheck();
    }
}
