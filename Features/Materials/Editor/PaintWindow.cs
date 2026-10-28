using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine.Experimental.Rendering;
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
        private Color32[] _maskPixels;
        private bool _maskRebuild = true, _maskUpload;
        private PreviewRenderUtility _preview;
        private Vector2 _angles = new(25f, -30f);
        private Vector3 _target;
        private float _distance = 2f;
        private Rect _viewRect;

        private bool _paintChanged = true;
        private int _shownVersion = int.MinValue;
        // The material being made in the background, at what size and for which version of the graph; the size shown.
        private Task<(List<BakedTexture>, Dictionary<PreviewChannel, Color32[]>, List<string>, double)?> _job;
        private int _jobSize, _jobVersion, _shownSize;
        private double _jobStarted;
        private bool Making => _job != null;
        private double _saveAt;
        private readonly List<StrokeRecord> _undo = new(), _redo = new();
        // The stroke being painted: which texels it's touched (each remembered as it was, the first time).
        private StrokeRecord _stroke;
        private bool[] _touched;
        // Saving: a background write in progress, and whether the files have changed since Unity last imported them.
        private Task _writing;
        private bool _needsImport;
        private readonly List<(int texel, float distance)> _found = new();

        private bool _stroking, _orbiting;
        private Vector3 _lastStamp;
        private bool _hover;
        private Vector3 _hoverPoint, _hoverNormal;

        public static void Open(SmartMaterialGraph graph, PaintedNode node)
        {
            if (graph == null || node == null)
                return;
            var window = GetWindow<PaintWindow>();
            window.titleContent = new GUIContent("Paint");
            window.minSize = new Vector2(640, 400);
            if (window._graph != graph || window._nodeId != node.ID)
                window.FinishSaving();
            window._graph = graph;
            window._nodeId = node.ID;
            window.Load();
            window.Show();
        }

        private PaintedNode Node => _graph != null && !string.IsNullOrEmpty(_nodeId) ? _graph.GetNodeById(_nodeId) as PaintedNode : null;

        // A Paint Mask only paints its mask.
        private bool MaskOnly => Node is { MaskOnly: true };
        private bool PaintsColor => _paintColor && !MaskOnly;
        private bool PaintsRoughness => _paintRoughness && !MaskOnly;
        private bool PaintsMetallic => _paintMetallic && !MaskOnly;
        private bool PaintsHeight => _paintHeight && !MaskOnly;
        private bool PaintsMask => _paintMask || MaskOnly;

        private void OnEnable()
        {
            wantsMouseMove = true;
            EditorApplication.update += Tick;
            if (_graph != null)
                Load();
        }

        private void OnDisable()
        {
            FinishSaving();
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
            _stroke = null;
            _touched = new bool[size * size];
            try
            {
                EditorUtility.DisplayProgressBar("Paint", "Laying the model out for painting...", 0.5f);
                _surface = MaterialCompiler.SurfaceFor(_graph, size);
                // The brush's lookup grid, made now rather than on the first dab.
                _surface.TexelsNear(_surface.Bounds.center, Vector3.up, 0f, _found);
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
            _maskRebuild = true;
            _paintChanged = true;
            _shownSize = 0;
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
                to.Erase[i] = MapSampling.Sample(from.Erase, from.Size, size, size, i);
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
            Pump();
            if (_saveAt > 0 && EditorApplication.timeSinceStartup > _saveAt && !_stroking)
                WriteInBackground();
        }

        // The material made again, coarse first and then finer (128, 256, then the preview's size), so a stroke shows up
        // quickly; more painting starts it over from coarse. The background job is checked from here, on the editor's
        // update and on every event the window gets (mouse drags included), rather than awaited: an await resumes
        // through Unity's main-thread queue, which doesn't run while the mouse is held down, so nothing showed until
        // the stroke ended. Mid-stroke it's remade a few times a second at most, at the coarse size.
        private void Pump()
        {
            if (_graph == null)
                return;
            if (_job != null)
            {
                if (!_job.IsCompleted)
                    return;
                var made = _job.Status == TaskStatus.RanToCompletion ? _job.Result : null;
                _job = null;
                if (made.HasValue)
                {
                    _made.Apply(made.Value);
                    _shownVersion = _jobVersion;
                    _shownSize = _jobSize;
                }
                Repaint();
            }

            var output = _graph.Output;
            int full = output != null ? output.PreviewResolution : 512;
            int next;
            if (_paintChanged || _graph.Version != _shownVersion)
            {
                if (_stroking && EditorApplication.timeSinceStartup - _jobStarted < 0.25)
                    return;
                next = Mathf.Min(128, full);
            }
            else if (_shownSize < full && !_stroking)
                next = _shownSize < 256 ? Mathf.Min(256, full) : full;
            else
                return;

            _paintChanged = false;
            _jobSize = next;
            _jobVersion = _graph.Version;
            _jobStarted = EditorApplication.timeSinceStartup;
            _job = MaterialPreview.Make(_graph, next);
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
            Pump();
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
            _made.UploadPatches();
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
            string status = Making ? "Updating..." : "";
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
                SmartMaterialEditor.Assign(_maskMaterial, new Dictionary<string, Texture> { [MaterialBaker.BaseColor] = _maskTexture });
                _maskRebuild = true;
            }
            // All of it after a load or an undo; otherwise dabs have already updated their own pixels.
            if (_maskRebuild || _maskPixels == null || _maskPixels.Length != size * size)
            {
                _maskPixels = new Color32[size * size];
                for (int i = 0; i < _maskPixels.Length; i++)
                    _maskPixels[i] = MaskPixel(_layer.Mask[i]);
                _maskRebuild = false;
                _maskUpload = true;
            }
            if (_maskUpload)
            {
                _maskTexture.SetPixels32(_maskPixels);
                _maskTexture.Apply(true);
                _maskUpload = false;
            }
            return _maskMaterial;
        }

        private static Color32 MaskPixel(float mask) => Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(1f, 0.15f, 0.1f), mask);

        private void DrawSide(Rect rect, PaintedNode node)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6, rect.y + 4, rect.width - 12, rect.height - 8));
            GUILayout.Label($"{_graph.name} › {node.Name}", EditorStyles.boldLabel);
            _view = (View)GUILayout.Toolbar((int)_view, new[] { new GUIContent("Material", "The whole Smart Material, with what's painted."), new GUIContent("Mask", "Just the painted mask (red), for painting a mask that isn't wired in yet.") });
            GUILayout.Space(6);

            EditorGUIUtility.labelWidth = 80;
            if (MaskOnly)
                EditorGUILayout.HelpBox("Paints this node's mask: painting adds to the mask coming in, Ctrl (or Erase) carves it away.", MessageType.None);
            else
            {
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
            }

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
                    BeginStroke();
                    for (int i = 0; i < _layer.Size * _layer.Size; i++)
                        Touch(i);
                    _layer.CopyFrom(new PaintLayer(_layer.Size));
                    EndStroke();
                    _maskRebuild = true;
                    Painted();
                }
                if (GUILayout.Button(new GUIContent("Save", "Save what's painted and have Unity import it now (strokes are written to disk as you go; Unity imports the file when this window closes).")))
                    FinishSaving();
            }
            if (GUILayout.Button(new GUIContent("Export PNGs", "Also write what's painted as PNG textures (colour, surface, height and mask), to open in other apps.")))
                ExportPngs();
            GUILayout.FlexibleSpace();
            foreach (var warning in _made.Warnings)
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            GUILayout.Label(_saveAt > 0 || _writing is { IsCompleted: false } ? "Saving strokes..." : _needsImport ? "Saved (imported when this closes)." : "Saved.", EditorStyles.miniLabel);
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
                            BeginStroke();
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
                    {
                        EndStroke();
                        _saveAt = EditorApplication.timeSinceStartup + 1.0;
                    }
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
                Touch(i);
                if (PaintsColor)
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
                    // Shown straight away, before the material's made again. (Erasing waits for it: what's under the
                    // paint isn't known here.)
                    if (!erase)
                        _made.Patch(layer.Size, i, _color, w);
                }
                else if (!erase)
                    // Painting roughness, metal, height or a mask: a faint tint where it's going, until the material
                    // (which shows what it does) is made again.
                    _made.Patch(layer.Size, i, PaintsMask ? new Color(1f, 0.35f, 0.25f) : Color.white, w * 0.3f);
                if (PaintsRoughness)
                    Channel(layer.Roughness, layer.RoughnessCover, i, _roughness, w, erase);
                if (PaintsMetallic)
                    Channel(layer.Metallic, layer.MetallicCover, i, _metallic, w, erase);
                if (PaintsHeight)
                    layer.HeightOffset[i] = erase
                        ? Mathf.Lerp(layer.HeightOffset[i], 0.5f, w)
                        : Mathf.Clamp01(layer.HeightOffset[i] + _heightAmount * w * 0.1f);
                if (PaintsMask)
                {
                    // Erasing also carves away the mask coming in (on a Paint Mask); painting takes that back.
                    if (erase)
                    {
                        layer.Mask[i] *= 1f - w;
                        layer.Erase[i] += (1f - layer.Erase[i]) * w;
                    }
                    else
                    {
                        layer.Mask[i] += (1f - layer.Mask[i]) * w;
                        layer.Erase[i] *= 1f - w;
                    }
                    if (_maskPixels != null && i < _maskPixels.Length)
                    {
                        _maskPixels[i] = MaskPixel(layer.Mask[i]);
                        _maskUpload = true;
                    }
                }
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
            _saveAt = Math.Max(_saveAt, EditorApplication.timeSinceStartup + 1.0);
        }

        // ---- Undo ----

        // One texel's paint, everything about it.
        private struct TexelState
        {
            public Color Colour;
            public float Roughness, RoughnessCover, Metallic, MetallicCover, Height, Mask, Erase;
        }

        // What a stroke changed: the texels it touched, each as it was before. Small next to the whole painting, so
        // undo costs about as much as the stroke did.
        private sealed class StrokeRecord
        {
            public readonly List<int> Texels = new();
            public readonly List<TexelState> Before = new();
        }

        private TexelState Read(int i) => new()
        {
            Colour = _layer.Colour[i], Roughness = _layer.Roughness[i], RoughnessCover = _layer.RoughnessCover[i],
            Metallic = _layer.Metallic[i], MetallicCover = _layer.MetallicCover[i], Height = _layer.HeightOffset[i],
            Mask = _layer.Mask[i], Erase = _layer.Erase[i],
        };

        private void Write(int i, TexelState state)
        {
            _layer.Colour[i] = state.Colour;
            _layer.Roughness[i] = state.Roughness;
            _layer.RoughnessCover[i] = state.RoughnessCover;
            _layer.Metallic[i] = state.Metallic;
            _layer.MetallicCover[i] = state.MetallicCover;
            _layer.HeightOffset[i] = state.Height;
            _layer.Mask[i] = state.Mask;
            _layer.Erase[i] = state.Erase;
        }

        private void BeginStroke() => _stroke = new StrokeRecord();

        // A texel about to change: remembered as it was, the first time this stroke touches it.
        private void Touch(int i)
        {
            if (_stroke == null || _touched == null || _touched[i])
                return;
            _touched[i] = true;
            _stroke.Texels.Add(i);
            _stroke.Before.Add(Read(i));
        }

        private void EndStroke()
        {
            if (_stroke == null)
                return;
            foreach (int i in _stroke.Texels)
                _touched[i] = false;
            if (_stroke.Texels.Count > 0)
            {
                _undo.Add(_stroke);
                if (_undo.Count > UndoSteps)
                    _undo.RemoveAt(0);
                _redo.Clear();
            }
            _stroke = null;
        }

        // Puts a stroke's texels back as they were, and returns how they were just now (to go the other way).
        private StrokeRecord Swap(StrokeRecord record)
        {
            var other = new StrokeRecord();
            for (int k = 0; k < record.Texels.Count; k++)
            {
                int i = record.Texels[k];
                other.Texels.Add(i);
                other.Before.Add(Read(i));
                Write(i, record.Before[k]);
            }
            return other;
        }

        private void Undo()
        {
            if (_undo.Count == 0 || _stroking)
                return;
            _redo.Add(Swap(_undo[_undo.Count - 1]));
            _undo.RemoveAt(_undo.Count - 1);
            _maskRebuild = true;
            Painted();
            Repaint();
        }

        private void Redo()
        {
            if (_redo.Count == 0 || _stroking)
                return;
            _undo.Add(Swap(_redo[_redo.Count - 1]));
            _redo.RemoveAt(_redo.Count - 1);
            _maskRebuild = true;
            Painted();
            Repaint();
        }

        // ---- Saving ----
        //
        // Strokes are written in the background, to one small .bytes file (only the painted tiles, deflated: encoding
        // three big PNGs on the main thread was the brush's big hitch). Unity only imports it when the window closes,
        // or on Save: until then the node paints from its live layer. Export PNGs writes textures for other apps.

        private string PaintPath(PaintedNode node, string part, string extension = "png")
        {
            string folder = $"{Path.GetDirectoryName(AssetDatabase.GetAssetPath(_graph)).Replace('\\', '/')}/{_graph.name} Paint";
            string id = string.IsNullOrEmpty(node.ID) ? "Paint" : node.ID.Substring(0, Mathf.Min(8, node.ID.Length));
            return $"{folder}/{id}_{part}.{extension}";
        }

        private string DataPath(PaintedNode node) => PaintPath(node, "Paint", "bytes");

        // Writes the PNGs on a background thread (one write at a time; strokes since then are written by the next).
        private void WriteInBackground()
        {
            var node = Node;
            if (node == null || _layer == null)
            {
                _saveAt = 0;
                return;
            }
            if (_writing is { IsCompleted: false })
            {
                _saveAt = EditorApplication.timeSinceStartup + 0.5;
                return;
            }
            _saveAt = 0;
            SmartMaterialEditor.EnsureFolder(Path.GetDirectoryName(PaintPath(node, "x")).Replace('\\', '/'));
            var layer = _layer;
            string path = DataPath(node);
            _needsImport = true;
            // (Read while more strokes may land: those are in the next write.)
            _writing = Task.Run(() => File.WriteAllBytes(path, layer.ToBytes()));
        }

        /// <summary>Writes anything not yet written, waits for it, and has Unity import the textures onto the node.</summary>
        private void FinishSaving()
        {
            if (_stroking)
                EndStroke();
            if (_saveAt > 0)
                WriteInBackground();
            try
            {
                _writing?.Wait();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            _writing = null;
            var node = Node;
            if (node == null || !_needsImport)
                return;
            _needsImport = false;
            string path = DataPath(node);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var data = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            // (Older paintings' textures are let go: the file has everything.)
            if (node.PaintData != data || node.ColourTexture != null || node.SurfaceTexture != null || node.HeightMaskTexture != null)
            {
                UnityEditor.Undo.RecordObject(_graph, "Paint");
                node.PaintData = data;
                node.ColourTexture = node.SurfaceTexture = node.HeightMaskTexture = null;
                EditorUtility.SetDirty(_graph);
            }
            // The graph window's preview picks the strokes up.
            _graph.Touch();
        }

        /// <summary>Writes what's painted as PNGs too (colour, surface, height and mask), for opening in other apps.</summary>
        private void ExportPngs()
        {
            var node = Node;
            if (node == null || _layer == null)
                return;
            SmartMaterialEditor.EnsureFolder(Path.GetDirectoryName(PaintPath(node, "x")).Replace('\\', '/'));
            var (colour, surface, heightMask) = _layer.ToPixels();
            uint size = (uint)_layer.Size;
            var parts = node.MaskOnly
                ? new[] { ("HeightMask", heightMask) }
                : new[] { ("Colour", colour), ("Surface", surface), ("HeightMask", heightMask) };
            foreach (var (part, pixels) in parts)
            {
                string path = PaintPath(node, part);
                File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(pixels, GraphicsFormat.R8G8B8A8_UNorm, size, size));
                Import(path, _layer.Size);
            }
            EditorUtility.RevealInFinder(PaintPath(node, parts[0].Item1));
        }

        // An exported texture's import: exact (no compression, no colour conversion, no mipmaps).
        private static Texture2D Import(string path, int size)
        {
            if (!File.Exists(path))
                return null;
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

    /// <summary>The Paint and Paint Mask nodes: a Paint button opening their viewport.</summary>
    [CustomNodeView(typeof(PaintNode))]
    [CustomNodeView(typeof(PaintMaskNode))]
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
            if (Target is PaintedNode node && node.Graph is SmartMaterialGraph graph)
                PaintWindow.Open(graph, node);
        }
    }
}
