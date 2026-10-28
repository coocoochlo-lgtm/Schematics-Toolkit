using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Remedy.Materials
{
    /// <summary>
    /// A Paint node's viewport: the Smart Material on its model, to paint on like Substance Painter. Left-drag paints
    /// (Ctrl erases) the channels switched on (colour, roughness, metallic, height, mask), in 3D, so strokes run straight
    /// across UV seams. [ and ] (or Shift+wheel) size the brush; right-drag or Alt+drag turns the view, middle-drag
    /// pans, the wheel zooms, F frames. Z undoes a stroke, Shift+Z redoes it. What's painted is saved as textures next
    /// to the graph a moment after each stroke (and when the window closes).
    /// </summary>
    internal sealed class PaintWindow : EditorWindow
    {
        private const float SideWidth = 240f;
        private const int UndoSteps = 16;

        private enum View { Material, Mask }

        [SerializeField] private SmartMaterialGraph _graph;
        [SerializeField] private string _nodeId;

        // The brush.
        [SerializeField] private bool _paintColor = true;
        [SerializeField] private Color _color = new(0.8f, 0.25f, 0.2f, 1f);
        [SerializeField] private bool _paintRoughness;
        [SerializeField] private float _roughness = 0.4f;
        [SerializeField] private bool _paintMetallic;
        [SerializeField] private float _metallic;
        [SerializeField] private bool _paintHeight;
        [SerializeField] private float _heightAmount = 0.5f;
        [SerializeField] private bool _paintMask;
        [SerializeField] private float _size = 0.05f;
        [SerializeField] private float _hardness = 0.5f;
        [SerializeField] private float _flow = 0.5f;
        [SerializeField] private bool _mirrorX;
        [SerializeField] private bool _erase;
        [SerializeField] private View _view;

        private PaintLayer _layer;
        private MaterialSurface _surface;
        private Mesh _mesh;
        private Action _releaseMesh;
        private readonly MaterialPreview _made = new();
        private Material _maskMaterial;
        private Texture2D _maskTexture;
        private bool _maskDirty = true;
        private PreviewRenderUtility _preview;
        private Vector2 _angles = new(25f, -30f);
        private Vector3 _target;
        private float _distance = 2f;
        private Rect _viewRect;

        private bool _making, _paintChanged = true;
        private int _shownVersion = int.MinValue;
        private double _saveAt;
        private readonly List<PaintLayer> _undo = new(), _redo = new();
        private readonly List<(int texel, float distance)> _found = new();

        private bool _stroking, _orbiting;
        private Vector3 _lastStamp;
        private bool _hover;
        private Vector3 _hoverPoint, _hoverNormal;

        public static void Open(SmartMaterialGraph graph, PaintNode node)
        {
            if (graph == null || node == null)
                return;
            var window = GetWindow<PaintWindow>();
            window.titleContent = new GUIContent("Paint");
            window.minSize = new Vector2(640, 400);
            if (window._graph != graph || window._nodeId != node.ID)
                window.SaveNow();
            window._graph = graph;
            window._nodeId = node.ID;
            window.Load();
            window.Show();
        }

        private PaintNode Node => _graph != null && !string.IsNullOrEmpty(_nodeId) ? _graph.GetNodeById(_nodeId) as PaintNode : null;

        private void OnEnable()
        {
            wantsMouseMove = true;
            EditorApplication.update += Tick;
            if (_graph != null)
                Load();
        }

        private void OnDisable()
        {
            SaveNow();
            EditorApplication.update -= Tick;
            _releaseMesh?.Invoke();
            _releaseMesh = null;
            _made.Dispose();
            if (_maskMaterial != null)
                DestroyImmediate(_maskMaterial);
            if (_maskTexture != null)
                DestroyImmediate(_maskTexture);
            _preview?.Cleanup();
            _preview = null;
        }

        // The node's painting (kept on it, live, while it's painted), the surface to paint on, and the mesh to show.
        private async void Load()
        {
            var node = Node;
            if (node == null)
                return;
            int size = node.Resolution;
            var layer = node.Live ?? node.Read();
            if (layer == null)
                layer = new PaintLayer(size);
            else if (layer.Size != size)
                layer = Resize(layer, size);
            node.Live = layer;
            _layer = layer;
            _undo.Clear();
            _redo.Clear();
            try
            {
                EditorUtility.DisplayProgressBar("Paint", "Laying the model out for painting...", 0.5f);
                _surface = MaterialCompiler.SurfaceFor(_graph, size);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            _size = Mathf.Clamp(_size, _surface.Size * 0.002f, _surface.Size);

            _releaseMesh?.Invoke();
            _releaseMesh = null;
            if (_surface.Tiling)
                _mesh = FlatMesh();
            else
            {
                var (mesh, release) = await SmartMaterialEditor.PreviewMesh(_graph);
                _mesh = mesh;
                _releaseMesh = release;
            }
            _maskDirty = true;
            _paintChanged = true;
            Frame();
            Repaint();
        }

        // The flat square a tiling material's painted on (the same one its surface is laid out on).
        private static Mesh FlatMesh()
        {
            var mesh = new Mesh { name = "Paint Square", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateTangents();
            return mesh;
        }

        private static PaintLayer Resize(PaintLayer from, int size)
        {
            var to = new PaintLayer(size);
            for (int i = 0; i < size * size; i++)
            {
                to.Colour[i] = MapSampling.Sample(from.Colour, from.Size, size, size, i);
                to.Roughness[i] = MapSampling.Sample(from.Roughness, from.Size, size, size, i);
                to.RoughnessCover[i] = MapSampling.Sample(from.RoughnessCover, from.Size, size, size, i);
                to.Metallic[i] = MapSampling.Sample(from.Metallic, from.Size, size, size, i);
                to.MetallicCover[i] = MapSampling.Sample(from.MetallicCover, from.Size, size, size, i);
                to.HeightOffset[i] = MapSampling.Sample(from.HeightOffset, from.Size, size, size, i);
                to.Mask[i] = MapSampling.Sample(from.Mask, from.Size, size, size, i);
            }
            return to;
        }

        // ---- Keeping the view up to date ----

        private void Tick()
        {
            var node = Node;
            if (node == null || _layer == null)
                return;
            // Someone else replaced its painting (an undo of the node, a reload): start from that.
            if (node.Live != _layer)
            {
                Load();
                return;
            }
            if (!_making && (_paintChanged || _graph.Version != _shownVersion))
                Remake();
            if (_saveAt > 0 && EditorApplication.timeSinceStartup > _saveAt && !_stroking)
                SaveNow();
        }

        private async void Remake()
        {
            _making = true;
            _paintChanged = false;
            int version = _graph.Version;
            try
            {
                var output = _graph.Output;
                var made = await MaterialPreview.Make(_graph, output != null ? output.PreviewResolution : 512);
                if (this == null)
                    return;
                if (made.HasValue)
                    _made.Apply(made.Value);
                _shownVersion = version;
                Repaint();
            }
            finally
            {
                _making = false;
            }
        }

        // ---- Drawing ----

        private void OnGUI()
        {
            var node = Node;
            if (node == null || _layer == null || _surface == null)
            {
                EditorGUILayout.HelpBox("Open this from a Paint node (its Paint button) in a Smart Material.", MessageType.Info);
                return;
            }
            var area = new Rect(0, 0, position.width, position.height);
            _viewRect = new Rect(area.x, area.y, area.width - SideWidth, area.height);
            HandleKeys();
            HandleMouse();
            if (Event.current.type == EventType.Repaint)
                DrawView();
            DrawSide(new Rect(_viewRect.xMax, area.y, SideWidth, area.height), node);
        }

        private void DrawView()
        {
            EditorGUI.DrawRect(_viewRect, new Color(0.18f, 0.18f, 0.2f));
            if (_mesh == null)
            {
                GUI.Label(_viewRect, "Loading...", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            var camera = PlaceCamera();
            _preview.BeginPreview(_viewRect, GUIStyle.none);
            var material = _view == View.Mask ? MaskMaterial() : _made.Material;
            for (int s = 0; s < _mesh.subMeshCount; s++)
                _preview.DrawMesh(_mesh, Matrix4x4.identity, material, s);
            camera.Render();
            GUI.DrawTexture(_viewRect, _preview.EndPreview(), ScaleMode.StretchToFill, false);

            // The brush: a ring lying on the surface under the mouse (and its mirror image).
            if (_hover)
            {
                DrawRing(camera, _hoverPoint, _hoverNormal, _size, _erase || Event.current.control ? new Color(1f, 0.5f, 0.4f) : Color.white);
                DrawRing(camera, _hoverPoint, _hoverNormal, _size * Mathf.Max(0.05f, _hardness), new Color(1f, 1f, 1f, 0.35f));
                if (_mirrorX)
                    DrawRing(camera, Mirror(_hoverPoint), Mirror(_hoverNormal), _size, new Color(1f, 1f, 1f, 0.5f));
            }
            string status = _making ? "Updating..." : "";
            GUI.Label(new Rect(_viewRect.x + 8, _viewRect.yMax - 20, _viewRect.width - 16, 18),
                $"Left-drag paint · Ctrl erase · [ ] size · Z undo · right-drag turn · F frame   {status}", EditorStyles.whiteMiniLabel);
        }

        private void DrawRing(Camera camera, Vector3 point, Vector3 normal, float radius, Color color)
        {
            var side = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var up = Vector3.Cross(normal, side);
            var points = new Vector3[33];
            for (int i = 0; i <= 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                var world = point + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius + normal * radius * 0.02f;
                var v = camera.WorldToViewportPoint(world);
                points[i] = new Vector3(_viewRect.x + v.x * _viewRect.width, _viewRect.y + (1f - v.y) * _viewRect.height, 0f);
            }
            Handles.color = color;
            Handles.DrawAAPolyLine(2f, points);
        }

        // The painted mask, red over grey, on a plain material (for seeing a mask that isn't wired into anything yet).
        private Material MaskMaterial()
        {
            if (_maskMaterial == null)
                _maskMaterial = new Material(SmartMaterialEditor.DefaultMaterial()) { hideFlags = HideFlags.HideAndDontSave };
            int size = _layer.Size;
            if (_maskTexture == null || _maskTexture.width != size)
            {
                if (_maskTexture != null)
                    DestroyImmediate(_maskTexture);
                _maskTexture = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { hideFlags = HideFlags.HideAndDontSave };
                _maskDirty = true;
            }
            if (_maskDirty)
            {
                var pixels = new Color32[size * size];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(1f, 0.15f, 0.1f), _layer.Mask[i]);
                _maskTexture.SetPixels32(pixels);
                _maskTexture.Apply(true);
                SmartMaterialEditor.Assign(_maskMaterial, new Dictionary<string, Texture> { [MaterialBaker.BaseColor] = _maskTexture });
                _maskDirty = false;
            }
            return _maskMaterial;
        }

        private void DrawSide(Rect rect, PaintNode node)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, rect.height - 8));
            GUILayout.Label($"{_graph.name} › {node.Name}", EditorStyles.boldLabel);
            _view = (View)GUILayout.Toolbar((int)_view, new[] { new GUIContent("Material", "The whole Smart Material, with what's painted."), new GUIContent("Mask", "Just the painted mask (red), for painting a mask that isn't wired in yet.") });
            GUILayout.Space(6);

            EditorGUIUtility.labelWidth = 80;
            GUILayout.Label("Paints", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _paintColor = EditorGUILayout.ToggleLeft(new GUIContent("Color", "Paint colour."), _paintColor, GUILayout.Width(80));
                using (new EditorGUI.DisabledScope(!_paintColor))
                    _color = EditorGUILayout.ColorField(GUIContent.none, _color, true, false, false);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _paintRoughness = EditorGUILayout.ToggleLeft(new GUIContent("Roughness", "Paint roughness: 0 shiny, 1 chalky."), _paintRoughness, GUILayout.Width(80));
                using (new EditorGUI.DisabledScope(!_paintRoughness))
                    _roughness = EditorGUILayout.Slider(_roughness, 0f, 1f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _paintMetallic = EditorGUILayout.ToggleLeft(new GUIContent("Metallic", "Paint metallic: 0 paint, plastic, stone; 1 bare metal."), _paintMetallic, GUILayout.Width(80));
                using (new EditorGUI.DisabledScope(!_paintMetallic))
                    _metallic = EditorGUILayout.Slider(_metallic, 0f, 1f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                _paintHeight = EditorGUILayout.ToggleLeft(new GUIContent("Height", "Raise (above 0) or carve in (below 0) the surface."), _paintHeight, GUILayout.Width(80));
                using (new EditorGUI.DisabledScope(!_paintHeight))
                    _heightAmount = EditorGUILayout.Slider(_heightAmount, -1f, 1f);
            }
            _paintMask = EditorGUILayout.ToggleLeft(new GUIContent("Mask", "Paint the node's Mask output: wire it into a Layer's Mask to place a material by hand."), _paintMask);

            GUILayout.Space(6);
            GUILayout.Label("Brush", EditorStyles.miniBoldLabel);
            _size = EditorGUILayout.Slider(new GUIContent("Size", "The brush's radius (meters). [ and ] change it, or Shift+wheel."), _size, _surface.Size * 0.002f, _surface.Size * 0.5f);
            _hardness = EditorGUILayout.Slider(new GUIContent("Hardness", "0 soft all the way out, 1 a hard edge."), _hardness, 0f, 1f);
            _flow = EditorGUILayout.Slider(new GUIContent("Flow", "How much each dab puts down: low builds up gently."), _flow, 0.02f, 1f);
            _mirrorX = EditorGUILayout.Toggle(new GUIContent("Mirror X", "Paint the other side (across X) at the same time."), _mirrorX);
            _erase = EditorGUILayout.Toggle(new GUIContent("Erase", "Take paint away (or hold Ctrl)."), _erase);
            EditorGUIUtility.labelWidth = 0;

            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                    if (GUILayout.Button(new GUIContent("Undo", "Z: take the last stroke back.")))
                        Undo();
                using (new EditorGUI.DisabledScope(_redo.Count == 0))
                    if (GUILayout.Button(new GUIContent("Redo", "Shift+Z: put it back.")))
                        Redo();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Clear", "Remove everything painted on this node.")) &&
                    EditorUtility.DisplayDialog("Clear Paint", $"Remove everything painted on \"{node.Name}\"?", "Clear", "Keep"))
                {
                    Remember();
                    _layer.CopyFrom(new PaintLayer(_layer.Size));
                    Painted();
                }
                if (GUILayout.Button(new GUIContent("Save", "Save what's painted to its textures now (it saves itself after each stroke too).")))
                    SaveNow();
            }
            GUILayout.FlexibleSpace();
            foreach (var warning in _made.Warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            GUILayout.Label(_saveAt > 0 ? "Unsaved strokes (saving shortly)." : "Saved.", EditorStyles.miniLabel);
            GUILayout.EndArea();
        }

        // ---- Camera ----

        private Camera PlaceCamera()
        {
            if (_preview == null)
            {
                _preview = new PreviewRenderUtility();
                _preview.camera.fieldOfView = 30f;
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.backgroundColor = new Color(0.18f, 0.18f, 0.2f);
                _preview.lights[0].intensity = 1.2f;
                _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                _preview.lights[1].intensity = 0.6f;
                _preview.ambientColor = new Color(0.25f, 0.25f, 0.28f);
            }
            var camera = _preview.camera;
            var rotation = Quaternion.Euler(_angles.x, _angles.y, 0f);
            camera.transform.SetPositionAndRotation(_target + rotation * new Vector3(0f, 0f, -_distance), rotation);
            camera.nearClipPlane = Mathf.Max(0.001f, _distance * 0.002f);
            camera.farClipPlane = Mathf.Max(100f, _distance * 20f);
            camera.aspect = Mathf.Max(0.01f, _viewRect.width / Mathf.Max(1f, _viewRect.height));
            return camera;
        }

        private void Frame()
        {
            if (_surface == null)
                return;
            _target = _surface.Bounds.center;
            _distance = Mathf.Max(0.05f, _surface.Bounds.extents.magnitude * 3f);
            if (_surface.Tiling)
                _angles = new Vector2(70f, 0f);
            Repaint();
        }

        // ---- Input ----

        private void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
                return;
            switch (e.keyCode)
            {
                case KeyCode.LeftBracket:
                    _size = Mathf.Max(_surface.Size * 0.002f, _size / 1.2f);
                    break;
                case KeyCode.RightBracket:
                    _size = Mathf.Min(_surface.Size * 0.5f, _size * 1.2f);
                    break;
                case KeyCode.Z when e.shift:
                    Redo();
                    break;
                case KeyCode.Z:
                    Undo();
                    break;
                case KeyCode.F:
                    Frame();
                    break;
                default:
                    return;
            }
            e.Use();
            Repaint();
        }

        private void HandleMouse()
        {
            var e = Event.current;
            bool inView = _viewRect.Contains(e.mousePosition);
            switch (e.type)
            {
                case EventType.MouseMove:
                    _hover = inView && Hit(e.mousePosition, out _hoverPoint, out _hoverNormal);
                    Repaint();
                    break;

                case EventType.MouseDown when inView:
                    GUIUtility.keyboardControl = 0;
                    if (e.button == 0 && !e.alt)
                    {
                        if (Hit(e.mousePosition, out var point, out var normal))
                        {
                            Remember();
                            _stroking = true;
                            Stamp(point, normal, _erase || e.control);
                            _lastStamp = point;
                        }
                    }
                    else
                        _orbiting = true;
                    e.Use();
                    break;

                case EventType.MouseDrag when _stroking:
                {
                    _hover = Hit(e.mousePosition, out _hoverPoint, out _hoverNormal);
                    if (_hover)
                    {
                        // Dabs a quarter of the brush apart along the way.
                        float spacing = Mathf.Max(_size * 0.25f, 1e-5f);
                        float gap = Vector3.Distance(_lastStamp, _hoverPoint);
                        if (gap >= spacing)
                        {
                            int steps = Mathf.Min(64, Mathf.FloorToInt(gap / spacing));
                            for (int s = 1; s <= steps; s++)
                                Stamp(Vector3.Lerp(_lastStamp, _hoverPoint, s / (float)steps), _hoverNormal, _erase || e.control);
                            _lastStamp = _hoverPoint;
                        }
                    }
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseDrag when _orbiting:
                    if (e.button == 2)
                    {
                        var camera = PlaceCamera();
                        float depth = Vector3.Dot(_target - camera.transform.position, camera.transform.forward);
                        float perPixel = 2f * Mathf.Max(0.001f, depth) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, _viewRect.height);
                        _target += (-camera.transform.right * e.delta.x + camera.transform.up * e.delta.y) * perPixel;
                    }
                    else
                    {
                        _angles.y += e.delta.x * 0.5f;
                        _angles.x = Mathf.Clamp(_angles.x + e.delta.y * 0.5f, -89f, 89f);
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp:
                    if (_stroking)
                        _saveAt = EditorApplication.timeSinceStartup + 1.0;
                    _stroking = _orbiting = false;
                    break;

                case EventType.ScrollWheel when inView:
                    if (e.shift)
                        _size = Mathf.Clamp(_size * (1f + e.delta.y * 0.05f), _surface.Size * 0.002f, _surface.Size * 0.5f);
                    else
                        _distance = Mathf.Max(0.01f, _distance * (1f + e.delta.y * 0.05f));
                    e.Use();
                    Repaint();
                    break;
            }
        }

        // Where a ray from the camera through the mouse meets the model (its own space), and the face's direction there.
        private bool Hit(Vector2 mouse, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (_surface == null)
                return false;
            var camera = PlaceCamera();
            var viewport = new Vector3((mouse.x - _viewRect.x) / _viewRect.width, 1f - (mouse.y - _viewRect.y) / _viewRect.height, 0f);
            var ray = camera.ViewportPointToRay(viewport);
            if (!_surface.Bvh.Raycast(ray.origin, ray.direction, camera.farClipPlane, out float distance, out int triangle))
                return false;
            point = ray.origin + ray.direction * distance;
            normal = _surface.FaceNormals[triangle];
            if (Vector3.Dot(normal, ray.direction) > 0f)
                normal = -normal;
            return true;
        }

        private static Vector3 Mirror(Vector3 v) => new(-v.x, v.y, v.z);

        // ---- Painting ----

        private void Stamp(Vector3 point, Vector3 normal, bool erase)
        {
            Dab(point, normal, erase);
            if (_mirrorX)
                Dab(Mirror(point), Mirror(normal), erase);
            Painted();
        }

        private void Dab(Vector3 point, Vector3 normal, bool erase)
        {
            _surface.TexelsNear(point, normal, _size, _found);
            float inner = Mathf.Clamp01(_hardness) * 0.99f;
            float flow = _flow * 0.5f;
            var layer = _layer;
            foreach (var (i, d) in _found)
            {
                float t = d / _size;
                float w = (t <= inner ? 1f : 1f - Smooth((t - inner) / (1f - inner))) * flow;
                if (w <= 0f)
                    continue;
                if (_paintColor)
                {
                    var current = layer.Colour[i];
                    if (erase)
                        current.a *= 1f - w;
                    else
                    {
                        var mixed = current.a > 0f ? Color.Lerp(current, _color, w) : _color;
                        mixed.a = current.a + (1f - current.a) * w;
                        current = mixed;
                    }
                    layer.Colour[i] = current;
                }
                if (_paintRoughness)
                    Channel(layer.Roughness, layer.RoughnessCover, i, _roughness, w, erase);
                if (_paintMetallic)
                    Channel(layer.Metallic, layer.MetallicCover, i, _metallic, w, erase);
                if (_paintHeight)
                    layer.HeightOffset[i] = erase
                        ? Mathf.Lerp(layer.HeightOffset[i], 0.5f, w)
                        : Mathf.Clamp01(layer.HeightOffset[i] + _heightAmount * w * 0.1f);
                if (_paintMask)
                    layer.Mask[i] = erase ? layer.Mask[i] * (1f - w) : layer.Mask[i] + (1f - layer.Mask[i]) * w;
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static void Channel(float[] values, float[] cover, int i, float value, float w, bool erase)
        {
            if (erase)
            {
                cover[i] *= 1f - w;
                return;
            }
            values[i] = cover[i] > 0f ? Mathf.Lerp(values[i], value, w) : value;
            cover[i] += (1f - cover[i]) * w;
        }

        private void Painted()
        {
            _paintChanged = true;
            _maskDirty = true;
            _saveAt = Math.Max(_saveAt, EditorApplication.timeSinceStartup + 1.0);
        }

        // ---- Undo ----

        // Keeps the painting as it is now, to come back to.
        private void Remember()
        {
            _undo.Add(_layer.Clone());
            if (_undo.Count > UndoSteps)
                _undo.RemoveAt(0);
            _redo.Clear();
        }

        private void Undo()
        {
            if (_undo.Count == 0)
                return;
            _redo.Add(_layer.Clone());
            _layer.CopyFrom(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            Painted();
            Repaint();
        }

        private void Redo()
        {
            if (_redo.Count == 0)
                return;
            _undo.Add(_layer.Clone());
            _layer.CopyFrom(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            Painted();
            Repaint();
        }

        // ---- Saving ----

        /// <summary>Writes what's painted to the node's three textures (made next to the graph the first time).</summary>
        private void SaveNow()
        {
            var node = Node;
            if (node == null || _layer == null || _saveAt <= 0)
            {
                _saveAt = 0;
                return;
            }
            _saveAt = 0;
            string folder = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(_graph)).Replace('\\', '/')}/{_graph.name} Paint";
            SmartMaterialEditor.EnsureFolder(folder);
            string id = string.IsNullOrEmpty(node.ID) ? "Paint" : node.ID.Substring(0, Mathf.Min(8, node.ID.Length));
            var (colour, surface, heightMask) = _layer.ToPixels();
            var colourTexture = Write($"{folder}/{id}_Colour.png", colour, _layer.Size);
            var surfaceTexture = Write($"{folder}/{id}_Surface.png", surface, _layer.Size);
            var heightMaskTexture = Write($"{folder}/{id}_HeightMask.png", heightMask, _layer.Size);
            if (node.ColourTexture != colourTexture || node.SurfaceTexture != surfaceTexture || node.HeightMaskTexture != heightMaskTexture)
            {
                UnityEditor.Undo.RecordObject(_graph, "Paint");
                node.ColourTexture = colourTexture;
                node.SurfaceTexture = surfaceTexture;
                node.HeightMaskTexture = heightMaskTexture;
                EditorUtility.SetDirty(_graph);
            }
            // The graph window's preview picks the strokes up.
            _graph.Touch();
        }

        // A painted texture as a PNG: readable, exact (no compression, no colour conversion, no mipmaps).
        private static Texture2D Write(string path, Color32[] pixels, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                bool changed = !importer.isReadable || importer.sRGBTexture || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed
                               || importer.maxTextureSize < size || importer.npotScale != TextureImporterNPOTScale.None || importer.alphaSource != TextureImporterAlphaSource.FromInput;
                if (changed)
                {
                    importer.isReadable = true;
                    importer.sRGBTexture = false;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, size);
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = false;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }

    /// <summary>The Paint node: a Paint button opening its viewport.</summary>
    [CustomNodeView(typeof(PaintNode))]
    public class PaintNodeView : NodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4, paddingRight = 4, paddingTop = 2, paddingBottom = 2 } };
            row.Add(new Button(OpenPainter) { text = "Paint...", tooltip = "Open its viewport and paint on the model." });
            extensionContainer.Add(row);
            titleContainer.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2)
                {
                    OpenPainter();
                    e.StopPropagation();
                }
            });
            RefreshExpandedState();
        }

        private void OpenPainter()
        {
            if (Target is PaintNode node && node.Graph is SmartMaterialGraph graph)
                PaintWindow.Open(graph, node);
        }
    }
}
