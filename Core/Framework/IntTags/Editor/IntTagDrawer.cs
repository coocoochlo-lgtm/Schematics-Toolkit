using UnityEditor;
using UnityEngine;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// Draws an <see cref="IntTag"/> as a dropdown of tag names (with "Add Tag..." and "Manage Tags..."), the way
    /// Unity draws its own Tag field, while the saved value stays a plain int.
    /// </summary>
    [CustomPropertyDrawer(typeof(IntTag))]
    public sealed class IntTagDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueProperty = property.FindPropertyRelative(nameof(IntTag.Value));
            if (valueProperty == null)
            {
                EditorGUI.LabelField(position, label.text, "IntTag has no Value field");
                return;
            }

            label = EditorGUI.BeginProperty(position, label, property);
            var fieldRect = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Keyboard), label);

            bool mixed = valueProperty.hasMultipleDifferentValues;
            int current = valueProperty.intValue;
            var content = new GUIContent(mixed ? "—" : IntTags.NameOf(current), mixed ? "Mixed values" : $"Int Tag id {current}");

            var previousColor = GUI.color;
            if (!mixed && !IntTags.Exists(current))
                GUI.color = new Color(1f, 0.6f, 0.6f);

            if (EditorGUI.DropdownButton(fieldRect, content, FocusType.Keyboard))
            {
                // The menu answers later, after this SerializedObject may be gone: write through a fresh one.
                var targets = property.serializedObject.targetObjects;
                var path = valueProperty.propertyPath;
                IntTagMenu.Show(fieldRect, mixed ? int.MinValue : current, id => Assign(targets, path, id));
            }

            GUI.color = previousColor;
            EditorGUI.EndProperty();
        }

        private static void Assign(Object[] targets, string path, int id)
        {
            if (targets == null || targets.Length == 0)
                return;
            foreach (var target in targets)
                if (target == null)
                    return;

            var serializedObject = new SerializedObject(targets);
            var property = serializedObject.FindProperty(path);
            if (property == null)
                return;
            property.intValue = id;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
