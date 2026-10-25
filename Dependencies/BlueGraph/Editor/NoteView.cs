using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph.Editor
{
    /// <summary>
    /// A note on the canvas: a colored box of text (a little see-through). Drag it anywhere on it to move it, any edge or
    /// corner to resize it; double-click to edit the text (clicking away keeps it, Escape doesn't). Text that doesn't fit
    /// scrolls. Ctrl + the mouse wheel (or the right-click menu) sizes its text. The swatch picks any color, the
    /// right-click menu a ready-made one.
    /// </summary>
    public class NoteView : GraphElement, ICanDirty
    {
        public Note Target { get; protected set; }

        private readonly ScrollView scroll;
        private readonly Label textLabel;
        private readonly TextField textEditor;
        private readonly ColorField colorField;
        private bool isEditingCancelled;
        private double lastColorChange = -10;

        /// <summary>The smallest a note gets.</summary>
        public const float MinWidth = 120f, MinHeight = 60f;

        /// <summary>How see-through a note is (its color's alpha).</summary>
        public const float Opacity = 0.85f;

        /// <summary>Text sizes offered in the right-click menu.</summary>
        private static readonly (string name, float size)[] TextSizes =
            { ("Small", 14f), ("Medium", 20f), ("Large", Note.DefaultFontSize), ("Larger", 36f), ("Huge", 52f) };

        public NoteView(Note note)
        {
            Target = note;
            ClearClassList();
            AddToClassList("noteView");
            style.minWidth = MinWidth;
            style.minHeight = MinHeight;
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 4;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 2;
            style.paddingLeft = style.paddingRight = 4;
            style.paddingTop = 2;
            style.paddingBottom = 4;

            // The swatch along the top, at the right.
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.justifyContent = Justify.FlexEnd;
            top.style.flexShrink = 0;
            colorField = new ColorField
            {
                showAlpha = false,
                showEyeDropper = false,
                hdr = false,
                tooltip = "Note color: here and on the minimap",
            };
            colorField.style.width = 18;
            colorField.style.height = 12;
            colorField.style.marginTop = colorField.style.marginBottom = 0;
            colorField.SetValueWithoutNotify(note.Color);
            colorField.RegisterValueChangedCallback(e => SetColor(e.newValue));
            top.Add(colorField);
            Add(top);

            // The text, scrolling when it's taller than the note.
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            textLabel = new Label(string.IsNullOrEmpty(note.Text) ? "" : note.Text);
            textLabel.style.whiteSpace = WhiteSpace.Normal;
            textLabel.style.paddingLeft = textLabel.style.paddingRight = 2;
            textLabel.enableRichText = true;
            scroll.Add(textLabel);
            textEditor = new TextField { multiline = true };
            textEditor.style.whiteSpace = WhiteSpace.Normal;
            textEditor.style.display = DisplayStyle.None;
            textEditor.style.marginLeft = textEditor.style.marginRight = 0;
            var input = textEditor.Q(TextField.textInputUssName);
            // See-through, so the note shows behind the text being edited.
            input.style.backgroundColor = Color.clear;
            input.style.borderTopWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 0;
            input.style.paddingLeft = input.style.paddingRight = 2;
            input.RegisterCallback<KeyDownEvent>(OnTextKeyDown, TrickleDown.TrickleDown);
            input.RegisterCallback<FocusOutEvent>(_ => FinishEditing());
            scroll.Add(textEditor);
            Add(scroll);

            capabilities |= Capabilities.Selectable | Capabilities.Movable | Capabilities.Deletable |
                            Capabilities.Resizable | Capabilities.Copiable;

            SetPosition(note.Region);
            // (Caught on the way down, before the scroll view and the canvas get the clicks.)
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            EdgeResizer.Attach(this, new Vector2(MinWidth, MinHeight), () =>
            {
                var graph = Graph;
                if (graph != null)
                    Undo.RegisterCompleteObjectUndo(graph, "Resize Note");
            }, () =>
            {
                var graph = Graph;
                if (graph != null)
                    EditorUtility.SetDirty(graph);
            });
            // Resized from the corner: kept.
            RegisterCallback<GeometryChangedEvent>(_ => KeepSize());
            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
            ApplyColor();
            ApplyFontSize();
        }

        public virtual void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (!(evt.target is NoteView))
                return;
            var current = Target.Color;
            foreach (var (colorName, color) in Comment.Palette)
            {
                var picked = color;
                evt.menu.AppendAction("Color/" + colorName, _ => SetColor(picked),
                    SameColor(current, picked) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            float currentSize = Target.FontSize;
            foreach (var (sizeName, size) in TextSizes)
            {
                var picked = size;
                evt.menu.AppendAction("Text Size/" + sizeName, _ => SetFontSize(picked),
                    Mathf.Approximately(currentSize, size) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            evt.menu.AppendAction("Edit Text", _ => EditText());
            evt.menu.AppendSeparator();
        }

        private static bool SameColor(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        private Graph Graph => GetFirstAncestorOfType<CanvasView>()?.Graph;

        /// <summary>Recolors the note (undoably; a burst of changes, like dragging in the color picker, is one undo step).</summary>
        public void SetColor(Color color)
        {
            color.a = 1f;
            if (SameColor(color, Target.Color))
            {
                colorField.SetValueWithoutNotify(Target.Color);
                return;
            }
            var graph = Graph;
            double now = EditorApplication.timeSinceStartup;
            if (graph != null && now - lastColorChange > 1.0)
                Undo.RegisterCompleteObjectUndo(graph, "Change Note Color");
            lastColorChange = now;
            Target.Color = color;
            colorField.SetValueWithoutNotify(Target.Color);
            ApplyColor();
            if (graph != null)
                EditorUtility.SetDirty(graph);
        }

        // Ctrl + wheel over the note: its text bigger or smaller (otherwise the wheel scrolls the text, or zooms the canvas).
        private double lastSizeChange = -10;

        private void OnWheel(WheelEvent evt)
        {
            if (!evt.ctrlKey && !evt.commandKey)
                return;
            SetFontSize(Target.FontSize + (evt.delta.y < 0 ? 2f : -2f));
            evt.StopPropagation();
        }

        /// <summary>Sizes the note's text (undoably; a burst of wheel steps is one undo step).</summary>
        public void SetFontSize(float size)
        {
            size = Mathf.Clamp(size, 8f, 96f);
            if (Mathf.Approximately(size, Target.FontSize))
                return;
            var graph = Graph;
            double now = EditorApplication.timeSinceStartup;
            if (graph != null && now - lastSizeChange > 1.0)
                Undo.RegisterCompleteObjectUndo(graph, "Note Text Size");
            lastSizeChange = now;
            Target.FontSize = size;
            ApplyFontSize();
            if (graph != null)
                EditorUtility.SetDirty(graph);
        }

        private void ApplyFontSize()
        {
            textLabel.style.fontSize = Target.FontSize;
            textEditor.style.fontSize = Target.FontSize;
            var input = textEditor.Q(TextField.textInputUssName);
            if (input != null)
                input.style.fontSize = Target.FontSize;
        }

        /// <summary>A box in the note's color, with dark text on light colors and light on dark; the usual blue outline when selected.</summary>
        public void ApplyColor()
        {
            var color = Target.Color;
            style.backgroundColor = new Color(color.r, color.g, color.b, Opacity);
            var border = selected ? (Color)new Color32(0x44, 0xC0, 0xFF, 0xFF) : Color.Lerp(color, Color.black, 0.25f);
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = border;
            float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            var textColor = luminance > 0.5f ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.95f, 0.95f, 0.95f);
            textLabel.style.color = textColor;
            // The text being edited in the same color (its field is see-through, on the note).
            textEditor.style.color = textColor;
            foreach (var text in textEditor.Query<TextElement>().ToList())
                text.style.color = textColor;
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

        // Double-click anywhere on the note (but its swatch and edge grips) to edit its text.
        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2 || textEditor.style.display != DisplayStyle.None)
                return;
            if (evt.target is VisualElement element && (colorField.Contains(element) || element.ClassListContains("edge-grip")))
                return;
            EditText();
            evt.StopPropagation();
        }

        /// <summary>Edits the text in place.</summary>
        public void EditText()
        {
            textLabel.style.display = DisplayStyle.None;
            textEditor.SetValueWithoutNotify(Target.Text ?? "");
            textEditor.style.display = DisplayStyle.Flex;
            textEditor.Q(TextField.textInputUssName).Focus();
        }

        private void OnTextKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                isEditingCancelled = true;
                FinishEditing();
                evt.StopPropagation();
            }
        }

        private void FinishEditing()
        {
            if (textEditor.style.display == DisplayStyle.None)
                return;
            textEditor.style.display = DisplayStyle.None;
            textLabel.style.display = DisplayStyle.Flex;
            if (!isEditingCancelled && textEditor.value != Target.Text)
            {
                var graph = Graph;
                if (graph != null)
                    Undo.RegisterCompleteObjectUndo(graph, "Edit Note");
                Target.Text = textEditor.value;
                textLabel.text = Target.Text;
                if (graph != null)
                    EditorUtility.SetDirty(graph);
            }
            isEditingCancelled = false;
        }

        public override void SetPosition(Rect newPos)
        {
            newPos.width = Mathf.Max(newPos.width, MinWidth);
            newPos.height = Mathf.Max(newPos.height, MinHeight);
            base.SetPosition(newPos);
            Target.Region = newPos;
        }

        // The corner handle resizes the element directly: its new size is kept in the note.
        private void KeepSize()
        {
            var size = layout.size;
            if (size.x <= 0f || size.y <= 0f)
                return;
            var region = Target.Region;
            if (Mathf.Approximately(region.width, size.x) && Mathf.Approximately(region.height, size.y))
                return;
            Target.Region = new Rect(region.position, size);
            var graph = Graph;
            if (graph != null)
                EditorUtility.SetDirty(graph);
        }

        public void Dirty() { }

        public void Update() { }
    }
}
