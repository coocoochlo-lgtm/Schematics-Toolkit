using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Merges Mesh Graph Renderers marked Batch into one mesh per area (a 32 m cell) and set of materials, with LODs:
    /// hundreds of rocks become a handful of draw calls. The merging happens on a background thread; the pieces keep
    /// showing until their merged mesh is ready. Play mode only (merged objects can't move).
    /// </summary>
    internal static class MeshBatcher
    {
        public const float CellSize = 32f;

        private sealed class Cell
        {
            public readonly List<MeshGraphRenderer> Members = new();
            public GameObject Root;
            public Mesh[] Meshes = Array.Empty<Mesh>();
            public int Build;
            public bool Dirty;
            public Vector3 Origin;
        }

        private static readonly Dictionary<(Vector3Int cell, string materials, int levels), Cell> _cells = new();
        private static readonly Dictionary<MeshGraphRenderer, Cell> _memberOf = new();
        private static bool _scheduled;
        private static Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _cells.Clear();
            _memberOf.Clear();
            _scheduled = false;
            _root = null;
        }

        public static void Add(MeshGraphRenderer renderer)
        {
            Remove(renderer, show: false);
            var generated = renderer.Generated;
            if (generated == null || generated.Data.Count == 0)
                return;
            var position = renderer.transform.position;
            var cellIndex = Vector3Int.FloorToInt(position / CellSize);
            int levels = renderer.UseLODs ? generated.Data.Count : 1;
            var key = (cellIndex, MaterialsKey(renderer), levels);
            if (!_cells.TryGetValue(key, out var cell))
                _cells[key] = cell = new Cell { Origin = ((Vector3)cellIndex + Vector3.one * 0.5f) * CellSize };
            cell.Members.Add(renderer);
            cell.Dirty = true;
            _memberOf[renderer] = cell;
            Schedule();
        }

        public static void Remove(MeshGraphRenderer renderer, bool show = true)
        {
            if (renderer == null || !_memberOf.TryGetValue(renderer, out var cell))
                return;
            _memberOf.Remove(renderer);
            cell.Members.Remove(renderer);
            cell.Dirty = true;
            if (show)
                renderer.SetRenderersVisible(true);
            Schedule();
        }

        private static string MaterialsKey(MeshGraphRenderer renderer)
        {
            var key = new System.Text.StringBuilder();
            if (renderer.Materials != null)
                foreach (var material in renderer.Materials)
                    key.Append(material != null ? material.GetHashCode() : 0).Append(',');
            key.Append('|').Append((int)renderer.Shadows);
            return key.ToString();
        }

        // Rebuilds what changed, once, after this frame's changes.
        private static async void Schedule()
        {
            if (_scheduled)
                return;
            _scheduled = true;
            await Task.Yield();
            _scheduled = false;
            foreach (var cell in new List<Cell>(_cells.Values))
                if (cell.Dirty)
                    Rebuild(cell);
        }

        private static async void Rebuild(Cell cell)
        {
            cell.Dirty = false;
            int build = ++cell.Build;
            if (cell.Members.Count == 0)
            {
                Clear(cell);
                return;
            }

            // Main thread: what goes in, where (relative to the cell).
            var members = new List<MeshGraphRenderer>();
            var pieces = new List<(List<MeshData> levels, Matrix4x4 matrix)>();
            var toCell = Matrix4x4.Translate(-cell.Origin);
            foreach (var member in cell.Members)
            {
                if (member == null || member.Generated == null)
                    continue;
                members.Add(member);
                pieces.Add((member.Generated.Data, toCell * member.transform.localToWorldMatrix));
            }
            if (pieces.Count == 0)
            {
                Clear(cell);
                return;
            }
            int levels = members[0].UseLODs ? members[0].Generated.Data.Count : 1;

            // Background: merge each level.
            List<MeshData> Merge()
            {
                var merged = new List<MeshData>();
                for (int level = 0; level < levels; level++)
                {
                    var mesh = new MeshData();
                    foreach (var (pieceLevels, matrix) in pieces)
                        if (pieceLevels.Count > 0)
                            mesh.Append(pieceLevels[Mathf.Min(level, pieceLevels.Count - 1)], matrix);
                    merged.Add(mesh);
                }
                return merged;
            }
            var result = Application.platform == RuntimePlatform.WebGLPlayer ? Merge() : await Task.Run(Merge);
            if (build != cell.Build)
                return; // changed again meanwhile: the newer build wins

            // Main thread: the cell's object, its meshes and LOD Group.
            if (cell.Root == null)
            {
                if (_root == null)
                    _root = new GameObject("[Batched Meshes]").transform;
                cell.Root = new GameObject($"Cell {cell.Origin}");
                cell.Root.transform.SetParent(_root, false);
                cell.Root.transform.position = cell.Origin;
            }
            foreach (var old in cell.Meshes)
                if (old != null)
                    UnityEngine.Object.Destroy(old);
            foreach (Transform child in cell.Root.transform)
                UnityEngine.Object.Destroy(child.gameObject);

            var template = members[0];
            var renderers = new Renderer[result.Count];
            cell.Meshes = new Mesh[result.Count];
            for (int level = 0; level < result.Count; level++)
            {
                var child = new GameObject($"LOD{level}");
                child.transform.SetParent(cell.Root.transform, false);
                cell.Meshes[level] = result[level].ToMesh($"Batched LOD{level}");
                child.AddComponent<MeshFilter>().sharedMesh = cell.Meshes[level];
                var renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = template.Materials;
                renderer.shadowCastingMode = template.Shadows;
                renderers[level] = renderer;
            }
            var heights = template.Generated.ScreenHeights;
            var group = cell.Root.GetComponent<LODGroup>();
            if (group == null)
                group = cell.Root.AddComponent<LODGroup>();
            var lods = new LOD[renderers.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                float height = i < lods.Length - 1 ? heights[Mathf.Min(i, heights.Length - 1)] : heights[heights.Length - 1];
                if (i > 0)
                    height = Mathf.Min(height, lods[i - 1].screenRelativeTransitionHeight - 0.001f);
                lods[i] = new LOD(Mathf.Max(0f, height), new[] { renderers[i] });
            }
            group.SetLODs(lods);
            group.RecalculateBounds();

            // The merged mesh draws them now.
            foreach (var member in members)
                if (member != null && _memberOf.TryGetValue(member, out var memberCell) && memberCell == cell)
                    member.SetRenderersVisible(false);
        }

        private static void Clear(Cell cell)
        {
            foreach (var old in cell.Meshes)
                if (old != null)
                    UnityEngine.Object.Destroy(old);
            cell.Meshes = Array.Empty<Mesh>();
            if (cell.Root != null)
                UnityEngine.Object.Destroy(cell.Root);
            cell.Root = null;
        }
    }
}
