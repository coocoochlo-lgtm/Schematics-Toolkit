using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What a State Machine's prefab offers the editor: its Behaviors (as spreadsheet columns), their Variables (for
/// conditions) and Signals (for transitions). Plus creating state graphs and wiring a definition to a prefab.
/// </summary>
public static class StateMachineEditorUtility
{
    public readonly struct BehaviorColumn
    {
        public readonly Type Type;
        public readonly string Path;
        public readonly string Label;
        public BehaviorColumn(Type type, string path)
        {
            Type = type;
            Path = path ?? "";
            Label = ObjectNames.NicifyVariableName(type.Name) + (string.IsNullOrEmpty(Path) ? "" : " (" + Path + ")");
        }
    }

    public readonly struct VariableChoice
    {
        public readonly Type Behavior;
        public readonly string Path;
        public readonly string Variable;
        public readonly Type ValueType;
        public readonly string Label;
        public VariableChoice(Type behavior, string path, string variable, Type valueType)
        {
            Behavior = behavior; Path = path ?? ""; Variable = variable; ValueType = valueType;
            Label = behavior.Name + (string.IsNullOrEmpty(Path) ? "" : "@" + Path.Replace('/', '\\')) + "/" + variable;
        }
    }

    public static string PathOf(GameObject root, GameObject target)
        => root == null || target == null ? "" : AnimationUtility.CalculateTransformPath(target.transform, root.transform);

    /// <summary>The Behaviors attached anywhere on the prefab, except the State Machine itself.</summary>
    public static List<BehaviorColumn> Columns(GameObject prefab)
    {
        var columns = new List<BehaviorColumn>();
        var root = prefab != null ? prefab.GetComponent<SchematicObject>() : null;
        if (root == null)
            return columns;

        foreach (var entry in root.Behaviors)
        {
            if (entry.Key == null || entry.Value?.Items == null) continue;
            foreach (var attachment in entry.Value.Items)
            {
                var type = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                if (type == null || type == typeof(StateMachine)) continue;
                columns.Add(new BehaviorColumn(type, PathOf(prefab, entry.Key)));
            }
        }
        columns.Sort((a, b) => string.CompareOrdinal(a.Path + a.Label, b.Path + b.Label));
        return columns;
    }

    public static List<VariableChoice> Variables(GameObject prefab)
    {
        var choices = new List<VariableChoice>();
        var root = prefab != null ? prefab.GetComponent<SchematicObject>() : null;
        if (root == null)
            return choices;

        foreach (var entry in root.Behaviors)
        {
            if (entry.Key == null || entry.Value?.Items == null) continue;
            foreach (var attachment in entry.Value.Items)
            {
                var metadata = attachment?.Metadata;
                var type = metadata != null ? metadata.BehaviorType.Type : null;
                if (type == null) continue;
                foreach (var variable in metadata.Variables)
                    choices.Add(new VariableChoice(type, PathOf(prefab, entry.Key), variable.Name, variable.Type.Type));
            }
        }
        return choices;
    }

    public static List<(string label, SignalData signal)> Signals(GameObject prefab)
    {
        var result = new List<(string, SignalData)>();
        var root = prefab != null ? prefab.GetComponent<SchematicObject>() : null;
        if (root == null)
            return result;

        var seen = new HashSet<SignalData>();
        foreach (var entry in root.Behaviors)
        {
            if (entry.Value?.Items == null) continue;
            foreach (var attachment in entry.Value.Items)
            {
                var metadata = attachment?.Metadata;
                var type = metadata != null ? metadata.BehaviorType.Type : null;
                if (type == null) continue;
                foreach (var signal in metadata.Signals)
                    if (signal.Asset != null && seen.Add(signal.Asset))
                        result.Add((type.Name + "/" + signal.Name, signal.Asset));
            }
        }
        return result;
    }

    /// <summary>Creates the Schematic Graph that runs while a state is active, next to the definition asset.</summary>
    public static SchematicGraph CreateStateGraph(StateMachineDefinition definition, StateDefinition state)
    {
        string definitionPath = AssetDatabase.GetAssetPath(definition);
        string folder = Path.Combine(Path.GetDirectoryName(definitionPath) ?? "Assets", definition.name + " States").Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));

        var graph = ScriptableObject.CreateInstance<SchematicGraph>();
        graph.Prefab = definition.Prefab;
        graph.name = state.Name;
        AssetDatabase.CreateAsset(graph, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(state.Name)}.asset"));

        Undo.RecordObject(definition, "Create State Graph");
        state.Graph = graph;
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        return graph;
    }

    /// <summary>
    /// The definition a prefab's State Machine Behavior uses, creating it (next to the prefab) and assigning it if
    /// there isn't one yet.
    /// </summary>
    public static StateMachineDefinition DefinitionFor(SchematicObject root, GameObject target, bool create)
    {
        if (root == null)
            return null;
        if (target == null)
            target = root.gameObject;

        var attachment = root.FindAttachment(typeof(StateMachine), target);
        var existing = attachment?.FindOverride("Definition")?.Value?.Get<StateMachineDefinition>();
        if (existing != null || !create)
            return existing;

        if (attachment == null)
        {
            SchematicBehaviorsHeaderGUI.AttachWithUndo(root, target, typeof(StateMachine));
            attachment = root.FindAttachment(typeof(StateMachine), target);
            if (attachment == null)
                return null;
        }

        string prefabPath = AssetDatabase.GetAssetPath(root.gameObject);
        string folder = string.IsNullOrEmpty(prefabPath) ? "Assets" : Path.GetDirectoryName(prefabPath).Replace('\\', '/');
        var definition = ScriptableObject.CreateInstance<StateMachineDefinition>();
        definition.Prefab = root.gameObject;
        var first = new StateDefinition { Name = "Idle" };
        definition.States.Add(first);
        definition.InitialState = first.Id;
        AssetDatabase.CreateAsset(definition, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{SafeName(root.name)} State Machine.asset"));

        Undo.RecordObject(root, "Create State Machine");
        attachment.SetOverride("Definition", definition, typeof(StateMachineDefinition));
        EditorUtility.SetDirty(root);
        AssetDatabase.SaveAssets();
        return definition;
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "State" : name;
    }
}
