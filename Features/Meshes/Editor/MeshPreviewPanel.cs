using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Meshes
{
    /// <summary>
    /// The Mesh Graph window's preview, in its bottom-right corner: the model, turning slowly until you drag it, with a
    /// button per LOD and its triangle count. It's remade in the background a moment after each edit. Bake saves it as
    /// assets (meshes and a prefab with its LODs).
    /// </summary>
    internal sealed class MeshPreviewPanel : VisualElement
    {
        private const float Width = 300f, Height = 240f;

        private readonly MeshGraph _graph;
        private readonly IMGUIContainer _view;
        private readonly Label _stats, _warnings;
        private readonly VisualElement _lodButtons, _body;
        private PreviewRenderUtility _preview;
        private GeneratedMesh _generated;
        private int _level;
        private int _shownVersion = -1;
        private bool _making;
        private Vector2 _angles = new(25f, -30f);
        private float _distance = 1f;
        private bool _dragged, _collapsed;
        private double _lastEdit;

        public MeshPreviewPanel(MeshGraph graph)
        {
            _graph = graph;
            name = "mesh-graph-preview";
            style.position = Position.Absolute;
            style.right = 8;
            style.bottom = 8;
            style.width = Width;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.92f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 6;
            style.paddingLeft = style.paddingRight = style.paddingTop = style.paddingBottom = 6;

            var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var title = new Label("MESH") { style = { unityFontStyleAndWeight = FontStyle.Bold, color = new Color(0.95f, 0.7f, 0.4f), flexGrow = 1 } };
            top.Add(title);
            var bake = new Button(BakeClicked) { text = "Bake", tooltip = "Save the model (with its LODs) as mesh assets and a prefab next to the graph." };
            top.Add(bake);
            var fold = new Button(() => SetCollapsed(!_collapsed)) { text = "–", tooltip = "Hide or show the preview." };
            top.Add(fold);
            Add(top);

            _body = new VisualElement();
            Add(_body);
            _view = new IMGUIContainer(DrawPreview) { style = { height = Height, marginTop = 4 } };
            _body.Add(_view);
            _lodButtons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            _body.Add(_lodButtons);
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
            if (_generated != null)
                MeshGraphGenerator.Release(_generated);
            _generated = null;
            _preview?.Cleanup();
            _preview = null;
        }

        private void Tick()
        {
            if (_graph == null)
                return;
            // An edit: remade a moment later (not on every keystroke).
            if (_graph.Version != _shownVersion && !_making)
            {
                if (_lastEdit == 0)
                    _lastEdit = EditorApplication.timeSinceStartup;
                if (EditorApplication.timeSinceStartup - _lastEdit > 0.2)
                {
                    _lastEdit = 0;
                    Remake();
                }
            }
            if (!_dragged && !_collapsed)
            {
                _angles.y += 0.4f;
                _view.MarkDirtyRepaint();
            }
        }

        private async void Remake()
        {
            _making = true;
            int version = _graph.Version;
            var generated = await MeshGraphGenerator.Generate(_graph);
            _making = false;
            if (panel == null)
            {
                MeshGraphGenerator.Release(generated);
                return;
            }
            if (_generated != null)
                MeshGraphGenerator.Release(_generated);
            _generated = generated;
            _shownVersion = version;
            if (_generated != null && _generated.Levels.Length > 0)
            {
                var bounds = _generated.Levels[0].bounds;
                _distance = Mathf.Max(0.1f, bounds.extents.magnitude) * 3.2f;
            }
            _level = Mathf.Clamp(_level, 0, _generated != null ? Mathf.Max(0, _generated.Levels.Length - 1) : 0);
            RefreshInfo();
            _view.MarkDirtyRepaint();
        }

        private void RefreshInfo()
        {
            _lodButtons.Clear();
            if (_generated == null)
            {
                _stats.text = "Couldn't make the model (see the Console).";
                return;
            }
            for (int i = 0; i < _generated.Levels.Length; i++)
            {
                int level = i;
                var button = new Button(() => { _level = level; RefreshInfo(); _view.MarkDirtyRepaint(); }) { text = $"LOD{i}" };
                if (i == _level)
                    button.style.backgroundColor = new Color(0.3f, 0.45f, 0.7f);
                _lodButtons.Add(button);
            }
            var data = _generated.Data[_level];
            _stats.text = string.Join("   ", _generated.Data.Select((d, i) => $"LOD{i}: {d.TriangleCount:N0} tris"))
                          + $"\nShowing LOD{_level}: {data.VertexCount:N0} vertices, {data.Slots.Count(s => s.Count > 0)} material slot(s)";
            _warnings.text = string.Join("\n", _generated.Warnings);
        }

        private void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(Width - 12, Height, GUILayout.ExpandWidth(true));
            HandleInput(rect);
            if (Event.current.type != EventType.Repaint)
                return;
            if (_generated == null || _generated.Levels.Length == 0 || _generated.Levels[_level] == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.2f));
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
            }
            var mesh = _generated.Levels[_level];
            var center = mesh.bounds.center;
            var rotation = Quaternion.Euler(_angles.x, _angles.y, 0f);
            _preview.camera.transform.position = center + rotation * new Vector3(0f, 0f, -_distance);
            _preview.camera.transform.rotation = rotation;
            _preview.camera.farClipPlane = _distance * 10f;

            _preview.BeginPreview(rect, GUIStyle.none);
            var material = MeshGraphEditor.DefaultMaterial();
            for (int s = 0; s < mesh.subMeshCount; s++)
                _preview.DrawMesh(mesh, Matrix4x4.identity, material, s);
            _preview.camera.Render();
            var texture = _preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        // Drag to turn it, scroll to zoom.
        private void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition))
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
            var prefab = await MeshGraphEditor.Bake(_graph);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"Baked {_graph.name} to {AssetDatabase.GetAssetPath(prefab)}.", prefab);
            }
        }
    }
}
