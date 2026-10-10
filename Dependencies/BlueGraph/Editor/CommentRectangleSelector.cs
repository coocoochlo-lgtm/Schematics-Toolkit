using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph.Editor
{
    /// <summary>
    /// The usual box selection, which also wraps the nodes it selects in a new comment (see
    /// <see cref="CanvasView.CommentOnBoxSelect"/>). A plain click on the canvas doesn't count as a box.
    /// </summary>
    public class CommentRectangleSelector : RectangleSelector
    {
        private const float MinimumDrag = 8f;

        private readonly CanvasView canvas;
        private Vector2 start;
        private bool boxing;

        public CommentRectangleSelector(CanvasView canvas)
        {
            this.canvas = canvas;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            base.RegisterCallbacksOnTarget();
            // Trickle-down: heard before the selector, which may stop the event; the selection is read a moment later.
            target.RegisterCallback<MouseDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseUpEvent>(OnUp, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseUpEvent>(OnUp, TrickleDown.TrickleDown);
            base.UnregisterCallbacksFromTarget();
        }

        private void OnDown(MouseDownEvent evt)
        {
            // A box starts on empty canvas (not on a node, edge, port or comment) with the left button.
            var element = evt.target as VisualElement;
            boxing = evt.button == 0 && !evt.altKey && (element == null || element.GetFirstOfType<GraphElement>() == null);
            start = evt.mousePosition;
        }

        private void OnUp(MouseUpEvent evt)
        {
            if (!boxing || evt.button != 0)
                return;
            boxing = false;
            if ((evt.mousePosition - start).magnitude < MinimumDrag)
                return;
            // After the selector has applied the box.
            canvas.schedule.Execute(canvas.OnBoxSelected);
        }
    }
}
