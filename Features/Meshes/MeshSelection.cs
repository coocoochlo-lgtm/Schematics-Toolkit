using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Vertex selection: a weight from 0 to 1 on every vertex (<see cref="MeshData.Selection"/>). Select nodes set it
    /// (by formula, region, facing, slot, what's inside or near other parts); Delete Selected and Join act on it; and
    /// the deformers (Transform, Twist, Bend, Taper, Noise, Spherize, Vertex Function) only move what's selected,
    /// blended by its weight. Pure math: safe on any thread.
    /// </summary>
    public static class MeshSelection
    {
        /// <summary>How a new selection mixes with the one already there.</summary>
        public enum Mode { Replace, Add, Subtract, Intersect }

        /// <summary>Sets every vertex's selection from <paramref name="weight"/> (0 to 1), mixed in by <paramref name="mode"/>.</summary>
        public static void Apply(MeshData mesh, Func<int, float> weight, Mode mode)
        {
            bool had = mesh.HasSelection;
            mesh.FillSelection();
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                float w = Mathf.Clamp01(weight(i));
                float old = had ? mesh.Selection[i] : 0f;
                mesh.Selection[i] = mode switch
                {
                    Mode.Add => Mathf.Max(old, w),
                    Mode.Subtract => Mathf.Min(old, 1f - w),
                    Mode.Intersect => Mathf.Min(old, w),
                    _ => w,
                };
            }
        }

        /// <summary>Selected becomes unselected and the other way round.</summary>
        public static void Invert(MeshData mesh)
        {
            mesh.FillSelection();
            for (int i = 0; i < mesh.VertexCount; i++)
                mesh.Selection[i] = 1f - mesh.Selection[i];
        }

        /// <summary>Nothing selected any more: nodes work on the whole mesh again.</summary>
        public static void Clear(MeshData mesh) => mesh.Selection.Clear();

        /// <summary>
        /// Grows the selection by <paramref name="rings"/> rings of neighbouring vertices (negative: shrinks it), each
        /// ring taking the weight of its strongest neighbour.
        /// </summary>
        public static void Grow(MeshData mesh, int rings)
        {
            if (!mesh.HasSelection || rings == 0)
                return;
            var positionOf = Weld(mesh, out var count);
            var neighbours = Neighbours(mesh, positionOf, count);
            var weight = new float[count];
            for (int i = 0; i < mesh.VertexCount; i++)
                weight[positionOf[i]] = Mathf.Max(weight[positionOf[i]], mesh.Selection[i]);
            bool grow = rings > 0;
            for (int r = 0; r < Mathf.Abs(rings); r++)
            {
                var next = (float[])weight.Clone();
                for (int v = 0; v < count; v++)
                    foreach (int n in neighbours[v])
                        next[v] = grow ? Mathf.Max(next[v], weight[n]) : Mathf.Min(next[v], weight[n]);
                weight = next;
            }
            for (int i = 0; i < mesh.VertexCount; i++)
                mesh.Selection[i] = weight[positionOf[i]];
        }

        /// <summary>
        /// Moves vertices only as far as they're selected: each ends up between where it was (in
        /// <paramref name="before"/>) and where the deformer put it, by its weight. With nothing selected, everything
        /// keeps the deformer's result. The two must have the same vertices in the same order.
        /// </summary>
        public static void Restrict(MeshData deformed, MeshData before)
        {
            if (!before.HasSelection || deformed.VertexCount != before.VertexCount)
                return;
            for (int i = 0; i < deformed.VertexCount; i++)
            {
                float w = before.Selection[i];
                if (w >= 1f)
                    continue;
                deformed.Positions[i] = Vector3.Lerp(before.Positions[i], deformed.Positions[i], w);
                var normal = Vector3.Lerp(before.Normals[i], deformed.Normals[i], w);
                deformed.Normals[i] = normal.sqrMagnitude > 1e-12f ? normal.normalized : before.Normals[i];
            }
        }

        /// <summary>
        /// The bounds of what's selected (half selected or more), or of the whole mesh with nothing selected: what Grab's
        /// 0 to 1 box measures across, so with the head selected, X 0.6 to 0.8 is part of the head, not the body.
        /// </summary>
        public static Bounds SelectedBounds(MeshData mesh)
        {
            if (!mesh.HasSelection)
                return mesh.Bounds;
            var min = Vector3.one * float.MaxValue;
            var max = Vector3.one * float.MinValue;
            for (int i = 0; i < mesh.VertexCount; i++)
                if (mesh.Selection[i] >= 0.5f)
                {
                    min = Vector3.Min(min, mesh.Positions[i]);
                    max = Vector3.Max(max, mesh.Positions[i]);
                }
            if (min.x > max.x)
                return mesh.Bounds;
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>
        /// For each vertex, how far it's inside a box given in the mesh's bounds (0 to 1 on each axis; with a
        /// selection, the selection's bounds): 1 inside, easing to 0 by <paramref name="falloff"/> (0 to 1 of the
        /// bounds) beyond it. Times the selection, if there is one.
        /// </summary>
        public static float[] BoundsWeights(MeshData mesh, Vector3 min, Vector3 max, float falloff)
        {
            var weights = new float[mesh.VertexCount];
            if (mesh.VertexCount == 0)
                return weights;
            var bounds = SelectedBounds(mesh);
            var lo = Vector3.Min(min, max);
            var hi = Vector3.Max(min, max);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i];
                var outside = Vector3.zero;
                for (int axis = 0; axis < 3; axis++)
                {
                    // Where it is across the bounds (a flat mesh is in the middle of its flat side).
                    float size = bounds.size[axis];
                    float t = size > 1e-6f ? (p[axis] - bounds.min[axis]) / size : 0.5f;
                    outside[axis] = Mathf.Max(0f, Mathf.Max(lo[axis] - t, t - hi[axis]));
                }
                float d = outside.magnitude;
                float w = d <= 1e-6f ? 1f : falloff > 0f ? 1f - Mathf.SmoothStep(0f, 1f, d / falloff) : 0f;
                weights[i] = w * (mesh.HasSelection ? mesh.Selection[i] : 1f);
            }
            return weights;
        }

        /// <summary>Runs a deformer on a copy of <paramref name="before"/>, then keeps it to the selection.</summary>
        public static MeshData Deform(MeshData before, Action<MeshData> deform)
        {
            var mesh = before.Clone();
            deform(mesh);
            Restrict(mesh, before);
            return mesh;
        }

        #region Pieces

        // Positions welded (each vertex's welded index), and how many there are.
        private static int[] Weld(MeshData mesh, out int count)
        {
            var welded = new Dictionary<Vector3, int>(new MeshData.PositionComparer());
            var of = new int[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                if (!welded.TryGetValue(mesh.Positions[i], out int index))
                    welded[mesh.Positions[i]] = index = welded.Count;
                of[i] = index;
            }
            count = welded.Count;
            return of;
        }

        private static List<HashSet<int>> Neighbours(MeshData mesh, int[] positionOf, int count)
        {
            var neighbours = new List<HashSet<int>>(count);
            for (int i = 0; i < count; i++)
                neighbours.Add(new HashSet<int>());
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = positionOf[slot[t + k]], b = positionOf[slot[t + (k + 1) % 3]];
                        if (a == b) continue;
                        neighbours[a].Add(b);
                        neighbours[b].Add(a);
                    }
            return neighbours;
        }

        /// <summary>
        /// Which piece each vertex is in: the separate shells of a merged mesh (a head, a neck, an arm), found by which
        /// triangles touch.
        /// </summary>
        public static int[] Pieces(MeshData mesh, out int pieceCount)
        {
            var positionOf = Weld(mesh, out int count);
            var parent = new int[count];
            for (int i = 0; i < count; i++)
                parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x)
                    x = parent[x] = parent[parent[x]];
                return x;
            }
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    int a = Find(positionOf[slot[t]]);
                    for (int k = 1; k < 3; k++)
                    {
                        int b = Find(positionOf[slot[t + k]]);
                        if (a != b) parent[b] = a;
                    }
                }
            var number = new Dictionary<int, int>();
            var piece = new int[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int root = Find(positionOf[i]);
                if (!number.TryGetValue(root, out int n))
                    number[root] = n = number.Count;
                piece[i] = n;
            }
            pieceCount = number.Count;
            return piece;
        }

        // Each piece's triangles (as corner positions) and bounds, for inside tests.
        private sealed class Solid
        {
            public readonly List<(Vector3 a, Vector3 b, Vector3 c)> Triangles = new();
            public Bounds Bounds;
            public bool HasBounds;

            public void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                Triangles.Add((a, b, c));
                var min = Vector3.Min(a, Vector3.Min(b, c));
                var max = Vector3.Max(a, Vector3.Max(b, c));
                if (!HasBounds)
                {
                    Bounds.SetMinMax(min, max);
                    HasBounds = true;
                }
                else
                    Bounds.SetMinMax(Vector3.Min(Bounds.min, min), Vector3.Max(Bounds.max, max));
            }

            // Inside: a ray from the point crosses the surface an odd number of times.
            public bool Contains(Vector3 p)
            {
                if (!HasBounds || p.x < Bounds.min.x || p.y < Bounds.min.y || p.z < Bounds.min.z
                    || p.x > Bounds.max.x || p.y > Bounds.max.y || p.z > Bounds.max.z)
                    return false;
                // (An odd direction, so the ray doesn't run along an edge.)
                var direction = new Vector3(0.5773f, 0.5774f, 0.5776f);
                int hits = 0;
                foreach (var (a, b, c) in Triangles)
                    if (RayHits(p, direction, a, b, c))
                        hits++;
                return (hits & 1) == 1;
            }

            private static bool RayHits(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
            {
                var e1 = b - a;
                var e2 = c - a;
                var h = Vector3.Cross(direction, e2);
                float det = Vector3.Dot(e1, h);
                if (Mathf.Abs(det) < 1e-12f)
                    return false;
                float f = 1f / det;
                var s = origin - a;
                float u = f * Vector3.Dot(s, h);
                if (u < 0f || u > 1f)
                    return false;
                var q = Vector3.Cross(s, e1);
                float v = f * Vector3.Dot(direction, q);
                if (v < 0f || u + v > 1f)
                    return false;
                return f * Vector3.Dot(e2, q) > 1e-6f;
            }
        }

        private static List<Solid> Solids(MeshData mesh, int[] piece, int pieceCount)
        {
            var solids = new List<Solid>(pieceCount);
            for (int i = 0; i < pieceCount; i++)
                solids.Add(new Solid());
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                    solids[piece[slot[t]]].Add(mesh.Positions[slot[t]], mesh.Positions[slot[t + 1]], mesh.Positions[slot[t + 2]]);
            return solids;
        }

        /// <summary>
        /// 1 for vertices inside another piece of the same mesh (a neck's top inside the head, a joint inside a limb):
        /// the hidden parts of overlapping shapes. The pieces must be closed. With <paramref name="other"/>, inside that
        /// mesh instead.
        /// </summary>
        public static float[] Inside(MeshData mesh, MeshData other = null)
        {
            var weights = new float[mesh.VertexCount];
            if (other != null)
            {
                var piece = Pieces(other, out int count);
                var solids = Solids(other, piece, count);
                for (int i = 0; i < mesh.VertexCount; i++)
                    foreach (var solid in solids)
                        if (solid.Contains(mesh.Positions[i]))
                        {
                            weights[i] = 1f;
                            break;
                        }
                return weights;
            }
            var own = Pieces(mesh, out int pieces);
            var ownSolids = Solids(mesh, own, pieces);
            for (int i = 0; i < mesh.VertexCount; i++)
                for (int s = 0; s < ownSolids.Count; s++)
                    if (s != own[i] && ownSolids[s].Contains(mesh.Positions[i]))
                    {
                        weights[i] = 1f;
                        break;
                    }
            return weights;
        }

        /// <summary>
        /// For each vertex, how near it is to another piece of the mesh (or to <paramref name="other"/>'s vertices):
        /// 1 within <paramref name="distance"/>, fading to 0 by <paramref name="distance"/> + <paramref name="falloff"/>.
        /// The places where parts meet.
        /// </summary>
        public static float[] Near(MeshData mesh, float distance, float falloff, MeshData other = null)
        {
            var weights = new float[mesh.VertexCount];
            float reach = Mathf.Max(1e-5f, distance + Mathf.Max(0f, falloff));
            var targets = other ?? mesh;
            int[] targetPiece = other == null ? Pieces(mesh, out _) : null;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / reach), Mathf.FloorToInt(p.y / reach), Mathf.FloorToInt(p.z / reach));
            for (int i = 0; i < targets.VertexCount; i++)
            {
                var key = Cell(targets.Positions[i]);
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<int>();
                list.Add(i);
            }
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i];
                var (cx, cy, cz) = Cell(p);
                float best = float.MaxValue;
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int y = cy - 1; y <= cy + 1; y++)
                        for (int z = cz - 1; z <= cz + 1; z++)
                            if (grid.TryGetValue((x, y, z), out var list))
                                foreach (int j in list)
                                {
                                    if (targetPiece != null && targetPiece[j] == targetPiece[i])
                                        continue;
                                    best = Mathf.Min(best, (targets.Positions[j] - p).sqrMagnitude);
                                }
                float d = Mathf.Sqrt(best);
                weights[i] = d <= distance ? 1f : falloff > 0f ? Mathf.Clamp01(1f - (d - distance) / falloff) : 0f;
            }
            return weights;
        }

        #endregion

        #region Delete and join

        /// <summary>
        /// Removes the selected part of the mesh: triangles with any selected corner (or, with
        /// <paramref name="allCorners"/>, only those with every corner selected), and the vertices nothing uses any more.
        /// "Selected" is a weight of a half or more. Afterwards nothing is selected.
        /// </summary>
        public static MeshData DeleteSelected(MeshData mesh, bool allCorners = false)
        {
            if (!mesh.HasSelection)
                return mesh.Clone();
            var result = new MeshData();
            result.Slots.Clear();
            var remap = new Dictionary<int, int>();
            int Keep(int v)
            {
                if (!remap.TryGetValue(v, out int index))
                {
                    index = result.AddVertex(mesh.Positions[v], mesh.Normals[v], mesh.UVs[v]);
                    if (mesh.HasColors)
                    {
                        result.FillColors();
                        result.Colors[index] = mesh.Colors[v];
                    }
                    result.FillSelection();
                    result.Selection[index] = mesh.Selection[v];
                    remap[v] = index;
                }
                return index;
            }
            for (int s = 0; s < mesh.Slots.Count; s++)
            {
                var slot = mesh.Slots[s];
                var target = result.Slot(s);
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    int selected = 0;
                    for (int k = 0; k < 3; k++)
                        if (mesh.Selection[slot[t + k]] >= 0.5f)
                            selected++;
                    if (allCorners ? selected == 3 : selected > 0)
                        continue;
                    target.Add(Keep(slot[t]));
                    target.Add(Keep(slot[t + 1]));
                    target.Add(Keep(slot[t + 2]));
                }
            }
            // What was selected is gone: nothing's selected now (so the next nodes work on the whole mesh).
            result.Selection.Clear();
            return result;
        }

        /// <summary>
        /// Joins pieces where they meet: each open edge (a rim left where a hidden part was cut away) snaps onto the
        /// nearest surface of another piece within <paramref name="distance"/>, so the cut parts end exactly on their
        /// neighbour with no gap and nothing poking through. Rims that land on the same spot weld together. Only
        /// selected vertices move, if there's a selection. Then normals are worked out again
        /// (<paramref name="smoothAngle"/>; -1 keeps them), so the seam shades smoothly. Best after Delete Selected with
        /// All Corners, which leaves the rims just inside the neighbour. <see cref="JoinMode.Weld"/> instead pairs each cut
        /// edge's corners with the nearest cut-edge corners of another part and welds them where they meet: two holes
        /// with matching rings of corners (a limb's open end and the socket cut for it) become one seamless surface,
        /// edge loops running straight through.
        /// </summary>
        /// <summary>How Join closes seams: snapping onto the other part's surface, or welding matching rims together.</summary>
        public enum JoinMode { Surface, Weld }

        public static MeshData Join(MeshData source, float distance, float smoothAngle = 50f, JoinMode mode = JoinMode.Surface)
        {
            var mesh = source.Clone();
            if (mesh.VertexCount == 0 || distance <= 0f)
                return mesh;
            var positionOf = Weld(mesh, out int count);
            var positions = new Vector3[count];
            for (int i = 0; i < mesh.VertexCount; i++)
                positions[positionOf[i]] = mesh.Positions[i];
            var piece = Pieces(mesh, out _);
            var pieceOf = new int[count];
            for (int i = 0; i < mesh.VertexCount; i++)
                pieceOf[positionOf[i]] = piece[i];

            // Open edges: used by one triangle.
            var edgeUse = new Dictionary<(int, int), int>();
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = positionOf[slot[t + k]], b = positionOf[slot[t + (k + 1) % 3]];
                        if (a == b) continue;
                        var key = a < b ? (a, b) : (b, a);
                        edgeUse[key] = edgeUse.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
            var open = new List<(int a, int b)>();
            var onRim = new bool[count];
            foreach (var entry in edgeUse)
                if (entry.Value == 1)
                {
                    open.Add(entry.Key);
                    onRim[entry.Key.Item1] = onRim[entry.Key.Item2] = true;
                }
            if (open.Count == 0)
                return mesh;

            // Which rim positions may move.
            var movable = new bool[count];
            for (int i = 0; i < mesh.VertexCount; i++)
                if (onRim[positionOf[i]] && (!mesh.HasSelection || mesh.Selection[i] >= 0.5f))
                    movable[positionOf[i]] = true;

            if (mode == JoinMode.Weld)
            {
                WeldRims(mesh, positionOf, positions, onRim, movable, pieceOf, distance);
                if (smoothAngle >= 0f)
                    mesh.RecalculateNormals(smoothAngle);
                return mesh;
            }

            // Other pieces' triangles in a grid, to find the nearest surface. (Rims left by deleting only fully hidden
            // triangles poke just inside the neighbour; pulling them onto its surface closes the seam without a gap or
            // a spike showing through.)
            float cellSize = Mathf.Max(1e-4f, distance);
            var grid = new Dictionary<(int, int, int), List<int>>();
            var triangles = new List<(int a, int b, int c)>();
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    int a = positionOf[slot[t]], b = positionOf[slot[t + 1]], c = positionOf[slot[t + 2]];
                    var lo = Cell(Vector3.Min(positions[a], Vector3.Min(positions[b], positions[c])));
                    var hi = Cell(Vector3.Max(positions[a], Vector3.Max(positions[b], positions[c])));
                    if ((long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4096)
                        continue;
                    int index = triangles.Count;
                    triangles.Add((a, b, c));
                    for (int x = lo.Item1; x <= hi.Item1; x++)
                        for (int y = lo.Item2; y <= hi.Item2; y++)
                            for (int z = lo.Item3; z <= hi.Item3; z++)
                            {
                                if (!grid.TryGetValue((x, y, z), out var list))
                                    grid[(x, y, z)] = list = new List<int>();
                                list.Add(index);
                            }
                }

            // Each movable rim position snaps onto the nearest point of another piece's surface.
            var target = (Vector3[])positions.Clone();
            var joined = new bool[count];
            for (int v = 0; v < count; v++)
            {
                if (!movable[v])
                    continue;
                var p = positions[v];
                var (cx, cy, cz) = Cell(p);
                float best = distance * distance;
                Vector3 nearest = p;
                bool found = false;
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int y = cy - 1; y <= cy + 1; y++)
                        for (int z = cz - 1; z <= cz + 1; z++)
                            if (grid.TryGetValue((x, y, z), out var list))
                                foreach (int t in list)
                                {
                                    var (a, b, c) = triangles[t];
                                    if (pieceOf[a] == pieceOf[v])
                                        continue;
                                    var q = ClosestOnTriangle(p, positions[a], positions[b], positions[c]);
                                    float d = (q - p).sqrMagnitude;
                                    if (d <= best)
                                    {
                                        best = d;
                                        nearest = q;
                                        found = true;
                                    }
                                }
                if (found)
                {
                    target[v] = nearest;
                    joined[v] = true;
                }
            }

            // Snapped rims that landed on top of each other (two cut parts meeting the same spot) weld into one.
            float weld = distance * 0.15f;
            var weldGrid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) WeldCell(Vector3 p) => (Mathf.FloorToInt(p.x / weld), Mathf.FloorToInt(p.y / weld), Mathf.FloorToInt(p.z / weld));
            var cluster = new int[count];
            for (int v = 0; v < count; v++)
                cluster[v] = v;
            for (int v = 0; v < count; v++)
            {
                if (!joined[v])
                    continue;
                var (cx, cy, cz) = WeldCell(target[v]);
                int into = -1;
                for (int x = cx - 1; x <= cx + 1 && into < 0; x++)
                    for (int y = cy - 1; y <= cy + 1 && into < 0; y++)
                        for (int z = cz - 1; z <= cz + 1 && into < 0; z++)
                            if (weldGrid.TryGetValue((x, y, z), out var list))
                                foreach (int other in list)
                                    if ((target[other] - target[v]).sqrMagnitude <= weld * weld)
                                    {
                                        into = other;
                                        break;
                                    }
                if (into >= 0)
                    cluster[v] = into;
                else
                {
                    var key = WeldCell(target[v]);
                    if (!weldGrid.TryGetValue(key, out var list))
                        weldGrid[key] = list = new List<int>();
                    list.Add(v);
                }
            }
            for (int i = 0; i < mesh.VertexCount; i++)
                mesh.Positions[i] = target[cluster[positionOf[i]]];

            if (smoothAngle >= 0f)
                mesh.RecalculateNormals(smoothAngle);
            return mesh;
        }

        // Each movable rim corner pairs with the nearest rim corner of another piece; paired corners meet in the middle.
        private static void WeldRims(MeshData mesh, int[] positionOf, Vector3[] positions, bool[] onRim, bool[] movable, int[] pieceOf, float distance)
        {
            int count = positions.Length;
            float cellSize = Mathf.Max(1e-4f, distance);
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));
            var grid = new Dictionary<(int, int, int), List<int>>();
            for (int v = 0; v < count; v++)
                if (onRim[v])
                {
                    var key = Cell(positions[v]);
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new List<int>();
                    list.Add(v);
                }

            var parent = new int[count];
            for (int v = 0; v < count; v++)
                parent[v] = v;
            int Root(int v)
            {
                while (parent[v] != v)
                    v = parent[v] = parent[parent[v]];
                return v;
            }
            for (int v = 0; v < count; v++)
            {
                if (!movable[v])
                    continue;
                var (cx, cy, cz) = Cell(positions[v]);
                float best = distance * distance;
                int nearest = -1;
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int y = cy - 1; y <= cy + 1; y++)
                        for (int z = cz - 1; z <= cz + 1; z++)
                            if (grid.TryGetValue((x, y, z), out var list))
                                foreach (int u in list)
                                {
                                    if (pieceOf[u] == pieceOf[v])
                                        continue;
                                    float d = (positions[u] - positions[v]).sqrMagnitude;
                                    if (d <= best)
                                    {
                                        best = d;
                                        nearest = u;
                                    }
                                }
                if (nearest >= 0)
                    parent[Root(v)] = Root(nearest);
            }

            var sum = new Vector3[count];
            var members = new int[count];
            for (int v = 0; v < count; v++)
            {
                int r = Root(v);
                sum[r] += positions[v];
                members[r]++;
            }
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int r = Root(positionOf[i]);
                if (members[r] > 1)
                    mesh.Positions[i] = sum[r] / members[r];
            }
        }

        // Closest point on a triangle (Ericson, Real-Time Collision Detection 5.1.5).
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            var ab = b - a;
            var ac = c - a;
            var ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            var bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            var cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        #endregion
    }
}
