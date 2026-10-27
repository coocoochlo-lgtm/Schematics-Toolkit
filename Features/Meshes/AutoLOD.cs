using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// LODs for any mesh: simpler copies made in the background with <see cref="MeshSimplifier"/>. The editor's Auto
    /// LOD tool uses this on models; game code can too (at runtime the mesh must be Read/Write enabled).
    /// </summary>
    public static class AutoLOD
    {
        /// <summary>The default share of triangles for LOD1 and LOD2.</summary>
        public static readonly float[] DefaultQualities = { 0.5f, 0.2f };

        /// <summary>
        /// Simpler copies of a mesh, one per quality (0.5: half the triangles), made on a background thread. Must be
        /// called on the main thread (it reads the mesh and uploads the results there).
        /// </summary>
        public static async Task<Mesh[]> Make(Mesh source, IReadOnlyList<float> qualities = null)
        {
            qualities ??= DefaultQualities;
            var data = MeshData.FromMesh(source);
            var list = new List<float>(qualities);
            List<MeshData> levels = Application.platform == RuntimePlatform.WebGLPlayer
                ? MeshSimplifier.Levels(data, list)
                : await Task.Run(() => MeshSimplifier.Levels(data, list));
            var meshes = new Mesh[levels.Count];
            for (int i = 0; i < levels.Count; i++)
                meshes[i] = levels[i].ToMesh($"{source.name} LOD{i + 1}");
            return meshes;
        }
    }
}
