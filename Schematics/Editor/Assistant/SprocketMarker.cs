using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// "This one!": while Sprocket explains something, corner brackets frame it (or, for a spot, a soft ping ring
    /// pulses there), with a little trail of dots running from Sprocket to it. Drawn with vectors, only while shown.
    /// </summary>
    internal sealed class SprocketMarker : VisualElement
    {
        private static readonly Color Accent = new(0.93f, 0.52f, 0.78f);
        private const float Pad = 5f, Spacing = 9f;

        private Rect _target;     // in the host root's space
        private bool _exact;      // a real outline (else just a point to ping)
        private Vector2 _from;    // Sprocket's side facing it
        private float _time;

        public SprocketMarker()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.display = DisplayStyle.None;
            generateVisualContent += Draw;
        }

        public bool Shown => style.display == DisplayStyle.Flex;

        public void Show(Rect target, bool exact, Vector2 hostSize)
        {
            _target = target;
            _exact = exact;
            _time = 0f;
            // Sized to the window so it's never culled; it draws anywhere inside.
            style.width = Mathf.Max(1f, hostSize.x);
            style.height = Mathf.Max(1f, hostSize.y);
            style.display = DisplayStyle.Flex;
            MarkDirtyRepaint();
        }

        public void Hide() => style.display = DisplayStyle.None;

        /// <summary>Animates it, following Sprocket (in the host root's space).</summary>
        public void Advance(float dt, Rect sprocket)
        {
            if (!Shown) return;
            _time += dt;
            bool right = _target.center.x > sprocket.center.x;
            _from = new Vector2(right ? sprocket.xMax - 8f : sprocket.xMin + 8f, sprocket.y + sprocket.height * 0.45f);
            // A gentle breath.
            style.opacity = 0.75f + 0.25f * Mathf.Sin(_time * 4f);
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            Vector2 end;

            if (_exact)
            {
                var r = new Rect(_target.x - Pad, _target.y - Pad, _target.width + Pad * 2f, _target.height + Pad * 2f);

                // A faint wash over it...
                painter.fillColor = new Color(Accent.r, Accent.g, Accent.b, 0.08f);
                painter.BeginPath();
                painter.MoveTo(r.min);
                painter.LineTo(new Vector2(r.xMax, r.yMin));
                painter.LineTo(r.max);
                painter.LineTo(new Vector2(r.xMin, r.yMax));
                painter.ClosePath();
                painter.Fill();

                // ...and corner brackets that nudge in and out.
                float nudge = Mathf.Sin(_time * 5f) * 1.5f;
                r = new Rect(r.x - nudge, r.y - nudge, r.width + nudge * 2f, r.height + nudge * 2f);
                float arm = Mathf.Min(14f, r.width * 0.35f, r.height * 0.35f);
                painter.strokeColor = Accent;
                painter.lineWidth = 2.5f;
                Corner(painter, new Vector2(r.xMin, r.yMin), arm, arm);
                Corner(painter, new Vector2(r.xMax, r.yMin), -arm, arm);
                Corner(painter, new Vector2(r.xMin, r.yMax), arm, -arm);
                Corner(painter, new Vector2(r.xMax, r.yMax), -arm, -arm);
                end = Nearest(r, _from);
            }
            else
            {
                // A ping: a dot, a ring, and a ring rippling outward.
                var center = _target.center;
                painter.fillColor = Accent;
                Circle(painter, center, 3.5f);
                painter.Fill();
                painter.strokeColor = Accent;
                painter.lineWidth = 2f;
                Circle(painter, center, 10f);
                painter.Stroke();
                float t = _time % 1.2f / 1.2f;
                painter.strokeColor = new Color(Accent.r, Accent.g, Accent.b, 1f - t);
                painter.lineWidth = 2f * (1f - t) + 0.5f;
                Circle(painter, center, 10f + 16f * t);
                painter.Stroke();
                end = center + (_from - center).normalized * 13f;
            }

            // A trail of dots from Sprocket, gently arcing, marching toward it.
            float distance = Vector2.Distance(_from, end);
            if (distance < 14f) return;
            var control = (_from + end) * 0.5f + Vector2.down * Mathf.Min(36f, distance * 0.3f);
            float length = distance * 1.1f;
            float march = _time * 18f % Spacing;
            for (float along = march; along < length; along += Spacing)
            {
                float u = along / length;
                var point = (1 - u) * (1 - u) * _from + 2 * (1 - u) * u * control + u * u * end;
                float fade = Mathf.Clamp01(u * 4f);
                painter.fillColor = new Color(Accent.r, Accent.g, Accent.b, 0.9f * fade);
                Circle(painter, point, 1.4f + 0.8f * u);
                painter.Fill();
            }
        }

        private static void Corner(Painter2D painter, Vector2 corner, float dx, float dy)
        {
            painter.BeginPath();
            painter.MoveTo(corner + new Vector2(0f, dy));
            painter.LineTo(corner);
            painter.LineTo(corner + new Vector2(dx, 0f));
            painter.Stroke();
        }

        private static void Circle(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
        }

        private static Vector2 Nearest(Rect rect, Vector2 point)
            => new(Mathf.Clamp(point.x, rect.xMin, rect.xMax), Mathf.Clamp(point.y, rect.yMin, rect.yMax));
    }
}
