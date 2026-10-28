using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// A mesh's triangles sorted into nested boxes, so a ray only tests the few triangles near its path: what ambient
    /// occlusion and thickness bakes cast thousands of rays through, and what a brush stroke hits. Any thread.
    /// </summary>
    public sealed class MeshBvh
    {
        private struct BoxNode
        {
            public Vector3 Min, Max;
            // A leaf's triangles (First, Count), or (Count 0) its two children: Left, and the one right after it.
            public int First, Count, Left;
        }

        private readonly Vector3[] _a, _b, _c;
        private readonly int[] _order;
        private readonly List<BoxNode> _nodes = new();

        public MeshBvh(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles)
        {
            int count = triangles.Count / 3;
            _a = new Vector3[count];
            _b = new Vector3[count];
            _c = new Vector3[count];
            _order = new int[count];
            var centres = new Vector3[count];
            for (int t = 0; t < count; t++)
            {
                _a[t] = positions[triangles[t * 3]];
                _b[t] = positions[triangles[t * 3 + 1]];
                _c[t] = positions[triangles[t * 3 + 2]];
                centres[t] = (_a[t] + _b[t] + _c[t]) / 3f;
                _order[t] = t;
            }
            if (count > 0)
                Build(0, count, centres);
        }

        public int TriangleCount => _a.Length;

        private int Build(int first, int count, Vector3[] centres)
        {
            var node = new BoxNode { Min = Vector3.positiveInfinity, Max = Vector3.negativeInfinity };
            var centreMin = Vector3.positiveInfinity;
            var centreMax = Vector3.negativeInfinity;
            for (int i = first; i < first + count; i++)
            {
                int t = _order[i];
                node.Min = Vector3.Min(node.Min, Vector3.Min(_a[t], Vector3.Min(_b[t], _c[t])));
                node.Max = Vector3.Max(node.Max, Vector3.Max(_a[t], Vector3.Max(_b[t], _c[t])));
                centreMin = Vector3.Min(centreMin, centres[t]);
                centreMax = Vector3.Max(centreMax, centres[t]);
            }
            int index = _nodes.Count;
            _nodes.Add(node);
            if (count <= 4)
            {
                node.First = first;
                node.Count = count;
                _nodes[index] = node;
                return index;
            }

            // Split across the longest side, at the middle of the centres (or half and half if they're all in one place).
            var size = centreMax - centreMin;
            int axis = size.x > size.y && size.x > size.z ? 0 : size.y > size.z ? 1 : 2;
            float split = (centreMin[axis] + centreMax[axis]) * 0.5f;
            int i0 = first, i1 = first + count - 1;
            while (i0 <= i1)
            {
                if (centres[_order[i0]][axis] < split)
                    i0++;
                else
                {
                    (_order[i0], _order[i1]) = (_order[i1], _order[i0]);
                    i1--;
                }
            }
            int leftCount = i0 - first;
            if (leftCount == 0 || leftCount == count)
                leftCount = count / 2;

            // The children go right after each other: left first, so the right one is found from it.
            int left = Build(first, leftCount, centres);
            int right = Build(first + leftCount, count - leftCount, centres);
            node.Left = left;
            node.First = right;
            node.Count = 0;
            _nodes[index] = node;
            return index;
        }

        /// <summary>The nearest triangle a ray hits within <paramref name="maxDistance"/>: how far, and which.</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out float distance, out int triangle)
            => Cast(origin, direction, maxDistance, false, out distance, out triangle);

        /// <summary>Whether a ray hits anything within <paramref name="maxDistance"/> (stops at the first: faster).</summary>
        public bool Any(Vector3 origin, Vector3 direction, float maxDistance, out float distance)
            => Cast(origin, direction, maxDistance, true, out distance, out _);

        private bool Cast(Vector3 origin, Vector3 direction, float maxDistance, bool any, out float distance, out int triangle)
        {
            distance = maxDistance;
            triangle = -1;
            if (_nodes.Count == 0)
                return false;
            var inverse = new Vector3(1f / direction.x, 1f / direction.y, 1f / direction.z);
            var stack = new int[64];
            int top = 0;
            stack[top++] = 0;
            while (top > 0)
            {
                var node = _nodes[stack[--top]];
                if (!HitsBox(node.Min, node.Max, origin, inverse, distance))
                    continue;
                if (node.Count > 0)
                {
                    for (int i = node.First; i < node.First + node.Count; i++)
                    {
                        int t = _order[i];
                        if (HitsTriangle(origin, direction, _a[t], _b[t], _c[t], out float d) && d < distance)
                        {
                            distance = d;
                            triangle = t;
                            if (any)
                                return true;
                        }
                    }
                }
                else if (top < stack.Length - 2)
                {
                    stack[top++] = node.First;
                    stack[top++] = node.Left;
                }
            }
            return triangle >= 0;
        }

        private static bool HitsBox(Vector3 min, Vector3 max, Vector3 origin, Vector3 inverse, float maxDistance)
        {
            float t1 = (min.x - origin.x) * inverse.x, t2 = (max.x - origin.x) * inverse.x;
            float near = Mathf.Min(t1, t2), far = Mathf.Max(t1, t2);
            t1 = (min.y - origin.y) * inverse.y;
            t2 = (max.y - origin.y) * inverse.y;
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
            t1 = (min.z - origin.z) * inverse.z;
            t2 = (max.z - origin.z) * inverse.z;
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
            return far >= Mathf.Max(near, 0f) && near <= maxDistance;
        }

        // Möller–Trumbore, both faces.
        private static bool HitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-12f && det < 1e-12f)
                return false;
            float inv = 1f / det;
            var s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f)
                return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f)
                return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0f;
        }
    }
}
