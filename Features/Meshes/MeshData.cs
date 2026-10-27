using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// A mesh as plain data: what flows along a Mesh Graph's wires. Unlike Unity's Mesh it can be built and changed on
    /// any thread (it only uses math, no engine calls), so whole graphs, and their LODs, are worked out in the
    /// background; only turning the result into a Mesh (<see cref="ToMesh"/>) happens on the main thread.
    /// <para>Each triangle belongs to a material slot (a submesh): merged pieces keep their own slots, so a tree's trunk
    /// and leaves can be one mesh with two materials.</para>
    /// </summary>
    public sealed class MeshData
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> UVs = new();
        public readonly List<Color> Colors = new();
        /// <summary>Triangles (3 vertex indices each), per material slot.</summary>
        public readonly List<List<int>> Slots = new() { new List<int>() };

        public int VertexCount => Positions.Count;

        public int TriangleCount
        {
            get
            {
                int count = 0;
                foreach (var slot in Slots)
                    count += slot.Count / 3;
                return count;
            }
        }

        /// <summary>Whether it has vertex colors (every vertex has one).</summary>
        public bool HasColors => Colors.Count == Positions.Count && Colors.Count > 0;

        /// <summary>The triangles of a material slot, made (with the slots before it) if it doesn't exist yet.</summary>
        public List<int> Slot(int index)
        {
            index = Mathf.Max(0, index);
            while (Slots.Count <= index)
                Slots.Add(new List<int>());
            return Slots[index];
        }

        /// <summary>Adds a vertex; returns its index.</summary>
        public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Positions.Add(position);
            Normals.Add(normal);
            UVs.Add(uv);
            if (Colors.Count > 0)
                Colors.Add(Color.white);
            return Positions.Count - 1;
        }

        public void AddTriangle(int a, int b, int c, int slot = 0)
        {
            var triangles = Slot(slot);
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>Two triangles: a, b, c, d going round (clockwise, seen from the front).</summary>
        public void AddQuad(int a, int b, int c, int d, int slot = 0)
        {
            AddTriangle(a, b, c, slot);
            AddTriangle(a, c, d, slot);
        }

        public MeshData Clone()
        {
            var copy = new MeshData();
            copy.Append(this);
            return copy;
        }

        /// <summary>Adds another mesh's vertices and triangles (each into its own slot number).</summary>
        public void Append(MeshData other) => Append(other, Matrix4x4.identity, false);

        /// <summary>Adds another mesh moved by a matrix (normals turned with it; mirrored matrices flip the triangles).</summary>
        public void Append(MeshData other, Matrix4x4 matrix, bool transform = true)
        {
            if (other == null)
                return;
            int offset = Positions.Count;
            bool colors = HasColors || other.HasColors;
            if (colors && !HasColors)
                FillColors();
            bool flip = transform && Determinant(matrix) < 0f;
            var normals = new NormalMatrix(matrix);
            for (int i = 0; i < other.Positions.Count; i++)
            {
                Positions.Add(transform ? matrix.MultiplyPoint3x4(other.Positions[i]) : other.Positions[i]);
                var normal = i < other.Normals.Count ? other.Normals[i] : Vector3.up;
                Normals.Add(transform ? normals.Apply(normal) : normal);
                UVs.Add(i < other.UVs.Count ? other.UVs[i] : Vector2.zero);
                if (colors)
                    Colors.Add(other.HasColors ? other.Colors[i] : Color.white);
            }
            for (int s = 0; s < other.Slots.Count; s++)
            {
                var source = other.Slots[s];
                var target = Slot(s);
                for (int t = 0; t + 2 < source.Count; t += 3)
                {
                    target.Add(source[t] + offset);
                    target.Add(source[flip ? t + 2 : t + 1] + offset);
                    target.Add(source[flip ? t + 1 : t + 2] + offset);
                }
            }
        }

        /// <summary>Moves every vertex by a matrix (normals turned with it).</summary>
        public void Transform(Matrix4x4 matrix)
        {
            var normals = new NormalMatrix(matrix);
            for (int i = 0; i < Positions.Count; i++)
            {
                Positions[i] = matrix.MultiplyPoint3x4(Positions[i]);
                Normals[i] = normals.Apply(Normals[i]);
            }
            if (Determinant(matrix) < 0f)
                FlipTriangles();
        }

        /// <summary>Turns every triangle to face the other way (and the normals with them).</summary>
        public void FlipTriangles(bool normalsToo = false)
        {
            foreach (var slot in Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                    (slot[t + 1], slot[t + 2]) = (slot[t + 2], slot[t + 1]);
            if (normalsToo)
                for (int i = 0; i < Normals.Count; i++)
                    Normals[i] = -Normals[i];
        }

        /// <summary>Gives every vertex a color (white), so colors can be set.</summary>
        public void FillColors()
        {
            while (Colors.Count < Positions.Count)
                Colors.Add(Color.white);
        }

        public Bounds Bounds
        {
            get
            {
                if (Positions.Count == 0)
                    return new Bounds();
                Vector3 min = Positions[0], max = Positions[0];
                foreach (var p in Positions)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                var bounds = new Bounds();
                bounds.SetMinMax(min, max);
                return bounds;
            }
        }

        #region Normals

        /// <summary>
        /// Works the normals out from the triangles. Where triangles meet at less than <paramref name="smoothAngle"/>
        /// degrees they're smoothed across; sharper edges stay hard (their vertices are split). 0: every triangle flat,
        /// 180: everything smooth.
        /// </summary>
        public void RecalculateNormals(float smoothAngle = 60f)
        {
            // Triangle normals, by area.
            var faceNormals = new List<Vector3>();
            foreach (var slot in Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                    faceNormals.Add(Vector3.Cross(Positions[slot[t + 1]] - Positions[slot[t]], Positions[slot[t + 2]] - Positions[slot[t]]));

            // Corners at the same place (whatever vertex they use) are smoothed together.
            var byPosition = new Dictionary<Vector3, List<(int slot, int corner, int face)>>(new PositionComparer());
            int face = 0;
            for (int s = 0; s < Slots.Count; s++)
                for (int t = 0; t + 2 < Slots[s].Count; t += 3, face++)
                    for (int k = 0; k < 3; k++)
                    {
                        var p = Positions[Slots[s][t + k]];
                        if (!byPosition.TryGetValue(p, out var list))
                            byPosition[p] = list = new List<(int, int, int)>();
                        list.Add((s, t + k, face));
                    }

            float cosLimit = Mathf.Cos(Mathf.Clamp(smoothAngle, 0f, 180f) * Mathf.Deg2Rad);
            var source = Clone();
            Positions.Clear(); Normals.Clear(); UVs.Clear(); Colors.Clear();
            var made = new Dictionary<(int vertex, Vector3 normal), int>();
            var newSlots = new List<List<int>>();
            foreach (var slot in source.Slots)
                newSlots.Add(new List<int>(new int[slot.Count]));

            foreach (var corners in byPosition.Values)
                foreach (var (s, corner, f) in corners)
                {
                    var own = faceNormals[f].normalized;
                    Vector3 sum = Vector3.zero;
                    foreach (var (_, _, other) in corners)
                    {
                        var n = faceNormals[other];
                        if (other == f || Vector3.Dot(own, n.normalized) >= cosLimit - 1e-5f)
                            sum += n;
                    }
                    var normal = sum.sqrMagnitude > 1e-12f ? sum.normalized : own;
                    int vertex = source.Slots[s][corner];
                    var key = (vertex, Quantize(normal));
                    if (!made.TryGetValue(key, out int index))
                    {
                        index = Positions.Count;
                        Positions.Add(source.Positions[vertex]);
                        Normals.Add(normal);
                        UVs.Add(vertex < source.UVs.Count ? source.UVs[vertex] : Vector2.zero);
                        if (source.HasColors)
                            Colors.Add(source.Colors[vertex]);
                        made[key] = index;
                    }
                    newSlots[s][corner] = index;
                }
            Slots.Clear();
            Slots.AddRange(newSlots);
        }

        private static Vector3 Quantize(Vector3 v) => new(Mathf.Round(v.x * 1000f), Mathf.Round(v.y * 1000f), Mathf.Round(v.z * 1000f));

        /// <summary>Positions equal to within a tenth of a millimetre.</summary>
        internal sealed class PositionComparer : IEqualityComparer<Vector3>
        {
            public const float Cell = 1e-4f;
            public bool Equals(Vector3 a, Vector3 b) => Key(a) == Key(b);
            public int GetHashCode(Vector3 v) => Key(v).GetHashCode();
            private static (long, long, long) Key(Vector3 v)
                => ((long)Math.Round(v.x / Cell), (long)Math.Round(v.y / Cell), (long)Math.Round(v.z / Cell));
        }

        #endregion

        #region Unity meshes

        /// <summary>Makes a Unity Mesh of it (main thread). Slots become submeshes.</summary>
        public Mesh ToMesh(string name = "Generated Mesh")
        {
            var mesh = new Mesh { name = name };
            WriteTo(mesh);
            return mesh;
        }

        /// <summary>Fills a Unity Mesh with it (main thread).</summary>
        public void WriteTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = Positions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, UVs);
            if (HasColors)
                mesh.SetColors(Colors);
            // Empty slots at the end aren't submeshes; ones between keep their number (and their material).
            int last = Slots.Count - 1;
            while (last > 0 && Slots[last].Count == 0)
                last--;
            mesh.subMeshCount = last + 1;
            for (int s = 0; s <= last; s++)
                mesh.SetTriangles(Slots[s], s, false);
            mesh.RecalculateBounds();
            if (Positions.Count > 0)
                mesh.RecalculateTangents();
        }

        /// <summary>Reads a Unity Mesh (main thread; at runtime it must be Read/Write enabled).</summary>
        public static MeshData FromMesh(Mesh mesh)
        {
            var data = new MeshData();
            if (mesh == null)
                return data;
            mesh.GetVertices(data.Positions);
            mesh.GetNormals(data.Normals);
            mesh.GetUVs(0, data.UVs);
            mesh.GetColors(data.Colors);
            while (data.Normals.Count < data.Positions.Count) data.Normals.Add(Vector3.up);
            while (data.UVs.Count < data.Positions.Count) data.UVs.Add(Vector2.zero);
            if (data.Colors.Count != data.Positions.Count) data.Colors.Clear();
            data.Slots.Clear();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var triangles = new List<int>();
                if (mesh.GetTopology(s) == MeshTopology.Triangles)
                    mesh.GetTriangles(triangles, s);
                data.Slots.Add(triangles);
            }
            if (data.Slots.Count == 0)
                data.Slots.Add(new List<int>());
            bool hadNormals = mesh.normals != null && mesh.normals.Length == data.Positions.Count;
            if (!hadNormals)
                data.RecalculateNormals();
            return data;
        }

        #endregion

        /// <summary>
        /// How a matrix turns normals: its inverse transpose (so squashed shapes keep normals square to their faces),
        /// worked out by hand from cofactors: no engine call, so it's safe on any thread.
        /// </summary>
        internal readonly struct NormalMatrix
        {
            private readonly Vector3 _r0, _r1, _r2;

            public NormalMatrix(Matrix4x4 m)
            {
                // Cofactors of the 3x3 part; divided by the determinant's sign (its size goes when normalized).
                float sign = Determinant(m) < 0f ? -1f : 1f;
                _r0 = sign * new Vector3(m.m11 * m.m22 - m.m12 * m.m21, m.m12 * m.m20 - m.m10 * m.m22, m.m10 * m.m21 - m.m11 * m.m20);
                _r1 = sign * new Vector3(m.m02 * m.m21 - m.m01 * m.m22, m.m00 * m.m22 - m.m02 * m.m20, m.m01 * m.m20 - m.m00 * m.m21);
                _r2 = sign * new Vector3(m.m01 * m.m12 - m.m02 * m.m11, m.m02 * m.m10 - m.m00 * m.m12, m.m00 * m.m11 - m.m01 * m.m10);
            }

            public Vector3 Apply(Vector3 normal)
            {
                // The cofactor matrix C has C[i][j] = cofactor(i, j); the inverse transpose is C / det, so n' = C n.
                var n = new Vector3(
                    _r0.x * normal.x + _r0.y * normal.y + _r0.z * normal.z,
                    _r1.x * normal.x + _r1.y * normal.y + _r1.z * normal.z,
                    _r2.x * normal.x + _r2.y * normal.y + _r2.z * normal.z);
                return n.sqrMagnitude > 1e-12f ? n.normalized : normal;
            }
        }

        internal static float Determinant(Matrix4x4 m)
            => m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);
    }
}
