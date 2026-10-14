using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Editor
{
    /// <summary>
    /// Watch a running network think: each head's trajectory through its value space (the first two dimensions), with
    /// where the master is steering it (target) and where the nearby points pull it (attractor); its timescale and gate;
    /// how much the translator uses each head; the recent error; and, when those modules are on, the shared plan grid
    /// with its three rulebooks and the episodic memory's slots with recall confidence.
    /// </summary>
    public class MnnDebugWindow : EditorWindow
    {
        private const int TrailLength = 128;

        private int _network, _instance;
        private bool _paused, _byType, _showPlan = true, _showMemory = true;
        private ManifoldNet _watched;
        private float[] _points, _values, _rules, _bank;
        private int[] _types;
        private MnnState _state;
        private readonly List<Vector2>[] _trails = new List<Vector2>[MnnSettings.MaxHeads];
        private float[] _outputs;
        private double _nextSample, _nextSlow;
        private Vector2 _scroll;
        private readonly List<uint> _resets = new();

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

            var s = net.Settings;
            int h = s.headCount, d = s.spaceDimensions;
            _instance = Mathf.Clamp(_instance, 0, net.InstanceCount - 1);
            _state = net.PeekStateImmediate(_instance);
            for (int head = 0; head < h; head++)
            {
                var trail = _trails[head];
                trail.Add(new Vector2(_state.Positions[head * d], d > 1 ? _state.Positions[head * d + 1] : 0f));
                if (trail.Count > TrailLength) trail.RemoveAt(0);
            }
            net.TryGetOutputs(_outputs);
            if (net.LastPlanReset != 0 && (_resets.Count == 0 || _resets[_resets.Count - 1] != net.LastPlanReset))
                _resets.Add(net.LastPlanReset);

            // Slower reads: the rulebooks and the memory bank.
            if (EditorApplication.timeSinceStartup >= _nextSlow)
            {
                _nextSlow = EditorApplication.timeSinceStartup + 0.5;
                if (s.wfcEnabled && _showPlan) _rules = net.GetRulebooks();
                if (s.memoryEnabled && _showMemory) _bank = net.GetMemoryBank();
            }
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
            _types = net.Settings.wfcEnabled ? net.GetPointTypes() : null;
            _outputs = new float[net.InstanceCount * net.Settings.outputCount];
            _rules = null;
            _bank = null;
            _state = null;
            _resets.Clear();
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
                if (net0 != null && net0.Settings.wfcEnabled)
                    _byType = GUILayout.Toggle(_byType, "Points by Type", EditorStyles.toolbarButton);
                _paused = GUILayout.Toggle(_paused, "Pause", EditorStyles.toolbarButton);
            }

            var net = Current;
            if (net == null || _state == null || net != _watched)
                return;
            var s = net.Settings;
            var stats = net.Stats;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(
                $"{(net.UsingGpu ? "GPU" : "CPU")} · {net.InstanceCount} instance(s) · {(net.Training ? s.trainingMode + " training" : "not training")} · {s.updateRule}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Steps {stats.Steps}   Updates {stats.Updates}   Revivals {stats.Revivals}   Rollbacks {stats.Rollbacks}");
            EditorGUILayout.LabelField($"Error trace {stats.ErrorTrace:0.####}   baseline {stats.ErrorBaseline:0.####}   α {stats.Alpha:0.##}");

            DrawHeads(net);
            DrawUsage(net);
            DrawRing("Recent error", net.ErrorRing, net.ErrorRingIndex, new Color(1f, 0.6f, 0.75f), true);
            DrawOutputs(net);
            if (s.wfcEnabled) DrawPlan(net);
            if (s.memoryEnabled) DrawMemory(net);

            ExtraGUI?.Invoke(net, _instance);
            EditorGUILayout.EndScrollView();
        }

        #region Heads

        private void DrawHeads(ManifoldNet net)
        {
            var s = net.Settings;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Heads (first two dimensions of each value space): trail, ○ target, + attractor", EditorStyles.miniBoldLabel);
            const float cell = 140f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 20f) / (cell + 6f)));
            int rows = Mathf.CeilToInt(s.headCount / (float)columns);
            var area = GUILayoutUtility.GetRect(position.width - 20f, rows * (cell + 20f));
            var usage = net.HeadUsage;
            float usageTotal = 0f;
            for (int h = 0; h < s.headCount; h++) usageTotal += usage[h];
            for (int h = 0; h < s.headCount; h++)
            {
                var rect = new Rect(area.x + (h % columns) * (cell + 6f), area.y + (h / columns) * (cell + 20f), cell, cell);
                float share = usageTotal > 0f ? usage[h] / usageTotal : 0f;
                DrawSpace(rect, net, h, share);
            }
        }

        private void DrawSpace(Rect rect, ManifoldNet net, int head, float share)
        {
            var s = net.Settings;
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.12f, 1f));
            Vector2 ToRect(Vector2 p) => new(rect.x + (p.x * 0.5f + 0.5f) * rect.width, rect.y + (0.5f - p.y * 0.5f) * rect.height);
            int d = s.spaceDimensions;
            Vector2 At(float[] a, int index) => new(a[index * d], d > 1 ? a[index * d + 1] : 0f);

            // Points, colored by their values (or by type with the plan grid on).
            for (int j = 0; j < s.pointsPerSpace; j++)
            {
                int at = head * s.pointsPerSpace + j;
                var p = ToRect(At(_points, at));
                var color = _byType && _types != null ? TypeColor(_types[at], s.typeCount) : PointColor(_values, at, s.outputCount);
                color.a = 0.55f;
                EditorGUI.DrawRect(new Rect(p.x - 1, p.y - 1, 2, 2), color);
            }

            // The trajectory, broken where it wraps around the edge, fading into the past.
            var trail = _trails[head];
            var headColor = HeadColor(head, s.headCount);
            for (int i = 1; i < trail.Count; i++)
            {
                if ((trail[i] - trail[i - 1]).sqrMagnitude > 0.5f) continue;
                Handles.color = new Color(headColor.r, headColor.g, headColor.b, i / (float)trail.Count);
                Handles.DrawAAPolyLine(2f, ToRect(trail[i - 1]), ToRect(trail[i]));
            }

            // Where it's steered and where it settles.
            var target = ToRect(At(_state.Targets, head));
            Handles.color = new Color(1f, 1f, 1f, 0.7f);
            Handles.DrawWireDisc(target, Vector3.forward, 4f);
            var attractor = ToRect(At(_state.Attractors, head));
            Handles.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Handles.DrawLine(attractor + new Vector2(-3, 0), attractor + new Vector2(3, 0));
            Handles.DrawLine(attractor + new Vector2(0, -3), attractor + new Vector2(0, 3));
            var now = ToRect(At(_state.Positions, head));
            EditorGUI.DrawRect(new Rect(now.x - 3, now.y - 3, 6, 6), Color.white);

            // The committed plan type for now, if any.
            string plan = "";
            if (s.wfcEnabled && _state.PlanType.Length > 0)
            {
                int type = _state.PlanType[head * s.PlanColumns];
                plan = type >= 0 ? $" · T{type}" : "";
            }
            GUI.Label(new Rect(rect.x + 3, rect.y + 2, rect.width, 16), $"Head {head} · {share:P0}{plan}", EditorStyles.miniLabel);
            float tau = MnnCpu.HeadTimescale(s, head);
            float gate = _state.Gates[head];
            float rate = MnnCpu.LiquidRate(s.TickDt, tau, gate, s.liquidGates);
            GUI.Label(new Rect(rect.x, rect.yMax + 1, rect.width, 16),
                s.liquidGates ? $"τ {tau:0.00}s  g {gate:+0.0;-0.0}  rate {rate:0.00}" : $"τ {tau:0.00}s  rate {rate:0.00}", EditorStyles.miniLabel);
        }

        private void DrawUsage(ManifoldNet net)
        {
            var s = net.Settings;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Head usage (how much the translator leans on each)", EditorStyles.miniBoldLabel);
            var bars = GUILayoutUtility.GetRect(position.width - 20f, 40f);
            EditorGUI.DrawRect(bars, new Color(0, 0, 0, 0.2f));
            var usage = net.HeadUsage;
            float total = 0f;
            for (int h = 0; h < s.headCount; h++) total += usage[h];
            float barWidth = bars.width / s.headCount;
            for (int h = 0; h < s.headCount; h++)
            {
                float share = total > 0f ? usage[h] / total : 0f;
                float height = Mathf.Clamp01(share * s.headCount * 0.5f) * bars.height;
                var color = share < s.reviveThreshold ? new Color(0.9f, 0.4f, 0.4f) : HeadColor(h, s.headCount);
                EditorGUI.DrawRect(new Rect(bars.x + h * barWidth + 1, bars.yMax - height, barWidth - 2, height), color);
            }
        }

        private void DrawRing(string label, System.ReadOnlySpan<float> ring, int start, Color color, bool autoScale)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            var graph = GUILayoutUtility.GetRect(position.width - 20f, 60f);
            EditorGUI.DrawRect(graph, new Color(0, 0, 0, 0.2f));
            float max = 1f;
            if (autoScale)
            {
                max = 1e-6f;
                for (int i = 0; i < ring.Length; i++) max = Mathf.Max(max, ring[i]);
            }
            var line = new Vector3[ring.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                float value = ring[(start + i) % ring.Length];
                line[i] = new Vector3(graph.x + graph.width * i / (ring.Length - 1f), graph.yMax - graph.height * Mathf.Clamp01(value / max));
            }
            Handles.color = color;
            Handles.DrawAAPolyLine(2f, line);
            GUI.Label(new Rect(graph.x + 4, graph.y + 2, 200, 16), $"max {max:0.####}", EditorStyles.miniLabel);
        }

        private void DrawOutputs(ManifoldNet net)
        {
            var s = net.Settings;
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
        }

        #endregion

        #region Plan grid

        private void DrawPlan(ManifoldNet net)
        {
            var s = net.Settings;
            EditorGUILayout.Space();
            _showPlan = EditorGUILayout.Foldout(_showPlan, $"Plan grid ({s.wfcAxis}): heads down, now → horizon across", true);
            if (!_showPlan || _state.PlanType.Length == 0)
                return;
            int f = s.PlanColumns, t = s.typeCount;
            const float size = 16f;
            var area = GUILayoutUtility.GetRect(position.width - 20f, s.headCount * size + 22f);

            // Committed cells in their type's color; undecided ones faded by their top type's probability.
            for (int h = 0; h < s.headCount; h++)
                for (int col = 0; col < f; col++)
                {
                    int c = h * f + col;
                    var rect = new Rect(area.x + 40 + col * (size + 2), area.y + h * size, size, size - 2);
                    int committed = _state.PlanType[c];
                    Color color;
                    if (committed >= 0)
                        color = TypeColor(committed, t);
                    else
                    {
                        int top = 0;
                        for (int k = 1; k < t; k++) if (_state.PlanSpread[c * t + k] > _state.PlanSpread[c * t + top]) top = k;
                        color = TypeColor(top, t);
                        color.a = Mathf.Clamp01((_state.PlanSpread[c * t + top] - 1f / t) / (1f - 1f / t)) * 0.8f + 0.1f;
                    }
                    EditorGUI.DrawRect(rect, color);
                    if (col == 0) EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.white);
                }
            for (int h = 0; h < s.headCount; h++)
                GUI.Label(new Rect(area.x, area.y + h * size, 40, size), $"H{h}", EditorStyles.miniLabel);
            var stats = net.Stats;
            string resets = _resets.Count == 0 ? "no surprise resets yet" : $"{stats.PlanResets} surprise reset(s), last at step {_resets[_resets.Count - 1]}";
            if (_resets.Count > 0 && stats.Steps - _resets[_resets.Count - 1] < 20)
                EditorGUI.DrawRect(new Rect(area.x + 40 + size + 2, area.yMax - 20, (f - 1) * (size + 2), 2), new Color(1f, 0.3f, 0.3f));
            GUI.Label(new Rect(area.x + 40, area.yMax - 18, 400, 16), resets, EditorStyles.miniLabel);

            // The three rulebooks.
            if (_rules == null || _rules.Length < 3 * t * t)
                return;
            string[] names = { "Coexist", "Follow", "Handoff" };
            const float cellSize = 8f;
            var books = GUILayoutUtility.GetRect(position.width - 20f, t * cellSize + 20f);
            float max = 1e-3f;
            foreach (var v in _rules) max = Mathf.Max(max, Mathf.Abs(v));
            for (int b = 0; b < 3; b++)
            {
                float x0 = books.x + b * (t * cellSize + 30f);
                GUI.Label(new Rect(x0, books.y, 100, 16), names[b], EditorStyles.miniLabel);
                for (int from = 0; from < t; from++)
                    for (int to = 0; to < t; to++)
                    {
                        float v = _rules[b * t * t + from * t + to] / max;
                        var color = v >= 0 ? new Color(0.3f, 0.9f, 0.5f, Mathf.Abs(v)) : new Color(0.95f, 0.35f, 0.35f, Mathf.Abs(v));
                        var rect = new Rect(x0 + to * cellSize, books.y + 16 + from * cellSize, cellSize - 1, cellSize - 1);
                        EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.3f));
                        EditorGUI.DrawRect(rect, color);
                    }
            }
        }

        #endregion

        #region Memory

        private void DrawMemory(ManifoldNet net)
        {
            var s = net.Settings;
            EditorGUILayout.Space();
            _showMemory = EditorGUILayout.Foldout(_showMemory, $"Episodic memory ({s.memoryScope}, {s.memorySlots} slots, {net.Stats.MemoryWrites} writes)", true);
            if (!_showMemory)
                return;
            DrawRing("Recall confidence c (instance 0)", net.ConfidenceRing, (int)(net.Stats.Steps % 256), new Color(0.5f, 0.8f, 1f), false);
            if (_bank == null || _bank.Length == 0)
                return;

            int stride = s.MemoryStride, keyWidth = s.hiddenWidth;
            int bank = s.memoryScope == MemoryScope.Shared ? 0 : _instance;
            int used = 0;
            for (int slot = 0; slot < s.memorySlots; slot++)
            {
                int at = (bank * s.memorySlots + slot) * stride;
                if (at + stride > _bank.Length || _bank[at + stride - 1] < 0.5f) continue;
                used++;
                float strength = _bank[at + keyWidth + s.outputCount];
                float age = _bank[at + keyWidth + s.outputCount + 1];
                float since = _bank[at + keyWidth + s.outputCount + 2];
                float attention = slot < _state.MemoryAttention.Length ? _state.MemoryAttention[slot] : 0f;
                var row = EditorGUILayout.GetControlRect();
                EditorGUI.LabelField(new Rect(row.x, row.y, 60, row.height), $"Slot {slot}", EditorStyles.miniLabel);
                var bar = new Rect(row.x + 60, row.y + 3, 120, row.height - 6);
                EditorGUI.DrawRect(bar, new Color(0, 0, 0, 0.2f));
                float t = Mathf.Clamp(strength / MnnCpu.MemoryMaxStrength, -1f, 1f);
                float mid = bar.x + bar.width * 0.5f;
                EditorGUI.DrawRect(t >= 0 ? new Rect(mid, bar.y, bar.width * 0.5f * t, bar.height) : new Rect(mid + bar.width * 0.5f * t, bar.y, -bar.width * 0.5f * t, bar.height),
                    t >= 0 ? new Color(0.3f, 0.9f, 0.5f) : new Color(0.95f, 0.35f, 0.35f));
                EditorGUI.LabelField(new Rect(bar.xMax + 6, row.y, row.width - 190, row.height),
                    $"strength {strength:+0.00;-0.00}   age {age:0}s   recalled {since:0}s ago   now {attention:P0}", EditorStyles.miniLabel);
            }
            if (used == 0)
                EditorGUILayout.LabelField("Empty: nothing remembered yet.", EditorStyles.miniLabel);
        }

        #endregion

        public static Color OutputColor(int output, int count) => Color.HSVToRGB((output * 0.618034f) % 1f, 0.65f, 1f);

        public static Color TypeColor(int type, int count) => Color.HSVToRGB((type / (float)Mathf.Max(1, count) + 0.05f) % 1f, 0.7f, 0.95f);

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
