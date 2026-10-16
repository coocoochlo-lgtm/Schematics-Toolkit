using BlueGraph;
using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps the nodes that use a Signal (Send / On Received, in any graph) in step with its parameters, so editing a
/// parameter updates their pins straight away: a renamed parameter keeps its wires, a retyped one is re-pinned, a
/// removed one goes, and the pins stay in the parameters' order (On Received fills its pins by position).
/// Also renames those nodes when the Signal is renamed. Everything is recorded for Undo.
/// </summary>
public static class SignalPortSync
{
    private static FieldInfo _portsField;
    private static MethodInfo _refreshPortMap;

    /// <summary>A Signal asset's own parameters, as names and types.</summary>
    public static List<(string name, Type type)> AssetParameters(SignalData signal)
    {
        var result = new List<(string, Type)>();
        if (signal == null)
            return result;
        foreach (var parameter in signal.Parameters.Contents)
        {
            Union.TypeLookup.TryGetValue(parameter.Value.Type, out var type);
            result.Add((parameter.Name, type ?? typeof(object)));
        }
        return result;
    }

    /// <summary>A Union that says it carries <paramref name="type"/> (Object for types a Union has no case for).</summary>
    public static Union UnionOf(Type type)
    {
        var union = Union.Null;
        if (type != null && Union.ReverseTypeLookup.TryGetValue(type, out var valueType))
            union.Type = valueType;
        else if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type))
            union.Type = Union.ValueType.Object;
        return union;
    }

    /// <summary>
    /// Replaces a Signal asset's parameters (for Signals that aren't a graph's: those go through
    /// <see cref="GraphScopeEditing.SetSignalParameters"/>), then updates the nodes that use it.
    /// </summary>
    public static void SetAssetParameters(SignalData signal, List<(string name, Type type)> parameters, string renamedFrom = null, string renamedTo = null)
    {
        if (signal == null)
            return;
        Undo.RecordObject(signal, "Edit Signal Parameters");
        signal.Parameters.Contents.Clear();
        foreach (var (name, type) in parameters)
            signal.Parameters.AddParameter(name, UnionOf(type));
        signal.RuntimeArgs = new Union[parameters.Count];
        EditorUtility.SetDirty(signal);
        Sync(signal, parameters, renamedFrom, renamedTo);
        AssetDatabase.SaveAssetIfDirty(signal);
    }

    /// <summary>
    /// Brings every node that sends or reacts to <paramref name="signal"/> up to date with these parameters. A rename
    /// looks through every graph in the project (so no wire is lost in a graph that isn't open); other changes only
    /// touch the graphs that are loaded, and the rest catch up when they're opened.
    /// </summary>
    public static void Sync(SignalData signal, IReadOnlyList<(string name, Type type)> parameters, string renamedFrom = null, string renamedTo = null)
    {
        if (signal == null)
            return;
        bool rename = !string.IsNullOrEmpty(renamedFrom) && !string.IsNullOrEmpty(renamedTo) && renamedFrom != renamedTo;
        foreach (var graph in Graphs(loadAll: rename))
        {
            bool changed = false;
            foreach (var node in graph.Nodes)
            {
                PortDirection direction;
                if (node is SendSignalNode send && send.Signal == signal) direction = PortDirection.Input;
                else if (node is OnSignalReceivedNode receive && receive.Signal == signal) direction = PortDirection.Output;
                else continue;

                if (!changed)
                    Undo.RecordObject(graph, "Edit Signal Parameters");
                changed = true;
                SyncNode(graph, node, parameters, direction, rename ? renamedFrom : null, renamedTo);
            }
            if (changed)
            {
                EditorUtility.SetDirty(graph);
                ReloadWindows(graph);
            }
        }
    }

    /// <summary>Renames the nodes that send or react to a Signal ("Send X", "On X") after the Signal was renamed.</summary>
    public static void RenameNodes(SignalData signal, string name)
    {
        if (signal == null)
            return;
        foreach (var graph in Graphs(loadAll: false))
        {
            bool changed = false;
            foreach (var node in graph.Nodes)
            {
                string wanted = node is SendSignalNode send && send.Signal == signal ? "Send " + name
                              : node is OnSignalReceivedNode receive && receive.Signal == signal ? "On " + name : null;
                if (wanted == null || node.Name == wanted)
                    continue;
                if (!changed)
                    Undo.RecordObject(graph, "Rename Signal");
                changed = true;
                node.Name = wanted;
            }
            if (changed)
            {
                EditorUtility.SetDirty(graph);
                ReloadWindows(graph);
            }
        }
    }

    /// <summary>Re-checks the direction rules on every node in the loaded graphs that uses this Signal.</summary>
    public static void Revalidate(SignalData signal)
    {
        foreach (var graph in Graphs(loadAll: false))
        {
            bool any = false;
            foreach (var node in graph.Nodes)
                if ((node is SendSignalNode send && send.Signal == signal) || (node is OnSignalReceivedNode receive && receive.Signal == signal))
                {
                    node.Validate();
                    any = true;
                }
            if (any)
                ReloadWindows(graph);
        }
    }

    private static void SyncNode(Graph graph, Node node, IReadOnlyList<(string name, Type type)> parameters, PortDirection direction, string renamedFrom, string renamedTo)
    {
        // Loads each port's wires, which renaming and disconnecting need.
        node.Validate();

        // Renamed in place, so it keeps its wires (BlueGraph re-points the other ends). A wire whose other end can't be
        // found would break that, so such a pin is re-made instead.
        if (renamedFrom != null && node.GetPort(renamedTo) == null && node.GetPort(renamedFrom) is { } renamed)
        {
            if (renamed.Connections.All(c => c.Port != null)) renamed.Name = renamedTo;
            else node.RemovePort(renamed);
        }

        var wanted = new HashSet<string>();
        var ordered = new List<Port>();
        foreach (var (name, type) in parameters)
        {
            if (string.IsNullOrEmpty(name) || !wanted.Add(name))
                continue;
            var port = node.GetPort(name);
            if (port != null && port.Direction != direction)
            {
                node.RemovePort(port);
                port = null;
            }
            if (port == null)
            {
                port = new Port
                {
                    Type = type,
                    Name = name,
                    Capacity = direction == PortDirection.Input ? PortCapacity.Single : PortCapacity.Multiple,
                    Direction = direction,
                };
                node.AddPort(port);
            }
            else if (port.Type != type)
            {
                // A new type: the old wires may not fit it any more.
                Disconnect(graph, port);
                port.Type = type;
            }
            ordered.Add(port);
        }

        // Pins of parameters that are gone (flow pins and Send's "To" stay).
        foreach (var port in node.Ports.Values.ToList())
            if (port.Type != typeof(ActionPort) && port.Name != SendSignalNode.ToPort && !wanted.Contains(port.Name))
                node.RemovePort(port);

        Reorder(node, ordered);
        node.Validate();
    }

    private static void Disconnect(Graph graph, Port port)
    {
        foreach (var connection in port.Connections.ToList())
        {
            var other = connection.Port;
            if (other == null)
                continue;
            if (port.Direction == PortDirection.Input) graph.RemoveEdge(other, port);
            else graph.RemoveEdge(port, other);
        }
    }

    // Puts the parameter pins after every other pin, in the parameters' order. BlueGraph keeps a node's pins in a
    // private array (its lookup is rebuilt from it in order), so it's set through reflection; if BlueGraph changes,
    // the order is just left as it is.
    private static void Reorder(Node node, List<Port> parameters)
    {
        try
        {
            _portsField ??= typeof(Node).GetField("ports", BindingFlags.Instance | BindingFlags.NonPublic);
            _refreshPortMap ??= typeof(Node).GetMethod("RefreshPortMap", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_portsField == null || _refreshPortMap == null)
                return;
            var current = node.Ports.Values.ToList();
            var others = current.Where(p => !parameters.Contains(p)).ToList();
            var order = others.Concat(parameters.Where(current.Contains)).ToArray();
            if (order.SequenceEqual(current))
                return;
            _portsField.SetValue(node, order);
            _refreshPortMap.Invoke(node, null);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static IEnumerable<SchematicGraph> Graphs(bool loadAll)
    {
        var graphs = new HashSet<SchematicGraph>(Resources.FindObjectsOfTypeAll<SchematicGraph>().Where(g => g != null && EditorUtility.IsPersistent(g)));
        if (loadAll)
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SchematicGraph)))
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (asset is SchematicGraph graph)
                        graphs.Add(graph);
        return graphs;
    }

    private static void ReloadWindows(SchematicGraph graph)
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
            if (window != null && window.SchematicGraph == graph && window.Canvas != null)
                window.Canvas.Reload();
    }
}
