using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using ModelNode = BlueGraph.Node;
using ModelPort = BlueGraph.Port;

/// <summary>Creates a typed redirect node by dragging a Schematic graph wire.</summary>
[InitializeOnLoad]
internal static class RedirectNodeEditor
{
    private sealed class WireDrag
    {
        public SchematicGraph Graph;
        public CanvasView Canvas;
        public Edge Edge;
        public Vector2 Start;
        public Vector2 Current;
        public SchematicGraphNode Redirect;
    }

    private sealed class EdgeStyle
    {
        public Edge Edge;
        public Color InputColor;
        public Color OutputColor;
        public int Width;
    }

    private static readonly Dictionary<int, WireDrag> Drags = new();
    private static readonly List<EdgeStyle> HighlightedStyles = new();
    private static HashSet<Edge> HighlightedEdges = new();
    private static Edge HoveredEdge;

    static RedirectNodeEditor()
    {
        DefaultSearchProvider.ExclusiveTags.Add(SchematicRedirectNode.SearchOnlyTag);
        SchematicGraphEditorWindow.CanvasLoaded += OnCanvasLoaded;
    }

    private static void OnCanvasLoaded(SchematicGraphEditorWindow window, VisualElement canvasElement)
    {
        if (window?.SchematicGraph == null || canvasElement is not CanvasView canvas)
            return;
        var graphType = window.SchematicGraph.GetType();
        if (graphType.GetCustomAttribute<SchematicTypeAttribute>(false)?.Name == "Audio")
            return; // Sound Cue wires carry sample blocks and need audio-specific processors.

        EventCallback<PointerDownEvent> down = evt => OnPointerDown(window.SchematicGraph, canvas, evt);
        EventCallback<PointerMoveEvent> move = evt => OnPointerMove(canvas, evt);
        EventCallback<PointerUpEvent> up = OnPointerUp;
        EventCallback<PointerLeaveEvent> leave = _ => ClearHighlight();
        canvas.RegisterCallback<PointerDownEvent>(down, TrickleDown.TrickleDown);
        canvas.RegisterCallback<PointerMoveEvent>(move, TrickleDown.TrickleDown);
        canvas.RegisterCallback<PointerUpEvent>(up, TrickleDown.TrickleDown);
        canvas.RegisterCallback<PointerLeaveEvent>(leave, TrickleDown.TrickleDown);
        canvas.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            canvas.UnregisterCallback<PointerDownEvent>(down, TrickleDown.TrickleDown);
            canvas.UnregisterCallback<PointerMoveEvent>(move, TrickleDown.TrickleDown);
            canvas.UnregisterCallback<PointerUpEvent>(up, TrickleDown.TrickleDown);
            canvas.UnregisterCallback<PointerLeaveEvent>(leave, TrickleDown.TrickleDown);
            ClearHighlight();
            foreach (var pair in new List<KeyValuePair<int, WireDrag>>(Drags))
                if (pair.Value.Canvas == canvas)
                    Drags.Remove(pair.Key);
        });
    }

    private static void OnPointerDown(SchematicGraph graph, CanvasView canvas, PointerDownEvent evt)
    {
        if (evt.button != 0 || FindEdge(canvas.panel?.Pick(evt.position)) is not Edge edge
            || edge.output is not PortView output || edge.input is not PortView input
            || output.Target?.Node is not SchematicGraphNode || input.Target?.Node is not SchematicGraphNode)
            return;

        var source = output.Target.Direction == PortDirection.Output ? output.Target : input.Target;
        var destination = output.Target.Direction == PortDirection.Input ? output.Target : input.Target;
        if (source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input)
            return;
        bool flow = source.Type == typeof(ActionPort) && destination.Type == typeof(ActionPort);
        if (!flow && source.Type != destination.Type)
            return;

        Drags[evt.pointerId] = new WireDrag
        {
            Graph = graph,
            Canvas = canvas,
            Edge = edge,
            Start = evt.position,
            Current = evt.position
        };
        canvas.CapturePointer(evt.pointerId);
        evt.StopImmediatePropagation();
    }

    private static void OnPointerMove(CanvasView canvas, PointerMoveEvent evt)
    {
        if (Drags.TryGetValue(evt.pointerId, out var drag))
        {
            drag.Current = evt.position;
            evt.StopImmediatePropagation();
            if (drag.Redirect == null)
            {
                if ((drag.Current - drag.Start).sqrMagnitude < 36f)
                    return;
                drag.Redirect = InsertRedirect(drag.Graph, drag.Canvas, drag.Edge, drag.Current);
            }
            if (drag.Redirect != null)
                MoveRedirect(drag, drag.Current);
            return;
        }

        UpdateHighlight(canvas, FindHoverEdge(canvas, canvas.panel?.Pick(evt.position)));
    }

    private static void OnPointerUp(PointerUpEvent evt)
    {
        if (!Drags.TryGetValue(evt.pointerId, out var drag))
            return;
        Drags.Remove(evt.pointerId);
        drag.Current = evt.position;
        if (drag.Canvas.HasPointerCapture(evt.pointerId))
            drag.Canvas.ReleasePointer(evt.pointerId);
        evt.StopImmediatePropagation();

        // A click remains a no-op; dragging creates the redirect immediately and leaves it at the release point.
        if (drag.Redirect == null && (drag.Current - drag.Start).sqrMagnitude >= 36f)
            drag.Redirect = InsertRedirect(drag.Graph, drag.Canvas, drag.Edge, drag.Current);
        if (drag.Redirect != null)
            MoveRedirect(drag, drag.Current);
    }

    private static SchematicGraphNode InsertRedirect(SchematicGraph graph, CanvasView canvas, Edge edge, Vector2 position)
    {
        if (edge?.output is not PortView output || edge.input is not PortView input
            || output.Target == null || input.Target == null)
            return null;

        var source = output.Target.Direction == PortDirection.Output ? output.Target : input.Target;
        var destination = output.Target.Direction == PortDirection.Input ? output.Target : input.Target;
        if (source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input)
            return null;
        bool flow = source.Type == typeof(ActionPort) && destination.Type == typeof(ActionPort);
        if (!flow && source.Type != destination.Type)
            return null;

        var sourceCenter = source == output.Target ? output.worldBound.center : input.worldBound.center;
        var destinationCenter = destination == input.Target ? input.worldBound.center : output.worldBound.center;
        var segment = destinationCenter - sourceCenter;
        float segmentLengthSquared = segment.sqrMagnitude;
        float t = segmentLengthSquared > 0f
            ? Mathf.Clamp01(Vector2.Dot(position - sourceCenter, segment) / segmentLengthSquared)
            : 0.5f;
        var graphPosition = canvas.contentViewContainer.WorldToLocal(sourceCenter + segment * t)
            - RedirectNodeView.PositionOffset;

        SchematicGraphNode redirect;
        if (flow)
        {
            redirect = NodeReflection.Instantiate<SchematicFlowRedirectNode>();
        }
        else
        {
            var valueRedirect = NodeReflection.Instantiate<SchematicRedirectNode>();
            valueRedirect.SetValueType(source.Type);
            redirect = valueRedirect;
        }

        redirect.Position = graphPosition;
        redirect.Name = flow ? "Flow Redirect" : "Redirect";
        var redirectInput = redirect.GetPort(flow ? "▷" : SchematicRedirectNode.InputPort);
        var redirectOutput = redirect.GetPort(flow ? "▶" : SchematicRedirectNode.OutputPort);
        if (redirectInput == null || redirectOutput == null
            || redirectInput.Direction != PortDirection.Input || redirectOutput.Direction != PortDirection.Output)
        {
            Debug.LogError($"Could not create redirect ports for {redirect.GetType().Name}; leaving the original wire unchanged.");
            return null;
        }

        Undo.RegisterCompleteObjectUndo(graph, "Insert Redirect Node");
        graph.RemoveEdge(source, destination);
        graph.AddNode(redirect);
        graph.AddEdge(source, redirectInput);
        graph.AddEdge(redirectOutput, destination);
        EditorUtility.SetDirty(graph);
        ClearHighlight();
        canvas.Reload();
        return redirect;
    }

    private static void MoveRedirect(WireDrag drag, Vector2 worldPosition)
    {
        var graphPosition = drag.Canvas.contentViewContainer.WorldToLocal(worldPosition)
            - RedirectNodeView.PositionOffset;
        var view = drag.Canvas.nodes.OfType<NodeView>().FirstOrDefault(candidate => candidate.Target == drag.Redirect);
        if (view == null)
            return;

        view.SetPosition(new Rect(graphPosition, RedirectNodeView.NodeSize));
        EditorUtility.SetDirty(drag.Graph);
    }

    private static Edge FindHoverEdge(CanvasView canvas, VisualElement picked)
    {
        var edge = FindEdge(picked);
        if (edge != null)
            return edge;
        for (var current = picked; current != null; current = current.parent)
        {
            if (current is not NodeView nodeView || !IsRedirect(nodeView.Target))
                continue;
            foreach (var port in nodeView.Target.Ports.Values)
                foreach (var candidate in canvas.edges.OfType<Edge>())
                    if (HasPort(candidate, port))
                        return candidate;
        }
        return null;
    }

    private static void UpdateHighlight(CanvasView canvas, Edge hovered)
    {
        if (hovered == HoveredEdge)
            return;
        var next = hovered == null ? new HashSet<Edge>() : FindRedirectChain(canvas, hovered);
        if (next.SetEquals(HighlightedEdges))
        {
            HoveredEdge = hovered;
            return;
        }

        ClearHighlight();
        HoveredEdge = hovered;
        var color = new Color(1f, 0.82f, 0.25f, 1f);
        foreach (var edge in next)
        {
            if (edge?.edgeControl == null)
                continue;
            HighlightedStyles.Add(new EdgeStyle
            {
                Edge = edge,
                InputColor = edge.edgeControl.inputColor,
                OutputColor = edge.edgeControl.outputColor,
                Width = edge.edgeControl.edgeWidth
            });
            edge.edgeControl.inputColor = color;
            edge.edgeControl.outputColor = color;
            edge.edgeControl.edgeWidth += 2;
            edge.edgeControl.UpdateLayout();
        }
        HighlightedEdges = next;
    }

    private static void ClearHighlight()
    {
        foreach (var style in HighlightedStyles)
        {
            if (style.Edge?.edgeControl == null)
                continue;
            style.Edge.edgeControl.inputColor = style.InputColor;
            style.Edge.edgeControl.outputColor = style.OutputColor;
            style.Edge.edgeControl.edgeWidth = style.Width;
            style.Edge.edgeControl.UpdateLayout();
        }
        HighlightedStyles.Clear();
        HighlightedEdges.Clear();
        HoveredEdge = null;
    }

    private static HashSet<Edge> FindRedirectChain(CanvasView canvas, Edge start)
    {
        var edgesByPort = new Dictionary<ModelPort, List<Edge>>();
        foreach (var edge in canvas.edges.OfType<Edge>())
        {
            if (edge.output is not PortView output || edge.input is not PortView input
                || output.Target == null || input.Target == null)
                continue;
            AddEdgeForPort(edgesByPort, output.Target, edge);
            AddEdgeForPort(edgesByPort, input.Target, edge);
        }

        var visited = new HashSet<Edge>();
        var queue = new Queue<Edge>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var edge = queue.Dequeue();
            if (edge == null || !visited.Add(edge)
                || edge.output is not PortView output || edge.input is not PortView input)
                continue;
            foreach (var port in new[] { output.Target, input.Target })
            {
                if (port == null || !IsRedirect(port.Node))
                    continue;
                foreach (var redirectPort in port.Node.Ports.Values)
                    if (edgesByPort.TryGetValue(redirectPort, out var linked))
                        foreach (var linkedEdge in linked)
                            if (!visited.Contains(linkedEdge))
                                queue.Enqueue(linkedEdge);
            }
        }
        return visited;
    }

    private static void AddEdgeForPort(Dictionary<ModelPort, List<Edge>> edgesByPort, ModelPort port, Edge edge)
    {
        if (!edgesByPort.TryGetValue(port, out var list))
            edgesByPort[port] = list = new List<Edge>();
        list.Add(edge);
    }

    private static bool HasPort(Edge edge, ModelPort port)
        => edge?.output is PortView output && ReferenceEquals(output.Target, port)
            || edge?.input is PortView input && ReferenceEquals(input.Target, port);

    private static bool IsRedirect(ModelNode node)
        => node is SchematicRedirectNode || node is SchematicFlowRedirectNode;

    private static Edge FindEdge(VisualElement element)
    {
        for (var current = element; current != null; current = current.parent)
            if (current is Edge edge)
                return edge;
        return null;
    }
}
