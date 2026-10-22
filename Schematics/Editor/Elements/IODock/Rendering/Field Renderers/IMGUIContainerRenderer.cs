using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
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

            EditorGUI.BeginChangeCheck();
            try
            {
                EditorGUILayout.PropertyField(property);
            }
            catch (Exception e) when (e is ObjectDisposedException || e is InvalidOperationException || e is NullReferenceException)
            {
                // A drawer (the Input Action's binding tree) held properties that no longer match the data, e.g. a
                // binding row clicked after the bindings changed under it. A fresh SerializedObject makes it rebuild
                // from the data as it is; the click can then be made again.
                DockSerializedObjects.Renew(_object);
                imguiContainer.MarkDirtyRepaint();
                GUIUtility.ExitGUI();
            }

            bool changed = EditorGUI.EndChangeCheck();
            if (seriializedObject.ApplyModifiedProperties() || changed)
            {
                // Only after an edit: it used to ask for a player loop update and a redraw on every redraw (so it
                // never stopped redrawing), and every editor frame on top of that, for every field in the dock.
                EditorApplication.QueuePlayerLoopUpdate();
                imguiContainer.MarkDirtyRepaint();
            }
            // Values that change by themselves (in Play Mode) are redrawn at the Inspector's Refresh Rate.
            InspectorRefresh.Request(imguiContainer);
        };

        imguiContainer.focusable = true;
        imguiContainer.tabIndex = 0;

        Add(imguiContainer);
    }
}