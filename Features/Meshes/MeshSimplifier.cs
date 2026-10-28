using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Makes a mesh with fewer triangles that looks as much like it as possible: what LODs are made with. It collapses
    /// edges one at a time, cheapest first, where "cost" is how far the surface would move (quadric error metrics,
    /// after Garland and Heckbert, in the fast iterative form of Sp4cerat's simplifier).
    /// <para>Positions are welded before simplifying, so a mesh split for hard edges (flat shading) simplifies as one
    /// surface; each triangle corner keeps its own UV, normal and color. Open edges and UV seams stay where they are,
    /// so textures don't tear and holes don't grow. Pure math: safe on any thread.</para>
    /// </summary>
    public static class MeshSimplifier
    {
        /// <summary>
        /// A simpler copy of <paramref name="source"/> with about <paramref name="ratio"/> of its triangles (0.5: half).
        /// <paramref name="aggressiveness"/> trades accuracy for speed (7 is a good middle; higher removes more per pass).
        /// </summary>
        public static MeshData Simplify(MeshData source, float ratio, float aggressiveness = 7f)
        {
            if (source == null)
                return new MeshData();
            int total = source.TriangleCount;
            int target = Mathf.Max(4, Mathf.RoundToInt(total * Mathf.Clamp01(ratio)));
            if (ratio >= 0.999f || total <= target)
                return source.Clone();
            var work = new Work(source);
            work.Run(target, aggressiveness);
            return work.Output(source);
        }

        /// <summary>One simplified copy per ratio, each made from the original (LOD1, LOD2...).</summary>
        public static List<MeshData> Levels(MeshData source, IReadOnlyList<float> ratios)
        {
            var levels = new List<MeshData>();
            foreach (float ratio in ratios)
                levels.Add(Simplify(source, ratio));
            return levels;
        }

        // A symmetric 4x4 matrix (10 numbers): the sum of squared distances to a set of planes.
        private struct Quadric
        {
            public double M0, M1, M2, M3, M4, M5, M6, M7, M8, M9;

            public Quadric(double a, double b, double c, double d)
            {
                M0 = a * a; M1 = a * b; M2 = a * c; M3 = a * d;
                M4 = b * b; M5 = b * c; M6 = b * d;
                M7 = c * c; M8 = c * d;
                M9 = d * d;
            }

            public static Quadric operator +(Quadric x, Quadric y) => new()
            {
                M0 = x.M0 + y.M0, M1 = x.M1 + y.M1, M2 = x.M2 + y.M2, M3 = x.M3 + y.M3, M4 = x.M4 + y.M4,
                M5 = x.M5 + y.M5, M6 = x.M6 + y.M6, M7 = x.M7 + y.M7, M8 = x.M8 + y.M8, M9 = x.M9 + y.M9,
            };

            public double this[int i] => i switch
            {
                0 => M0, 1 => M1, 2 => M2, 3 => M3, 4 => M4, 5 => M5, 6 => M6, 7 => M7, 8 => M8, _ => M9,
            };

            public double Det(int a11, int a12, int a13, int a21, int a22, int a23, int a31, int a32, int a33)
                => this[a11] * this[a22] * this[a33] + this[a13] * this[a21] * this[a32] + this[a12] * this[a23] * this[a31]
                 - this[a13] * this[a22] * this[a31] - this[a11] * this[a23] * this[a32] - this[a12] * this[a21] * this[a33];

            public double Error(double x, double y, double z)
                => M0 * x * x + 2 * M1 * x * y + 2 * M2 * x * z + 2 * M3 * x + M4 * y * y
                 + 2 * M5 * y * z + 2 * M6 * y + M7 * z * z + 2 * M8 * z + M9;
        }

        private struct Vec
        {
            public double X, Y, Z;
            public Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
            public Vec(Vector3 v) { X = v.x; Y = v.y; Z = v.z; }
            public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s, a.Z * s);
            public double Dot(Vec b) => X * b.X + Y * b.Y + Z * b.Z;
            public Vec Cross(Vec b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
            public Vec Normalized()
            {
                double length = Math.Sqrt(X * X + Y * Y + Z * Z);
                return length > 1e-20 ? new Vec(X / length, Y / length, Z / length) : new Vec(0, 0, 0);
            }
        }

        private sealed class Triangle
        {
            public readonly int[] V = new int[3];     // welded positions
            public readonly int[] A = new int[3];     // the source vertex each corner takes its UV, normal and color from
            public readonly double[] Err = new double[4];
            public int Slot;
            public bool Deleted, Dirty;
            public Vec Normal;
        }

        private struct Vertex
        {
            public Vec P;
            public Quadric Q;
            public int TStart, TCount;
            public bool Border;
        }

        private struct Ref
        {
            public int Tid, TVertex;
        }

        private sealed class Work
        {
            private readonly List<Triangle> _triangles = new();
            private Vertex[] _vertices;
            private readonly List<Ref> _refs = new();
            // Per position: the one source vertex all its corners use (a smooth spot), or -1 (a hard edge or seam,
            // whose corners keep their own). Collapsing into a smooth spot gives the moved corners its vertex, so the
            // simpler mesh shares vertices as the original did.
            private int[] _canonical;

            public Work(MeshData source)
            {
                // Weld positions: hard edges and seams are one surface to simplify.
                var welded = new Dictionary<Vector3, int>(new MeshData.PositionComparer());
                var positionOf = new int[source.Positions.Count];
                var positions = new List<Vector3>();
                for (int i = 0; i < source.Positions.Count; i++)
                {
                    if (!welded.TryGetValue(source.Positions[i], out int index))
                    {
                        index = positions.Count;
                        positions.Add(source.Positions[i]);
                        welded[source.Positions[i]] = index;
                    }
                    positionOf[i] = index;
                }
                _vertices = new Vertex[positions.Count];
                for (int i = 0; i < positions.Count; i++)
                    _vertices[i].P = new Vec(positions[i]);

                for (int s = 0; s < source.Slots.Count; s++)
                {
                    var slot = source.Slots[s];
                    for (int t = 0; t + 2 < slot.Count; t += 3)
                    {
                        var triangle = new Triangle { Slot = s };
                        for (int k = 0; k < 3; k++)
                        {
                            triangle.A[k] = slot[t + k];
                            triangle.V[k] = positionOf[slot[t + k]];
                        }
                        // Welding can leave a triangle with two corners at one place: it has no area, so it goes.
                        if (triangle.V[0] == triangle.V[1] || triangle.V[1] == triangle.V[2] || triangle.V[0] == triangle.V[2])
                            continue;
                        _triangles.Add(triangle);
                    }
                }

                _canonical = new int[positions.Count];
                for (int i = 0; i < _canonical.Length; i++)
                    _canonical[i] = -2; // (none seen yet)
                foreach (var triangle in _triangles)
                    for (int k = 0; k < 3; k++)
                    {
                        int v = triangle.V[k];
                        if (_canonical[v] == -2)
                            _canonical[v] = triangle.A[k];
                        else if (_canonical[v] != triangle.A[k])
                            _canonical[v] = -1;
                    }

                // UV seams stay put: a position whose corners disagree on UVs is held like an open edge.
                var firstUV = new Vector2?[positions.Count];
                foreach (var triangle in _triangles)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = triangle.A[k];
                        var uv = a < source.UVs.Count ? source.UVs[a] : Vector2.zero;
                        int v = triangle.V[k];
                        if (firstUV[v] == null)
                            firstUV[v] = uv;
                        else if ((firstUV[v].Value - uv).sqrMagnitude > 1e-8f)
                            _vertices[v].Border = true;
                    }
            }

            public void Run(int target, float aggressiveness)
            {
                int deleted = 0;
                var deleted0 = new List<bool>();
                var deleted1 = new List<bool>();
                int count = _triangles.Count;

                for (int iteration = 0; iteration < 100; iteration++)
                {
                    if (count - deleted <= target)
                        break;
                    // Every few passes: drop removed triangles and rebuild who touches what.
                    if (iteration % 5 == 0)
                        UpdateMesh(iteration);
                    foreach (var triangle in _triangles)
                        triangle.Dirty = false;

                    // Edges cheaper than this go this pass; it rises each pass.
                    double threshold = 1e-9 * Math.Pow(iteration + 3, aggressiveness);

                    for (int ti = 0; ti < _triangles.Count; ti++)
                    {
                        var triangle = _triangles[ti];
                        if (triangle.Err[3] > threshold || triangle.Deleted || triangle.Dirty)
                            continue;
                        for (int j = 0; j < 3; j++)
                        {
                            if (triangle.Err[j] >= threshold)
                                continue;
                            int i0 = triangle.V[j], i1 = triangle.V[(j + 1) % 3];
                            // An edge along a border may only slide along it.
                            if (_vertices[i0].Border != _vertices[i1].Border)
                                continue;

                            CalculateError(i0, i1, out var p);
                            Resize(deleted0, _vertices[i0].TCount);
                            Resize(deleted1, _vertices[i1].TCount);
                            if (Flipped(p, i1, ref _vertices[i0], deleted0) || Flipped(p, i0, ref _vertices[i1], deleted1))
                                continue;

                            // Collapse i1 into i0.
                            _vertices[i0].P = p;
                            _vertices[i0].Q = _vertices[i0].Q + _vertices[i1].Q;
                            int start = _refs.Count;
                            UpdateTriangles(i0, _vertices[i0], deleted0, ref deleted, -1, -1);
                            UpdateTriangles(i0, _vertices[i1], deleted1, ref deleted, _canonical[i1], _canonical[i0]);
                            int touched = _refs.Count - start;
                            if (touched <= _vertices[i0].TCount)
                            {
                                // Fits where i0's list was: reuse that space.
                                for (int k = 0; k < touched; k++)
                                    _refs[_vertices[i0].TStart + k] = _refs[start + k];
                                _refs.RemoveRange(start, touched);
                            }
                            else
                                _vertices[i0].TStart = start;
                            _vertices[i0].TCount = touched;
                            break;
                        }
                        if (count - deleted <= target)
                            break;
                    }
                }
            }

            private static void Resize(List<bool> list, int size)
            {
                list.Clear();
                for (int i = 0; i < size; i++)
                    list.Add(false);
            }

            // Would moving a vertex to p turn any of its other triangles over (or make one a sliver)?
            private bool Flipped(Vec p, int other, ref Vertex vertex, List<bool> deleted)
            {
                for (int k = 0; k < vertex.TCount; k++)
                {
                    var r = _refs[vertex.TStart + k];
                    var triangle = _triangles[r.Tid];
                    if (triangle.Deleted)
                        continue;
                    int s = r.TVertex;
                    int id1 = triangle.V[(s + 1) % 3], id2 = triangle.V[(s + 2) % 3];
                    if (id1 == other || id2 == other)
                    {
                        // Shares the edge being collapsed: it goes.
                        deleted[k] = true;
                        continue;
                    }
                    var d1 = (_vertices[id1].P - p).Normalized();
                    var d2 = (_vertices[id2].P - p).Normalized();
                    if (Math.Abs(d1.Dot(d2)) > 0.999)
                        return true;
                    var n = d1.Cross(d2).Normalized();
                    deleted[k] = false;
                    if (n.Dot(triangle.Normal) < 0.2)
                        return true;
                }
                return false;
            }

            private void UpdateTriangles(int i0, Vertex vertex, List<bool> deleted, ref int deletedCount, int fromAttribute, int toAttribute)
            {
                for (int k = 0; k < vertex.TCount; k++)
                {
                    var r = _refs[vertex.TStart + k];
                    var triangle = _triangles[r.Tid];
                    if (triangle.Deleted)
                        continue;
                    if (deleted[k])
                    {
                        triangle.Deleted = true;
                        deletedCount++;
                        continue;
                    }
                    triangle.V[r.TVertex] = i0;
                    // Both smooth spots: the moved corner takes the kept vertex's attributes.
                    if (fromAttribute >= 0 && toAttribute >= 0 && triangle.A[r.TVertex] == fromAttribute)
                        triangle.A[r.TVertex] = toAttribute;
                    triangle.Dirty = true;
                    triangle.Err[0] = CalculateError(triangle.V[0], triangle.V[1], out _);
                    triangle.Err[1] = CalculateError(triangle.V[1], triangle.V[2], out _);
                    triangle.Err[2] = CalculateError(triangle.V[2], triangle.V[0], out _);
                    triangle.Err[3] = Math.Min(triangle.Err[0], Math.Min(triangle.Err[1], triangle.Err[2]));
                    _refs.Add(r);
                }
            }

            private void UpdateMesh(int iteration)
            {
                if (iteration > 0)
                    _triangles.RemoveAll(t => t.Deleted);

                // Who touches what.
                for (int i = 0; i < _vertices.Length; i++)
                {
                    _vertices[i].TStart = 0;
                    _vertices[i].TCount = 0;
                }
                foreach (var triangle in _triangles)
                    for (int j = 0; j < 3; j++)
                        _vertices[triangle.V[j]].TCount++;
                int start = 0;
                for (int i = 0; i < _vertices.Length; i++)
                {
                    _vertices[i].TStart = start;
                    start += _vertices[i].TCount;
                    _vertices[i].TCount = 0;
                }
                _refs.Clear();
                for (int i = 0; i < start; i++)
                    _refs.Add(default);
                for (int ti = 0; ti < _triangles.Count; ti++)
                {
                    var triangle = _triangles[ti];
                    for (int j = 0; j < 3; j++)
                    {
                        ref var vertex = ref _vertices[triangle.V[j]];
                        _refs[vertex.TStart + vertex.TCount] = new Ref { Tid = ti, TVertex = j };
                        vertex.TCount++;
                    }
                }

                if (iteration != 0)
                    return;

                // Open edges: an edge only one triangle uses. Its ends are held like seams.
                var neighbours = new Dictionary<int, int>();
                for (int i = 0; i < _vertices.Length; i++)
                {
                    neighbours.Clear();
                    for (int k = 0; k < _vertices[i].TCount; k++)
                    {
                        var triangle = _triangles[_refs[_vertices[i].TStart + k].Tid];
                        for (int j = 0; j < 3; j++)
                        {
                            int id = triangle.V[j];
                            neighbours[id] = neighbours.TryGetValue(id, out int n) ? n + 1 : 1;
                        }
                    }
                    foreach (var entry in neighbours)
                        if (entry.Value == 1)
                            _vertices[entry.Key].Border = true;
                }

                // The planes around each position.
                foreach (var triangle in _triangles)
                {
                    var p0 = _vertices[triangle.V[0]].P;
                    var n = (_vertices[triangle.V[1]].P - p0).Cross(_vertices[triangle.V[2]].P - p0).Normalized();
                    triangle.Normal = n;
                    var plane = new Quadric(n.X, n.Y, n.Z, -n.Dot(p0));
                    for (int j = 0; j < 3; j++)
                        _vertices[triangle.V[j]].Q = _vertices[triangle.V[j]].Q + plane;
                }
                foreach (var triangle in _triangles)
                {
                    for (int j = 0; j < 3; j++)
                        triangle.Err[j] = CalculateError(triangle.V[j], triangle.V[(j + 1) % 3], out _);
                    triangle.Err[3] = Math.Min(triangle.Err[0], Math.Min(triangle.Err[1], triangle.Err[2]));
                }
            }

            // The cost of collapsing an edge, and where the merged vertex goes.
            private double CalculateError(int a, int b, out Vec result)
            {
                var q = _vertices[a].Q + _vertices[b].Q;
                bool border = _vertices[a].Border && _vertices[b].Border;
                double det = q.Det(0, 1, 2, 1, 4, 5, 2, 5, 7);
                if (Math.Abs(det) > 1e-12 && !border)
                {
                    // The best place: where the planes' distances add up least.
                    result = new Vec(
                        -1 / det * q.Det(1, 2, 3, 4, 5, 6, 5, 7, 8),
                        1 / det * q.Det(0, 2, 3, 1, 5, 6, 2, 7, 8),
                        -1 / det * q.Det(0, 1, 3, 1, 4, 6, 2, 5, 8));
                    return q.Error(result.X, result.Y, result.Z);
                }
                // Otherwise one end, the other, or the middle: whichever costs least.
                var p1 = _vertices[a].P;
                var p2 = _vertices[b].P;
                var p3 = (p1 + p2) * 0.5;
                double e1 = q.Error(p1.X, p1.Y, p1.Z), e2 = q.Error(p2.X, p2.Y, p2.Z), e3 = q.Error(p3.X, p3.Y, p3.Z);
                double best = Math.Min(e1, Math.Min(e2, e3));
                result = best == e1 ? p1 : best == e2 ? p2 : p3;
                return best;
            }

            /// <summary>The simplified mesh: a vertex per (position, source corner) pair still used.</summary>
            public MeshData Output(MeshData source)
            {
                var output = new MeshData();
                output.Slots.Clear();
                for (int s = 0; s < source.Slots.Count; s++)
                    output.Slots.Add(new List<int>());
                bool colors = source.HasColors;
                var made = new Dictionary<(int position, int attribute), int>();
                foreach (var triangle in _triangles)
                {
                    if (triangle.Deleted)
                        continue;
                    for (int k = 0; k < 3; k++)
                    {
                        var key = (triangle.V[k], triangle.A[k]);
                        if (!made.TryGetValue(key, out int index))
                        {
                            index = output.Positions.Count;
                            var p = _vertices[triangle.V[k]].P;
                            int a = triangle.A[k];
                            output.Positions.Add(new Vector3((float)p.X, (float)p.Y, (float)p.Z));
                            output.Normals.Add(a < source.Normals.Count ? source.Normals[a] : Vector3.up);
                            output.UVs.Add(a < source.UVs.Count ? source.UVs[a] : Vector2.zero);
                            if (colors)
                                output.Colors.Add(source.Colors[a]);
                            if (source.HasSelection)
                                output.Selection.Add(source.Selection[a]);
                            made[key] = index;
                        }
                        output.Slots[triangle.Slot].Add(index);
                    }
                }
                if (output.Slots.Count == 0)
                    output.Slots.Add(new List<int>());
                return output;
            }
        }
    }
}
