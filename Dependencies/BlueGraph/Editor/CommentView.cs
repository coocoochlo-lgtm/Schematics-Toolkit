using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;

namespace BlueGraph.Editor
{
    public class CommentView : GraphElement, ICanDirty
    {
        public Comment Target { get; protected set; }

        private readonly VisualElement titleContainer;
        private readonly TextField titleEditor;
        private readonly Label titleLabel;
        
        private CommentTheme theme;
        private bool isEditingCancelled;

        private readonly ColorField colorField;
        private double lastColorChange = -10;

        public CommentView(Comment comment)
        {
            Target = comment;
            SetPosition(comment.Region);
            
            styleSheets.Add(Resources.Load<StyleSheet>("BlueGraphEditor/CommentView"));
            
            // The header: the title (with its editor) on the left, a color swatch on the right.
            titleContainer = new VisualElement();
            titleContainer.AddToClassList("titleContainer");
            titleContainer.style.flexDirection = FlexDirection.Row;
            titleContainer.style.alignItems = Align.FlexStart;

            var titleStack = new VisualElement();
            titleStack.style.flexGrow = 1;
            titleStack.style.flexShrink = 1;
            titleContainer.Add(titleStack);
            
            titleEditor = new TextField();
            
            var input = titleEditor.Q(TextField.textInputUssName);
            input.RegisterCallback<KeyDownEvent>(OnTitleKeyDown);
            input.RegisterCallback<FocusOutEvent>(e => { OnFinishEditingTitle(); });
            
            titleStack.Add(titleEditor);
            
            titleLabel = new Label();
            titleLabel.text = comment.Text;
            
            titleStack.Add(titleLabel);

            titleEditor.style.display = DisplayStyle.None;

            // Any color at all, through Unity's color picker. (The context menu has a palette of ready-made ones.)
            colorField = new ColorField
            {
                showAlpha = false,
                showEyeDropper = false,
                hdr = false,
                tooltip = "Comment color: tints the comment here and on the minimap",
            };
            colorField.AddToClassList("commentColorSwatch");
            colorField.style.width = 22;
            colorField.style.height = 16;
            colorField.style.marginTop = 9;
            colorField.style.marginRight = 6;
            colorField.style.flexShrink = 0;
            colorField.SetValueWithoutNotify(comment.Color);
            colorField.RegisterValueChangedCallback(e => SetColor(e.newValue));
            titleContainer.Add(colorField);

            Add(titleContainer);

            ClearClassList();
            AddToClassList("commentView");
            
            capabilities |= Capabilities.Selectable | Capabilities.Movable | 
                            Capabilities.Deletable | Capabilities.Resizable;

            RegisterCallback<MouseDownEvent>(OnMouseDown);
            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
            // Right-clicking the body is right-clicking the canvas (its own menu: create a node, add a note...); the
            // comment's menu is its header's.
            RegisterCallback<PointerDownEvent>(OpenCanvasMenuFromBody, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OpenCanvasMenuFromBody, TrickleDown.TrickleDown);
            RegisterCallback<MouseDownEvent>(StopRightClickInBody, TrickleDown.TrickleDown);
            RegisterCallback<MouseUpEvent>(StopRightClickInBody, TrickleDown.TrickleDown);
            
            SetTheme(Target.Theme);
            ApplyColor();
            ApplyFontSize();

            // Resized from any edge or corner. Sized by hand, it keeps that size (no longer fitting itself to its nodes;
            // Resize To Fit Nodes in its menu turns that back on).
            EdgeResizer.Attach(this, new Vector2(150f, 50f), () =>
            {
                var graph = GetFirstAncestorOfType<CanvasView>()?.Graph;
                if (graph != null)
                    Undo.RegisterCompleteObjectUndo(graph, "Resize Comment");
            }, () =>
            {
                Target.FixedSize = true;
                var graph = GetFirstAncestorOfType<CanvasView>()?.Graph;
                if (graph != null)
                    EditorUtility.SetDirty(graph);
            });
        }
        
        public virtual void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target is CommentView)
            {
                // A palette of ready-made colors; the swatch in the header picks any other.
                var current = Target.Color;
                foreach (var (colorName, color) in Comment.Palette)
                {
                    var picked = color;
                    bool isCurrent = SameColor(current, picked);
                    evt.menu.AppendAction("Color/" + colorName, _ => SetColor(picked),
                        isCurrent ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                }
                evt.menu.AppendSeparator("Color/");
                // (The event is recycled once the menu shows, so keep where it was opened now.)
                var window = GetFirstAncestorOfType<CanvasView>()?.EditorWindow;
                var screenPoint = (window != null ? window.position.position : Vector2.zero) + evt.mousePosition;
                evt.menu.AppendAction("Color/Custom\u2026", _ => CommentColorWindow.Open(this, screenPoint));

                // The title's size: the notes' choices (or Ctrl + the mouse wheel over the header).
                float currentSize = Target.FontSize;
                foreach (var (sizeName, size) in NoteView.TextSizes)
                {
                    var pickedSize = size;
                    evt.menu.AppendAction("Text Size/" + sizeName, _ => SetFontSize(pickedSize),
                        Mathf.Approximately(currentSize, size) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
                }

                evt.menu.AppendAction("Resize To Fit Nodes", _ =>
                {
                    Target.FixedSize = !Target.FixedSize;
                    if (!Target.FixedSize)
                        FitToMembers();
                }, Target.FixedSize ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Checked);

                evt.menu.AppendSeparator();
            }
        }

        // A right-click in the body (not the header, not an edge grip): the canvas's menu, where it was clicked.
        private void OpenCanvasMenuFromBody<T>(PointerEventBase<T> evt) where T : PointerEventBase<T>, new()
        {
            if (evt.button != 1 || HitTest(evt.localPosition) || (evt.target is VisualElement element && element.ClassListContains("edge-grip")))
                return;
            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (canvas == null || panel?.contextualMenuManager == null)
                return;
            // (Shown on the press or the release, whichever the platform opens menus on.)
            panel.contextualMenuManager.DisplayMenuIfEventMatches(evt, canvas);
            evt.StopPropagation();
        }

        private void StopRightClickInBody<T>(MouseEventBase<T> evt) where T : MouseEventBase<T>, new()
        {
            if (evt.button == 1 && !HitTest(evt.localMousePosition))
                evt.StopPropagation();
        }

        // Ctrl + wheel over the header: the title bigger or smaller.
        private double lastSizeChange = -10;

        private void OnWheel(WheelEvent evt)
        {
            if ((!evt.ctrlKey && !evt.commandKey) || !HitTest(evt.localMousePosition))
                return;
            SetFontSize(Target.FontSize + (evt.delta.y < 0 ? 2f : -2f));
            evt.StopPropagation();
        }

        /// <summary>Sizes the comment's title (undoably; a burst of wheel steps is one undo step).</summary>
        public void SetFontSize(float size)
        {
            size = Mathf.Clamp(size, 8f, 96f);
            if (Mathf.Approximately(size, Target.FontSize))
                return;
            var graph = GetFirstAncestorOfType<CanvasView>()?.Graph;
            double now = EditorApplication.timeSinceStartup;
            if (graph != null && now - lastSizeChange > 1.0)
                Undo.RegisterCompleteObjectUndo(graph, "Comment Text Size");
            lastSizeChange = now;
            Target.FontSize = size;
            ApplyFontSize();
            if (graph != null)
                EditorUtility.SetDirty(graph);
        }

        private void ApplyFontSize()
        {
            titleLabel.style.fontSize = Target.FontSize;
            titleEditor.style.fontSize = Target.FontSize;
            var input = titleEditor.Q(TextField.textInputUssName);
            if (input != null)
                input.style.fontSize = Target.FontSize;
        }

        private static bool SameColor(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>
        /// Recolors the comment (undoably). A burst of changes, like dragging around Unity's color picker, makes one
        /// undo step rather than hundreds.
        /// </summary>
        public void SetColor(Color color)
        {
            color.a = 1f;
            if (SameColor(color, Target.Color) && Target.Color.a > 0f)
            {
                colorField.SetValueWithoutNotify(Target.Color);
                return;
            }

            var graph = GetFirstAncestorOfType<CanvasView>()?.Graph;
            double now = EditorApplication.timeSinceStartup;
            if (graph != null && now - lastColorChange > 1.0)
                Undo.RegisterCompleteObjectUndo(graph, "Change Comment Color");
            lastColorChange = now;

            Target.Color = color;
            colorField.SetValueWithoutNotify(Target.Color);
            ApplyColor();
            if (graph != null)
                EditorUtility.SetDirty(graph);
        }

        /// <summary>
        /// Paints the comment in its color: a solid title bar and a see-through body and outline, so the nodes inside
        /// stay easy to read. Selected comments keep the usual blue outline.
        /// </summary>
        public void ApplyColor()
        {
            var color = Target.Color;
            style.backgroundColor = new Color(color.r, color.g, color.b, 0.12f);
            var border = selected ? (Color)new Color32(0x44, 0xC0, 0xFF, 0xFF) : new Color(color.r, color.g, color.b, 0.55f);
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = border;

            titleLabel.style.backgroundColor = color;
            // A soft gloss over the color (the browser cards' and notes'), under the title.
            var gloss = Resources.Load<Texture2D>("Textures/button-gradient");
            if (gloss != null)
            {
                titleLabel.style.backgroundImage = gloss;
                titleLabel.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0.28f);
            }
            // White text, with a soft shadow so it reads on light colors too.
            titleLabel.style.color = Color.white;
            titleLabel.style.textShadow = NoteView.TextShade;
        }

        public override void OnSelected()
        {
            base.OnSelected();
            ApplyColor();
        }

        public override void OnUnselected()
        {
            base.OnUnselected();
            ApplyColor();
        }
        
        /// <summary>
        /// Change the color theme used on the canvas. Kept for older code: colors have replaced themes (a comment
        /// saved with a theme but no color shows in that theme's color).
        /// </summary>
        public void SetTheme(CommentTheme theme)
        {
            RemoveFromClassList("theme-" + this.theme);
            AddToClassList("theme-" + theme);
            this.theme = theme;
            Target.Theme = theme;
        }
        
        private void OnTitleKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    isEditingCancelled = true;
                    OnFinishEditingTitle();
                    break;
                case KeyCode.Return:
                    OnFinishEditingTitle();
                    break;
                default:
                    break;
            }
        }
        
        private void OnFinishEditingTitle()
        {
            // Show the label and hide the editor
            titleLabel.visible = true;
            titleEditor.style.display = DisplayStyle.None;

            if (!isEditingCancelled)
            {
                string oldName = titleLabel.text;
                string newName = titleEditor.value;
                
                titleLabel.text = newName;
                OnRenamed(oldName, newName);
            }
                
            isEditingCancelled = false;
        }
        
        public void EditTitle()
        {
            titleLabel.visible = false;

            titleEditor.SetValueWithoutNotify(Target.Text);
            titleEditor.style.display = DisplayStyle.Flex;
            titleEditor.Q(TextField.textInputUssName).Focus();
        }
        
        public virtual void OnRenamed(string oldName, string newName)
        {
            Target.Text = newName;
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            // A double-click on the color swatch is for the swatch, not the title.
            if (evt.clickCount == 2 && !(evt.target is VisualElement element && colorField.Contains(element)))
            {
                if (HitTest(evt.localMousePosition))
                {
                    EditTitle();
                }
            }
        }
        
        /// <summary>
        /// Override HitTest to only trigger when they click the title
        /// </summary>
        public override bool HitTest(Vector2 localPoint)
        {
            Vector2 mappedPoint = this.ChangeCoordinatesTo(titleContainer, localPoint);
            return titleContainer.ContainsPoint(mappedPoint);
        }
        
        /// <summary>Space between a comment's edges and the nodes it fits around (the top leaves room for the title).</summary>
        public const float FitPadding = 30f, FitTitlePadding = 60f;

        /// <summary>
        /// The nodes this comment groups: those whose centre lies inside it. A node dragged partly past the edge stays in
        /// (and the comment grows to fit it); dragged out past its centre, it leaves.
        /// </summary>
        // What a comment groups and fits around: the canvas's nodes and notes.
        private static IEnumerable<GraphElement> Groupable(CanvasView canvas)
        {
            foreach (var node in canvas.nodes.ToList())
                if (node is NodeView view)
                    yield return view;
            foreach (var note in canvas.Query<NoteView>().ToList())
                yield return note;
        }

        public List<NodeView> MemberNodes()
        {
            var result = new List<NodeView>();
            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (canvas == null)
                return result;
            var region = Target.Region;
            foreach (var node in canvas.nodes.ToList())
                if (node is NodeView view && region.Contains(view.GetPosition().center))
                    result.Add(view);
            return result;
        }

        /// <summary>Resizes the comment around its member nodes (unless it's fixed-size). True if it changed.</summary>
        public bool FitToMembers() => FitToMembers(animate: false);

        /// <summary>The same; <paramref name="animate"/>: it springs to its new size rather than jumping.</summary>
        public bool FitToMembers(bool animate)
        {
            if (Target.FixedSize)
                return false;
            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (canvas == null)
                return false;
            // Its nodes and notes: those whose centre is inside it.
            var region = Target.Region;
            bool any = false;
            var bounds = default(Rect);
            foreach (var member in Groupable(canvas))
            {
                var r = member.GetPosition();
                if (!region.Contains(r.center))
                    continue;
                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, r.xMin), Mathf.Min(bounds.yMin, r.yMin), Mathf.Max(bounds.xMax, r.xMax), Mathf.Max(bounds.yMax, r.yMax)) : r;
                any = true;
            }
            if (!any)
                return false;
            var fitted = Padded(bounds);
            if (fitted == Target.Region)
                return false;
            if (animate)
            {
                SpringTo(fitted);
                return true;
            }

            StopSpring();
            // Set directly: SetPosition would treat a move as dragging the comment and carry the nodes along.
            base.SetPosition(fitted);
            Target.Region = fitted;
            return true;
        }

        /// <summary>
        /// A node dragged from <paramref name="from"/> to <paramref name="to"/>: the comment refits around its nodes as it
        /// goes. The dragged node stays one of them while it's still touching the comment (so pushing it against an edge
        /// grows the comment, pulling it back in shrinks it); flung clear of the comment in one go, it leaves. Others are
        /// its nodes when their centre is inside. True if the comment's size changed.
        /// </summary>
        public bool FitWhileDragging(GraphElement dragged, Rect from, Rect to)
        {
            // (A comment being dragged itself carries its nodes: nothing to fit.)
            if (Target.FixedSize || selected)
                return false;
            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (canvas == null)
                return false;
            var region = Target.Region;
            bool any = false;
            var bounds = default(Rect);
            foreach (var view in Groupable(canvas))
            {
                var rect = view == dragged ? to : view.GetPosition();
                bool member = view == dragged
                    ? region.Contains(from.center) && to.Overlaps(region)
                    : region.Contains(rect.center);
                if (!member)
                    continue;
                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin), Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax)) : rect;
                any = true;
            }
            if (!any)
                return false;
            var fitted = Padded(bounds);
            if (fitted == region)
                return false;
            SpringTo(fitted);
            return true;
        }

        private static Rect Padded(Rect bounds) =>
            Rect.MinMaxRect(bounds.xMin - FitPadding, bounds.yMin - FitTitlePadding, bounds.xMax + FitPadding, bounds.yMax + FitPadding);

        // The size and place shown, springing towards the comment's real ones (Target.Region) after a refit, so it
        // stretches and settles, a little elastically, rather than jumping.
        private Vector4 shown, velocity;
        private IVisualElementScheduledItem spring;
        private double lastSpringTick;

        // How stiff, and how much it bounces (1 would settle without overshooting).
        private const float SpringStiffness = 420f, SpringDamping = 0.6f;

        private static Vector4 Edges(Rect r) => new Vector4(r.xMin, r.yMin, r.xMax, r.yMax);

        private void SpringTo(Rect region)
        {
            if (spring == null || !spring.isActive)
            {
                shown = Edges(base.GetPosition());
                velocity = Vector4.zero;
                lastSpringTick = EditorApplication.timeSinceStartup;
                if (spring == null)
                    spring = schedule.Execute(SpringTick).Every(16);
                else
                    spring.Resume();
            }
            Target.Region = region;
        }

        private void SpringTick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Clamp((float)(now - lastSpringTick), 0.001f, 0.05f);
            lastSpringTick = now;
            var target = Edges(Target.Region);
            float damping = 2f * Mathf.Sqrt(SpringStiffness) * SpringDamping;
            // (Two half steps: steady even when a frame runs long.)
            for (int i = 0; i < 2; i++)
            {
                float h = dt * 0.5f;
                velocity += (SpringStiffness * (target - shown) - damping * velocity) * h;
                shown += velocity * h;
            }
            if ((target - shown).magnitude < 0.5f && velocity.magnitude < 10f)
            {
                shown = target;
                spring.Pause();
            }
            base.SetPosition(Rect.MinMaxRect(shown.x, shown.y, shown.z, shown.w));
        }

        // The spring stopped where it is (the comment moved or resized some other way).
        private void StopSpring()
        {
            if (spring != null && spring.isActive)
                spring.Pause();
        }

        /// <summary>
        /// Box-selecting picks a comment only when the box wraps all of it, not when it catches some of the nodes inside.
        /// (<paramref name="rectangle"/> is in the comment's own space.)
        /// </summary>
        public override bool Overlaps(Rect rectangle)
        {
            var box = Rect.MinMaxRect(Mathf.Min(rectangle.xMin, rectangle.xMax), Mathf.Min(rectangle.yMin, rectangle.yMax),
                Mathf.Max(rectangle.xMin, rectangle.xMax), Mathf.Max(rectangle.yMin, rectangle.yMax));
            return box.xMin <= 0f && box.yMin <= 0f && box.xMax >= layout.width && box.yMax >= layout.height;
        }

        public override void SetPosition(Rect newPos)
        {
            var previous = Target.Region;
            StopSpring();
            base.SetPosition(newPos);
            Target.Region = newPos;

            // A comment groups the nodes inside it: moving it (not resizing) carries them along.
            var delta = newPos.position - previous.position;
            if (delta != Vector2.zero && Mathf.Approximately(newPos.width, previous.width) && Mathf.Approximately(newPos.height, previous.height))
                MoveContainedNodes(previous, delta);
        }

        /// <summary>The nodes lying entirely inside this comment.</summary>
        public List<NodeView> ContainedNodes(Rect region)
        {
            var result = new List<NodeView>();
            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (canvas == null)
                return result;

            foreach (var node in canvas.nodes.ToList())
            {
                if (!(node is NodeView view))
                    continue;
                var rect = view.GetPosition();
                if (region.Contains(rect.min) && region.Contains(rect.max))
                    result.Add(view);
            }
            return result;
        }

        private void MoveContainedNodes(Rect region, Vector2 delta)
        {
            bool moved = false;
            foreach (var view in ContainedNodes(region))
            {
                // Selected nodes are already being dragged along with the comment.
                if (view.selected)
                    continue;
                var rect = view.GetPosition();
                view.SetPosition(new Rect(rect.position + delta, rect.size));
                moved = true;
            }

            // Notes lying entirely inside come along too.
            var notes = GetFirstAncestorOfType<CanvasView>()?.Query<NoteView>().ToList();
            if (notes != null)
                foreach (var note in notes)
                {
                    var rect = note.Target.Region;
                    if (note.selected || !region.Contains(rect.min) || !region.Contains(rect.max))
                        continue;
                    note.SetPosition(new Rect(rect.position + delta, rect.size));
                    moved = true;
                }

            var canvas = GetFirstAncestorOfType<CanvasView>();
            if (moved && canvas != null && canvas.Graph != null)
                EditorUtility.SetDirty(canvas.Graph);
        }

        public void Dirty() { }

        public void Update() { }
    }
}
