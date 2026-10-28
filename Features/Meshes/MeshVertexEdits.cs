using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>How a proportional edit fades out from the picked vertices to its Radius (the same curves as Blender's).</summary>
    public enum ProportionalFalloff { Smooth, Sphere, Root, Linear, Sharp, Constant }

    /// <summary>
    /// One hand edit: some vertices picked and moved (turned and scaled about their middle, then moved), with the
    /// vertices around them following if it's proportional. The picked vertices are kept as where they were, not by
    /// number, so an edit still finds them when the mesh before it changes a little (another node's settings tweaked).
    /// </summary>
    [Serializable]
    public sealed class VertexMove
    {
        [Tooltip("Where the picked vertices were (before this edit).")]
        public List<Vector3> Picked = new();
        public Vector3 Move;
        [Tooltip("Degrees, about the picked vertices' middle.")]
        public Vector3 Rotate;
        [Tooltip("About the picked vertices' middle (1 is as they are).")]
        public Vector3 Scale = Vector3.one;
        [Tooltip("The vertices around the picked ones follow them, less the further away they are, out to Radius.")]
        public bool Proportional;
        [Tooltip("How far out (meters) the vertices around follow.")]
        public float Radius = 0.25f;
        [Tooltip("How the following fades out: Smooth, Sphere (round), Root, Linear, Sharp (pointy), Constant (all the same).")]
        public ProportionalFalloff Falloff = ProportionalFalloff.Smooth;
        [Tooltip("Only vertices joined to the picked ones (along the surface) follow: moving a lip leaves the other lip alone.")]
        public bool ConnectedOnly;

        public VertexMove Copy()
        {
            var copy = (VertexMove)MemberwiseClone();
            copy.Picked = new List<Vector3>(Picked);
            return copy;
        }
    }

    /// <summary>Replays hand edits (<see cref="VertexMove"/>s) on a mesh. Any thread.</summary>
    public static class MeshVertexEdits
    {
        /// <summary>
        /// Does each edit in turn (the first <paramref name="count"/> of them; -1 all), scaled by
        /// <paramref name="strength"/> (0 none, 1 as made, 2 twice as far). Moves positions and turns normals; works the
        /// normals out again is left to the caller.
        /// </summary>
        public static void Apply(MeshData mesh, IList<VertexMove> moves, float strength = 1f, int count = -1)
        {
            if (moves == null || mesh.VertexCount == 0)
                return;
            int n = count < 0 ? moves.Count : Mathf.Min(count, moves.Count);
            if (n == 0)
                return;
            var groups = new Groups(mesh);
            for (int m = 0; m < n; m++)
                if (moves[m] != null)
                    Apply(mesh, groups, moves[m], strength);
        }

        /// <summary>Does one edit.</summary>
        public static void Apply(MeshData mesh, VertexMove move, float strength = 1f) => Apply(mesh, new Groups(mesh), move, strength);

        private static void Apply(MeshData mesh, Groups groups, VertexMove move, float strength)
        {
            var picked = groups.Find(move.Picked);
            if (picked.Count == 0)
                return;
            var pivot = Vector3.zero;
            foreach (int g in picked)
                pivot += groups.Position(g);
            pivot /= picked.Count;

            var weights = Weights(groups, picked, move);
            var turn = Quaternion.Euler(move.Rotate);
            for (int g = 0; g < groups.Count; g++)
            {
                float w = weights[g] * strength;
                if (w == 0f)
                    continue;
                var spin = Quaternion.SlerpUnclamped(Quaternion.identity, turn, w);
                var scale = Vector3.LerpUnclamped(Vector3.one, move.Scale, w);
                foreach (int v in groups.Members(g))
                {
                    var p = mesh.Positions[v];
                    mesh.Positions[v] = pivot + spin * Vector3.Scale(scale, p - pivot) + move.Move * w;
                    if (v < mesh.Normals.Count)
                        mesh.Normals[v] = spin * mesh.Normals[v];
                }
            }
            groups.Refresh(mesh);
        }

        /// <summary>
        /// How much each place (vertices in one spot count as one) follows an edit: 1 for the picked ones, fading out to
        /// 0 at Radius when it's proportional (along the surface, if Connected Only), 0 for the rest.
        /// </summary>
        public static float[] Weights(MeshData mesh, VertexMove move, out Groups groups)
        {
            groups = new Groups(mesh);
            return Weights(groups, groups.Find(move.Picked), move);
        }

        private static float[] Weights(Groups groups, HashSet<int> picked, VertexMove move)
        {
            var weights = new float[groups.Count];
            foreach (int g in picked)
                weights[g] = 1f;
            float radius = move.Radius;
            if (!move.Proportional || radius <= 0f)
                return weights;

            float[] distance = move.ConnectedOnly ? groups.SurfaceDistances(picked, radius) : groups.Distances(picked, radius);
            for (int g = 0; g < groups.Count; g++)
                if (!picked.Contains(g) && distance[g] < radius)
                    weights[g] = Fade(1f - distance[g] / radius, move.Falloff);
            return weights;
        }

        /// <summary>A falloff curve: 1 at the picked vertices (<paramref name="t"/> 1) to 0 at the radius (t 0).</summary>
        public static float Fade(float t, ProportionalFalloff falloff)
        {
            t = Mathf.Clamp01(t);
            return falloff switch
            {
                ProportionalFalloff.Sphere => Mathf.Sqrt(2f * t - t * t),
                ProportionalFalloff.Root => Mathf.Sqrt(t),
                ProportionalFalloff.Linear => t,
                ProportionalFalloff.Sharp => t * t,
                ProportionalFalloff.Constant => t > 0f ? 1f : 0f,
                _ => t * t * (3f - 2f * t),
            };
        }

        /// <summary>
        /// A mesh's vertices gathered by place: the vertices a seam or a hard edge splits (one corner, several
        /// vertices) are one place, so they're picked and moved together and the mesh doesn't tear. Knows which places
        /// share an edge, for distances along the surface.
        /// </summary>
        public sealed class Groups
        {
            private readonly List<List<int>> _members = new();
            private readonly List<Vector3> _positions = new();
            private readonly int[] _groupOf;
            private readonly MeshData _mesh;
            private List<HashSet<int>> _neighbours;

            public Groups(MeshData mesh)
            {
                _mesh = mesh;
                _groupOf = new int[mesh.VertexCount];
                var byPlace = new Dictionary<Vector3, int>(new MeshData.PositionComparer());
                for (int v = 0; v < mesh.VertexCount; v++)
                {
                    var p = mesh.Positions[v];
                    if (!byPlace.TryGetValue(p, out int g))
                    {
                        g = _members.Count;
                        byPlace[p] = g;
                        _members.Add(new List<int>());
                        _positions.Add(p);
                    }
                    _members[g].Add(v);
                    _groupOf[v] = g;
                }
            }

            public int Count => _members.Count;
            public Vector3 Position(int group) => _positions[group];
            public List<int> Members(int group) => _members[group];
            public int GroupOf(int vertex) => _groupOf[vertex];

            /// <summary>Takes the places' positions from the mesh again (after it's moved: the groups stay as they were).</summary>
            public void Refresh(MeshData mesh)
            {
                for (int g = 0; g < _members.Count; g++)
                    _positions[g] = mesh.Positions[_members[g][0]];
            }

            /// <summary>The place at (or, failing that, nearest) each position.</summary>
            public HashSet<int> Find(List<Vector3> positions)
            {
                var found = new HashSet<int>();
                if (positions == null || _members.Count == 0)
                    return found;
                var exact = new Dictionary<Vector3, int>(new MeshData.PositionComparer());
                for (int g = 0; g < _positions.Count; g++)
                    exact[_positions[g]] = g;
                foreach (var p in positions)
                {
                    if (exact.TryGetValue(p, out int g))
                    {
                        found.Add(g);
                        continue;
                    }
                    // Moved by a change before this edit: the nearest place.
                    int best = 0;
                    float bestDistance = float.MaxValue;
                    for (int i = 0; i < _positions.Count; i++)
                    {
                        float d = (_positions[i] - p).sqrMagnitude;
                        if (d < bestDistance)
                        {
                            bestDistance = d;
                            best = i;
                        }
                    }
                    found.Add(best);
                }
                return found;
            }

            /// <summary>Straight-line distance from each place to the nearest picked one (radius or more: just "far").</summary>
            public float[] Distances(HashSet<int> picked, float radius)
            {
                var distance = new float[Count];
                // The picked places in a grid of radius-sized cells: each place checks the cells around its own.
                var cells = new Dictionary<(int, int, int), List<Vector3>>();
                (int, int, int) Cell(Vector3 p) => (Mathf.FloorToInt(p.x / radius), Mathf.FloorToInt(p.y / radius), Mathf.FloorToInt(p.z / radius));
                foreach (int g in picked)
                {
                    var key = Cell(_positions[g]);
                    if (!cells.TryGetValue(key, out var list))
                        cells[key] = list = new List<Vector3>();
                    list.Add(_positions[g]);
                }
                for (int g = 0; g < Count; g++)
                {
                    var p = _positions[g];
                    var (cx, cy, cz) = Cell(p);
                    float best = radius * radius;
                    for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (cells.TryGetValue((cx + x, cy + y, cz + z), out var list))
                            foreach (var q in list)
                                best = Mathf.Min(best, (p - q).sqrMagnitude);
                    distance[g] = Mathf.Sqrt(best);
                }
                return distance;
            }

            /// <summary>Distance along the surface (edge by edge) from the nearest picked place, out to the radius.</summary>
            public float[] SurfaceDistances(HashSet<int> picked, float radius)
            {
                var neighbours = Neighbours();
                var distance = new float[Count];
                for (int g = 0; g < Count; g++)
                    distance[g] = float.MaxValue;
                var open = new SortedSet<(float distance, int group)>();
                foreach (int g in picked)
                {
                    distance[g] = 0f;
                    open.Add((0f, g));
                }
                while (open.Count > 0)
                {
                    var (d, g) = open.Min;
                    open.Remove(open.Min);
                    if (d > distance[g])
                        continue;
                    foreach (int next in neighbours[g])
                    {
                        float through = d + Vector3.Distance(_positions[g], _positions[next]);
                        if (through < distance[next] && through < radius)
                        {
                            if (distance[next] != float.MaxValue)
                                open.Remove((distance[next], next));
                            distance[next] = through;
                            open.Add((through, next));
                        }
                    }
                }
                return distance;
            }

            private List<HashSet<int>> Neighbours()
            {
                if (_neighbours != null)
                    return _neighbours;
                _neighbours = new List<HashSet<int>>(Count);
                for (int g = 0; g < Count; g++)
                    _neighbours.Add(new HashSet<int>());
                foreach (var slot in _mesh.Slots)
                    for (int t = 0; t + 2 < slot.Count; t += 3)
                    {
                        int a = _groupOf[slot[t]], b = _groupOf[slot[t + 1]], c = _groupOf[slot[t + 2]];
                        Link(a, b);
                        Link(b, c);
                        Link(c, a);
                    }
                return _neighbours;
            }

            private void Link(int a, int b)
            {
                if (a == b)
                    return;
                _neighbours[a].Add(b);
                _neighbours[b].Add(a);
            }
        }
    }
}
