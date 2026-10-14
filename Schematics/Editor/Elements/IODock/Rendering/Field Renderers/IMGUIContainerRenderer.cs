using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws a field with Unity's own Inspector drawer (e.g. an Input Action with its bindings) inside the IO Dock.
/// It uses the object's one long-lived SerializedObject (<see cref="DockSerializedObjects"/>) and finds the property
/// again by its path every frame, so drawers that keep properties between frames, or open popups that edit through
/// them (the bindings popup), always see a live one. Nothing rebuilds the dock while it's edited.
/// </summary>
[FieldRendererTarget(typeof(IMGUIContainerRendererAttribute))]
public class IMGUIContainerRenderer : FieldRenderer
{
    private string _propertyPath;

    public IMGUIContainerRenderer(SchematicGraphEditorWindow window, UnityEngine.Object obj, object parent, object target, FieldOrPropertyInfo fieldInfo, CustomFieldRendererAttribute attr, string path, List<Action> onModified = null) : base(window, obj, parent, target, fieldInfo, attr, path, onModified)
    {
    }

    /// <summary>The serialized path of the drawn field.</summary>
    public string FieldPath => _propertyPath;

    protected override void Redraw()
    {
        _propertyPath = ManagedPath.Combine(_path, _field.Name);

        var container = new IMGUIContainer(DrawField)
        {
            style =
            {
                flexGrow = 0,
                flexShrink = 0
            }
        };
        container.focusable = true;
        container.tabIndex = 0;
        Add(container);
    }

    private void DrawField()
    {
        if (_object == null)
            return;
        var serializedObject = DockSerializedObjects.Get(_object);
        if (serializedObject == null)
            return;

        serializedObject.UpdateIfRequiredOrScript();
        var property = serializedObject.FindProperty(_propertyPath);
        if (property == null)
        {
            EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(_field.Name), "(not found)");
            return;
        }

        float labelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Min(labelWidth, 110f);
        EditorGUILayout.PropertyField(property, true);
        EditorGUIUtility.labelWidth = labelWidth;

        // Also catches changes made by the drawer's own popups between frames (they edit this SerializedObject).
        if (serializedObject.hasModifiedProperties)
            serializedObject.ApplyModifiedProperties();
    }
}
