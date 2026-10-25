using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.Collections.Generic;
using UnityEditor;
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

    private static Texture2D _input, _output;
    private static Texture _behavior;
    private static Texture2D InputIcon => _input ??= Resources.Load<Texture2D>("Icons/Input");
    private static Texture2D OutputIcon => _output ??= Resources.Load<Texture2D>("Icons/Output");
    private static Texture BehaviorIcon => _behavior ??= EditorGUIUtility.IconContent("cs Script Icon").image;

    private const string VisibleKey = "SchematicMinimap.Visible";
    private const float Margin = 8f;

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

    private Rect ToMap(Rect r) => new Rect(_mapOffset + (r.position - _contentBounds.position) * _mapScale, r.size * _mapScale);

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

        _mapScale = Mathf.Min(area.width / bounds.width, area.height / bounds.height);
        _mapOffset = new Vector2(area.x + (area.width - bounds.width * _mapScale) * 0.5f, area.y + (area.height - bounds.height * _mapScale) * 0.5f);

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
        foreach (var note in _canvas.Query<NoteView>().ToList())
        {
            var rect = ToMap(note.GetPosition());
            var noteColor = note.Target.Color;
            EditorGUI.DrawRect(rect, _canvas.selection.Contains(note) ? new Color(0.3f, 0.6f, 0.9f, 1f) : noteColor);
            // A pencil on it (dark on light colors, light on dark), as big as fits.
            float pencil = Mathf.Min(12f, rect.width - 2f, rect.height - 2f);
            if (NoteIcon != null && pencil >= 4f)
            {
                var previous = GUI.color;
                float luminance = 0.2126f * noteColor.r + 0.7152f * noteColor.g + 0.0722f * noteColor.b;
                GUI.color = luminance > 0.5f ? new Color(0.1f, 0.1f, 0.1f, 0.85f) : new Color(1f, 1f, 1f, 0.9f);
                GUI.DrawTexture(new Rect(rect.center.x - pencil * 0.5f, rect.center.y - pencil * 0.5f, pencil, pencil), NoteIcon, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }
            if (rect.Contains(mouse) && !string.IsNullOrEmpty(note.Target.Text))
                hover = FirstLine(note.Target.Text);
        }

        _nodeHits.Clear();
        foreach (var node in Nodes())
        {
            var target = node.Target;
            var rect = ToMap(node.GetPosition());
            _nodeHits.Add((rect, node.GetPosition()));
            bool selected = _canvas.selection.Contains(node);
            EditorGUI.DrawRect(rect, selected ? new Color(0.3f, 0.6f, 0.9f, 0.9f) : new Color(0.45f, 0.45f, 0.45f, 0.75f));

            // A mark for the nodes that use the IO Dock's items.
            Texture icon = null;
            Color tint = Color.white;
            float size = 12f;
            switch (target)
            {
                case SendSignalNode _: icon = OutputIcon; tint = new Color(1f, 0.75f, 0.35f); break;
                case OnSignalReceivedNode _: icon = InputIcon; tint = new Color(0.45f, 0.9f, 1f); break;
                case GetBehaviorVariableNode get:
                    icon = PrefabHierarchy.VariableIcon;
                    tint = PrefabHierarchy.ColorOf(BehaviorReflection.Get(get.Target.Type)?.FindVariableType(get.Variable));
                    size = 6f; // (Variables are many and small: half the others' marks.)
                    break;
                case SetBehaviorVariableNode set:
                    icon = PrefabHierarchy.VariableIcon;
                    tint = PrefabHierarchy.ColorOf(BehaviorReflection.Get(set.Target.Type)?.FindVariableType(set.Variable));
                    size = 6f;
                    break;
                case GraphVariableNodeBase graphVariable:
                    icon = DockRows.GraphVariableIcon;
                    tint = DockRows.ColorOf(graphVariable.Definition?.ValueType);
                    size = 6f;
                    break;
                case BehaviorReferenceNode _: icon = BehaviorIcon; break;
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

    // The comment under the mouse: the innermost one, when comments sit inside each other.
    private static Texture2D _noteIcon;
    // Notes' mark: a pencil (white, tinted where it's drawn).
    private static Texture2D NoteIcon => _noteIcon != null ? _noteIcon : _noteIcon = Resources.Load<Texture2D>("Textures/note-pencil");

    // A note's first line, short, for its name on hover.
    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        var line = end >= 0 ? text.Substring(0, end) : text;
        return line.Length > 40 ? line.Substring(0, 40) + "\u2026" : line;
    }

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
    private readonly List<(Rect map, Rect graph, CommentView comment)> _commentHits = new();
    private readonly List<CommentMark> _commentMarks = new();

    private Vector2 ToGraph(Vector2 local) => _contentBounds.position + (local - _mapOffset) / _mapScale;

    // A click on a node glides to that node, on a comment to the whole comment, anywhere else to that point.
    private void Click(Vector2 local)
    {
        if (_mapScale <= 0f) return;

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
