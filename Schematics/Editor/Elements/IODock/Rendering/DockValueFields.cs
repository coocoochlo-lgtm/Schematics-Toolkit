using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Native UI Toolkit fields for the IO Dock's Variable values (numbers, text, toggles, enums, vectors, colours, object
/// references): much cheaper than an IMGUI field per row, which redraws its whole layout every time the dock repaints.
/// Text and numbers are delayed (they change on Enter or when you click away), like the Inspector's. Anything else
/// (rotations, layer masks...) returns null, for the caller's IMGUI fallback.
/// </summary>
internal static class DockValueFields
{
    /// <summary>A field for a value of <paramref name="type"/>, calling <paramref name="changed"/> with each new value; null when there's no native one.</summary>
    public static VisualElement Create(Type type, object value, Action<object> changed)
    {
        if (type == null)
            return null;
        VisualElement field = null;
        if (type == typeof(bool)) field = Bind(new Toggle(), value is bool b && b, changed);
        else if (type == typeof(int)) field = Bind(new IntegerField { isDelayed = true }, value is int i ? i : 0, changed);
        else if (type == typeof(long)) field = Bind(new LongField { isDelayed = true }, value is long l ? l : 0L, changed);
        else if (type == typeof(float)) field = Bind(new FloatField { isDelayed = true }, value is float f ? f : 0f, changed);
        else if (type == typeof(double)) field = Bind(new DoubleField { isDelayed = true }, value is double d ? d : 0d, changed);
        else if (type == typeof(string)) field = Bind(new TextField { isDelayed = true }, value as string ?? "", changed);
        else if (type.IsEnum)
        {
            var current = value as Enum ?? (Enum)Enum.ToObject(type, 0);
            field = type.IsDefined(typeof(FlagsAttribute), false)
                ? Bind(new EnumFlagsField(current), current, changed)
                : Bind(new EnumField(current), current, changed);
        }
        else if (type == typeof(Vector2)) field = Bind(new Vector2Field(), value is Vector2 v2 ? v2 : default, changed);
        else if (type == typeof(Vector3)) field = Bind(new Vector3Field(), value is Vector3 v3 ? v3 : default, changed);
        else if (type == typeof(Vector4)) field = Bind(new Vector4Field(), value is Vector4 v4 ? v4 : default, changed);
        else if (type == typeof(Vector2Int)) field = Bind(new Vector2IntField(), value is Vector2Int v2i ? v2i : default, changed);
        else if (type == typeof(Vector3Int)) field = Bind(new Vector3IntField(), value is Vector3Int v3i ? v3i : default, changed);
        else if (type == typeof(Color)) field = Bind(new ColorField(), value is Color c ? c : Color.white, changed);
        else if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            field = Bind(new ObjectField { objectType = type, allowSceneObjects = true }, value as UnityEngine.Object, changed);
        if (field == null)
            return null;
        field.style.flexGrow = 1;
        field.style.flexShrink = 1;
        field.style.minWidth = 40;
        field.style.marginLeft = field.style.marginRight = 0;
        return field;
    }

    /// <summary>Shows a value without reporting it as a change (after a revert).</summary>
    public static void Show(VisualElement field, object value)
    {
        switch (field)
        {
            case Toggle toggle: toggle.SetValueWithoutNotify(value is bool b && b); break;
            case IntegerField integer: integer.SetValueWithoutNotify(value is int i ? i : 0); break;
            case LongField longField: longField.SetValueWithoutNotify(value is long l ? l : 0L); break;
            case FloatField floatField: floatField.SetValueWithoutNotify(value is float f ? f : 0f); break;
            case DoubleField doubleField: doubleField.SetValueWithoutNotify(value is double d ? d : 0d); break;
            case TextField text: text.SetValueWithoutNotify(value as string ?? ""); break;
            case EnumFlagsField flags when value is Enum flagsValue: flags.SetValueWithoutNotify(flagsValue); break;
            case EnumField enumField when value is Enum enumValue: enumField.SetValueWithoutNotify(enumValue); break;
            case Vector2Field v2: v2.SetValueWithoutNotify(value is Vector2 vector2 ? vector2 : default); break;
            case Vector3Field v3: v3.SetValueWithoutNotify(value is Vector3 vector3 ? vector3 : default); break;
            case Vector4Field v4: v4.SetValueWithoutNotify(value is Vector4 vector4 ? vector4 : default); break;
            case Vector2IntField v2i: v2i.SetValueWithoutNotify(value is Vector2Int vector2Int ? vector2Int : default); break;
            case Vector3IntField v3i: v3i.SetValueWithoutNotify(value is Vector3Int vector3Int ? vector3Int : default); break;
            case ColorField color: color.SetValueWithoutNotify(value is Color c ? c : Color.white); break;
            case ObjectField objectField: objectField.SetValueWithoutNotify(value as UnityEngine.Object); break;
        }
    }

    private static VisualElement Bind<T>(BaseField<T> field, T value, Action<object> changed)
    {
        field.SetValueWithoutNotify(value);
        field.RegisterValueChangedCallback(evt => changed?.Invoke(evt.newValue));
        return field;
    }
}
