using Box3D;
using Dreamteck.Splines;
using Remedy.Box3DPhysics;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Remedy.Rivers
{
    /// <summary>
    /// A river along a Dreamteck spline: draw the spline along the water surface (point size scales the width), and
    /// this builds the water mesh (with UVs running downstream for a flowing material such as Schematics/River) and,
    /// in Play Mode, carries Box3D bodies along: buoyancy by how deep they sit, and a current that drags them
    /// downstream, fastest in the middle of the river.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [AddComponentMenu("Rivers/Spline River")]
    public class SplineRiver : MonoBehaviour
    {
        [Tooltip("The spline running along the river's surface, upstream to downstream.")]
        public SplineComputer Spline;

        [Header("Shape")]
        [Tooltip("Width of the river, multiplied by each spline point's size.")]
        public float Width = 6f;
        [Tooltip("Distance between rows of the mesh along the river.")]
        public float Resolution = 1f;
        [Tooltip("Columns across the river (more gives a smoother current profile on the surface).")]
        [Range(1, 16)] public int CrossSegments = 4;
        [Tooltip("Raises the surface above the spline.")]
        public float SurfaceOffset = 0f;
        [Tooltip("Metres of river per texture repeat along it.")]
        public float UVLength = 4f;

        [Header("Current")]
        [Tooltip("Speed of the water in the middle of the river, in m/s.")]
        public float FlowSpeed = 3f;
        [Tooltip("How much slower the water is at the banks (0 = same speed everywhere, 1 = still at the banks).")]
        [Range(0f, 1f)] public float BankSlowdown = 0.6f;

        [Header("Physics")]
        [Tooltip("How deep the water is below the surface; bodies lower than this aren't in it.")]
        public float Depth = 4f;
        [Tooltip("Upward push when fully under, as a multiple of the body's weight (above 1 floats).")]
        public float Buoyancy = 1.4f;
        [Tooltip("How strongly the water pulls bodies to its own velocity (current and stillness alike).")]
        public float Drag = 1.5f;

        private Mesh _mesh;
        private bool _dirty = true;
        private SplineComputer _subscribed;
        private Bounds _worldBounds;

        // Rebuild when anything about the river changes.
        private void OnValidate() => _dirty = true;

        private void OnEnable()
        {
            _dirty = true;
            Subscribe();
        }

        private void OnDisable()
        {
            if (_subscribed != null)
                _subscribed.onRebuild -= MarkDirty;
            _subscribed = null;
        }

        private void MarkDirty() => _dirty = true;

        private void Subscribe()
        {
            if (_subscribed == Spline)
                return;
            if (_subscribed != null)
                _subscribed.onRebuild -= MarkDirty;
            _subscribed = Spline;
            if (_subscribed != null)
                _subscribed.onRebuild += MarkDirty;
        }

        private void Update()
        {
            Subscribe();
            if (_dirty)
            {
                _dirty = false;
                BuildMesh();
            }
        }

        #region Mesh

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector4> _tangents = new List<Vector4>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _flow = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>Rebuilds the water surface from the spline.</summary>
        public void BuildMesh()
        {
            if (Spline == null)
                return;
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "River" };
                _mesh.MarkDynamic();
                _mesh.hideFlags = HideFlags.DontSave;
            }

            _vertices.Clear(); _normals.Clear(); _tangents.Clear(); _uvs.Clear(); _flow.Clear(); _triangles.Clear();

            float length = Spline.CalculateLength();
            float step = Mathf.Max(0.05f, Resolution);
            int rows = Mathf.Max(2, Mathf.CeilToInt(length / step) + 1);
            int columns = Mathf.Max(1, CrossSegments);
            var bounds = new Bounds();
            bool first = true;

            double percent = 0.0;
            for (int row = 0; row < rows; row++)
            {
                float distance = Mathf.Min(row * step, length);
                if (row > 0)
                    percent = Spline.Travel(percent, distance - (row - 1) * step);
                if (row == rows - 1)
                    percent = 1.0;
                var sample = Spline.Evaluate(percent);

                Vector3 up = sample.up;
                Vector3 right = sample.right;
                float halfWidth = Width * 0.5f * Mathf.Max(0.01f, sample.size);
                Vector3 center = sample.position + up * SurfaceOffset;

                for (int c = 0; c <= columns; c++)
                {
                    float across = (float)c / columns;            // 0 at the left bank, 1 at the right
                    Vector3 world = center + right * ((across * 2f - 1f) * halfWidth);
                    if (first) { bounds = new Bounds(world, Vector3.zero); first = false; }
                    else bounds.Encapsulate(world);

                    _vertices.Add(transform.InverseTransformPoint(world));
                    _normals.Add(transform.InverseTransformDirection(up));
                    Vector3 tangent = transform.InverseTransformDirection(right);
                    _tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
                    _uvs.Add(new Vector2(across, distance / Mathf.Max(0.01f, UVLength)));
                    // Flow speed at this vertex (UV2.x), for the material's scrolling.
                    _flow.Add(new Vector2(CurrentAt(across * 2f - 1f) / Mathf.Max(0.01f, UVLength), 0f));
                }
            }

            int stride = columns + 1;
            for (int row = 0; row < rows - 1; row++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int a = row * stride + c, b = a + 1, d = a + stride, e = d + 1;
                    _triangles.Add(a); _triangles.Add(d); _triangles.Add(b);
                    _triangles.Add(b); _triangles.Add(d); _triangles.Add(e);
                }
            }

            _mesh.Clear();
            _mesh.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.SetTangents(_tangents);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _flow);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            bounds.Expand(new Vector3(0f, 2f, 0f));
            bounds.min -= new Vector3(0f, Depth, 0f);
            _worldBounds = bounds;
        }

        /// <summary>The current's speed across the river: <paramref name="lateral"/> is -1 at the left bank, 0 in the middle, 1 at the right.</summary>
        public float CurrentAt(float lateral) => FlowSpeed * (1f - BankSlowdown * lateral * lateral);

        #endregion

        #region Water queries

        /// <summary>
        /// The water at a world position: whether it's within the river's banks, the surface height above it, and the
        /// water's velocity there.
        /// </summary>
        public bool Sample(Vector3 position, out float surfaceHeight, out Vector3 waterVelocity)
        {
            surfaceHeight = 0f;
            waterVelocity = Vector3.zero;
            if (Spline == null)
                return false;

            var sample = new SplineSample();
            Spline.Project(ref sample, position);
            float halfWidth = Width * 0.5f * Mathf.Max(0.01f, sample.size);
            Vector3 center = sample.position + sample.up * SurfaceOffset;
            float lateral = Vector3.Dot(position - center, sample.right) / halfWidth;
            if (Mathf.Abs(lateral) > 1f)
                return false;

            surfaceHeight = center.y;
            Vector3 downstream = sample.forward;
            waterVelocity = downstream * CurrentAt(lateral);
            return true;
        }

        #endregion

        #region Physics

        private const int MaxShapes = 256;
        private readonly HashSet<ulong> _visited = new HashSet<ulong>();

        private void FixedUpdate()
        {
            if (!Application.isPlaying || Spline == null)
                return;
            var world = SchematicPhysics.World;
            if (!world.IsValid)
                return;

            var aabb = new B3Aabb { LowerBound = _worldBounds.min, UpperBound = _worldBounds.max };
            Span<ShapeId> shapes = stackalloc ShapeId[MaxShapes];
            int count = world.OverlapAABB(aabb, QueryFilter.Default, shapes);
            Vector3 gravity = SchematicPhysics.Gravity;
            float dt = Time.fixedDeltaTime;
            _visited.Clear();

            for (int i = 0; i < count; i++)
            {
                var shape = new Shape { Id = shapes[i] };
                if (!shape.IsValid)
                    continue;
                var body = shape.GetBody();
                if (!body.IsValid || body.Type != BodyType.Dynamic || !_visited.Add(body.Id.ToUInt64()))
                    continue;

                // The body's extent from its shapes found here (its first shape is a fair estimate for most props).
                var bodyBox = shape.GetAABB();
                Vector3 lower = bodyBox.LowerBound, upper = bodyBox.UpperBound;
                Vector3 center = (Vector3)(float3)body.GetWorldCenterOfMass();

                if (!Sample(center, out float surface, out Vector3 waterVelocity))
                    continue;
                if (lower.y > surface || upper.y < surface - Depth)
                    continue;

                float height = Mathf.Max(0.01f, upper.y - lower.y);
                float submerged = Mathf.Clamp01((surface - lower.y) / height);
                float mass = body.GetMass();

                Vector3 buoyancy = -gravity * (mass * Buoyancy * submerged);
                Vector3 velocity = body.LinearVelocity;
                Vector3 pull = (waterVelocity - velocity) * (mass * Drag * submerged);
                // Vertical drag only, so the current doesn't fight buoyancy.
                pull.y = -velocity.y * mass * Drag * 0.5f * submerged;

                body.ApplyForceToCenter((float3)(buoyancy + pull), true);
            }
        }

        #endregion

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.4f);
            Gizmos.DrawWireCube(_worldBounds.center, _worldBounds.size);
        }
    }
}
