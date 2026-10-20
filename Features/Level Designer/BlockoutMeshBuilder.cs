using System.Collections.Generic;
using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>The kinds of shape a <see cref="BlockoutShape"/> can be.</summary>
    public enum BlockoutShapeKind
    {
        Box,
        /// <summary>A slope rising along the shape's forward (+Z) axis.</summary>
        Ramp,
        /// <summary>A solid flight of steps rising along +Z; step height comes from <see cref="BlockoutShape.StepHeight"/>.</summary>
        Stairs,
        Cylinder,
        Sphere,
        /// <summary>A triangular prism (a roof shape) whose ridge runs along +Z.</summary>
        Wedge,
        /// <summary>A block with a round-topped tunnel running through it along +Z.</summary>
        Arch,
        /// <summary>A curved ramp (like a skate ramp) rising along +Z, flat at the bottom and steep at the top.</summary>
        QuarterPipe,
        /// <summary>A vertical driving loop with straight entry and exit tracks.</summary>
        Loop,
        /// <summary>A U-shaped driving trough running along +Z.</summary>
        HalfPipe,
    }

    /// <summary>
    /// Builds the procedural meshes of <see cref="BlockoutShape"/>s, in meters, with the pivot at the centre of the
    /// bottom face (so a shape placed on the floor sits on it). Size x is the width, y the height, z the length.<br/>
    /// Besides positions and normals, each vertex carries what the blockout shader needs to draw edges and labels
    /// without a texture:
    /// <list type="bullet">
    /// <item>UV0 = (u, v) position on its face in meters, measured from the face's bottom-left corner (seen from
    /// outside), z = 1 if the face shows its per-meter cell labels, w = edge mode (0 none, 1 all four sides, 2 only
    /// the top and bottom of the face).</item>
    /// <item>UV1 = (face width, face height) in meters, then which shape axis u and v run along
    /// (<see cref="AxisX"/>, <see cref="AxisY"/>, <see cref="AxisZ"/> or <see cref="AxisNone"/>), so each 1 m cell
    /// can be labelled like "x3 y2".</item>
    /// </list>
    /// </summary>
    public sealed class BlockoutMeshBuilder
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector4> _uv0 = new();
        private readonly List<Vector4> _uv1 = new();
        private readonly List<int> _triangles = new();

        /// <summary>How a face is laid out for the shader (see the class summary).</summary>
        public struct FaceInfo
        {
            public bool Label;
            public int Edges;
            /// <summary>Optional: the face's own coordinate origin, instead of the polygon's lower-left corner
            /// (lets several polygons share one face, like the side of the stairs).</summary>
            public Vector3? Origin;
            /// <summary>Optional: the face's full size, instead of the polygon's bounds.</summary>
            public Vector2? Size;
            /// <summary>No longer used: faces now label every 1 m cell instead of showing one size label. Kept so
            /// existing code that sets it still compiles.</summary>
            public Vector2? LabelCenter;

            public static FaceInfo Default => new FaceInfo { Label = true, Edges = 1 };
            public static FaceInfo Plain => new FaceInfo { Label = false, Edges = 1 };
        }

        /// <summary>Builds the mesh for a shape into <paramref name="mesh"/> (cleared first).</summary>
        public static void Build(Mesh mesh, BlockoutShapeKind kind, Vector3 size, float stepHeight = 0.25f, int segments = 24,
            float archOpening = 0.6f, float loopApproachLength = 4f, float trackThickness = 0.5f)
        {
            var builder = new BlockoutMeshBuilder();
            size = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));
            segments = Mathf.Clamp(segments, 6, 64);

            switch (kind)
            {
                case BlockoutShapeKind.Ramp: builder.Ramp(size); break;
                case BlockoutShapeKind.Stairs: builder.Stairs(size, stepHeight); break;
                case BlockoutShapeKind.Cylinder: builder.Cylinder(size, segments); break;
                case BlockoutShapeKind.Sphere: builder.Sphere(size, segments); break;
                case BlockoutShapeKind.Wedge: builder.Wedge(size); break;
                case BlockoutShapeKind.Arch: builder.Arch(size, segments, archOpening); break;
                case BlockoutShapeKind.QuarterPipe: builder.QuarterPipe(size, segments); break;
                case BlockoutShapeKind.Loop: builder.Loop(size, segments, loopApproachLength, trackThickness); break;
                case BlockoutShapeKind.HalfPipe: builder.HalfPipe(size, segments, trackThickness); break;
                default: builder.Box(size); break;
            }

            builder.WriteTo(mesh);
        }

        private void WriteTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        // ---------------------------------------------------------------- faces

        /// <summary>Axis codes stored in UV1.zw: 0 = X, 1 = Y, 2 = Z, 3 = none (a slanted or curved direction).</summary>
        public const float AxisX = 0f, AxisY = 1f, AxisZ = 2f, AxisNone = 3f;

        // The shape axis a face direction runs along (either way), or none when it's slanted.
        private static float AxisCode(Vector3 direction)
        {
            var abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (abs.x > 0.99f) return AxisX;
            if (abs.y > 0.99f) return AxisY;
            if (abs.z > 0.99f) return AxisZ;
            return AxisNone;
        }

        /// <summary>
        /// Adds a flat convex (or star-shaped around its first point) polygon facing <paramref name="normal"/>, with
        /// <paramref name="up"/> as the face's "up" for its coordinates and label (text reads left to right when
        /// looking at the face from outside).
        /// </summary>
        public void AddPolygon(IReadOnlyList<Vector3> points, Vector3 normal, Vector3 up, FaceInfo info)
        {
            if (points.Count < 3)
                return;

            normal = normal.normalized;
            Vector3 right = Vector3.Cross(normal, up).normalized;
            Vector3 faceUp = Vector3.Cross(right, normal).normalized;

            // Face coordinates: from the given origin, or from the polygon's lower-left corner.
            float minU = float.MaxValue, minV = float.MaxValue, maxU = float.MinValue, maxV = float.MinValue;
            foreach (var p in points)
            {
                float u = Vector3.Dot(p, right), v = Vector3.Dot(p, faceUp);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, v); maxV = Mathf.Max(maxV, v);
            }
            if (info.Origin.HasValue)
            {
                minU = Vector3.Dot(info.Origin.Value, right);
                minV = Vector3.Dot(info.Origin.Value, faceUp);
            }
            Vector2 faceSize = info.Size ?? new Vector2(maxU - minU, maxV - minV);
            // Which of the shape's axes the face's u and v run along, so the shader can letter its cell labels.
            float axisU = AxisCode(right), axisV = AxisCode(faceUp);

            int start = _vertices.Count;
            foreach (var p in points)
            {
                _vertices.Add(p);
                _normals.Add(normal);
                _uv0.Add(new Vector4(Vector3.Dot(p, right) - minU, Vector3.Dot(p, faceUp) - minV, info.Label ? 1f : 0f, info.Edges));
                _uv1.Add(new Vector4(faceSize.x, faceSize.y, axisU, axisV));
            }

            // Fan from the first point, wound so the front faces the normal (Unity: clockwise seen from the front).
            bool flip = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal) < 0f;
            for (int i = 1; i < points.Count - 1; i++)
                AddTriangle(start, start + i, start + i + 1, flip);
        }

        /// <summary>Adds a rectangle given its centre, outward normal, up direction and size.</summary>
        public void AddQuad(Vector3 center, Vector3 normal, Vector3 up, float width, float height, FaceInfo info)
        {
            Vector3 right = Vector3.Cross(normal.normalized, up).normalized * (width * 0.5f);
            Vector3 u = Vector3.Cross(right.normalized, normal.normalized).normalized * (height * 0.5f);
            AddPolygon(new[] { center - right - u, center - right + u, center + right + u, center + right - u }, normal, up, info);
        }

        private void AddTriangle(int a, int b, int c, bool flip)
        {
            if (flip) { _triangles.Add(a); _triangles.Add(c); _triangles.Add(b); }
            else { _triangles.Add(a); _triangles.Add(b); _triangles.Add(c); }
        }

        // Adds a smoothly shaded grid of vertices (rows x columns), used for curved surfaces. normalAt gives the
        // outward normal; uv is the face position in meters; faceSize the full face size.
        private void AddSmoothGrid(Vector3[,] points, Vector3[,] normals, Vector2[,] uvs, Vector2 faceSize, int edges)
        {
            int rows = points.GetLength(0), cols = points.GetLength(1);
            int start = _vertices.Count;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    _vertices.Add(points[r, c]);
                    _normals.Add(normals[r, c]);
                    _uv0.Add(new Vector4(uvs[r, c].x, uvs[r, c].y, 0f, edges));
                    _uv1.Add(new Vector4(faceSize.x, faceSize.y, AxisNone, AxisNone));
                }
            }

            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int i0 = start + r * cols + c, i1 = i0 + 1, i2 = i0 + cols, i3 = i2 + 1;
                    Vector3 n = normals[r, c] + normals[r + 1, c + 1] + normals[r, c + 1] + normals[r + 1, c];
                    Vector3 a = points[r, c], b = points[r + 1, c], d = points[r + 1, c + 1], e = points[r, c + 1];
                    // Both triangles share a winding; summing them stays meaningful when one collapses at a pole.
                    Vector3 facing = Vector3.Cross(b - a, d - a) + Vector3.Cross(d - a, e - a);
                    bool flip = Vector3.Dot(facing, n) < 0f;
                    // Degenerate rows (sphere poles) still triangulate fine: their zero-area triangles are invisible.
                    AddTriangle(i0, i2, i3, flip);
                    AddTriangle(i0, i3, i1, flip);
                }
            }
        }

        // ---------------------------------------------------------------- shapes

        private void Box(Vector3 s)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f;
            var info = FaceInfo.Default;
            AddQuad(new Vector3(0, s.y, 0), Vector3.up, Vector3.forward, s.x, s.z, info);
            AddQuad(new Vector3(0, 0, 0), Vector3.down, Vector3.forward, s.x, s.z, info);
            AddQuad(new Vector3(0, s.y * 0.5f, -hz), Vector3.back, Vector3.up, s.x, s.y, info);
            AddQuad(new Vector3(0, s.y * 0.5f, hz), Vector3.forward, Vector3.up, s.x, s.y, info);
            AddQuad(new Vector3(-hx, s.y * 0.5f, 0), Vector3.left, Vector3.up, s.z, s.y, info);
            AddQuad(new Vector3(hx, s.y * 0.5f, 0), Vector3.right, Vector3.up, s.z, s.y, info);
        }

        private void Ramp(Vector3 s)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f;
            var info = FaceInfo.Default;
            AddQuad(Vector3.zero, Vector3.down, Vector3.forward, s.x, s.z, info);
            AddQuad(new Vector3(0, s.y * 0.5f, hz), Vector3.forward, Vector3.up, s.x, s.y, info);

            // The slope, from the front bottom edge up to the back top edge.
            Vector3 slopeUp = new Vector3(0, s.y, s.z).normalized;
            Vector3 slopeNormal = new Vector3(0, s.z, -s.y).normalized;
            AddPolygon(new[] { new Vector3(-hx, 0, -hz), new Vector3(-hx, s.y, hz), new Vector3(hx, s.y, hz), new Vector3(hx, 0, -hz) },
                slopeNormal, slopeUp, info);

            // Side triangles: the label sits low and toward the tall end, where the triangle has room.
            foreach (float side in new[] { -1f, 1f })
            {
                var tri = new[] { new Vector3(side * hx, 0, hz), new Vector3(side * hx, 0, -hz), new Vector3(side * hx, s.y, hz) };
                var sideInfo = info;
                // On the right side (+X) face coordinates run toward +Z, on the left toward -Z.
                sideInfo.LabelCenter = new Vector2(side > 0 ? s.z * 0.7f : s.z * 0.3f, s.y * 0.3f);
                AddPolygon(tri, new Vector3(side, 0, 0), Vector3.up, sideInfo);
            }
        }

        private void Wedge(Vector3 s)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f;
            var info = FaceInfo.Default;
            AddQuad(Vector3.zero, Vector3.down, Vector3.forward, s.x, s.z, info);

            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 normal = new Vector3(side * s.y, hx, 0).normalized;
                Vector3 up = new Vector3(-side * hx, s.y, 0).normalized;
                AddPolygon(new[] { new Vector3(side * hx, 0, -hz), new Vector3(0, s.y, -hz), new Vector3(0, s.y, hz), new Vector3(side * hx, 0, hz) },
                    normal, up, info);
            }

            foreach (float end in new[] { -1f, 1f })
            {
                var endInfo = info;
                endInfo.LabelCenter = new Vector2(hx, s.y * 0.3f);
                AddPolygon(new[] { new Vector3(-hx, 0, end * hz), new Vector3(0, s.y, end * hz), new Vector3(hx, 0, end * hz) },
                    new Vector3(0, 0, end), Vector3.up, endInfo);
            }
        }

        private void Stairs(Vector3 s, float stepHeight)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f;
            int steps = Mathf.Clamp(Mathf.RoundToInt(s.y / Mathf.Max(0.05f, stepHeight)), 1, 200);
            float rise = s.y / steps, run = s.z / steps;
            var info = FaceInfo.Default;

            AddQuad(Vector3.zero, Vector3.down, Vector3.forward, s.x, s.z, info);
            AddQuad(new Vector3(0, s.y * 0.5f, hz), Vector3.forward, Vector3.up, s.x, s.y, info);

            for (int i = 0; i < steps; i++)
            {
                float z0 = -hz + i * run;
                float top = (i + 1) * rise;
                // Tread and riser: no labels (they'd be tiny and repeated), but edges so each step reads.
                AddQuad(new Vector3(0, top, z0 + run * 0.5f), Vector3.up, Vector3.forward, s.x, run, FaceInfo.Plain);
                AddQuad(new Vector3(0, top - rise * 0.5f, z0), Vector3.back, Vector3.up, s.x, rise, FaceInfo.Plain);

                // The sides are one face each, built from a column per step that shares the face's coordinates.
                foreach (float side in new[] { -1f, 1f })
                {
                    var sideInfo = new FaceInfo
                    {
                        Label = true, // every column shares the face's coordinates, so the text runs across them
                        Edges = 2,
                        Origin = new Vector3(side * hx, 0, side > 0 ? -hz : hz),
                        Size = new Vector2(s.z, s.y),
                        LabelCenter = new Vector2(side > 0 ? s.z * 0.7f : s.z * 0.3f, s.y * 0.25f),
                    };
                    AddPolygon(new[]
                    {
                        new Vector3(side * hx, 0, z0), new Vector3(side * hx, top, z0),
                        new Vector3(side * hx, top, z0 + run), new Vector3(side * hx, 0, z0 + run),
                    }, new Vector3(side, 0, 0), Vector3.up, sideInfo);
                }
            }
        }

        private void Cylinder(Vector3 s, int segments)
        {
            float a = s.x * 0.5f, b = s.z * 0.5f;
            var points = new Vector3[2, segments + 1];
            var normals = new Vector3[2, segments + 1];
            var uvs = new Vector2[2, segments + 1];
            float circumference = 0f;
            Vector3 previous = Vector3.zero;
            var ring = new Vector3[segments + 1];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(t) * a, 0, -Mathf.Cos(t) * b);
                if (i > 0)
                    circumference += Vector3.Distance(previous, p);
                previous = p;
                ring[i] = p;
                var n = new Vector3(p.x / (a * a), 0, p.z / (b * b)).normalized;
                points[0, i] = p; points[1, i] = p + Vector3.up * s.y;
                normals[0, i] = n; normals[1, i] = n;
                uvs[0, i] = new Vector2(circumference, 0); uvs[1, i] = new Vector2(circumference, s.y);
            }
            AddSmoothGrid(points, normals, uvs, new Vector2(circumference, s.y), 2);

            // Caps: labelled discs (edges off; the side's top and bottom edge lines frame them).
            var capInfo = new FaceInfo { Label = true, Edges = 0 };
            var top = new Vector3[segments];
            var bottom = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                top[i] = ring[i] + Vector3.up * s.y;
                bottom[i] = ring[i];
            }
            AddPolygon(top, Vector3.up, Vector3.forward, capInfo);
            AddPolygon(bottom, Vector3.down, Vector3.forward, capInfo);
        }

        private void Sphere(Vector3 s, int segments)
        {
            int rings = Mathf.Max(4, segments / 2);
            Vector3 radii = s * 0.5f;
            var points = new Vector3[rings + 1, segments + 1];
            var normals = new Vector3[rings + 1, segments + 1];
            var uvs = new Vector2[rings + 1, segments + 1];
            for (int r = 0; r <= rings; r++)
            {
                float phi = (float)r / rings * Mathf.PI; // 0 at the bottom pole
                for (int c = 0; c <= segments; c++)
                {
                    float theta = (float)c / segments * Mathf.PI * 2f;
                    var unit = new Vector3(Mathf.Sin(phi) * Mathf.Sin(theta), -Mathf.Cos(phi), -Mathf.Sin(phi) * Mathf.Cos(theta));
                    var p = Vector3.Scale(unit, radii);
                    points[r, c] = p + Vector3.up * radii.y;
                    normals[r, c] = new Vector3(p.x / (radii.x * radii.x), p.y / (radii.y * radii.y), p.z / (radii.z * radii.z)).normalized;
                    uvs[r, c] = new Vector2(theta * radii.x, phi * radii.y);
                }
            }
            AddSmoothGrid(points, normals, uvs, new Vector2(Mathf.PI * 2f * radii.x, Mathf.PI * radii.y), 0);
        }

        private void QuarterPipe(Vector3 s, int segments)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f;
            int steps = Mathf.Max(4, segments / 2);
            var info = FaceInfo.Default;

            AddQuad(Vector3.zero, Vector3.down, Vector3.forward, s.x, s.z, info);
            AddQuad(new Vector3(0, s.y * 0.5f, hz), Vector3.forward, Vector3.up, s.x, s.y, info);

            // The curve: a quarter ellipse centred at the front top edge, from the front bottom to the back top.
            var profile = new Vector2[steps + 1]; // (z, y)
            var profileNormals = new Vector2[steps + 1];
            float length = 0f;
            var lengths = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps * Mathf.PI * 0.5f;
                profile[i] = new Vector2(-hz + s.z * Mathf.Sin(t), s.y - s.y * Mathf.Cos(t));
                profileNormals[i] = new Vector2(-Mathf.Sin(t) / s.z, Mathf.Cos(t) / s.y).normalized;
                if (i > 0)
                    length += Vector2.Distance(profile[i - 1], profile[i]);
                lengths[i] = length;
            }

            var points = new Vector3[steps + 1, 2];
            var normals = new Vector3[steps + 1, 2];
            var uvs = new Vector2[steps + 1, 2];
            for (int i = 0; i <= steps; i++)
            {
                for (int c = 0; c < 2; c++)
                {
                    float x = c == 0 ? hx : -hx;
                    points[i, c] = new Vector3(x, profile[i].y, profile[i].x);
                    normals[i, c] = new Vector3(0, profileNormals[i].y, profileNormals[i].x);
                    uvs[i, c] = new Vector2(c == 0 ? 0f : s.x, lengths[i]);
                }
            }
            AddSmoothGrid(points, normals, uvs, new Vector2(s.x, length), 1);

            // Sides: star-shaped around the bottom back corner, so the fan starts there.
            foreach (float side in new[] { -1f, 1f })
            {
                var poly = new List<Vector3> { new Vector3(side * hx, 0, hz) };
                for (int i = 0; i <= steps; i++)
                    poly.Add(new Vector3(side * hx, profile[i].y, profile[i].x));
                var sideInfo = info;
                sideInfo.Edges = 2;
                sideInfo.LabelCenter = new Vector2(side > 0 ? s.z * 0.75f : s.z * 0.25f, s.y * 0.25f);
                AddPolygon(poly, new Vector3(side, 0, 0), Vector3.up, sideInfo);
            }
        }

        private void Loop(Vector3 s, int segments, float approachLength, float thickness)
        {
            int steps = Mathf.Max(24, segments * 2);
            float radiusY = s.y * 0.5f;
            approachLength = Mathf.Clamp(Mathf.Max(0f, approachLength), 0f, Mathf.Max(0f, s.z * 0.5f - 0.05f));
            float diameterZ = Mathf.Max(0.1f, s.z - 2f * approachLength);
            float radiusZ = diameterZ * 0.5f;
            float halfWidth = s.x * 0.25f;
            float lateralOffset = halfWidth;
            thickness = Mathf.Max(0.05f, thickness);
            var profile = new List<(Vector3 point, Vector3 normal, Vector3 across, float distance)>();
            float distance = 0f;

            void AddProfilePoint(Vector3 point, Vector3 normal, Vector3 across)
            {
                if (profile.Count > 0)
                {
                    if ((profile[^1].point - point).sqrMagnitude < 1e-8f)
                        return;
                    distance += Vector3.Distance(profile[^1].point, point);
                }
                profile.Add((point, normal.normalized, across.normalized, distance));
            }

            // Offset the approach and exit on opposite sides so they don't overlap at the loop's bottom.
            AddProfilePoint(new Vector3(-lateralOffset, 0f, -approachLength), Vector3.up, Vector3.right);
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = t * Mathf.PI * 2f;
                float eased = t * t * (3f - 2f * t);
                float x = Mathf.Lerp(-lateralOffset, lateralOffset, eased);
                float dx = 6f * lateralOffset / Mathf.PI * t * (1f - t);
                var point = new Vector3(x, radiusY * (1f - Mathf.Cos(angle)), radiusZ * Mathf.Sin(angle));
                var tangent = new Vector3(dx, radiusY * Mathf.Sin(angle), radiusZ * Mathf.Cos(angle)).normalized;
                var radial = new Vector3(0f, Mathf.Cos(angle) / radiusY, -Mathf.Sin(angle) / radiusZ);
                var normal = (radial - tangent * Vector3.Dot(radial, tangent)).normalized;
                var across = Vector3.Cross(normal, tangent).normalized;
                AddProfilePoint(point, normal, across);
            }
            AddProfilePoint(new Vector3(lateralOffset, 0f, approachLength), Vector3.up, Vector3.right);

            AddSolidTrack(profile, halfWidth, thickness, new Vector2(halfWidth * 2f, distance));
        }

        private void HalfPipe(Vector3 s, int segments, float thickness)
        {
            int steps = Mathf.Max(12, segments);
            float halfWidth = s.x * 0.5f;
            thickness = Mathf.Max(0.05f, thickness);
            var profile = new List<(Vector3 point, Vector3 normal, float distance)>();
            float distance = 0f;
            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (float)i / steps);
                var point = new Vector3(halfWidth * Mathf.Sin(angle), s.y * (1f - Mathf.Cos(angle)), 0f);
                var normal = new Vector3(-Mathf.Sin(angle) / halfWidth, Mathf.Cos(angle) / s.y, 0f).normalized;
                if (i > 0)
                    distance += Vector3.Distance(profile[^1].point, point);
                profile.Add((point, normal, distance));
            }

            // The U-shaped ride surface and its solid back. Its open ends face along +/-Z.
            var inner = new Vector3[steps + 1, 2];
            var outer = new Vector3[steps + 1, 2];
            var innerNormals = new Vector3[steps + 1, 2];
            var outerNormals = new Vector3[steps + 1, 2];
            var innerUvs = new Vector2[steps + 1, 2];
            var outerUvs = new Vector2[steps + 1, 2];
            for (int i = 0; i <= steps; i++)
            for (int end = 0; end < 2; end++)
            {
                float z = end == 0 ? -s.z * 0.5f : s.z * 0.5f;
                var (point, normal, alongCurve) = profile[i];
                inner[i, end] = point + Vector3.forward * z;
                outer[i, end] = inner[i, end] - normal * thickness;
                innerNormals[i, end] = normal;
                outerNormals[i, end] = -normal;
                innerUvs[i, end] = new Vector2(alongCurve, z + s.z * 0.5f);
                outerUvs[i, end] = innerUvs[i, end];
            }
            AddSmoothGrid(inner, innerNormals, innerUvs, new Vector2(distance, s.z), 1);
            AddSmoothGrid(outer, outerNormals, outerUvs, new Vector2(distance, s.z), 1);

            // Solid side rails join the inside and underside at both lips, leaving the channel open at its ends.
            for (int side = 0; side < 2; side++)
            {
                var wall = new Vector3[2, 2];
                var normals = new Vector3[2, 2];
                var uvs = new Vector2[2, 2];
                int row = side == 0 ? 0 : steps;
                Vector3 normal = side == 0 ? Vector3.left : Vector3.right;
                for (int end = 0; end < 2; end++)
                {
                    wall[0, end] = inner[row, end];
                    wall[1, end] = outer[row, end];
                    normals[0, end] = normals[1, end] = normal;
                    uvs[0, end] = uvs[1, end] = new Vector2(end * s.z, 0f);
                }
                AddSmoothGrid(wall, normals, uvs, new Vector2(s.z, thickness), 0);
            }
        }

        private void AddSolidTrack(IReadOnlyList<(Vector3 point, Vector3 normal, Vector3 across, float distance)> profile,
            float halfWidth, float thickness, Vector2 faceSize)
        {
            int count = profile.Count;
            var inner = new Vector3[count, 2];
            var outer = new Vector3[count, 2];
            var innerNormals = new Vector3[count, 2];
            var outerNormals = new Vector3[count, 2];
            var innerUvs = new Vector2[count, 2];
            var outerUvs = new Vector2[count, 2];
            for (int i = 0; i < count; i++)
            for (int side = 0; side < 2; side++)
            {
                var (point, normal, across, alongPath) = profile[i];
                float offset = side == 0 ? -halfWidth : halfWidth;
                inner[i, side] = point + across * offset;
                outer[i, side] = inner[i, side] - normal * thickness;
                innerNormals[i, side] = normal;
                outerNormals[i, side] = -normal;
                innerUvs[i, side] = outerUvs[i, side] = new Vector2(side * halfWidth * 2f, alongPath);
            }
            AddSmoothGrid(inner, innerNormals, innerUvs, faceSize, 1);
            AddSmoothGrid(outer, outerNormals, outerUvs, faceSize, 1);

            for (int side = 0; side < 2; side++)
            {
                var wall = new Vector3[count, 2];
                var normals = new Vector3[count, 2];
                var uvs = new Vector2[count, 2];
                for (int i = 0; i < count; i++)
                {
                    Vector3 normal = side == 0 ? -profile[i].across : profile[i].across;
                    wall[i, 0] = inner[i, side];
                    wall[i, 1] = outer[i, side];
                    normals[i, 0] = normals[i, 1] = normal;
                    uvs[i, 0] = new Vector2(profile[i].distance, 0f);
                    uvs[i, 1] = new Vector2(profile[i].distance, thickness);
                }
                AddSmoothGrid(wall, normals, uvs, new Vector2(faceSize.y, thickness), 0);
            }
        }

        private void Arch(Vector3 s, int segments, float opening)
        {
            float hx = s.x * 0.5f, hz = s.z * 0.5f;
            float radius = Mathf.Clamp(opening, 0.1f, 0.95f) * hx;
            // Where the round top starts: leave at least a tenth of the height above the tunnel.
            float spring = Mathf.Clamp(s.y * 0.9f - radius, 0f, s.y);
            radius = Mathf.Min(radius, s.y * 0.9f - spring);
            var info = FaceInfo.Default;

            AddQuad(new Vector3(0, s.y, 0), Vector3.up, Vector3.forward, s.x, s.z, info);
            AddQuad(new Vector3(-hx, s.y * 0.5f, 0), Vector3.left, Vector3.up, s.z, s.y, info);
            AddQuad(new Vector3(hx, s.y * 0.5f, 0), Vector3.right, Vector3.up, s.z, s.y, info);
            float footWidth = hx - radius;
            AddQuad(new Vector3(-hx + footWidth * 0.5f, 0, 0), Vector3.down, Vector3.forward, footWidth, s.z, FaceInfo.Plain);
            AddQuad(new Vector3(hx - footWidth * 0.5f, 0, 0), Vector3.down, Vector3.forward, footWidth, s.z, FaceInfo.Plain);

            // The tunnel outline (inner) and the matching points on the block's outline (outer), from the right
            // foot, over the top, to the left foot. Angles include the top corners so the outer points turn them.
            var angles = new List<float>();
            for (int i = 0; i <= segments / 2; i++)
                angles.Add((float)i / (segments / 2) * Mathf.PI);
            float corner = Mathf.Atan2(s.y - spring, hx);
            angles.Add(corner);
            angles.Add(Mathf.PI - corner);
            angles.Sort();

            var inner = new List<Vector2> { new Vector2(radius, 0) };
            var outer = new List<Vector2> { new Vector2(hx, 0) };
            foreach (float angle in angles)
            {
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                inner.Add(new Vector2(0, spring) + dir * radius);
                float tx = Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.MaxValue;
                float ty = dir.y > 1e-4f ? (s.y - spring) / dir.y : float.MaxValue;
                outer.Add(new Vector2(0, spring) + dir * Mathf.Min(tx, ty));
            }
            inner.Add(new Vector2(-radius, 0));
            outer.Add(new Vector2(-hx, 0));

            // Front and back faces: quads between the outlines, sharing the face's coordinates.
            foreach (float end in new[] { -1f, 1f })
            {
                Vector3 normal = new Vector3(0, 0, end);
                var faceInfo = new FaceInfo
                {
                    Label = true, // the strips share the face's coordinates, so the label runs across them
                    Edges = 1,
                    Origin = new Vector3(end < 0 ? -hx : hx, 0, end * hz),
                    Size = new Vector2(s.x, s.y),
                    LabelCenter = new Vector2(hx, (spring + radius + s.y) * 0.5f),
                };
                for (int i = 0; i < inner.Count - 1; i++)
                {
                    AddPolygon(new[]
                    {
                        new Vector3(inner[i].x, inner[i].y, end * hz), new Vector3(outer[i].x, outer[i].y, end * hz),
                        new Vector3(outer[i + 1].x, outer[i + 1].y, end * hz), new Vector3(inner[i + 1].x, inner[i + 1].y, end * hz),
                    }, normal, Vector3.up, faceInfo);
                }
            }

            // The tunnel's inside, facing inward.
            var points = new Vector3[inner.Count, 2];
            var normals = new Vector3[inner.Count, 2];
            var uvs = new Vector2[inner.Count, 2];
            float run = 0f;
            for (int i = 0; i < inner.Count; i++)
            {
                if (i > 0)
                    run += Vector2.Distance(inner[i - 1], inner[i]);
                Vector2 n;
                if (i == 0) n = Vector2.left;
                else if (i == inner.Count - 1) n = Vector2.right;
                else n = (new Vector2(0, spring) - inner[i]).normalized;
                for (int c = 0; c < 2; c++)
                {
                    points[i, c] = new Vector3(inner[i].x, inner[i].y, c == 0 ? -hz : hz);
                    normals[i, c] = new Vector3(n.x, n.y, 0);
                    uvs[i, c] = new Vector2(c == 0 ? 0 : s.z, run);
                }
            }
            AddSmoothGrid(points, normals, uvs, new Vector2(s.z, run), 1);
        }
    }
}
