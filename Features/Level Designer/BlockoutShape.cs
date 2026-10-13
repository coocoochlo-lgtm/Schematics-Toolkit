using System;
using System.Collections.Generic;
using System.Globalization;
using Box3D.Hybrid;
using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>What a blockout shape stands for in the level; picks its colour so the layout reads at a glance.</summary>
    public enum BlockoutCategory
    {
        Floor,
        Wall,
        Platform,
        Hazard,
        Interactive,
        Water,
        Decoration,
        Custom,
    }

    /// <summary>
    /// A grey-box building block: a box, ramp, stairs, cylinder, sphere, wedge, arch or quarter pipe whose size is set
    /// in meters (not by scaling), so its measurements are always exact. The blockout shader prints the size on every
    /// face, draws a 1 m grid with 0.25 m subdivisions, and colours the shape by its <see cref="Category"/>.<br/>
    /// The mesh is generated (and saved in the scene); collision is a static Box3D mesh shape so characters driven by
    /// the toolkit's physics can run on the blockout straight away.
    /// </summary>
    [ExecuteAlways]
    [SelectionBase]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Level Designer/Blockout Shape")]
    public class BlockoutShape : MonoBehaviour
    {
        /// <summary>The shader the blockout material uses (also looked up when no material was assigned).</summary>
        public const string ShaderName = "Remedy/Level Designer/Blockout";

        public BlockoutShapeKind Kind = BlockoutShapeKind.Box;

        /// <summary>Width (x), height (y) and length (z) in meters. The pivot is the centre of the bottom face.</summary>
        public Vector3 Size = new Vector3(2f, 1f, 2f);

        public BlockoutCategory Category = BlockoutCategory.Floor;

        /// <summary>The colour used when <see cref="Category"/> is Custom.</summary>
        public Color CustomColor = new Color(0.6f, 0.6f, 0.65f);

        /// <summary>Stairs: the height of one step (the count is rounded to fit the total height).</summary>
        [Min(0.05f)] public float StepHeight = 0.25f;

        /// <summary>Round shapes: how many sides they're made of.</summary>
        [Range(6, 64)] public int Segments = 24;

        /// <summary>Arch: how much of the width the tunnel takes up.</summary>
        [Range(0.1f, 0.95f)] public float ArchOpening = 0.6f;

        /// <summary>Prints the size on the faces.</summary>
        public bool ShowDimensions = true;

        /// <summary>Gives the shape static Box3D collision (a mesh shape on a static body).</summary>
        public bool Collision = true;

        [SerializeField] private Material _material;
        [SerializeField, HideInInspector] private Mesh _mesh;
        // The mesh last handed to the Box3D mesh shape, so reloading a scene doesn't touch (and dirty) it again.
        [SerializeField, HideInInspector] private Mesh _collisionMesh;

        /// <summary>
        /// Set by an <see cref="ArtDesignScene"/> that brings its blockout into Play Mode for collision only: shapes
        /// that wake while it's set switch their renderers off.
        /// </summary>
        public static bool HideRenderersInPlayMode;

        /// <summary>Raised when any shape is rebuilt (the editor tools refresh their caches).</summary>
        public static event Action<BlockoutShape> Rebuilt;

        /// <summary>Every enabled shape, in every loaded scene.</summary>
        public static IReadOnlyList<BlockoutShape> All => _all;
        private static readonly List<BlockoutShape> _all = new();

        // Which shape owns each mesh: a duplicated object starts out sharing its original's mesh, and must get its own.
        private static readonly Dictionary<Mesh, BlockoutShape> _meshOwners = new();

        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int LabelLengthId = Shader.PropertyToID("_LabelLength");
        private static readonly int[] LabelIds =
        {
            Shader.PropertyToID("_Label0"), Shader.PropertyToID("_Label1"), Shader.PropertyToID("_Label2"),
            Shader.PropertyToID("_Label3"), Shader.PropertyToID("_Label4"),
        };

        private MaterialPropertyBlock _block;
        private Vector3 _appliedScale;

        /// <summary>The generated mesh (built on demand).</summary>
        public Mesh Mesh
        {
            get
            {
                if (_mesh == null)
                    Rebuild();
                return _mesh;
            }
        }

        /// <summary>The material shapes render with: assigned by the editor tools, or the shared default.</summary>
        public Material Material
        {
            get => _material;
            set
            {
                _material = value;
                ApplyRendering();
            }
        }

        /// <summary>The shape's size in the world (its <see cref="Size"/> times the object's scale).</summary>
        public Vector3 WorldSize
        {
            get
            {
                var scale = transform.lossyScale;
                return new Vector3(Size.x * Mathf.Abs(scale.x), Size.y * Mathf.Abs(scale.y), Size.z * Mathf.Abs(scale.z));
            }
        }

        /// <summary>The colour of a category (Custom returns grey; use <see cref="Color"/> for the shape's own).</summary>
        public static Color ColorOf(BlockoutCategory category) => category switch
        {
            BlockoutCategory.Floor => new Color(0.55f, 0.58f, 0.62f),
            BlockoutCategory.Wall => new Color(0.72f, 0.68f, 0.62f),
            BlockoutCategory.Platform => new Color(0.95f, 0.62f, 0.25f),
            BlockoutCategory.Hazard => new Color(0.9f, 0.25f, 0.22f),
            BlockoutCategory.Interactive => new Color(0.35f, 0.8f, 0.4f),
            BlockoutCategory.Water => new Color(0.25f, 0.55f, 0.95f),
            BlockoutCategory.Decoration => new Color(0.7f, 0.5f, 0.85f),
            _ => new Color(0.6f, 0.6f, 0.65f),
        };

        /// <summary>The colour this shape renders with.</summary>
        public Color Color => Category == BlockoutCategory.Custom ? CustomColor : ColorOf(Category);

        private void OnEnable()
        {
            if (!_all.Contains(this))
                _all.Add(this);

            if (_mesh != null && SharedWithAnother())
                _mesh = null; // a copy of another shape: build our own

            if (_mesh == null)
                Rebuild();
            else
            {
                _meshOwners[_mesh] = this;
                ApplyRendering();
            }

            if (Application.isPlaying && HideRenderersInPlayMode && TryGetComponent<MeshRenderer>(out var meshRenderer))
                meshRenderer.enabled = false;
        }

        // True when another live shape already owns this shape's mesh.
        private bool SharedWithAnother()
        {
            return _meshOwners.TryGetValue(_mesh, out var owner) && owner != null && owner != this && owner._mesh == _mesh;
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

        private void OnDestroy()
        {
            if (_mesh != null && _meshOwners.TryGetValue(_mesh, out var owner) && owner == this)
                _meshOwners.Remove(_mesh);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Size = new Vector3(Mathf.Max(0.01f, Size.x), Mathf.Max(0.01f, Size.y), Mathf.Max(0.01f, Size.z));
            if (!isActiveAndEnabled)
                return;
            // Mesh data can change here; adding or removing components has to wait until Unity is done validating.
            RebuildMesh();
            ApplyRendering();
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                    SyncCollision();
            };
        }

        private void Update()
        {
            // The label and grid follow the scale, which isn't part of Size.
            if (!Application.isPlaying && transform.lossyScale != _appliedScale)
                ApplyRendering();
        }
#endif

        /// <summary>Regenerates the mesh, the collision and the shader values after Size or Kind changed.</summary>
        public void Rebuild()
        {
            RebuildMesh();
            ApplyRendering();
            SyncCollision();
            Rebuilt?.Invoke(this);
        }

        private void RebuildMesh()
        {
            if (_mesh != null && SharedWithAnother())
                _mesh = null; // a copy of another shape (Duplicate, copy-paste): build our own
            if (_mesh == null)
                _mesh = new Mesh { name = "Blockout " + Kind };
            _meshOwners[_mesh] = this;
            _mesh.name = "Blockout " + Kind;
            BlockoutMeshBuilder.Build(_mesh, Kind, Size, StepHeight, Segments, ArchOpening);

            var filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != _mesh)
                filter.sharedMesh = _mesh;
        }

        /// <summary>Re-applies the colour, size and label to the renderer (property blocks aren't saved).</summary>
        public void ApplyRendering()
        {
            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                return;

            if (_material == null)
                _material = DefaultMaterial;
            if (_material != null && meshRenderer.sharedMaterial != _material)
                meshRenderer.sharedMaterial = _material;

            _block ??= new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(_block);
            Vector3 worldSize = WorldSize;
            _block.SetVector(SizeId, worldSize);
            _block.SetColor(BaseColorId, Color);

            var glyphs = ShowDimensions ? EncodeLabel(worldSize) : Array.Empty<float>();
            _block.SetFloat(LabelLengthId, glyphs.Length);
            for (int i = 0; i < LabelIds.Length; i++)
            {
                _block.SetVector(LabelIds[i], new Vector4(
                    GlyphAt(glyphs, i * 4), GlyphAt(glyphs, i * 4 + 1), GlyphAt(glyphs, i * 4 + 2), GlyphAt(glyphs, i * 4 + 3)));
            }
            meshRenderer.SetPropertyBlock(_block);
            _appliedScale = transform.lossyScale;
        }

        private static float GlyphAt(float[] glyphs, int index) => index < glyphs.Length ? glyphs[index] : GlyphSpace;

        private void SyncCollision()
        {
            if (Application.isPlaying)
                return;

            var shape = GetComponent<Box3DMeshShape>();
            if (Collision)
            {
                // Our own static body, unless the shape is part of a bigger body (a compound) further up.
                var body = GetComponent<Box3DBody>();
                if (body == null && GetComponentInParent<Box3DBody>() == null)
                {
                    body = gameObject.AddComponent<Box3DBody>();
                    body.BodyType = Box3DBodyType.Static;
                    MarkDirty(body);
                }
                if (shape == null)
                {
                    shape = gameObject.AddComponent<Box3DMeshShape>();
                    _collisionMesh = null;
                }
                if (_collisionMesh != _mesh)
                {
                    shape.SetMesh(_mesh);
                    _collisionMesh = _mesh;
                    MarkDirty(shape);
                    MarkDirty(this);
                }
            }
            else if (shape != null)
            {
                _collisionMesh = null;
                DestroyImmediate(shape);
                var body = GetComponent<Box3DBody>();
                if (body != null && body.BodyType == Box3DBodyType.Static && GetComponents<Box3DShape>().Length == 0)
                    DestroyImmediate(body);
            }
        }

        private static void MarkDirty(UnityEngine.Object target)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(target);
#endif
        }

        private static Material _defaultMaterial;

        /// <summary>
        /// A material on the blockout shader, used when a shape has none assigned. The editor tools assign a saved
        /// material asset instead, so builds include the shader.
        /// </summary>
        public static Material DefaultMaterial
        {
            get
            {
                if (_defaultMaterial == null)
                {
                    var shader = Shader.Find(ShaderName);
                    if (shader != null)
                        _defaultMaterial = new Material(shader) { name = "Blockout (Default)", hideFlags = HideFlags.DontSave };
                }
                return _defaultMaterial;
            }
        }

        // ---------------------------------------------------------------- label encoding

        /// <summary>Glyph codes the shader draws: 0-9 are digits, then these.</summary>
        public const float GlyphDot = 10f, GlyphTimes = 11f, GlyphMeters = 12f, GlyphSpace = 13f, GlyphMinus = 14f;

        /// <summary>The longest label the shader can show (5 vectors of 4 glyphs).</summary>
        public const int MaxGlyphs = 20;

        /// <summary>Formats a length in meters the way labels show it: up to two decimals, no trailing zeros.</summary>
        public static string FormatMeters(float meters)
        {
            return Math.Round(meters, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Turns a size into the glyph codes for "W x H x L m".</summary>
        public static float[] EncodeLabel(Vector3 size)
        {
            string text = FormatMeters(size.x) + "x" + FormatMeters(size.y) + "x" + FormatMeters(size.z) + "m";
            var glyphs = new List<float>(text.Length);
            foreach (char c in text)
            {
                if (glyphs.Count >= MaxGlyphs)
                    break;
                if (c >= '0' && c <= '9') glyphs.Add(c - '0');
                else if (c == '.') glyphs.Add(GlyphDot);
                else if (c == 'x') glyphs.Add(GlyphTimes);
                else if (c == 'm') glyphs.Add(GlyphMeters);
                else if (c == '-') glyphs.Add(GlyphMinus);
                else glyphs.Add(GlyphSpace);
            }
            return glyphs.ToArray();
        }

        // ---------------------------------------------------------------- queries

        /// <summary>
        /// Casts a ray against this shape's mesh (exact triangles, no physics needed, works in Edit Mode).
        /// </summary>
        public bool Raycast(Ray ray, out float distance, out Vector3 normal)
        {
            distance = float.MaxValue;
            normal = Vector3.up;
            var mesh = _mesh;
            if (mesh == null)
                return false;

            var toLocal = transform.worldToLocalMatrix;
            var localOrigin = toLocal.MultiplyPoint3x4(ray.origin);
            var localDir = toLocal.MultiplyVector(ray.direction);
            // Quick reject against the bounds.
            var bounds = mesh.bounds;
            bounds.Expand(0.01f);
            if (!bounds.IntersectRay(new Ray(localOrigin, localDir)))
                return false;

            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            bool hit = false;
            float best = float.MaxValue;
            Vector3 bestNormal = Vector3.up;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                if (IntersectTriangle(localOrigin, localDir, a, b, c, out float t) && t < best)
                {
                    best = t;
                    bestNormal = Vector3.Cross(b - a, c - a);
                    hit = true;
                }
            }
            if (!hit)
                return false;

            var localHit = localOrigin + localDir * best;
            var worldHit = transform.localToWorldMatrix.MultiplyPoint3x4(localHit);
            distance = Vector3.Distance(ray.origin, worldHit);
            normal = transform.localToWorldMatrix.inverse.transpose.MultiplyVector(bestNormal).normalized;
            if (Vector3.Dot(normal, ray.direction) > 0f)
                normal = -normal;
            return true;
        }

        /// <summary>Casts a ray against every enabled shape; the nearest hit wins.</summary>
        public static bool RaycastAll(Ray ray, float maxDistance, out RaycastResult result, BlockoutShape ignore = null)
        {
            result = default;
            result.Distance = maxDistance;
            bool hit = false;
            foreach (var shape in _all)
            {
                if (shape == null || shape == ignore || !shape.isActiveAndEnabled)
                    continue;
                if (shape.Raycast(ray, out float distance, out Vector3 normal) && distance < result.Distance)
                {
                    result = new RaycastResult { Shape = shape, Distance = distance, Point = ray.GetPoint(distance), Normal = normal };
                    hit = true;
                }
            }
            return hit;
        }

        /// <summary>A hit from <see cref="RaycastAll"/>.</summary>
        public struct RaycastResult
        {
            public BlockoutShape Shape;
            public float Distance;
            public Vector3 Point;
            public Vector3 Normal;
        }

        // Möller–Trumbore, front and back faces.
        private static bool IntersectTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-8f)
                return false;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f)
                return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f)
                return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 1e-5f;
        }
    }
}
