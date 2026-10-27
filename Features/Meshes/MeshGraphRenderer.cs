using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remedy.Meshes
{
    /// <summary>One of a Mesh Graph Renderer's Parameter values.</summary>
    [Serializable]
    public struct MeshParameterValue
    {
        public string Name;
        public float Value;

        public MeshParameterValue(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// Shows a Mesh Graph's model on this object, with its Parameters, and sets up its LODs (a LOD Group with LOD1 and
    /// LOD2 under it). The model is made in the background when the object is enabled, or when a Parameter changes
    /// (Set Graph Parameters on it works too); objects with the same graph and Parameters share one mesh, so they
    /// batch and instance together.
    /// <para>With Batch on, many of them (in play) are merged into one mesh per area and material: far fewer draw
    /// calls for scenery (rocks, fences, trees placed by the hundred).</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Schematics/Meshes/Mesh Graph Renderer")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MeshGraphRenderer : SchematicGraphRunner
    {
        public enum ColliderMode { None, Mesh, Convex }

        [Tooltip("The Mesh Graph that makes the model.")]
        public MeshGraph Graph;

        [Tooltip("The graph's Parameters for this object (ones not listed keep their defaults).")]
        public List<MeshParameterValue> Parameters = new();

        [Tooltip("Materials, by the graph's material slots (slot 0 first).")]
        public Material[] Materials = Array.Empty<Material>();

        [Tooltip("Use the graph's LODs (if its Output makes them).")]
        public bool UseLODs = true;

        public ShadowCastingMode Shadows = ShadowCastingMode.On;

        [Tooltip("A Mesh Collider from the model's simplest LOD (cheap to collide with).")]
        public ColliderMode Collider = ColliderMode.None;

        [Tooltip("In play, merge with other Batch renderers nearby that use the same materials: far fewer draw calls. The object can't move once merged.")]
        public bool Batch;

        [NonSerialized] private GeneratedMesh _generated;
        [NonSerialized] private int _request;
        [NonSerialized] private bool _dirty;
        [NonSerialized] private readonly List<MeshRenderer> _lodRenderers = new();
        [NonSerialized] private int _graphVersion = -1;

        /// <summary>The model being shown (null until it's made).</summary>
        public GeneratedMesh Generated => _generated;

        /// <summary>Raised when a new model has been made and shown.</summary>
        public event Action<MeshGraphRenderer> ModelChanged;

        public override SchematicGraph RunningGraph => Graph;

        /// <summary>Sets one of the graph's Parameters (Set Graph Parameters uses this); the model is made again.</summary>
        public override bool SetGraphParameter(string name, object value)
        {
            if (Graph == null)
                return false;
            bool known = false;
            foreach (var (parameter, _) in Graph.ParameterList)
                if (string.Equals(parameter, name, StringComparison.OrdinalIgnoreCase))
                    known = true;
            if (!known)
                return false;
            float number = value switch
            {
                float f => f,
                int i => i,
                bool b => b ? 1f : 0f,
                double d => (float)d,
                _ => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture),
            };
            SetParameter(name, number);
            return true;
        }

        /// <summary>Sets a Parameter's value; the model is made again (once, at the end of the frame's changes).</summary>
        public void SetParameter(string name, float value)
        {
            for (int i = 0; i < Parameters.Count; i++)
                if (string.Equals(Parameters[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (Parameters[i].Value == value)
                        return;
                    Parameters[i] = new MeshParameterValue(Parameters[i].Name, value);
                    _dirty = true;
                    return;
                }
            Parameters.Add(new MeshParameterValue(name, value));
            _dirty = true;
        }

        /// <summary>Makes the model again now (after changing the graph or settings from code).</summary>
        public void Regenerate() => Generate();

        private void OnEnable() => Generate();

        private void OnDisable()
        {
            _request++;
            MeshBatcher.Remove(this);
            ReleaseGenerated();
        }

        private void OnValidate() => _dirty = true;

        private void Update()
        {
            // Parameters changed this frame (or the graph was edited, in the editor): made again, once.
            if (Graph != null && Graph.Version != _graphVersion && _generated != null)
                _dirty = true;
            if (_dirty)
            {
                _dirty = false;
                Generate();
            }
        }

        private async void Generate()
        {
            int request = ++_request;
            if (Graph == null)
            {
                ReleaseGenerated();
                Show(null);
                return;
            }
            _graphVersion = Graph.Version;
            var parameters = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in Parameters)
                if (!string.IsNullOrWhiteSpace(parameter.Name))
                    parameters[parameter.Name.Trim()] = parameter.Value;

            var generated = await MeshGraphGenerator.Generate(Graph, parameters);
            // Stale (a newer request, or the object went away meanwhile).
            if (request != _request || this == null || !isActiveAndEnabled)
            {
                MeshGraphGenerator.Release(generated);
                return;
            }
            ReleaseGenerated();
            _generated = generated;
            Show(generated);
            ModelChanged?.Invoke(this);
        }

        private void ReleaseGenerated()
        {
            if (_generated != null)
                MeshGraphGenerator.Release(_generated);
            _generated = null;
        }

        private void Show(GeneratedMesh generated)
        {
            var filter = GetComponent<MeshFilter>();
            var renderer = GetComponent<MeshRenderer>();
            var levels = generated != null ? generated.Levels : Array.Empty<Mesh>();
            int count = UseLODs ? levels.Length : Mathf.Min(1, levels.Length);

            filter.sharedMesh = levels.Length > 0 ? levels[0] : null;
            Configure(renderer);

            // LOD1, LOD2... as hidden children (made at runtime, never saved).
            while (_lodRenderers.Count < count - 1)
            {
                var child = new GameObject($"LOD{_lodRenderers.Count + 1}");
                child.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy | HideFlags.NotEditable;
                child.transform.SetParent(transform, false);
                child.AddComponent<MeshFilter>();
                _lodRenderers.Add(child.AddComponent<MeshRenderer>());
            }
            for (int i = 0; i < _lodRenderers.Count; i++)
            {
                var lod = _lodRenderers[i];
                if (lod == null)
                    continue;
                bool used = i + 1 < count;
                lod.gameObject.SetActive(used);
                if (!used)
                    continue;
                lod.GetComponent<MeshFilter>().sharedMesh = levels[i + 1];
                Configure(lod);
            }

            var group = GetComponent<LODGroup>();
            if (count > 1 || (generated != null && generated.ScreenHeights.Length > 0 && generated.ScreenHeights[generated.ScreenHeights.Length - 1] > 0f))
            {
                if (group == null)
                    group = gameObject.AddComponent<LODGroup>();
                var lods = new LOD[Mathf.Max(1, count)];
                var heights = generated != null ? generated.ScreenHeights : new[] { 0f };
                for (int i = 0; i < lods.Length; i++)
                {
                    // Each level shows down to its height; the last down to the cull height.
                    float height = i < lods.Length - 1 ? heights[Mathf.Min(i, heights.Length - 1)] : heights[heights.Length - 1];
                    if (i > 0)
                        height = Mathf.Min(height, lods[i - 1].screenRelativeTransitionHeight - 0.001f);
                    lods[i] = new LOD(Mathf.Max(0f, height), new Renderer[] { i == 0 ? renderer : _lodRenderers[i - 1] });
                }
                group.SetLODs(lods);
                group.RecalculateBounds();
            }
            else if (group != null)
                group.SetLODs(new[] { new LOD(0f, new Renderer[] { renderer }) });

            // A collider from the simplest level.
            var meshCollider = GetComponent<MeshCollider>();
            if (Collider != ColliderMode.None && levels.Length > 0)
            {
                if (meshCollider == null)
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.convex = Collider == ColliderMode.Convex;
                meshCollider.sharedMesh = levels[count > 0 ? count - 1 : 0];
            }
            else if (meshCollider != null)
                meshCollider.sharedMesh = null;

            // Batching (in play): the merged mesh draws it instead.
            if (Batch && Application.isPlaying && generated != null)
                MeshBatcher.Add(this);
        }

        private void Configure(MeshRenderer renderer)
        {
            renderer.sharedMaterials = Materials ?? Array.Empty<Material>();
            renderer.shadowCastingMode = Shadows;
        }

        /// <summary>Its own renderers (LOD0 and the LOD children), for the batcher to hide or show.</summary>
        internal void SetRenderersVisible(bool visible)
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = visible;
            foreach (var lod in _lodRenderers)
                if (lod != null)
                    lod.enabled = visible;
            var group = GetComponent<LODGroup>();
            if (group != null)
                group.enabled = visible;
        }

        private void OnDestroy()
        {
            foreach (var lod in _lodRenderers)
                if (lod != null)
                {
                    if (Application.isPlaying) Destroy(lod.gameObject);
                    else DestroyImmediate(lod.gameObject);
                }
            _lodRenderers.Clear();
        }
    }
}
