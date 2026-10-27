using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// What Mesh Graph nodes do to meshes: bend, twist, taper, displace, mirror, repeat, scatter, project UVs... Each
    /// changes the mesh it's given in place (nodes give them a copy). Pure math: safe on any thread.
    /// </summary>
    public static class MeshModifiers
    {
        /// <summary>Rotates a vector around an axis (unit length) by an angle in radians.</summary>
        public static Vector3 Rotate(Vector3 v, Vector3 axis, float radians)
        {
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            return v * cos + Vector3.Cross(axis, v) * sin + axis * (Vector3.Dot(axis, v) * (1f - cos));
        }

        /// <summary>A matrix that moves, turns (Euler degrees, Unity's Z-X-Y order) and scales: built by hand, safe anywhere.</summary>
        public static Matrix4x4 TRS(Vector3 position, Vector3 eulerDegrees, Vector3 scale)
        {
            Vector3 x = Vector3.right, y = Vector3.up, z = Vector3.forward;
            // Unity applies Z, then X, then Y (in world axes).
            void Turn(Vector3 axis, float degrees)
            {
                if (degrees == 0f) return;
                float radians = degrees * Mathf.Deg2Rad;
                x = Rotate(x, axis, radians);
                y = Rotate(y, axis, radians);
                z = Rotate(z, axis, radians);
            }
            Turn(Vector3.forward, eulerDegrees.z);
            Turn(Vector3.right, eulerDegrees.x);
            Turn(Vector3.up, eulerDegrees.y);
            return Basis(x * scale.x, y * scale.y, z * scale.z, position);
        }

        /// <summary>
        /// The Euler angles (degrees, Unity's order) of a rotation given by where it points forward (Z) and up (Y):
        /// the inverse of <see cref="TRS"/>'s turning. Safe anywhere.
        /// </summary>
        public static Vector3 EulerFromAxes(Vector3 forward, Vector3 up)
        {
            var y = up.sqrMagnitude > 1e-12f ? up.normalized : Vector3.up;
            var z = forward - y * Vector3.Dot(forward, y);
            if (z.sqrMagnitude < 1e-10f)
                z = Mathf.Abs(y.z) < 0.9f ? Vector3.forward - y * y.z : Vector3.right;
            z = z.normalized;
            var x = Vector3.Cross(y, z);
            float pitch = Mathf.Asin(Mathf.Clamp(-z.y, -1f, 1f));
            float yaw = Mathf.Atan2(z.x, z.z);
            float roll = Mathf.Atan2(x.y, y.y);
            return new Vector3(pitch, yaw, roll) * Mathf.Rad2Deg;
        }

        /// <summary>A matrix from its axes and position.</summary>
        public static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z, Vector3 position)
            => new(new Vector4(x.x, x.y, x.z, 0f), new Vector4(y.x, y.y, y.z, 0f), new Vector4(z.x, z.y, z.z, 0f), new Vector4(position.x, position.y, position.z, 1f));

        /// <summary>Copies laid one after another, each moved by <paramref name="step"/> from the one before.</summary>
        public static MeshData Array(MeshData item, int count, Matrix4x4 step)
        {
            var result = new MeshData();
            var matrix = Matrix4x4.identity;
            for (int i = 0; i < Mathf.Max(1, count); i++)
            {
                result.Append(item, matrix);
                matrix = step * matrix;
            }
            return result;
        }

        /// <summary>Copies in a ring around Y, at <paramref name="radius"/>, each turned to face out (petals, fence posts).</summary>
        public static MeshData Radial(MeshData item, int count, float radius, bool faceOut = true)
        {
            var result = new MeshData();
            count = Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                var out_ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var matrix = faceOut
                    ? Basis(Vector3.Cross(Vector3.up, out_), Vector3.up, out_, out_ * radius)
                    : Basis(Vector3.right, Vector3.up, Vector3.forward, out_ * radius);
                result.Append(item, matrix);
            }
            return result;
        }

        /// <summary>Adds a mirrored copy across a plane through the origin (axis 0: X, 1: Y, 2: Z).</summary>
        public static void Mirror(MeshData mesh, int axis, bool keepOriginal = true)
        {
            var scale = Vector3.one;
            scale[Mathf.Clamp(axis, 0, 2)] = -1f;
            var matrix = Basis(Vector3.right * scale.x, Vector3.up * scale.y, Vector3.forward * scale.z, Vector3.zero);
            if (keepOriginal)
            {
                var copy = mesh.Clone();
                mesh.Append(copy, matrix);
            }
            else
                mesh.Transform(matrix);
        }

        /// <summary>The mesh's extent along an axis: its lowest and highest point.</summary>
        private static (float min, float max) Extent(MeshData mesh, int axis)
        {
            if (mesh.VertexCount == 0)
                return (0f, 0f);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var p in mesh.Positions)
            {
                min = Mathf.Min(min, p[axis]);
                max = Mathf.Max(max, p[axis]);
            }
            return (min, max);
        }

        /// <summary>Twists the mesh around Y: the top turns <paramref name="degrees"/> further than the bottom.</summary>
        public static void Twist(MeshData mesh, float degrees)
        {
            var (min, max) = Extent(mesh, 1);
            float height = Mathf.Max(1e-5f, max - min);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                float radians = (mesh.Positions[i].y - min) / height * degrees * Mathf.Deg2Rad;
                mesh.Positions[i] = Rotate(mesh.Positions[i], Vector3.up, radians);
                mesh.Normals[i] = Rotate(mesh.Normals[i], Vector3.up, radians);
            }
        }

        /// <summary>
        /// Bends the mesh's height into an arc (towards +X): the top ends up turned <paramref name="degrees"/> from
        /// the bottom, which stays put. A rod bent 180 makes an arch.
        /// </summary>
        public static void Bend(MeshData mesh, float degrees)
        {
            if (Mathf.Abs(degrees) < 1e-3f)
                return;
            var (min, max) = Extent(mesh, 1);
            float height = Mathf.Max(1e-5f, max - min);
            float total = degrees * Mathf.Deg2Rad;
            float radius = height / total;
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i];
                float phi = (p.y - min) / radius;
                float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
                float r = radius - p.x;
                mesh.Positions[i] = new Vector3(radius - r * cos, min + r * sin, p.z);
                var n = mesh.Normals[i];
                mesh.Normals[i] = new Vector3(n.x * cos + n.y * sin, -n.x * sin + n.y * cos, n.z);
            }
        }

        /// <summary>Scales the mesh across (X and Z) from 1 at its bottom to <paramref name="topScale"/> at its top.</summary>
        public static void Taper(MeshData mesh, float topScale)
        {
            var (min, max) = Extent(mesh, 1);
            float height = Mathf.Max(1e-5f, max - min);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i];
                float s = Mathf.Lerp(1f, topScale, (p.y - min) / height);
                mesh.Positions[i] = new Vector3(p.x * s, p.y, p.z * s);
            }
        }

        /// <summary>
        /// Pushes the surface in and out by smooth noise (rocks, terrain, bark): along each vertex's normal, or
        /// straight up. The same place always moves the same way, so hard edges don't crack open.
        /// </summary>
        public static void Displace(MeshData mesh, float amount, float frequency, int seed, bool alongNormals = true)
        {
            // Corners at one place can have different normals (a hard edge): they all move by their average, so they
            // stay together.
            var shared = new Dictionary<Vector3, Vector3>(new MeshData.PositionComparer());
            if (alongNormals)
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    shared.TryGetValue(mesh.Positions[i], out var sum);
                    shared[mesh.Positions[i]] = sum + mesh.Normals[i];
                }
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i];
                float n = Noise.Fractal(p * frequency, seed, 3) * amount;
                var direction = Vector3.up;
                if (alongNormals)
                    direction = shared.TryGetValue(p, out var sum) && sum.sqrMagnitude > 1e-10f ? sum.normalized : mesh.Normals[i];
                mesh.Positions[i] = p + direction * n;
            }
        }

        /// <summary>
        /// Places copies of <paramref name="item"/> over the surface of <paramref name="surface"/>: evenly by area,
        /// stood up along the surface (or straight up, by <paramref name="alignToSurface"/>), spun at random, and sized
        /// between <paramref name="minScale"/> and <paramref name="maxScale"/>. Leaves on branches, rocks on ground.
        /// </summary>
        public static MeshData Scatter(MeshData surface, MeshData item, int count, int seed, float minScale, float maxScale, float alignToSurface = 1f)
        {
            var result = new MeshData();
            if (surface == null || item == null || count <= 0)
                return result;
            // Triangles by area, to pick from.
            var triangles = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n)>();
            var cumulative = new List<float>();
            float total = 0f;
            foreach (var slot in surface.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    Vector3 a = surface.Positions[slot[t]], b = surface.Positions[slot[t + 1]], c = surface.Positions[slot[t + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * 0.5f;
                    if (area <= 1e-10f) continue;
                    total += area;
                    triangles.Add((a, b, c, cross.normalized));
                    cumulative.Add(total);
                }
            if (triangles.Count == 0)
                return result;

            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float pick = (float)random.NextDouble() * total;
                int index = cumulative.BinarySearch(pick);
                if (index < 0) index = ~index;
                index = Mathf.Clamp(index, 0, triangles.Count - 1);
                var (a, b, c, n) = triangles[index];
                float u = (float)random.NextDouble(), v = (float)random.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                var point = a + (b - a) * u + (c - a) * v;

                var up = Vector3.Lerp(Vector3.up, n, Mathf.Clamp01(alignToSurface));
                up = up.sqrMagnitude > 1e-8f ? up.normalized : n;
                var side = Mathf.Abs(Vector3.Dot(up, Vector3.forward)) > 0.95f ? Vector3.right : Vector3.forward;
                var x = Vector3.Cross(up, side).normalized;
                x = Rotate(x, up, (float)random.NextDouble() * Mathf.PI * 2f);
                var z = Vector3.Cross(x, up);
                float scale = Mathf.Lerp(minScale, maxScale, (float)random.NextDouble());
                result.Append(item, Basis(x * scale, up * scale, z * scale, point));
            }
            return result;
        }

        /// <summary>
        /// UVs projected from three sides (a box): each vertex takes them from the side its normal faces most, so
        /// any shape gets a texture without stretching. <paramref name="scale"/> is UV units per metre.
        /// </summary>
        public static void BoxProject(MeshData mesh, float scale)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i] * scale;
                var n = mesh.Normals[i];
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                mesh.UVs[i] = ax >= ay && ax >= az ? new Vector2(p.z, p.y) : ay >= az ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
            }
        }

        /// <summary>UVs projected flat along an axis (0: X, 1: Y, 2: Z).</summary>
        public static void PlanarProject(MeshData mesh, int axis, float scale)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Positions[i] * scale;
                mesh.UVs[i] = axis == 0 ? new Vector2(p.z, p.y) : axis == 1 ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
            }
        }

        /// <summary>Scales and moves the UVs.</summary>
        public static void TransformUVs(MeshData mesh, Vector2 scale, Vector2 offset)
        {
            for (int i = 0; i < mesh.UVs.Count; i++)
                mesh.UVs[i] = Vector2.Scale(mesh.UVs[i], scale) + offset;
        }

        /// <summary>Colors every vertex from <paramref name="bottom"/> at the mesh's lowest point to <paramref name="top"/> at its highest.</summary>
        public static void HeightColors(MeshData mesh, Color bottom, Color top)
        {
            mesh.FillColors();
            var (min, max) = Extent(mesh, 1);
            float height = Mathf.Max(1e-5f, max - min);
            for (int i = 0; i < mesh.VertexCount; i++)
                mesh.Colors[i] = Color.Lerp(bottom, top, (mesh.Positions[i].y - min) / height);
        }

        /// <summary>Puts every triangle in one material slot.</summary>
        public static void SetSlot(MeshData mesh, int slot)
        {
            var all = new List<int>();
            foreach (var triangles in mesh.Slots)
                all.AddRange(triangles);
            mesh.Slots.Clear();
            mesh.Slot(Mathf.Max(0, slot)).AddRange(all);
        }

        /// <summary>Joins vertices that are the same in every way (position, normal, UV, color): smaller meshes.</summary>
        public static void Weld(MeshData mesh)
        {
            var source = mesh.Clone();
            mesh.Positions.Clear(); mesh.Normals.Clear(); mesh.UVs.Clear(); mesh.Colors.Clear();
            var made = new Dictionary<(Vector3, Vector3, Vector2, Color), int>();
            var remap = new int[source.VertexCount];
            bool colors = source.HasColors;
            for (int i = 0; i < source.VertexCount; i++)
            {
                var key = (Q(source.Positions[i], 1e4f), Q(source.Normals[i], 1e3f), new Vector2(Mathf.Round(source.UVs[i].x * 1e4f), Mathf.Round(source.UVs[i].y * 1e4f)), colors ? source.Colors[i] : Color.white);
                if (!made.TryGetValue(key, out int index))
                {
                    index = mesh.Positions.Count;
                    mesh.Positions.Add(source.Positions[i]);
                    mesh.Normals.Add(source.Normals[i]);
                    mesh.UVs.Add(source.UVs[i]);
                    if (colors) mesh.Colors.Add(source.Colors[i]);
                    made[key] = index;
                }
                remap[i] = index;
            }
            for (int s = 0; s < mesh.Slots.Count; s++)
                for (int t = 0; t < mesh.Slots[s].Count; t++)
                    mesh.Slots[s][t] = remap[mesh.Slots[s][t]];
        }

        private static Vector3 Q(Vector3 v, float f) => new(Mathf.Round(v.x * f), Mathf.Round(v.y * f), Mathf.Round(v.z * f));
    }

    /// <summary>Smooth 3D noise (gradient noise), made here so it's safe on background threads.</summary>
    public static class Noise
    {
        /// <summary>Noise at a point: about -1 to 1, smooth, the same for the same point and seed.</summary>
        public static float At(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            float u = Fade(fx), v = Fade(fy), w = Fade(fz);
            float Corner(int dx, int dy, int dz) => Gradient(Hash(x0 + dx, y0 + dy, z0 + dz, seed), fx - dx, fy - dy, fz - dz);
            float x00 = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), u);
            float x10 = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), u);
            float x01 = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), u);
            float x11 = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
        }

        /// <summary>Layers of noise, each twice as fine and half as strong.</summary>
        public static float Fractal(Vector3 p, int seed, int octaves)
        {
            float sum = 0f, amplitude = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += At(p, seed + o * 101) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                p *= 2f;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static int Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)x * 668265263u + (uint)y * 2246822519u + (uint)z * 3266489917u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return (int)(h ^ (h >> 16));
            }
        }

        private static float Gradient(int hash, float x, float y, float z)
        {
            switch (hash & 15)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x + z; case 5: return -x + z; case 6: return x - z; case 7: return -x - z;
                case 8: return y + z; case 9: return -y + z; case 10: return y - z; case 11: return -y - z;
                case 12: return x + y; case 13: return -x + y; case 14: return -y + z; default: return -y - z;
            }
        }
    }
}
