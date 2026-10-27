using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// The heavier modelling operations: subdividing, spherizing, loop cuts and extrusion (booleans are in
    /// <see cref="MeshBoolean"/>). Each takes a mesh and gives a new one. Pure math: safe on any thread.
    /// </summary>
    public static class MeshOperations
    {
        // Positions welded (hard edges and seams are one surface): each vertex's position index.
        private static int[] Weld(MeshData mesh, out List<Vector3> positions)
        {
            var welded = new Dictionary<Vector3, int>(new MeshData.PositionComparer());
            var of = new int[mesh.VertexCount];
            positions = new List<Vector3>();
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (!welded.TryGetValue(mesh.Positions[i], out int index))
                {
                    index = positions.Count;
                    positions.Add(mesh.Positions[i]);
                    welded[mesh.Positions[i]] = index;
                }
                of[i] = index;
            }
            return of;
        }

        private static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

        // A new vertex part way between two (UV, normal and color blended).
        private static int Blend(MeshData from, MeshData to, int a, int b, float t, Vector3 position)
        {
            var normal = Vector3.Lerp(from.Normals[a], from.Normals[b], t);
            var uv = from.UVs[a] + (from.UVs[b] - from.UVs[a]) * t;
            int index = to.AddVertex(position, normal.sqrMagnitude > 1e-12f ? normal.normalized : from.Normals[a], uv);
            if (from.HasColors)
            {
                to.FillColors();
                to.Colors[index] = Color.Lerp(from.Colors[a], from.Colors[b], t);
            }
            return index;
        }

        private static int CopyVertex(MeshData from, MeshData to, int index, Vector3 position)
        {
            int copy = to.AddVertex(position, from.Normals[index], from.UVs[index]);
            if (from.HasColors)
            {
                to.FillColors();
                to.Colors[copy] = from.Colors[index];
            }
            return copy;
        }

        #region Subdivide

        /// <summary>
        /// Splits every triangle into four, <paramref name="levels"/> times. Smooth (Loop subdivision) rounds the shape
        /// off as it goes, a cube towards a sphere; otherwise the shape stays exactly as it was, with more triangles to
        /// bend and displace. Open edges stay where they are (smooth along themselves).
        /// </summary>
        public static MeshData Subdivide(MeshData mesh, int levels, bool smooth)
        {
            var result = mesh;
            for (int level = 0; level < Mathf.Clamp(levels, 0, 6); level++)
                result = SubdivideOnce(result, smooth);
            return result == mesh ? mesh.Clone() : result;
        }

        private static MeshData SubdivideOnce(MeshData mesh, bool smooth)
        {
            var positionOf = Weld(mesh, out var positions);

            // The edges: who's at each end, and the corners opposite them.
            var edges = new Dictionary<(int, int), (int index, int opposite0, int opposite1, int faces)>();
            var neighbours = new List<HashSet<int>>();
            for (int i = 0; i < positions.Count; i++)
                neighbours.Add(new HashSet<int>());
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = positionOf[slot[t + k]], b = positionOf[slot[t + (k + 1) % 3]], c = positionOf[slot[t + (k + 2) % 3]];
                        if (a == b)
                            continue;
                        neighbours[a].Add(b);
                        neighbours[b].Add(a);
                        var key = EdgeKey(a, b);
                        if (edges.TryGetValue(key, out var edge))
                            edges[key] = (edge.index, edge.opposite0, c, edge.faces + 1);
                        else
                            edges[key] = (edges.Count, c, -1, 1);
                    }

            // Where each position goes, and where each edge's new point goes.
            var moved = new Vector3[positions.Count];
            var boundary = new bool[positions.Count];
            var boundaryNeighbours = new List<int>[positions.Count];
            foreach (var entry in edges)
                if (entry.Value.faces == 1)
                {
                    var (a, b) = entry.Key;
                    boundary[a] = boundary[b] = true;
                    (boundaryNeighbours[a] ??= new List<int>()).Add(b);
                    (boundaryNeighbours[b] ??= new List<int>()).Add(a);
                }
            for (int v = 0; v < positions.Count; v++)
            {
                if (!smooth)
                {
                    moved[v] = positions[v];
                    continue;
                }
                if (boundary[v])
                {
                    var around = boundaryNeighbours[v];
                    moved[v] = around != null && around.Count == 2
                        ? positions[v] * 0.75f + (positions[around[0]] + positions[around[1]]) * 0.125f
                        : positions[v];
                    continue;
                }
                int n = neighbours[v].Count;
                if (n < 3)
                {
                    moved[v] = positions[v];
                    continue;
                }
                float beta = n == 3 ? 3f / 16f : 3f / (8f * n);
                var sum = Vector3.zero;
                foreach (int w in neighbours[v])
                    sum += positions[w];
                moved[v] = positions[v] * (1f - n * beta) + sum * beta;
            }
            var edgePoint = new Dictionary<(int, int), Vector3>();
            foreach (var entry in edges)
            {
                var (a, b) = entry.Key;
                var e = entry.Value;
                edgePoint[entry.Key] = smooth && e.faces == 2 && e.opposite1 >= 0
                    ? (positions[a] + positions[b]) * 0.375f + (positions[e.opposite0] + positions[e.opposite1]) * 0.125f
                    : (positions[a] + positions[b]) * 0.5f;
            }

            // The new triangles: a vertex per (position, source corner) and per (edge, its two corners), so seams and
            // hard edges keep their own UVs and normals.
            var result = new MeshData();
            result.Slots.Clear();
            var corners = new Dictionary<(int, int), int>();
            var mids = new Dictionary<(int, int), int>();
            int Corner(int vertex)
            {
                var key = (positionOf[vertex], vertex);
                if (!corners.TryGetValue(key, out int index))
                    corners[key] = index = CopyVertex(mesh, result, vertex, moved[positionOf[vertex]]);
                return index;
            }
            int Mid(int va, int vb)
            {
                var key = va < vb ? (va, vb) : (vb, va);
                if (!mids.TryGetValue(key, out int index))
                {
                    var point = edgePoint.TryGetValue(EdgeKey(positionOf[va], positionOf[vb]), out var p) ? p : (mesh.Positions[va] + mesh.Positions[vb]) * 0.5f;
                    mids[key] = index = Blend(mesh, result, key.Item1, key.Item2, 0.5f, point);
                }
                return index;
            }
            for (int s = 0; s < mesh.Slots.Count; s++)
            {
                var slot = mesh.Slots[s];
                var target = result.Slot(s);
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    int a = slot[t], b = slot[t + 1], c = slot[t + 2];
                    int ca = Corner(a), cb = Corner(b), cc = Corner(c);
                    int ab = Mid(a, b), bc = Mid(b, c), ac = Mid(c, a);
                    target.AddRange(new[] { ca, ab, ac, ab, cb, bc, ac, bc, cc, ab, bc, ac });
                }
            }
            return result;
        }

        #endregion

        #region Spherize

        /// <summary>
        /// Pulls the mesh towards a sphere around its middle: <paramref name="amount"/> 0 leaves it, 1 is a perfect
        /// sphere, in between rounds it off (a box at 0.3 is a pillow). <paramref name="radius"/> 0: its average size.
        /// </summary>
        public static MeshData Spherize(MeshData mesh, float amount, float radius = 0f)
        {
            var result = mesh.Clone();
            if (result.VertexCount == 0)
                return result;
            var center = result.Bounds.center;
            if (radius <= 0f)
            {
                float sum = 0f;
                foreach (var p in result.Positions)
                    sum += (p - center).magnitude;
                radius = sum / result.VertexCount;
            }
            for (int i = 0; i < result.VertexCount; i++)
            {
                var offset = result.Positions[i] - center;
                var direction = offset.sqrMagnitude > 1e-12f ? offset.normalized : Vector3.up;
                result.Positions[i] = Vector3.Lerp(result.Positions[i], center + direction * radius, amount);
                var normal = Vector3.Lerp(result.Normals[i], direction, amount);
                result.Normals[i] = normal.sqrMagnitude > 1e-12f ? normal.normalized : direction;
            }
            return result;
        }

        #endregion

        #region Loop cut

        /// <summary>
        /// Cuts the mesh across an axis (0: X, 1: Y, 2: Z) with <paramref name="cuts"/> evenly spaced slices, adding a
        /// ring of edges around it at each: more places for Bend, Twist and Taper to bend it, and for Extrude to start.
        /// </summary>
        public static MeshData LoopCut(MeshData mesh, int axis, int cuts)
        {
            axis = Mathf.Clamp(axis, 0, 2);
            var result = mesh.Clone();
            if (cuts <= 0 || result.VertexCount == 0)
                return result;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in result.Positions)
            {
                min = Mathf.Min(min, p[axis]);
                max = Mathf.Max(max, p[axis]);
            }
            for (int k = 1; k <= cuts; k++)
                result = Slice(result, axis, Mathf.Lerp(min, max, k / (float)(cuts + 1)));
            return result;
        }

        /// <summary>Splits the triangles that cross a plane (axis = value) along it.</summary>
        public static MeshData Slice(MeshData mesh, int axis, float value)
        {
            var result = new MeshData();
            result.Positions.AddRange(mesh.Positions);
            result.Normals.AddRange(mesh.Normals);
            result.UVs.AddRange(mesh.UVs);
            if (mesh.HasColors)
                result.Colors.AddRange(mesh.Colors);
            result.Slots.Clear();
            var split = new Dictionary<(int, int), int>();

            float Side(int v)
            {
                float d = mesh.Positions[v][axis] - value;
                return Mathf.Abs(d) < 1e-6f ? 0f : d;
            }
            int Cut(int a, int b)
            {
                // The same point for the same edge either way round (and the same place for welded copies).
                var key = a < b ? (a, b) : (b, a);
                if (split.TryGetValue(key, out int index))
                    return index;
                int lo = key.Item1, hi = key.Item2;
                if (Compare(mesh.Positions[lo], mesh.Positions[hi]) > 0)
                    (lo, hi) = (hi, lo);
                float da = mesh.Positions[lo][axis] - value, db = mesh.Positions[hi][axis] - value;
                float t = da / (da - db);
                var point = Vector3.Lerp(mesh.Positions[lo], mesh.Positions[hi], t);
                point[axis] = value;
                split[key] = index = Blend(mesh, result, lo, hi, t, point);
                return index;
            }

            for (int s = 0; s < mesh.Slots.Count; s++)
            {
                var slot = mesh.Slots[s];
                var target = result.Slot(s);
                var front = new List<int>(4);
                var back = new List<int>(4);
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    int[] tri = { slot[t], slot[t + 1], slot[t + 2] };
                    float[] d = { Side(tri[0]), Side(tri[1]), Side(tri[2]) };
                    bool crosses = (d[0] > 0 || d[1] > 0 || d[2] > 0) && (d[0] < 0 || d[1] < 0 || d[2] < 0);
                    if (!crosses)
                    {
                        target.AddRange(tri);
                        continue;
                    }
                    front.Clear();
                    back.Clear();
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tri[k], b = tri[(k + 1) % 3];
                        float da = d[k], db = d[(k + 1) % 3];
                        if (da >= 0) front.Add(a);
                        if (da <= 0) back.Add(a);
                        if ((da > 0 && db < 0) || (da < 0 && db > 0))
                        {
                            int cut = Cut(a, b);
                            front.Add(cut);
                            back.Add(cut);
                        }
                    }
                    Fan(front, target);
                    Fan(back, target);
                }
            }
            return result;
        }

        private static int Compare(Vector3 a, Vector3 b)
        {
            int c = a.x.CompareTo(b.x);
            if (c != 0) return c;
            c = a.y.CompareTo(b.y);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        private static void Fan(List<int> polygon, List<int> into)
        {
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                into.Add(polygon[0]);
                into.Add(polygon[i]);
                into.Add(polygon[i + 1]);
            }
        }

        #endregion

        #region Extrude

        public enum ExtrudeSelection { FacingDirection, All, MaterialSlot }

        /// <summary>
        /// Pulls faces out of the mesh, with walls joining them to where they were: the top of a box pulled up into a
        /// tower, a plane's edge into a wall. Faces are picked by which way they face (within
        /// <paramref name="angle"/> degrees of <paramref name="direction"/>), all of them, or a material slot. They move
        /// <paramref name="distance"/> along <paramref name="direction"/> (or along their own normals), and the moved
        /// faces can be scaled about their middle (<paramref name="capScale"/>: 0.5 tapers a tower).
        /// </summary>
        public static MeshData Extrude(MeshData mesh, ExtrudeSelection selection, Vector3 direction, float angle, int slotIndex,
            float distance, bool alongNormals, float capScale, int wallSlot = -1)
        {
            var result = mesh.Clone();
            var positionOf = Weld(result, out var positions);
            var dir = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
            float cosLimit = Mathf.Cos(Mathf.Clamp(angle, 0f, 180f) * Mathf.Deg2Rad);

            // Picked triangles, as (slot, first index).
            var picked = new List<(int slot, int t)>();
            var pickedSet = new HashSet<(int, int)>();
            for (int s = 0; s < result.Slots.Count; s++)
            {
                var slot = result.Slots[s];
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    bool take = selection switch
                    {
                        ExtrudeSelection.All => true,
                        ExtrudeSelection.MaterialSlot => s == slotIndex,
                        _ => Vector3.Dot(FaceNormal(result, slot, t), dir) >= cosLimit - 1e-5f,
                    };
                    if (take)
                    {
                        picked.Add((s, t));
                        pickedSet.Add((s, t));
                    }
                }
            }
            if (picked.Count == 0)
                return result;

            // How far each picked position moves: along the direction, or its picked faces' average normal.
            var normalSum = new Dictionary<int, Vector3>();
            foreach (var (s, t) in picked)
            {
                var n = FaceNormal(result, result.Slots[s], t);
                for (int k = 0; k < 3; k++)
                {
                    int p = positionOf[result.Slots[s][t + k]];
                    normalSum.TryGetValue(p, out var sum);
                    normalSum[p] = sum + n;
                }
            }

            // The picked faces fall into separate patches (each top of an array of boxes): each scales about its own middle.
            var patchOf = Patches(result, positionOf, picked);
            var patchCenter = new Dictionary<int, (Vector3 sum, int count)>();
            foreach (var entry in normalSum)
            {
                int patch = patchOf[entry.Key];
                patchCenter.TryGetValue(patch, out var c);
                patchCenter[patch] = (c.sum + positions[entry.Key], c.count + 1);
            }
            var target = new Dictionary<int, Vector3>();
            foreach (var entry in normalSum)
            {
                var offset = alongNormals ? (entry.Value.sqrMagnitude > 1e-12f ? entry.Value.normalized : dir) * distance : dir * distance;
                var (sum, count) = patchCenter[patchOf[entry.Key]];
                var middle = sum / Mathf.Max(1, count);
                target[entry.Key] = middle + (positions[entry.Key] - middle) * capScale + offset;
            }

            // The moved faces get their own vertices (the walls and the faces left behind keep the old ones).
            var movedVertex = new Dictionary<int, int>();
            int Moved(int vertex)
            {
                if (!movedVertex.TryGetValue(vertex, out int copy))
                    movedVertex[vertex] = copy = CopyVertex(result, result, vertex, target[positionOf[vertex]]);
                return copy;
            }

            // Edges with one picked face on them: the patches' rims, where walls go.
            var edgeCount = new Dictionary<(int, int), int>();
            foreach (var (s, t) in picked)
                for (int k = 0; k < 3; k++)
                {
                    var key = EdgeKey(positionOf[result.Slots[s][t + k]], positionOf[result.Slots[s][t + (k + 1) % 3]]);
                    edgeCount[key] = edgeCount.TryGetValue(key, out int n) ? n + 1 : 1;
                }

            var walls = new List<(int a, int b, Vector3 outward)>();
            foreach (var (s, t) in picked)
            {
                var slot = result.Slots[s];
                var center = (result.Positions[slot[t]] + result.Positions[slot[t + 1]] + result.Positions[slot[t + 2]]) / 3f;
                for (int k = 0; k < 3; k++)
                {
                    int a = slot[t + k], b = slot[t + (k + 1) % 3];
                    if (edgeCount[EdgeKey(positionOf[a], positionOf[b])] == 1)
                        walls.Add((a, b, (result.Positions[a] + result.Positions[b]) * 0.5f - center));
                }
            }
            foreach (var (s, t) in picked)
                for (int k = 0; k < 3; k++)
                    result.Slots[s][t + k] = Moved(result.Slots[s][t + k]);

            // The walls: a quad per rim edge, flat shaded, facing outwards.
            var wallTriangles = result.Slot(wallSlot >= 0 ? wallSlot : 0);
            foreach (var (a, b, outward) in walls)
            {
                Vector3 pa = result.Positions[a], pb = result.Positions[b], qa = target[positionOf[a]], qb = target[positionOf[b]];
                var normal = Vector3.Cross(pb - pa, qa - pa);
                if (Vector3.Dot(normal, outward) < 0f)
                    normal = -normal;
                normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : outward.normalized;
                float width = (pb - pa).magnitude, height = (qa - pa).magnitude;
                int va = result.AddVertex(pa, normal, new Vector2(0f, 0f));
                int vb = result.AddVertex(pb, normal, new Vector2(width, 0f));
                int wb = result.AddVertex(qb, normal, new Vector2(width, height));
                int wa = result.AddVertex(qa, normal, new Vector2(0f, height));
                AddOutward(result, wallTriangles, va, vb, wb, normal);
                AddOutward(result, wallTriangles, va, wb, wa, normal);
            }
            return result;
        }

        private static void AddOutward(MeshData mesh, List<int> into, int a, int b, int c, Vector3 normal)
        {
            var cross = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            if (cross.sqrMagnitude < 1e-14f)
                return;
            if (Vector3.Dot(cross, normal) >= 0f)
                into.AddRange(new[] { a, b, c });
            else
                into.AddRange(new[] { a, c, b });
        }

        private static Vector3 FaceNormal(MeshData mesh, List<int> slot, int t)
        {
            var a = mesh.Positions[slot[t]];
            var n = Vector3.Cross(mesh.Positions[slot[t + 1]] - a, mesh.Positions[slot[t + 2]] - a);
            return n.sqrMagnitude > 1e-14f ? n.normalized : Vector3.zero;
        }

        // Which patch (connected group of picked faces) each picked position is in.
        private static Dictionary<int, int> Patches(MeshData mesh, int[] positionOf, List<(int slot, int t)> picked)
        {
            var parent = new Dictionary<int, int>();
            int Find(int x)
            {
                while (parent[x] != x)
                    x = parent[x] = parent[parent[x]];
                return x;
            }
            foreach (var (s, t) in picked)
                for (int k = 0; k < 3; k++)
                    parent.TryAdd(positionOf[mesh.Slots[s][t + k]], positionOf[mesh.Slots[s][t + k]]);
            foreach (var (s, t) in picked)
            {
                int a = Find(positionOf[mesh.Slots[s][t]]);
                for (int k = 1; k < 3; k++)
                {
                    int b = Find(positionOf[mesh.Slots[s][t + k]]);
                    if (a != b) parent[b] = a;
                }
            }
            var patch = new Dictionary<int, int>();
            foreach (var key in new List<int>(parent.Keys))
                patch[key] = Find(key);
            return patch;
        }

        #endregion
    }
}
