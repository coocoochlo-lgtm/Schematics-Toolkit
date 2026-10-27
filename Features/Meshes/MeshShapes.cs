using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// The basic shapes a Mesh Graph starts from, centred on the origin with Y up, with normals and UVs. Pure math (no
    /// engine calls), so they're built on background threads.
    /// </summary>
    public static class MeshShapes
    {
        /// <summary>
        /// A grid of quads over a surface: <paramref name="surface"/> gives each grid point's position, normal and UV
        /// (u across, v along, both 0 to 1). Triangles face the way the normals do.
        /// </summary>
        public static void Grid(MeshData mesh, int columns, int rows, Func<float, float, (Vector3 position, Vector3 normal, Vector2 uv)> surface, int slot = 0)
        {
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            int start = mesh.VertexCount;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= columns; c++)
                {
                    var (p, n, uv) = surface(c / (float)columns, r / (float)rows);
                    mesh.AddVertex(p, n, uv);
                }
            int stride = columns + 1;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < columns; c++)
                {
                    int a = start + r * stride + c, b = a + 1, d = a + stride, e = d + 1;
                    AddFacing(mesh, a, d, e, slot);
                    AddFacing(mesh, a, e, b, slot);
                }
        }

        // A triangle, turned to face the way its vertices' normals point; ones with no area are left out (a sphere's poles).
        private static void AddFacing(MeshData mesh, int a, int b, int c, int slot)
        {
            var pa = mesh.Positions[a];
            var cross = Vector3.Cross(mesh.Positions[b] - pa, mesh.Positions[c] - pa);
            if (cross.sqrMagnitude < 1e-14f)
                return;
            var normal = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c];
            if (Vector3.Dot(cross, normal) >= 0f)
                mesh.AddTriangle(a, b, c, slot);
            else
                mesh.AddTriangle(a, c, b, slot);
        }

        /// <summary>A box of a given size, each side split into <paramref name="segments"/> by <paramref name="segments"/>.</summary>
        public static MeshData Box(Vector3 size, int segments = 1)
        {
            var mesh = new MeshData();
            var half = size * 0.5f;
            Face(mesh, Vector3.right, Vector3.forward, Vector3.up, half, segments);
            Face(mesh, Vector3.right, Vector3.back, Vector3.down, half, segments);
            Face(mesh, Vector3.back, Vector3.up, Vector3.right, half, segments);
            Face(mesh, Vector3.forward, Vector3.up, Vector3.left, half, segments);
            Face(mesh, Vector3.right, Vector3.up, Vector3.forward, half, segments);
            Face(mesh, Vector3.left, Vector3.up, Vector3.back, half, segments);
            return mesh;
        }

        private static void Face(MeshData mesh, Vector3 u, Vector3 v, Vector3 normal, Vector3 half, int segments)
        {
            Vector3 Scale(Vector3 axis) => Vector3.Scale(axis, half);
            Grid(mesh, segments, segments, (s, t) =>
                (Scale(normal) + Scale(u) * (s * 2f - 1f) + Scale(v) * (t * 2f - 1f), normal,
                 new Vector2(s * Mathf.Abs(Vector3.Dot(u, half)) * 2f, t * Mathf.Abs(Vector3.Dot(v, half)) * 2f)));
        }

        /// <summary>A flat, upward-facing grid of a given size (X by Z).</summary>
        public static MeshData Plane(Vector2 size, int segmentsX = 1, int segmentsZ = 1)
        {
            var mesh = new MeshData();
            Grid(mesh, segmentsX, segmentsZ, (s, t) =>
                (new Vector3((s - 0.5f) * size.x, 0f, (t - 0.5f) * size.y), Vector3.up, new Vector2(s * size.x, t * size.y)));
            return mesh;
        }

        /// <summary>A sphere of rings and segments (a UV sphere).</summary>
        public static MeshData Sphere(float radius, int segments = 24, int rings = 16)
        {
            var mesh = new MeshData();
            Grid(mesh, Mathf.Max(3, segments), Mathf.Max(2, rings), (s, t) =>
            {
                float theta = s * Mathf.PI * 2f, phi = t * Mathf.PI;
                var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), -Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                return (n * radius, n, new Vector2(s, t));
            });
            return mesh;
        }

        /// <summary>
        /// A cylinder, standing on the origin (bottom at y = 0). A top radius of 0 makes a cone. Caps close its ends.
        /// </summary>
        public static MeshData Cylinder(float bottomRadius, float topRadius, float height, int segments = 24, int heightSegments = 1, bool caps = true)
        {
            var mesh = new MeshData();
            segments = Mathf.Max(3, segments);
            // The side's slope, for its normals (a cone's lean outwards and up).
            float slope = (bottomRadius - topRadius) / Mathf.Max(1e-5f, height);
            Grid(mesh, segments, Mathf.Max(1, heightSegments), (s, t) =>
            {
                float theta = s * Mathf.PI * 2f;
                var around = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                float r = Mathf.Lerp(bottomRadius, topRadius, t);
                var n = (around + Vector3.up * slope).normalized;
                return (around * r + Vector3.up * (t * height), n, new Vector2(s, t * height));
            });
            if (caps)
            {
                if (bottomRadius > 1e-5f)
                    Disc(mesh, 0f, bottomRadius, segments, Vector3.down);
                if (topRadius > 1e-5f)
                    Disc(mesh, height, topRadius, segments, Vector3.up);
            }
            return mesh;
        }

        private static void Disc(MeshData mesh, float y, float radius, int segments, Vector3 normal)
        {
            int center = mesh.AddVertex(new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f));
            int first = mesh.VertexCount;
            for (int i = 0; i <= segments; i++)
            {
                float theta = i / (float)segments * Mathf.PI * 2f;
                var around = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                mesh.AddVertex(around * radius + Vector3.up * y, normal, new Vector2(0.5f + around.x * 0.5f, 0.5f + around.z * 0.5f));
            }
            for (int i = 0; i < segments; i++)
                AddFacing(mesh, center, first + i, first + i + 1, 0);
        }

        /// <summary>A ring: <paramref name="radius"/> to the middle of its tube, which is <paramref name="thickness"/> across.</summary>
        public static MeshData Torus(float radius, float thickness, int segments = 32, int sides = 16)
        {
            var mesh = new MeshData();
            float tube = thickness * 0.5f;
            Grid(mesh, Mathf.Max(3, segments), Mathf.Max(3, sides), (s, t) =>
            {
                float theta = s * Mathf.PI * 2f, phi = t * Mathf.PI * 2f;
                var around = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                var n = around * Mathf.Cos(phi) + Vector3.up * Mathf.Sin(phi);
                return (around * radius + n * tube, n, new Vector2(s, t));
            });
            return mesh;
        }

        /// <summary>
        /// A shape turned on a lathe: <paramref name="radii"/> are its radius at evenly spaced heights from 0 (the
        /// bottom) to <paramref name="height"/> (the top), spun around Y. Vases, pillars, tree trunks, chess pieces.
        /// </summary>
        public static MeshData Lathe(IReadOnlyList<float> radii, float height, int segments = 24, bool caps = true)
        {
            var mesh = new MeshData();
            if (radii == null || radii.Count < 2)
                return mesh;
            segments = Mathf.Max(3, segments);
            int rows = radii.Count - 1;
            Grid(mesh, segments, rows, (s, t) =>
            {
                float theta = s * Mathf.PI * 2f;
                var around = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));
                float f = t * rows;
                int i = Mathf.Min(rows - 1, Mathf.FloorToInt(f));
                float r = Mathf.Lerp(radii[i], radii[i + 1], f - i);
                // The profile's slope here, for the normal.
                float dr = (radii[i + 1] - radii[i]) / Mathf.Max(1e-5f, height / rows);
                var n = (around - Vector3.up * dr).normalized;
                return (around * r + Vector3.up * (t * height), n, new Vector2(s, t * height));
            });
            // (A profile that comes in to nothing at an end closes itself; one that doesn't gets a cap.)
            mesh.RecalculateNormals(60f);
            if (caps)
            {
                if (radii[0] > 1e-4f)
                    Disc(mesh, 0f, radii[0], segments, Vector3.down);
                if (radii[radii.Count - 1] > 1e-4f)
                    Disc(mesh, height, radii[radii.Count - 1], segments, Vector3.up);
            }
            return mesh;
        }
    }
}
