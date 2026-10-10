using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Adds a "Schematic Behaviors" section with an "Add Schematic Behavior" button under the GameObject header of any
/// object that is (or is a child of) a <see cref="SchematicObject"/>. Unity's own Add Component button can't be
/// replaced, so this sits alongside it.<br/>
/// In Edit Mode it edits the defaults stored on the Schematic Object; in Play Mode it shows the live instances.
/// </summary>
[InitializeOnLoad]
public static class SchematicBehaviorsHeaderGUI
{
    private static readonly AdvancedDropdownState _dropdownState = new();
    private static GUIStyle _revertStyle;

    static SchematicBehaviorsHeaderGUI()
    {
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
    }

    private static void OnHeaderGUI(Editor editor)
    {
        if (editor.targets.Length != 1 || !(editor.target is GameObject gameObject))
            return;

        var root = gameObject.GetComponentInParent<SchematicObject>(true);
        if (root == null)
            return;

        _revertStyle ??= new GUIStyle(EditorStyles.miniButton) { padding = new RectOffset(2, 2, 0, 0) };

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Schematic Behaviors", EditorStyles.boldLabel);
            if (root.gameObject != gameObject)
                EditorGUILayout.LabelField($"on {root.name}", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            if (Application.isPlaying && root.IsSpawned)
                EditorGUILayout.LabelField($"UID {root.UID}", EditorStyles.miniLabel, GUILayout.Width(70));
        }

        root.Behaviors.TryGetValue(gameObject, out var list);
        if (list != null)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (Application.isPlaying)
                    DrawLiveAttachment(root, gameObject, list.Items[i]);
                else
                    DrawAttachment(root, gameObject, list, i);
            }
        }

        if (!Application.isPlaying)
        {
            DrawAddButton(root, gameObject, list);
            DrawMissingTargetsCleanup(root);
        }
        else
        {
            // Live values change every frame.
            editor.Repaint();
        }

        EditorGUILayout.Space(2);
    }

    #region Edit Mode

    private static void DrawAttachment(SchematicObject root, GameObject gameObject, BehaviorAttachmentList list, int index)
    {
        var attachment = list.Items[index];
        var metadata = attachment?.Metadata;
        var behaviorType = metadata != null ? metadata.BehaviorType.Type : null;
        var title = behaviorType != null ? ObjectNames.NicifyVariableName(behaviorType.Name)
                  : metadata != null ? $"{metadata.name} (Missing Script)"
                  : "(Missing Behavior)";

        var foldoutKey = $"SchematicBehavior.{gameObject.GetInstanceID()}.{index}";

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            bool expanded;
            using (new EditorGUILayout.HorizontalScope())
            {
                expanded = EditorGUILayout.Foldout(SessionState.GetBool(foldoutKey, true), title, true, EditorStyles.foldoutHeader);
                SessionState.SetBool(foldoutKey, expanded);

                if (attachment != null)
                {
                    EditorGUI.BeginChangeCheck();
                    var enabled = EditorGUILayout.ToggleLeft(new GUIContent("Enabled", "The instance's Enabled flag when it spawns."), attachment.EnabledByDefault, GUILayout.Width(70));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(root, "Toggle Schematic Behavior");
                        attachment.EnabledByDefault = enabled;
                        Commit(root);
                    }
                }

                if (GUILayout.Button(new GUIContent("✕", "Remove Behavior"), EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    Undo.RecordObject(root, "Remove Schematic Behavior");
                    list.Items.RemoveAt(index);
                    if (list.Items.Count == 0)
                        root.Behaviors.Remove(gameObject);
                    Commit(root);
                    GUIUtility.ExitGUI(); // the list changed under the layout; redraw next frame
                }
            }

            if (expanded && attachment != null && metadata != null)
            {
                EditorGUI.indentLevel++;
                DrawVariables(root, attachment, metadata);
                DrawSignals(metadata);
                EditorGUI.indentLevel--;
            }
        }
    }

    private static void DrawVariables(SchematicObject root, BehaviorAttachment attachment, BehaviorMetadata metadata)
    {
        if (metadata.Variables.Count == 0)
        {
            EditorGUILayout.LabelField("No Variables", EditorStyles.miniLabel);
            return;
        }

        foreach (var definition in metadata.Variables)
        {
            var type = definition.Type.Type;
            var entry = attachment.FindOverride(definition.Name);
            var current = entry != null ? entry.Value.Get(type) : definition.Default?.Get(type);
            var label = new GUIContent(ObjectNames.NicifyVariableName(definition.Name),
                entry != null ? "Overridden on this object." : "Using the Behavior's default.");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (SchematicValueField.Draw(label, type, current, out var newValue))
                {
                    Undo.RecordObject(root, "Set Schematic Variable Default");
                    if (entry == null)
                    {
                        entry = new VariableOverride { Name = definition.Name };
                        attachment.Overrides.Add(entry);
                    }
                    entry.Value.Set(newValue);
                    Commit(root);
                }

                using (new EditorGUI.DisabledScope(entry == null))
                {
                    if (GUILayout.Button(new GUIContent("↺", "Revert to the Behavior's default"), _revertStyle, GUILayout.Width(20)))
                    {
                        Undo.RecordObject(root, "Revert Schematic Variable Default");
                        attachment.Overrides.Remove(entry);
                        Commit(root);
                    }
                }
            }
        }
    }

    private static void DrawSignals(BehaviorMetadata metadata)
    {
        if (metadata.Signals.Count == 0)
            return;

        EditorGUILayout.LabelField(new GUIContent("Signals", "Drag a Signal into a Schematic Graph to send or receive it there."), EditorStyles.miniBoldLabel);
        foreach (var signal in metadata.Signals)
        {
            // The object field doubles as a drag handle for the Signal asset; whatever is dropped on it is ignored.
            EditorGUILayout.ObjectField(new GUIContent(BehaviorMetadataEditor.DescribeSignal(signal)), signal.Asset, typeof(SignalData), false);
        }
    }

    private static void DrawAddButton(SchematicObject root, GameObject gameObject, BehaviorAttachmentList list)
    {
        var content = new GUIContent("Add Schematic Behavior");
        var rect = GUILayoutUtility.GetRect(content, GUI.skin.button);
        const float width = 230f;
        rect.x += Mathf.Max(0, (rect.width - width) * 0.5f);
        rect.width = Mathf.Min(width, rect.width);

        if (!GUI.Button(rect, content))
            return;

        var attached = new HashSet<Type>();
        if (list != null)
            foreach (var attachment in list.Items)
                if (attachment?.Metadata != null && attachment.Metadata.BehaviorType.Type != null)
                    attached.Add(attachment.Metadata.BehaviorType.Type);

        new BehaviorDropdown(_dropdownState, attached, type => AddBehavior(root, gameObject, type)).Show(rect);
    }

    private static void AddBehavior(SchematicObject root, GameObject gameObject, Type behaviorType)
    {
        var metadata = BehaviorMetadataGenerator.GetOrCreate(behaviorType);
        if (metadata == null)
            return;

        Undo.RecordObject(root, "Add Schematic Behavior");
        if (!root.Behaviors.TryGetValue(gameObject, out var list) || list == null)
        {
            list = new BehaviorAttachmentList();
            root.Behaviors[gameObject] = list;
        }
        list.Items.Add(new BehaviorAttachment { Metadata = metadata });
        Commit(root);
    }

    // Entries whose child GameObject was deleted would otherwise linger in the dictionary.
    private static void DrawMissingTargetsCleanup(SchematicObject root)
    {
        int missing = 0;
        foreach (var key in root.Behaviors.Keys)
            if (key == null)
                missing++;

        if (missing == 0)
            return;

        EditorGUILayout.HelpBox($"{missing} Behavior list(s) belong to GameObjects that no longer exist.", MessageType.Warning);
        if (GUILayout.Button("Remove Missing"))
        {
            Undo.RecordObject(root, "Remove Missing Schematic Behaviors");
            var keys = new List<GameObject>(root.Behaviors.Keys);
            foreach (var key in keys)
                if (key == null)
                    root.Behaviors.Remove(key);
            Commit(root);
        }
    }

    private static void Commit(SchematicObject root)
    {
        EditorUtility.SetDirty(root);
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
    }

    #endregion

    #region Play Mode

    private static void DrawLiveAttachment(SchematicObject root, GameObject gameObject, BehaviorAttachment attachment)
    {
        BehaviorInstance instance = null;
        foreach (var candidate in root.BehaviorInstances)
        {
            if (candidate.Attachment == attachment && candidate.GameObject == gameObject)
            {
                instance = candidate;
                break;
            }
        }

        var title = instance != null ? ObjectNames.NicifyVariableName(instance.Behavior.GetType().Name)
                  : attachment?.Metadata != null ? attachment.Metadata.name : "(Missing Behavior)";

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                if (instance != null)
                {
                    using (new EditorGUI.DisabledScope(!instance.IsSpawned))
                    {
                        EditorGUI.BeginChangeCheck();
                        var enabled = EditorGUILayout.ToggleLeft("Enabled", instance.Enabled, GUILayout.Width(70));
                        if (EditorGUI.EndChangeCheck())
                            instance.SetEnabled(enabled);
                    }
                }
            }

            if (instance == null || !instance.IsSpawned)
            {
                EditorGUILayout.LabelField("Not spawned", EditorStyles.miniLabel);
                return;
            }

            EditorGUI.indentLevel++;
            var runtime = instance.Runtime;
            var variables = runtime.Behavior.Variables;
            for (int i = 0; i < variables.Count; i++)
            {
                var label = new GUIContent(ObjectNames.NicifyVariableName(variables[i].Name));
                if (SchematicValueField.Draw(label, variables[i].ValueType, runtime.GetVariableBoxed(instance, i), out var newValue))
                    runtime.SetVariableBoxed(instance, i, newValue);
            }
            EditorGUI.indentLevel--;
        }
    }

    #endregion
}
