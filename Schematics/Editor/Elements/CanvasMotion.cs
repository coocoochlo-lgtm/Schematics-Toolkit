using System.Collections.Generic;
using System.Linq;
using BlueGraph.Editor;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Glides the Schematic Graph's view to where it's asked to go instead of jumping there: used by the IO Dock, the
/// minimap, and the framing keys (A: all, F: selection, O: origin, [ and ]: previous / next node), which it takes over
/// from the instant versions. A new target mid-glide just redirects it; grabbing the view (any click on the canvas)
/// stops it.
/// </summary>
public class CanvasMotion
{
    private const float Smoothing = 12f;     // higher = snappier
    private const float FramePadding = 40f;

    private readonly CanvasView _canvas;
    private readonly IVisualElementScheduledItem _tick;
    private Vector3 _targetPosition, _targetScale;
    private double _lastTime;
    private bool _moving;
    private int _nodeCursor = -1;

    public CanvasMotion(CanvasView canvas)
    {
        _canvas = canvas;
        _tick = canvas.schedule.Execute(Step).Every(10);
        _tick.Pause();

        canvas.RegisterCallback<MouseDownEvent>(_ => Stop(), TrickleDown.TrickleDown);
        canvas.RegisterCallback<WheelEvent>(_ => Stop(), TrickleDown.TrickleDown);
        canvas.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
    }

    private Vector3 Position => _canvas.contentViewContainer.transform.position;
    private Vector3 Scale => _canvas.contentViewContainer.transform.scale;

    /// <summary>Glides so the given point (in graph space) is in the middle, keeping the zoom.</summary>
    public void CenterOn(Vector2 graphPoint) => GlideTo(graphPoint, Scale.x);

    /// <summary>Glides so the rect (in graph space) is centred and fits, zooming out if needed (never in past the current zoom unless asked).</summary>
    public void Frame(Rect graphRect, bool allowZoomIn = false)
    {
        var size = _canvas.layout.size;
        if (size.x <= 0 || size.y <= 0 || graphRect.width <= 0 || graphRect.height <= 0)
        {
            CenterOn(graphRect.center);
            return;
        }

        float fit = Mathf.Min((size.x - FramePadding * 2) / graphRect.width, (size.y - FramePadding * 2) / graphRect.height);
        float scale = allowZoomIn ? fit : Mathf.Min(Scale.x, fit);
        scale = Mathf.Clamp(scale, _canvas.minScale, _canvas.maxScale);
        GlideTo(graphRect.center, scale);
    }

    public void Stop()
    {
        _moving = false;
        _tick.Pause();
    }

    private void GlideTo(Vector2 graphCenter, float scale)
    {
        var size = _canvas.layout.size;
        _targetScale = new Vector3(scale, scale, 1f);
        _targetPosition = new Vector3(size.x * 0.5f - graphCenter.x * scale, size.y * 0.5f - graphCenter.y * scale, 0f);
        _lastTime = EditorApplication.timeSinceStartup;
        if (!_moving)
        {
            _moving = true;
            _tick.Resume();
        }
    }

    private void Step()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Clamp((float)(now - _lastTime), 0.001f, 0.05f);
        _lastTime = now;

        // Exponential ease-out: framerate independent, and smooth when the target changes mid-way.
        float t = 1f - Mathf.Exp(-Smoothing * dt);
        var position = Vector3.Lerp(Position, _targetPosition, t);
        var scale = Vector3.Lerp(Scale, _targetScale, t);

        if ((position - _targetPosition).sqrMagnitude < 0.25f && Mathf.Abs(scale.x - _targetScale.x) < 0.0005f)
        {
            position = _targetPosition;
            scale = _targetScale;
            Stop();
        }
        _canvas.UpdateViewTransform(position, scale);
    }

    #region Framing keys

    private void OnKeyDown(KeyDownEvent evt)
    {
        // Only keys pressed on the canvas itself, never while typing in a field or a comment title.
        if (evt.target != _canvas || evt.ctrlKey || evt.commandKey || evt.altKey || evt.shiftKey)
            return;

        switch (evt.keyCode)
        {
            case KeyCode.A: FrameAll(); break;
            case KeyCode.F: FrameSelection(); break;
            case KeyCode.O: GlideTo(Vector2.zero, 1f); break;
            case KeyCode.LeftBracket: StepNode(-1); break;
            case KeyCode.RightBracket: StepNode(1); break;
            default: return;
        }
        // Instead of the instant versions.
        evt.StopImmediatePropagation();
    }

    private void FrameAll()
    {
        var rects = _canvas.graphElements.ToList().Where(e => e is NodeView || e is CommentView).Select(e => e.GetPosition()).ToList();
        if (rects.Count > 0)
            Frame(Bounds(rects), allowZoomIn: true);
    }

    private void FrameSelection()
    {
        var rects = _canvas.selection.OfType<GraphElement>().Select(e => e.GetPosition()).ToList();
        if (rects.Count == 0)
        {
            FrameAll();
            return;
        }
        Frame(Bounds(rects));
    }

    // Steps through the nodes left-to-right, top-to-bottom, selecting each one.
    private void StepNode(int direction)
    {
        var nodes = _canvas.nodes.ToList().OfType<NodeView>()
            .OrderBy(n => n.GetPosition().y).ThenBy(n => n.GetPosition().x).ToList();
        if (nodes.Count == 0)
            return;

        var selected = _canvas.selection.OfType<NodeView>().FirstOrDefault();
        int index = selected != null ? nodes.IndexOf(selected) : _nodeCursor;
        index = ((index + direction) % nodes.Count + nodes.Count) % nodes.Count;
        _nodeCursor = index;

        _canvas.ClearSelection();
        _canvas.AddToSelection(nodes[index]);
        Frame(nodes[index].GetPosition());
    }

    private static Rect Bounds(List<Rect> rects)
    {
        var bounds = rects[0];
        foreach (var r in rects)
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin), Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax));
        return bounds;
    }

    #endregion
}
