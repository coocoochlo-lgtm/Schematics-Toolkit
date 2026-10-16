using BlueGraph.Editor;
using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GraphViewEdge = UnityEditor.Experimental.GraphView.Edge;

/// <summary>
/// The flow visualizer of the Schematic Graph window: in Play Mode, a little ball runs along a flow arrow each time a
/// flow goes through it, and every value wire shows its current value above it (and sends a ball along when it
/// changes). It follows the object selected in the Hierarchy when that object runs this graph, or else every object
/// running it.
/// <para>
/// Switched with the "Flow" button above the minimap (remembered in EditorPrefs, off by default), because watching
/// every wire costs a little each frame. While it's off nothing listens to the graphs and nothing ticks, so it costs
/// nothing.
/// </para>
/// </summary>
public class FlowVisualizer : VisualElement
{
    #region Switch

    private const string EnabledKey = "SchematicGraph.FlowVisualizer";
    private static bool? _enabled;

    /// <summary>Whether the visualizer is on (in every graph window).</summary>
    public static bool Enabled
    {
        get => _enabled ??= EditorPrefs.GetBool(EnabledKey, false);
        set
        {
            if (value == Enabled)
                return;
            _enabled = value;
            EditorPrefs.SetBool(EnabledKey, value);
            EnabledChanged?.Invoke();
        }
    }

    /// <summary>Raised when the visualizer is switched on or off.</summary>
    public static event Action EnabledChanged;

    /// <summary>The "Flow" button that switches the visualizer, lit while it's on.</summary>
    public static Button CreateToggle()
    {
        var button = new Button(() => Enabled = !Enabled)
        {
            text = "Flow",
            tooltip = "Show the flow while playing: balls run along the arrows a flow goes through, and value wires show " +
                      "their current value. Follows the selected object if it runs this graph. Costs a little performance, " +
                      "so it's off until you switch it on.",
        };
        void Show()
        {
            bool on = Enabled;
            button.style.backgroundColor = on ? new StyleColor(new Color(0.85f, 0.62f, 0.15f, 1f)) : new StyleColor(StyleKeyword.Null);
            button.style.color = on ? new StyleColor(new Color(0.08f, 0.08f, 0.08f, 1f)) : new StyleColor(StyleKeyword.Null);
        }
        Action changed = Show;
        button.RegisterCallback<AttachToPanelEvent>(_ => { EnabledChanged -= changed; EnabledChanged += changed; Show(); });
        button.RegisterCallback<DetachFromPanelEvent>(_ => EnabledChanged -= changed);
        Show();
        return button;
    }

    #endregion

    private const int TickMs = 16;
    private const float BallDuration = 0.45f;      // seconds a ball takes along a wire
    private const float BallGap = 0.12f;           // a wire gets a new ball at most this often (a stream, not a pile)
    private const float BallSize = 9f;
    private const int MaxBalls = 160;
    private const int MaxQueued = 512;
    private const double ValueInterval = 0.1;      // values are read 10 times a second
    private const double EdgeScanInterval = 0.5;   // wires are re-listed twice a second (the canvas rebuilds them on reload)
    private const int MaxLabelLength = 28;

    private static readonly Color FlowBallColor = new(1f, 0.82f, 0.3f, 1f);

    private readonly SchematicGraphEditorWindow _window;
    private readonly VisualElement _labelLayer, _ballLayer;
    private readonly IVisualElementScheduledItem _tick;
    private bool _listening;

    // The canvas' wires, re-listed now and then: flow arrows by (from node, to node), and the value wires with their labels.
    private readonly Dictionary<(BlueGraph.Node, BlueGraph.Node), List<GraphViewEdge>> _flowEdges = new();
    private readonly Dictionary<GraphViewEdge, ValueWire> _valueWires = new();
    private readonly List<GraphViewEdge> _staleWires = new();
    private readonly HashSet<GraphViewEdge> _seenWires = new();
    private double _nextEdgeScan, _nextValueRead;

    // Flow arrows gone through since the last tick (filled while the game runs, emptied by the tick).
    private readonly List<(SchematicGraphNode from, SchematicGraphNode to)> _queued = new();

    private readonly List<Ball> _balls = new();
    private readonly Dictionary<GraphViewEdge, double> _lastBallAt = new();

    // The object being followed (null: every object running the graph).
    private GameObject _inspected;

    private sealed class ValueWire
    {
        public GraphViewEdge Edge;
        public SchematicGraphNode Node;
        public string Port;
        public Color Color;
        public Label Label;
        public string Text;
    }

    private sealed class Ball
    {
        public VisualElement Element;
        public GraphViewEdge Edge;
        public double Start;
        public bool Active;
    }

    public FlowVisualizer(SchematicGraphEditorWindow window)
    {
        _window = window;
        name = "flow-visualizer";
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = style.top = style.right = style.bottom = 0;
        style.overflow = Overflow.Hidden;

        _labelLayer = Layer();
        Add(_labelLayer);
        _ballLayer = Layer();
        Add(_ballLayer);

        _tick = schedule.Execute(Tick).Every(TickMs);
        _tick.Pause();

        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            EnabledChanged -= UpdateActive;
            EnabledChanged += UpdateActive;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            UpdateActive();
        });
        RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            EnabledChanged -= UpdateActive;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SetActive(false);
        });
    }

    private static VisualElement Layer()
    {
        var layer = new VisualElement { pickingMode = PickingMode.Ignore };
        layer.style.position = Position.Absolute;
        layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
        return layer;
    }

    private void OnPlayModeChanged(PlayModeStateChange change) => UpdateActive();

    private void UpdateActive() => SetActive(Enabled && EditorApplication.isPlaying && panel != null);

    private void SetActive(bool active)
    {
        if (active == _listening)
            return;
        _listening = active;
        if (active)
        {
            Flow.EditorEdgeTraversed += OnTraversed;
            _nextEdgeScan = _nextValueRead = 0;
            _tick.Resume();
        }
        else
        {
            Flow.EditorEdgeTraversed -= OnTraversed;
            _tick.Pause();
            ResetShown();
        }
    }

    // Everything shown goes: off, out of Play Mode, or the window closed.
    private void ResetShown()
    {
        _queued.Clear();
        _flowEdges.Clear();
        _valueWires.Clear();
        _lastBallAt.Clear();
        _balls.Clear();
        _labelLayer.Clear();
        _ballLayer.Clear();
        _inspected = null;
    }

    // From the game, as a flow goes along an arrow: just noted here, shown on the next tick.
    private void OnTraversed(GameObject instance, SchematicGraphNode from, SchematicGraphNode to)
    {
        if (from == null || to == null || !ReferenceEquals(from.Graph, _window.SchematicGraph))
            return;
        if (_inspected != null && !ReferenceEquals(instance, _inspected))
            return;
        if (_queued.Count < MaxQueued)
            _queued.Add((from, to));
    }

    private void Tick()
    {
        var canvas = _window.Canvas;
        if (canvas == null || _window.SchematicGraph == null || !EditorApplication.isPlaying)
        {
            _queued.Clear();
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now >= _nextEdgeScan)
        {
            _nextEdgeScan = now + EdgeScanInterval;
            ScanEdges(canvas);
        }

        // Balls for the arrows flows went through.
        for (int i = 0; i < _queued.Count; i++)
        {
            var (from, to) = _queued[i];
            if (_flowEdges.TryGetValue((from, to), out var edges))
                for (int e = 0; e < edges.Count; e++)
                    Launch(edges[e], FlowBallColor, now);
        }
        _queued.Clear();

        // Values, 10 times a second: a changed value sends a ball along its wire.
        bool readValues = now >= _nextValueRead;
        if (readValues)
        {
            _nextValueRead = now + ValueInterval;
            _inspected = FindInspected();
        }

        float zoom = Mathf.Max(0.05f, canvas.viewTransform.scale.x);
        var area = new Rect(Vector2.zero, layout.size);
        if (float.IsNaN(area.width) || float.IsNaN(area.height))
            return;
        var visibleArea = new Rect(area.x - 40f, area.y - 40f, area.width + 80f, area.height + 80f);

        foreach (var wire in _valueWires.Values)
        {
            if (readValues)
                ReadValue(wire, now);

            if (wire.Text == null || !Ends(wire.Edge, out var a, out var b))
            {
                wire.Label.style.display = DisplayStyle.None;
                continue;
            }
            var mid = PointOn(a, b, 0.5f, zoom);
            if (!visibleArea.Contains(mid))
            {
                wire.Label.style.display = DisplayStyle.None;
                continue;
            }
            wire.Label.style.display = DisplayStyle.Flex;
            wire.Label.style.left = Mathf.Round(mid.x);
            wire.Label.style.top = Mathf.Round(mid.y - 6f);
        }

        // Balls along their wires.
        float half = BallSize * 0.5f;
        for (int i = 0; i < _balls.Count; i++)
        {
            var ball = _balls[i];
            if (!ball.Active)
                continue;
            float t = (float)((now - ball.Start) / BallDuration);
            if (t >= 1f || ball.Edge == null || ball.Edge.panel == null || !Ends(ball.Edge, out var a, out var b))
            {
                ball.Active = false;
                ball.Element.style.display = DisplayStyle.None;
                continue;
            }
            var point = PointOn(a, b, Mathf.Clamp01(t), zoom);
            ball.Element.style.display = DisplayStyle.Flex;
            ball.Element.style.translate = new Translate(point.x - half, point.y - half, 0f);
        }
    }

    #region Wires

    private void ScanEdges(CanvasView canvas)
    {
        _flowEdges.Clear();
        _seenWires.Clear();
        foreach (var edge in canvas.edges.ToList())
        {
            if (!(edge.output is PortView output) || !(edge.input is PortView input) || output.Target == null || input.Target == null)
                continue;
            var fromNode = output.Target.Node;
            var toNode = input.Target.Node;
            if (fromNode == null || toNode == null)
                continue;

            if (output.Target.Type == typeof(ActionPort))
            {
                if (!_flowEdges.TryGetValue((fromNode, toNode), out var list))
                    _flowEdges[(fromNode, toNode)] = list = new List<GraphViewEdge>(1);
                list.Add(edge);
                continue;
            }

            if (!(fromNode is SchematicGraphNode source))
                continue;
            _seenWires.Add(edge);
            if (_valueWires.ContainsKey(edge))
                continue;
            var color = Color.Lerp(PrefabHierarchy.ColorOf(output.Target.Type), Color.white, 0.35f);
            _valueWires[edge] = new ValueWire
            {
                Edge = edge,
                Node = source,
                Port = output.Target.Name,
                Color = color,
                Label = CreateLabel(color),
            };
        }

        // Wires that went (deleted, or the canvas rebuilt its views) take their labels with them.
        _staleWires.Clear();
        foreach (var edge in _valueWires.Keys)
            if (!_seenWires.Contains(edge))
                _staleWires.Add(edge);
        foreach (var edge in _staleWires)
        {
            _valueWires[edge].Label.RemoveFromHierarchy();
            _valueWires.Remove(edge);
            _lastBallAt.Remove(edge);
        }
        _staleWires.Clear();
        _seenWires.Clear();
    }

    private Label CreateLabel(Color color)
    {
        var label = new Label { pickingMode = PickingMode.Ignore };
        label.style.position = Position.Absolute;
        // Centred over the point it's placed at, sitting just above the wire.
        label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f), 0f);
        label.style.fontSize = 10;
        label.style.color = color;
        label.style.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 0.82f);
        label.style.paddingLeft = label.style.paddingRight = 4;
        label.style.paddingTop = label.style.paddingBottom = 1;
        label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
        label.style.borderTopLeftRadius = label.style.borderTopRightRadius =
            label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 3;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.unityTextAlign = TextAnchor.MiddleCenter;
        label.style.display = DisplayStyle.None;
        _labelLayer.Add(label);
        return label;
    }

    private void ReadValue(ValueWire wire, double now)
    {
        string text;
        try
        {
            text = wire.Node.TryPeekOutputFor(_inspected, wire.Port, out var value) ? Format(value) : null;
        }
        catch (Exception)
        {
            text = null;
        }
        if (text == wire.Text)
            return;
        bool hadValue = wire.Text != null;
        wire.Text = text;
        if (text != null)
            wire.Label.text = text;
        if (hadValue && text != null)
            Launch(wire.Edge, wire.Color, now);
    }

    // The two ends of a wire in this element's space: the centres of its ports' connectors.
    private bool Ends(GraphViewEdge edge, out Vector2 a, out Vector2 b)
    {
        a = b = default;
        if (edge.output == null || edge.input == null || edge.output.panel == null || edge.input.panel == null)
            return false;
        a = this.WorldToLocal(edge.output.GetGlobalCenter());
        b = this.WorldToLocal(edge.input.GetGlobalCenter());
        return !float.IsNaN(a.x) && !float.IsNaN(a.y) && !float.IsNaN(b.x) && !float.IsNaN(b.y);
    }

    // A point along a wire, following the S-shaped curve the canvas draws between two side-facing ports.
    private static Vector2 PointOn(Vector2 a, Vector2 b, float t, float zoom)
    {
        float tangent = Mathf.Max(Mathf.Abs(b.x - a.x) * 0.5f, 30f * zoom);
        var c1 = a + new Vector2(tangent, 0f);
        var c2 = b - new Vector2(tangent, 0f);
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * b;
    }

    #endregion

    #region Balls

    private void Launch(GraphViewEdge edge, Color color, double now)
    {
        if (edge == null || edge.panel == null)
            return;
        if (_lastBallAt.TryGetValue(edge, out var last) && now - last < BallGap)
            return;
        _lastBallAt[edge] = now;

        Ball ball = null;
        for (int i = 0; i < _balls.Count; i++)
            if (!_balls[i].Active) { ball = _balls[i]; break; }
        if (ball == null)
        {
            if (_balls.Count >= MaxBalls)
                return;
            ball = new Ball { Element = CreateBall() };
            _balls.Add(ball);
        }
        ball.Edge = edge;
        ball.Start = now;
        ball.Active = true;
        ball.Element.style.backgroundColor = color;
        ball.Element.style.display = DisplayStyle.None;   // shown at its first spot on the next tick
    }

    private VisualElement CreateBall()
    {
        var element = new VisualElement { pickingMode = PickingMode.Ignore };
        element.style.position = Position.Absolute;
        element.style.left = element.style.top = 0;
        element.style.width = element.style.height = BallSize;
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = BallSize * 0.5f;
        element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = 1;
        var rim = new Color(0f, 0f, 0f, 0.6f);
        element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = rim;
        element.style.display = DisplayStyle.None;
        _ballLayer.Add(element);
        return element;
    }

    #endregion

    #region Following an object

    // The selected object (or the one it's part of) when it runs this graph in the scene; otherwise everyone.
    private GameObject FindInspected()
    {
        var selected = Selection.activeGameObject;
        if (selected == null || EditorUtility.IsPersistent(selected))
            return null;
        var schematic = selected.GetComponentInParent<SchematicObject>(true);
        if (schematic == null || schematic.SchematicGraph != _window.SchematicGraph)
            return null;
        return schematic.gameObject;
    }

    #endregion

    #region Formatting

    /// <summary>A value, short enough to sit above a wire.</summary>
    internal static string Format(Union value)
    {
        string text;
        try
        {
            var v = value.GetValue<Vector4>();
            text = value.Type switch
            {
                Union.ValueType.Null => "null",
                Union.ValueType.Bool => v.x != 0f ? "true" : "false",
                Union.ValueType.Int => ((int)v.x).ToString(CultureInfo.InvariantCulture),
                Union.ValueType.Float => Number(v.x),
                Union.ValueType.Vector2 => $"({Number(v.x)}, {Number(v.y)})",
                Union.ValueType.Vector3 => $"({Number(v.x)}, {Number(v.y)}, {Number(v.z)})",
                Union.ValueType.Vector4 => $"({Number(v.x)}, {Number(v.y)}, {Number(v.z)}, {Number(v.w)})",
                Union.ValueType.Color or Union.ValueType.Color32 => "#" + ColorUtility.ToHtmlStringRGBA(new Color(v.x, v.y, v.z, v.w)),
                Union.ValueType.Quaternion => Euler(new Quaternion(v.x, v.y, v.z, v.w)),
                Union.ValueType.LayerMask => $"Layers {(int)v.x}",
                Union.ValueType.String => "\"" + value.GetValue<string>() + "\"",
                _ => Describe(value.GetValue<object>()),
            };
        }
        catch (Exception)
        {
            text = "?";
        }
        if (text != null && text.Length > MaxLabelLength)
            text = text.Substring(0, MaxLabelLength - 1) + "…";
        return text;
    }

    private static string Number(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? value.ToString(CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Euler(Quaternion rotation)
    {
        if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
            return "(0, 0, 0)°";
        var euler = rotation.eulerAngles;
        return $"({Number(euler.x)}, {Number(euler.y)}, {Number(euler.z)})°";
    }

    private static string Describe(object value)
    {
        switch (value)
        {
            case null: return "null";
            case UnityEngine.Object unityObject: return unityObject != null ? unityObject.name : "None";
            case string text: return "\"" + text + "\"";
            case ICollection collection: return $"[{collection.Count}]";
            default: return value.ToString();
        }
    }

    #endregion
}
