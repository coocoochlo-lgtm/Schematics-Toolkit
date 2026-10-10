using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A minimap of the Schematic Graph, in the canvas' corner. Signal nodes show as arrows (sending or receiving),
/// Variable nodes as dots in their type's color, Behavior nodes as script icons, and comments as named regions.
/// Hover anything to see its name. Click a node or comment to glide to it, or anywhere else to glide there; drag to
/// steer the view. Toggle it with the button above it.
/// </summary>
public class SchematicMinimap : VisualElement
{
    private const float Width = 220f, Height = 150f, Padding = 6f;

    private readonly CanvasView _canvas;
    private readonly CanvasMotion _motion;
    private readonly IMGUIContainer _drawing;
    private Rect _contentBounds;
    private bool _dragging;

    private static Texture2D _input, _output;
    private static Texture _behavior;
    private static Texture2D InputIcon => _input ??= Resources.Load<Texture2D>("Icons/Input");
    private static Texture2D OutputIcon => _output ??= Resources.Load<Texture2D>("Icons/Output");
    private static Texture BehaviorIcon => _behavior ??= EditorGUIUtility.IconContent("cs Script Icon").image;

    private const string VisibleKey = "SchematicMinimap.Visible";

    public SchematicMinimap(CanvasView canvas, CanvasMotion motion)
    {
        _canvas = canvas;
        _motion = motion;
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.right = 8;
        style.bottom = 8;
        style.alignItems = Align.FlexEnd;

        var toggle = new Button(() => SetVisible(!_drawing.visible)) { text = "Map", tooltip = "Show or hide the minimap" };
        toggle.style.alignSelf = Align.FlexEnd;
        Add(toggle);

        _drawing = new IMGUIContainer(Draw);
        _drawing.style.width = Width;
        _drawing.style.height = Height;
        _drawing.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        _drawing.style.borderTopLeftRadius = _drawing.style.borderTopRightRadius =
            _drawing.style.borderBottomLeftRadius = _drawing.style.borderBottomRightRadius = 4;
        Add(_drawing);

        _drawing.RegisterCallback<MouseDownEvent>(evt => { _dragging = true; Click(evt.localMousePosition); evt.StopPropagation(); });
        _drawing.RegisterCallback<MouseMoveEvent>(evt => { if (_dragging) { Pan(evt.localMousePosition); evt.StopPropagation(); } });
        _drawing.RegisterCallback<MouseUpEvent>(evt => { _dragging = false; evt.StopPropagation(); });
        _drawing.RegisterCallback<MouseLeaveEvent>(_ => _dragging = false);
        _drawing.RegisterCallback<WheelEvent>(evt => evt.StopPropagation());

        SetVisible(EditorPrefs.GetBool(VisibleKey, true));

        // Redraw as the view moves and nodes change.
        schedule.Execute(() => { if (_drawing.visible) _drawing.MarkDirtyRepaint(); }).Every(100);
    }

    private void SetVisible(bool visible)
    {
        _drawing.visible = visible;
        _drawing.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        EditorPrefs.SetBool(VisibleKey, visible);
    }

    private List<NodeView> Nodes() => _canvas.Query<NodeView>().ToList();

    private void Draw()
    {
        var area = new Rect(Padding, Padding, Width - Padding * 2, Height - Padding * 2);
        var nodes = Nodes();
        var viewport = _canvas.contentViewContainer.WorldToLocal(_canvas.worldBound);

        // Fit every node, comment and the current view.
        Rect bounds = viewport;
        foreach (var node in nodes)
            bounds = Encapsulate(bounds, node.GetPosition());
        foreach (var comment in _canvas.Query<CommentView>().ToList())
            bounds = Encapsulate(bounds, comment.GetPosition());
        bounds = Inflate(bounds, 40f);
        _contentBounds = bounds;

        float scale = Mathf.Min(area.width / bounds.width, area.height / bounds.height);
        var offset = new Vector2(area.x + (area.width - bounds.width * scale) * 0.5f, area.y + (area.height - bounds.height * scale) * 0.5f);
        Rect ToMap(Rect r) => new Rect(offset + (r.position - bounds.position) * scale, r.size * scale);

        string hover = null;
        var mouse = Event.current.mousePosition;

        // Comments first, under the nodes: a tinted region with an outline.
        _hitRects.Clear();
        foreach (var comment in _canvas.Query<CommentView>().ToList())
        {
            var region = ToMap(comment.GetPosition());
            EditorGUI.DrawRect(region, new Color(0.35f, 0.55f, 0.85f, 0.15f));
            DrawOutline(region, new Color(0.45f, 0.65f, 0.95f, 0.6f));
            _hitRects.Add((region, comment.GetPosition(), true));
            if (region.Contains(mouse))
                hover = comment.Target.Text;
        }

        foreach (var node in nodes)
        {
            var target = node.Target;
            var rect = ToMap(node.GetPosition());
            _hitRects.Add((rect, node.GetPosition(), false));
            bool selected = _canvas.selection.Contains(node);
            EditorGUI.DrawRect(rect, selected ? new Color(0.3f, 0.6f, 0.9f, 0.9f) : new Color(0.45f, 0.45f, 0.45f, 0.75f));

            // A mark for the nodes that use the IO Dock's items.
            Texture icon = null;
            Color tint = Color.white;
            switch (target)
            {
                case SendSignalNode _: icon = OutputIcon; tint = new Color(1f, 0.75f, 0.35f); break;
                case OnSignalReceivedNode _: icon = InputIcon; tint = new Color(0.45f, 0.9f, 1f); break;
                case GetBehaviorVariableNode get:
                    icon = PrefabHierarchy.VariableIcon;
                    tint = PrefabHierarchy.ColorOf(BehaviorReflection.Get(get.Target.Type)?.FindVariableType(get.Variable));
                    break;
                case SetBehaviorVariableNode set:
                    icon = PrefabHierarchy.VariableIcon;
                    tint = PrefabHierarchy.ColorOf(BehaviorReflection.Get(set.Target.Type)?.FindVariableType(set.Variable));
                    break;
                case BehaviorReferenceNode _: icon = BehaviorIcon; break;
            }

            if (icon != null)
            {
                const float size = 12f;
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

        // Kept for Pan.
        _mapScale = scale;
        _mapOffset = offset;
    }

    private float _mapScale = 1f;
    private Vector2 _mapOffset;
    // What was drawn where last time: minimap rect, graph rect, and whether it's a comment.
    private readonly List<(Rect map, Rect graph, bool comment)> _hitRects = new();

    private Vector2 ToGraph(Vector2 local) => _contentBounds.position + (local - _mapOffset) / _mapScale;

    // A click on a node glides to that node, on a comment to the whole comment, anywhere else to that point.
    private void Click(Vector2 local)
    {
        if (_mapScale <= 0f) return;

        // Nodes win over the comments around them; they were added after the comments.
        for (int i = _hitRects.Count - 1; i >= 0; i--)
        {
            var hit = _hitRects[i];
            if (!hit.map.Contains(local))
                continue;
            if (hit.comment)
                _motion.Frame(hit.graph);
            else
                _motion.CenterOn(hit.graph.center);
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
