using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// A model made from a Mesh Graph: its meshes from LOD0 (full detail) down, and when each gives way to the next.
    /// Shared by every object that asked for the same graph with the same Parameters; released when the last lets go.
    /// </summary>
    public sealed class GeneratedMesh
    {
        /// <summary>LOD0 first.</summary>
        public Mesh[] Levels { get; internal set; } = Array.Empty<Mesh>();

        /// <summary>The same, as data (for batching, and for baking).</summary>
        public List<MeshData> Data { get; internal set; } = new();

        /// <summary>The screen height (a fraction) below which each level gives way to the next; the last one culls it.</summary>
        public float[] ScreenHeights { get; internal set; } = { 0.01f };

        /// <summary>Problems found reading the graph.</summary>
        public List<string> Warnings { get; internal set; } = new();

        internal string Key;
        internal int Users;

        public bool HasLODs => Levels.Length > 1;
    }

    /// <summary>
    /// Makes models from Mesh Graphs without stalling frames: the graph is read on the main thread, then the model
    /// and its LODs are made on a background thread, and only the final upload to Unity meshes happens back on the
    /// main thread. Requests for the same graph and Parameters share one result (and one set of meshes, so the
    /// renderer can batch and instance them).
    /// </summary>
    public static class MeshGraphGenerator
    {
        private static readonly Dictionary<string, GeneratedMesh> _ready = new();
        private static readonly Dictionary<string, Task<GeneratedMesh>> _making = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _ready.Clear();
            _making.Clear();
        }

        /// <summary>
        /// The model a graph makes with these Parameters (null: defaults). Call <see cref="Release"/> when done with
        /// it. Must be called (and is resumed) on the main thread.
        /// </summary>
        public static async Task<GeneratedMesh> Generate(MeshGraph graph, IReadOnlyDictionary<string, float> parameters = null)
        {
            if (graph == null)
                return null;
            string key = KeyOf(graph, parameters);
            if (_ready.TryGetValue(key, out var ready))
            {
                ready.Users++;
                return ready;
            }
            if (!_making.TryGetValue(key, out var making))
            {
                _making[key] = making = Make(graph, parameters, key);
            }
            var result = await making;
            if (result != null)
                result.Users++;
            return result;
        }

        /// <summary>Lets go of a model: its meshes are destroyed once nobody uses them.</summary>
        public static void Release(GeneratedMesh generated)
        {
            if (generated == null || --generated.Users > 0)
                return;
            if (generated.Key != null && _ready.TryGetValue(generated.Key, out var cached) && cached == generated)
                _ready.Remove(generated.Key);
            foreach (var mesh in generated.Levels)
                if (mesh != null)
                    DestroyMesh(mesh);
        }

        /// <summary>Forgets every finished model of a graph (it changed); objects using them keep theirs until they let go.</summary>
        public static void Invalidate(MeshGraph graph)
        {
            string prefix = Prefix(graph);
            var stale = new List<string>();
            foreach (var key in _ready.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                    stale.Add(key);
            foreach (var key in stale)
                _ready.Remove(key);
        }

        private static async Task<GeneratedMesh> Make(MeshGraph graph, IReadOnlyDictionary<string, float> parameters, string key)
        {
            try
            {
                // Main thread: read the graph.
                var plan = MeshCompiler.Compile(graph, parameters);
                // Background: make the model and its LODs (WebGL has no threads: there it's made here).
                List<MeshData> levels = Application.platform == RuntimePlatform.WebGLPlayer
                    ? plan.Run()
                    : await Task.Run(plan.Run);
                // Main thread again: upload.
                var generated = new GeneratedMesh
                {
                    Key = key,
                    Data = levels,
                    Warnings = plan.Warnings,
                    ScreenHeights = plan.ScreenHeights,
                };
                var meshes = new Mesh[levels.Count];
                for (int i = 0; i < levels.Count; i++)
                {
                    meshes[i] = levels[i].ToMesh($"{graph.name} LOD{i}");
                    meshes[i].hideFlags = HideFlags.DontSave;
                }
                generated.Levels = meshes;
                _ready[key] = generated;
                foreach (var warning in plan.Warnings)
                    Debug.LogWarning($"[Mesh Graph {graph.name}] {warning}", graph);
                return generated;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, graph);
                return null;
            }
            finally
            {
                _making.Remove(key);
            }
        }

        private static string Prefix(MeshGraph graph) => graph.GetHashCode() + "|";

        // The graph (and its version: an edited graph makes new models), and the Parameters in name order.
        private static string KeyOf(MeshGraph graph, IReadOnlyDictionary<string, float> parameters)
        {
            var key = new StringBuilder(Prefix(graph)).Append(graph.Version).Append('|');
            if (parameters != null)
            {
                var names = new List<string>(parameters.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names)
                    key.Append(name.ToLowerInvariant()).Append('=').Append(parameters[name].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            return key.ToString();
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(mesh);
            else
                UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
