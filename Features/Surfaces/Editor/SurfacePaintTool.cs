using Remedy.LevelDesigner.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Remedy.Surfaces
{
    /// <summary>
    /// The Surface Paint brush (Level Designer, Variant mode): click and drag over the variant's geometry to paint
    /// Surface Materials onto it, triangle by triangle (the blockout X-ray under it isn't touched). Only surfaces facing
    /// the way the one under the cursor does are painted, so walls' backs aren't painted through. Each stroke is one
    /// undo step. Esc stops painting.
    /// </summary>
    [EditorTool("Surface Paint")]
    public class SurfacePaintTool : EditorTool
    {
        /// <summary>The material being painted (null with the eraser).</summary>
        public static SurfaceMaterial Selected;
        /// <summary>Erasing rather than painting.</summary>
        public static bool Erasing;

        /// <summary>The brush's radius, in meters.</summary>
        public static float Radius
        {
            get => EditorPrefs.GetFloat("Remedy.SurfacePaint.Radius", 0.75f);
            set => EditorPrefs.SetFloat("Remedy.SurfacePaint.Radius", Mathf.Clamp(value, 0.05f, 20f));
        }

        public static bool IsActive => ToolManager.activeToolType == typeof(SurfacePaintTool);

        public static void Activate() => ToolManager.SetActiveTool<SurfacePaintTool>();

        public override GUIContent toolbarIcon => EditorGUIUtility.IconContent("Grid.PaintTool");

        private bool _hasHit;
        private Vector3 _hitPoint, _hitNormal;
        private bool _stroking;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView))
                return;
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(control);

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ToolManager.RestorePreviousPersistentTool();
                e.Use();
                return;
            }

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.MouseDown)
            {
                _hasHit = PickSurface(e.mousePosition, out _hitPoint, out _hitNormal, out _, out _);
                window.Repaint();
            }

            bool painting = e.button == 0 && !e.alt;
            if (e.type == EventType.MouseDown && painting)
            {
                GUIUtility.hotControl = control;
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(Erasing ? "Erase Surfaces" : "Paint Surfaces");
                _stroking = true;
                if (_hasHit)
                    PaintAt(_hitPoint, _hitNormal);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _stroking && GUIUtility.hotControl == control)
            {
                if (_hasHit)
                    PaintAt(_hitPoint, _hitNormal);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _stroking)
            {
                _stroking = false;
                if (GUIUtility.hotControl == control)
                    GUIUtility.hotControl = 0;
                e.Use();
            }

            if (e.type == EventType.Repaint && _hasHit)
            {
                var color = Erasing || Selected == null ? new Color(1f, 1f, 1f, 0.9f) : Selected.PaintColor;
                color.a = 0.9f;
                Handles.color = color;
                Handles.DrawWireDisc(_hitPoint, _hitNormal, Radius, 2f);
                Handles.color = new Color(color.r, color.g, color.b, 0.15f);
                Handles.DrawSolidDisc(_hitPoint, _hitNormal, Radius);
            }
        }

        #region Picking and painting

        // The paintable surface under the cursor: the nearest triangle of a mesh in the variant scene.
        private static bool PickSurface(Vector2 mouse, out Vector3 point, out Vector3 normal, out MeshFilter filter, out int triangle)
        {
            point = normal = default;
            filter = null;
            triangle = -1;
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var ignore = new List<GameObject>();
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var picked = HandleUtility.PickGameObject(mouse, ignore.ToArray(), null, out _);
                if (picked == null)
                    return false;
                var candidate = picked.GetComponent<MeshFilter>();
                if (Paintable(candidate) && MeshRay.Cast(candidate, ray.origin, ray.direction, float.MaxValue, out triangle, out float distance, out normal))
                {
                    filter = candidate;
                    point = ray.origin + ray.direction * distance;
                    return true;
                }
                ignore.Add(picked); // the blockout X-ray, or something with no mesh: look behind it
            }
            return false;
        }

        // Variant geometry only: meshes in the active scene (not the blockout under it).
        private static bool Paintable(MeshFilter filter)
            => filter != null && filter.sharedMesh != null && filter.gameObject.scene == SceneManager.GetActiveScene()
               && (!LevelDesignerContext.BlockoutSceneUnderVariant.IsValid() || filter.gameObject.scene != LevelDesignerContext.BlockoutSceneUnderVariant);

        // Paints (or erases) every triangle in the brush, on every variant mesh it reaches.
        private static void PaintAt(Vector3 center, Vector3 normal)
        {
            float radius = Radius;
            var material = Erasing ? null : Selected;
            if (!Erasing && material == null)
                return;
            var brush = new Bounds(center, Vector3.one * radius * 2f);
            foreach (var filter in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (!Paintable(filter))
                    continue;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null && !renderer.bounds.Intersects(brush))
                    continue;
                var data = MeshRay.Data(filter.sharedMesh);
                var toWorld = filter.transform.localToWorldMatrix;
                var picked = new List<int>();
                for (int t = 0; t < data.Count; t++)
                {
                    var worldCenter = toWorld.MultiplyPoint3x4(data.Centers[t]);
                    if ((worldCenter - center).sqrMagnitude > radius * radius)
                        continue;
                    var worldNormal = toWorld.MultiplyVector(data.Normals[t]).normalized;
                    if (Vector3.Dot(worldNormal, normal) < 0.5f)
                        continue; // facing away: a wall's back, the other side of a thin floor
                    picked.Add(t);
                }
                if (picked.Count == 0)
                    continue;

                var paint = filter.GetComponent<SurfacePaint>();
                if (paint == null)
                {
                    if (Erasing)
                        continue;
                    paint = Undo.AddComponent<SurfacePaint>(filter.gameObject);
                }
                Undo.RecordObject(paint, Erasing ? "Erase Surfaces" : "Paint Surfaces");
                paint.Paint(picked, t => data.Corners(t), material);
                EditorUtility.SetDirty(paint);
            }
            SceneView.RepaintAll();
        }

        #endregion
    }

    /// <summary>Meshes as triangles for the brush: corners, centers and normals (mesh space), kept per mesh.</summary>
    internal static class MeshRay
    {
        public sealed class MeshData
        {
            public Vector3[] Vertices;
            public int[] Indices;
            public Vector3[] Centers, Normals;
            public int Count => Centers.Length;
            public (Vector3, Vector3, Vector3) Corners(int t) => (Vertices[Indices[t * 3]], Vertices[Indices[t * 3 + 1]], Vertices[Indices[t * 3 + 2]]);
        }

        private static readonly Dictionary<Mesh, (int stamp, MeshData data)> _cache = new();

        public static MeshData Data(Mesh mesh)
        {
            int stamp = mesh.vertexCount * 31 + (int)mesh.GetIndexCount(0);
            if (_cache.TryGetValue(mesh, out var cached) && cached.stamp == stamp)
                return cached.data;
            var vertices = mesh.vertices;
            var indices = mesh.triangles; // every submesh's triangles, in order
            int count = indices.Length / 3;
            var data = new MeshData { Vertices = vertices, Indices = indices, Centers = new Vector3[count], Normals = new Vector3[count] };
            for (int t = 0; t < count; t++)
            {
                Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
                data.Centers[t] = (a + b + c) / 3f;
                data.Normals[t] = Vector3.Cross(b - a, c - a).normalized;
            }
            _cache[mesh] = (stamp, data);
            return data;
        }

        /// <summary>The nearest triangle of a mesh a world ray hits (distance in world meters, normal in world space).</summary>
        public static bool Cast(MeshFilter filter, Vector3 origin, Vector3 direction, float maxDistance, out int triangle, out float distance, out Vector3 normal)
        {
            triangle = -1;
            distance = maxDistance;
            normal = default;
            var data = Data(filter.sharedMesh);
            var toLocal = filter.transform.worldToLocalMatrix;
            Vector3 o = toLocal.MultiplyPoint3x4(origin), d = toLocal.MultiplyVector(direction);
            for (int t = 0; t < data.Count; t++)
            {
                var (a, b, c) = data.Corners(t);
                if (Hits(o, d, a, b, c, out float hit) && hit < distance)
                {
                    distance = hit;
                    triangle = t;
                }
            }
            if (triangle < 0)
                return false;
            normal = filter.transform.TransformDirection(data.Normals[triangle]).normalized;
            if (Vector3.Dot(normal, direction) > 0f)
                normal = -normal;
            return true;
        }

        private static bool Hits(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance >= 0f;
        }
    }

    /// <summary>
    /// The Surface Paint panel in the Scene view (Variant mode): the brush on and off, a chip for each Surface Material
    /// (in its paint color) and an eraser, the brush size, whether painted surfaces are shown, and making a new material.
    /// </summary>
    [Overlay(typeof(SceneView), "Remedy.Surfaces.Paint", "Surface Paint", true)]
    public class SurfacePaintOverlay : Overlay, ITransientOverlay
    {
        public bool visible => LevelDesignerContext.Mode == LevelDesignerMode.Variant;

        private VisualElement _root, _chips;
        private Toggle _paint;

        public override VisualElement CreatePanelContent()
        {
            _root = new VisualElement { style = { width = 220 } };

            _paint = new Toggle("Paint") { tooltip = "Click and drag over the variant to paint the selected material (Esc stops)." };
            _paint.SetValueWithoutNotify(SurfacePaintTool.IsActive);
            _paint.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) SurfacePaintTool.Activate();
                else if (SurfacePaintTool.IsActive) ToolManager.RestorePreviousPersistentTool();
            });
            _root.Add(_paint);
            void SyncTool() => _paint?.SetValueWithoutNotify(SurfacePaintTool.IsActive);
            ToolManager.activeToolChanged += SyncTool;
            _root.RegisterCallback<DetachFromPanelEvent>(_ => ToolManager.activeToolChanged -= SyncTool);

            _chips = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            _root.Add(_chips);

            var radius = new Slider("Brush", 0.05f, 10f) { value = SurfacePaintTool.Radius, showInputField = true, tooltip = "Brush radius (meters)." };
            radius.RegisterValueChangedCallback(evt => SurfacePaintTool.Radius = evt.newValue);
            _root.Add(radius);

            var show = new Toggle("Show Paint") { value = SurfacePaintView.Show, tooltip = "Tint painted surfaces with their material's color." };
            show.RegisterValueChangedCallback(evt => { SurfacePaintView.Show = evt.newValue; SceneView.RepaintAll(); });
            _root.Add(show);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            buttons.Add(new Button(NewMaterial) { text = "New Material…", tooltip = "Make a Surface Material (in Assets/Content/Surfaces) and paint with it." });
            buttons.Add(new Button(() => { if (SurfacePaintTool.Selected != null) Selection.activeObject = SurfacePaintTool.Selected; }) { text = "Edit", tooltip = "Select the material to edit its color and properties." });
            _root.Add(buttons);

            BuildChips();
            EditorApplication.projectChanged += BuildChips;
            _root.RegisterCallback<DetachFromPanelEvent>(_ => EditorApplication.projectChanged -= BuildChips);
            return _root;
        }

        private void BuildChips()
        {
            if (_chips == null)
                return;
            _chips.Clear();
            var library = SurfaceLibraryEditor.Sync();
            var materials = library != null ? library.Materials : SurfaceLibraryEditor.AllMaterials();
            if (SurfacePaintTool.Selected == null && !SurfacePaintTool.Erasing && materials.Count > 0)
                SurfacePaintTool.Selected = materials[0];
            foreach (var material in materials)
            {
                if (material == null) continue;
                var m = material;
                _chips.Add(Chip(m.name, m.PaintColor, !SurfacePaintTool.Erasing && SurfacePaintTool.Selected == m, () =>
                {
                    SurfacePaintTool.Selected = m;
                    SurfacePaintTool.Erasing = false;
                    BuildChips();
                }));
            }
            _chips.Add(Chip("Erase", new Color(0.2f, 0.2f, 0.2f), SurfacePaintTool.Erasing, () =>
            {
                SurfacePaintTool.Erasing = true;
                BuildChips();
            }));
        }

        private static Button Chip(string text, Color color, bool selected, System.Action click)
        {
            var chip = new Button(click) { text = text };
            chip.style.backgroundColor = color;
            chip.style.color = color.grayscale > 0.6f ? Color.black : Color.white;
            chip.style.marginLeft = chip.style.marginRight = chip.style.marginTop = chip.style.marginBottom = 1;
            float border = selected ? 2f : 0f;
            chip.style.borderTopWidth = chip.style.borderBottomWidth = chip.style.borderLeftWidth = chip.style.borderRightWidth = border;
            chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = Color.white;
            return chip;
        }

        private void NewMaterial()
        {
            var material = SurfaceLibraryEditor.Create("New Surface Material", Color.HSVToRGB(Random.value, 0.55f, 0.85f));
            SurfaceLibraryEditor.Sync();
            SurfacePaintTool.Selected = material;
            SurfacePaintTool.Erasing = false;
            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
            BuildChips();
        }
    }

    /// <summary>
    /// Tints painted surfaces in the Scene view with their material's paint color (while painting, or with Show Paint
    /// on in Variant mode).
    /// </summary>
    [InitializeOnLoad]
    public static class SurfacePaintView
    {
        public static bool Show
        {
            get => EditorPrefs.GetBool("Remedy.SurfacePaint.Show", true);
            set => EditorPrefs.SetBool("Remedy.SurfacePaint.Show", value);
        }

        private static Material _material;
        private static readonly Dictionary<SurfacePaint, (int version, Mesh mesh)> _meshes = new();

        static SurfacePaintView()
        {
            SceneView.duringSceneGui += Draw;
            Undo.undoRedoPerformed += () =>
            {
                foreach (var paint in SurfacePaintLayer.All)
                    if (paint != null) paint.Changed();
                SceneView.RepaintAll();
            };
        }

        private static void Draw(SceneView view)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            if (!SurfacePaintTool.IsActive && !(Show && LevelDesignerContext.Mode == LevelDesignerMode.Variant))
                return;
            if (!EnsureMaterial())
                return;
            foreach (var paint in SurfacePaintLayer.All)
            {
                if (paint == null || paint.PaintedCount == 0 || !paint.isActiveAndEnabled)
                    continue;
                var mesh = MeshFor(paint);
                _material.SetPass(0);
                Graphics.DrawMeshNow(mesh, paint.transform.localToWorldMatrix);
            }
        }

        private static bool EnsureMaterial()
        {
            if (_material != null) return true;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return true;
        }

        // The painted triangles as a tinted mesh, nudged off the surface so it isn't hidden by it.
        private static Mesh MeshFor(SurfacePaint paint)
        {
            if (_meshes.TryGetValue(paint, out var cached) && cached.version == paint.Version && cached.mesh != null)
                return cached.mesh;
            var mesh = cached.mesh != null ? cached.mesh : new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.Clear();
            int count = paint.PaintedCount;
            var vertices = new Vector3[count * 3];
            var colors = new Color[count * 3];
            var indices = new int[count * 3];
            var scale = paint.transform.lossyScale;
            float nudge = 0.004f / Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            for (int i = 0; i < count; i++)
            {
                paint.Corners(i, out var a, out var b, out var c);
                var offset = Vector3.Cross(b - a, c - a).normalized * nudge;
                var color = paint.MaterialAt(i) != null ? paint.MaterialAt(i).PaintColor : Color.magenta;
                color.a = 0.5f;
                vertices[i * 3] = a + offset;
                vertices[i * 3 + 1] = b + offset;
                vertices[i * 3 + 2] = c + offset;
                colors[i * 3] = colors[i * 3 + 1] = colors[i * 3 + 2] = color;
                indices[i * 3] = i * 3;
                indices[i * 3 + 1] = i * 3 + 1;
                indices[i * 3 + 2] = i * 3 + 2;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = indices;
            _meshes[paint] = (paint.Version, mesh);
            return mesh;
        }
    }
}
