using Remedy.Surfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Audio
{
    /// <summary>
    /// Bakes a <see cref="SoundField"/>: from every cell, rays go out in all directions against the scene's meshes (not
    /// physics, which doesn't run in Edit Mode), and what they hit -- how far, how many, which side, made of what Surface
    /// Material (painted per triangle, else the object's tag; with neither, bare and fully hard: no absorption, full
    /// hardness) -- becomes the cell's measurements. Far, reflective hits are gathered into echo anchors, and each cell remembers the two it hears best.
    /// </summary>
    public static class SoundFieldBaker
    {
        public const string Folder = "Assets/Content/Audio/Sound Fields";
        /// <summary>Above this many cells, baking asks first.</summary>
        public const int LargeBake = 400_000;
        /// <summary>
        /// Hits nearer than this don't make echo anchors (closer, they're part of the sound itself). Small hard rooms
        /// echo too: whether a near wall's echo is heard is down to its material (see the Spatial effects' echo gate).
        /// </summary>
        public const float AnchorMinDistance = 1.5f;
        /// <summary>The grid hits are gathered into anchors on, in meters.</summary>
        public const float AnchorGrid = 8f;
        public const int MaxAnchors = 64;

        public static Vector3Int CellsFor(SoundField field)
        {
            float cell = Mathf.Max(0.25f, field.CellSize);
            var size = field.Size;
            return new Vector3Int(Mathf.Max(1, Mathf.CeilToInt(size.x / cell)), Mathf.Max(1, Mathf.CeilToInt(size.y / cell)), Mathf.Max(1, Mathf.CeilToInt(size.z / cell)));
        }

        /// <summary>Whether the field's settings have changed since it was baked.</summary>
        public static bool IsStale(SoundField field)
        {
            var data = field.Data;
            return data == null || data.Count == 0 || !Mathf.Approximately(data.CellSize, field.CellSize) || data.Cells != CellsFor(field)
                   || (data.Box.center - field.Center).sqrMagnitude > 1e-4f || data.Rays != field.BakeRays;
        }

        private sealed class Bucket
        {
            public int Count;
            public Vector3 Position, Normal;
            public float Reflectivity, Hardness;
        }

        /// <summary>Bakes the field (with a progress bar you can cancel). False if cancelled or there was nothing to bake.</summary>
        public static bool Bake(SoundField field)
        {
            var cells = CellsFor(field);
            long count = (long)cells.x * cells.y * cells.z;
            if (count > 4_000_000)
            {
                EditorUtility.DisplayDialog("Sound Field", $"{count:N0} cells is too many to bake. Make the box smaller or the cells bigger.", "OK");
                return false;
            }
            if (count > LargeBake && !EditorUtility.DisplayDialog("Sound Field", $"Bake {count:N0} cells? It can take a while (bigger cells bake faster).", "Bake", "Cancel"))
                return false;

            var geometry = BakeGeometry.Gather(field);
            if (geometry.TriangleCount == 0)
            {
                EditorUtility.DisplayDialog("Sound Field", "There are no meshes around the field to bake against (check Bake Layers).", "OK");
                return false;
            }

            var fieldDirections = SoundFieldData.Directions(field.BakeRays);
            var toWorld = field.transform.localToWorldMatrix;
            var toLocal = field.transform.worldToLocalMatrix;
            var directions = fieldDirections.Select(d => toWorld.MultiplyVector(d).normalized).ToArray();
            float reach = Mathf.Max(1f, field.BakeReach);
            float cellSize = Mathf.Max(0.25f, field.CellSize);
            var box = new Bounds(field.Center, (Vector3)cells * cellSize);

            var packed = new byte[count * SoundFieldData.Stride];
            var scratch = ScriptableObject.CreateInstance<SoundFieldData>();
            scratch.Cells = cells;
            scratch.CellSize = cellSize;
            scratch.Box = box;
            scratch.Packed = packed;
            var buckets = new Dictionary<(int, int, int, int), Bucket>();
            var open = new List<int>();

            try
            {
                int n = directions.Length;
                int index = 0;
                for (int z = 0; z < cells.z; z++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Baking Sound Field", $"{field.name}: layer {z + 1} of {cells.z}", z / (float)cells.z))
                        return false;
                    for (int y = 0; y < cells.y; y++)
                    for (int x = 0; x < cells.x; x++, index++)
                    {
                        var origin = toWorld.MultiplyPoint3x4(scratch.CellCenter(x, y, z));
                        int hits = 0, backs = 0;
                        float distances = 0f, weights = 0f, absorption = 0f, hardness = 0f, wetness = 0f;
                        float nearest = float.MaxValue;
                        byte nearestDirection = 0;
                        for (int d = 0; d < n; d++)
                        {
                            if (!geometry.Raycast(origin, directions[d], reach, out float t, out int triangle, out bool back))
                                continue;
                            hits++;
                            if (back) { backs++; continue; }
                            distances += t;
                            float weight = 1f / (1f + t * 0.25f);
                            weights += weight;
                            absorption += geometry.Absorption[triangle] * weight;
                            hardness += geometry.Hardness[triangle] * weight;
                            wetness += geometry.Wetness[triangle] * weight;
                            if (t < nearest)
                            {
                                nearest = t;
                                nearestDirection = (byte)d;
                            }
                            // (Not the floor: it's under every sound, and would crowd out the walls.)
                            if (t >= AnchorMinDistance && geometry.Normal[triangle].y < 0.7f)
                                AddToBucket(buckets, origin + directions[d] * t, geometry.Normal[triangle], 1f - geometry.Absorption[triangle], geometry.Hardness[triangle]);
                        }

                        // Inside something: most rays find the backs of faces.
                        bool solid = hits >= n / 2 && backs > hits / 2;
                        int fronts = hits - backs;
                        var cell = new SoundCell
                        {
                            Size = fronts > 0 ? distances / fronts : reach,
                            Enclosure = fronts / (float)n,
                            Absorption = weights > 0f ? absorption / weights : 0f,
                            Hardness = weights > 0f ? hardness / weights : 0f,
                            Wetness = weights > 0f ? wetness / weights : 0f,
                            NearestDirection = nearestDirection,
                            NearestDistance = fronts > 0 ? nearest : reach,
                            AnchorA = SoundFieldData.NoAnchor,
                            AnchorB = SoundFieldData.NoAnchor,
                        };
                        scratch.Set(index, solid, cell);
                        if (!solid)
                            open.Add(index);
                    }
                }

                // Links: where sound passes straight from a cell to the next (both open, nothing between their middles),
                // the ways it finds round corners.
                var links = new byte[count];
                int[] steps = { 1, cells.x, cells.x * cells.y };
                for (int i = 0; i < open.Count; i++)
                {
                    if (i % 4096 == 0 && EditorUtility.DisplayCancelableProgressBar("Baking Sound Field", $"{field.name}: ways round corners", i / (float)open.Count))
                        return false;
                    int cellIndex = open[i];
                    int cx = cellIndex % cells.x, cy = cellIndex / cells.x % cells.y, cz = cellIndex / (cells.x * cells.y);
                    var from = toWorld.MultiplyPoint3x4(scratch.CellCenter(cx, cy, cz));
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if ((axis == 0 ? cx + 1 >= cells.x : axis == 1 ? cy + 1 >= cells.y : cz + 1 >= cells.z) || !scratch.IsOpen(cellIndex + steps[axis]))
                            continue;
                        var to = toWorld.MultiplyPoint3x4(scratch.CellCenter(cx + (axis == 0 ? 1 : 0), cy + (axis == 1 ? 1 : 0), cz + (axis == 2 ? 1 : 0)));
                        var between = to - from;
                        float distance = between.magnitude;
                        if (!geometry.Raycast(from, between / distance, distance, out _, out _, out _))
                            links[cellIndex] |= (byte)(1 << axis);
                    }
                }

                var doorways = FindDoorways(scratch, links, open);

                // Echo anchors: the most-seen, most reflective surfaces far from the cells.
                int totalAnchorHits = buckets.Values.Sum(b => b.Count);
                int minimum = Mathf.Max(8, (int)(totalAnchorHits * 0.002f));
                var anchors = buckets.Values
                    .Where(b => b.Count >= minimum)
                    .OrderByDescending(b => b.Count * (b.Reflectivity / b.Count))
                    .Take(MaxAnchors)
                    .Select(b => new SoundFieldAnchor
                    {
                        Position = toLocal.MultiplyPoint3x4(b.Position / b.Count),
                        Normal = toLocal.MultiplyVector(b.Normal).normalized,
                        Area = b.Count,
                        Reflectivity = b.Reflectivity / b.Count,
                        Hardness = b.Hardness / b.Count,
                    })
                    .ToList();

                // Each open cell's two best anchors it can see (facing it, close and big, nothing in the way).
                if (anchors.Count > 0)
                {
                    var scored = new List<(float score, int anchor)>(anchors.Count);
                    for (int i = 0; i < open.Count; i++)
                    {
                        if (i % 2048 == 0 && EditorUtility.DisplayCancelableProgressBar("Baking Sound Field", $"{field.name}: echo anchors", i / (float)open.Count))
                            return false;
                        int cellIndex = open[i];
                        int cx = cellIndex % cells.x, cy = cellIndex / cells.x % cells.y, cz = cellIndex / (cells.x * cells.y);
                        var local = scratch.CellCenter(cx, cy, cz);
                        scored.Clear();
                        for (int a = 0; a < anchors.Count; a++)
                        {
                            var toCell = local - anchors[a].Position;
                            float distance = toCell.magnitude;
                            if (distance < 1f || Vector3.Dot(anchors[a].Normal, toCell) <= 0f)
                                continue;
                            scored.Add((anchors[a].Reflectivity * anchors[a].Area / (1f + distance * distance * 0.01f), a));
                        }
                        scored.Sort((p, q) => q.score.CompareTo(p.score));
                        ushort first = SoundFieldData.NoAnchor, second = SoundFieldData.NoAnchor;
                        var worldCell = toWorld.MultiplyPoint3x4(local);
                        for (int s = 0; s < scored.Count && s < 4 && second == SoundFieldData.NoAnchor; s++)
                        {
                            var target = toWorld.MultiplyPoint3x4(anchors[scored[s].anchor].Position);
                            var toTarget = target - worldCell;
                            float distance = toTarget.magnitude;
                            if (geometry.Raycast(worldCell, toTarget / distance, distance - 1.5f, out _, out _, out _))
                                continue; // something in the way
                            if (first == SoundFieldData.NoAnchor) first = (ushort)scored[s].anchor;
                            else second = (ushort)scored[s].anchor;
                        }
                        scratch.TryGet(cellIndex, out var cell);
                        cell.AnchorA = first;
                        cell.AnchorB = second;
                        scratch.Set(cellIndex, false, cell);
                    }
                }

                var data = field.Data != null ? field.Data : CreateAsset(field);
                Undo.RecordObject(data, "Bake Sound Field");
                data.Cells = cells;
                data.CellSize = cellSize;
                data.Box = box;
                data.Rays = field.BakeRays;
                data.Packed = packed;
                data.Links = links;
                data.Doorways = doorways;
                data.Anchors = anchors;
                data.BakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
                if (field.Data != data)
                {
                    Undo.RecordObject(field, "Bake Sound Field");
                    field.Data = data;
                    EditorUtility.SetDirty(field);
                }
                SceneView.RepaintAll();
                Debug.Log($"Sound Field \"{field.name}\": baked {count:N0} cells ({open.Count:N0} open) against {geometry.TriangleCount:N0} triangles, {anchors.Count} echo anchors, {doorways.Count} doorways.", field);
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UnityEngine.Object.DestroyImmediate(scratch);
            }
        }

        /// <summary>How far to either side of a step a wall must be for the step to be a doorway, in cells.</summary>
        public const int DoorwayReach = 3;

        // Doorways: steps between cells along X or Z where, within a few cells to either side, the same step is blocked
        // by a wall with open space in front of it (a corridor's own sides don't count: nothing open faces them).
        // Neighbouring doorway steps are one doorway.
        private static List<SoundFieldDoorway> FindDoorways(SoundFieldData data, byte[] links, List<int> open)
        {
            var cells = data.Cells;
            int[] steps = { 1, cells.x, cells.x * cells.y };
            var found = new HashSet<int>();
            foreach (int cell in open)
                for (int axis = 0; axis <= 2; axis += 2)
                {
                    if ((links[cell] & (1 << axis)) == 0)
                        continue;
                    int lateral = axis == 0 ? 2 : 0;
                    if (WallBeside(data, links, steps, cell, axis, lateral, 1) && WallBeside(data, links, steps, cell, axis, lateral, -1))
                        found.Add(cell * 3 + axis);
                }

            var doorways = new List<SoundFieldDoorway>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            var members = new List<int>();
            foreach (int start in found)
            {
                if (!seen.Add(start))
                    continue;
                members.Clear();
                queue.Enqueue(start);
                int axis = start % 3, lateral = axis == 0 ? 2 : 0;
                while (queue.Count > 0)
                {
                    int key = queue.Dequeue();
                    members.Add(key);
                    int cell = key / 3;
                    // Beside it (across the opening) and above or below it.
                    foreach (int step in new[] { steps[lateral], -steps[lateral], steps[1], -steps[1] })
                    {
                        int next = (cell + step) * 3 + axis;
                        if (found.Contains(next) && seen.Add(next))
                            queue.Enqueue(next);
                    }
                }
                var middle = Vector3.zero;
                var across = new HashSet<int>();
                foreach (int key in members)
                {
                    int cell = key / 3;
                    int x = cell % cells.x, y = cell / cells.x % cells.y, z = cell / (cells.x * cells.y);
                    middle += data.CellCenter(x, y, z) + (axis == 0 ? Vector3.right : Vector3.forward) * (data.CellSize * 0.5f);
                    across.Add(lateral == 0 ? x : z);
                }
                doorways.Add(new SoundFieldDoorway
                {
                    Position = middle / members.Count, // (cell middles are in the field's space)
                    Normal = axis == 0 ? Vector3.right : Vector3.forward,
                    Width = across.Count * data.CellSize,
                    Links = members.ToArray(),
                });
            }
            return doorways;
        }

        // Whether the step from a cell along an axis is walled off a few cells to one side (a wall facing open space).
        private static bool WallBeside(SoundFieldData data, byte[] links, int[] steps, int cell, int axis, int lateral, int sign)
        {
            var cells = data.Cells;
            int coordinate = lateral == 0 ? cell % cells.x : cell / (cells.x * cells.y);
            int limit = lateral == 0 ? cells.x : cells.z;
            for (int k = 1; k <= DoorwayReach; k++)
            {
                int at = coordinate + sign * k;
                if (at < 0 || at >= limit)
                    return false; // the field's edge isn't a wall
                int beside = cell + sign * k * steps[lateral];
                if ((links[beside] & (1 << axis)) != 0)
                    continue;
                // Blocked: a wall across the step if there's open space on either side of it.
                return data.IsOpen(beside) || data.IsOpen(beside + steps[axis]);
            }
            return false;
        }

        private static void AddToBucket(Dictionary<(int, int, int, int), Bucket> buckets, Vector3 point, Vector3 normal, float reflectivity, float hardness)
        {
            // Hits on the same patch of the same face: the same grid square and the same main facing.
            var a = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            int facing = a.x >= a.y && a.x >= a.z ? (normal.x > 0 ? 0 : 1) : a.y >= a.z ? (normal.y > 0 ? 2 : 3) : (normal.z > 0 ? 4 : 5);
            var key = (Mathf.FloorToInt(point.x / AnchorGrid), Mathf.FloorToInt(point.y / AnchorGrid), Mathf.FloorToInt(point.z / AnchorGrid), facing);
            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = new Bucket();
            bucket.Count++;
            bucket.Position += point;
            bucket.Normal += normal;
            bucket.Reflectivity += reflectivity;
            bucket.Hardness += hardness;
        }

        private static SoundFieldData CreateAsset(SoundField field)
        {
            EnsureFolder(Folder);
            string scene = field.gameObject.scene.IsValid() && !string.IsNullOrEmpty(field.gameObject.scene.name) ? field.gameObject.scene.name : "Scene";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{scene} - {field.name}.asset");
            var data = ScriptableObject.CreateInstance<SoundFieldData>();
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// The scene's meshes around a field as world-space triangles, each with its Surface Material's values, in a
    /// bounding volume tree for fast rays.
    /// </summary>
    internal sealed class BakeGeometry
    {
        public int TriangleCount => _a.Length;
        public float[] Absorption, Hardness, Wetness;
        public Vector3[] Normal;

        private Vector3[] _a, _edge1, _edge2;
        private int[] _order;
        private Node[] _nodes;

        private struct Node
        {
            public Vector3 Min, Max;
            public int Left, Right;   // children (-1 in a leaf)
            public int Start, Count;  // the leaf's triangles in _order
        }

        public static BakeGeometry Gather(SoundField field)
        {
            var a = new List<Vector3>();
            var b = new List<Vector3>();
            var c = new List<Vector3>();
            var absorption = new List<float>();
            var hardness = new List<float>();
            var wetness = new List<float>();

            // The field's box in the world, grown by how far rays reach.
            var fieldBounds = TransformBounds(field.transform.localToWorldMatrix, field.Box);
            fieldBounds.Expand(Mathf.Max(1f, field.BakeReach) * 2f);

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
                {
                    var mesh = filter.sharedMesh;
                    if (mesh == null || (field.BakeLayers.value & (1 << filter.gameObject.layer)) == 0 || filter.transform.IsChildOf(field.transform))
                        continue;
                    if (!filter.TryGetComponent(out Renderer renderer) || !renderer.enabled || !renderer.bounds.Intersects(fieldBounds))
                        continue;
                    var paint = filter.GetComponent<SurfacePaint>();
                    var objectMaterial = SurfaceMaterialTag.Of(filter.gameObject);
                    var vertices = mesh.vertices;
                    var triangles = mesh.triangles;
                    var matrix = filter.transform.localToWorldMatrix;
                    for (int t = 0; t < triangles.Length / 3; t++)
                    {
                        a.Add(matrix.MultiplyPoint3x4(vertices[triangles[t * 3]]));
                        b.Add(matrix.MultiplyPoint3x4(vertices[triangles[t * 3 + 1]]));
                        c.Add(matrix.MultiplyPoint3x4(vertices[triangles[t * 3 + 2]]));
                        // No material (not painted, not tagged): bare and fully hard -- the most reverb and echo.
                        var material = (paint != null ? paint.MaterialOfTriangle(t) : null) ?? objectMaterial;
                        absorption.Add(material != null ? Mathf.Clamp01(material.Absorption) : 0f);
                        hardness.Add(material != null ? Mathf.Clamp01(material.Hardness) : 1f);
                        wetness.Add(material != null ? Mathf.Clamp01(material.Wetness) : 0f);
                    }
                }
            }

            var geometry = new BakeGeometry
            {
                Absorption = absorption.ToArray(),
                Hardness = hardness.ToArray(),
                Wetness = wetness.ToArray(),
            };
            int count = a.Count;
            geometry._a = a.ToArray();
            geometry._edge1 = new Vector3[count];
            geometry._edge2 = new Vector3[count];
            geometry.Normal = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                geometry._edge1[i] = b[i] - a[i];
                geometry._edge2[i] = c[i] - a[i];
                geometry.Normal[i] = Vector3.Cross(geometry._edge1[i], geometry._edge2[i]).normalized;
            }
            geometry.Build(b, c);
            return geometry;
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds local)
        {
            var result = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            var e = local.extents;
            for (int corner = 0; corner < 8; corner++)
                result.Encapsulate(matrix.MultiplyPoint3x4(local.center + new Vector3((corner & 1) == 0 ? -e.x : e.x, (corner & 2) == 0 ? -e.y : e.y, (corner & 4) == 0 ? -e.z : e.z)));
            return result;
        }

        // A median-split tree over the triangles' centres, four triangles to a leaf.
        private void Build(List<Vector3> b, List<Vector3> c)
        {
            int count = _a.Length;
            _order = Enumerable.Range(0, count).ToArray();
            var centers = new Vector3[count];
            var min = new Vector3[count];
            var max = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                centers[i] = (_a[i] + b[i] + c[i]) / 3f;
                min[i] = Vector3.Min(_a[i], Vector3.Min(b[i], c[i]));
                max[i] = Vector3.Max(_a[i], Vector3.Max(b[i], c[i]));
            }
            var nodes = new List<Node>(Mathf.Max(1, count / 2));
            if (count > 0)
                BuildNode(nodes, 0, count, centers, min, max);
            _nodes = nodes.ToArray();
        }

        private int BuildNode(List<Node> nodes, int start, int count, Vector3[] centers, Vector3[] min, Vector3[] max)
        {
            var node = new Node { Min = Vector3.positiveInfinity, Max = Vector3.negativeInfinity, Left = -1, Right = -1, Start = start, Count = count };
            for (int i = start; i < start + count; i++)
            {
                node.Min = Vector3.Min(node.Min, min[_order[i]]);
                node.Max = Vector3.Max(node.Max, max[_order[i]]);
            }
            int index = nodes.Count;
            nodes.Add(node);
            if (count <= 4)
                return index;

            var extent = node.Max - node.Min;
            int axis = extent.x >= extent.y && extent.x >= extent.z ? 0 : extent.y >= extent.z ? 1 : 2;
            Array.Sort(_order, start, count, Comparer<int>.Create((p, q) => centers[p][axis].CompareTo(centers[q][axis])));
            int half = count / 2;
            int left = BuildNode(nodes, start, half, centers, min, max);
            int right = BuildNode(nodes, start + half, count - half, centers, min, max);
            node.Left = left;
            node.Right = right;
            node.Count = 0;
            nodes[index] = node;
            return index;
        }

        private readonly int[] _stack = new int[128];

        /// <summary>The nearest triangle a ray hits within <paramref name="reach"/>, and whether it hit its back.</summary>
        public bool Raycast(Vector3 origin, Vector3 direction, float reach, out float distance, out int triangle, out bool back)
        {
            distance = reach;
            triangle = -1;
            back = false;
            if (_nodes == null || _nodes.Length == 0)
                return false;
            var inverse = new Vector3(1f / direction.x, 1f / direction.y, 1f / direction.z);
            int top = 0;
            _stack[top++] = 0;
            while (top > 0)
            {
                ref var node = ref _nodes[_stack[--top]];
                if (!HitsBox(origin, inverse, node.Min, node.Max, distance))
                    continue;
                if (node.Left < 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        int t = _order[i];
                        if (HitsTriangle(origin, direction, t, out float hit) && hit < distance)
                        {
                            distance = hit;
                            triangle = t;
                        }
                    }
                }
                else if (top + 2 <= _stack.Length)
                {
                    _stack[top++] = node.Left;
                    _stack[top++] = node.Right;
                }
            }
            if (triangle < 0)
                return false;
            back = Vector3.Dot(Normal[triangle], direction) > 0f;
            return true;
        }

        private static bool HitsBox(Vector3 origin, Vector3 inverse, Vector3 min, Vector3 max, float reach)
        {
            float t1 = (min.x - origin.x) * inverse.x, t2 = (max.x - origin.x) * inverse.x;
            float near = Mathf.Min(t1, t2), far = Mathf.Max(t1, t2);
            t1 = (min.y - origin.y) * inverse.y; t2 = (max.y - origin.y) * inverse.y;
            near = Mathf.Max(near, Mathf.Min(t1, t2)); far = Mathf.Min(far, Mathf.Max(t1, t2));
            t1 = (min.z - origin.z) * inverse.z; t2 = (max.z - origin.z) * inverse.z;
            near = Mathf.Max(near, Mathf.Min(t1, t2)); far = Mathf.Min(far, Mathf.Max(t1, t2));
            return far >= Mathf.Max(near, 0f) && near <= reach;
        }

        private bool HitsTriangle(Vector3 origin, Vector3 direction, int t, out float distance)
        {
            distance = 0f;
            var e1 = _edge1[t];
            var e2 = _edge2[t];
            var p = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            var s = origin - _a[t];
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 1e-4f;
        }
    }

    /// <summary>The Sound Field's Inspector: its settings, what's baked, and Bake / Clear.</summary>
    [CustomEditor(typeof(SoundField))]
    public class SoundFieldEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var field = (SoundField)target;
            EditorGUILayout.Space();

            var cells = SoundFieldBaker.CellsFor(field);
            long count = (long)cells.x * cells.y * cells.z;
            EditorGUILayout.HelpBox($"{cells.x} × {cells.y} × {cells.z} = {count:N0} cells (about {count * SoundFieldData.Stride / 1024f / 1024f:0.0} MB baked).", MessageType.None);
            if (field.Data != null && field.Data.Count > 0)
            {
                var data = field.Data;
                EditorGUILayout.HelpBox($"Baked {data.BakedAt}: {data.Count:N0} cells, {data.Anchors?.Count ?? 0} echo anchors.", MessageType.Info);
                if (SoundFieldBaker.IsStale(field))
                    EditorGUILayout.HelpBox("The box, cell size or rays changed since the bake: bake again.", MessageType.Warning);
            }
            else
                EditorGUILayout.HelpBox("Not baked yet: Bake to measure the space (Sound Cues ignore it until then).", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Bake", GUILayout.Height(26)))
                    EditorApplication.delayCall += () => { if (field != null) SoundFieldBaker.Bake(field); };
                using (new EditorGUI.DisabledScope(field.Data == null))
                    if (GUILayout.Button("Clear", GUILayout.Height(26), GUILayout.Width(70)))
                    {
                        Undo.RecordObject(field.Data, "Clear Sound Field");
                        field.Data.Packed = Array.Empty<byte>();
                        field.Data.Cells = Vector3Int.zero;
                        field.Data.Anchors.Clear();
                        EditorUtility.SetDirty(field.Data);
                        SceneView.RepaintAll();
                    }
            }
        }
    }
}
