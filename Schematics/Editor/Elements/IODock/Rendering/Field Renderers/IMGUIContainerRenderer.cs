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

    // The property wasn't in the data yet last time it drew.
    private bool _waiting;
    private double _waitingSince;
    private bool _reportedMissing;

    // TEMPORARY diagnostics: the longest start of the path the object's SerializedObject has, and what's under it.
    private string DescribeMissing()
    {
        var serializedObject = DockSerializedObjects.Get(_object);
        if (serializedObject == null)
            return "It has no SerializedObject (the object is gone).";
        string found = "", next = null;
        var parts = FieldPath.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            string attempt = string.Join(".", parts, 0, i + 1);
            // ("Array" and "data[n]" only exist together.)
            if (parts[i] == "Array")
                continue;
            if (serializedObject.FindProperty(attempt) == null)
            {
                next = attempt;
                break;
            }
            found = attempt;
        }
        var children = new List<string>();
        var parent = string.IsNullOrEmpty(found) ? null : serializedObject.FindProperty(found);
        var iterator = parent != null ? parent.Copy() : serializedObject.GetIterator();
        var end = parent?.GetEndProperty();
        if (iterator.NextVisible(true))
            do
            {
                if (end != null && SerializedProperty.EqualContents(iterator, end))
                    break;
                children.Add(iterator.propertyPath + (iterator.isArray ? $"[{iterator.arraySize}]" : ""));
            } while (children.Count < 30 && iterator.NextVisible(false));
        return $"It has \"{found}\" but not \"{next}\". Under it: {string.Join(", ", children)}";
    }

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
            if (property == null)
            {
                // Not there yet (an input just added to the list, not yet in the data): look again next frame. It used
                // to give up here and, never redrawn, stayed blank.
                if (!_waiting)
                    _waitingSince = EditorApplication.timeSinceStartup;
                _waiting = true;
                // TEMPORARY (Trace Input Asset Writes): still missing after a second, say what's missing.
                if (!_reportedMissing && InputAssetWriteTrace.Enabled && EditorApplication.timeSinceStartup - _waitingSince > 1.0)
                {
                    _reportedMissing = true;
                    Debug.Log($"[InputTrace] A dock field can't find its data: \"{FieldPath}\" on {InputAssetWriteTrace.Describe(_object)}. {DescribeMissing()}");
                }
                return;
            }
            _waiting = false;
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
            bool applied = seriializedObject.ApplyModifiedProperties();
            // TEMPORARY (Trace Input Asset Writes): an edit made here that didn't get saved, and where it should have gone.
            if (changed && InputAssetWriteTrace.Enabled)
                Debug.Log($"[InputTrace] Dock field edited: \"{FieldPath}\" applied: {applied}; SerializedObject on "
                    + $"{InputAssetWriteTrace.Describe(seriializedObject.targetObject)}, dock object {InputAssetWriteTrace.Describe(_object)}");
            if (applied || changed)
            {
                // Only after an edit: it used to ask for a player loop update and a redraw on every redraw (so it
                // never stopped redrawing), and every editor frame on top of that, for every field in the dock.
                EditorApplication.QueuePlayerLoopUpdate();
                imguiContainer.MarkDirtyRepaint();
            }
            // Values that change by themselves (in Play Mode) are redrawn at the Inspector's Refresh Rate.
            InspectorRefresh.Request(imguiContainer);
        };

        // Redrawn every editor frame while it needs it: while it's waiting for its data, while the mouse is over it or it
        // has focus (Unity's Input Action editor, a tree with popups, needs every frame to respond), and when an edit
        // made from one of its popups is waiting to be saved (only a redraw saves it). Otherwise at the Refresh Rate.
        bool hovered = false;
        imguiContainer.RegisterCallback<PointerEnterEvent>(_ => hovered = true);
        imguiContainer.RegisterCallback<PointerLeaveEvent>(_ => hovered = false);
        void Watch()
        {
            if (imguiContainer.panel == null)
                return;
            bool focused = imguiContainer.focusController?.focusedElement == imguiContainer;
            bool pending = DockSerializedObjects.HasPendingChanges(_object);
            if (_waiting || hovered || focused || pending)
                imguiContainer.MarkDirtyRepaint();
        }
        imguiContainer.RegisterCallback<AttachToPanelEvent>(_ => { EditorApplication.update -= Watch; EditorApplication.update += Watch; });
        imguiContainer.RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.update -= Watch);

        imguiContainer.focusable = true;
        imguiContainer.tabIndex = 0;

        Add(imguiContainer);
    }
}