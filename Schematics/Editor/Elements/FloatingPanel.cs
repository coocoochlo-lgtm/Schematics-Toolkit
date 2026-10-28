using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// A panel floating over a graph canvas (a preview, say): drag its header to move it, drop it near a corner to dock
    /// it there, and drag the grip on its inner corner to size it. It keeps clear of the minimap, remembers where it
    /// was and how big (per <c>key</c>), and keeps its clicks, scrolls and right-clicks to itself, so the canvas
    /// underneath doesn't pan, zoom or open its menu.
    /// </summary>
    public class FloatingPanel : VisualElement
    {
        /// <summary>Where it's docked (or None: wherever it was dropped).</summary>
        public enum Corner { None, TopLeft, TopRight, BottomLeft, BottomRight }

        private const float Margin = 8f, Gap = 6f, SnapDistance = 70f, GripSize = 14f;
        private static readonly Vector2 MinSize = new(200f, 140f);

        /// <summary>Its title row: what it is and its buttons. Dragging an empty part of it moves the panel.</summary>
        public VisualElement Header { get; }
        /// <summary>What it shows: fills the space under the header.</summary>
        public VisualElement Body { get; }

        private readonly string _key;
        private readonly VisualElement _grip;
        private Corner _corner;
        private Vector2 _position, _size;
        private bool _collapsed;

        // A move or resize in progress.
        private bool _moving, _resizing;
        private Vector2 _pointerStart, _positionStart, _sizeStart;
        private int _pointer = -1;

        public FloatingPanel(string key, Vector2 size, Corner corner = Corner.BottomRight)
        {
            _key = "Remedy.FloatingPanel." + key;
            _corner = (Corner)EditorPrefs.GetInt(_key + ".Corner", (int)corner);
            _size = new Vector2(EditorPrefs.GetFloat(_key + ".Width", size.x), EditorPrefs.GetFloat(_key + ".Height", size.y));
            _position = new Vector2(EditorPrefs.GetFloat(_key + ".X", 40f), EditorPrefs.GetFloat(_key + ".Y", 40f));

            style.position = Position.Absolute;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.94f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 6;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 6;
            style.flexDirection = FlexDirection.Column;

            Header = new VisualElement { name = "floating-panel-header", tooltip = "Drag to move; drop near a corner to dock it there." };
            Header.style.flexDirection = FlexDirection.Row;
            Header.style.alignItems = Align.Center;
            Header.style.flexShrink = 0;
            Add(Header);
            Body = new VisualElement { name = "floating-panel-body" };
            Body.style.flexGrow = 1;
            Body.style.flexDirection = FlexDirection.Column;
            Add(Body);

            _grip = new VisualElement { name = "floating-panel-grip", tooltip = "Drag to resize." };
            _grip.style.position = Position.Absolute;
            _grip.style.width = _grip.style.height = GripSize;
            _grip.generateVisualContent += DrawGrip;
            Add(_grip);

            Header.RegisterCallback<PointerDownEvent>(e =>
            {
                // Only on the header itself (or its labels), not its buttons and fields.
                if (e.button != 0 || IsControl(e.target as VisualElement))
                    return;
                StartDrag(e, resize: false);
            });
            _grip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button == 0)
                    StartDrag(e, resize: true);
            });
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);

            // Its input stays with it: the canvas under it mustn't pan, zoom, select or open its context menu.
            RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
            RegisterCallback<MouseUpEvent>(e => e.StopPropagation());
            RegisterCallback<WheelEvent>(e => e.StopPropagation());
            RegisterCallback<ContextClickEvent>(e => e.StopPropagation());
            RegisterCallback<ContextualMenuPopulateEvent>(e => e.StopImmediatePropagation());

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                parent?.RegisterCallback<GeometryChangedEvent>(OnParentResized);
                Place();
            });
            RegisterCallback<DetachFromPanelEvent>(_ => parent?.UnregisterCallback<GeometryChangedEvent>(OnParentResized));
            // The minimap can grow or move (an inset under it): keep checking for it.
            schedule.Execute(Place).Every(300);
        }

        /// <summary>Folds it down to just its header (or opens it again).</summary>
        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                _collapsed = value;
                Body.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                _grip.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                Place();
            }
        }

        private static bool IsControl(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e.name == "floating-panel-header")
                    return false;
                if (e is Button || e is BaseField<System.Enum> || e.focusable)
                    return true;
            }
            return false;
        }

        private void OnParentResized(GeometryChangedEvent e) => Place();

        // ---- Moving and sizing ----

        private void StartDrag(PointerDownEvent e, bool resize)
        {
            var area = Area();
            _moving = !resize;
            _resizing = resize;
            _pointer = e.pointerId;
            _pointerStart = e.position;
            // Where it is now, as a free position (a docked panel comes loose when it's moved).
            var rect = CurrentRect(area);
            _positionStart = rect.position;
            _sizeStart = rect.size;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if ((!_moving && !_resizing) || e.pointerId != _pointer)
                return;
            var delta = (Vector2)e.position - _pointerStart;
            var area = Area();
            if (_moving)
            {
                _corner = Corner.None;
                _position = _positionStart + delta;
            }
            else
            {
                // The grip is on the inner corner: dragging it away from the docked corner grows the panel.
                var (growX, growY) = GrowDirections();
                var size = new Vector2(_sizeStart.x + delta.x * growX, _sizeStart.y + delta.y * growY);
                _size = Vector2.Max(MinSize, Vector2.Min(size, area.size - Vector2.one * Margin * 2f));
                if (_corner == Corner.None)
                {
                    // Floating: the grip's at the bottom right, the top left stays put.
                    _position = _positionStart;
                }
            }
            Place();
            e.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if ((!_moving && !_resizing) || e.pointerId != _pointer)
                return;
            if (_moving)
                _corner = NearCorner(Area());
            _moving = _resizing = false;
            this.ReleasePointer(e.pointerId);
            Place();
            Save();
            e.StopPropagation();
        }

        // Which way the size grows as the grip moves right and down.
        private (float x, float y) GrowDirections() => _corner switch
        {
            Corner.TopRight => (-1f, 1f),
            Corner.BottomLeft => (1f, -1f),
            Corner.BottomRight => (-1f, -1f),
            _ => (1f, 1f),
        };

        // The corner it was dropped close to, if any.
        private Corner NearCorner(Rect area)
        {
            var rect = CurrentRect(area);
            bool left = rect.xMin - area.xMin < SnapDistance, right = area.xMax - rect.xMax < SnapDistance;
            bool top = rect.yMin - area.yMin < SnapDistance, bottom = area.yMax - rect.yMax < SnapDistance;
            if (top && left) return Corner.TopLeft;
            if (top && right) return Corner.TopRight;
            if (bottom && left) return Corner.BottomLeft;
            if (bottom && right) return Corner.BottomRight;
            return Corner.None;
        }

        private void Save()
        {
            EditorPrefs.SetInt(_key + ".Corner", (int)_corner);
            EditorPrefs.SetFloat(_key + ".Width", _size.x);
            EditorPrefs.SetFloat(_key + ".Height", _size.y);
            EditorPrefs.SetFloat(_key + ".X", _position.x);
            EditorPrefs.SetFloat(_key + ".Y", _position.y);
        }

        // ---- Placing ----

        private Rect Area() => parent != null ? new Rect(0f, 0f, parent.layout.width, parent.layout.height) : new Rect(0f, 0f, 800f, 600f);

        private float Height => _collapsed ? Mathf.Max(24f, Header.layout.height + 12f) : _size.y;

        // Where it sits: in its corner (moved off the minimap if they'd overlap), or where it was dropped (kept inside).
        private Rect CurrentRect(Rect area)
        {
            float width = Mathf.Min(_size.x, Mathf.Max(MinSize.x, area.width - Margin * 2f));
            float height = Mathf.Min(Height, Mathf.Max(24f, area.height - Margin * 2f));
            Rect rect;
            switch (_corner)
            {
                case Corner.TopLeft: rect = new Rect(area.xMin + Margin, area.yMin + Margin, width, height); break;
                case Corner.TopRight: rect = new Rect(area.xMax - Margin - width, area.yMin + Margin, width, height); break;
                case Corner.BottomLeft: rect = new Rect(area.xMin + Margin, area.yMax - Margin - height, width, height); break;
                case Corner.BottomRight: rect = new Rect(area.xMax - Margin - width, area.yMax - Margin - height, width, height); break;
                default:
                    var p = new Vector2(Mathf.Clamp(_position.x, area.xMin, Mathf.Max(area.xMin, area.xMax - width)),
                                        Mathf.Clamp(_position.y, area.yMin, Mathf.Max(area.yMin, area.yMax - height)));
                    return new Rect(p, new Vector2(width, height));
            }
            // Docked where the minimap is: above it (bottom corners) or below it (top corners).
            var minimap = Minimap();
            if (minimap.HasValue && minimap.Value.Overlaps(rect))
            {
                var m = minimap.Value;
                if (_corner is Corner.BottomLeft or Corner.BottomRight)
                    rect.y = Mathf.Max(area.yMin + Margin, m.yMin - Gap - height);
                else
                    rect.y = Mathf.Min(area.yMax - Margin - height, m.yMax + Gap);
            }
            return rect;
        }

        private Rect? Minimap()
        {
            if (parent == null || panel == null)
                return null;
            var minimap = panel.visualTree.Q<SchematicMinimap>();
            if (minimap == null || minimap.resolvedStyle.display == DisplayStyle.None || minimap.worldBound.width <= 0f)
                return null;
            var bound = minimap.worldBound;
            var min = parent.WorldToLocal(bound.min);
            var max = parent.WorldToLocal(bound.max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void Place()
        {
            if (parent == null)
                return;
            var rect = CurrentRect(Area());
            style.left = rect.x;
            style.top = rect.y;
            style.right = StyleKeyword.Auto;
            style.bottom = StyleKeyword.Auto;
            style.width = rect.width;
            style.height = _collapsed ? StyleKeyword.Auto : rect.height;

            // The grip on the inner corner (towards the middle of the canvas): bottom right when it's floating.
            var (growX, growY) = GrowDirections();
            _grip.style.left = growX < 0 ? 0f : StyleKeyword.Auto;
            _grip.style.right = growX < 0 ? StyleKeyword.Auto : 0f;
            _grip.style.top = growY < 0 ? 0f : StyleKeyword.Auto;
            _grip.style.bottom = growY < 0 ? StyleKeyword.Auto : 0f;
            _grip.MarkDirtyRepaint();
        }

        // Three short diagonal lines, angled into its corner.
        private void DrawGrip(MeshGenerationContext context)
        {
            var (growX, growY) = GrowDirections();
            var painter = context.painter2D;
            painter.strokeColor = new Color(1f, 1f, 1f, 0.35f);
            painter.lineWidth = 1.2f;
            for (int i = 1; i <= 3; i++)
            {
                float d = i * 4f;
                // The corner the grip hugs: right/bottom when growing that way, else left/top.
                float cx = growX > 0 ? GripSize - 2f : 2f, cy = growY > 0 ? GripSize - 2f : 2f;
                float sx = growX > 0 ? -1f : 1f, sy = growY > 0 ? -1f : 1f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(cx + sx * d, cy));
                painter.LineTo(new Vector2(cx, cy + sy * d));
                painter.Stroke();
            }
        }
    }
}
