using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Places the item picked on the blockout toolbar.
    /// <list type="bullet">
    /// <item><b>Primitives</b>: click to drop one at its default size, or drag out its footprint on a surface (the
    /// drag direction is the shape's forward, so ramps and stairs rise the way you dragged), let go, move the
    /// mouse to set the height and click. Hold Shift when letting go of the drag to keep the default height.</item>
    /// <item><b>Rulers</b>: Distance drags from point to point (hold Shift to go straight out of the surface it
    /// started on instead of onto the surface under the mouse), Height drags up from a surface, Jump Arc and Run
    /// drag the direction from the take-off point (using the active character's numbers).</item>
    /// <item><b>Schematics</b>: click to place, or drag to face it in the drag direction.</item>
    /// </list>
    /// Esc or right-click cancels. Ctrl+drag measures with the temporary ruler, as everywhere in a blockout.
    /// </summary>
    [EditorTool("Blockout Place")]
    public class BlockoutPlaceTool : EditorTool
    {
        /// <summary>The item being placed.</summary>
        public static BlockoutItem Item { get; private set; }

        private enum Stage { Idle, Dragging, Height }

        private static Stage _stage;
        private static Vector3 _start;
        private static Vector3 _current;
        private static Vector3 _planeNormal = Vector3.up;
        // The surface normal a ruler started on (Shift measures straight out along it).
        private static Vector3 _rulerNormal = Vector3.up;
        private static float _height;
        private static GUIContent _icon;

        /// <summary>Picks an item and switches to this tool.</summary>
        public static void Activate(BlockoutItem item)
        {
            Item = item;
            _stage = Stage.Idle;
            BlockoutCatalog.SetLast(item);
            ToolManager.SetActiveTool<BlockoutPlaceTool>();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// True while this tool is the active Scene view tool. Other Scene view handles of the Level Designer (the
        /// shape resize handles, ruler end handles) stand down then, so placing always wins the mouse.
        /// </summary>
        public static bool IsActive => ToolManager.activeToolType == typeof(BlockoutPlaceTool);

        public override GUIContent toolbarIcon => _icon ??= new GUIContent("Place", "Blockout: place the item picked on the blockout toolbar");

        public override bool IsAvailable() => LevelDesignerContext.Mode == LevelDesignerMode.Blockout;

        public override void OnWillBeDeactivated()
        {
            _stage = Stage.Idle;
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || LevelDesignerContext.Mode != LevelDesignerMode.Blockout)
                return;

            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(control);

            // Ctrl+drag measures (BlockoutRulerDrawer handles it), whatever is being placed.
            if (BlockoutRulerDrawer.IsMeasuring)
                return;

            if (Item == null)
            {
                if (e.type == EventType.Repaint)
                    LevelDesignerSceneGUI.Label(HandleUtility.GUIPointToWorldRay(e.mousePosition).GetPoint(5f),
                        "Pick something from the blockout toolbar (right-click a button for the list)", Color.white);
                return;
            }

            if ((e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) || (e.type == EventType.MouseDown && e.button == 1 && _stage != Stage.Idle))
            {
                if (_stage != Stage.Idle)
                {
                    _stage = Stage.Idle;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    window.Repaint();
                }
                return;
            }

            switch (Item.Palette)
            {
                case BlockoutPalette.Primitives: PrimitiveGUI(e, control); break;
                case BlockoutPalette.Rulers: RulerGUI(e, control); break;
                default: SchematicGUI(e, control); break;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                window.Repaint();
        }

        // ---------------------------------------------------------------- primitives

        private static Vector3 PlanePoint(Vector2 guiPoint, Vector3 origin, Vector3 normal)
        {
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            var plane = new Plane(normal, origin);
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : origin;
        }

        // Snaps a point within the placement plane (the plane's own coordinate stays put).
        private static Vector3 SnapInPlane(Vector3 point, Vector3 normal)
        {
            var snapped = LevelDesignerSettings.SnapPoint(point);
            return snapped - normal * Vector3.Dot(snapped - point, normal);
        }

        private void PrimitiveGUI(Event e, int control)
        {
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && _stage == Stage.Idle:
                    if (LevelDesignerSceneGUI.Raycast(e.mousePosition, out var hit))
                    {
                        _planeNormal = LevelDesignerSceneGUI.NearestAxis(hit.Normal);
                        _start = _current = SnapInPlane(hit.Point, _planeNormal);
                        _stage = Stage.Dragging;
                        GUIUtility.hotControl = control;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag when _stage == Stage.Dragging && GUIUtility.hotControl == control:
                    _current = SnapInPlane(PlanePoint(e.mousePosition, _start, _planeNormal), _planeNormal);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && _stage == Stage.Dragging && GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    if (!HasFootprint(out _, out _, out _, out _))
                    {
                        // A click: the default size, centred where clicked.
                        CreateShape(_start, _planeNormal, ForwardFor(_planeNormal, Vector3.zero), new Vector2(Item.DefaultSize.x, Item.DefaultSize.z), Item.DefaultSize.y);
                        _stage = Stage.Idle;
                    }
                    else if (e.shift)
                    {
                        CommitFootprint(Item.DefaultSize.y);
                        _stage = Stage.Idle;
                    }
                    else
                    {
                        _height = Item.DefaultSize.y;
                        _stage = Stage.Height;
                    }
                    break;

                case EventType.MouseMove when _stage == Stage.Height:
                case EventType.MouseDrag when _stage == Stage.Height:
                {
                    FootprintFrame(out var center, out _, out _, out _);
                    var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    _height = LevelDesignerSettings.SnapLength(Mathf.Max(0.05f, LevelDesignerSceneGUI.ClosestOnLine(ray, center, _planeNormal)));
                    break;
                }

                case EventType.MouseDown when e.button == 0 && _stage == Stage.Height:
                    CommitFootprint(_height);
                    _stage = Stage.Idle;
                    e.Use();
                    break;

                case EventType.Repaint:
                    DrawPrimitivePreview(e);
                    break;
            }
        }

        private static bool HasFootprint(out Vector3 center, out Vector3 forward, out float width, out float length)
        {
            FootprintFrame(out center, out forward, out width, out length);
            float minimum = Mathf.Max(0.05f, LevelDesignerSettings.Snap * 0.5f);
            return (_current - _start).magnitude >= minimum && width >= 0.05f && length >= 0.05f;
        }

        // The dragged rectangle as centre, forward axis (the drag's main direction) and size along/across it.
        private static void FootprintFrame(out Vector3 center, out Vector3 forward, out float width, out float length)
        {
            Vector3 delta = _current - _start;
            forward = ForwardFor(_planeNormal, delta);
            Vector3 right = Vector3.Cross(_planeNormal, forward).normalized;
            length = Mathf.Abs(Vector3.Dot(delta, forward));
            width = Mathf.Abs(Vector3.Dot(delta, right));
            center = (_start + _current) * 0.5f;
        }

        // The world axis in the placement plane nearest to the drag (or a sensible default when there's no drag).
        private static Vector3 ForwardFor(Vector3 planeNormal, Vector3 drag)
        {
            drag -= planeNormal * Vector3.Dot(drag, planeNormal);
            if (drag.sqrMagnitude < 1e-6f)
                return Mathf.Abs(planeNormal.y) > 0.5f ? Vector3.forward : Vector3.up;
            return LevelDesignerSceneGUI.NearestAxis(drag);
        }

        private void CommitFootprint(float height)
        {
            FootprintFrame(out var center, out var forward, out float width, out float length);
            CreateShape(center, _planeNormal, forward, new Vector2(LevelDesignerSettings.SnapLength(width), LevelDesignerSettings.SnapLength(length)), height);
        }

        private void CreateShape(Vector3 basePoint, Vector3 up, Vector3 forward, Vector2 footprint, float height)
        {
            var go = new GameObject(Item.Name);
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.transform.SetPositionAndRotation(basePoint, Quaternion.LookRotation(forward, up));
            var shape = go.AddComponent<BlockoutShape>();
            shape.Kind = Item.ShapeKind;
            shape.Category = Item.Category;
            shape.Size = new Vector3(Mathf.Max(0.05f, footprint.x), Mathf.Max(0.05f, height), Mathf.Max(0.05f, footprint.y));
            shape.Material = LevelDesignerContext.BlockoutMaterial;
            shape.Rebuild();
            Undo.RegisterCreatedObjectUndo(go, "Place " + Item.Name);
            Selection.activeGameObject = go;
        }

        private void DrawPrimitivePreview(Event e)
        {
            var color = BlockoutShape.ColorOf(Item.Category);
            Vector3 center, forward;
            Vector3 size;

            if (_stage == Stage.Idle)
            {
                if (!LevelDesignerSceneGUI.Raycast(e.mousePosition, out var hover))
                    return;
                var normal = LevelDesignerSceneGUI.NearestAxis(hover.Normal);
                center = SnapInPlane(hover.Point, normal);
                forward = ForwardFor(normal, Vector3.zero);
                size = Item.DefaultSize;
                DrawBox(center, normal, forward, size, color, 0.35f);
                LevelDesignerSceneGUI.Label(center + normal * (size.y + 0.3f), Item.Name + "  " + SizeText(size), Color.white);
                return;
            }

            FootprintFrame(out center, out forward, out float width, out float length);
            width = LevelDesignerSettings.SnapLength(width);
            length = LevelDesignerSettings.SnapLength(length);
            float height = _stage == Stage.Height ? _height : 0.02f;
            size = new Vector3(width, height, length);
            DrawBox(center, _planeNormal, forward, size, color, 0.8f);

            Vector3 right = Vector3.Cross(_planeNormal, forward).normalized;
            LevelDesignerSceneGUI.Label(center + forward * (length * 0.5f) + _planeNormal * 0.05f, LevelDesignerSceneGUI.Meters(width), Color.white);
            LevelDesignerSceneGUI.Label(center + right * (width * 0.5f) + _planeNormal * 0.05f, LevelDesignerSceneGUI.Meters(length), Color.white);
            if (_stage == Stage.Height)
                LevelDesignerSceneGUI.Label(center + _planeNormal * height + right * (width * 0.5f), "↕ " + LevelDesignerSceneGUI.Meters(height) + "\nclick to place", Color.white);
        }

        private static void DrawBox(Vector3 basePoint, Vector3 up, Vector3 forward, Vector3 size, Color color, float alpha)
        {
            var rotation = Quaternion.LookRotation(forward, up);
            using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, alpha), Matrix4x4.TRS(basePoint, rotation, Vector3.one)))
            {
                Handles.DrawWireCube(new Vector3(0f, size.y * 0.5f, 0f), size);
                Vector3 hx = Vector3.right * size.x * 0.5f, hz = Vector3.forward * size.z * 0.5f;
                Handles.DrawSolidRectangleWithOutline(new[] { -hx - hz, -hx + hz, hx + hz, hx - hz },
                    new Color(color.r, color.g, color.b, alpha * 0.25f), new Color(color.r, color.g, color.b, alpha));
            }
        }

        private static string SizeText(Vector3 size) =>
            $"{BlockoutShape.FormatMeters(size.x)} x {BlockoutShape.FormatMeters(size.y)} x {BlockoutShape.FormatMeters(size.z)} m";

        // ---------------------------------------------------------------- rulers

        private void RulerGUI(Event e, int control)
        {
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && _stage == Stage.Idle:
                    if (!LevelDesignerSceneGUI.Raycast(e.mousePosition, out var hit))
                        break;
                    if ((Item.RulerKind == BlockoutRulerKind.JumpArc || Item.RulerKind == BlockoutRulerKind.RunDistance) && !ActiveCharacter.TryGetMetrics(out _))
                    {
                        SceneView.lastActiveSceneView?.ShowNotification(new GUIContent("Pick a character first:\nLevel Designer window → Characters"));
                        e.Use();
                        break;
                    }
                    _start = _current = LevelDesignerSettings.Snap > 0f ? LevelDesignerSettings.SnapPoint(hit.Point) : hit.Point;
                    _rulerNormal = hit.OnSurface && hit.Normal.sqrMagnitude > 1e-6f ? hit.Normal.normalized : Vector3.up;
                    _height = 0f;
                    _stage = Stage.Dragging;
                    GUIUtility.hotControl = control;
                    e.Use();
                    break;

                case EventType.MouseDrag when _stage == Stage.Dragging && GUIUtility.hotControl == control:
                {
                    var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    if (Item.RulerKind == BlockoutRulerKind.Height)
                        _height = LevelDesignerSettings.SnapLength(Mathf.Max(0.05f, LevelDesignerSceneGUI.ClosestOnLine(ray, _start, Vector3.up)));
                    else if (Item.RulerKind == BlockoutRulerKind.Distance)
                    {
                        if (e.shift)
                            _current = BlockoutRulerDrawer.PerpendicularEnd(e.mousePosition, _start, _rulerNormal);
                        else if (LevelDesignerSceneGUI.Raycast(e.mousePosition, out var dragHit))
                            _current = LevelDesignerSettings.Snap > 0f ? LevelDesignerSettings.SnapPoint(dragHit.Point) : dragHit.Point;
                    }
                    else
                        _current = PlanePoint(e.mousePosition, _start, Vector3.up);
                    e.Use();
                    break;
                }

                case EventType.MouseUp when e.button == 0 && _stage == Stage.Dragging && GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    _stage = Stage.Idle;
                    e.Use();
                    CreateRuler();
                    break;

                case EventType.Repaint:
                    DrawRulerPreview(e);
                    break;
            }
        }

        private void CreateRuler()
        {
            Vector3 direction = _current - _start;
            direction.y = 0f;
            var rotation = direction.sqrMagnitude > 0.01f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : Quaternion.identity;

            switch (Item.RulerKind)
            {
                case BlockoutRulerKind.Distance:
                {
                    if (Vector3.Distance(_start, _current) < 0.01f)
                        return;
                    var ruler = LevelDesignerSceneGUI.CreateRuler(BlockoutRulerKind.Distance, _start, Quaternion.identity);
                    ruler.End = _current - _start;
                    Selection.activeGameObject = ruler.gameObject;
                    break;
                }
                case BlockoutRulerKind.Height:
                {
                    var ruler = LevelDesignerSceneGUI.CreateRuler(BlockoutRulerKind.Height, _start, Quaternion.identity);
                    ruler.End = new Vector3(0f, Mathf.Max(0.25f, _height), 0f);
                    ruler.Color = new Color(0.5f, 1f, 0.6f);
                    Selection.activeGameObject = ruler.gameObject;
                    break;
                }
                default:
                {
                    if (!ActiveCharacter.TryGetMetrics(out var metrics))
                        return;
                    var ruler = LevelDesignerSceneGUI.CreateRuler(Item.RulerKind, _start, rotation,
                        $"{metrics.CharacterName} {(Item.RulerKind == BlockoutRulerKind.JumpArc ? "Jump" : "Run")}");
                    ruler.Character = ActiveCharacter.Prefab;
                    ruler.Metrics = metrics;
                    ruler.Seconds = Item.Seconds > 0f ? Item.Seconds : 1f;
                    ruler.Color = Item.RulerKind == BlockoutRulerKind.RunDistance ? new Color(0.45f, 0.75f, 1f) : ruler.Color;
                    Selection.activeGameObject = ruler.gameObject;
                    break;
                }
            }
        }

        private void DrawRulerPreview(Event e)
        {
            if (_stage == Stage.Idle)
            {
                if (LevelDesignerSceneGUI.Raycast(e.mousePosition, out var hover))
                {
                    using (new Handles.DrawingScope(new Color(1f, 0.85f, 0.2f)))
                        Handles.DrawWireDisc(hover.Point, hover.Normal, HandleUtility.GetHandleSize(hover.Point) * 0.15f);
                    LevelDesignerSceneGUI.Label(hover.Point + Vector3.up * HandleUtility.GetHandleSize(hover.Point) * 0.4f, Item.Name, Color.white);
                }
                return;
            }

            switch (Item.RulerKind)
            {
                case BlockoutRulerKind.Distance:
                    LevelDesignerSceneGUI.DrawMeasurement(_start, _current, new Color(1f, 0.85f, 0.2f),
                        e.shift ? "Perpendicular (Shift)" : "Shift: perpendicular");
                    break;
                case BlockoutRulerKind.Height:
                    LevelDesignerSceneGUI.DrawMeasurement(_start, _start + Vector3.up * _height, new Color(0.5f, 1f, 0.6f));
                    break;
                default:
                    if (ActiveCharacter.TryGetMetrics(out var metrics))
                    {
                        Vector3 direction = _current - _start;
                        direction.y = 0f;
                        if (direction.sqrMagnitude < 1e-4f)
                            direction = Vector3.forward;
                        var points = new System.Collections.Generic.List<Vector3>();
                        if (Item.RulerKind == BlockoutRulerKind.JumpArc)
                            metrics.SampleArc(_start, direction, points, 4f);
                        else
                        {
                            points.Add(_start);
                            points.Add(_start + direction.normalized * metrics.RunDistance(Item.Seconds));
                        }
                        using (new Handles.DrawingScope(new Color(1f, 1f, 1f, 0.8f)))
                            Handles.DrawAAPolyLine(3f, points.ToArray());
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- schematics

        private void SchematicGUI(Event e, int control)
        {
            if (Item.Prefab == null)
                return;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && _stage == Stage.Idle:
                    if (!LevelDesignerSceneGUI.Raycast(e.mousePosition, out var hit))
                        break;
                    _start = _current = LevelDesignerSettings.Snap > 0f ? SnapInPlane(hit.Point, Vector3.up) : hit.Point;
                    _stage = Stage.Dragging;
                    GUIUtility.hotControl = control;
                    e.Use();
                    break;

                case EventType.MouseDrag when _stage == Stage.Dragging && GUIUtility.hotControl == control:
                    _current = PlanePoint(e.mousePosition, _start, Vector3.up);
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0 && _stage == Stage.Dragging && GUIUtility.hotControl == control:
                {
                    GUIUtility.hotControl = 0;
                    _stage = Stage.Idle;
                    e.Use();
                    Vector3 direction = _current - _start;
                    direction.y = 0f;
                    var instance = PrefabUtility.InstantiatePrefab(Item.Prefab, SceneManager.GetActiveScene()) as GameObject;
                    if (instance == null)
                        break;
                    var rotation = direction.sqrMagnitude > 0.04f ? Quaternion.LookRotation(LevelDesignerSceneGUI.NearestAxis(direction), Vector3.up) : Item.Prefab.transform.rotation;
                    instance.transform.SetPositionAndRotation(_start, rotation);
                    Undo.RegisterCreatedObjectUndo(instance, "Place " + Item.Name);
                    Selection.activeGameObject = instance;
                    break;
                }

                case EventType.Repaint:
                {
                    Vector3 at;
                    if (_stage == Stage.Dragging)
                        at = _start;
                    else if (LevelDesignerSceneGUI.Raycast(e.mousePosition, out var hover))
                        at = LevelDesignerSettings.Snap > 0f ? SnapInPlane(hover.Point, Vector3.up) : hover.Point;
                    else
                        break;
                    float size = HandleUtility.GetHandleSize(at);
                    using (new Handles.DrawingScope(new Color(0.6f, 0.85f, 1f)))
                    {
                        Handles.DrawWireDisc(at, Vector3.up, size * 0.3f);
                        if (_stage == Stage.Dragging)
                            Handles.DrawLine(at, _current, 2f);
                    }
                    LevelDesignerSceneGUI.Label(at + Vector3.up * size * 0.5f, Item.Name, Color.white);
                    break;
                }
            }
        }
    }
}
