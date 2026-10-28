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
            if (from.HasSelection)
            {
                to.FillSelection();
                to.Selection[index] = Mathf.Lerp(from.Selection[a], from.Selection[b], t);
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
            if (from.HasSelection)
            {
                to.FillSelection();
                to.Selection[copy] = from.Selection[index];
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

        /// <summary>
        /// Catmull-Clark subdivision, the box modeller's kind: works on quads (pairs of triangles making one, as boxes,
        /// grids and loop cuts give), splitting each into four and rounding the shape off, so a cut-up cube becomes a
        /// smooth, even body with its edge loops kept. Triangles that aren't half of a quad split into three quads.
        /// </summary>
        /// <summary>How Subdivide rounds: Loop (for triangle meshes) or Catmull-Clark (for quads: boxes, cut-up cubes).</summary>
        public enum SubdivideMethod { Loop, CatmullClark }

        public static MeshData SubdivideQuads(MeshData mesh, int levels)
        {
            levels = Mathf.Clamp(levels, 0, 5);
            if (levels == 0 || mesh.VertexCount == 0)
                return mesh.Clone();

            // Kept as polygons between levels (so the next level finds the same quads), made triangles at the end.
            var vertices = mesh.Clone();
            var polygons = new List<(int[] corners, int slot)>();
            for (int s = 0; s < mesh.Slots.Count; s++)
                // Quads bent a little (by Relax, Grab, Noise) are still quads to subdivide.
                foreach (var polygon in Polygons(mesh, mesh.Slots[s], 0.7f))
                    polygons.Add((polygon, s));
            for (int level = 0; level < levels; level++)
                (vertices, polygons) = CatmullClark(vertices, polygons);

            var result = vertices;
            foreach (var slot in result.Slots)
                slot.Clear();
            foreach (var (corners, slot) in polygons)
                Fan(corners, result.Slot(slot));
            // The same point made once per face it's on: one vertex each again (where they match).
            MeshModifiers.Weld(result);
            return result;
        }

        private static (MeshData, List<(int[] corners, int slot)>) CatmullClark(MeshData mesh, List<(int[] corners, int slot)> polygons)
        {
            var positionOf = Weld(mesh, out var positions);
            int count = positions.Count;

            // Each face's middle, and each edge's faces.
            var facePoints = new Vector3[polygons.Count];
            var edgeFaces = new Dictionary<(int, int), List<int>>();
            for (int f = 0; f < polygons.Count; f++)
            {
                var corners = polygons[f].corners;
                var sum = Vector3.zero;
                foreach (int v in corners)
                    sum += positions[positionOf[v]];
                facePoints[f] = sum / corners.Length;
                for (int k = 0; k < corners.Length; k++)
                {
                    var key = EdgeKey(positionOf[corners[k]], positionOf[corners[(k + 1) % corners.Length]]);
                    if (key.Item1 == key.Item2)
                        continue;
                    if (!edgeFaces.TryGetValue(key, out var list))
                        edgeFaces[key] = list = new List<int>(2);
                    list.Add(f);
                }
            }

            // Edge points: between the ends and the two faces' middles; on an open edge, its middle.
            var edgePoints = new Dictionary<(int, int), Vector3>(edgeFaces.Count);
            var faceSum = new Vector3[count];
            var faceCount = new int[count];
            var edgeSum = new Vector3[count];
            var edgeCount = new int[count];
            var boundarySum = new Vector3[count];
            var boundaryCount = new int[count];
            foreach (var entry in edgeFaces)
            {
                var (a, b) = entry.Key;
                var middle = (positions[a] + positions[b]) * 0.5f;
                bool open = entry.Value.Count != 2;
                edgePoints[entry.Key] = open ? middle : (positions[a] + positions[b] + facePoints[entry.Value[0]] + facePoints[entry.Value[1]]) * 0.25f;
                edgeSum[a] += middle; edgeCount[a]++;
                edgeSum[b] += middle; edgeCount[b]++;
                if (open)
                {
                    boundarySum[a] += positions[b]; boundaryCount[a]++;
                    boundarySum[b] += positions[a]; boundaryCount[b]++;
                }
            }
            for (int f = 0; f < polygons.Count; f++)
            {
                var seen = new HashSet<int>();
                foreach (int v in polygons[f].corners)
                    if (seen.Add(positionOf[v]))
                    {
                        faceSum[positionOf[v]] += facePoints[f];
                        faceCount[positionOf[v]]++;
                    }
            }

            // Corners move towards the average of their faces and edges (open edges stay along themselves).
            var moved = new Vector3[count];
            for (int v = 0; v < count; v++)
            {
                if (boundaryCount[v] == 2)
                    moved[v] = positions[v] * 0.75f + boundarySum[v] * 0.125f;
                else if (boundaryCount[v] > 0 || edgeCount[v] < 3 || faceCount[v] == 0)
                    moved[v] = positions[v];
                else
                {
                    float n = edgeCount[v];
                    var F = faceSum[v] / faceCount[v];
                    var R = edgeSum[v] / edgeCount[v];
                    moved[v] = (F + 2f * R + (n - 3f) * positions[v]) / n;
                }
            }

            // Each face becomes a quad per corner: the corner, its next edge's point, the face's middle, its last
            // edge's point. Corners and edge points are shared across a face (UVs and colours blended from its corners).
            var result = new MeshData();
            result.Slots.Clear();
            var next = new List<(int[] corners, int slot)>(polygons.Count * 4);
            var cornerCopies = new Dictionary<int, int>();
            for (int f = 0; f < polygons.Count; f++)
            {
                var (corners, slot) = polygons[f];
                int n = corners.Length;
                var mids = new int[n];
                for (int k = 0; k < n; k++)
                {
                    int a = corners[k], b = corners[(k + 1) % n];
                    var key = EdgeKey(positionOf[a], positionOf[b]);
                    var point = edgePoints.TryGetValue(key, out var p) ? p : (mesh.Positions[a] + mesh.Positions[b]) * 0.5f;
                    mids[k] = Blend(mesh, result, a, b, 0.5f, point);
                }
                int centre = Blend(mesh, result, corners[0], corners[n / 2], 0.5f, facePoints[f]);
                for (int k = 0; k < n; k++)
                {
                    int v = corners[k];
                    if (!cornerCopies.TryGetValue(v, out int copy))
                        cornerCopies[v] = copy = CopyVertex(mesh, result, v, moved[positionOf[v]]);
                    next.Add((new[] { copy, mids[k], centre, mids[(k + n - 1) % n] }, slot));
                }
            }
            return (result, next);
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
        /// With a selection, the sphere is around the selection's middle and its size (the node then moves only what's
        /// selected): a head rounded on a whole body.
        /// </summary>
        public static MeshData Spherize(MeshData mesh, float amount, float radius = 0f)
        {
            var result = mesh.Clone();
            if (result.VertexCount == 0)
                return result;
            // Around the middle of the selection, if there is one (a head on a body), else of the whole mesh.
            var center = result.Bounds.center;
            float total = 0f;
            if (result.HasSelection)
            {
                var sum = Vector3.zero;
                for (int i = 0; i < result.VertexCount; i++)
                {
                    sum += result.Positions[i] * result.Selection[i];
                    total += result.Selection[i];
                }
                if (total > 1e-5f)
                    center = sum / total;
            }
            if (radius <= 0f)
            {
                float sum = 0f, weights = 0f;
                for (int i = 0; i < result.VertexCount; i++)
                {
                    float w = total > 1e-5f ? result.Selection[i] : 1f;
                    sum += (result.Positions[i] - center).magnitude * w;
                    weights += w;
                }
                radius = sum / Mathf.Max(1e-5f, weights);
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
            if (mesh.HasSelection)
                result.Selection.AddRange(mesh.Selection);
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
                var target = result.Slot(s);
                var front = new List<int>(6);
                var back = new List<int>(6);
                // Cut as quads where two triangles make one (a box's faces, a grid): cutting the triangles alone would
                // also cut each quad's hidden diagonal, leaving stray points along the new loop.
                foreach (var polygon in Polygons(mesh, mesh.Slots[s]))
                {
                    int n = polygon.Length;
                    bool above = false, below = false;
                    for (int k = 0; k < n; k++)
                    {
                        float dk = Side(polygon[k]);
                        above |= dk > 0f;
                        below |= dk < 0f;
                    }
                    if (!(above && below))
                    {
                        Fan(polygon, target);
                        continue;
                    }
                    front.Clear();
                    back.Clear();
                    for (int k = 0; k < n; k++)
                    {
                        int va = polygon[k], vb = polygon[(k + 1) % n];
                        float da = Side(va), db = Side(vb);
                        if (da >= 0) front.Add(va);
                        if (da <= 0) back.Add(va);
                        if ((da > 0 && db < 0) || (da < 0 && db > 0))
                        {
                            int cut = Cut(va, vb);
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

        /// <summary>
        /// A slot's triangles as polygons: pairs of triangles sharing their longest edge, facing within
        /// <paramref name="flatness"/> (the dot of their normals) of each other, become quads (in order round the edge);
        /// the rest stay triangles.
        /// </summary>
        private static List<int[]> Polygons(MeshData mesh, List<int> slot, float flatness = 0.9999f)
        {
            int count = slot.Count / 3;
            var longest = new (int a, int b)[count];
            var byEdge = new Dictionary<(int, int), int>();
            for (int t = 0; t < count; t++)
            {
                float best = -1f;
                for (int k = 0; k < 3; k++)
                {
                    int a = slot[t * 3 + k], b = slot[t * 3 + (k + 1) % 3];
                    float length = (mesh.Positions[a] - mesh.Positions[b]).sqrMagnitude;
                    if (length > best)
                    {
                        best = length;
                        longest[t] = (a, b);
                    }
                }
                byEdge[longest[t]] = t;
            }
            Vector3 FaceNormal(int t)
            {
                var p0 = mesh.Positions[slot[t * 3]];
                return Vector3.Cross(mesh.Positions[slot[t * 3 + 1]] - p0, mesh.Positions[slot[t * 3 + 2]] - p0).normalized;
            }
            int Opposite(int t, int a, int b)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = slot[t * 3 + k];
                    if (v != a && v != b) return v;
                }
                return a;
            }

            var paired = new bool[count];
            var polygons = new List<int[]>(count);
            for (int t = 0; t < count; t++)
            {
                if (paired[t])
                    continue;
                var (a, b) = longest[t];
                if (byEdge.TryGetValue((b, a), out int other) && other != t && !paired[other]
                    && Vector3.Dot(FaceNormal(t), FaceNormal(other)) > flatness)
                {
                    paired[t] = paired[other] = true;
                    // Triangle a, b, c and its partner b, a, d: round the quad, a, d, b, c.
                    polygons.Add(new[] { a, Opposite(other, a, b), b, Opposite(t, a, b) });
                    continue;
                }
                paired[t] = true;
                polygons.Add(new[] { slot[t * 3], slot[t * 3 + 1], slot[t * 3 + 2] });
            }
            return polygons;
        }

        private static int Compare(Vector3 a, Vector3 b)
        {
            int c = a.x.CompareTo(b.x);
            if (c != 0) return c;
            c = a.y.CompareTo(b.y);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        private static void Fan(IReadOnlyList<int> polygon, List<int> into)
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

        public enum ExtrudeSelection { FacingDirection, All, MaterialSlot, Selected }

        /// <summary>
        /// Pulls faces out of the mesh, with walls joining them to where they were: the top of a box pulled up into a
        /// tower, a plane's edge into a wall. Faces are picked by which way they face (within
        /// <paramref name="angle"/> degrees of <paramref name="direction"/>), all of them, a material slot, or the
        /// vertex selection (faces with every corner selected). They move
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
                        ExtrudeSelection.Selected => result.Selected(slot[t]) >= 0.5f && result.Selected(slot[t + 1]) >= 0.5f && result.Selected(slot[t + 2]) >= 0.5f,
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
