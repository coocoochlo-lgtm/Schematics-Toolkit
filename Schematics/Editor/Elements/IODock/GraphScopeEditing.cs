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

    /// <summary>
    /// Sets a Variable's starting value. The fields that call this are delayed (they commit on Enter or when they
    /// lose focus), and the docks aren't rebuilt for it, so the field you're in keeps its focus.
    /// </summary>
    public static void SetVariableDefault(SchematicGraph graph, GraphVariable variable, object value)
    {
        Undo.RecordObject(graph, "Set Graph Variable Default");
        variable.Default = new SchematicValue(value, variable.ValueType);
        Commit(graph, structural: false, redraw: false);
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

    public static GraphSignal AddSignal(SchematicGraph graph, string name = null, GraphSignalDirection direction = GraphSignalDirection.Input)
    {
        Undo.RecordObject(graph, "Add Graph Signal");
        string fallback = direction == GraphSignalDirection.Output ? "New Output Signal" : "New Input Signal";
        var signal = new GraphSignal { Name = Unique(name ?? fallback, graph.Signals.Select(s => s.Name)) };
        signal.Asset = ScriptableObject.CreateInstance<SignalData>();
        signal.Asset.name = signal.Name;
        signal.Asset.Name = signal.Name;
        signal.Asset.GraphDirection = direction == GraphSignalDirection.Unspecified ? GraphSignalDirection.Input : direction;
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
        // Other graphs' nodes for it too (open ones; the rest are renamed when the window loads them).
        SignalPortSync.RenameNodes(signal.Asset, name);
        Commit(graph);
    }

    /// <summary>
    /// Makes a Signal an Input (other graphs send it, this graph reacts) or an Output (this graph sends it, other
    /// graphs react). Nodes that now use it the wrong way round show an error saying so.
    /// </summary>
    public static void SetSignalDirection(SchematicGraph graph, GraphSignal signal, GraphSignalDirection direction)
    {
        if (signal?.Asset == null || direction == GraphSignalDirection.Unspecified || signal.Direction == direction)
            return;
        Undo.RecordObject(graph, "Change Graph Signal Direction");
        Undo.RecordObject(signal.Asset, "Change Graph Signal Direction");
        signal.Direction = direction;
        EditorUtility.SetDirty(signal.Asset);
        SignalPortSync.Revalidate(signal.Asset);
        Commit(graph);
    }

    /// <summary>
    /// Graph Signals made before Signals had a direction get one, from how their graph uses them: one it only sends
    /// is an Output, anything else an Input (the graph reacts to it, others send it). Returns true if any changed.
    /// Not recorded for Undo: it's a one-time upgrade of old data.
    /// </summary>
    public static bool MigrateDirections(SchematicGraph graph)
    {
        if (graph == null)
            return false;
        bool changed = false;
        foreach (var signal in graph.Signals)
        {
            if (signal?.Asset == null || signal.Direction != GraphSignalDirection.Unspecified)
                continue;
            bool sends = graph.Nodes.Any(n => n is SendSignalNode send && send.Signal == signal.Asset);
            bool hears = graph.Nodes.Any(n => n is OnSignalReceivedNode receive && receive.Signal == signal.Asset);
            signal.Direction = sends && !hears ? GraphSignalDirection.Output : GraphSignalDirection.Input;
            EditorUtility.SetDirty(signal.Asset);
            changed = true;
        }
        if (changed)
        {
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssetIfDirty(graph);
            // So nodes now using a Signal the wrong way round show it (their views listen for errors).
            foreach (var node in graph.Nodes)
                if (node is SendSignalNode || node is OnSignalReceivedNode)
                    node.Validate();
        }
        return changed;
    }

    /// <summary>A graph Signal's parameters, as names and types.</summary>
    public static List<(string name, Type type)> SignalParameterList(GraphSignal signal)
        => signal == null ? new List<(string, Type)>()
         : signal.Parameters.Where(p => p != null).Select(p => (p.Name, p.Type.Type ?? typeof(float))).ToList();

    /// <summary>
    /// Replaces a Signal's parameters from the dock's or Inspector's parameter editor. <paramref name="renamedFrom"/>
    /// / <paramref name="renamedTo"/> name a parameter that was renamed, so the nodes' pins keep their wires.
    /// </summary>
    public static void SetSignalParameters(SchematicGraph graph, GraphSignal signal, List<(string name, Type type)> parameters, string renamedFrom = null, string renamedTo = null)
        => SetSignalParameters(graph, signal, parameters.Select(p => new GraphSignalParameter { Name = p.name, Type = p.type }).ToList(), renamedFrom, renamedTo);

    /// <summary>
    /// Replaces a Signal's parameters (names and types); the nodes using it, in this graph and others, get matching
    /// pins straight away (a renamed parameter keeps its wires).
    /// </summary>
    public static void SetSignalParameters(SchematicGraph graph, GraphSignal signal, List<GraphSignalParameter> parameters, string renamedFrom = null, string renamedTo = null)
    {
        Undo.RecordObject(graph, "Edit Graph Signal Parameters");
        signal.Parameters = parameters;
        // The asset carries them too, for anything that reads Signals without knowing the graph.
        if (signal.Asset != null)
        {
            Undo.RecordObject(signal.Asset, "Edit Graph Signal Parameters");
            signal.Asset.Parameters.Contents.Clear();
            foreach (var parameter in parameters)
                signal.Asset.Parameters.AddParameter(parameter.Name, DefaultUnion(parameter.Type.Type));
            signal.Asset.RuntimeArgs = new Union[parameters.Count];
            EditorUtility.SetDirty(signal.Asset);
            SignalPortSync.Sync(signal.Asset, SignalParameterList(signal), renamedFrom, renamedTo);
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
    private static Union DefaultUnion(Type type) => SignalPortSync.UnionOf(type);

    private static string Unique(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken.Where(t => t != null));
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++)
            if (!set.Contains($"{name} {i}"))
                return $"{name} {i}";
    }

    /// <summary>Saves the graph and tells the open windows (structural changes re-pin and redraw the canvas).</summary>
    /// <param name="redraw">False for value-only changes: saved, but the docks aren't rebuilt (the field already shows it).</param>
    public static void Commit(SchematicGraph graph, bool structural = true, bool redraw = true)
    {
        EditorUtility.SetDirty(graph);
        AssetDatabase.SaveAssetIfDirty(graph);
        if (!redraw)
            return;
        foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
            if (window != null && window.SchematicGraph != null)
                window.OnGraphScopeChanged(graph, structural);
        Changed?.Invoke(graph);
    }
}
