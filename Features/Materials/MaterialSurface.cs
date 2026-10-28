using Remedy.Meshes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>
    /// Where each texel of a texture lands on a mesh: its place (in the mesh's own space), the way the surface faces
    /// there, and which triangle it's on. Laid out by the mesh's UVs. Everything mesh-aware in a Smart Material works
    /// from it: noise in 3D (so it runs straight across UV seams), ambient occlusion, edges and cavities, thickness, and
    /// brush strokes painted onto the model. Its bakes are worked out once and kept. Any thread.
    /// </summary>
    public sealed class MaterialSurface
    {
        public readonly int Width, Height;
        /// <summary>Each texel's place on the mesh (its own space, meters).</summary>
        public readonly Vector3[] Position;
        /// <summary>Which way the surface faces there (smoothed like the mesh's shading).</summary>
        public readonly Vector3[] Normal;
        /// <summary>The triangle each texel is on (-1: none: outside every UV island).</summary>
        public readonly int[] Triangle;
        /// <summary>Where in its triangle each texel is (the second and third corners' shares).</summary>
        public readonly Vector2[] Barycentric;
        /// <summary>The mesh's bounds.</summary>
        public readonly Bounds Bounds;
        /// <summary>No mesh: a flat 1 m square, made to tile (its noise wraps round at the edges).</summary>
        public readonly bool Tiling;
        /// <summary>The mesh's vertices and triangles (one list of 3 corners each), and the triangles' faces.</summary>
        public readonly Vector3[] Vertices;
        public readonly int[] Triangles;
        public readonly Vector3[] FaceNormals;
        /// <summary>How far one texel step goes on the mesh along U (x) and V (y), per triangle (meters).</summary>
        public readonly Vector2[] TexelLength;

        private readonly Dictionary<string, object> _bakes = new();
        private MeshBvh _bvh;
        private int[] _placeOf;
        private Vector3[] _placeNormal;
        private List<Edge> _edges;

        /// <summary>The size of the mesh, corner to corner (meters).</summary>
        public float Size => Mathf.Max(1e-4f, Bounds.size.magnitude);

        public int Count => Width * Height;

        public bool Covered(int texel) => Triangle[texel] >= 0;

        private MaterialSurface(int width, int height, bool tiling, Bounds bounds, Vector3[] vertices, int[] triangles)
        {
            Width = width;
            Height = height;
            Tiling = tiling;
            Bounds = bounds;
            int count = width * height;
            Position = new Vector3[count];
            Normal = new Vector3[count];
            Triangle = new int[count];
            Barycentric = new Vector2[count];
            Array.Fill(Triangle, -1);
            Vertices = vertices;
            Triangles = triangles;
            FaceNormals = new Vector3[triangles.Length / 3];
            TexelLength = new Vector2[triangles.Length / 3];
        }

        /// <summary>A flat 1 m square (X across, Z up the texture, facing up), for materials that tile.</summary>
        public static MaterialSurface Flat(int width, int height)
        {
            var vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            var surface = new MaterialSurface(width, height, true, new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f)), vertices, new[] { 0, 1, 2, 0, 2, 3 });
            surface.FaceNormals[0] = surface.FaceNormals[1] = Vector3.up;
            surface.TexelLength[0] = surface.TexelLength[1] = new Vector2(1f / width, 1f / height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    surface.Position[i] = new Vector3((x + 0.5f) / width - 0.5f, 0f, (y + 0.5f) / height - 0.5f);
                    surface.Normal[i] = Vector3.up;
                    surface.Triangle[i] = 0;
                }
            return surface;
        }

        /// <summary>Lays a mesh out by its UVs (the first set) at this size.</summary>
        public static MaterialSurface FromMesh(MeshData mesh, int width, int height)
        {
            if (mesh == null || mesh.VertexCount == 0 || mesh.TriangleCount == 0)
                return Flat(width, height);
            var all = new List<int>();
            foreach (var slot in mesh.Slots)
                all.AddRange(slot);
            var surface = new MaterialSurface(width, height, false, mesh.Bounds, mesh.Positions.ToArray(), all.ToArray());
            var positions = mesh.Positions;
            var normals = mesh.Normals;
            var uvs = mesh.UVs;
            int triangleCount = all.Count / 3;

            for (int t = 0; t < triangleCount; t++)
            {
                int ia = all[t * 3], ib = all[t * 3 + 1], ic = all[t * 3 + 2];
                Vector3 a = positions[ia], b = positions[ib], c = positions[ic];
                surface.FaceNormals[t] = MeshData.Direction(Vector3.Cross(b - a, c - a), Vector3.up);
                // How far a step in U and in V goes on the mesh here.
                Vector2 ua = uvs[ia], ub = uvs[ib], uc = uvs[ic];
                Vector3 e1 = b - a, e2 = c - a;
                Vector2 d1 = ub - ua, d2 = uc - ua;
                float det = d1.x * d2.y - d2.x * d1.y;
                if (Mathf.Abs(det) > 1e-12f)
                {
                    float r = 1f / det;
                    var dPdu = (e1 * d2.y - e2 * d1.y) * r;
                    var dPdv = (e2 * d1.x - e1 * d2.x) * r;
                    surface.TexelLength[t] = new Vector2(dPdu.magnitude / width, dPdv.magnitude / height);
                }
                else
                    surface.TexelLength[t] = new Vector2(1e-4f, 1e-4f);
            }

            // Each triangle onto the texels whose centres it covers (rows in parallel: each texel is written by the
            // triangles over it; where UVs overlap, the last one wins).
            var rowTriangles = new List<int>[height];
            for (int y = 0; y < height; y++)
                rowTriangles[y] = new List<int>();
            for (int t = 0; t < triangleCount; t++)
            {
                float minV = Mathf.Min(uvs[all[t * 3]].y, Mathf.Min(uvs[all[t * 3 + 1]].y, uvs[all[t * 3 + 2]].y));
                float maxV = Mathf.Max(uvs[all[t * 3]].y, Mathf.Max(uvs[all[t * 3 + 1]].y, uvs[all[t * 3 + 2]].y));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(minV * height - 0.5f)), y1 = Mathf.Min(height - 1, Mathf.CeilToInt(maxV * height - 0.5f));
                for (int y = y0; y <= y1; y++)
                    rowTriangles[y].Add(t);
            }
            Parallel.For(0, height, y =>
            {
                float v = (y + 0.5f) / height;
                foreach (int t in rowTriangles[y])
                {
                    int ia = all[t * 3], ib = all[t * 3 + 1], ic = all[t * 3 + 2];
                    Vector2 ua = uvs[ia], ub = uvs[ib], uc = uvs[ic];
                    float area = (ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y);
                    if (Mathf.Abs(area) < 1e-14f)
                        continue;
                    float minU = Mathf.Min(ua.x, Mathf.Min(ub.x, uc.x)), maxU = Mathf.Max(ua.x, Mathf.Max(ub.x, uc.x));
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(minU * width - 0.5f)), x1 = Mathf.Min(width - 1, Mathf.CeilToInt(maxU * width - 0.5f));
                    for (int x = x0; x <= x1; x++)
                    {
                        float u = (x + 0.5f) / width;
                        // Barycentric weights of the texel's centre.
                        float w1 = ((u - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (v - ua.y)) / area;
                        float w2 = ((ub.x - ua.x) * (v - ua.y) - (u - ua.x) * (ub.y - ua.y)) / area;
                        float w0 = 1f - w1 - w2;
                        const float edge = -1e-4f;
                        if (w0 < edge || w1 < edge || w2 < edge)
                            continue;
                        int i = y * width + x;
                        surface.Triangle[i] = t;
                        surface.Barycentric[i] = new Vector2(w1, w2);
                        surface.Position[i] = positions[ia] * w0 + positions[ib] * w1 + positions[ic] * w2;
                        var n = normals[ia] * w0 + normals[ib] * w1 + normals[ic] * w2;
                        surface.Normal[i] = MeshData.Direction(n, surface.FaceNormals[t]);
                    }
                }
            });
            return surface;
        }

        /// <summary>How far one texel step goes on the mesh at a texel, along U (x) and V (y).</summary>
        public Vector2 TexelLengthAt(int texel)
        {
            int t = Triangle[texel];
            return t >= 0 ? TexelLength[t] : new Vector2(1f / Width, 1f / Height);
        }

        /// <summary>The triangles for casting rays at.</summary>
        public MeshBvh Bvh
        {
            get
            {
                lock (_bakes)
                    return _bvh ??= new MeshBvh(Vertices, Triangles);
            }
        }

        // A bake, made once per set of settings (texel-by-texel work runs outside the lock: two asking at once may both make it).
        private T Bake<T>(string key, Func<T> make)
        {
            lock (_bakes)
                if (_bakes.TryGetValue(key, out var made))
                    return (T)made;
            var result = make();
            lock (_bakes)
                _bakes[key] = result;
            return result;
        }

        // ---- Ambient occlusion and thickness ----

        /// <summary>
        /// How open each texel is to the sky: 1 out in the open, darker in creases and under overhangs. Rays go out
        /// from the surface up to <paramref name="distance"/> (a share of the mesh's size); <paramref name="rays"/> per texel.
        /// </summary>
        public float[] AmbientOcclusion(float distance, int rays)
        {
            rays = Mathf.Clamp(rays, 4, 256);
            distance = Mathf.Clamp(distance, 0.001f, 4f);
            return Bake($"ao:{distance:0.####}:{rays}", () => CastAll(distance * Size, rays, false));
        }

        /// <summary>How thin the mesh is at each texel: 1 where it's thin (edges of leaves, ears, fins), 0 where it's thick.</summary>
        public float[] Thinness(float distance, int rays)
        {
            rays = Mathf.Clamp(rays, 4, 256);
            distance = Mathf.Clamp(distance, 0.001f, 4f);
            return Bake($"thin:{distance:0.####}:{rays}", () => CastAll(distance * Size, rays, true));
        }

        private float[] CastAll(float reach, int rays, bool inward)
        {
            var result = new float[Count];
            if (Tiling)
            {
                Array.Fill(result, inward ? 0f : 1f);
                return result;
            }
            var bvh = Bvh;
            float bias = Size * 2e-4f;
            Parallel.For(0, Height, y =>
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    int t = Triangle[i];
                    if (t < 0)
                    {
                        result[i] = inward ? 0f : 1f;
                        continue;
                    }
                    var face = FaceNormals[t];
                    var normal = inward ? -Normal[i] : Normal[i];
                    var origin = Position[i] + (inward ? -face : face) * (bias + TexelLength[t].magnitude * 0.5f);
                    // Two directions square to the normal, for turning hemisphere samples to face it.
                    var side = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    var up = Vector3.Cross(normal, side);
                    float sum = 0f;
                    uint seed = (uint)i * 9781u + 1u;
                    for (int r = 0; r < rays; r++)
                    {
                        // Cosine-weighted, stratified round the circle, jittered per texel.
                        float a = Hash01(seed + (uint)r * 2u), b = Hash01(seed + (uint)r * 2u + 1u);
                        float angle = (r + a) / rays * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt(b);
                        var direction = side * (Mathf.Cos(angle) * radius) + up * (Mathf.Sin(angle) * radius) + normal * Mathf.Sqrt(Mathf.Max(0f, 1f - b));
                        if (inward)
                            sum += bvh.Raycast(origin, direction, reach, out float d, out _) ? d / reach : 1f;
                        else if (bvh.Any(origin, direction, reach, out float d))
                            sum += 1f - d / reach;
                    }
                    result[i] = inward ? 1f - sum / rays : 1f - sum / rays;
                }
            });
            return result;
        }

        private static float Hash01(uint h)
        {
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }

        // ---- Edges and cavities ----

        private struct Edge
        {
            public Vector3 A, B;
            /// <summary>Degrees between the faces either side: positive sticks out (an edge), negative dips in (a crease).</summary>
            public float Angle;
        }

        /// <summary>
        /// Edges (corners that stick out) and cavities (creases that dip in), 0 to 1. Mesh edges sharper than
        /// <paramref name="sharpAngle"/> degrees are lines <paramref name="width"/> meters wide, fading out across it;
        /// gently curved surfaces (a subdivided shape) add their bend, times <paramref name="smooth"/>.
        /// </summary>
        public (float[] edges, float[] cavities) Curvature(float width, float sharpAngle, float smooth)
        {
            width = Mathf.Max(1e-5f, width);
            sharpAngle = Mathf.Clamp(sharpAngle, 1f, 179f);
            return Bake($"curve:{width:0.#####}:{sharpAngle:0.#}:{smooth:0.###}", () => MakeCurvature(width, sharpAngle, smooth));
        }

        private (float[], float[]) MakeCurvature(float width, float sharpAngle, float smooth)
        {
            var edges = new float[Count];
            var cavities = new float[Count];
            if (Tiling)
                return (edges, cavities);
            var all = Edges();

            // Sharp edges in a grid of width-sized cells (each edge in every cell along it), for finding the near ones.
            var cells = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / width), Mathf.FloorToInt(p.y / width), Mathf.FloorToInt(p.z / width));
            for (int e = 0; e < all.Count; e++)
            {
                if (Mathf.Abs(all[e].Angle) < sharpAngle * 0.5f)
                    continue;
                var a = all[e].A;
                var b = all[e].B;
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / (width * 0.5f)), 1, 100000);
                var last = (int.MinValue, 0, 0);
                for (int s = 0; s <= steps; s++)
                {
                    var key = Cell(Vector3.Lerp(a, b, s / (float)steps));
                    if (key == last)
                        continue;
                    last = key;
                    if (!cells.TryGetValue(key, out var list))
                        cells[key] = list = new List<int>();
                    if (list.Count == 0 || list[list.Count - 1] != e)
                        list.Add(e);
                }
            }

            // Gentle bending, per place: how far the neighbours fall away below (or rise above) its normal.
            var bend = smooth > 0f ? PlaceBend(sharpAngle) : null;

            Parallel.For(0, Height, y =>
            {
                var seen = new HashSet<int>();
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    int t = Triangle[i];
                    if (t < 0)
                        continue;
                    var p = Position[i];
                    var (cx, cy, cz) = Cell(p);
                    float edge = 0f, cavity = 0f;
                    seen.Clear();
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                            continue;
                        foreach (int e in list)
                        {
                            if (!seen.Add(e))
                                continue;
                            var segment = all[e];
                            float d = DistanceToSegment(p, segment.A, segment.B);
                            if (d >= width)
                                continue;
                            // Full strength from the sharp angle up, fading in from half of it.
                            float strength = Mathf.InverseLerp(sharpAngle * 0.5f, sharpAngle, Mathf.Abs(segment.Angle));
                            float fade = 1f - d / width;
                            float value = strength * fade * fade * (3f - 2f * fade);
                            if (segment.Angle > 0f)
                                edge = Mathf.Max(edge, value);
                            else
                                cavity = Mathf.Max(cavity, value);
                        }
                    }
                    if (bend != null)
                    {
                        var w = Barycentric[i];
                        float k = bend[Triangles[t * 3]] * (1f - w.x - w.y) + bend[Triangles[t * 3 + 1]] * w.x + bend[Triangles[t * 3 + 2]] * w.y;
                        edge = Mathf.Max(edge, Mathf.Clamp01(k * smooth * 4f));
                        cavity = Mathf.Max(cavity, Mathf.Clamp01(-k * smooth * 4f));
                    }
                    edges[i] = edge;
                    cavities[i] = cavity;
                }
            });
            return (edges, cavities);
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float length = ab.sqrMagnitude;
            float t = length > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / length) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        // The mesh's places (vertices in one spot count as one), each vertex's place, and each place's normal.
        private void Places()
        {
            if (_placeOf != null)
                return;
            var byPlace = new Dictionary<Vector3, int>(new PlaceComparer());
            var placeOf = new int[Vertices.Length];
            var sums = new List<Vector3>();
            for (int v = 0; v < Vertices.Length; v++)
            {
                if (!byPlace.TryGetValue(Vertices[v], out int p))
                {
                    p = sums.Count;
                    byPlace[Vertices[v]] = p;
                    sums.Add(Vector3.zero);
                }
                placeOf[v] = p;
            }
            for (int t = 0; t < FaceNormals.Length; t++)
            {
                var a = Vertices[Triangles[t * 3]];
                float area = Vector3.Cross(Vertices[Triangles[t * 3 + 1]] - a, Vertices[Triangles[t * 3 + 2]] - a).magnitude;
                for (int k = 0; k < 3; k++)
                    sums[placeOf[Triangles[t * 3 + k]]] += FaceNormals[t] * area;
            }
            var normals = new Vector3[sums.Count];
            for (int p = 0; p < sums.Count; p++)
                normals[p] = MeshData.Direction(sums[p], Vector3.up);
            _placeNormal = normals;
            _placeOf = placeOf;
        }

        // Every edge between places, with the angle between the faces either side (open edges count as sharp).
        private List<Edge> Edges()
        {
            lock (_bakes)
            {
                if (_edges != null)
                    return _edges;
                Places();
                var faces = new Dictionary<(int, int), List<int>>();
                for (int t = 0; t < FaceNormals.Length; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = _placeOf[Triangles[t * 3 + k]], b = _placeOf[Triangles[t * 3 + (k + 1) % 3]];
                        if (a == b)
                            continue;
                        var key = a < b ? (a, b) : (b, a);
                        if (!faces.TryGetValue(key, out var list))
                            faces[key] = list = new List<int>(2);
                        list.Add(t);
                    }
                var positions = new Vector3[_placeNormal.Length];
                for (int v = 0; v < Vertices.Length; v++)
                    positions[_placeOf[v]] = Vertices[v];
                var edges = new List<Edge>(faces.Count);
                foreach (var ((a, b), list) in faces)
                {
                    float angle;
                    if (list.Count == 1)
                        angle = 90f;
                    else
                    {
                        int f1 = list[0], f2 = list[1];
                        var n1 = FaceNormals[f1];
                        var n2 = FaceNormals[f2];
                        angle = Vector3.Angle(n1, n2);
                        // Sticks out if the other face falls away below this one's plane.
                        var centre2 = (Vertices[Triangles[f2 * 3]] + Vertices[Triangles[f2 * 3 + 1]] + Vertices[Triangles[f2 * 3 + 2]]) / 3f;
                        var centre1 = (Vertices[Triangles[f1 * 3]] + Vertices[Triangles[f1 * 3 + 1]] + Vertices[Triangles[f1 * 3 + 2]]) / 3f;
                        if (Vector3.Dot(n1, centre2 - centre1) > 0f)
                            angle = -angle;
                    }
                    edges.Add(new Edge { A = positions[a], B = positions[b], Angle = angle });
                }
                return _edges = edges;
            }
        }

        // Per vertex: how much the surface bends there across its smooth (not sharp) edges: positive bulging out, negative dipping in.
        private float[] PlaceBend(float sharpAngle)
        {
            Edges();
            var positions = new Vector3[_placeNormal.Length];
            for (int v = 0; v < Vertices.Length; v++)
                positions[_placeOf[v]] = Vertices[v];
            var sum = new float[_placeNormal.Length];
            var count = new int[_placeNormal.Length];
            var places = new Dictionary<Vector3, int>(new PlaceComparer());
            for (int p = 0; p < positions.Length; p++)
                places[positions[p]] = p;
            // Places on a sharp edge are left out: their averaged normal points between the faces, so their flat
            // neighbours would look bent (and the sharp edge is drawn as a line anyway).
            var onSharp = new bool[_placeNormal.Length];
            foreach (var edge in _edges)
                if (Mathf.Abs(edge.Angle) >= sharpAngle * 0.5f)
                    onSharp[places[edge.A]] = onSharp[places[edge.B]] = true;
            foreach (var edge in _edges)
            {
                if (Mathf.Abs(edge.Angle) >= sharpAngle * 0.5f)
                    continue;
                int a = places[edge.A], b = places[edge.B];
                if (onSharp[a] || onSharp[b])
                    continue;
                var ab = positions[b] - positions[a];
                float length = ab.magnitude;
                if (length < 1e-9f)
                    continue;
                // How far the neighbour falls below this place's tangent plane, per unit of distance.
                sum[a] += -Vector3.Dot(_placeNormal[a], ab) / length;
                sum[b] += Vector3.Dot(_placeNormal[b], ab) / length;
                count[a]++;
                count[b]++;
            }
            var bend = new float[Vertices.Length];
            for (int v = 0; v < Vertices.Length; v++)
            {
                int p = _placeOf[v];
                bend[v] = count[p] > 0 ? sum[p] / count[p] : 0f;
            }
            return bend;
        }

        /// <summary>Positions equal to within a tenth of a millimetre.</summary>
        private sealed class PlaceComparer : IEqualityComparer<Vector3>
        {
            private const float Cell = 1e-4f;
            public bool Equals(Vector3 a, Vector3 b) => Key(a) == Key(b);
            public int GetHashCode(Vector3 v) => Key(v).GetHashCode();
            private static (long, long, long) Key(Vector3 v)
                => ((long)Math.Round(v.x / Cell), (long)Math.Round(v.y / Cell), (long)Math.Round(v.z / Cell));
        }

        // ---- Brushes ----

        private Dictionary<(int, int, int), List<int>> _texelCells;
        private float _texelCell;

        /// <summary>
        /// The texels within <paramref name="radius"/> of a point on the mesh, facing the same way as
        /// <paramref name="normal"/> (so a brush doesn't paint through to the other side of a thin part), with how far
        /// each is. For brush strokes.
        /// </summary>
        public void TexelsNear(Vector3 point, Vector3 normal, float radius, List<(int texel, float distance)> found)
        {
            found.Clear();
            if (_texelCells == null)
            {
                // Texels in a grid of cells about 1/64 of the mesh's size.
                _texelCell = Mathf.Max(Size / 64f, 1e-4f);
                _texelCells = new Dictionary<(int, int, int), List<int>>();
                for (int i = 0; i < Count; i++)
                {
                    if (Triangle[i] < 0)
                        continue;
                    var key = CellOf(Position[i]);
                    if (!_texelCells.TryGetValue(key, out var list))
                        _texelCells[key] = list = new List<int>();
                    list.Add(i);
                }
            }
            int reach = Mathf.CeilToInt(radius / _texelCell);
            var (cx, cy, cz) = CellOf(point);
            float radiusSquared = radius * radius;
            for (int x = cx - reach; x <= cx + reach; x++)
            for (int y = cy - reach; y <= cy + reach; y++)
            for (int z = cz - reach; z <= cz + reach; z++)
            {
                if (!_texelCells.TryGetValue((x, y, z), out var list))
                    continue;
                foreach (int i in list)
                {
                    float d = (Position[i] - point).sqrMagnitude;
                    if (d <= radiusSquared && Vector3.Dot(Normal[i], normal) > 0.1f)
                        found.Add((i, Mathf.Sqrt(d)));
                }
            }
        }

        private (int, int, int) CellOf(Vector3 p) => (Mathf.FloorToInt(p.x / _texelCell), Mathf.FloorToInt(p.y / _texelCell), Mathf.FloorToInt(p.z / _texelCell));
    }
}
