using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Surfaces
{
    /// <summary>
    /// Surface Materials painted onto a mesh, triangle by triangle (the Level Designer's paint brush, in Variant mode,
    /// adds and fills it). Only painted triangles are kept, in the mesh's own space with their corners, so they make
    /// up the Surfaces layer: casts for materials hit painted surfaces only, follow the object if it moves, and don't
    /// need the mesh to be readable in builds.
    /// </summary>
    [AddComponentMenu("Remedy/Surfaces/Surface Paint")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class SurfacePaint : MonoBehaviour
    {
        [Tooltip("The materials painted here (each painted triangle says which).")]
        [SerializeField] private List<SurfaceMaterial> _palette = new();
        // Per painted triangle: the mesh triangle it is, its material (index into the palette) and its three corners.
        [SerializeField, HideInInspector] private int[] _triangles = Array.Empty<int>();
        [SerializeField, HideInInspector] private byte[] _materials = Array.Empty<byte>();
        [SerializeField, HideInInspector] private Vector3[] _corners = Array.Empty<Vector3>();

        [NonSerialized] private Dictionary<int, int> _slotOfTriangle;
        [NonSerialized] private Bvh _bvh;

        /// <summary>The materials painted here.</summary>
        public IReadOnlyList<SurfaceMaterial> Palette => _palette;

        /// <summary>How many triangles are painted.</summary>
        public int PaintedCount => _triangles.Length;

        /// <summary>The mesh triangle (index) of a painted triangle, by its position in the painted list.</summary>
        public int TriangleAt(int slot) => _triangles[slot];

        /// <summary>The material of a painted triangle, by its position in the painted list.</summary>
        public SurfaceMaterial MaterialAt(int slot)
        {
            int index = _materials[slot];
            return index < _palette.Count ? _palette[index] : null;
        }

        /// <summary>The material a mesh triangle is painted with (null when it isn't painted).</summary>
        public SurfaceMaterial MaterialOfTriangle(int triangle)
            => Slots.TryGetValue(triangle, out int slot) ? MaterialAt(slot) : null;

        /// <summary>The corners of a painted triangle (mesh space), by its position in the painted list.</summary>
        public void Corners(int slot, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = _corners[slot * 3];
            b = _corners[slot * 3 + 1];
            c = _corners[slot * 3 + 2];
        }

        private Dictionary<int, int> Slots
        {
            get
            {
                if (_slotOfTriangle == null)
                {
                    _slotOfTriangle = new Dictionary<int, int>(_triangles.Length);
                    for (int i = 0; i < _triangles.Length; i++)
                        _slotOfTriangle[_triangles[i]] = i;
                }
                return _slotOfTriangle;
            }
        }

        /// <summary>
        /// Paints (or, with a null material, clears) mesh triangles; <paramref name="corners"/> gives each one's three
        /// corners in mesh space. For the editor's brush (record Undo on this first).
        /// </summary>
        public void Paint(IReadOnlyList<int> triangles, Func<int, (Vector3, Vector3, Vector3)> corners, SurfaceMaterial material)
        {
            var slots = Slots;
            var tris = new List<int>(_triangles);
            var mats = new List<byte>(_materials);
            var points = new List<Vector3>(_corners);
            byte index = 0;
            if (material != null)
            {
                int found = _palette.IndexOf(material);
                if (found < 0)
                {
                    if (_palette.Count >= 255)
                    {
                        Debug.LogWarning($"{name}: a surface can hold at most 255 different materials.", this);
                        return;
                    }
                    _palette.Add(material);
                    found = _palette.Count - 1;
                }
                index = (byte)found;
            }

            var removed = new HashSet<int>();
            foreach (int triangle in triangles)
            {
                if (slots.TryGetValue(triangle, out int slot))
                {
                    if (material == null) removed.Add(slot);
                    else mats[slot] = index;
                }
                else if (material != null)
                {
                    var (a, b, c) = corners(triangle);
                    slots[triangle] = tris.Count;
                    tris.Add(triangle);
                    mats.Add(index);
                    points.Add(a); points.Add(b); points.Add(c);
                }
            }

            if (removed.Count > 0)
            {
                var keepTris = new List<int>(tris.Count);
                var keepMats = new List<byte>(tris.Count);
                var keepPoints = new List<Vector3>(points.Count);
                for (int i = 0; i < tris.Count; i++)
                {
                    if (removed.Contains(i)) continue;
                    keepTris.Add(tris[i]);
                    keepMats.Add(mats[i]);
                    keepPoints.Add(points[i * 3]); keepPoints.Add(points[i * 3 + 1]); keepPoints.Add(points[i * 3 + 2]);
                }
                tris = keepTris; mats = keepMats; points = keepPoints;
            }

            _triangles = tris.ToArray();
            _materials = mats.ToArray();
            _corners = points.ToArray();
            DropUnusedPalette();
            Changed();
        }

        /// <summary>Clears all paint.</summary>
        public void Clear()
        {
            _triangles = Array.Empty<int>();
            _materials = Array.Empty<byte>();
            _corners = Array.Empty<Vector3>();
            _palette.Clear();
            Changed();
        }

        /// <summary>Goes up whenever the paint changes (for views that draw it).</summary>
        [NonSerialized] public int Version;

        /// <summary>Forgets cached lookups (after an Undo, or the data changed some other way).</summary>
        public void Changed()
        {
            _slotOfTriangle = null;
            _bvh = null;
            Version++;
        }

        // Palette entries no triangle uses any more are removed (and the indices moved down).
        private void DropUnusedPalette()
        {
            var used = new bool[_palette.Count];
            foreach (byte m in _materials)
                if (m < used.Length) used[m] = true;
            var remap = new byte[_palette.Count];
            var palette = new List<SurfaceMaterial>();
            for (int i = 0; i < _palette.Count; i++)
            {
                if (!used[i]) continue;
                remap[i] = (byte)palette.Count;
                palette.Add(_palette[i]);
            }
            if (palette.Count == _palette.Count)
                return;
            for (int i = 0; i < _materials.Length; i++)
                _materials[i] = remap[_materials[i]];
            _palette = palette;
        }

        private void OnEnable()
        {
            SurfacePaintLayer.Register(this);
            Changed();
        }

        private void OnDisable() => SurfacePaintLayer.Unregister(this);

        private void OnValidate() => Changed(); // Undo, or edited in the Inspector

        /// <summary>
        /// The nearest painted triangle a world-space ray hits within <paramref name="maxDistance"/>: its position in
        /// the painted list, and the hit's distance, point and normal (world space).
        /// </summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out int slot, out float distance, out Vector3 point, out Vector3 normal)
        {
            slot = -1;
            distance = maxDistance;
            point = normal = default;
            if (_triangles.Length == 0)
                return false;
            _bvh ??= new Bvh(_corners);

            // Into mesh space: an affine map keeps the ray's parameter in world meters.
            var toLocal = transform.worldToLocalMatrix;
            Vector3 o = toLocal.MultiplyPoint3x4(origin);
            Vector3 d = toLocal.MultiplyVector(direction);
            if (!_bvh.Raycast(_corners, o, d, maxDistance, out slot, out distance))
                return false;
            point = origin + direction * distance;
            Corners(slot, out var a, out var b, out var c);
            normal = transform.TransformDirection(Vector3.Cross(b - a, c - a)).normalized;
            if (Vector3.Dot(normal, direction) > 0f)
                normal = -normal; // facing the ray
            return true;
        }

        /// <summary>The painted triangles' bounds in world space (for skipping objects a ray can't reach).</summary>
        public Bounds WorldBounds
        {
            get
            {
                _bvh ??= new Bvh(_corners);
                var local = _bvh.Bounds;
                var matrix = transform.localToWorldMatrix;
                var center = matrix.MultiplyPoint3x4(local.center);
                var e = local.extents;
                var x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
                var y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
                var z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
                var extents = new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                return new Bounds(center, extents * 2f);
            }
        }

        /// <summary>
        /// A bounding volume tree over triangles (median split, a few triangles a leaf), so a ray only tests the few
        /// triangles near it.
        /// </summary>
        private sealed class Bvh
        {
            private struct Node
            {
                public Vector3 Min, Max;
                public int Left, Right;   // children (-1 in a leaf)
                public int Start, Count;  // the leaf's triangles in _order
            }

            private const int LeafSize = 4;
            private readonly List<Node> _nodes = new();
            private readonly int[] _order;
            public readonly Bounds Bounds;

            public Bvh(Vector3[] corners)
            {
                int count = corners.Length / 3;
                _order = new int[count];
                var centers = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    _order[i] = i;
                    centers[i] = (corners[i * 3] + corners[i * 3 + 1] + corners[i * 3 + 2]) / 3f;
                }
                if (count > 0)
                    Build(corners, centers, 0, count);
                Bounds = count > 0 ? new Bounds((_nodes[0].Min + _nodes[0].Max) * 0.5f, _nodes[0].Max - _nodes[0].Min) : default;
            }

            private int Build(Vector3[] corners, Vector3[] centers, int start, int count)
            {
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = -min;
                for (int i = start; i < start + count; i++)
                {
                    int t = _order[i];
                    for (int k = 0; k < 3; k++)
                    {
                        min = Vector3.Min(min, corners[t * 3 + k]);
                        max = Vector3.Max(max, corners[t * 3 + k]);
                    }
                }
                int index = _nodes.Count;
                _nodes.Add(new Node { Min = min, Max = max, Left = -1, Right = -1, Start = start, Count = count });
                if (count <= LeafSize)
                    return index;

                var size = max - min;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                Array.Sort(_order, start, count, Comparer<int>.Create((p, q) => centers[p][axis].CompareTo(centers[q][axis])));
                int half = count / 2;
                int left = Build(corners, centers, start, half);
                int right = Build(corners, centers, start + half, count - half);
                var node = _nodes[index];
                node.Left = left;
                node.Right = right;
                node.Count = 0;
                _nodes[index] = node;
                return index;
            }

            public bool Raycast(Vector3[] corners, Vector3 origin, Vector3 direction, float maxDistance, out int slot, out float distance)
            {
                slot = -1;
                distance = maxDistance;
                if (_nodes.Count == 0)
                    return false;
                var inverse = new Vector3(1f / direction.x, 1f / direction.y, 1f / direction.z);
                var stack = new Stack<int>();
                stack.Push(0);
                while (stack.Count > 0)
                {
                    var node = _nodes[stack.Pop()];
                    if (!HitsBox(node.Min, node.Max, origin, inverse, distance))
                        continue;
                    if (node.Left < 0)
                    {
                        for (int i = node.Start; i < node.Start + node.Count; i++)
                        {
                            int t = _order[i];
                            if (HitsTriangle(origin, direction, corners[t * 3], corners[t * 3 + 1], corners[t * 3 + 2], out float d) && d < distance)
                            {
                                distance = d;
                                slot = t;
                            }
                        }
                    }
                    else
                    {
                        stack.Push(node.Left);
                        stack.Push(node.Right);
                    }
                }
                return slot >= 0;
            }

            private static bool HitsBox(Vector3 min, Vector3 max, Vector3 origin, Vector3 inverse, float maxDistance)
            {
                float t1 = (min.x - origin.x) * inverse.x, t2 = (max.x - origin.x) * inverse.x;
                float near = Mathf.Min(t1, t2), far = Mathf.Max(t1, t2);
                t1 = (min.y - origin.y) * inverse.y; t2 = (max.y - origin.y) * inverse.y;
                near = Mathf.Max(near, Mathf.Min(t1, t2)); far = Mathf.Min(far, Mathf.Max(t1, t2));
                t1 = (min.z - origin.z) * inverse.z; t2 = (max.z - origin.z) * inverse.z;
                near = Mathf.Max(near, Mathf.Min(t1, t2)); far = Mathf.Min(far, Mathf.Max(t1, t2));
                // NaN (a ray along a face of the box) counts as a hit: the triangles decide.
                return !(far < Mathf.Max(near, 0f)) && !(near > maxDistance);
            }

            // Möller–Trumbore, both sides.
            private static bool HitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
            {
                distance = 0f;
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 p = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-12f)
                    return false;
                float inv = 1f / det;
                Vector3 s = origin - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f)
                    return false;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(direction, q) * inv;
                if (v < 0f || u + v > 1f)
                    return false;
                distance = Vector3.Dot(e2, q) * inv;
                return distance >= 0f;
            }
        }
    }

    /// <summary>Every enabled <see cref="SurfacePaint"/>: the Surfaces layer that material casts hit.</summary>
    public static class SurfacePaintLayer
    {
        private static readonly List<SurfacePaint> _paints = new();

        public static IReadOnlyList<SurfacePaint> All => _paints;

        internal static void Register(SurfacePaint paint)
        {
            if (!_paints.Contains(paint))
                _paints.Add(paint);
        }

        internal static void Unregister(SurfacePaint paint) => _paints.Remove(paint);

        /// <summary>The nearest painted surface a ray hits (direction should be normalized; distance in meters).</summary>
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out SurfaceHit hit)
        {
            hit = default;
            float best = maxDistance;
            bool found = false;
            var ray = new Ray(origin, direction);
            for (int i = _paints.Count - 1; i >= 0; i--)
            {
                var paint = _paints[i];
                if (paint == null)
                {
                    _paints.RemoveAt(i);
                    continue;
                }
                if (paint.PaintedCount == 0)
                    continue;
                var bounds = paint.WorldBounds;
                bounds.Expand(0.01f);
                if (!bounds.Contains(origin) && (!bounds.IntersectRay(ray, out float enter) || enter > best))
                    continue;
                if (paint.Raycast(origin, direction, best, out int slot, out float distance, out var point, out var normal) && distance < best)
                {
                    best = distance;
                    found = true;
                    hit = new SurfaceHit
                    {
                        Material = paint.MaterialAt(slot),
                        Point = point,
                        Normal = normal,
                        Distance = distance,
                        Object = paint.gameObject,
                        Painted = true,
                        Triangle = paint.TriangleAt(slot),
                    };
                }
            }
            return found;
        }
    }
}
