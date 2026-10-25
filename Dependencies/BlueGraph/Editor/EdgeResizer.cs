using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph.Editor
{
    /// <summary>
    /// Resizing a graph element (a comment, a note) from any of its edges or corners: thin grips along each, with the
    /// matching resize cursor. Dragging one moves just that edge (or the two edges of a corner), in graph space, so it
    /// follows the mouse at any zoom.
    /// </summary>
    public static class EdgeResizer
    {
        [Flags]
        private enum Edge { Left = 1, Right = 2, Top = 4, Bottom = 8 }

        /// <summary>
        /// Adds the grips to <paramref name="element"/>; <paramref name="began"/> runs as a drag starts (record an undo),
        /// <paramref name="ended"/> once it's let go. It's resized with its own SetPosition, never below <paramref name="minSize"/>.
        /// </summary>
        public static void Attach(GraphElement element, Vector2 minSize, Action began = null, Action ended = null)
        {
            var sheet = Resources.Load<StyleSheet>("BlueGraphEditor/EdgeResizer");
            if (sheet != null)
                element.styleSheets.Add(sheet);
            Add(element, "left", Edge.Left, minSize, began, ended);
            Add(element, "right", Edge.Right, minSize, began, ended);
            Add(element, "top", Edge.Top, minSize, began, ended);
            Add(element, "bottom", Edge.Bottom, minSize, began, ended);
            Add(element, "top-left", Edge.Top | Edge.Left, minSize, began, ended);
            Add(element, "top-right", Edge.Top | Edge.Right, minSize, began, ended);
            Add(element, "bottom-left", Edge.Bottom | Edge.Left, minSize, began, ended);
            Add(element, "bottom-right", Edge.Bottom | Edge.Right, minSize, began, ended);
        }

        private static void Add(GraphElement element, string name, Edge edges, Vector2 minSize, Action began, Action ended)
        {
            var grip = new VisualElement { name = "edge-grip-" + name };
            grip.AddToClassList("edge-grip");
            grip.AddToClassList("edge-grip-" + name);

            bool dragging = false;
            Vector2 start = default;
            Rect startRect = default;

            // Where the mouse is in the element's parent (graph space, whatever the zoom).
            Vector2 InGraph(Vector2 world) => element.parent != null ? element.parent.WorldToLocal(world) : world;

            grip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                dragging = true;
                start = InGraph(evt.position);
                startRect = element.GetPosition();
                grip.CapturePointer(evt.pointerId);
                began?.Invoke();
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging)
                    return;
                var delta = InGraph(evt.position) - start;
                float xMin = startRect.xMin, xMax = startRect.xMax, yMin = startRect.yMin, yMax = startRect.yMax;
                if ((edges & Edge.Left) != 0) xMin = Mathf.Min(startRect.xMin + delta.x, xMax - minSize.x);
                if ((edges & Edge.Right) != 0) xMax = Mathf.Max(startRect.xMax + delta.x, xMin + minSize.x);
                if ((edges & Edge.Top) != 0) yMin = Mathf.Min(startRect.yMin + delta.y, yMax - minSize.y);
                if ((edges & Edge.Bottom) != 0) yMax = Mathf.Max(startRect.yMax + delta.y, yMin + minSize.y);
                element.SetPosition(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging)
                    return;
                dragging = false;
                grip.ReleasePointer(evt.pointerId);
                ended?.Invoke();
                evt.StopPropagation();
            });
            grip.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (!dragging)
                    return;
                dragging = false;
                ended?.Invoke();
            });
            // (Clicks on a grip don't start a box selection or a move.)
            grip.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());

            element.Add(grip);
        }
    }
}
