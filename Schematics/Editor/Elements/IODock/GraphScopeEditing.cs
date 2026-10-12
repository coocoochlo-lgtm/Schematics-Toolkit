using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edits a graph's own Variables and Signals (the Graph tab and the Inspector both use these), with Undo. Nodes in the
/// graph that use an item are kept in step: renamed with it, re-pinned when its type or parameters change.
/// </summary>
public static class GraphScopeEditing
{
    /// <summary>Types a graph Variable can have, as menu paths.</summary>
    public static readonly (string path, Type type)[] VariableTypes =
    {
        ("float", typeof(float)), ("int", typeof(int)), ("bool", typeof(bool)), ("string", typeof(string)),
        ("Vector2", typeof(Vector2)), ("Vector3", typeof(Vector3)), ("Vector4", typeof(Vector4)),
        ("Color", typeof(Color)), ("Quaternion", typeof(Quaternion)), ("LayerMask", typeof(LayerMask)),
        ("Objects/GameObject", typeof(GameObject)), ("Objects/Transform", typeof(Transform)),
        ("Objects/Material", typeof(Material)), ("Objects/Sprite", typeof(Sprite)), ("Objects/Texture2D", typeof(Texture2D)),
        ("Objects/AudioClip", typeof(AudioClip)), ("Objects/AnimationClip", typeof(AnimationClip)),
        ("Objects/Scriptable Object", typeof(ScriptableObject)), ("Objects/Signal", typeof(SignalData)),
    };

    /// <summary>Raised after any change, so open docks and inspectors redraw.</summary>
    public static event Action<SchematicGraph> Changed;

    #region Variables

    public static GraphVariable AddVariable(SchematicGraph graph, Type type, string name = null)
    {
        Undo.RecordObject(graph, "Add Graph Variable");
        var variable = new GraphVariable
        {
            Name = Unique(name ?? "New " + ObjectNames.NicifyVariableName(DockRows.TypeName(type)), graph.Variables.Select(v => v.Name)),
            Type = type,
            Default = new SchematicValue(type.IsValueType ? Activator.CreateInstance(type) : null, type),
        };
        graph.Variables.Add(variable);
        Commit(graph);
        return variable;
    }

    public static void RenameVariable(SchematicGraph graph, GraphVariable variable, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == variable.Name)
            return;
        Undo.RecordObject(graph, "Rename Graph Variable");
        name = Unique(name.Trim(), graph.Variables.Where(v => v != variable).Select(v => v.Name));
        variable.Name = name;
        foreach (var node in graph.Nodes.OfType<GraphVariableNodeBase>())
            if (node.VariableId == variable.Id && node.DeclaringGraph == graph)
                node.Name = (node is SetGraphVariableNode ? "Set " : "Get ") + ObjectNames.NicifyVariableName(name);
        Commit(graph);
    }

    public static void SetVariableType(SchematicGraph graph, GraphVariable variable, Type type)
    {
        if (type == null || type == variable.ValueType)
            return;
        Undo.RecordObject(graph, "Change Graph Variable Type");
        variable.Type = type;
        variable.Default = new SchematicValue(type.IsValueType ? Activator.CreateInstance(type) : null, type);
        Commit(graph);
    }

    public static void SetVariableDefault(SchematicGraph graph, GraphVariable variable, object value)
    {
        Undo.RecordObject(graph, "Set Graph Variable Default");
        variable.Default = new SchematicValue(value, variable.ValueType);
        Commit(graph, structural: false);
    }

    public static void DuplicateVariable(SchematicGraph graph, GraphVariable variable)
    {
        Undo.RecordObject(graph, "Duplicate Graph Variable");
        graph.Variables.Insert(graph.Variables.IndexOf(variable) + 1, new GraphVariable
        {
            Name = Unique(variable.Name, graph.Variables.Select(v => v.Name)),
            Type = variable.ValueType,
            Default = variable.Default?.Clone() ?? new SchematicValue(),
            Networked = variable.Networked,
            Description = variable.Description,
        });
        Commit(graph);
    }

    public static void DeleteVariable(SchematicGraph graph, GraphVariable variable)
    {
        int uses = graph.Nodes.OfType<GraphVariableNodeBase>().Count(n => n.VariableId == variable.Id && n.DeclaringGraph == graph);
        if (uses > 0 && !EditorUtility.DisplayDialog("Delete Graph Variable",
                $"{uses} node(s) in this graph use \"{variable.Name}\". They'll stay, but stop working until you point them at another Variable.", "Delete", "Cancel"))
            return;
        Undo.RecordObject(graph, "Delete Graph Variable");
        graph.Variables.Remove(variable);
        Commit(graph);
    }

    #endregion

    #region Signals

    public static GraphSignal AddSignal(SchematicGraph graph, string name = null)
    {
        Undo.RecordObject(graph, "Add Graph Signal");
        var signal = new GraphSignal { Name = Unique(name ?? "New Signal", graph.Signals.Select(s => s.Name)) };
        signal.Asset = ScriptableObject.CreateInstance<SignalData>();
        signal.Asset.name = signal.Name;
        signal.Asset.Name = signal.Name;
        AssetDatabase.AddObjectToAsset(signal.Asset, graph);
        Undo.RegisterCreatedObjectUndo(signal.Asset, "Add Graph Signal");
        graph.Signals.Add(signal);
        Commit(graph);
        return signal;
    }

    public static void RenameSignal(SchematicGraph graph, GraphSignal signal, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == signal.Name)
            return;
        Undo.RecordObject(graph, "Rename Graph Signal");
        name = Unique(name.Trim(), graph.Signals.Where(s => s != signal).Select(s => s.Name));
        signal.Name = name;
        if (signal.Asset != null)
        {
            Undo.RecordObject(signal.Asset, "Rename Graph Signal");
            signal.Asset.name = name;
            signal.Asset.Name = name;
            EditorUtility.SetDirty(signal.Asset);
        }
        foreach (var node in graph.Nodes)
        {
            if (node is SendSignalNode send && send.Signal == signal.Asset) send.Name = "Send " + name;
            else if (node is OnSignalReceivedNode receive && receive.Signal == signal.Asset) receive.Name = "On " + name;
        }
        Commit(graph);
    }

    /// <summary>Replaces a Signal's parameters (names and types); nodes using it get matching pins.</summary>
    public static void SetSignalParameters(SchematicGraph graph, GraphSignal signal, List<GraphSignalParameter> parameters)
    {
        Undo.RecordObject(graph, "Edit Graph Signal Parameters");
        signal.Parameters = parameters;
        // The asset carries them too, for anything that reads Signals without knowing the graph.
        if (signal.Asset != null)
        {
            Undo.RecordObject(signal.Asset, "Edit Graph Signal Parameters");
            signal.Asset.Parameters.Contents.Clear();
            foreach (var parameter in parameters)
                signal.Asset.Parameters.AddParameter(parameter.Name, DefaultUnion(ReferenceEquals(parameter.Type, null) ? null : parameter.Type.Type));
            EditorUtility.SetDirty(signal.Asset);
        }
        Commit(graph);
    }

    public static void DeleteSignal(SchematicGraph graph, GraphSignal signal)
    {
        int uses = graph.Nodes.Count(n => (n is SendSignalNode s && s.Signal == signal.Asset) || (n is OnSignalReceivedNode r && r.Signal == signal.Asset));
        if (uses > 0 && !EditorUtility.DisplayDialog("Delete Graph Signal",
                $"{uses} node(s) in this graph use \"{signal.Name}\". They'll stay, but stop working.", "Delete", "Cancel"))
            return;
        Undo.RecordObject(graph, "Delete Graph Signal");
        graph.Signals.Remove(signal);
        if (signal.Asset != null)
            Undo.DestroyObjectImmediate(signal.Asset);
        Commit(graph);
    }

    #endregion

    // Values a parameter's Union starts as, so its type is known without the graph.
    private static Union DefaultUnion(Type type)
    {
        if (type == null) return Union.Null;
        foreach (var pair in Union.TypeLookup)
            if (pair.Value == type)
            {
                var union = Union.Null;
                union.Type = pair.Key;
                return union;
            }
        return Union.Null;
    }

    private static string Unique(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken.Where(t => t != null));
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!set.Contains($"{name} {i}"))
                return $"{name} {i}";
    }

    /// <summary>Saves the graph and tells the open windows (structural changes re-pin and redraw the canvas).</summary>
    public static void Commit(SchematicGraph graph, bool structural = true)
    {
        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssetIfDirty(graph);
        foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
            if (window != null && window.SchematicGraph != null)
                window.OnGraphScopeChanged(graph, structural);
        Changed?.Invoke(graph);
    }
}
