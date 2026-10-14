using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph.Editor
{
    /// <summary>
    /// Comments are made only while C is held down, so they never appear by accident:
    /// <list type="bullet">
    /// <item>Hold C and drag on empty canvas to draw a comment over that region (a comment drawn around nodes then
    /// hugs them; one drawn over empty space keeps the size you drew).</item>
    /// <item>Hold C and click empty canvas to wrap the selected nodes in a comment, or, with nothing selected, to drop
    /// a small comment where you clicked.</item>
    /// </list>
    /// Typing a "c" into a text field never counts, and neither does Ctrl/Cmd/Alt+C. While C is held, a hint shows at
    /// the top of the canvas, and box selection and panning step aside for the comment drawing.
    /// </summary>
    public class CommentKeyTool : Manipulator
    {
        private const float MinimumDrag = 8f;

        private readonly CanvasView canvas;
        private bool held;
        private bool drawing;
        private Vector2 start;
        private VisualElement preview;
        private Label hint;

        /// <summary>True while C is held down over the canvas.</summary>
        public bool IsHeld => held;

        public CommentKeyTool(CanvasView canvas)
        {
            this.canvas = canvas;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            // Trickle-down: heard before the canvas' own selectors and draggers, so a C+drag doesn't also box-select.
            target.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<MouseCaptureOutEvent>(OnCaptureOut);
            target.RegisterCallback<FocusOutEvent>(OnFocusOut);
            target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<MouseCaptureOutEvent>(OnCaptureOut);
            target.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        /// <summary>
        /// Forgets that C is held, e.g. when the window loses focus while it's down (its key-up then goes elsewhere).
        /// </summary>
        public void Release()
        {
            held = false;
            ShowHint(false);

            var pending = afterRelease;
            afterRelease = null;
            pending?.Invoke();
        }

        private System.Action afterRelease;

        /// <summary>
        /// Runs an action once C is let go (or right away if it isn't held). A new comment's title editor waits for
        /// this: focused while C is still down, the key's auto-repeat would type "ccc" into it.
        /// </summary>
        public void RunAfterRelease(System.Action action)
        {
            if (!held)
                action();
            else
                afterRelease += action;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.C || evt.ctrlKey || evt.commandKey || evt.altKey || IsTyping(evt.target as VisualElement))
                return;
            if (!held)
            {
                held = true;
                ShowHint(true);
            }
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode == KeyCode.C)
                Release();
        }

        private void OnFocusOut(FocusOutEvent evt)
        {
            // Focus moving within the canvas (to a node, say) keeps C held; focus leaving it doesn't.
            if (evt.relatedTarget is VisualElement next && (next == canvas || canvas.Contains(next)))
                return;
            Release();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            Release();
            EndDrawing();
        }

        /// <summary>
        /// Whether keys pressed on this element are being typed into a text field (or an IMGUI control, which may be
        /// one), rather than being shortcuts.
        /// </summary>
        private static bool IsTyping(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e is IMGUIContainer)
                    return true;
                if (e.ClassListContains("unity-base-text-field") || e.ClassListContains("unity-text-input") || e.ClassListContains("unity-base-text-field__input"))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Empty canvas: the canvas itself, its grid, or the graph's layers, but not a node, edge, port or comment, and
        /// not one of the overlays added on top of the canvas (the minimap, docks, panels).
        /// </summary>
        private bool IsEmptyCanvas(VisualElement element)
        {
            if (element == null || element == canvas || element is GridBackground)
                return true;
            if (element.GetFirstOfType<GraphElement>() != null)
                return false;
            return element == canvas.contentViewContainer || canvas.contentViewContainer.Contains(element);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (!held || evt.button != 0 || drawing || !IsEmptyCanvas(evt.target as VisualElement))
                return;

            drawing = true;
            start = evt.mousePosition;

            preview = new VisualElement { pickingMode = PickingMode.Ignore, name = "comment-draw-preview" };
            preview.style.position = Position.Absolute;
            var color = Comment.DefaultColor;
            preview.style.backgroundColor = new Color(color.r, color.g, color.b, 0.12f);
            preview.style.borderTopColor = preview.style.borderBottomColor =
                preview.style.borderLeftColor = preview.style.borderRightColor = new Color(color.r, color.g, color.b, 0.8f);
            preview.style.borderTopWidth = preview.style.borderBottomWidth =
                preview.style.borderLeftWidth = preview.style.borderRightWidth = 2;
            preview.style.borderTopLeftRadius = preview.style.borderTopRightRadius =
                preview.style.borderBottomLeftRadius = preview.style.borderBottomRightRadius = 8;
            canvas.Add(preview);
            UpdatePreview(evt.mousePosition);

            target.CaptureMouse();
            evt.StopImmediatePropagation();
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            if (!drawing)
                return;
            UpdatePreview(evt.mousePosition);
            evt.StopPropagation();
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (!drawing || evt.button != 0)
                return;
            var end = evt.mousePosition;
            EndDrawing();
            evt.StopPropagation();

            if ((end - start).magnitude >= MinimumDrag)
            {
                // Corners from window space to graph space.
                var a = canvas.contentViewContainer.WorldToLocal(start);
                var b = canvas.contentViewContainer.WorldToLocal(end);
                canvas.AddComment(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }
            else
            {
                canvas.AddCommentAt(end);
            }
        }

        private void OnCaptureOut(MouseCaptureOutEvent evt) => EndDrawing();

        private void EndDrawing()
        {
            if (!drawing)
                return;
            drawing = false;
            preview?.RemoveFromHierarchy();
            preview = null;
            if (target != null && target.HasMouseCapture())
                target.ReleaseMouse();
        }

        private void UpdatePreview(Vector2 mouse)
        {
            if (preview == null)
                return;
            var a = canvas.WorldToLocal(start);
            var b = canvas.WorldToLocal(mouse);
            preview.style.left = Mathf.Min(a.x, b.x);
            preview.style.top = Mathf.Min(a.y, b.y);
            preview.style.width = Mathf.Abs(a.x - b.x);
            preview.style.height = Mathf.Abs(a.y - b.y);
        }

        private void ShowHint(bool show)
        {
            if (show && hint == null)
            {
                hint = new Label("C held: drag to draw a comment, or click to wrap the selection")
                {
                    pickingMode = PickingMode.Ignore,
                    name = "comment-key-hint",
                };
                hint.style.position = Position.Absolute;
                hint.style.top = 8;
                hint.style.left = 0;
                hint.style.right = 0;
                hint.style.alignSelf = Align.Center;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                hint.style.color = new Color(1f, 1f, 1f, 0.85f);
                hint.style.fontSize = 11;
            }
            if (hint == null)
                return;
            if (show && hint.parent == null)
                canvas.Add(hint);
            else if (!show)
                hint.RemoveFromHierarchy();
        }
    }
}
