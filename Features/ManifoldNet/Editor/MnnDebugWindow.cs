using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Editor
{
    /// <summary>
    /// Watch a running network think: each head's trail through its value space (the first two dimensions), how much
    /// the translator uses each head, the recent error, and the training stats.
    /// </summary>
    public class MnnDebugWindow : EditorWindow
    {
        private const int TrailLength = 96;

        private int _network, _instance;
        private bool _paused;
        private ManifoldNet _watched;
        private float[] _points, _values, _heads;
        private readonly List<Vector2>[] _trails = new List<Vector2>[MnnSettings.MaxHeads];
        private float[] _outputs;
        private double _nextSample;
        private Vector2 _scroll;

        public static event System.Action<ManifoldNet, int> ExtraGUI;

        [MenuItem("Tools/Remedy/Manifold Network/Debug Window")]
        public static void Open() => GetWindow<MnnDebugWindow>("Manifold Network");

        public static void Open(ManifoldNet network, int instance)
        {
            var window = GetWindow<MnnDebugWindow>("Manifold Network");
            window._network = Mathf.Max(0, ManifoldNet.Live.ToList().IndexOf(network));
            window._instance = instance;
        }

        private void OnEnable() => EditorApplication.update += Sample;
        private void OnDisable() => EditorApplication.update -= Sample;

        private void Sample()
        {
            if (_paused || EditorApplication.timeSinceStartup < _nextSample)
                return;
            _nextSample = EditorApplication.timeSinceStartup + 0.05;
            var net = Current;
            if (net == null)
                return;
            if (net != _watched)
                Watch(net);

            int h = net.Settings.headCount, d = net.Settings.spaceDimensions;
            _instance = Mathf.Clamp(_instance, 0, net.InstanceCount - 1);
            net.GetHeadPositionsImmediate(_instance, _heads);
            for (int head = 0; head < h; head++)
            {
                var trail = _trails[head];
                var point = new Vector2(_heads[head * d], d > 1 ? _heads[head * d + 1] : 0f);
                trail.Add(point);
                if (trail.Count > TrailLength) trail.RemoveAt(0);
            }
            net.TryGetOutputs(_outputs);
            Repaint();
        }

        private ManifoldNet Current
        {
            get
            {
                var live = ManifoldNet.Live;
                if (live.Count == 0) return null;
                _network = Mathf.Clamp(_network, 0, live.Count - 1);
                return live[_network];
            }
        }

        private void Watch(ManifoldNet net)
        {
            _watched = net;
            net.GetValueSpaces(out _points, out _values);
            _heads = new float[net.Settings.headCount * net.Settings.spaceDimensions];
            _outputs = new float[net.InstanceCount * net.Settings.outputCount];
            for (int i = 0; i < _trails.Length; i++) _trails[i] = new List<Vector2>();
        }

        private void OnGUI()
        {
            var live = ManifoldNet.Live;
            if (live.Count == 0)
            {
                EditorGUILayout.HelpBox("No networks are running. Enter Play Mode with a Brain, or create a ManifoldNet.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _network = EditorGUILayout.Popup(_network, live.Select((n, i) => $"{i}: {n.Name}").ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(220));
                var net0 = Current;
                if (net0 != null && net0.InstanceCount > 1)
                    _instance = EditorGUILayout.IntSlider(_instance, 0, net0.InstanceCount - 1, GUILayout.Width(200));
                GUILayout.FlexibleSpace();
                _paused = GUILayout.Toggle(_paused, "Pause", EditorStyles.toolbarButton);
            }

            var net = Current;
            if (net == null || _heads == null || net != _watched)
                return;
            var s = net.Settings;
            var stats = net.Stats;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(
                $"{(net.UsingGpu ? "GPU" : "CPU")} · {net.InstanceCount} instance(s) · {(net.Training ? s.trainingMode + " training" : "not training")}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Steps {stats.Steps}   Updates {stats.Updates}   Revivals {stats.Revivals}   Rollbacks {stats.Rollbacks}");
            EditorGUILayout.LabelField($"Error trace {stats.ErrorTrace:0.####}   baseline {stats.ErrorBaseline:0.####}   α {stats.Alpha:0.##}");

            // Head trails in their spaces.
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Heads (first two dimensions of each value space)", EditorStyles.miniBoldLabel);
            const float cell = 120f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (cell + 6f)));
            int rows = Mathf.CeilToInt(s.headCount / (float)columns);
            var area = GUILayoutUtility.GetRect(position.width - 20f, rows * (cell + 6f));
            var usage = net.HeadUsage;
            float usageTotal = 0f;
            for (int h = 0; h < s.headCount; h++) usageTotal += usage[h];
            for (int h = 0; h < s.headCount; h++)
            {
                var rect = new Rect(area.x + (h % columns) * (cell + 6f), area.y + (h / columns) * (cell + 6f), cell, cell);
                float share = usageTotal > 0f ? usage[h] / usageTotal : 0f;
                DrawSpace(rect, net, h, share);
            }

            // Usage bars.
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Head usage (how much the translator leans on each)", EditorStyles.miniBoldLabel);
            var bars = GUILayoutUtility.GetRect(position.width - 20f, 40f);
            EditorGUI.DrawRect(bars, new Color(0, 0, 0, 0.2f));
            float barWidth = bars.width / s.headCount;
            for (int h = 0; h < s.headCount; h++)
            {
                float share = usageTotal > 0f ? usage[h] / usageTotal : 0f;
                float height = Mathf.Clamp01(share * s.headCount * 0.5f) * bars.height;
                var color = share < s.reviveThreshold ? new Color(0.9f, 0.4f, 0.4f) : HeadColor(h, s.headCount);
                EditorGUI.DrawRect(new Rect(bars.x + h * barWidth + 1, bars.yMax - height, barWidth - 2, height), color);
            }

            // Error ring.
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recent error", EditorStyles.miniBoldLabel);
            var graph = GUILayoutUtility.GetRect(position.width - 20f, 70f);
            EditorGUI.DrawRect(graph, new Color(0, 0, 0, 0.2f));
            var ring = net.ErrorRing;
            float max = 1e-6f;
            for (int i = 0; i < ring.Length; i++) max = Mathf.Max(max, ring[i]);
            var line = new Vector3[ring.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                float value = ring[(net.ErrorRingIndex + i) % ring.Length];
                line[i] = new Vector3(graph.x + graph.width * i / (ring.Length - 1f), graph.yMax - graph.height * value / max);
            }
            Handles.color = new Color(1f, 0.6f, 0.75f);
            Handles.DrawAAPolyLine(2f, line);
            GUI.Label(new Rect(graph.x + 4, graph.y + 2, 200, 16), $"max {max:0.####}", EditorStyles.miniLabel);

            // Outputs.
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Outputs", EditorStyles.miniBoldLabel);
            for (int n = 0; n < s.outputCount; n++)
            {
                string label = net.OutputLabels != null && n < net.OutputLabels.Length ? net.OutputLabels[n] : $"Output {n}";
                float value = _outputs[_instance * s.outputCount + n];
                var row = EditorGUILayout.GetControlRect();
                EditorGUI.LabelField(new Rect(row.x, row.y, 140, row.height), label);
                var bar = new Rect(row.x + 145, row.y + 3, row.width - 210, row.height - 6);
                EditorGUI.DrawRect(bar, new Color(0, 0, 0, 0.2f));
                float t = Mathf.Clamp(value, -1f, 1f);
                float mid = bar.x + bar.width * 0.5f;
                EditorGUI.DrawRect(t >= 0 ? new Rect(mid, bar.y, bar.width * 0.5f * t, bar.height) : new Rect(mid + bar.width * 0.5f * t, bar.y, -bar.width * 0.5f * t, bar.height),
                    OutputColor(n, s.outputCount));
                EditorGUI.LabelField(new Rect(bar.xMax + 6, row.y, 60, row.height), value.ToString("0.###"));
            }

            ExtraGUI?.Invoke(net, _instance);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSpace(Rect rect, ManifoldNet net, int head, float share)
        {
            var s = net.Settings;
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.12f, 1f));
            Vector2 ToRect(Vector2 p) => new(rect.x + (p.x * 0.5f + 0.5f) * rect.width, rect.y + (0.5f - p.y * 0.5f) * rect.height);

            // Points, colored by their values.
            int d = s.spaceDimensions;
            for (int j = 0; j < s.pointsPerSpace; j++)
            {
                int at = (head * s.pointsPerSpace + j);
                var p = ToRect(new Vector2(_points[at * d], d > 1 ? _points[at * d + 1] : 0f));
                var color = PointColor(_values, at, s.outputCount);
                color.a = 0.55f;
                EditorGUI.DrawRect(new Rect(p.x - 1, p.y - 1, 2, 2), color);
            }

            // The trail, broken where it wraps around the edge.
            var trail = _trails[head];
            Handles.color = HeadColor(head, s.headCount);
            for (int i = 1; i < trail.Count; i++)
            {
                if ((trail[i] - trail[i - 1]).sqrMagnitude > 0.5f) continue;
                Handles.color = new Color(Handles.color.r, Handles.color.g, Handles.color.b, i / (float)trail.Count);
                Handles.DrawAAPolyLine(2f, ToRect(trail[i - 1]), ToRect(trail[i]));
            }
            if (trail.Count > 0)
            {
                var at = ToRect(trail[trail.Count - 1]);
                EditorGUI.DrawRect(new Rect(at.x - 3, at.y - 3, 6, 6), Color.white);
            }
            GUI.Label(new Rect(rect.x + 3, rect.y + 2, rect.width, 16), $"Head {head} · {share:P0}", EditorStyles.miniLabel);
        }

        public static Color OutputColor(int output, int count) => Color.HSVToRGB((output * 0.618034f) % 1f, 0.65f, 1f);

        private static Color HeadColor(int head, int count) => Color.HSVToRGB(0.55f + 0.3f * head / Mathf.Max(1, count), 0.4f, 1f);

        /// <summary>A point's color: positive values pull toward their output's color, negative toward a darker shade of it.</summary>
        public static Color PointColor(float[] values, int point, int outputs)
        {
            Color sum = Color.black;
            float weight = 0f;
            for (int n = 0; n < outputs; n++)
            {
                float v = values[point * outputs + n];
                var color = OutputColor(n, outputs);
                if (v < 0f) color *= 0.35f;
                float w = Mathf.Abs(v);
                sum += color * w;
                weight += w;
            }
            var result = weight > 1e-5f ? sum / weight : Color.gray;
            result.a = 1f;
            return result;
        }
    }
}
