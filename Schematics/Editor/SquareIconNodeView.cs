using BlueGraph;
using BlueGraph.Editor;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// A math node (Add, Multiply, Sin, And... in any kind of graph: see <see cref="ISquareIconNode"/>): a flat square with no title bar and an icon of what it does in
    /// the middle (its symbol, or a plot of its curve). Values come in on the left (a number typed beside each pin
    /// that has nothing wired in), the result goes out on the right, and its name shows on hover (and under it, if it
    /// was given one of its own).
    /// </summary>
    [CustomNodeView(typeof(MathNode))]
    [CustomNodeView(typeof(BooleanNode))]
    public class SquareIconNodeView : NodeView
    {
        private const float Size = 64f;
        private const float IconSize = 34f;
        private const float PinBox = 14f;
        private const float PinInset = 4f;

        /// <summary>Operators (+, ×, and...) are green, functions of one value (Sin, Abs...) yellow; both glossy, with white icons.</summary>
        public static readonly Color OperatorColor = new(0.16f, 0.66f, 0.30f);
        public static readonly Color FunctionColor = new(0.93f, 0.71f, 0.10f);
        private static readonly Color IconColor = Color.white;

        private ISquareIconNode Op => (ISquareIconNode)Target;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            AddToClassList("square-node");
            var color = Op.IsFunction ? FunctionColor : OperatorColor;

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
                border.style.backgroundColor = color;
                border.style.overflow = Overflow.Visible;
                border.style.paddingLeft = border.style.paddingRight = border.style.paddingTop = border.style.paddingBottom = 0;
                // Glossy: a lighter rim on top, a darker one below, and a crisp shine over the upper half.
                border.style.borderTopWidth = border.style.borderBottomWidth = border.style.borderLeftWidth = border.style.borderRightWidth = 1;
                border.style.borderTopColor = Color.Lerp(color, Color.white, 0.55f);
                border.style.borderLeftColor = border.style.borderRightColor = Color.Lerp(color, Color.black, 0.15f);
                border.style.borderBottomColor = Color.Lerp(color, Color.black, 0.45f);
                NodeGloss.Apply(border, 8f);
            }
            // (Nodes' body gradient would darken the colour under the shine.)
            var body = this.Q("contents");
            if (body != null)
            {
                body.style.backgroundImage = StyleKeyword.None;
                body.style.backgroundColor = Color.clear;
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

            // The icon, dead centre: a frame filling the square, centring it.
            var middle = new VisualElement { name = "math-icon-frame", pickingMode = PickingMode.Ignore };
            middle.style.position = Position.Absolute;
            middle.style.left = middle.style.top = middle.style.right = middle.style.bottom = 0;
            middle.style.alignItems = Align.Center;
            middle.style.justifyContent = Justify.Center;
            var icon = new VisualElement { name = "math-icon", pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = IconSize;
            icon.style.flexShrink = 0;
            middle.Add(icon);
            if (!string.IsNullOrEmpty(Op.Symbol))
            {
                var symbol = new Label(Op.Symbol) { pickingMode = PickingMode.Ignore };
                symbol.style.position = Position.Absolute;
                symbol.style.left = symbol.style.top = symbol.style.right = symbol.style.bottom = 0;
                symbol.style.unityTextAlign = TextAnchor.MiddleCenter;
                symbol.style.fontSize = Op.Symbol.Length > 2 ? 14 : Op.Symbol.Length > 1 ? 16 : 26;
                symbol.style.unityFontStyleAndWeight = FontStyle.Bold;
                symbol.style.color = IconColor;
                // A faint shadow keeps white readable on the yellow.
                symbol.style.textShadow = new TextShadow { offset = new Vector2(0f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.35f) };
                symbol.style.marginLeft = symbol.style.marginRight = symbol.style.marginTop = symbol.style.marginBottom = 0;
                symbol.style.paddingLeft = symbol.style.paddingRight = symbol.style.paddingTop = symbol.style.paddingBottom = 0;
                icon.Add(symbol);
            }
            else
                icon.generateVisualContent += DrawPlot;
            (border ?? (VisualElement)this).Add(middle);

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

        // A pin with no name beside it (the square's too small; hovering it shows its name): just its circle, in a
        // box of the same size on both sides, so inputs and outputs sit the same distance in.
        private static void Bare(PortView view)
        {
            var label = view.Q<Label>("type");
            if (label != null)
                label.style.display = DisplayStyle.None;
            view.tooltip = view.portName;
            view.style.width = view.style.minWidth = view.style.maxWidth = PinBox;
            view.style.height = 18;
            view.style.paddingLeft = view.style.paddingRight = view.style.paddingTop = view.style.paddingBottom = 0;
            view.style.marginLeft = view.style.marginRight = 0;
            view.style.justifyContent = Justify.Center;
            view.style.alignItems = Align.Center;
            var connector = view.Q("connector");
            if (connector != null)
                connector.style.marginLeft = connector.style.marginRight = 0;
        }

        private static void Side(VisualElement container, bool left)
        {
            container.style.position = Position.Absolute;
            container.style.top = 0;
            container.style.bottom = 0;
            container.style.width = PinBox;
            container.style.alignItems = Align.Center;
            // The circles' centres a few pixels inside the square's edge, the same on both sides.
            if (left)
                container.style.left = PinInset - PinBox / 2f;
            else
                container.style.right = PinInset - PinBox / 2f;
            container.style.flexDirection = FlexDirection.Column;
            container.style.justifyContent = Justify.SpaceAround;
            container.style.paddingTop = container.style.paddingBottom = 8;
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
                painter.strokeColor = new Color(1f, 1f, 1f, 0.35f);
                painter.lineWidth = 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.xMin, zero));
                painter.LineTo(new Vector2(rect.xMax, zero));
                painter.Stroke();
            }

            // The curve, lifting the pen at jumps (Floor's steps, Fraction's wrap, Sign's flip).
            painter.strokeColor = IconColor;
            painter.lineWidth = 2.4f;
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
