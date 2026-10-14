using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Draws the rulers kept in loaded scenes (and lets their ends be dragged when selected), and runs the
    /// temporary Ctrl ruler in blockout scenes: hold Ctrl and drag to measure.
    /// <list type="bullet">
    /// <item>Without Shift the end sits on whatever surface is under the mouse (wall to wall, floor to ledge).</item>
    /// <item>Holding <b>Shift</b> makes it rise straight out of the surface the ruler started on (perpendicular to
    /// it), so it reads a clearance: the height above a floor, the depth out from a wall.</item>
    /// <item>Hold <b>Alt</b> as you let go to keep it as a Distance ruler (or turn on "Keep Ctrl + Drag Rulers" in the
    /// Snap menu to keep every one). Shift used to mean "keep", but it now picks the perpendicular mode.</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    public static class BlockoutRulerDrawer
    {
        private static readonly List<BlockoutRuler> _rulers = new();
        private static bool _dirty = true;
        private static readonly List<Vector3> _arc = new();

        // The temporary ruler.
        private static bool _measuring;
        private static Vector3 _measureStart;
        private static Vector3 _measureEnd;
        // The surface normal where the ruler started (Shift measures straight out along it).
        private static Vector3 _measureNormal = Vector3.up;
        private static Vector2 _lastMouse;
        // A fixed control id for the Ctrl ruler's mouse capture (any id no other control uses).
        private static readonly int _measureControl = "Remedy.LevelDesigner.CtrlRuler".GetHashCode();

        static BlockoutRulerDrawer()
        {
            SceneView.beforeSceneGui += BeforeSceneGUI;
            SceneView.duringSceneGui += DuringSceneGUI;
            EditorApplication.hierarchyChanged += MarkDirty;
            LevelDesignerContext.ModeChanged += MarkDirty;
        }

        /// <summary>Re-collects the rulers on the next repaint.</summary>
        public static void MarkDirty() => _dirty = true;

        /// <summary>True while the Ctrl ruler is measuring.</summary>
        public static bool IsMeasuring => _measuring;

        private static bool ActionKey(Event e) => Application.platform == RuntimePlatform.OSXEditor ? e.command || e.control : e.control;

        // Runs before tools and selection, so Ctrl+click can be claimed before anything else reacts to it. Only the
        // press is taken here (the Scene camera may not be set up for rays yet); the measuring itself happens in
        // DuringSceneGUI, where it is.
        private static void BeforeSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || LevelDesignerContext.Mode != LevelDesignerMode.Blockout || !LevelDesignerSettings.CtrlRuler)
                return;
            if (e.button != 0 || !ActionKey(e) || e.alt || GUIUtility.hotControl != 0)
                return;
            _measuring = true;
            _pendingStart = true;
            _pendingPoint = e.mousePosition;
            GUIUtility.hotControl = _measureControl;
            e.Use();
            view.Repaint();
        }

        private static bool _pendingStart;
        private static Vector2 _pendingPoint;

        // Follows the drag (Shift: perpendicular to the start surface), keeps the ruler on Alt + release (or always,
        // with KeepCtrlRulers), cancels on Esc.
        private static void HandleMeasuring(Event e)
        {
            if (!_measuring)
                return;
            if (_pendingStart)
            {
                if (!LevelDesignerSceneGUI.Raycast(_pendingPoint, out var startHit))
                {
                    Stop();
                    return;
                }
                _pendingStart = false;
                _measureStart = _measureEnd = Snap(startHit.Point);
                _measureNormal = startHit.OnSurface && startHit.Normal.sqrMagnitude > 1e-6f ? startHit.Normal.normalized : Vector3.up;
                _lastMouse = _pendingPoint;
            }

            switch (e.type)
            {
                case EventType.MouseDrag when GUIUtility.hotControl == _measureControl:
                    _lastMouse = e.mousePosition;
                    UpdateEnd(e.shift);
                    e.Use();
                    SceneView.RepaintAll();
                    break;

                // Pressing or letting go of Shift switches mode without having to move the mouse.
                case EventType.KeyDown when e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift:
                case EventType.KeyUp when e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift:
                    UpdateEnd(e.type == EventType.KeyDown);
                    e.Use();
                    SceneView.RepaintAll();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == _measureControl:
                    if ((e.alt || LevelDesignerSettings.KeepCtrlRulers) && Vector3.Distance(_measureStart, _measureEnd) > 0.01f)
                    {
                        var ruler = LevelDesignerSceneGUI.CreateRuler(BlockoutRulerKind.Distance, _measureStart, Quaternion.identity);
                        ruler.End = _measureEnd - _measureStart;
                    }
                    Stop();
                    e.Use();
                    break;

                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    Stop();
                    e.Use();
                    break;
            }

            // Lost the mouse some other way (window lost focus): stop.
            if (_measuring && GUIUtility.hotControl != _measureControl)
                Stop();
        }

        // Moves the ruler's end under the mouse: onto the surface there, or (perpendicular) along the start
        // surface's normal to the point nearest the mouse ray.
        private static void UpdateEnd(bool perpendicular)
        {
            if (perpendicular)
                _measureEnd = PerpendicularEnd(_lastMouse, _measureStart, _measureNormal);
            else if (LevelDesignerSceneGUI.Raycast(_lastMouse, out var endHit))
                _measureEnd = Snap(endHit.Point);
        }

        /// <summary>
        /// The point straight out of a surface (along <paramref name="normal"/> from <paramref name="start"/>) nearest
        /// to the mouse ray, its distance snapped to the grid step.
        /// </summary>
        public static Vector3 PerpendicularEnd(Vector2 guiPoint, Vector3 start, Vector3 normal)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            float along = LevelDesignerSettings.SnapSigned(LevelDesignerSceneGUI.ClosestOnLine(ray, start, normal));
            return start + normal.normalized * along;
        }

        private static void Stop()
        {
            _measuring = false;
            _pendingStart = false;
            if (GUIUtility.hotControl == _measureControl)
                GUIUtility.hotControl = 0;
            SceneView.RepaintAll();
        }

        // The Ctrl ruler snaps its ends to the grid while snapping is on.
        private static Vector3 Snap(Vector3 point) => LevelDesignerSettings.Snap > 0f ? LevelDesignerSettings.SnapPoint(point) : point;

        private static void DuringSceneGUI(SceneView view)
        {
            HandleMeasuring(Event.current);
            if (_measuring && !_pendingStart)
            {
                bool perpendicular = Event.current.shift;
                if (perpendicular && Event.current.type == EventType.Repaint)
                {
                    using (new Handles.DrawingScope(new Color(1f, 0.85f, 0.2f, 0.35f)))
                        Handles.DrawWireDisc(_measureStart, _measureNormal, HandleUtility.GetHandleSize(_measureStart) * 0.3f);
                }
                string hint = (perpendicular ? "Perpendicular (Shift)" : "Shift: perpendicular") +
                              (LevelDesignerSettings.KeepCtrlRulers ? "" : "\nAlt + release to keep");
                LevelDesignerSceneGUI.DrawMeasurement(_measureStart, _measureEnd, new Color(1f, 0.85f, 0.2f), hint);
            }

            if (EditorApplication.isPlaying)
                return;

            if (_dirty)
            {
                _dirty = false;
                _rulers.Clear();
                _rulers.AddRange(Object.FindObjectsByType<BlockoutRuler>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            }

            foreach (var ruler in _rulers)
            {
                if (ruler == null || !ruler.isActiveAndEnabled)
                    continue;
                bool selected = Selection.Contains(ruler.gameObject);
                switch (ruler.Kind)
                {
                    case BlockoutRulerKind.Distance: DrawDistance(ruler, selected); break;
                    case BlockoutRulerKind.Height: DrawHeight(ruler, selected); break;
                    case BlockoutRulerKind.JumpArc: DrawJumpArc(ruler, selected); break;
                    case BlockoutRulerKind.RunDistance: DrawRun(ruler, selected); break;
                }
            }
        }

        private static void DrawDistance(BlockoutRuler ruler, bool selected)
        {
            Vector3 start = ruler.transform.position;
            LevelDesignerSceneGUI.DrawMeasurement(start, ruler.WorldEnd, ruler.Color, ruler.Label);
            if (selected && !BlockoutPlaceTool.IsActive)
                EndHandle(ruler, false);
        }

        private static void DrawHeight(BlockoutRuler ruler, bool selected)
        {
            Vector3 bottom = ruler.transform.position;
            float height = ruler.End.y;
            Vector3 top = bottom + Vector3.up * height;
            if (Event.current.type == EventType.Repaint)
            {
                using (new Handles.DrawingScope(ruler.Color))
                {
                    Handles.DrawLine(bottom, top, 3f);
                    int ticks = Mathf.FloorToInt(Mathf.Abs(height) / 0.5f);
                    for (int i = 1; i <= ticks; i++)
                    {
                        float y = i * 0.5f * Mathf.Sign(height);
                        float size = i % 2 == 0 ? 0.25f : 0.12f;
                        Handles.DrawLine(bottom + Vector3.up * y, bottom + Vector3.up * y + Vector3.right * size);
                    }
                    Handles.DrawWireDisc(bottom, Vector3.up, 0.25f);
                    Handles.DrawWireDisc(top, Vector3.up, 0.25f);
                }
            }
            LevelDesignerSceneGUI.Label(top + Vector3.up * 0.2f,
                (string.IsNullOrEmpty(ruler.Label) ? "" : ruler.Label + "\n") + LevelDesignerSceneGUI.Meters(Mathf.Abs(height)), ruler.Color);
            if (selected && !BlockoutPlaceTool.IsActive)
                EndHandle(ruler, true);
        }

        private static void EndHandle(BlockoutRuler ruler, bool verticalOnly)
        {
            Vector3 end = ruler.WorldEnd;
            EditorGUI.BeginChangeCheck();
            float size = HandleUtility.GetHandleSize(end) * 0.12f;
            Vector3 moved = verticalOnly
                ? Handles.Slider(end, Vector3.up, size, Handles.SphereHandleCap, LevelDesignerSettings.Snap)
                : Handles.FreeMoveHandle(end, size, Vector3.one * LevelDesignerSettings.Snap, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(ruler, "Move Ruler End");
                ruler.End = (LevelDesignerSettings.Snap > 0f ? LevelDesignerSettings.SnapPoint(moved) : moved) - ruler.transform.position;
                if (verticalOnly)
                    ruler.End = new Vector3(0f, ruler.End.y, 0f);
            }
        }

        /// <summary>
        /// Checks a running jump from the ruler: follows the arc until it lands on a surface (true), hits a wall or
        /// ceiling, or falls past the ruler's max drop (false). Fills <paramref name="path"/> with the part flown.
        /// </summary>
        public static bool EvaluateJump(BlockoutRuler ruler, List<Vector3> path, out Vector3 landing)
        {
            landing = default;
            var metrics = ruler.Metrics;
            Vector3 start = ruler.transform.position + Vector3.up * 0.02f;
            metrics.SampleArc(start, ruler.transform.forward, _arc, ruler.MaxDrop, ruler.WithHover);
            path.Clear();
            if (_arc.Count == 0)
                return false;
            path.Add(_arc[0]);
            for (int i = 1; i < _arc.Count; i++)
            {
                Vector3 a = _arc[i - 1], b = _arc[i];
                Vector3 segment = b - a;
                float length = segment.magnitude;
                if (length > 1e-5f && BlockoutShape.RaycastAll(new Ray(a, segment / length), length, out var hit))
                {
                    path.Add(hit.Point);
                    landing = hit.Point;
                    return segment.y <= 0f && hit.Normal.y > 0.6f;
                }
                path.Add(b);
            }
            return false;
        }

        private static readonly List<Vector3> _path = new();

        private static void DrawJumpArc(BlockoutRuler ruler, bool selected)
        {
            var metrics = ruler.Metrics;
            Vector3 start = ruler.transform.position;
            if (!metrics.IsValid)
            {
                LevelDesignerSceneGUI.Label(start + Vector3.up * 0.5f, "Jump Arc: no character numbers.\nPick a character in the Level Designer window.", new Color(1f, 0.6f, 0.3f));
                return;
            }
            if (Event.current.type != EventType.Repaint)
                return;

            bool lands = EvaluateJump(ruler, _path, out var landing);
            var color = lands ? new Color(0.3f, 0.95f, 0.4f) : new Color(1f, 0.3f, 0.25f);

            {
                using (new Handles.DrawingScope(color))
                {
                    Handles.DrawAAPolyLine(4f, _path.ToArray());
                    Handles.DrawWireDisc(start, Vector3.up, 0.2f);
                    Handles.ArrowHandleCap(0, start, Quaternion.LookRotation(ruler.transform.forward), 0.8f, EventType.Repaint);
                }

                // The top of the jump, and the level it starts from (dotted) for reading heights.
                Vector3 forward = Flat(ruler.transform.forward);
                float apexTime = metrics.RiseTime + metrics.HangTime * 0.5f;
                Vector3 apex = start + forward * metrics.ForwardAt(apexTime, ruler.WithHover) + Vector3.up * metrics.JumpHeight;
                Vector3 flatLanding = start + forward * metrics.JumpDistance;
                using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, 0.4f)))
                {
                    Handles.DrawDottedLine(start, flatLanding, 4f);
                    Handles.DrawDottedLine(apex, apex - Vector3.up * metrics.JumpHeight, 4f);
                }
                LevelDesignerSceneGUI.Label(apex + Vector3.up * 0.25f, "↑ " + LevelDesignerSceneGUI.Meters(metrics.JumpHeight), color);
                LevelDesignerSceneGUI.Label(flatLanding, "↔ " + LevelDesignerSceneGUI.Meters(metrics.JumpDistance) + " (same height)", color);

                string title = string.IsNullOrEmpty(ruler.Label) ? metrics.CharacterName : ruler.Label;
                if (lands)
                {
                    float across = Vector3.Distance(Flat(start), Flat(landing));
                    float rise = landing.y - start.y;
                    LevelDesignerSceneGUI.Label(landing + Vector3.up * 0.4f,
                        $"{title}: lands\n↔ {LevelDesignerSceneGUI.Meters(across)}  ↕ {(rise >= 0 ? "+" : "")}{BlockoutShape.FormatMeters(rise)} m", color);
                }
                else
                {
                    Vector3 end = _path.Count > 0 ? _path[_path.Count - 1] : start;
                    LevelDesignerSceneGUI.Label(end + Vector3.up * 0.4f, $"{title}: doesn't make it", color);
                }
            }
        }

        private static void DrawRun(BlockoutRuler ruler, bool selected)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var metrics = ruler.Metrics;
            Vector3 start = ruler.transform.position + Vector3.up * 0.02f;
            if (!metrics.IsValid)
            {
                LevelDesignerSceneGUI.Label(start + Vector3.up * 0.5f, "Run ruler: no character numbers.\nPick a character in the Level Designer window.", new Color(1f, 0.6f, 0.3f));
                return;
            }
            Vector3 forward = Flat(ruler.transform.forward);
            float total = metrics.RunDistance(ruler.Seconds);
            Vector3 end = start + forward * total;
            Vector3 side = Vector3.Cross(Vector3.up, forward) * 0.2f;
            using (new Handles.DrawingScope(ruler.Color))
            {
                Handles.DrawLine(start, end, 3f);
                for (int second = 0; second <= Mathf.FloorToInt(ruler.Seconds); second++)
                {
                    Vector3 tick = start + forward * metrics.RunDistance(second);
                    Handles.DrawLine(tick - side * 1.5f, tick + side * 1.5f, 2f);
                    if (second > 0)
                        LevelDesignerSceneGUI.Label(tick + Vector3.up * 0.3f, second + " s", ruler.Color);
                }
                Handles.DrawLine(end - side * 2f, end + side * 2f, 3f);
            }
            string title = string.IsNullOrEmpty(ruler.Label) ? metrics.CharacterName : ruler.Label;
            LevelDesignerSceneGUI.Label(end + Vector3.up * 0.6f, $"{title}: {LevelDesignerSceneGUI.Meters(total)} in {BlockoutShape.FormatMeters(ruler.Seconds)} s", ruler.Color);
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }
    }
}
