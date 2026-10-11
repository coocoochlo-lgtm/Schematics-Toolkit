using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    /// <summary>How the brain clouds look (Preferences > Manifold Network Gizmos in the editor).</summary>
    public static class BrainCloudSettings
    {
        public static bool Show = true;
        [Tooltip("Size of the cloud over each agent.")]
        public static float Scale = 0.35f;
        public static float PointSize = 0.025f;
        public static float HeadSize = 0.08f;
        public static float PointOpacity = 0.35f;
        public static int TailLength = 24;
        public static bool SelectedOnly = false;
        public static float MaxDistance = 40f;
        public static bool ShowInGameView = false;
        [Tooltip("Height of the cloud above the top of the agent's bounds.")]
        public static float Gap = 0.3f;
    }

    /// <summary>
    /// Draws a small cloud over every agent with a brain: one mini value space per head, the points as faint orbs in
    /// their outputs' colors (fixed, so they're uploaded once), and the heads as bright orbs moving through them with
    /// fading tails. Everything is GPU instanced, straight from the network's own head buffer.
    /// </summary>
    internal static class BrainClouds
    {
        private sealed class Cloud
        {
            public BrainNetwork Network;
            public GraphicsBuffer Points, Colors, Centers, Tails;
            public int Capacity, TailLength, TailHead;
            public Vector4[] CenterData;
            public MaterialPropertyBlock Properties = new();
        }

        private static readonly Dictionary<BrainNetwork, Cloud> _clouds = new();
        private static Material _material;
        private static ComputeShader _tails;
        private static int _recordKernel = -1;
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            BrainHub.NetworkAdded += network => network.Ticked += OnTicked;
            BrainHub.NetworkRemoved += Release;
        }

        private static Cloud CloudFor(BrainNetwork network)
        {
            if (_clouds.TryGetValue(network, out var cloud) && cloud.Capacity == network.Capacity && cloud.TailLength == Mathf.Max(1, BrainCloudSettings.TailLength))
                return cloud;
            if (cloud != null) ReleaseBuffers(cloud);

            var s = network.Settings;
            cloud ??= new Cloud { Network = network };
            cloud.Capacity = network.Capacity;
            cloud.TailLength = Mathf.Max(1, BrainCloudSettings.TailLength);
            cloud.TailHead = 0;

            network.Net.GetValueSpaces(out var points, out var values);
            cloud.Points = new GraphicsBuffer(GraphicsBuffer.Target.Structured, points.Length, sizeof(float));
            cloud.Points.SetData(points);
            var colors = new Color[s.headCount * s.pointsPerSpace];
            for (int p = 0; p < colors.Length; p++)
                colors[p] = PointColor(network.Definition, values, p, s.outputCount);
            cloud.Colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, colors.Length, sizeof(float) * 4);
            cloud.Colors.SetData(colors);

            cloud.CenterData = new Vector4[cloud.Capacity];
            cloud.Centers = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cloud.Capacity, sizeof(float) * 4);
            cloud.Tails = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Mathf.Max(1, cloud.Capacity * s.headCount * cloud.TailLength * s.spaceDimensions), sizeof(float));
            cloud.Tails.SetData(new float[cloud.Tails.count]);
            _clouds[network] = cloud;
            return cloud;
        }

        /// <summary>
        /// A point's color: positive values pull toward their output's color, negative ones toward a darker shade, so
        /// "strongly left" and "strongly right" look different.
        /// </summary>
        private static Color PointColor(BrainDefinition definition, float[] values, int point, int outputs)
        {
            Color sum = Color.black;
            float weight = 0f;
            for (int n = 0; n < outputs; n++)
            {
                float v = values[point * outputs + n];
                var color = definition.OutputColor(n);
                if (v < 0f) color *= 0.35f;
                float w = Mathf.Abs(v) * Mathf.Abs(v);
                sum += color * w;
                weight += w;
            }
            var result = weight > 1e-6f ? sum / weight : new Color(0.5f, 0.5f, 0.5f);
            result.a = 1f;
            return result;
        }

        private static void OnTicked(BrainNetwork network)
        {
            if (!BrainCloudSettings.Show || !EnsureResources())
                return;
            var cloud = CloudFor(network);
            var s = network.Settings;
            cloud.TailHead = (cloud.TailHead + 1) % cloud.TailLength;
            _tails.SetBuffer(_recordKernel, "_Heads", network.Net.HeadPositions);
            _tails.SetBuffer(_recordKernel, "_Tails", cloud.Tails);
            _tails.SetInt("_Rows", cloud.Capacity);
            _tails.SetInt("_H", s.headCount);
            _tails.SetInt("_D", s.spaceDimensions);
            _tails.SetInt("_HeadStride", network.Net.HeadStride);
            _tails.SetInt("_TailLength", cloud.TailLength);
            _tails.SetInt("_TailHead", cloud.TailHead);
            _tails.Dispatch(_recordKernel, (cloud.Capacity * s.headCount + 63) / 64, 1, 1);
        }

        private static bool EnsureResources()
        {
            if (!SystemInfo.supportsComputeShaders)
                return false;
            if (_material == null)
            {
                var shader = Resources.Load<Shader>("BrainOrbs");
                if (shader == null) return false;
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (_tails == null)
            {
                _tails = Resources.Load<ComputeShader>("BrainTails");
                if (_tails == null) return false;
                _recordKernel = _tails.FindKernel("Record");
            }
            return true;
        }

        /// <summary>Submits this frame's clouds. Called by the brain loop every frame.</summary>
        internal static void Draw()
        {
            if (!BrainCloudSettings.Show || !EnsureResources())
                return;
            foreach (var network in BrainHub.All)
            {
                if (network.Empty) continue;
                var cloud = CloudFor(network);
                if (!UpdateCenters(network, cloud))
                    continue;
                Submit(network, cloud);
            }
        }

        private static readonly Dictionary<BrainAgent, Renderer[]> _renderers = new();

        // Each agent's cloud floats above the top of its world bounds.
        private static bool UpdateCenters(BrainNetwork network, Cloud cloud)
        {
            Vector3 viewer = ViewerPosition();
            float maxDistance = BrainCloudSettings.MaxDistance;
            bool any = false;
            for (int row = 0; row < cloud.Capacity; row++)
            {
                var agent = network.AgentAtRow(row);
                var gameObject = agent?.GameObject;
                if (gameObject == null || !gameObject.activeInHierarchy || (BrainCloudSettings.SelectedOnly && !IsSelected(gameObject)))
                {
                    cloud.CenterData[row] = Vector4.zero;
                    continue;
                }
                if (!_renderers.TryGetValue(agent, out var renderers) || renderers == null || (renderers.Length > 0 && renderers[0] == null))
                    _renderers[agent] = renderers = gameObject.GetComponentsInChildren<Renderer>();

                Bounds bounds = new(gameObject.transform.position, Vector3.zero);
                bool first = true;
                foreach (var renderer in renderers)
                {
                    if (renderer == null || !renderer.enabled) continue;
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                Vector3 center = new(bounds.center.x, bounds.max.y + BrainCloudSettings.Gap + BrainCloudSettings.Scale, bounds.center.z);
                bool visible = (center - viewer).sqrMagnitude <= maxDistance * maxDistance;
                cloud.CenterData[row] = new Vector4(center.x, center.y, center.z, visible ? 1f : 0f);
                any |= visible;
            }
            cloud.Centers.SetData(cloud.CenterData);
            return any;
        }

        private static void Submit(BrainNetwork network, Cloud cloud)
        {
            var s = network.Settings;
            var properties = cloud.Properties;
            properties.SetBuffer("_Points", cloud.Points);
            properties.SetBuffer("_PointColors", cloud.Colors);
            properties.SetBuffer("_Heads", network.Net.HeadPositions);
            properties.SetBuffer("_Centers", cloud.Centers);
            properties.SetBuffer("_Tails", cloud.Tails);
            properties.SetInt("_H", s.headCount);
            properties.SetInt("_P", s.pointsPerSpace);
            properties.SetInt("_D", s.spaceDimensions);
            properties.SetInt("_HeadStride", network.Net.HeadStride);
            properties.SetInt("_TailLength", cloud.TailLength);
            properties.SetInt("_TailHead", cloud.TailHead);
            properties.SetInt("_Rows", cloud.Capacity);
            properties.SetFloat("_Scale", BrainCloudSettings.Scale);
            properties.SetFloat("_PointSize", BrainCloudSettings.PointSize);
            properties.SetFloat("_HeadSize", BrainCloudSettings.HeadSize);
            properties.SetFloat("_Opacity", BrainCloudSettings.PointOpacity);
            properties.SetFloat("_Sigma", Mathf.Max(0.02f, s.dispersion));

            foreach (var camera in Cameras())
            {
                Draw(properties, 0, cloud.Capacity * s.headCount * s.pointsPerSpace, camera);
                Draw(properties, 2, cloud.Capacity * s.headCount * cloud.TailLength, camera);
                Draw(properties, 1, cloud.Capacity * s.headCount, camera);
            }
        }

        private static void Draw(MaterialPropertyBlock properties, int mode, int count, Camera camera)
        {
            if (count <= 0) return;
            properties.SetInt("_Mode", mode);
            var parameters = new RenderParams(_material)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
                matProps = properties,
                camera = camera,
            };
            Graphics.RenderPrimitives(parameters, MeshTopology.Triangles, 6, count);
        }

        // Scene views always; the game view only when asked (and always in builds when shown).
        private static IEnumerable<Camera> Cameras()
        {
            if (BrainCloudSettings.ShowInGameView || !Application.isEditor)
            {
                yield return null; // every camera
                yield break;
            }
#if UNITY_EDITOR
            foreach (UnityEditor.SceneView view in UnityEditor.SceneView.sceneViews)
                if (view != null && view.camera != null)
                    yield return view.camera;
#endif
        }

        private static Vector3 ViewerPosition()
        {
#if UNITY_EDITOR
            if (!BrainCloudSettings.ShowInGameView && UnityEditor.SceneView.lastActiveSceneView != null && UnityEditor.SceneView.lastActiveSceneView.camera != null)
                return UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
#endif
            var main = Camera.main;
            return main != null ? main.transform.position : Vector3.zero;
        }

        private static bool IsSelected(GameObject gameObject)
        {
#if UNITY_EDITOR
            foreach (var selected in UnityEditor.Selection.gameObjects)
                if (selected == gameObject || gameObject.transform.IsChildOf(selected.transform))
                    return true;
            return false;
#else
            return true;
#endif
        }

        private static void Release(BrainNetwork network)
        {
            if (_clouds.TryGetValue(network, out var cloud))
                ReleaseBuffers(cloud);
            _clouds.Remove(network);
            foreach (var agent in network.Agents)
                _renderers.Remove(agent);
        }

        private static void ReleaseBuffers(Cloud cloud)
        {
            cloud.Points?.Release();
            cloud.Colors?.Release();
            cloud.Centers?.Release();
            cloud.Tails?.Release();
            cloud.Points = cloud.Colors = cloud.Centers = cloud.Tails = null;
        }
    }
}
