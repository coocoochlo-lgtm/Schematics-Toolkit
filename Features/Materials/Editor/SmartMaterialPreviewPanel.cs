using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Remedy.Materials
{
    /// <summary>The maps a preview can show flat (2D), one at a time.</summary>
    internal enum PreviewChannel { BaseColor, Normal, Roughness, Metallic, Height, Occlusion }

    /// <summary>
    /// A Smart Material made for showing: its textures as Unity textures (remade in place each time), a lit material
    /// wearing them, and its grey channels for a flat view. Shared by the graph window's preview and the paint window.
    /// </summary>
    internal sealed class MaterialPreview : IDisposable
    {
        private readonly Dictionary<string, Texture2D> _textures = new();
        private readonly Dictionary<PreviewChannel, Texture2D> _channels = new();
        private Material _material;

        public List<string> Warnings = new();
        public int Size;
        public double Seconds;

        /// <summary>The lit material wearing the made textures.</summary>
        public Material Material
        {
            get
            {
                if (_material == null)
                    _material = new Material(SmartMaterialEditor.DefaultMaterial()) { hideFlags = HideFlags.HideAndDontSave, name = "Smart Material Preview" };
                return _material;
            }
        }

        /// <summary>A map to show flat.</summary>
        public Texture2D Channel(PreviewChannel channel) => _channels.TryGetValue(channel, out var t) ? t : null;

        /// <summary>
        /// Makes a graph's material at <paramref name="size"/> (in the background) and its textures. Null when it
        /// couldn't be made (and why, in the Console).
        /// </summary>
        public static async Task<(List<BakedTexture> baked, Dictionary<PreviewChannel, Color32[]> channels, List<string> warnings, double seconds)?> Make(SmartMaterialGraph graph, int size)
        {
            var output = graph.Output;
            if (output == null)
                return null;
            var plan = MaterialCompiler.Compile(graph, size);
            var packing = SmartMaterialEditor.Resolve(output.Packing);
            float depth = output.HeightDepth, distance = output.OcclusionDistance;
            bool ao = output.AmbientOcclusion;
            var started = DateTime.Now;
            try
            {
                return await Task.Run(() =>
                {
                    var result = plan.Run();
                    var baked = MaterialBaker.Bake(result, packing, depth, ao, distance, 2, false);
                    var maps = result.Maps;
                    var channels = new Dictionary<PreviewChannel, Color32[]>
                    {
                        [PreviewChannel.Roughness] = Grey(maps.Roughness),
                        [PreviewChannel.Metallic] = Grey(maps.Metallic),
                        [PreviewChannel.Height] = Grey(maps.HeightMap),
                    };
                    var occlusion = new float[maps.Count];
                    var meshAo = ao && !result.Surface.Tiling ? result.Surface.AmbientOcclusion(distance, 32) : null;
                    for (int i = 0; i < occlusion.Length; i++)
                        occlusion[i] = maps.Occlusion[i] * (meshAo != null ? meshAo[i] : 1f);
                    channels[PreviewChannel.Occlusion] = Grey(occlusion);
                    return ((List<BakedTexture>, Dictionary<PreviewChannel, Color32[]>, List<string>, double)?)(baked, channels, new List<string>(result.Warnings), (DateTime.Now - started).TotalSeconds);
                });
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, graph);
                return null;
            }
        }

        private static Color32[] Grey(float[] values)
        {
            var pixels = new Color32[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(values[i] * 255f), 0, 255);
                pixels[i] = new Color32(v, v, v, 255);
            }
            return pixels;
        }

        /// <summary>Puts what <see cref="Make"/> made onto the textures and the material (main thread).</summary>
        public void Apply((List<BakedTexture> baked, Dictionary<PreviewChannel, Color32[]> channels, List<string> warnings, double seconds) made)
        {
            var assigned = new Dictionary<string, Texture>();
            foreach (var baked in made.baked)
            {
                var pixels = baked.Pixels;
                if (baked.IsNormalMap)
                {
                    // X in alpha too: read right whether the shader expects RGB or Unity's AG-packed normals.
                    pixels = (Color32[])pixels.Clone();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i].a = pixels[i].r;
                }
                var texture = Upload(_textures, baked.Suffix, baked.Width, baked.Height, pixels, !baked.IsColor);
                assigned[baked.Suffix] = texture;
                if (baked.Suffix == MaterialBaker.BaseColor)
                    _channels[PreviewChannel.BaseColor] = texture;
                else if (baked.IsNormalMap)
                    _channels[PreviewChannel.Normal] = Upload(_textures, "NormalFlat", baked.Width, baked.Height, baked.Pixels, true);
            }
            foreach (var (channel, pixels) in made.channels)
            {
                int size = Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
                _channels[channel] = Upload(_textures, "Channel" + channel, size, size, pixels, true);
            }
            SmartMaterialEditor.Assign(Material, assigned);
            Warnings = made.warnings;
            Seconds = made.seconds;
            Size = made.baked.Count > 0 ? made.baked[0].Width : 0;
        }

        private static Texture2D Upload(Dictionary<string, Texture2D> cache, string key, int width, int height, Color32[] pixels, bool linear)
        {
            if (!cache.TryGetValue(key, out var texture) || texture == null || texture.width != width || texture.height != height)
            {
                if (texture != null)
                    Object.DestroyImmediate(texture);
                texture = new Texture2D(width, height, TextureFormat.RGBA32, true, linear) { hideFlags = HideFlags.HideAndDontSave, name = key, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
                cache[key] = texture;
            }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        public void Dispose()
        {
            foreach (var texture in _textures.Values)
                if (texture != null)
                    Object.DestroyImmediate(texture);
            _textures.Clear();
            _channels.Clear();
            if (_material != null)
                Object.DestroyImmediate(_material);
        }
    }

    /// <summary>
    /// The Smart Material window's preview, in its bottom-right corner: the material on its mesh (turning slowly until
    /// you drag it), or one of its maps laid flat. It's remade in the background a moment after each edit, at the
    /// Output's Preview Resolution. Bake saves the textures and a material next to the graph.
    /// </summary>
    internal sealed class SmartMaterialPreviewPanel : VisualElement
    {
        private const float Width = 300f, Height = 260f;

        private readonly SmartMaterialGraph _graph;
        private readonly IMGUIContainer _view;
        private readonly Label _stats, _warnings;
        private readonly VisualElement _body;
        private readonly MaterialPreview _made = new();
        private PreviewRenderUtility _preview;
        private Mesh _mesh;
        private Action _releaseMesh;
        private Object _meshSource;
        private int _meshVersion = int.MinValue;
        private int _shownVersion = int.MinValue;
        private bool _making, _dragged, _collapsed, _flat;
        private PreviewChannel _channel;
        private Vector2 _angles = new(20f, -30f);
        private float _distance = 3f;
        private double _lastEdit;

        public SmartMaterialPreviewPanel(SmartMaterialGraph graph)
        {
            _graph = graph;
            name = "smart-material-preview";
            style.position = Position.Absolute;
            style.right = 8;
            style.bottom = 8;
            style.width = Width;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.92f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 6;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 6;

            var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            top.Add(new Label("MATERIAL") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.55f, 0.85f, 0.75f), flexGrow = 1 } });
            var mode = new Button(() =>
            {
                _flat = !_flat;
                _view.MarkDirtyRepaint();
            }) { text = "2D", tooltip = "Show one of its maps laid flat (2D), or the material on its mesh (3D)." };
            top.Add(mode);
            var channel = new UnityEditor.UIElements.EnumField(PreviewChannel.BaseColor) { tooltip = "The map shown flat in 2D.", style = { width = 90 } };
            channel.RegisterValueChangedCallback(e =>
            {
                _channel = (PreviewChannel)e.newValue;
                _flat = true;
                _view.MarkDirtyRepaint();
            });
            top.Add(channel);
            top.Add(new Button(BakeClicked) { text = "Bake", tooltip = "Make it at the Output's Resolution and save its textures (PNG) and a material next to the graph." });
            top.Add(new Button(() => SetCollapsed(!_collapsed)) { text = "–", tooltip = "Hide or show the preview." });
            Add(top);

            _body = new VisualElement();
            Add(_body);
            _view = new IMGUIContainer(DrawPreview) { style = { height = Height, marginTop = 4 } };
            _body.Add(_view);
            _stats = new Label { style = { fontSize = 10, color = new Color(0.75f, 0.75f, 0.75f), marginTop = 2 } };
            _body.Add(_stats);
            _warnings = new Label { style = { fontSize = 10, color = new Color(1f, 0.6f, 0.4f), whiteSpace = WhiteSpace.Normal } };
            _body.Add(_warnings);

            RegisterCallback<AttachToPanelEvent>(_ => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        private void SetCollapsed(bool collapsed)
        {
            _collapsed = collapsed;
            _body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Dispose()
        {
            EditorApplication.update -= Tick;
            _releaseMesh?.Invoke();
            _releaseMesh = null;
            _made.Dispose();
            _preview?.Cleanup();
            _preview = null;
        }

        private void Tick()
        {
            if (_graph == null)
                return;
            if (_graph.Version != _shownVersion && !_making)
            {
                if (_lastEdit == 0)
                    _lastEdit = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - _lastEdit > 0.25)
                {
                    _lastEdit = 0;
                    Remake();
                }
            }
            if (!_dragged && !_collapsed && !_flat)
            {
                _angles.y += 0.3f;
                _view.MarkDirtyRepaint();
            }
        }

        private async void Remake()
        {
            _making = true;
            int version = _graph.Version;
            _stats.text = "Making...";
            try
            {
                // The mesh, when what it's made for has changed.
                var source = _graph.Output != null ? _graph.Output.Mesh : null;
                int meshVersion = source is Meshes.MeshGraph meshGraph ? meshGraph.Version : 0;
                if (_mesh == null || source != _meshSource || meshVersion != _meshVersion)
                {
                    var (mesh, release) = await SmartMaterialEditor.PreviewMesh(_graph);
                    _releaseMesh?.Invoke();
                    _mesh = mesh;
                    _releaseMesh = release;
                    _meshSource = source;
                    _meshVersion = meshVersion;
                    if (_mesh != null)
                        _distance = Mathf.Max(0.1f, _mesh.bounds.extents.magnitude) * 3.2f;
                }
                var output = _graph.Output;
                var made = await MaterialPreview.Make(_graph, output != null ? output.PreviewResolution : 256);
                if (panel == null)
                    return;
                if (made.HasValue)
                {
                    _made.Apply(made.Value);
                    _stats.text = $"{_made.Size}×{_made.Size} in {_made.Seconds:0.00} s" + (output != null ? $"  ·  bakes at {output.Resolution}" : "");
                    _warnings.text = string.Join("\n", _made.Warnings);
                }
                else
                    _stats.text = "Couldn't make it (see the Console).";
                _shownVersion = version;
                _view.MarkDirtyRepaint();
            }
            finally
            {
                _making = false;
            }
        }

        private void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(Width - 12, Height, GUILayout.ExpandWidth(true));
            HandleInput(rect);
            if (Event.current.type != EventType.Repaint)
                return;
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.2f));
            if (_flat)
            {
                var texture = _made.Channel(_channel);
                if (texture != null)
                {
                    float side = Mathf.Min(rect.width, rect.height);
                    GUI.DrawTexture(new Rect(rect.center.x - side / 2f, rect.center.y - side / 2f, side, side), texture, ScaleMode.ScaleToFit, false);
                }
                return;
            }
            if (_mesh == null)
            {
                GUI.Label(rect, _making ? "Making..." : "Nothing to show yet", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (_preview == null)
            {
                _preview = new PreviewRenderUtility();
                _preview.camera.fieldOfView = 30f;
                _preview.camera.nearClipPlane = 0.01f;
                _preview.camera.farClipPlane = 1000f;
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.backgroundColor = new Color(0.18f, 0.18f, 0.2f);
                _preview.lights[0].intensity = 1.2f;
                _preview.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
                _preview.lights[1].intensity = 0.6f;
                _preview.ambientColor = new Color(0.25f, 0.25f, 0.28f);
            }
            var center = _mesh.bounds.center;
            var rotation = Quaternion.Euler(_angles.x, _angles.y, 0f);
            _preview.camera.transform.position = center + rotation * new Vector3(0f, 0f, -_distance);
            _preview.camera.transform.rotation = rotation;
            _preview.camera.farClipPlane = _distance * 10f;
            _preview.BeginPreview(rect, GUIStyle.none);
            for (int s = 0; s < _mesh.subMeshCount; s++)
                _preview.DrawMesh(_mesh, Matrix4x4.identity, _made.Material, s);
            _preview.camera.Render();
            GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        // Drag to turn it, scroll to zoom.
        private void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) || _flat)
                return;
            if (e.type == EventType.MouseDrag)
            {
                _dragged = true;
                _angles.y += e.delta.x * 0.6f;
                _angles.x = Mathf.Clamp(_angles.x + e.delta.y * 0.6f, -89f, 89f);
                e.Use();
                _view.MarkDirtyRepaint();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                _distance = Mathf.Max(0.05f, _distance * (1f + e.delta.y * 0.05f));
                e.Use();
                _view.MarkDirtyRepaint();
            }
        }

        private async void BakeClicked()
        {
            var material = await SmartMaterialEditor.Bake(_graph);
            if (material != null)
            {
                EditorGUIUtility.PingObject(material);
                Debug.Log($"Baked {_graph.name} to {AssetDatabase.GetAssetPath(material)}.", material);
            }
        }
    }
}
