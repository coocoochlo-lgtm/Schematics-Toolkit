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

        // A prefab asset (selected in the Project window) is never edited in place: changes to it go through Prefab
        // Mode (or the Schematic Graph's IO Dock), which save it properly.
        bool readOnlyAsset = !Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(root);
        if (readOnlyAsset)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox("Open the prefab to edit its Schematic Behaviors.", MessageType.None);
                if (GUILayout.Button("Open Prefab", GUILayout.Width(90), GUILayout.Height(38)))
                    AssetDatabase.OpenAsset(root.transform.root.gameObject);
            }
        }

        root.Behaviors.TryGetValue(gameObject, out var list);
        using var readOnly = new EditorGUI.DisabledScope(readOnlyAsset);
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

        if (gameObject == root.gameObject)
            using (new EditorGUI.DisabledScope(readOnlyAsset))
                DrawGraphScope(root);

        if (!Application.isPlaying && !readOnlyAsset)
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

        var foldoutKey = $"SchematicBehavior.{gameObject.GetHashCode()}.{index}";

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
                DrawVariables(root, gameObject, index, attachment, metadata);
                DrawSignals(root, gameObject, metadata);
                ExtraAttachmentGUI?.Invoke(root, gameObject, attachment);
                EditorGUI.indentLevel--;
            }
        }
    }

    private static void DrawVariables(SchematicObject root, GameObject gameObject, int attachmentIndex, BehaviorAttachment attachment, BehaviorMetadata metadata)
    {
        if (metadata.Variables.Count == 0)
        {
            EditorGUILayout.LabelField("No Variables", EditorStyles.miniLabel);
            return;
        }

        foreach (var definition in metadata.Variables)
            DrawVariable(root, gameObject, attachmentIndex, attachment, definition);
    }

    private static void DrawVariable(SchematicObject root, GameObject gameObject, int attachmentIndex, BehaviorAttachment attachment, VariableDefinition definition, bool showLabel = true)
    {
        {
            var type = definition.Type.Type;
            var entry = attachment.FindOverride(definition.Name);
            var current = entry != null ? entry.Value.Get(type) : definition.Default?.Get(type);
            var label = new GUIContent(showLabel ? ObjectNames.NicifyVariableName(definition.Name) : "",
                entry != null ? "Overridden on this object." : "Using the Behavior's default.");

            if (SchematicValue.IsBoxable(type))
            {
                DrawBoxedVariable(root, gameObject, attachmentIndex, attachment, definition, entry, label);
                return;
            }

            // As an IO Dock row; embedded single fields (showLabel off) stay plain.
            if (showLabel)
                InspectorRows.Begin(VariableRow(root, gameObject, attachment, definition), ObjectNames.NicifyVariableName(definition.Name),
                    InspectorRows.TypeName(type) + (definition.Networked ? "  ⇅" : ""), entry != null ? "Overridden on this object. Click for details; drag into a graph." : null);
            else
                EditorGUILayout.BeginHorizontal();
            {
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                bool changed = SchematicValueField.Draw(GUIContent.none, type, current, out var newValue);
                EditorGUI.indentLevel = indent;
                if (changed)
                {
                    Undo.RecordObject(root, "Set Schematic Variable Default");
                    if (entry == null)
                    {
                        entry = new VariableOverride { Name = definition.Name };
                        attachment.Overrides.Add(entry);
                    }
                    entry.Value.Set(newValue, type);
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
            if (showLabel) InspectorRows.End();
            else EditorGUILayout.EndHorizontal();
        }
    }

    private static string PathOf(SchematicObject root, GameObject gameObject)
        => gameObject == root.gameObject ? "" : AnimationUtility.CalculateTransformPath(gameObject.transform, root.transform);

    private static InspectorRows.Row VariableRow(SchematicObject root, GameObject gameObject, BehaviorAttachment attachment, VariableDefinition definition) => new()
    {
        Kind = InspectorRows.RowKind.BehaviorVariable,
        Behavior = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null,
        Path = PathOf(root, gameObject),
        Name = definition.Name,
        ValueType = definition.Type.Type,
    };

    // Arrays, lists and serializable classes are drawn with Unity's own property drawers, straight from the
    // Schematic Object's serialized data, so object references and nested lists behave like any other field.
    private static void DrawBoxedVariable(SchematicObject root, GameObject gameObject, int attachmentIndex, BehaviorAttachment attachment,
                                          VariableDefinition definition, VariableOverride entry, GUIContent label)
    {
        var type = definition.Type.Type;

        if (entry == null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, new GUIContent($"Default {ObjectNames.NicifyVariableName(type.Name)}"));
                if (GUILayout.Button(new GUIContent("Override", "Give this object its own value."), EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    Undo.RecordObject(root, "Override Schematic Variable");
                    attachment.SetOverride(definition.Name, definition.Default?.Get(type), type);
                    Commit(root);
                    GUIUtility.ExitGUI();
                }
            }
            return;
        }

        int entryIndex = 0;
        foreach (var key in root.Behaviors.Keys)
        {
            if (key == gameObject) break;
            entryIndex++;
        }
        int overrideIndex = attachment.Overrides.IndexOf(entry);

        var serializedObject = new SerializedObject(root);
        var property = serializedObject.FindProperty(
            $"{nameof(SchematicObject.Behaviors)}.items.Array.data[{entryIndex}].value.{nameof(BehaviorAttachmentList.Items)}.Array.data[{attachmentIndex}]" +
            $".{nameof(BehaviorAttachment.Overrides)}.Array.data[{overrideIndex}].{nameof(VariableOverride.Value)}.{SchematicValue.BoxedValuePath}");

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (property != null)
                    EditorGUILayout.PropertyField(property, label, true);
                else
                    EditorGUILayout.LabelField(label, new GUIContent("(not serializable by Unity)"));
            }

            if (GUILayout.Button(new GUIContent("↺", "Revert to the Behavior's default"), _revertStyle, GUILayout.Width(20)))
            {
                Undo.RecordObject(root, "Revert Schematic Variable Default");
                attachment.Overrides.Remove(entry);
                Commit(root);
                GUIUtility.ExitGUI();
            }
        }

        // Applying rebuilds the Schematic Object's Behavior data, so stop drawing this pass.
        if (serializedObject.ApplyModifiedProperties())
        {
            Commit(root);
            GUIUtility.ExitGUI();
        }
    }

    private static void DrawSignals(SchematicObject root, GameObject gameObject, BehaviorMetadata metadata)
    {
        if (metadata.Signals.Count == 0)
            return;

        EditorGUILayout.LabelField(new GUIContent("Signals", "Drag a Signal into a Schematic Graph to send or receive it there."), EditorStyles.miniBoldLabel);
        foreach (var signal in metadata.Signals)
        {
            var parameters = new List<string>();
            foreach (var parameter in signal.Parameters)
                parameters.Add($"{InspectorRows.TypeName(parameter.Type.Type)} {parameter.Name}");
            InspectorRows.Begin(new InspectorRows.Row
            {
                Kind = InspectorRows.RowKind.Signal, Behavior = metadata.BehaviorType.Type, Path = PathOf(root, gameObject),
                Name = signal.Name, Signal = signal.Asset, Direction = signal.Direction,
            }, ObjectNames.NicifyVariableName(signal.Name), string.Join(", ", parameters));
            InspectorRows.End();
        }
    }

    #region Graph Variables

    // The root's graph: its Variables (this object's starting values, or live values in Play Mode) and its Signals.
    private static void DrawGraphScope(SchematicObject root)
    {
        var graph = root.SchematicGraph;
        if (graph == null || (graph.Variables.Count == 0 && graph.Signals.Count == 0))
            return;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var foldoutKey = $"SchematicGraphScope.{graph.Uid}";
            bool expanded = EditorGUILayout.Foldout(SessionState.GetBool(foldoutKey, true), $"Graph: {graph.name}", true, EditorStyles.foldoutHeader);
            SessionState.SetBool(foldoutKey, expanded);
            if (!expanded)
                return;

            bool live = Application.isPlaying && root.IsSpawned;
            var store = live ? root.GraphVariables(graph) : null;
            if (graph.Variables.Count > 0)
                EditorGUILayout.LabelField(new GUIContent("Variables", live ? "This object's current values." : "This object's starting values."), EditorStyles.miniBoldLabel);
            for (int i = 0; i < graph.Variables.Count; i++)
            {
                var variable = graph.Variables[i];
                var type = variable?.ValueType;
                if (type == null)
                    continue;
                var row = new InspectorRows.Row { Kind = InspectorRows.RowKind.GraphVariable, Graph = graph, VariableId = variable.Id, Name = variable.Name, ValueType = type };
                InspectorRows.Begin(row, variable.Name, InspectorRows.TypeName(type) + (variable.Networked ? "  ⇅" : ""), variable.Description);
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                if (live)
                {
                    int index = store.IndexOf(variable.Id);
                    if (SchematicValueField.Draw(GUIContent.none, type, store.Get(index), out var value))
                        store.Set(index, value);
                }
                else if (SchematicValue.IsBoxable(type))
                    EditorGUILayout.LabelField("(set in the graph)", EditorStyles.miniLabel);
                else
                {
                    var own = root.FindGraphOverride(graph, variable.Id);
                    if (SchematicValueField.Draw(GUIContent.none, type, (own ?? variable.Default)?.Get(type), out var value))
                    {
                        Undo.RecordObject(root, "Set Graph Variable");
                        root.SetGraphOverride(graph, variable.Id, value, type);
                        Commit(root);
                    }
                    using (new EditorGUI.DisabledScope(own == null))
                        if (GUILayout.Button(new GUIContent("↺", "Back to the graph's starting value"), _revertStyle, GUILayout.Width(20)))
                        {
                            Undo.RecordObject(root, "Revert Graph Variable");
                            root.SetGraphOverride(graph, variable.Id, null, null);
                            Commit(root);
                        }
                }
                EditorGUI.indentLevel = indent;
                InspectorRows.End();
            }

            if (graph.Signals.Count > 0)
                EditorGUILayout.LabelField("Signals", EditorStyles.miniBoldLabel);
            foreach (var signal in graph.Signals)
            {
                if (signal?.Asset == null) continue;
                var parameters = new List<string>();
                foreach (var parameter in signal.Parameters)
                    parameters.Add($"{InspectorRows.TypeName(parameter.Type.Type)} {parameter.Name}");
                // Shown as what it is for the graph: an Input (others send it) or an Output (the graph sends it).
                bool output = signal.Direction == GraphSignalDirection.Output;
                InspectorRows.Begin(new InspectorRows.Row
                {
                    Kind = InspectorRows.RowKind.Signal, Graph = graph, Name = signal.Name, Signal = signal.Asset,
                    Direction = output ? SignalDirection.Output : SignalDirection.Input,
                }, signal.Name, (output ? "Output" : "Input") + (parameters.Count > 0 ? " · " + string.Join(", ", parameters) : ""), signal.Description);
                InspectorRows.End();
            }
        }
    }

    #endregion

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

        // [RequireBehavior] dependencies first, skipping the ones already attached here.
        foreach (var requiredBehavior in SchematicBehavior.GetRequiredBehaviors(behaviorType))
        {
            bool attached = false;
            if (root.Behaviors.TryGetValue(gameObject, out var current) && current != null)
                foreach (var present in current.Items)
                    if (present?.Metadata != null && present.Metadata.BehaviorType.Type == requiredBehavior)
                        attached = true;
            if (!attached)
                AddBehavior(root, gameObject, requiredBehavior);
        }

        foreach (var required in SchematicBehavior.GetRequiredComponents(behaviorType))
            if (!gameObject.GetComponent(required))
                Undo.AddComponent(gameObject, required);

        Undo.RecordObject(root, "Add Schematic Behavior");
        if (!root.Behaviors.TryGetValue(gameObject, out var list) || list == null)
        {
            list = new BehaviorAttachmentList();
            root.Behaviors[gameObject] = list;
        }
        var attachment = new BehaviorAttachment { Metadata = metadata };
        list.Items.Add(attachment);

        // Setup may create child objects; register them so undoing the add removes them too.
        var existing = new HashSet<Transform>(gameObject.GetComponentsInChildren<Transform>(true));
        try
        {
            SchematicBehavior.RunSetup(behaviorType, gameObject, attachment);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, gameObject);
        }
        foreach (var child in gameObject.GetComponentsInChildren<Transform>(true))
            if (!existing.Contains(child))
                Undo.RegisterCreatedObjectUndo(child.gameObject, "Add Schematic Behavior");
        Commit(root);
    }

    // Entries whose child GameObject was deleted would otherwise linger in the dictionary.
    #region API for other editors (the Schematic Graph's IO Dock)

    /// <summary>
    /// Extra Inspector GUI drawn under an expanded Behavior, for editors in other assemblies (e.g. the State Machine
    /// Behavior's "Open State Machine" button).
    /// </summary>
    public static event Action<SchematicObject, GameObject, BehaviorAttachment> ExtraAttachmentGUI;

    /// <summary>Shows the "Add Schematic Behavior" dropdown for a GameObject of a Schematic Object.</summary>
    public static void ShowAddBehaviorDropdown(Rect rect, SchematicObject root, GameObject gameObject, Action<Type> onAdded = null)
    {
        var attached = new HashSet<Type>();
        if (root.Behaviors.TryGetValue(gameObject, out var list) && list != null)
            foreach (var attachment in list.Items)
                if (attachment?.Metadata != null && attachment.Metadata.BehaviorType.Type != null)
                    attached.Add(attachment.Metadata.BehaviorType.Type);

        new BehaviorDropdown(_dropdownState, attached, type =>
        {
            AddBehavior(root, gameObject, type);
            onAdded?.Invoke(type);
        }).Show(rect);
    }

    /// <summary>One Variable's value field for this object (its override, or the default), for embedding in an IMGUIContainer.</summary>
    public static void DrawVariableGUI(SchematicObject root, GameObject gameObject, BehaviorAttachment attachment, string variableName, bool showLabel = false)
    {
        if (!root.Behaviors.TryGetValue(gameObject, out var list) || list == null || attachment?.Metadata == null)
            return;
        int index = list.Items.IndexOf(attachment);
        var definition = attachment.Metadata.FindVariable(variableName);
        if (index < 0 || definition == null)
            return;
        DrawVariable(root, gameObject, index, attachment, definition, showLabel);
    }

    /// <summary>Shows the Behavior picker; what happens with the choice is up to the caller.</summary>
    public static void ShowBehaviorDropdown(Rect rect, HashSet<Type> exclude, Action<Type> onPicked)
        => new BehaviorDropdown(_dropdownState, exclude ?? new HashSet<Type>(), onPicked).Show(rect);

    /// <summary>Attaches a Behavior with Undo, as the Inspector's add button does (required Behaviors and components, OnSetup).</summary>
    public static void AttachWithUndo(SchematicObject root, GameObject gameObject, Type behaviorType) => AddBehavior(root, gameObject, behaviorType);

    /// <summary>Removes an attachment with Undo.</summary>
    public static void RemoveWithUndo(SchematicObject root, GameObject gameObject, BehaviorAttachment attachment)
    {
        if (!root.Behaviors.TryGetValue(gameObject, out var list) || list == null)
            return;
        Undo.RecordObject(root, "Remove Schematic Behavior");
        list.Items.Remove(attachment);
        if (list.Items.Count == 0)
            root.Behaviors.Remove(gameObject);
        Commit(root);
    }

    /// <summary>The Inspector's drawing of one attachment's Variable values, for embedding in an IMGUIContainer.</summary>
    public static void DrawVariablesGUI(SchematicObject root, GameObject gameObject, BehaviorAttachment attachment)
    {
        if (!root.Behaviors.TryGetValue(gameObject, out var list) || list == null || attachment?.Metadata == null)
            return;
        int index = list.Items.IndexOf(attachment);
        if (index < 0)
            return;
        if (Application.isPlaying && root.IsSpawned)
            DrawLiveAttachment(root, gameObject, attachment);
        else
            DrawVariables(root, gameObject, index, attachment, attachment.Metadata);
    }

    #endregion

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
                var definition = attachment?.Metadata != null ? attachment.Metadata.FindVariable(variables[i].Name) : null;
                InspectorRows.Begin(new InspectorRows.Row
                {
                    Kind = InspectorRows.RowKind.BehaviorVariable, Behavior = instance.Behavior.GetType(), Path = PathOf(root, gameObject),
                    Name = variables[i].Name, ValueType = variables[i].ValueType,
                }, ObjectNames.NicifyVariableName(variables[i].Name), InspectorRows.TypeName(variables[i].ValueType) + (definition != null && definition.Networked ? "  ⇅" : ""));
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                if (SchematicValueField.Draw(GUIContent.none, variables[i].ValueType, runtime.GetVariableBoxed(instance, i), out var newValue))
                    runtime.SetVariableBoxed(instance, i, newValue);
                EditorGUI.indentLevel = indent;
                InspectorRows.End();
            }
            EditorGUI.indentLevel--;
        }
    }

    #endregion
}
