using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

[FieldRendererTarget(typeof(IMGUIContainerRendererAttribute))]
public class IMGUIContainerRenderer : FieldRenderer
{
    public IMGUIContainerRenderer(SchematicGraphEditorWindow window, UnityEngine.Object obj, object parent, object target, FieldOrPropertyInfo fieldInfo, CustomFieldRendererAttribute attr, string path, List<Action> onModified = null) : base(window, obj, parent, target, fieldInfo, attr, path, onModified)
    {
    }

    public string FieldPath { get; private set; }

    protected override void Redraw()
    {
        // The object's one long-lived SerializedObject (see DockSerializedObjects), not a new one per redraw: Unity's own
        // drawers (the Input Action's bindings popup) keep its properties between frames, and a SerializedObject that's
        // replaced or disposed under them fails with "SerializedProperty ... has disappeared!".
        FieldPath = _path + "." + _field.Name;

        var imguiContainerContainer = new VisualElement();

        var imguiContainer = new IMGUIContainer()
        {
            style =
            {
                flexGrow = 0,
                flexShrink = 0
            }
        };

        imguiContainer.onGUIHandler = () =>
        {
            // Found again every frame, so it's never one left over from before the list or object changed. (A list item
            // just added, like a new input, is picked up even if the dock's SerializedObject hadn't seen it yet.)
            var property = DockSerializedObjects.FindProperty(_object, FieldPath);
            if (property == null) return;
            var seriializedObject = property.serializedObject;

            EditorGUILayout.PropertyField(property);

            // Ensure the value of the property is not null
            if (property.boxedValue == null)
                property.boxedValue = default;

            seriializedObject.ApplyModifiedProperties();

            EditorApplication.QueuePlayerLoopUpdate();
            imguiContainer.MarkDirtyRepaint();
        };

        EditorApplication.update += () =>
        {
            if (imguiContainer == null) return;
            imguiContainer.MarkDirtyRepaint();
        }; 

        imguiContainer.focusable = true;
        imguiContainer.tabIndex = 0;

        Add(imguiContainer);
    }
}