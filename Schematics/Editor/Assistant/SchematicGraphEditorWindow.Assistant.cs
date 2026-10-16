using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// What the assistant can do in a graph window: the same edits a person makes by dragging from the IO Dock or the node
/// search, placed at graph positions instead of the mouse.
/// </summary>
public partial class SchematicGraphEditorWindow
{
    internal const string FlowPort = "flow";

    /// <summary>The window-local point (what the drop handlers take) showing a graph position.</summary>
    private Vector2 WindowPointOf(Vector2 graphPosition)
    {
        var world = Canvas.contentViewContainer.LocalToWorld(graphPosition);
        return rootVisualElement.parent.ChangeCoordinatesTo(rootVisualElement, world);
    }

    /// <summary>A graph position in the window's root element's space, for the assistant to fly to.</summary>
    internal Vector2 RootPointOf(Vector2 graphPosition)
        => rootVisualElement.WorldToLocal(Canvas.contentViewContainer.LocalToWorld(graphPosition));

    /// <summary>Where a node's view is, in the window's root element's space.</summary>
    internal Rect? RootRectOf(Node node)
    {
        var view = ViewOf(node);
        if (view == null)
            return null;
        var world = view.worldBound;
        var min = rootVisualElement.WorldToLocal(world.min);
        var max = rootVisualElement.WorldToLocal(world.max);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Glides the canvas so a graph position is on screen.</summary>
    internal void AssistantShow(Vector2 graphPosition)
    {
        if (_motion != null)
            _motion.Frame(new Rect(graphPosition - new Vector2(150, 100), new Vector2(500, 300)));
    }

    internal Node AssistantAddNode(NodeReflectionData type, Vector2 graphPosition)
    {
        var node = type.CreateInstance();
        Canvas.AddNodeFromSearch(node, position.position + WindowPointOf(graphPosition), null);
        node.Position = graphPosition;
        RedrawGraphIO();
        return node;
    }

    internal Node AssistantAddSignalNode(SignalData signal, bool send, Vector2 graphPosition)
    {
        var node = send ? CreateInvokeNode(signal, WindowPointOf(graphPosition)) : CreateOnInvokeNode(signal, WindowPointOf(graphPosition));
        node.Position = graphPosition;
        return node;
    }

    internal Node AssistantAddVariableNode(Type behavior, string path, string variable, bool set, Vector2 graphPosition)
    {
        var item = new DockItem { Kind = DockItem.ItemKind.Variable, Behavior = behavior, Path = path ?? "", Name = variable };
        var node = CreateVariableNode(item, WindowPointOf(graphPosition), set);
        node.Position = graphPosition;
        return node;
    }

    internal Node AssistantAddBehaviorNode(Type behavior, string path, Vector2 graphPosition)
    {
        var item = new DockItem { Kind = DockItem.ItemKind.Behavior, Behavior = behavior, Path = path ?? "" };
        var node = CreateBehaviorNode(item, WindowPointOf(graphPosition));
        node.Position = graphPosition;
        return node;
    }

    /// <summary>
    /// Connects an output of one node to an input of another. "flow" (or empty) means the flow arrows. Returns why not,
    /// or null once connected.
    /// </summary>
    internal string AssistantConnect(Node from, string fromPort, Node to, string toPort)
    {
        var fromView = ViewOf(from);
        var toView = ViewOf(to);
        if (fromView == null || toView == null)
            return "a node isn't on the canvas";

        var output = FindPort(from, fromPort, PortDirection.Output);
        var input = FindPort(to, toPort, PortDirection.Input);
        if (output == null) return $"{from.Name} has no output \"{fromPort}\"";
        if (input == null) return $"{to.Name} has no input \"{toPort}\"";

        var outputView = fromView.GetOutputPort(output.Name);
        var inputView = toView.GetInputPort(input.Name);
        if (outputView == null || inputView == null)
            return "the ports aren't shown";
        Canvas.AddEdge(new UnityEditor.Experimental.GraphView.Edge { output = outputView, input = inputView }, true);
        return null;
    }

    private static Port FindPort(Node node, string name, PortDirection direction)
    {
        var ports = node.Ports.Values.Where(p => p.Direction == direction).ToList();
        bool flow = string.IsNullOrEmpty(name) || name.Equals(FlowPort, StringComparison.OrdinalIgnoreCase) || name == "▶" || name == "▷";
        if (flow)
            return ports.FirstOrDefault(p => p.Type == typeof(ActionPort))
                ?? ports.FirstOrDefault(p => p.Name == "▶" || p.Name == "▷");
        return ports.FirstOrDefault(p => p.Name == name)
            ?? ports.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? ports.FirstOrDefault(p => string.Equals(Simplify(p.Name), Simplify(name), StringComparison.OrdinalIgnoreCase));
    }

    internal static string PortLabel(Port port) => port.Type == typeof(ActionPort) ? FlowPort : port.Name;

    internal static string Simplify(string name) => new string((name ?? "").Where(char.IsLetterOrDigit).ToArray());

    internal void AssistantMove(Node node, Vector2 graphPosition)
    {
        Undo.RegisterCompleteObjectUndo(SchematicGraph, "Move Node");
        node.Position = graphPosition;
        var view = ViewOf(node);
        if (view != null)
            view.SetPosition(new Rect(graphPosition, view.GetPosition().size));
        EditorUtility.SetDirty(SchematicGraph);
    }

    internal void AssistantDelete(Node node)
    {
        Undo.RegisterCompleteObjectUndo(SchematicGraph, "Delete Node");
        SchematicGraph.RemoveNode(node);
        EditorUtility.SetDirty(SchematicGraph);
        Canvas.Reload();
        RedrawGraphIO();
    }

    internal void AssistantRefresh()
    {
        Canvas.Reload();
        RedrawGraphIO();
    }

    internal void AssistantFocus(Node node) => FocusNode(node);

    /// <summary>Adds a Behavior to one of the prefab's objects, as the IO Dock's "+ Add Behavior" does.</summary>
    internal bool AssistantAddBehavior(string path, Type behavior)
    {
        if (_dock == null)
            return false;
        _dock.AddBehavior(path ?? "", behavior);
        return true;
    }

    /// <summary>The window's root element, where the assistant floats.</summary>
    internal VisualElement AssistantLayer => rootVisualElement;
}
