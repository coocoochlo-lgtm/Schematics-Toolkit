using BlueGraph;
using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The bar along the bottom of the Schematic Graph window while a flow is stopped at a breakpoint or error, or after
/// a node in this graph threw: what happened, where, the values the node saw, and buttons to continue, step or stop.
/// </summary>
public class FlowDebugPanel : VisualElement
{
    private readonly SchematicGraphEditorWindow _window;

    private readonly Label _status;
    private readonly VisualElement _buttons;
    private readonly ScrollView _body;

    // Which foldouts were open, kept across refreshes.
    private bool _traceOpen, _valuesOpen = true;

    public FlowDebugPanel(SchematicGraphEditorWindow window)
    {
        _window = window;
        AddToClassList("flow-debug");
        pickingMode = PickingMode.Position;

        var header = new VisualElement();
        header.AddToClassList("flow-debug-header");
        Add(header);

        _status = new Label();
        _status.AddToClassList("flow-debug-status");
        header.Add(_status);

        _buttons = new VisualElement();
        _buttons.AddToClassList("flow-debug-buttons");
        header.Add(_buttons);

        _body = new ScrollView(ScrollViewMode.Vertical);
        _body.AddToClassList("flow-debug-body");
        Add(_body);

        // Clicks and scrolling here are for the panel, not the graph behind it.
        RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
        RegisterCallback<WheelEvent>(evt => evt.StopPropagation());

        style.display = DisplayStyle.None;

        // Its height settles once it's laid out (and changes as foldouts open): whatever keeps clear of it hears.
        RegisterCallback<GeometryChangedEvent>(_ => Changed?.Invoke());
    }

    /// <summary>Raised when the panel shows, hides or changes size (the minimap moves up out of its way).</summary>
    public event Action Changed;

    /// <summary>How much of the bottom of the graph the panel covers right now (0 while it's hidden).</summary>
    public float ShownHeight
    {
        get
        {
            if (style.display.value == DisplayStyle.None) return 0f;
            float height = layout.height;
            return float.IsNaN(height) ? 0f : height;
        }
    }

    /// <summary>The error shown when no flow is stopped: the newest one in this graph.</summary>
    internal FlowDebugger.FlowError ShownError { get; private set; }

    public void Refresh()
    {
        RefreshContent();
        Changed?.Invoke();
    }

    private void RefreshContent()
    {
        _buttons.Clear();
        _body.Clear();
        ShownError = null;
        RemoveFromClassList("flow-debug--error");
        RemoveFromClassList("flow-debug--paused");

        var flow = FlowDebugger.Current;
        if (flow != null)
        {
            ShowPaused(flow);
            style.display = DisplayStyle.Flex;
            return;
        }

        var graph = _window.SchematicGraph;
        ShownError = FlowDebugger.Errors.LastOrDefault(e => e.Graph == graph);
        if (ShownError != null)
        {
            ShowError(ShownError);
            style.display = DisplayStyle.Flex;
            return;
        }

        style.display = DisplayStyle.None;
    }

    private void ShowPaused(Flow flow)
    {
        bool error = flow.Paused == Flow.PauseReason.Error;
        AddToClassList(error ? "flow-debug--error" : "flow-debug--paused");

        string where = $"{NodeName(flow.PausedNode)}  ·  {InstanceName(flow)}";
        string graphNote = flow.Graph != _window.SchematicGraph && flow.Graph != null ? $"  ·  in {flow.Graph.name}" : "";
        string waiting = FlowDebugger.PausedFlows.Count > 1 ? $"  ·  {FlowDebugger.PausedFlows.Count - 1} more stopped" : "";
        _status.text = error
            ? $"⛔  {flow.Error?.GetType().Name ?? "Error"} in {where}{graphNote}{waiting}"
            : $"⏸  {(flow.Paused == Flow.PauseReason.Step ? "Step" : "Breakpoint")}: {where}{graphNote}{waiting}";

        AddButton("Continue (F5)", FlowDebugger.Continue, "Let this flow carry on" + (error ? ", skipping the nodes after the one that failed." : "."));
        if (!error)
            AddButton("Step (F10)", FlowDebugger.Step, "Run this node, then stop at the next one.");
        AddButton("Stop (Shift+F5)", FlowDebugger.Stop, "Drop the rest of this flow.");
        if (flow.Graph != null && flow.Graph != _window.SchematicGraph)
            AddButton("Show", () => SchematicGraphEditorWindow.ShowNodeInGraph(flow.Graph, flow.PausedNode), "Open the graph this flow is stopped in.");
        else if (flow.PausedNode != null)
            AddButton("Show", () => _window.FrameDebugNode(flow.PausedNode), "Frame the node.");
        if (error)
            AddButton("Copy", () => EditorGUIUtility.systemCopyBuffer = flow.Error?.ToString(), "Copy the error and its stack trace.");

        if (error && flow.Error != null)
            AddException(flow.Error);
        if (flow.PausedNode != null)
            AddValues(flow.PausedNode, flow.PausedScope);
    }

    private void ShowError(FlowDebugger.FlowError error)
    {
        AddToClassList("flow-debug--error");
        int count = FlowDebugger.Errors.Count(e => e.Graph == _window.SchematicGraph);
        _status.text = $"⛔  {error.Exception?.GetType().Name ?? "Error"} in {error.NodeName}  ·  {error.InstanceName}  ·  {error.Time:HH:mm:ss}"
            + (count > 1 ? $"  ·  {count - 1} older" : "");

        var node = _window.SchematicGraph != null ? _window.SchematicGraph.GetNodeById(error.NodeId) : null;
        if (node != null)
            AddButton("Show", () => _window.FrameDebugNode(node), "Frame the node that threw.");
        AddButton("Copy", () => EditorGUIUtility.systemCopyBuffer = error.Exception?.ToString(), "Copy the error and its stack trace.");
        AddButton("Dismiss", () => FlowDebugger.DismissError(error), "Hide this error (older ones show next).");
        if (count > 1)
            AddButton("Clear All", () => FlowDebugger.DismissErrors(_window.SchematicGraph), "Hide every error from this graph.");

        if (error.Exception != null)
            AddException(error.Exception);
    }

    private void AddException(Exception exception)
    {
        var message = ReadOnlyText(exception.Message);
        message.AddToClassList("flow-debug-message");
        _body.Add(message);

        var trace = new Foldout { text = "Stack Trace", value = _traceOpen };
        trace.RegisterValueChangedCallback(evt => { if (evt.target == trace) _traceOpen = evt.newValue; });
        var traceText = ReadOnlyText(exception.ToString());
        traceText.AddToClassList("flow-debug-trace");
        trace.Add(traceText);
        _body.Add(trace);
    }

    private void AddValues(SchematicGraphNode node, FlowScope scope)
    {
        var values = new Foldout { text = "Values", value = _valuesOpen };
        values.RegisterValueChangedCallback(evt => { if (evt.target == values) _valuesOpen = evt.newValue; });

        int rows = 0;
        foreach (var port in node.Ports.Values)
        {
            if (port.Type == typeof(ActionPort))
                continue;

            string text;
            if (port.Direction == PortDirection.Input)
                text = node.PeekInput(port.Name, scope, out var value) ? Describe(value) : "(not connected)";
            else
                text = Describe(node.PeekOutput(port.Name, scope));

            var row = new VisualElement();
            row.AddToClassList("flow-debug-value");
            var name = new Label((port.Direction == PortDirection.Input ? "▷ " : "▶ ") + port.Name);
            name.AddToClassList("flow-debug-value-name");
            row.Add(name);
            var valueLabel = new Label(text);
            valueLabel.AddToClassList("flow-debug-value-text");
            row.Add(valueLabel);
            values.Add(row);
            rows++;
        }

        if (rows == 0)
            values.Add(new Label("This node has no values."));
        _body.Add(values);
    }

    private void AddButton(string text, Action action, string tooltip)
    {
        var button = new Button(() =>
        {
            action();
            Refresh();
        }) { text = text, tooltip = tooltip };
        button.AddToClassList("flow-debug-button");
        _buttons.Add(button);
    }

    // Selectable, so messages can be copied a piece at a time.
    private static TextField ReadOnlyText(string text)
    {
        var field = new TextField { multiline = true, isReadOnly = true, value = text ?? "" };
        field.AddToClassList("flow-debug-text");
        return field;
    }

    private static string Describe(Union value)
    {
        try
        {
            if (value.Type == Union.ValueType.Null)
                return "null";
            return $"{value} ({value.Type})";
        }
        catch (Exception exception)
        {
            return $"(can't show: {exception.Message})";
        }
    }

    private static string NodeName(SchematicGraphNode node) => node != null ? node.Name : "?";

    private static string InstanceName(Flow flow) => flow.Instance != null ? flow.Instance.name : "(no object)";
}
