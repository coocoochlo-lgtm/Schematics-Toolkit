using BlueGraph;
using BlueGraph.Editor;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Audio
{
    /// <summary>
    /// A Sound Cue math node (Add, Multiply, Sine Wave...): a flat square with no title bar and an icon of what it does in
    /// the middle (its symbol, or a plot of its curve). Values come in on the left (a number typed beside each pin
    /// that has nothing wired in), the result goes out on the right, and its name shows on hover (and under it, if it
    /// was given one of its own).
    /// </summary>
    [CustomNodeView(typeof(SoundMathOpNode))]
    public class SoundMathNodeView : NodeView
    {
        private const float Size = 64f;
        private const float IconSize = 36f;

        private SoundMathOpNode Op => (SoundMathOpNode)Target;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            AddToClassList("square-node");
            var color = Target.GetType().GetCustomAttribute<NodeColorAttribute>(true)?.Color ?? new Color(0.16f, 0.54f, 0.54f);

            titleContainer.style.display = DisplayStyle.None;
            extensionContainer.style.display = DisplayStyle.None;
            var divider = this.Q("divider");
            if (divider != null)
                divider.style.display = DisplayStyle.None;
            style.width = style.minWidth = style.maxWidth = Size;
            style.height = style.minHeight = style.maxHeight = Size;

            var border = this.Q("node-border");
            if (border != null)
            {
                border.style.width = border.style.height = Size;
                border.style.borderTopLeftRadius = border.style.borderTopRightRadius =
                    border.style.borderBottomLeftRadius = border.style.borderBottomRightRadius = 8;
                border.style.backgroundColor = new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 0.96f);
                border.style.overflow = Overflow.Visible;
            }
            var selection = this.Q("selection-border");
            if (selection != null)
                selection.style.borderTopLeftRadius = selection.style.borderTopRightRadius =
                    selection.style.borderBottomLeftRadius = selection.style.borderBottomRightRadius = 9;

            // Pins down the sides, centred top to bottom, over the square.
            var contents = this.Q("contents");
            if (contents != null)
                contents.style.height = Size;
            var top = this.Q("top");
            if (top != null)
            {
                top.style.position = Position.Absolute;
                top.style.left = top.style.right = top.style.top = top.style.bottom = 0;
            }
            Side(inputContainer, left: true);
            Side(outputContainer, left: false);

            // The icon, in the middle.
            var icon = new VisualElement { name = "math-icon", pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.left = icon.style.top = (Size - IconSize) / 2f;
            icon.style.width = icon.style.height = IconSize;
            if (!string.IsNullOrEmpty(Op.Symbol))
            {
                var symbol = new Label(Op.Symbol) { pickingMode = PickingMode.Ignore };
                symbol.style.flexGrow = 1;
                symbol.style.unityTextAlign = TextAnchor.MiddleCenter;
                symbol.style.fontSize = Op.Symbol.Length > 1 ? 15 : 26;
                symbol.style.unityFontStyleAndWeight = FontStyle.Bold;
                symbol.style.color = new Color(0.92f, 0.97f, 0.97f);
                symbol.style.marginLeft = symbol.style.marginRight = symbol.style.marginTop = symbol.style.marginBottom = 0;
                symbol.style.paddingLeft = symbol.style.paddingRight = symbol.style.paddingTop = symbol.style.paddingBottom = 0;
                icon.Add(symbol);
            }
            else
                icon.generateVisualContent += DrawPlot;
            (border ?? (VisualElement)this).Add(icon);

            // Its name of its own, under it (the name of what it does is the icon, and shows on hover).
            if (!string.IsNullOrEmpty(Target.CustomName))
            {
                var caption = new Label(Target.CustomName) { pickingMode = PickingMode.Ignore };
                caption.style.position = Position.Absolute;
                caption.style.top = Size + 3;
                caption.style.left = -48;
                caption.style.width = Size + 96;
                caption.style.unityTextAlign = TextAnchor.UpperCenter;
                caption.style.whiteSpace = WhiteSpace.Normal;
                caption.style.fontSize = 10;
                caption.style.color = new Color(0.8f, 0.8f, 0.8f, 0.85f);
                Add(caption);
            }

            tooltip = string.IsNullOrEmpty(Target.CustomName) ? ReflectionData?.Name ?? Target.Name : $"{Target.CustomName} ({ReflectionData?.Name ?? Target.Name})";
        }

        protected override void AddInputPort(Port port)
        {
            base.AddInputPort(port);
            var view = Inputs[Inputs.Count - 1];
            Bare(view);
            // The number typed in for an unwired pin sits outside the square, to the left of its pin.
            var field = view.Q(className: "property-field-container");
            if (field != null)
            {
                field.style.position = Position.Absolute;
                field.style.right = new Length(100, LengthUnit.Percent);
                field.style.width = 44;
                field.style.marginRight = 2;
                field.Query<Label>(className: "unity-base-field__label").ForEach(label => label.style.display = DisplayStyle.None);
            }
        }

        protected override void AddOutputPort(Port port)
        {
            base.AddOutputPort(port);
            Bare(Outputs[Outputs.Count - 1]);
        }

        // A pin with no name beside it (the square's too small; hovering it shows its name).
        private static void Bare(PortView view)
        {
            var label = view.Q<Label>("type");
            if (label != null)
                label.style.display = DisplayStyle.None;
            view.tooltip = view.portName;
            view.style.height = 20;
            view.style.minWidth = 0;
            view.style.paddingLeft = view.style.paddingRight = 0;
        }

        private static void Side(VisualElement container, bool left)
        {
            container.style.position = Position.Absolute;
            container.style.top = 0;
            container.style.bottom = 0;
            if (left)
                container.style.left = -4;
            else
                container.style.right = -4;
            container.style.flexDirection = FlexDirection.Column;
            container.style.justifyContent = Justify.SpaceAround;
            container.style.paddingTop = container.style.paddingBottom = 6;
            container.style.backgroundColor = Color.clear;
            container.style.overflow = Overflow.Visible;
        }

        // The node's own operation over its plot range: what it does, drawn.
        private void DrawPlot(MeshGenerationContext context)
        {
            var rect = context.visualElement.contentRect;
            if (rect.width < 4 || rect.height < 4)
                return;
            const int samples = 64;
            var range = Op.PlotRange;
            var x = new float[samples];
            var b = new float[samples];
            var y = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                x[i] = Mathf.Lerp(range.x, range.y, i / (samples - 1f));
                b[i] = Op.PlotB;
            }
            try
            {
                Op.Evaluate(x, b, y, samples);
            }
            catch
            {
                return;
            }
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < samples; i++)
                if (!float.IsNaN(y[i]) && !float.IsInfinity(y[i]))
                {
                    min = Mathf.Min(min, y[i]);
                    max = Mathf.Max(max, y[i]);
                }
            if (min > max)
                return;
            if (max - min < 1e-4f)
            {
                min -= 1f;
                max += 1f;
            }

            Vector2 Point(int i) => new(rect.xMin + rect.width * i / (samples - 1f), rect.yMax - rect.height * (y[i] - min) / (max - min));

            var painter = context.painter2D;
            // Zero, faintly, where it's in view.
            if (min < 0f && max > 0f)
            {
                float zero = rect.yMax - rect.height * (0f - min) / (max - min);
                painter.strokeColor = new Color(1f, 1f, 1f, 0.18f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.xMin, zero));
                painter.LineTo(new Vector2(rect.xMax, zero));
                painter.Stroke();
            }

            // The curve, lifting the pen at jumps (Floor's steps, Fraction's wrap, Sign's flip).
            painter.strokeColor = new Color(0.92f, 0.97f, 0.97f);
            painter.lineWidth = 2f;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(Point(0));
            for (int i = 1; i < samples; i++)
            {
                if (Mathf.Abs(y[i] - y[i - 1]) > (max - min) * 0.4f)
                    painter.MoveTo(Point(i));
                else
                    painter.LineTo(Point(i));
            }
            painter.Stroke();
        }
    }
}
