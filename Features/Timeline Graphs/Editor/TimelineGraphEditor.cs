using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace Remedy.Schematics.TimelineGraphs
{
    /// <summary>
    /// Timeline Graphs in the editor: making them (with their Timeline), and the Timeline bar along the top of their
    /// graph window: open the Timeline, and in Play Mode, control it on the objects playing it.
    /// </summary>
    [InitializeOnLoad]
    internal static class TimelineGraphEditor
    {
        static TimelineGraphEditor()
        {
            SchematicGraphEditorWindow.CanvasLoaded += (window, canvas) =>
            {
                canvas.Q("timeline-graph-bar")?.RemoveFromHierarchy();
                if (window.SchematicGraph is TimelineGraph graph)
                    canvas.Add(new TimelineBar(graph));
            };
        }

        #region Making

        [MenuItem("Assets/Create/Schematics/Timeline Graph", priority = 81)]
        private static void Create()
        {
            string folder = "Assets";
            var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(selected))
                folder = AssetDatabase.IsValidFolder(selected) ? selected : Path.GetDirectoryName(selected).Replace('\\', '/');

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New Timeline Graph.asset");
            var graph = ScriptableObject.CreateInstance<TimelineGraph>();
            AssetDatabase.CreateAsset(graph, path);
            CreateTimeline(graph);
            Selection.activeObject = graph;
            EditorGUIUtility.PingObject(graph);
        }

        /// <summary>Makes a Timeline for a graph (next to it), with a Schematic track ready for markers.</summary>
        public static TimelineAsset CreateTimeline(TimelineGraph graph)
        {
            string graphPath = AssetDatabase.GetAssetPath(graph);
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(Path.GetDirectoryName(graphPath), Path.GetFileNameWithoutExtension(graphPath) + " Timeline.playable").Replace('\\', '/'));
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, path);
            timeline.CreateTrack<SchematicTrack>(null, "Schematic");
            Undo.RecordObject(graph, "Create Timeline");
            graph.Timeline = timeline;
            EditorUtility.SetDirty(graph);
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        /// <summary>Opens a Timeline in the Timeline window.</summary>
        public static void Open(TimelineAsset timeline, PlayableDirector director = null)
        {
            if (timeline == null) return;
            try
            {
                var window = TimelineEditor.GetOrCreateWindow();
                if (director != null) window.SetTimeline(director);
                else window.SetTimeline(timeline);
                window.Show();
                window.Focus();
            }
            catch (Exception)
            {
                // Older Timeline versions: select it and open the window.
                Selection.activeObject = timeline;
                EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
            }
        }

        #endregion

        #region Owners

        /// <summary>The Timeline Graph a marker, clip or track belongs to (by its Timeline asset).</summary>
        public static TimelineGraph OwnerOf(UnityEngine.Object part)
        {
            if (part == null) return null;
            var timeline = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(part)) as TimelineAsset;
            if (timeline == null) return null;
            if (_owners.TryGetValue(timeline, out var owner) && owner != null && owner.Timeline == timeline)
                return owner;
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineGraph"))
                if (AssetDatabase.LoadAssetAtPath<TimelineGraph>(AssetDatabase.GUIDToAssetPath(guid)) is { } graph && graph.Timeline == timeline)
                    return _owners[timeline] = graph;
            return null;
        }

        private static readonly Dictionary<TimelineAsset, TimelineGraph> _owners = new();

        #endregion

        /// <summary>The bar along the top of a Timeline Graph's canvas.</summary>
        private sealed class TimelineBar : VisualElement
        {
            private readonly TimelineGraph _graph;
            private readonly Label _title, _time;
            private readonly VisualElement _live;
            private readonly PopupField<TimelineGraphPlayer> _players;
            private readonly Slider _slider;
            private readonly Button _play;
            private List<TimelineGraphPlayer> _choices = new();
            private bool _dragging;

            public TimelineBar(TimelineGraph graph)
            {
                _graph = graph;
                name = "timeline-graph-bar";
                pickingMode = PickingMode.Position;
                style.position = Position.Absolute;
                style.left = 8; style.right = 8; style.top = 8;
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.paddingLeft = 8; style.paddingRight = 8; style.paddingTop = 4; style.paddingBottom = 4;
                style.backgroundColor = new Color(0.12f, 0.11f, 0.16f, 0.92f);
                var accent = new Color(0.93f, 0.52f, 0.78f);
                style.borderBottomColor = accent; style.borderTopColor = accent; style.borderLeftColor = accent; style.borderRightColor = accent;
                style.borderBottomWidth = 1; style.borderTopWidth = 1; style.borderLeftWidth = 1; style.borderRightWidth = 1;
                style.borderTopLeftRadius = 6; style.borderTopRightRadius = 6; style.borderBottomLeftRadius = 6; style.borderBottomRightRadius = 6;
                RegisterCallback<MouseDownEvent>(e => e.StopPropagation());
                RegisterCallback<WheelEvent>(e => e.StopPropagation());

                var icon = new Image { image = EditorGUIUtility.IconContent("TimelineAsset Icon").image };
                icon.style.width = 16; icon.style.height = 16; icon.style.marginRight = 6;
                Add(icon);
                _title = new Label();
                _title.style.unityFontStyleAndWeight = FontStyle.Bold;
                _title.style.color = new Color(0.95f, 0.9f, 1f);
                _title.style.marginRight = 8;
                Add(_title);
                Add(new Button(OpenOrCreate) { text = "Open Timeline", tooltip = "Edit it in the Timeline window (in Play Mode, on the object picked here)." });

                // Play Mode: the objects playing it, and their controls.
                _live = new VisualElement();
                _live.style.flexDirection = FlexDirection.Row;
                _live.style.alignItems = Align.Center;
                _live.style.flexGrow = 1;
                _live.style.marginLeft = 8;
                _players = new PopupField<TimelineGraphPlayer>(_choices, (TimelineGraphPlayer)null, p => p != null ? p.Host.name : "—", p => p != null ? p.Host.name : "—");
                _players.style.minWidth = 90;
                _live.Add(_players);
                _play = new Button(() => { var p = Current; if (p == null) return; if (p.IsPlaying) p.Pause(); else if (p.Director.state == PlayState.Paused) p.Resume(); else p.Play(false); }) { text = "▶" };
                _live.Add(_play);
                _live.Add(new Button(() => Current?.Stop()) { text = "■", tooltip = "Stop" });
                _live.Add(new Button(() => Current?.Play(true)) { text = "↺", tooltip = "Play from the start" });
                _slider = new Slider(0, 1) { style = { flexGrow = 1, minWidth = 60 } };
                _slider.RegisterValueChangedCallback(evt => { if (_dragging) Current?.Seek(evt.newValue); });
                _slider.RegisterCallback<PointerDownEvent>(_ => _dragging = true, TrickleDown.TrickleDown);
                _slider.RegisterCallback<PointerUpEvent>(_ => _dragging = false, TrickleDown.TrickleDown);
                _live.Add(_slider);
                _time = new Label { style = { minWidth = 80, unityTextAlign = TextAnchor.MiddleRight, color = new Color(0.8f, 0.8f, 0.85f) } };
                _live.Add(_time);
                Add(_live);

                schedule.Execute(Refresh).Every(100);
                Refresh();
            }

            private TimelineGraphPlayer Current => _players.value;

            private void OpenOrCreate()
            {
                if (_graph.Timeline == null)
                    CreateTimeline(_graph);
                Open(_graph.Timeline, Application.isPlaying ? Current?.Director : null);
            }

            private void Refresh()
            {
                _title.text = _graph.Timeline != null ? $"Timeline · {_graph.Timeline.name}" : "Timeline · (none yet: Open Timeline makes one)";

                bool live = Application.isPlaying;
                _live.style.display = live ? DisplayStyle.Flex : DisplayStyle.None;
                if (!live)
                    return;

                var players = UnityEngine.Object.FindObjectsByType<TimelineGraphPlayer>(FindObjectsSortMode.None).Where(p => p.Graph == _graph).ToList();
                if (!players.SequenceEqual(_choices))
                {
                    var keep = Current;
                    _choices = players;
                    _players.choices = _choices;
                    _players.SetValueWithoutNotify(keep != null && players.Contains(keep) ? keep : players.FirstOrDefault());
                }
                var current = Current;
                _play.SetEnabled(current != null);
                _slider.SetEnabled(current != null);
                if (current == null)
                {
                    _time.text = "not playing anywhere";
                    return;
                }
                _play.text = current.IsPlaying ? "❚❚" : "▶";
                _slider.highValue = Mathf.Max(0.01f, (float)current.Duration);
                if (!_dragging)
                    _slider.SetValueWithoutNotify((float)current.Time);
                _time.text = $"{current.Time:0.00} / {current.Duration:0.00}s";
            }
        }
    }
}
