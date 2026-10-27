using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A minimap of the Schematic Graph, in the canvas' corner. Signal nodes show as arrows (sending or receiving),
/// Variable nodes as dots in their type's color, Behavior nodes as script icons, and comments as boxes in their own
/// color with their name in small text in the middle; hover a comment's box and its name grows to be easy to read.
/// Hover a node to see its name. Click a node or comment to glide to it, or anywhere else to glide there; drag to
/// steer the view. Toggle it with the button above it.
/// </summary>
/// <remarks>
/// Layers, bottom to top: the comment boxes (UI Toolkit elements), the nodes and the view outline (drawn), then the
/// comment names (UI Toolkit labels, so their hover growth can be a smooth style transition). Only the drawn layer
/// takes the mouse, so clicks and drags always reach the navigation.
/// </remarks>
public class SchematicMinimap : VisualElement
{
    private const float Width = 220f, Height = 150f, Padding = 6f;
    private const float LabelSize = 8f, HoverScale = 1.75f;

    private readonly CanvasView _canvas;
    private readonly CanvasMotion _motion;
    private readonly VisualElement _frame, _commentLayer, _labelLayer;
    private readonly IMGUIContainer _drawing;
    private Rect _contentBounds;
    private bool _dragging;

    private static Texture _behavior;
    private static Texture BehaviorIcon => _behavior ??= EditorGUIUtility.IconContent("cs Script Icon").image;

    private const string VisibleKey = "SchematicMinimap.Visible";
    private const float Margin = 8f;

    // Read-only graph tiles use the minimap's own rendering and hit-test rules. The live CanvasView path below keeps
    // its own viewport-aware map, while both paths use the same layers: comments, orthogonal wires, notes, nodes.
    public readonly struct OverviewHit
    {
        public readonly string Label;
        public readonly Rect Bounds;
        public OverviewHit(string label, Rect bounds) { Label = label; Bounds = bounds; }
    }

    public static Texture2D RenderOverview(SchematicGraph graph, int size = 128)
    {
        var bg = EditorGUIUtility.isProSkin ? new Color32(25, 28, 34, 255) : new Color32(232, 235, 240, 255);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
        var nodes = graph?.Nodes;
        if (nodes == null || nodes.Count == 0) return MakeOverviewTexture(pixels, size);
        var bounds = PreviewBounds(graph, nodes);
        var area = new Rect(size * 0.09f, size * 0.09f, size * 0.82f, size * 0.82f);
        FitPreview(bounds, area, out float scale, out Vector2 offset);
        Rect Map(Rect rect) => MapPreviewRect(rect, bounds, scale, offset);

        foreach (var comment in graph.EditorComments)
            BlendPreviewRect(pixels, size, Map(comment.Region), comment.Color, 0.22f);

        var byId = new Dictionary<string, BlueGraph.Node>();
        foreach (var node in nodes) byId[node.ID] = node;
        foreach (var source in nodes)
            foreach (var port in source.Ports.Values)
                foreach (var connection in port.Connections)
                    if (connection != null && byId.TryGetValue(connection.NodeID, out var target))
                    {
                        var a = Map(new Rect(source.Position, new Vector2(180, 90))).center;
                        var b = Map(new Rect(target.Position, new Vector2(180, 90))).center;
                        int ax = Mathf.RoundToInt(a.x), ay = Mathf.RoundToInt(a.y), bx = Mathf.RoundToInt(b.x), by = Mathf.RoundToInt(b.y);
                        int mid = (ax + bx) / 2;
                        var wire = new Color32(158, 184, 214, 184);
                        DrawPreviewLine(pixels, size, ax, ay, mid, ay, wire);
                        DrawPreviewLine(pixels, size, mid, ay, mid, by, wire);
                        DrawPreviewLine(pixels, size, mid, by, bx, by, wire);
                    }
        foreach (var note in graph.EditorNotes)
            FillPreviewRect(pixels, size, Map(note.Region), note.Color);
        foreach (var node in nodes)
        {
            var rect = Map(new Rect(node.Position, new Vector2(180, 90)));
            GetNodeMark(node, out var icon, out _, out _);
            if (icon == null)
                BlendPreviewRect(pixels, size, rect, HeaderColor(node), 0.75f);
        }
        return MakeOverviewTexture(pixels, size);
    }

    public static OverviewHit? OverviewHitAt(SchematicGraph graph, Vector2 point, int size = 128)
    {
        var nodes = graph?.Nodes;
        if (nodes == null || nodes.Count == 0) return null;
        var bounds = PreviewBounds(graph, nodes);
        var area = new Rect(size * 0.09f, size * 0.09f, size * 0.82f, size * 0.82f);
        FitPreview(bounds, area, out float scale, out Vector2 offset);
        var p = new Vector2(point.x, size - point.y);
        foreach (var node in nodes)
        {
            var rect = MapPreviewRect(new Rect(node.Position, new Vector2(180, 90)), bounds, scale, offset);
            if (Inflate(rect, 4f).Contains(p)) return new OverviewHit($"{node.DisplayName} · {node.GetType().Name}", rect);
        }
        foreach (var note in graph.EditorNotes)
        {
            var rect = MapPreviewRect(note.Region, bounds, scale, offset);
            if (rect.Contains(p)) return new OverviewHit(FirstWords(note.Text, 3), rect);
        }
        foreach (var comment in graph.EditorComments)
        {
            var rect = MapPreviewRect(comment.Region, bounds, scale, offset);
            if (rect.Contains(p)) return new OverviewHit(FirstWords(comment.Text, 3), rect);
        }
        return null;
    }

    /// <summary>Returns a zoom factor that gives nodes a consistent relative width in the preview viewport.</summary>
    public static float RecommendedPreviewZoom(SchematicGraph graph, int size = 256)
    {
        var nodes = graph?.Nodes;
        if (nodes == null || nodes.Count == 0) return 2.5f;
        var bounds = PreviewBounds(graph, nodes);
        var area = new Rect(size * 0.09f, size * 0.09f, size * 0.82f, size * 0.82f);
        FitPreview(bounds, area, out float scale, out _);
        float nodeWidth = 180f * scale;
        return Mathf.Clamp(size * 0.24f / Mathf.Max(1f, nodeWidth), 1f, 8f);
    }

    /// <summary>Draws the same semantic node marks as the live minimap over a zoomed graph preview.</summary>
    public static void DrawOverviewMarks(SchematicGraph graph, Rect viewport, Vector2 zoomCenter, float zoom, int size = 256)
    {
        var nodes = graph?.Nodes;
        if (nodes == null || nodes.Count == 0 || viewport.width <= 0 || viewport.height <= 0) return;
        var bounds = PreviewBounds(graph, nodes);
        var area = new Rect(size * 0.09f, size * 0.09f, size * 0.82f, size * 0.82f);
        FitPreview(bounds, area, out float mapScale, out Vector2 offset);
        foreach (var node in nodes)
        {
            GetNodeMark(node, out var icon, out var tint, out float iconSize);
            if (icon == null) continue;
            var mapRect = MapPreviewRect(new Rect(node.Position, new Vector2(180, 90)), bounds, mapScale, offset);
            var center = mapRect.center;
            float x = viewport.center.x + (center.x / size - zoomCenter.x) * zoom * viewport.width;
            float y = viewport.center.y + (1f - center.y / size - zoomCenter.y) * zoom * viewport.height;
            float pixels = Mathf.Clamp(iconSize * viewport.width / Width * zoom, 3f, 16f);
            var rect = new Rect(x - pixels * 0.5f, y - pixels * 0.5f, pixels, pixels);
            if (rect.xMin < viewport.xMin || rect.xMax > viewport.xMax || rect.yMin < viewport.yMin || rect.yMax > viewport.yMax)
                continue;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
            GUI.color = old;
        }
    }

    private static void GetNodeMark(BlueGraph.Node target, out Texture icon, out Color tint, out float size)
    {
        icon = null;
        tint = Color.white;
        size = 12f;
        switch (target)
        {
            case SendSignalNode _: icon = DockRows.SignalIcon; tint = DockRows.SignalColor(output: false); break;
            case OnSignalReceivedNode _: icon = DockRows.SignalInIcon; tint = DockRows.SignalColor(output: true); break;
            case GetBehaviorVariableNode get:
                icon = PrefabHierarchy.VariableIcon;
                tint = PrefabHierarchy.ColorOf(BehaviorReflection.Get(get.Target.Type)?.FindVariableType(get.Variable));
                size = 6f; break;
            case SetBehaviorVariableNode set:
                icon = PrefabHierarchy.VariableIcon;
                tint = PrefabHierarchy.ColorOf(BehaviorReflection.Get(set.Target.Type)?.FindVariableType(set.Variable));
                size = 6f; break;
            case GraphVariableNodeBase graphVariable:
                icon = DockRows.GraphVariableIcon;
                tint = DockRows.ColorOf(graphVariable.Definition?.ValueType);
                size = 6f; break;
            case BehaviorReferenceNode _: icon = BehaviorIcon; break;
        }
    }

    private static Color HeaderColor(BlueGraph.Node node)
        => node?.GetType().GetCustomAttribute<NodeColorAttribute>(true)?.Color ?? new Color32(119, 170, 255, 255);

    private static Rect PreviewBounds(SchematicGraph graph, IReadOnlyList<BlueGraph.Node> nodes)
    {
        var bounds = new Rect(nodes[0].Position, new Vector2(180, 90));
        foreach (var node in nodes) bounds = Encapsulate(bounds, new Rect(node.Position, new Vector2(180, 90)));
        foreach (var comment in graph.EditorComments) bounds = Encapsulate(bounds, comment.Region);
        foreach (var note in graph.EditorNotes) bounds = Encapsulate(bounds, note.Region);
        return Inflate(bounds, 40f);
    }

    private static void FitPreview(Rect bounds, Rect area, out float scale, out Vector2 offset)
    {
        scale = Mathf.Min(area.width / Mathf.Max(1f, bounds.width), area.height / Mathf.Max(1f, bounds.height));
        offset = new Vector2(area.x + (area.width - bounds.width * scale) * 0.5f, area.y + (area.height - bounds.height * scale) * 0.5f);
    }

    private static Rect MapPreviewRect(Rect rect, Rect bounds, float scale, Vector2 offset)
        => new Rect(offset + (rect.position - bounds.position) * scale, rect.size * scale);

    private static Texture2D MakeOverviewTexture(Color32[] pixels, int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "Schematic Minimap Preview", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels); texture.Apply(false, true); return texture;
    }

    private static void FillPreviewRect(Color32[] pixels, int size, Rect rect, Color color)
    {
        int x0 = Mathf.FloorToInt(rect.xMin), x1 = Mathf.CeilToInt(rect.xMax);
        int y0 = Mathf.FloorToInt(rect.yMin), y1 = Mathf.CeilToInt(rect.yMax);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            if ((uint)x < size && (uint)y < size) pixels[y * size + x] = color;
    }

    private static void BlendPreviewRect(Color32[] pixels, int size, Rect rect, Color color, float alpha)
    {
        int x0 = Mathf.FloorToInt(rect.xMin), x1 = Mathf.CeilToInt(rect.xMax);
        int y0 = Mathf.FloorToInt(rect.yMin), y1 = Mathf.CeilToInt(rect.yMax);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            if ((uint)x < size && (uint)y < size)
            {
                int i = y * size + x;
                pixels[i] = Color32.Lerp(pixels[i], color, alpha);
            }
    }

    private static void DrawPreviewLine(Color32[] pixels, int size, int x0, int y0, int x1, int y1, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            if ((uint)x0 < size && (uint)y0 < size)
            {
                int i = y0 * size + x0;
                pixels[i] = Color32.Lerp(pixels[i], color, color.a / 255f);
            }
            if (x0 == x1 && y0 == y1) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }

    /// <summary>The row of buttons above the map (Map, and any the window adds in front of it).</summary>
    public VisualElement Buttons { get; }

    /// <summary>
    /// Keeps the minimap (and its buttons) above something along the bottom of the canvas, like the debugger's panel
    /// while a flow is stopped or an error is showing: <paramref name="height"/> is how tall it is (0 when it's hidden).
    /// </summary>
    public void SetBottomInset(float height)
    {
        if (float.IsNaN(height) || height < 0f) height = 0f;
        float bottom = Margin + height;
        if (Mathf.Approximately(style.bottom.value.value, bottom)) return;
        style.bottom = bottom;
    }

    public SchematicMinimap(CanvasView canvas, CanvasMotion motion)
    {
        _canvas = canvas;
        _motion = motion;
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.right = Margin;
        style.bottom = Margin;
        style.alignItems = Align.FlexEnd;
        // Slides up and down smoothly when something along the bottom of the graph (the debugger's panel) comes and goes.
        style.transitionProperty = new List<StylePropertyName> { "bottom" };
        style.transitionDuration = new List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
        style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };

        // The graph's little row of buttons, above the map: others (the flow visualizer's toggle) go in front of Map.
        Buttons = new VisualElement { pickingMode = PickingMode.Ignore };
        Buttons.style.flexDirection = FlexDirection.Row;
        Buttons.style.alignSelf = Align.FlexEnd;
        Add(Buttons);

        var toggle = new Button(() => SetVisible(!_visible)) { text = "Map", tooltip = "Show or hide the minimap" };
        Buttons.Add(toggle);

        _frame = new VisualElement { pickingMode = PickingMode.Ignore };
        _frame.style.width = Width;
        _frame.style.height = Height;
        _frame.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        _frame.style.borderTopLeftRadius = _frame.style.borderTopRightRadius =
            _frame.style.borderBottomLeftRadius = _frame.style.borderBottomRightRadius = 4;
        Add(_frame);

        _commentLayer = Layer();
        _frame.Add(_commentLayer);

        _drawing = new IMGUIContainer(Draw);
        _drawing.style.position = Position.Absolute;
        _drawing.style.left = _drawing.style.top = _drawing.style.right = _drawing.style.bottom = 0;
        _frame.Add(_drawing);

        _labelLayer = Layer();
        _frame.Add(_labelLayer);

        _drawing.RegisterCallback<MouseDownEvent>(evt => { _dragging = true; Click(evt.localMousePosition); evt.StopPropagation(); });
        _drawing.RegisterCallback<MouseMoveEvent>(evt =>
        {
            SetHovered(CommentAt(evt.localMousePosition));
            if (_dragging) { Pan(evt.localMousePosition); evt.StopPropagation(); }
        });
        _drawing.RegisterCallback<MouseUpEvent>(evt => { _dragging = false; evt.StopPropagation(); });
        _drawing.RegisterCallback<MouseLeaveEvent>(_ => { _dragging = false; SetHovered(null); });
        _drawing.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());

        SetVisible(EditorPrefs.GetBool(VisibleKey, true));

        // Follow the view as it moves and the graph as it changes.
        schedule.Execute(() =>
        {
            if (!_visible) return;
            UpdateMap();
            _drawing.MarkDirtyRepaint();
        }).Every(33);
    }

    private static VisualElement Layer()
    {
        var layer = new VisualElement { pickingMode = PickingMode.Ignore };
        layer.style.position = Position.Absolute;
        layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
        return layer;
    }

    private bool _visible;

    private void SetVisible(bool visible)
    {
        _visible = visible;
        _frame.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        EditorPrefs.SetBool(VisibleKey, visible);
    }

    private List<NodeView> Nodes() => _canvas.Query<NodeView>().ToList();

    private Rect ToMap(Rect r) => MapPreviewRect(r, _contentBounds, _mapScale, _mapOffset);

    /// <summary>
    /// Fits every node, comment and the current view into the map, and lays the comment boxes and names out to match.
    /// </summary>
    private void UpdateMap()
    {
        var area = new Rect(Padding, Padding, Width - Padding * 2, Height - Padding * 2);
        var viewport = _canvas.contentViewContainer.WorldToLocal(_canvas.worldBound);
        var comments = _canvas.Query<CommentView>().ToList();

        Rect bounds = viewport;
        foreach (var node in Nodes())
            bounds = Encapsulate(bounds, node.GetPosition());
        foreach (var comment in comments)
            bounds = Encapsulate(bounds, comment.GetPosition());
        foreach (var note in _canvas.Query<NoteView>().ToList())
            bounds = Encapsulate(bounds, note.GetPosition());
        bounds = Inflate(bounds, 40f);
        if (bounds.width <= 0f || bounds.height <= 0f)
            return;
        _contentBounds = bounds;

        FitPreview(bounds, area, out _mapScale, out _mapOffset);

        _commentHits.Clear();
        for (int i = 0; i < comments.Count; i++)
        {
            var comment = comments[i];
            var region = ToMap(comment.GetPosition());
            _commentHits.Add((region, comment.GetPosition(), comment));
            if (i >= _commentMarks.Count)
                _commentMarks.Add(new CommentMark(_commentLayer, _labelLayer));
            _commentMarks[i].Show(comment, region, comment == _hovered);
        }
        for (int i = comments.Count; i < _commentMarks.Count; i++)
            _commentMarks[i].Hide();

        if (_hovered != null && !comments.Contains(_hovered))
            _hovered = null;
    }

    private void Draw()
    {
        if (_mapScale <= 0f) return;
        var viewport = _canvas.contentViewContainer.WorldToLocal(_canvas.worldBound);

        string hover = null;
        var mouse = Event.current.mousePosition;

        // The comments sit underneath, as UI Toolkit boxes (see CommentMark); their names are drawn on top. Notes are
        // solid boxes in their color, at their size.
        // Draw connected ports before nodes so wires sit behind the node marks, as they do on the graph canvas.
        Handles.color = new Color(0.62f, 0.72f, 0.84f, 0.72f);
        foreach (var edge in _canvas.edges)
        {
            if (edge?.output?.node is not NodeView source || edge.input?.node is not NodeView target)
                continue;
            var from = ToMap(source.GetPosition()).center;
            var to = ToMap(target.GetPosition()).center;
            Handles.DrawAAPolyLine(1.25f, from, new Vector2((from.x + to.x) * 0.5f, from.y), new Vector2((from.x + to.x) * 0.5f, to.y), to);
        }
        foreach (var note in _canvas.Query<NoteView>().ToList())
        {
            var rect = ToMap(note.GetPosition());
            var noteColor = note.Target.Color;
            EditorGUI.DrawRect(rect, _canvas.selection.Contains(note) ? new Color(0.3f, 0.6f, 0.9f, 1f) : noteColor);
        }

        _nodeHits.Clear();
        foreach (var node in Nodes())
        {
            var target = node.Target;
            var rect = ToMap(node.GetPosition());
            _nodeHits.Add((rect, node.GetPosition()));
            bool selected = _canvas.selection.Contains(node);
            GetNodeMark(target, out var icon, out var tint, out float size);
            if (selected)
                EditorGUI.DrawRect(rect, new Color(0.3f, 0.6f, 0.9f, 0.9f));
            else if (icon == null)
            {
                var header = HeaderColor(target);
                EditorGUI.DrawRect(rect, new Color(header.r, header.g, header.b, 0.75f));
            }

            if (icon != null)
            {
                var iconRect = new Rect(rect.center.x - size * 0.5f, rect.center.y - size * 0.5f, size, size);
                var previous = GUI.color;
                GUI.color = tint;
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                GUI.color = previous;
                if (Inflate(iconRect, 2f).Contains(mouse))
                    hover = target.Name;
            }
            else if (rect.Contains(mouse) && target != null)
                hover = target.Name;
        }

        // Each note's pencil, on top (at a size that reads, however small the note is on the map). Hovered, or in the
        // comment hovered, its first few words instead: laid out clear of the comment's name and of each other.
        var named = new List<(Rect map, BlueGraph.Note note)>();
        _noteHits.Clear();
        foreach (var note in _canvas.Query<NoteView>().ToList())
        {
            var rect = ToMap(note.GetPosition());
            _noteHits.Add((Inflate(NoteIconRect(rect), 2f), note.GetPosition()));
            bool inHovered = _hovered != null && _hovered.GetPosition().Contains(note.GetPosition().center);
            if (inHovered || NoteHovered(rect, mouse))
                named.Add((rect, note.Target));
            else
                DrawNotePencil(rect, note.Target);
        }
        if (named.Count > 0)
        {
            var taken = new List<Rect>();
            var commentName = HoveredCommentNameRect();
            if (commentName.width > 0f)
                taken.Add(Inflate(commentName, 1f));
            named.Sort((a, b) => a.map.center.y.CompareTo(b.map.center.y));
            foreach (var (map, note) in named)
                _noteHits.Add((DrawNoteName(map, note, taken), note.Region));
        }

        // The visible part of the graph.
        var view = ToMap(viewport);
        DrawOutline(view, new Color(1f, 1f, 1f, 0.8f));

        if (hover != null)
        {
            var content = new GUIContent(hover);
            var size = EditorStyles.miniLabel.CalcSize(content);
            var labelRect = new Rect(Mathf.Clamp(mouse.x + 8, 0, Width - size.x - 4), Mathf.Max(0, mouse.y - size.y - 2), size.x + 4, size.y);
            EditorGUI.DrawRect(labelRect, new Color(0, 0, 0, 0.8f));
            GUI.Label(labelRect, content, EditorStyles.miniLabel);
        }
    }

    private static Texture2D _noteIcon;
    // Notes' mark: a pencil (white, tinted where it's drawn).
    private static Texture2D NoteIcon => _noteIcon != null ? _noteIcon : _noteIcon = Resources.Load<Texture2D>("Textures/note-pencil");

    private const float NoteIconSize = 10f;

    private static Rect NoteIconRect(Rect rect)
        => new Rect(rect.center.x - NoteIconSize * 0.5f, rect.center.y - NoteIconSize * 0.5f, NoteIconSize, NoteIconSize);

    private static bool NoteHovered(Rect rect, Vector2 mouse) => rect.Contains(mouse) || Inflate(NoteIconRect(rect), 2f).Contains(mouse);

    // A note's mark on the map: its pencil (white, as notes' text is) at its middle.
    private static void DrawNotePencil(Rect rect, BlueGraph.Note note)
    {
        if (NoteIcon == null)
            return;
        var previous = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(NoteIconRect(rect), NoteIcon, ScaleMode.ScaleToFit);
        GUI.color = previous;
    }

    private static GUIStyle _noteNameStyle;

    // A note's first three words on a patch of its colour, at the note, or moved (down, else up) clear of the names
    // already placed (<paramref name="taken"/>), with a thin line back to the note when moved.
    private Rect DrawNoteName(Rect rect, BlueGraph.Note note, List<Rect> taken)
    {
        _noteNameStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = Color.white }, alignment = TextAnchor.MiddleCenter };
        var content = new GUIContent(FirstWords(note.Text, 3));
        var size = _noteNameStyle.CalcSize(content);
        var label = new Rect(Mathf.Clamp(rect.center.x - size.x * 0.5f - 3f, 0f, Mathf.Max(0f, Width - size.x - 6f)),
            rect.center.y - size.y * 0.5f, size.x + 6f, size.y);
        label = Clear(label, taken);
        taken.Add(Inflate(label, 1f));

        if (Mathf.Abs(label.center.y - rect.center.y) > 1f || Mathf.Abs(label.center.x - rect.center.x) > 1f)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.6f);
            var to = new Vector2(label.center.x, label.center.y > rect.center.y ? label.yMin : label.yMax);
            Handles.DrawLine(rect.center, to);
            DrawNotePencil(rect, note);
        }
        EditorGUI.DrawRect(label, note.Color);
        GUI.Label(label, content, _noteNameStyle);
        return label;
    }

    // A label moved down (or, past the bottom, up) until it no longer overlaps any already placed.
    private static Rect Clear(Rect label, List<Rect> taken)
    {
        var start = label;
        for (int pass = 0; pass < 2; pass++)
        {
            label = start;
            for (int i = 0; i < 40; i++)
            {
                bool moved = false;
                foreach (var other in taken)
                    if (label.Overlaps(other))
                    {
                        label.y = pass == 0 ? other.yMax : other.yMin - label.height;
                        moved = true;
                    }
                if (!moved)
                    break;
            }
            if (label.yMin >= 0f && label.yMax <= Height)
                return label;
        }
        label.y = Mathf.Clamp(label.y, 0f, Height - label.height);
        return label;
    }

    // Where the hovered comment's name is drawn on the map (its label, scaled up), or nothing.
    private Rect HoveredCommentNameRect()
    {
        if (_hovered == null)
            return default;
        foreach (var mark in _commentMarks)
            if (mark.Comment == _hovered)
                return mark.NameRect(_frame);
        return default;
    }

    // The first few words of a note ("..." when there are more); "Note" when it has none.
    private static string FirstWords(string text, int count)
    {
        var words = (text ?? "").Split(new[] { ' ', '\n', '\r', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return "Note";
        return string.Join(" ", words, 0, Mathf.Min(count, words.Length)) + (words.Length > count ? "..." : "");
    }

    // The comment under the mouse: the innermost one, when comments sit inside each other.
    private CommentView CommentAt(Vector2 local)
    {
        CommentView best = null;
        float bestArea = float.MaxValue;
        foreach (var hit in _commentHits)
        {
            float area = hit.map.width * hit.map.height;
            if (hit.map.Contains(local) && area < bestArea)
            {
                best = hit.comment;
                bestArea = area;
            }
        }
        return best;
    }

    private CommentView _hovered;

    private void SetHovered(CommentView comment)
    {
        if (comment == _hovered) return;
        _hovered = comment;
        for (int i = 0; i < _commentMarks.Count; i++)
            _commentMarks[i].SetHovered(_commentMarks[i].Comment != null && _commentMarks[i].Comment == comment);
    }

    /// <summary>
    /// One comment on the map: a box in the comment's color (under the nodes) and its name centered over it (above
    /// them). Hovering the box grows the name smoothly and gives it a backing in the comment's color, so it can be
    /// read even when the box is tiny or the name is long.
    /// </summary>
    private sealed class CommentMark
    {
        private readonly VisualElement _box, _labelBox;
        private readonly Label _label;
        private bool _hovered;
        private Color _color;
        private float _boxWidth;
        public CommentView Comment { get; private set; }

        public CommentMark(VisualElement boxLayer, VisualElement labelLayer)
        {
            _box = new VisualElement { pickingMode = PickingMode.Ignore };
            _box.style.position = Position.Absolute;
            _box.style.borderTopWidth = _box.style.borderBottomWidth = _box.style.borderLeftWidth = _box.style.borderRightWidth = 1;
            _box.style.borderTopLeftRadius = _box.style.borderTopRightRadius =
                _box.style.borderBottomLeftRadius = _box.style.borderBottomRightRadius = 2;
            boxLayer.Add(_box);

            // Same rect as the box, centering the name in it; the name may spill out of it (when hovered).
            _labelBox = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelBox.style.position = Position.Absolute;
            _labelBox.style.justifyContent = Justify.Center;
            _labelBox.style.alignItems = Align.Center;
            labelLayer.Add(_labelBox);

            _label = new Label { pickingMode = PickingMode.Ignore };
            _label.style.fontSize = LabelSize;
            _label.style.unityTextAlign = TextAnchor.MiddleCenter;
            _label.style.whiteSpace = WhiteSpace.NoWrap;
            _label.style.overflow = Overflow.Hidden;
            _label.style.textOverflow = TextOverflow.Ellipsis;
            _label.style.flexShrink = 0;
            _label.style.marginLeft = _label.style.marginRight = _label.style.marginTop = _label.style.marginBottom = 0;
            _label.style.paddingLeft = _label.style.paddingRight = 2;
            _label.style.paddingTop = _label.style.paddingBottom = 0;
            _label.style.borderTopLeftRadius = _label.style.borderTopRightRadius =
                _label.style.borderBottomLeftRadius = _label.style.borderBottomRightRadius = 2;
            _label.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color", "color" };
            _label.style.transitionDuration = new List<TimeValue> { new TimeValue(0.15f, TimeUnit.Second) };
            _label.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) };
            _labelBox.Add(_label);
        }

        public void Show(CommentView comment, Rect region, bool hovered)
        {
            var color = comment.Target.Color;
            bool changed = Comment != comment || color != _color;
            Comment = comment;
            _color = color;
            _boxWidth = region.width;

            _box.style.display = DisplayStyle.Flex;
            _box.style.left = region.x;
            _box.style.top = region.y;
            _box.style.width = region.width;
            _box.style.height = region.height;
            _box.style.backgroundColor = new Color(_color.r, _color.g, _color.b, 0.22f);
            var border = comment.selected ? new Color(1f, 1f, 1f, 0.9f) : new Color(_color.r, _color.g, _color.b, 0.85f);
            _box.style.borderTopColor = _box.style.borderBottomColor = _box.style.borderLeftColor = _box.style.borderRightColor = border;

            _labelBox.style.display = DisplayStyle.Flex;
            _labelBox.style.left = region.x;
            _labelBox.style.top = region.y;
            _labelBox.style.width = region.width;
            _labelBox.style.height = region.height;
            if (_label.text != comment.Target.Text)
                _label.text = comment.Target.Text;

            if (changed)
                _hovered = !hovered; // a different comment or color: restyle fully
            SetHovered(hovered);
        }

        public void SetHovered(bool hovered)
        {
            if (hovered == _hovered && Comment != null)
            {
                // Width can still change while the box is resized.
                if (!hovered) _label.style.maxWidth = Mathf.Max(0f, _boxWidth - 2f);
                return;
            }
            _hovered = hovered;

            // A name too squeezed to read stays hidden until its box is hovered.
            _label.style.visibility = hovered || _boxWidth >= 14f ? Visibility.Visible : Visibility.Hidden;

            if (hovered)
            {
                _labelBox.BringToFront();
                _label.style.maxWidth = StyleKeyword.None;
                _label.style.scale = new Scale(new Vector3(HoverScale, HoverScale, 1f));
                _label.style.backgroundColor = new Color(_color.r * 0.8f, _color.g * 0.8f, _color.b * 0.8f, 0.95f);
                float luminance = 0.2126f * _color.r + 0.7152f * _color.g + 0.0722f * _color.b;
                _label.style.color = luminance * 0.8f > 0.5f ? Color.black : Color.white;
            }
            else
            {
                _label.style.maxWidth = Mathf.Max(0f, _boxWidth - 2f);
                _label.style.scale = new Scale(Vector3.one);
                _label.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                // The comment's color, lightened, so the name reads on the dark map and still matches its box.
                _label.style.color = Color.Lerp(_color, Color.white, 0.55f);
            }
        }

        /// <summary>Where its name is drawn, in <paramref name="space"/>'s coordinates (scaled up when hovered).</summary>
        public Rect NameRect(VisualElement space)
        {
            var bound = _label.worldBound;
            if (float.IsNaN(bound.width) || bound.width <= 0f)
                return default;
            return space.WorldToLocal(bound);
        }

        public void Hide()
        {
            Comment = null;
            _box.style.display = DisplayStyle.None;
            _labelBox.style.display = DisplayStyle.None;
        }
    }

    private float _mapScale = 1f;
    private Vector2 _mapOffset;
    // What was drawn where last time (minimap rect, graph rect), so clicks can find it.
    private readonly List<(Rect map, Rect graph)> _nodeHits = new();
    // Notes' pencils and shown names on the map, and the notes they're for (clicked: the view frames the note).
    private readonly List<(Rect map, Rect graph)> _noteHits = new();
    private readonly List<(Rect map, Rect graph, CommentView comment)> _commentHits = new();
    private readonly List<CommentMark> _commentMarks = new();

    private Vector2 ToGraph(Vector2 local) => _contentBounds.position + (local - _mapOffset) / _mapScale;

    // A click on a note (its pencil or name) frames it, on a node glides to it, on a comment frames the whole comment,
    // anywhere else glides to that point.
    private void Click(Vector2 local)
    {
        if (_mapScale <= 0f) return;

        // A note's pencil or name: to the note (names are drawn over everything, so they win).
        for (int i = _noteHits.Count - 1; i >= 0; i--)
        {
            if (_noteHits[i].map.Contains(local))
            {
                _motion.Frame(_noteHits[i].graph);
                return;
            }
        }
        // Nodes win over the comments around them.
        for (int i = _nodeHits.Count - 1; i >= 0; i--)
        {
            if (_nodeHits[i].map.Contains(local))
            {
                _motion.CenterOn(_nodeHits[i].graph.center);
                return;
            }
        }
        var comment = CommentAt(local);
        if (comment != null)
        {
            _motion.Frame(comment.GetPosition());
            return;
        }
        _motion.CenterOn(ToGraph(local));
    }

    // Dragging steers the view smoothly towards the point under the cursor.
    private void Pan(Vector2 local)
    {
        if (_mapScale <= 0f) return;
        _motion.CenterOn(ToGraph(local));
    }

    private static Rect Encapsulate(Rect a, Rect b) => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    private static Rect Inflate(Rect r, float by) => new Rect(r.x - by, r.y - by, r.width + by * 2, r.height + by * 2);

    private static void DrawOutline(Rect r, Color color)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), color);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), color);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), color);
        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), color);
    }
}
