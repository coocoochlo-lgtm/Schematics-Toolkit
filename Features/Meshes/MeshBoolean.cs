using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Boolean operations on closed meshes: Union (both as one shell, the insides gone), Subtract (A with B carved out
    /// of it: windows in walls, a bite out of an apple) and Intersect (only where they overlap). Built on BSP trees
    /// (after Evan Wallace's csg.js), made without recursion so big meshes can't overflow the stack. The meshes should
    /// be closed (no holes) for the inside and outside to make sense. Pure math: safe on any thread.
    /// </summary>
    public static class MeshBoolean
    {
        public enum Operation { Union, Subtract, Intersect }

        private const float Epsilon = 1e-5f;

        /// <summary>
        /// A combined with B. Each part keeps its material slots; the carved surfaces get B's. Afterwards, points
        /// closer than <paramref name="mergeDistance"/> merge (0: off), clearing the slivers and near-duplicate points
        /// left along the cut.
        /// </summary>
        public static MeshData Apply(MeshData a, MeshData b, Operation operation, float mergeDistance = 0f)
        {
            if (a == null || a.TriangleCount == 0)
                return operation == Operation.Union && b != null ? b.Clone() : new MeshData();
            if (b == null || b.TriangleCount == 0)
                return operation == Operation.Intersect ? new MeshData() : a.Clone();

            var na = BspNode.Build(Polygons(a));
            var nb = BspNode.Build(Polygons(b));
            var ba = a.Bounds;
            var bb = b.Bounds;
            switch (operation)
            {
                case Operation.Union:
                    na.ClipTo(nb, bb);
                    nb.ClipTo(na, ba);
                    nb.Invert();
                    nb.ClipTo(na, ba);
                    nb.Invert();
                    na.AddPolygons(nb.AllPolygons());
                    break;
                case Operation.Subtract:
                    na.Invert();
                    na.ClipTo(nb, bb);
                    nb.ClipTo(na, ba);
                    nb.Invert();
                    nb.ClipTo(na, ba);
                    nb.Invert();
                    na.AddPolygons(nb.AllPolygons());
                    na.Invert();
                    break;
                default:
                    na.Invert();
                    nb.ClipTo(na, ba);
                    nb.Invert();
                    na.ClipTo(nb, bb);
                    nb.ClipTo(na, ba);
                    na.AddPolygons(nb.AllPolygons());
                    na.Invert();
                    break;
            }
            var result = ToMesh(na.AllPolygons(), a.HasColors || b.HasColors, a.HasSelection || b.HasSelection);
            MeshModifiers.Weld(result);
            if (mergeDistance > 0f)
            {
                MeshModifiers.MergeByDistance(result, mergeDistance, false);
                MeshModifiers.Weld(result);
            }
            return result;
        }

        #region Polygons

        private struct Vertex
        {
            public Vector3 Position, Normal;
            public Vector2 UV;
            public Color Color;
            public float Selected;

            public Vertex Lerp(Vertex other, float t) => new()
            {
                Selected = Mathf.Lerp(Selected, other.Selected, t),
                Position = Vector3.Lerp(Position, other.Position, t),
                Normal = Vector3.Lerp(Normal, other.Normal, t),
                UV = UV + (other.UV - UV) * t,
                Color = Color.Lerp(Color, other.Color, t),
            };
        }

        private struct Plane
        {
            public Vector3 Normal;
            public float W;

            public static bool TryFromPoints(Vector3 a, Vector3 b, Vector3 c, out Plane plane)
            {
                var n = Vector3.Cross(b - a, c - a);
                plane = default;
                // Slivers (three points nearly in a line) have no trustworthy facing; one pointing the wrong way
                // would make the tree think everything behind it is inside. They cover no area, so they're dropped.
                float longest = Mathf.Max((b - a).sqrMagnitude, Mathf.Max((c - b).sqrMagnitude, (a - c).sqrMagnitude));
                if (n.magnitude <= longest * 1e-4f || n.sqrMagnitude < 1e-20f)
                    return false;
                n = MeshData.Direction(n);
                plane = new Plane { Normal = n, W = Vector3.Dot(n, a) };
                return true;
            }

            public Plane Flipped() => new() { Normal = -Normal, W = -W };
        }

        private sealed class Polygon
        {
            public List<Vertex> Vertices;
            public Plane Plane;
            public int Slot;

            public void Flip()
            {
                Vertices.Reverse();
                for (int i = 0; i < Vertices.Count; i++)
                {
                    var v = Vertices[i];
                    v.Normal = -v.Normal;
                    Vertices[i] = v;
                }
                Plane = Plane.Flipped();
            }

            public Vector3 Centre
            {
                get
                {
                    var sum = Vector3.zero;
                    foreach (var v in Vertices)
                        sum += v.Position;
                    return sum / Vertices.Count;
                }
            }

            public bool Overlaps(Bounds bounds)
            {
                var min = Vertices[0].Position;
                var max = min;
                for (int i = 1; i < Vertices.Count; i++)
                {
                    min = Vector3.Min(min, Vertices[i].Position);
                    max = Vector3.Max(max, Vertices[i].Position);
                }
                var bmin = bounds.min;
                var bmax = bounds.max;
                return min.x <= bmax.x && max.x >= bmin.x && min.y <= bmax.y && max.y >= bmin.y && min.z <= bmax.z && max.z >= bmin.z;
            }
        }

        private static List<Polygon> Polygons(MeshData mesh)
        {
            var list = new List<Polygon>();
            bool colors = mesh.HasColors;
            for (int s = 0; s < mesh.Slots.Count; s++)
            {
                var slot = mesh.Slots[s];
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    var verts = new List<Vertex>(3);
                    for (int k = 0; k < 3; k++)
                    {
                        int i = slot[t + k];
                        verts.Add(new Vertex { Position = mesh.Positions[i], Normal = mesh.Normals[i], UV = mesh.UVs[i], Color = colors ? mesh.Colors[i] : Color.white, Selected = mesh.Selected(i) });
                    }
                    if (Plane.TryFromPoints(verts[0].Position, verts[1].Position, verts[2].Position, out var plane))
                        list.Add(new Polygon { Vertices = verts, Plane = plane, Slot = s });
                }
            }
            return list;
        }

        private static MeshData ToMesh(List<Polygon> polygons, bool colors, bool selection)
        {
            RepairTJunctions(polygons);
            var mesh = new MeshData();
            foreach (var polygon in polygons)
            {
                int first = mesh.VertexCount;
                foreach (var v in polygon.Vertices)
                {
                    int index = mesh.AddVertex(v.Position, v.Normal.sqrMagnitude > 1e-12f ? v.Normal.normalized : polygon.Plane.Normal, v.UV);
                    if (colors)
                    {
                        mesh.FillColors();
                        mesh.Colors[index] = v.Color;
                    }
                    if (selection)
                    {
                        mesh.FillSelection();
                        mesh.Selection[index] = v.Selected;
                    }
                }
                int count = polygon.Vertices.Count;
                bool inLine = false;
                for (int k = 0; k < count && !inLine; k++)
                    inLine = Collinear(polygon.Vertices[(k + count - 1) % count].Position, polygon.Vertices[k].Position, polygon.Vertices[(k + 1) % count].Position);
                if (!inLine)
                {
                    for (int i = 1; i + 1 < count; i++)
                        mesh.AddTriangle(first, first + i, first + i + 1, polygon.Slot);
                    continue;
                }
                // Corners added along its edges (T-junction repair) are in line with their neighbours: a fan from a
                // corner would make flat slivers there and leave gaps, so it's fanned from its middle instead.
                var middle = polygon.Vertices[0];
                var sum = Vector3.zero;
                var normal = Vector3.zero;
                var uv = Vector2.zero;
                foreach (var v in polygon.Vertices)
                {
                    sum += v.Position;
                    normal += v.Normal;
                    uv += v.UV;
                }
                int center = mesh.AddVertex(sum / count, normal.sqrMagnitude > 1e-12f ? normal.normalized : polygon.Plane.Normal, uv * (1f / count));
                if (colors)
                {
                    mesh.FillColors();
                    mesh.Colors[center] = middle.Color;
                }
                if (selection)
                {
                    mesh.FillSelection();
                    mesh.Selection[center] = middle.Selected;
                }
                for (int i = 0; i < count; i++)
                {
                    int a = first + i, b = first + (i + 1) % count;
                    if (!Collinear(mesh.Positions[center], mesh.Positions[a], mesh.Positions[b]))
                        mesh.AddTriangle(center, a, b, polygon.Slot);
                }
            }
            return mesh;
        }

        private static bool Collinear(Vector3 a, Vector3 b, Vector3 c)
            => Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-14f;

        /// <summary>
        /// Cutting leaves corners in the middle of neighbouring polygons' edges (T-junctions), which show as hairline
        /// cracks: each such corner is added to the edge it sits on, so neighbours share their corners again.
        /// </summary>
        private static void RepairTJunctions(List<Polygon> polygons)
        {
            // Every distinct corner, in a grid of cells for finding the ones near an edge.
            var corners = new List<Vector3>();
            var seen = new HashSet<Vector3>(new MeshData.PositionComparer());
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = -min;
            foreach (var polygon in polygons)
                foreach (var v in polygon.Vertices)
                    if (seen.Add(v.Position))
                    {
                        corners.Add(v.Position);
                        min = Vector3.Min(min, v.Position);
                        max = Vector3.Max(max, v.Position);
                    }
            if (corners.Count == 0)
                return;
            var size = max - min;
            float cell = Mathf.Max(1e-3f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / Mathf.Max(1f, Mathf.Pow(corners.Count, 1f / 3f)));
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt((p.x - min.x) / cell), Mathf.FloorToInt((p.y - min.y) / cell), Mathf.FloorToInt((p.z - min.z) / cell));
            for (int i = 0; i < corners.Count; i++)
            {
                var key = Cell(corners[i]);
                if (!grid.TryGetValue(key, out var list))
                    grid[key] = list = new List<int>();
                list.Add(i);
            }

            var found = new List<(float t, Vector3 p)>();
            foreach (var polygon in polygons)
            {
                var vertices = polygon.Vertices;
                var repaired = new List<Vertex>(vertices.Count + 2);
                bool changed = false;
                for (int e = 0; e < vertices.Count; e++)
                {
                    var a = vertices[e];
                    var b = vertices[(e + 1) % vertices.Count];
                    repaired.Add(a);
                    var ab = b.Position - a.Position;
                    float length2 = ab.sqrMagnitude;
                    if (length2 < 1e-12f)
                        continue;
                    found.Clear();
                    var lo = Cell(Vector3.Min(a.Position, b.Position));
                    var hi = Cell(Vector3.Max(a.Position, b.Position));
                    // (Long edges cross many cells: only ones up to a sensible count are searched.)
                    if ((long)(hi.Item1 - lo.Item1 + 1) * (hi.Item2 - lo.Item2 + 1) * (hi.Item3 - lo.Item3 + 1) > 4096)
                        continue;
                    for (int x = lo.Item1; x <= hi.Item1; x++)
                        for (int y = lo.Item2; y <= hi.Item2; y++)
                            for (int z = lo.Item3; z <= hi.Item3; z++)
                                if (grid.TryGetValue((x, y, z), out var list))
                                    foreach (int i in list)
                                    {
                                        var p = corners[i];
                                        float t = Vector3.Dot(p - a.Position, ab) / length2;
                                        if (t <= 1e-4f || t >= 1f - 1e-4f)
                                            continue;
                                        if ((a.Position + ab * t - p).sqrMagnitude < 1e-10f)
                                            found.Add((t, p));
                                    }
                    if (found.Count == 0)
                        continue;
                    found.Sort((x, y) => x.t.CompareTo(y.t));
                    foreach (var (t, p) in found)
                    {
                        var v = a.Lerp(b, t);
                        v.Position = p;
                        repaired.Add(v);
                    }
                    changed = true;
                }
                if (changed)
                    polygon.Vertices = repaired;
            }
        }

        private const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

        // Puts a polygon in the right list(s) for a plane, cutting it in two if it spans it.
        private static void Split(Plane plane, Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack, List<Polygon> front, List<Polygon> back)
        {
            int polygonType = 0;
            var types = new int[polygon.Vertices.Count];
            for (int i = 0; i < polygon.Vertices.Count; i++)
            {
                float t = Vector3.Dot(plane.Normal, polygon.Vertices[i].Position) - plane.W;
                int type = t < -Epsilon ? Back : t > Epsilon ? Front : Coplanar;
                polygonType |= type;
                types[i] = type;
            }
            switch (polygonType)
            {
                case Coplanar:
                    (Vector3.Dot(plane.Normal, polygon.Plane.Normal) > 0 ? coplanarFront : coplanarBack).Add(polygon);
                    break;
                case Front:
                    front.Add(polygon);
                    break;
                case Back:
                    back.Add(polygon);
                    break;
                default:
                    var f = new List<Vertex>();
                    var b = new List<Vertex>();
                    int count = polygon.Vertices.Count;
                    for (int i = 0; i < count; i++)
                    {
                        int j = (i + 1) % count;
                        int ti = types[i], tj = types[j];
                        var vi = polygon.Vertices[i];
                        var vj = polygon.Vertices[j];
                        if (ti != Back) f.Add(vi);
                        if (ti != Front) b.Add(vi);
                        if ((ti | tj) == Spanning)
                        {
                            float t = (plane.W - Vector3.Dot(plane.Normal, vi.Position)) / Vector3.Dot(plane.Normal, vj.Position - vi.Position);
                            var v = vi.Lerp(vj, t);
                            f.Add(v);
                            b.Add(v);
                        }
                    }
                    if (f.Count >= 3) front.Add(new Polygon { Vertices = f, Plane = polygon.Plane, Slot = polygon.Slot });
                    if (b.Count >= 3) back.Add(new Polygon { Vertices = b, Plane = polygon.Plane, Slot = polygon.Slot });
                    break;
            }
        }

        #endregion

        #region BSP tree

        private sealed class BspNode
        {
            public Plane Plane;
            public bool HasPlane;
            public BspNode FrontNode, BackNode;
            public readonly List<Polygon> Polygons = new();

            public static BspNode Build(List<Polygon> polygons)
            {
                var node = new BspNode();
                node.AddPolygons(polygons);
                return node;
            }

            // Every node, root first (the tree walked without recursion).
            private List<BspNode> Nodes()
            {
                var list = new List<BspNode>();
                var stack = new Stack<BspNode>();
                stack.Push(this);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    list.Add(node);
                    if (node.FrontNode != null) stack.Push(node.FrontNode);
                    if (node.BackNode != null) stack.Push(node.BackNode);
                }
                return list;
            }

            /// <summary>Inside becomes outside.</summary>
            public void Invert()
            {
                foreach (var node in Nodes())
                {
                    foreach (var polygon in node.Polygons)
                        polygon.Flip();
                    if (node.HasPlane)
                        node.Plane = node.Plane.Flipped();
                    (node.FrontNode, node.BackNode) = (node.BackNode, node.FrontNode);
                }
            }

            /// <summary>The parts of <paramref name="polygons"/> outside this tree's solid.</summary>
            public List<Polygon> Clip(List<Polygon> polygons)
            {
                var kept = new List<Polygon>();
                var work = new Stack<(BspNode node, List<Polygon> polygons)>();
                work.Push((this, polygons));
                while (work.Count > 0)
                {
                    var (node, list) = work.Pop();
                    if (!node.HasPlane)
                    {
                        kept.AddRange(list);
                        continue;
                    }
                    var front = new List<Polygon>();
                    var back = new List<Polygon>();
                    foreach (var polygon in list)
                        Split(node.Plane, polygon, front, back, front, back);
                    if (node.FrontNode != null) work.Push((node.FrontNode, front));
                    else kept.AddRange(front);
                    // Behind a leaf is inside: dropped.
                    if (node.BackNode != null) work.Push((node.BackNode, back));
                }
                return kept;
            }

            /// <summary>
            /// Removes the parts of this tree's polygons inside <paramref name="other"/>. Polygons nowhere near
            /// <paramref name="otherBounds"/> are wholly in or out, so they're kept or dropped whole: cutting them by
            /// every plane of the other solid (planes run on forever) would only chop the far side of the mesh into
            /// slivers for nothing.
            /// </summary>
            public void ClipTo(BspNode other, Bounds otherBounds)
            {
                otherBounds.Expand(Epsilon * 20f);
                foreach (var node in Nodes())
                {
                    var near = new List<Polygon>();
                    var whole = new List<Polygon>();
                    foreach (var polygon in node.Polygons)
                    {
                        if (!polygon.Overlaps(otherBounds) && other.Outside(polygon.Centre, out bool outside))
                        {
                            if (outside)
                                whole.Add(polygon);
                            continue;
                        }
                        near.Add(polygon);
                    }
                    var clipped = near.Count > 0 ? other.Clip(near) : near;
                    node.Polygons.Clear();
                    node.Polygons.AddRange(whole);
                    node.Polygons.AddRange(clipped);
                }
            }

            // Whether a point is outside this tree's solid: false when it lies on a plane and can't be told cheaply.
            private bool Outside(Vector3 point, out bool outside)
            {
                var node = this;
                while (true)
                {
                    if (!node.HasPlane)
                    {
                        outside = true;
                        return true;
                    }
                    float d = Vector3.Dot(node.Plane.Normal, point) - node.Plane.W;
                    if (Mathf.Abs(d) <= Epsilon * 10f)
                    {
                        outside = false;
                        return false;
                    }
                    var next = d > 0f ? node.FrontNode : node.BackNode;
                    if (next == null)
                    {
                        // Past a leaf: in front is outside, behind is inside.
                        outside = d > 0f;
                        return true;
                    }
                    node = next;
                }
            }

            public List<Polygon> AllPolygons()
            {
                var all = new List<Polygon>();
                foreach (var node in Nodes())
                    all.AddRange(node.Polygons);
                return all;
            }

            // A good plane to split polygons by: of a few candidates, the one cutting fewest of a sample and leaving the
            // two sides most even (the first polygon's plane, as csg.js uses, builds lopsided trees: seconds, not
            // milliseconds, for a few thousand triangles).
            private static Plane ChooseSplitter(List<Polygon> list)
            {
                if (list.Count <= 8)
                    return list[0].Plane;
                int candidates = Math.Min(12, list.Count);
                int samples = Math.Min(96, list.Count);
                Plane best = list[0].Plane;
                int bestScore = int.MaxValue;
                for (int c = 0; c < candidates; c++)
                {
                    var plane = list[c * list.Count / candidates].Plane;
                    int front = 0, back = 0, spans = 0;
                    for (int k = 0; k < samples; k++)
                    {
                        var polygon = list[k * list.Count / samples];
                        int type = 0;
                        foreach (var v in polygon.Vertices)
                        {
                            float t = Vector3.Dot(plane.Normal, v.Position) - plane.W;
                            type |= t < -Epsilon ? Back : t > Epsilon ? Front : Coplanar;
                        }
                        if (type == Spanning) spans++;
                        else if (type == Front) front++;
                        else if (type == Back) back++;
                    }
                    int score = spans * 6 + Math.Abs(front - back);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = plane;
                    }
                }
                return best;
            }

            /// <summary>Adds polygons to the tree, splitting them by its planes (and growing it).</summary>
            public void AddPolygons(List<Polygon> polygons)
            {
                var work = new Stack<(BspNode node, List<Polygon> polygons)>();
                work.Push((this, polygons));
                while (work.Count > 0)
                {
                    var (node, list) = work.Pop();
                    if (list.Count == 0)
                        continue;
                    if (!node.HasPlane)
                    {
                        node.Plane = ChooseSplitter(list);
                        node.HasPlane = true;
                    }
                    var front = new List<Polygon>();
                    var back = new List<Polygon>();
                    foreach (var polygon in list)
                        Split(node.Plane, polygon, node.Polygons, node.Polygons, front, back);
                    if (front.Count > 0)
                        work.Push((node.FrontNode ??= new BspNode(), front));
                    if (back.Count > 0)
                        work.Push((node.BackNode ??= new BspNode(), back));
                }
            }
        }

        #endregion
    }
}
