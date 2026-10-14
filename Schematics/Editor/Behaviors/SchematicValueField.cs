using System;
using UnityEditorInternal;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws an Inspector field for a Behavior Variable value of any type <see cref="SchematicValue"/> supports.
/// Text and number fields are delayed: the value only changes (and gets saved) when you press Enter or click away,
/// so typing doesn't save the asset on every character, rebuild the list and steal the focus.
/// </summary>
public static class SchematicValueField
{
    private static readonly GUIContent[] _xy = { new("X"), new("Y") };
    private static readonly GUIContent[] _xyz = { new("X"), new("Y"), new("Z") };
    private static readonly GUIContent[] _xyzw = { new("X"), new("Y"), new("Z"), new("W") };

    /// <returns>True if the user changed the value (for text and numbers: once they're done typing).</returns>
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
        else if (type == typeof(int)) newValue = EditorGUILayout.DelayedIntField(label, value is int i ? i : 0);
        else if (type == typeof(long)) newValue = EditorGUILayout.LongField(label, value is long l ? l : 0);
        else if (type == typeof(float)) newValue = EditorGUILayout.DelayedFloatField(label, value is float f ? f : 0f);
        else if (type == typeof(double)) newValue = EditorGUILayout.DelayedDoubleField(label, value is double d ? d : 0d);
        else if (type == typeof(string)) newValue = EditorGUILayout.DelayedTextField(label, value as string ?? "");
        else if (type.IsEnum)
        {
            var current = value as Enum ?? (Enum)Enum.ToObject(type, 0);
            newValue = type.IsDefined(typeof(FlagsAttribute), false)
                ? EditorGUILayout.EnumFlagsField(label, current)
                : EditorGUILayout.EnumPopup(label, current);
        }
        else if (type == typeof(Vector2))
        {
            var v2 = value is Vector2 vector2 ? vector2 : default;
            var parts = DelayedFloats(label, _xy, v2.x, v2.y);
            newValue = new Vector2(parts[0], parts[1]);
        }
        else if (type == typeof(Vector3))
        {
            var v3 = value is Vector3 vector3 ? vector3 : default;
            var parts = DelayedFloats(label, _xyz, v3.x, v3.y, v3.z);
            newValue = new Vector3(parts[0], parts[1], parts[2]);
        }
        else if (type == typeof(Vector4))
        {
            var v4 = value is Vector4 vector4 ? vector4 : default;
            var parts = DelayedFloats(label, _xyzw, v4.x, v4.y, v4.z, v4.w);
            newValue = new Vector4(parts[0], parts[1], parts[2], parts[3]);
        }
        else if (type == typeof(Vector2Int))
        {
            var v2i = value is Vector2Int vector2Int ? vector2Int : default;
            var parts = DelayedInts(label, _xy, v2i.x, v2i.y);
            newValue = new Vector2Int(parts[0], parts[1]);
        }
        else if (type == typeof(Vector3Int))
        {
            var v3i = value is Vector3Int vector3Int ? vector3Int : default;
            var parts = DelayedInts(label, _xyz, v3i.x, v3i.y, v3i.z);
            newValue = new Vector3Int(parts[0], parts[1], parts[2]);
        }
        else if (type == typeof(Color)) newValue = EditorGUILayout.ColorField(label, value is Color c ? c : Color.white);
        else if (type == typeof(Quaternion))
        {
            var rotation = value is Quaternion q && q != default ? q : Quaternion.identity;
            var euler = rotation.eulerAngles;
            EditorGUI.BeginChangeCheck();
            var parts = DelayedFloats(label, _xyz, euler.x, euler.y, euler.z);
            // Only rebuilt from the angles when they were edited, so an untouched rotation keeps its exact value.
            newValue = EditorGUI.EndChangeCheck() ? Quaternion.Euler(parts[0], parts[1], parts[2]) : rotation;
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

    // A row of delayed number fields (Unity has no delayed Vector fields): each commits on Enter or when it loses focus.
    private static float[] DelayedFloats(GUIContent label, GUIContent[] names, params float[] values)
    {
        var rect = Row(label);
        int indent = EditorGUI.indentLevel;
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel = 0;
        EditorGUIUtility.labelWidth = 13f;
        float width = rect.width / values.Length;
        for (int i = 0; i < values.Length; i++)
            values[i] = EditorGUI.DelayedFloatField(new Rect(rect.x + width * i, rect.y, width - 2f, rect.height), names[i], values[i]);
        EditorGUIUtility.labelWidth = labelWidth;
        EditorGUI.indentLevel = indent;
        return values;
    }

    private static int[] DelayedInts(GUIContent label, GUIContent[] names, params int[] values)
    {
        var rect = Row(label);
        int indent = EditorGUI.indentLevel;
        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUI.indentLevel = 0;
        EditorGUIUtility.labelWidth = 13f;
        float width = rect.width / values.Length;
        for (int i = 0; i < values.Length; i++)
            values[i] = EditorGUI.DelayedIntField(new Rect(rect.x + width * i, rect.y, width - 2f, rect.height), names[i], values[i]);
        EditorGUIUtility.labelWidth = labelWidth;
        EditorGUI.indentLevel = indent;
        return values;
    }

    // The field's line, after its label when it has one.
    private static Rect Row(GUIContent label)
    {
        var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
        if (label != null && (!string.IsNullOrEmpty(label.text) || label.image != null))
            rect = EditorGUI.PrefixLabel(rect, label);
        return rect;
    }
}
