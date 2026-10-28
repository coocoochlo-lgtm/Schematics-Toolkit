using BlueGraph;
using BlueGraph.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Meshes
{
    /// <summary>
    /// An Edit Vertices node's viewport: the mesh coming into the node, with its edits, to pick vertices on and move
    /// them like in a modelling app. Click to pick (Shift adds, Ctrl takes away, drag a box for many; A all, Alt+A
    /// none), then G moves, R turns, S scales (X, Y, Z lock to an axis; click or Enter keeps it, Esc or right-click
    /// cancels), or drag a picked vertex. O turns proportional editing on: the vertices around follow, out to Radius
    /// (the mouse wheel changes it while moving). Right-drag (or Alt+drag) turns the view, middle-drag pans, the wheel
    /// zooms, F frames. Every edit can be undone, and changed or deleted in the list on the right.
    /// </summary>
    internal sealed class VertexEditWindow : EditorWindow
    {
        private const float SideWidth = 250f, PickPixels = 12f;

        private enum Tool { None, Grab, Rotate, Scale }

        [SerializeField] private MeshGraph _graph;
        [SerializeField] private string _nodeId;
        // The tool's settings, used for each new edit.
        [SerializeField] private bool _proportional;
        [SerializeField] private float _radius = 0.25f;
        [SerializeField] private ProportionalFalloff _falloff = ProportionalFalloff.Smooth;
        [SerializeField] private bool _connectedOnly;
        [SerializeField] private bool _xray;

        private MeshData _working;               // the mesh coming in, with every edit done (positions only)
        private MeshVertexEdits.Groups _groups;  // its vertices by place
        private readonly HashSet<int> _picked = new();
        private readonly List<string> _warnings = new();
        private int _version = int.MinValue;
        private int _selectedMove = -1;
        private Vector2 _listScroll;

        private PreviewRenderUtility _preview;
        private Mesh _shown;
        private Vector2 _angles = new(20f, -30f);
        private Vector3 _target;
        private float _distance = 2f;
        private Rect _view;

        // An edit being made: the move, the mesh with it done, and how much each place follows.
        private Tool _tool;
        private int _axis = -1;
        private Vector2 _toolStart;
        private Vector3 _pivot;
        private VertexMove _pending;
        private MeshData _pendingMesh;
        private float[] _weights;
        private bool _toolFromDrag;

        // A press waiting to become a click, a drag of a picked vertex, or a box.
        private bool _pressed, _boxing;
        private Vector2 _pressAt;
        private int _pressedGroup = -1;

        public static void Open(MeshGraph graph, MeshEditVerticesNode node)
        {
            if (graph == null || node == null)
                return;
            var window = GetWindow<VertexEditWindow>();
            window.titleContent = new GUIContent("Edit Vertices");
            window.minSize = new Vector2(600, 360);
            window._graph = graph;
            window._nodeId = node.ID;
            window._picked.Clear();
            window._selectedMove = -1;
            window._version = int.MinValue;
            window.Rebuild(frame: true);
            window.Show();
        }

        private MeshEditVerticesNode Node => _graph != null && !string.IsNullOrEmpty(_nodeId) ? _graph.GetNodeById(_nodeId) as MeshEditVerticesNode : null;

        private void OnEnable()
        {
            wantsMouseMove = true;
            Undo.undoRedoPerformed += OnUndo;
            _preview = new PreviewRenderUtility();
            _preview.camera.fieldOfView = 30f;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 1000f;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.2f, 0.2f, 0.22f);
            _preview.lights[0].intensity = 1.1f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _preview.lights[1].intensity = 0.6f;
            _shown = new Mesh { name = "Edit Vertices Preview", hideFlags = HideFlags.HideAndDontSave };
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            _preview?.Cleanup();
            _preview = null;
            if (_shown != null)
                DestroyImmediate(_shown);
        }

        private void OnUndo()
        {
            CancelTool();
            _version = int.MinValue;
            Repaint();
        }

        // The graph changed (this node's edits, or a node before it): the mesh again.
        private void Update()
        {
            if (_graph == null || _tool != Tool.None)
                return;
            if (_graph.Version != _version)
                Rebuild(frame: false);
        }

        private void Rebuild(bool frame)
        {
            var node = Node;
            _warnings.Clear();
            if (node == null)
            {
                _working = null;
                _groups = null;
                Repaint();
                return;
            }
            _version = _graph.Version;
            int before = _groups?.Count ?? -1;
            _working = MeshCompiler.CompileInput(_graph, node, "In", _warnings).Get().Clone();
            MeshVertexEdits.Apply(_working, node.Moves);
            _groups = new MeshVertexEdits.Groups(_working);
            if (_groups.Count != before)
                _picked.Clear();
            Show(_working);
            if (frame)
                Frame(false);
            Repaint();
        }

        private void Show(MeshData data)
        {
            if (_shown == null || data == null)
                return;
            data.WriteTo(_shown);
            _shown.RecalculateNormals();
        }

        // ---- Drawing ----

        private void OnGUI()
        {
            var node = Node;
            if (node == null || _working == null)
            {
                EditorGUILayout.HelpBox("Open this from an Edit Vertices node (its Edit button) in a Mesh Graph.", MessageType.Info);
                return;
            }
            DrawToolbar(node);
            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _view = new Rect(area.x, area.y, area.width - SideWidth, area.height);
            var side = new Rect(_view.xMax, area.y, SideWidth, area.height);
            HandleKeys(node);
            HandleMouse(node);
            if (Event.current.type == EventType.Repaint)
                DrawView();
            DrawSide(side, node);
        }

        private void DrawToolbar(MeshEditVerticesNode node)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label($"{_graph.name} › {node.Name}", EditorStyles.miniBoldLabel, GUILayout.Width(200));
                _proportional = GUILayout.Toggle(_proportional, new GUIContent("Proportional", "O: the vertices around the picked ones follow them, less the further away they are, out to Radius."), EditorStyles.toolbarButton);
                using (new EditorGUI.DisabledScope(!_proportional))
                {
                    EditorGUIUtility.labelWidth = 44;
                    _radius = Mathf.Max(0.001f, EditorGUILayout.FloatField(new GUIContent("Radius", "How far out (meters) the vertices around follow. The mouse wheel changes it while moving."), _radius, GUILayout.Width(100)));
                    _falloff = (ProportionalFalloff)EditorGUILayout.EnumPopup(new GUIContent("", "How the following fades out: Smooth, Sphere (round), Root, Linear, Sharp (pointy), Constant."), _falloff, EditorStyles.toolbarPopup, GUILayout.Width(80));
                    _connectedOnly = GUILayout.Toggle(_connectedOnly, new GUIContent("Connected", "Only vertices joined to the picked ones (along the surface) follow."), EditorStyles.toolbarButton);
                    EditorGUIUtility.labelWidth = 0;
                }
                GUILayout.Space(8);
                _xray = GUILayout.Toggle(_xray, new GUIContent("X-Ray", "Show and pick the vertices round the back, too."), EditorStyles.toolbarButton);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_picked.Count} picked", EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Frame", "F: fit the picked vertices (or the whole mesh) in view."), EditorStyles.toolbarButton))
                    Frame(_picked.Count > 0);
            }
        }

        private void DrawView()
        {
            var camera = PlaceCamera();
            _preview.BeginPreview(_view, GUIStyle.none);
            var material = MeshGraphEditor.DefaultMaterial();
            for (int s = 0; s < _shown.subMeshCount; s++)
                _preview.DrawMesh(_shown, Matrix4x4.identity, material, s);
            camera.Render();
            GUI.DrawTexture(_view, _preview.EndPreview(), ScaleMode.StretchToFill, false);

            // The vertices: dots, picked ones bigger and orange, followers yellow by how much they follow.
            GUI.BeginClip(_view);
            var mesh = _pendingMesh ?? _working;
            var eye = camera.transform.position;
            int count = _groups.Count;
            bool many = count > 20000;
            for (int g = 0; g < count; g++)
            {
                bool picked = _picked.Contains(g);
                float weight = _weights != null && g < _weights.Length ? _weights[g] : 0f;
                if (many && !picked && weight <= 0f)
                    continue;
                int v = _groups.Members(g)[0];
                var p = mesh.Positions[v];
                if (!picked && weight <= 0f && !Facing(mesh, g, p, eye))
                    continue;
                var s = ToScreen(camera, p, out bool inFront) - _view.position;
                if (!inFront)
                    continue;
                float size = picked ? 6f : 4f;
                var colour = picked ? new Color(1f, 0.55f, 0.1f)
                    : weight > 0f ? Color.Lerp(new Color(0.15f, 0.15f, 0.15f, 0.9f), new Color(1f, 0.9f, 0.2f), weight)
                    : new Color(0.08f, 0.08f, 0.08f, 0.85f);
                EditorGUI.DrawRect(new Rect(s.x - size / 2f, s.y - size / 2f, size, size), colour);
            }

            // The proportional radius, around the middle of the pick.
            if (_tool != Tool.None && _pending != null && _pending.Proportional)
            {
                var centre = ToScreen(camera, _pivot, out _) - _view.position;
                var edge = ToScreen(camera, _pivot + camera.transform.right * _pending.Radius, out _) - _view.position;
                Handles.color = new Color(1f, 1f, 1f, 0.6f);
                Handles.DrawWireDisc(centre, Vector3.forward, Vector2.Distance(centre, edge));
            }
            // A box being dragged out.
            if (_boxing)
            {
                var box = BoxRect();
                box.position -= _view.position;
                EditorGUI.DrawRect(box, new Color(0.4f, 0.6f, 1f, 0.15f));
                Handles.color = new Color(0.5f, 0.7f, 1f, 0.9f);
                Handles.DrawSolidRectangleWithOutline(box, Color.clear, Handles.color);
            }
            // The axis it's locked to.
            if (_tool != Tool.None && _axis >= 0)
            {
                var axis = Axis(_axis);
                var a = ToScreen(camera, _pivot - axis * 1000f, out _) - _view.position;
                var b = ToScreen(camera, _pivot + axis * 1000f, out _) - _view.position;
                Handles.color = _axis == 0 ? new Color(1f, 0.3f, 0.3f) : _axis == 1 ? new Color(0.4f, 1f, 0.4f) : new Color(0.4f, 0.6f, 1f);
                Handles.DrawLine(a, b);
            }
            GUI.EndClip();

            var status = StatusText();
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(_view.x + 8, _view.yMax - 22, _view.width - 16, 18), status, EditorStyles.whiteMiniLabel);
        }

        private string StatusText()
        {
            if (_tool == Tool.None)
                return _picked.Count > 0 ? "G move · R turn · S scale · O proportional · drag a picked vertex to move it" : "Click vertices to pick them (Shift adds, Ctrl takes away, drag a box for many) · A all";
            string axis = _axis < 0 ? "" : "  locked to " + "XYZ"[_axis];
            string what = _tool switch
            {
                Tool.Grab => $"Move {Format(_pending.Move)}",
                Tool.Rotate => $"Turn {Format(_pending.Rotate)}",
                _ => $"Scale {Format(_pending.Scale)}",
            };
            string proportional = _pending.Proportional ? $"  ·  radius {_pending.Radius:0.###} (wheel)" : "";
            return $"{what}{axis}{proportional}  ·  X/Y/Z lock  ·  click/Enter keep  ·  Esc/right-click cancel";
        }

        private static string Format(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        private void DrawSide(Rect rect, MeshEditVerticesNode node)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, rect.height - 8));
            GUILayout.Label("Edits", EditorStyles.boldLabel);
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            var moves = node.Moves;
            if (moves.Count == 0)
                GUILayout.Label("None yet: pick vertices and press G.", EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                if (move == null)
                    continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    string label = $"{i + 1}. {Describe(move)}";
                    if (GUILayout.Toggle(_selectedMove == i, label, EditorStyles.miniButtonLeft) != (_selectedMove == i))
                        _selectedMove = _selectedMove == i ? -1 : i;
                    if (GUILayout.Button(new GUIContent("×", "Delete this edit."), EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    {
                        int index = i;
                        Change("Delete Vertex Edit", () => node.Moves.RemoveAt(index));
                        _selectedMove = -1;
                        GUIUtility.ExitGUI();
                    }
                }
                if (_selectedMove == i)
                    DrawMoveSettings(node, move);
            }
            GUILayout.EndScrollView();

            foreach (var warning in _warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            GUILayout.Label(
                "Right-drag or Alt+drag: turn the view. Middle-drag: pan. Wheel: zoom. " +
                "The node's Strength scales every edit; here they're shown at full strength.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndArea();
        }

        private static string Describe(VertexMove move)
        {
            var parts = new List<string>();
            if (move.Move != Vector3.zero) parts.Add("move");
            if (move.Rotate != Vector3.zero) parts.Add("turn");
            if (move.Scale != Vector3.one) parts.Add("scale");
            string what = parts.Count > 0 ? string.Join("+", parts) : "nothing";
            return $"{what}, {move.Picked.Count} vert{(move.Picked.Count == 1 ? "" : "s")}{(move.Proportional ? " ~" : "")}";
        }

        private void DrawMoveSettings(MeshEditVerticesNode node, VertexMove move)
        {
            EditorGUI.indentLevel++;
            EditorGUIUtility.labelWidth = 80;
            EditorGUI.BeginChangeCheck();
            var moved = EditorGUILayout.Vector3Field("Move", move.Move);
            var turned = EditorGUILayout.Vector3Field("Turn", move.Rotate);
            var scaled = EditorGUILayout.Vector3Field("Scale", move.Scale);
            bool proportional = EditorGUILayout.Toggle("Proportional", move.Proportional);
            float radius = move.Radius;
            var falloff = move.Falloff;
            bool connected = move.ConnectedOnly;
            using (new EditorGUI.DisabledScope(!proportional))
            {
                radius = Mathf.Max(0.001f, EditorGUILayout.FloatField("Radius", move.Radius));
                falloff = (ProportionalFalloff)EditorGUILayout.EnumPopup("Falloff", move.Falloff);
                connected = EditorGUILayout.Toggle("Connected", move.ConnectedOnly);
            }
            if (EditorGUI.EndChangeCheck())
                Change("Change Vertex Edit", () =>
                {
                    move.Move = moved;
                    move.Rotate = turned;
                    move.Scale = scaled;
                    move.Proportional = proportional;
                    move.Radius = radius;
                    move.Falloff = falloff;
                    move.ConnectedOnly = connected;
                });
            EditorGUIUtility.labelWidth = 0;
            EditorGUI.indentLevel--;
            GUILayout.Space(4);
        }

        // A change to the node's edits, undoable; the graph remakes its model, and this view its mesh.
        private void Change(string name, System.Action change)
        {
            Undo.RecordObject(_graph, name);
            change();
            EditorUtility.SetDirty(_graph);
            _graph.Touch();
            Rebuild(frame: false);
        }

        // ---- Camera ----

        private Camera PlaceCamera()
        {
            var camera = _preview.camera;
            var rotation = Quaternion.Euler(_angles.x, _angles.y, 0f);
            camera.transform.SetPositionAndRotation(_target + rotation * new Vector3(0f, 0f, -_distance), rotation);
            camera.farClipPlane = Mathf.Max(100f, _distance * 20f);
            camera.nearClipPlane = Mathf.Max(0.001f, _distance * 0.002f);
            camera.aspect = Mathf.Max(0.01f, _view.width / Mathf.Max(1f, _view.height));
            return camera;
        }

        private Vector2 ToScreen(Camera camera, Vector3 world, out bool inFront)
        {
            var v = camera.WorldToViewportPoint(world);
            inFront = v.z > 0f;
            return new Vector2(_view.x + v.x * _view.width, _view.y + (1f - v.y) * _view.height);
        }

        private bool Facing(MeshData mesh, int group, Vector3 position, Vector3 eye)
        {
            if (_xray)
                return true;
            // Seen if any of its vertices' normals face the eye (a corner faces several ways).
            foreach (int v in _groups.Members(group))
                if (v < mesh.Normals.Count && Vector3.Dot(mesh.Normals[v], eye - position) > 0f)
                    return true;
            return false;
        }

        private void Frame(bool pickedOnly)
        {
            if (_working == null || _working.VertexCount == 0)
                return;
            var bounds = new Bounds();
            bool first = true;
            for (int g = 0; g < _groups.Count; g++)
                if (!pickedOnly || _picked.Contains(g))
                {
                    var p = _groups.Position(g);
                    if (first)
                        bounds = new Bounds(p, Vector3.zero);
                    else
                        bounds.Encapsulate(p);
                    first = false;
                }
            _target = bounds.center;
            _distance = Mathf.Max(0.05f, bounds.extents.magnitude * 3.2f);
            Repaint();
        }

        private static Vector3 Axis(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        // ---- Input ----

        private void HandleKeys(MeshEditVerticesNode node)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
                return;
            switch (e.keyCode)
            {
                case KeyCode.G when _picked.Count > 0 && _tool == Tool.None:
                    StartTool(Tool.Grab, e.mousePosition, false);
                    break;
                case KeyCode.R when _picked.Count > 0 && _tool == Tool.None:
                    StartTool(Tool.Rotate, e.mousePosition, false);
                    break;
                case KeyCode.S when _picked.Count > 0 && _tool == Tool.None:
                    StartTool(Tool.Scale, e.mousePosition, false);
                    break;
                case KeyCode.X when _tool != Tool.None:
                case KeyCode.Y when _tool != Tool.None:
                case KeyCode.Z when _tool != Tool.None:
                {
                    int axis = e.keyCode - KeyCode.X;
                    _axis = _axis == axis ? -1 : axis;
                    RefreshPending();
                    break;
                }
                case KeyCode.O:
                    _proportional = !_proportional;
                    if (_pending != null)
                    {
                        _pending.Proportional = _proportional;
                        RefreshWeights();
                        RefreshPending();
                    }
                    break;
                case KeyCode.Return or KeyCode.KeypadEnter when _tool != Tool.None:
                    ConfirmTool(node);
                    break;
                case KeyCode.Escape when _tool != Tool.None:
                    CancelTool();
                    break;
                case KeyCode.A when _tool == Tool.None:
                    _picked.Clear();
                    if (!e.alt)
                        for (int g = 0; g < _groups.Count; g++)
                            _picked.Add(g);
                    break;
                case KeyCode.F:
                    Frame(_picked.Count > 0);
                    break;
                default:
                    return;
            }
            e.Use();
            Repaint();
        }

        private void HandleMouse(MeshEditVerticesNode node)
        {
            var e = Event.current;
            bool inView = _view.Contains(e.mousePosition);

            // Making an edit: the mouse drives it.
            if (_tool != Tool.None)
            {
                switch (e.type)
                {
                    case EventType.MouseMove:
                    case EventType.MouseDrag:
                        RefreshPending();
                        e.Use();
                        break;
                    case EventType.MouseDown when e.button == 0 && !_toolFromDrag:
                        ConfirmTool(node);
                        e.Use();
                        break;
                    case EventType.MouseUp when e.button == 0 && _toolFromDrag:
                        ConfirmTool(node);
                        e.Use();
                        break;
                    case EventType.MouseDown when e.button == 1:
                        CancelTool();
                        e.Use();
                        break;
                    case EventType.ScrollWheel when _pending.Proportional:
                        _radius = _pending.Radius = Mathf.Max(0.001f, _pending.Radius * (1f + e.delta.y * 0.05f));
                        RefreshWeights();
                        RefreshPending();
                        e.Use();
                        break;
                }
                Repaint();
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown when inView:
                    GUIUtility.keyboardControl = 0;
                    if (e.button == 0 && !e.alt)
                    {
                        _pressed = true;
                        _pressAt = e.mousePosition;
                        _pressedGroup = Nearest(e.mousePosition);
                        e.Use();
                    }
                    else if (e.button is 1 or 2 || e.alt)
                        e.Use();
                    break;

                case EventType.MouseDrag:
                    if (_pressed && !_boxing && Vector2.Distance(e.mousePosition, _pressAt) > 4f)
                    {
                        // Dragging a picked vertex moves the pick; anywhere else draws a box.
                        if (_pressedGroup >= 0 && _picked.Contains(_pressedGroup) && !e.shift && !e.control)
                        {
                            _pressed = false;
                            StartTool(Tool.Grab, _pressAt, true);
                            RefreshPending();
                        }
                        else
                            _boxing = true;
                    }
                    else if (!_pressed && (e.button == 1 || (e.button == 0 && e.alt)))
                    {
                        _angles.y += e.delta.x * 0.5f;
                        _angles.x = Mathf.Clamp(_angles.x + e.delta.y * 0.5f, -89f, 89f);
                    }
                    else if (!_pressed && e.button == 2)
                    {
                        var camera = PlaceCamera();
                        float perPixel = WorldPerPixel(camera, _target);
                        _target += (-camera.transform.right * e.delta.x + camera.transform.up * e.delta.y) * perPixel;
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _pressed:
                    if (_boxing)
                        PickBox(e.shift, e.control);
                    else
                        PickOne(_pressedGroup, e.shift, e.control);
                    _pressed = _boxing = false;
                    e.Use();
                    Repaint();
                    break;

                case EventType.ScrollWheel when inView:
                    _distance = Mathf.Max(0.01f, _distance * (1f + e.delta.y * 0.05f));
                    e.Use();
                    Repaint();
                    break;
            }
        }

        private void PickOne(int group, bool add, bool remove)
        {
            if (!add && !remove)
                _picked.Clear();
            if (group < 0)
                return;
            if (remove || (add && _picked.Contains(group)))
                _picked.Remove(group);
            else
                _picked.Add(group);
        }

        private void PickBox(bool add, bool remove)
        {
            if (!add && !remove)
                _picked.Clear();
            var camera = PlaceCamera();
            var box = BoxRect();
            var eye = camera.transform.position;
            for (int g = 0; g < _groups.Count; g++)
            {
                var p = _groups.Position(g);
                if (!Facing(_working, g, p, eye))
                    continue;
                var s = ToScreen(camera, p, out bool inFront);
                if (!inFront || !box.Contains(s))
                    continue;
                if (remove)
                    _picked.Remove(g);
                else
                    _picked.Add(g);
            }
        }

        private Rect BoxRect()
        {
            var now = Event.current.mousePosition;
            return Rect.MinMaxRect(Mathf.Min(now.x, _pressAt.x), Mathf.Min(now.y, _pressAt.y), Mathf.Max(now.x, _pressAt.x), Mathf.Max(now.y, _pressAt.y));
        }

        // The seen vertex nearest the mouse, if one's close enough.
        private int Nearest(Vector2 mouse)
        {
            var camera = PlaceCamera();
            var eye = camera.transform.position;
            int best = -1;
            float bestDistance = PickPixels * PickPixels;
            float bestDepth = float.MaxValue;
            for (int g = 0; g < _groups.Count; g++)
            {
                var p = _groups.Position(g);
                if (!Facing(_working, g, p, eye))
                    continue;
                var s = ToScreen(camera, p, out bool inFront);
                if (!inFront)
                    continue;
                float d = (s - mouse).sqrMagnitude;
                float depth = (p - eye).sqrMagnitude;
                // The nearest on screen; of ones on top of each other, the one in front.
                if (d < bestDistance - 4f || (d <= bestDistance + 4f && depth < bestDepth))
                {
                    best = g;
                    bestDistance = Mathf.Min(bestDistance, d);
                    bestDepth = depth;
                }
            }
            return best;
        }

        private float WorldPerPixel(Camera camera, Vector3 at)
        {
            float depth = Vector3.Dot(at - camera.transform.position, camera.transform.forward);
            return 2f * Mathf.Max(0.001f, depth) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, _view.height);
        }

        // ---- Making an edit ----

        private void StartTool(Tool tool, Vector2 mouse, bool fromDrag)
        {
            _tool = tool;
            _toolFromDrag = fromDrag;
            _toolStart = mouse;
            _axis = -1;
            _pending = new VertexMove
            {
                Proportional = _proportional,
                Radius = _radius,
                Falloff = _falloff,
                ConnectedOnly = _connectedOnly,
            };
            _pivot = Vector3.zero;
            foreach (int g in _picked)
            {
                var p = _groups.Position(g);
                _pending.Picked.Add(p);
                _pivot += p;
            }
            _pivot /= Mathf.Max(1, _picked.Count);
            RefreshWeights();
        }

        private void RefreshWeights()
        {
            if (_pending == null)
                return;
            _pending.Falloff = _falloff;
            _pending.ConnectedOnly = _connectedOnly;
            _weights = MeshVertexEdits.Weights(_working, _pending, out _);
        }

        // The edit from where the mouse is now, done on a copy of the mesh to show.
        private void RefreshPending()
        {
            if (_pending == null)
                return;
            var camera = PlaceCamera();
            var mouse = Event.current.mousePosition;
            var centre = ToScreen(camera, _pivot, out _);
            _pending.Move = Vector3.zero;
            _pending.Rotate = Vector3.zero;
            _pending.Scale = Vector3.one;
            switch (_tool)
            {
                case Tool.Grab:
                {
                    var delta = mouse - _toolStart;
                    if (_axis < 0)
                    {
                        float perPixel = WorldPerPixel(camera, _pivot);
                        _pending.Move = (camera.transform.right * delta.x - camera.transform.up * delta.y) * perPixel;
                    }
                    else
                    {
                        // Along the axis as it's drawn on screen: pixels per meter that way.
                        var axis = Axis(_axis);
                        var along = ToScreen(camera, _pivot + axis, out _) - centre;
                        if (along.sqrMagnitude > 1f)
                            _pending.Move = axis * (Vector2.Dot(delta, along) / along.sqrMagnitude);
                    }
                    break;
                }
                case Tool.Rotate:
                {
                    float from = Mathf.Atan2(_toolStart.y - centre.y, _toolStart.x - centre.x);
                    float to = Mathf.Atan2(mouse.y - centre.y, mouse.x - centre.x);
                    float angle = Mathf.DeltaAngle(from * Mathf.Rad2Deg, to * Mathf.Rad2Deg);
                    // Screen-clockwise turns the way the view's own axis turns; a locked axis turns the same way on screen.
                    var axis = _axis < 0 ? camera.transform.forward : Axis(_axis);
                    if (_axis >= 0 && Vector3.Dot(axis, camera.transform.forward) < 0f)
                        angle = -angle;
                    _pending.Rotate = Quaternion.AngleAxis(angle, axis).eulerAngles;
                    break;
                }
                case Tool.Scale:
                {
                    float factor = Vector2.Distance(mouse, centre) / Mathf.Max(1f, Vector2.Distance(_toolStart, centre));
                    _pending.Scale = _axis < 0 ? Vector3.one * factor : Vector3.one + (Axis(_axis) * (factor - 1f));
                    break;
                }
            }
            _pendingMesh = _working.Clone();
            MeshVertexEdits.Apply(_pendingMesh, _pending);
            Show(_pendingMesh);
        }

        private void ConfirmTool(MeshEditVerticesNode node)
        {
            var move = _pending;
            bool nothing = move == null || (move.Move == Vector3.zero && move.Rotate == Vector3.zero && move.Scale == Vector3.one);
            ClearTool();
            if (nothing)
            {
                Show(_working);
                return;
            }
            Change("Edit Vertices", () => node.Moves.Add(move));
        }

        private void CancelTool()
        {
            ClearTool();
            if (_working != null)
                Show(_working);
        }

        private void ClearTool()
        {
            _tool = Tool.None;
            _pending = null;
            _pendingMesh = null;
            _weights = null;
            _axis = -1;
            _toolFromDrag = false;
            Repaint();
        }
    }

    /// <summary>The Edit Vertices node: an Edit button opening its viewport, and how many edits it has.</summary>
    [CustomNodeView(typeof(MeshEditVerticesNode))]
    public class MeshEditVerticesNodeView : NodeView
    {
        private Label _count;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4, paddingRight = 4, paddingTop = 2, paddingBottom = 2 } };
            var edit = new Button(OpenEditor) { text = "Edit...", tooltip = "Open its viewport: pick vertices and move them (G move, R turn, S scale, O proportional)." };
            row.Add(edit);
            _count = new Label { style = { marginLeft = 6, fontSize = 10, color = new Color(0.75f, 0.75f, 0.75f) } };
            row.Add(_count);
            extensionContainer.Add(row);
            RefreshCount();
            titleContainer.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2)
                {
                    OpenEditor();
                    e.StopPropagation();
                }
            });
            RefreshExpandedState();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            RefreshCount();
        }

        private void RefreshCount()
        {
            if (_count != null && Target is MeshEditVerticesNode node)
                _count.text = node.Moves.Count == 0 ? "no edits yet" : $"{node.Moves.Count} edit{(node.Moves.Count == 1 ? "" : "s")}";
        }

        private void OpenEditor()
        {
            if (Target is MeshEditVerticesNode node && node.Graph is MeshGraph graph)
                VertexEditWindow.Open(graph, node);
        }
    }
}
