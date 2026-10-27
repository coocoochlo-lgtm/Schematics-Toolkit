using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Audio
{
    /// <summary>
    /// Sound Cues in the editor: making them (with an Output and a Sound Wave wired up), and the bar along the top of a
    /// Sound Cue's graph window to preview it.
    /// </summary>
    [InitializeOnLoad]
    internal static class SoundCueEditor
    {
        static SoundCueEditor()
        {
            // Sound Cue nodes only show in Sound Cues (not in every other graph's node search).
            DefaultSearchProvider.ExclusiveTags.Add(SoundNode.Tag);
            DefaultSearchProvider.ExclusiveTags.Add(SoundCueGraph.OutputOnlyTag);

            SchematicGraphEditorWindow.CanvasLoaded += (window, canvas) =>
            {
                canvas.Q("sound-cue-bar")?.RemoveFromHierarchy();
                if (window.SchematicGraph is SoundCueGraph cue)
                    canvas.Add(new PreviewBar(cue, window));
            };
        }

        #region Making

        [MenuItem("Assets/Create/Schematics/Sound Cue", priority = 82)]
        private static void Create()
        {
            string folder = "Assets";
            var selected = Selection.activeObject != null ? AssetDatabase.GetAssetPath(Selection.activeObject) : null;
            if (!string.IsNullOrEmpty(selected))
                folder = AssetDatabase.IsValidFolder(selected) ? selected : Path.GetDirectoryName(selected).Replace('\\', '/');
            var cue = CreateIn(folder);
            Selection.activeObject = cue;
            EditorGUIUtility.PingObject(cue);
        }

        /// <summary>
        /// A new Sound Cue in a folder, with a Sound Wave wired into its Output, ready for an audio file. How the
        /// Project Manager (Schematics and Audio pages) makes them too.
        /// </summary>
        [SchematicTypeCreator(typeof(SoundCueGraph))]
        public static SoundCueGraph CreateIn(string folder)
        {
            EnsureFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/New Sound Cue.asset");
            var cue = ScriptableObject.CreateInstance<SoundCueGraph>();
            AssetDatabase.CreateAsset(cue, path);

            var output = NewNode<SoundCueOutput>("Output", new Vector2(420, 0));
            var wave = NewNode<SoundWaveNode>("Sound Wave", new Vector2(80, 0));
            if (output != null && wave != null)
            {
                cue.AddNode(wave);
                cue.AddNode(output);
                var from = wave.GetPort("Audio");
                var to = output.GetPort(SoundCueOutput.AudioPort);
                if (from != null && to != null)
                    cue.AddEdge(from, to);
            }
            EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();
            return cue;
        }

        private static T NewNode<T>(string name, Vector2 position) where T : Node
        {
            var node = NodeReflection.GetNodeType(typeof(T))?.CreateInstance() as T;
            if (node == null)
                return null;
            node.Name = name;
            node.Position = position;
            return node;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        #endregion

        /// <summary>
        /// The bar along the top of a Sound Cue's canvas: whether the cue is Mono or Stereo (by its title), preview it
        /// (once or looping), scrub through it on the timeline, and see where it is and what's wrong. For Stereo cues
        /// the radar pad shows the listener (in the middle, facing up) and the sound: drag the dot to place it, or pick a
        /// motion.
        /// </summary>
        private sealed class PreviewBar : VisualElement
        {
            private static readonly Color Pink = new(0.95f, 0.6f, 0.8f);

            private readonly SoundCueGraph _cue;
            // The window showing the cue: what the bar's previews belong to.
            private readonly SchematicGraphEditorWindow _window;
            private readonly Button _play, _loop, _channels;
            private readonly Label _time, _warnings, _where;
            private readonly Slider _timeline;
            private readonly RadarPad _radar;
            private readonly VisualElement _spatial;

            private float _length = -1f;      // seconds; 0: endless; -1: not known yet
            private int _lengthDirty = -1;    // the cue's dirty count _length was found at
            private float? _pendingSeek;
            private bool _scrubbing;      // a scrub waiting for the next tick (so dragging doesn't restart it every event)

            public PreviewBar(SoundCueGraph cue, SchematicGraphEditorWindow window)
            {
                _cue = cue;
                _window = window;
                name = "sound-cue-bar";
                style.position = Position.Absolute;
                style.left = 8;
                style.top = 8;
                style.flexDirection = FlexDirection.Column;
                style.paddingLeft = style.paddingRight = 8;
                style.paddingTop = style.paddingBottom = 4;
                style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.85f);
                style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 6;

                var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                top.Add(new Label("SOUND CUE") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 4, color = Pink } });
                _channels = new Button(ToggleChannels) { tooltip = "Stereo: placed in the world, panned and fading with distance (the radar pad). Mono: flat and centered wherever it plays. Click to switch." };
                Pill(_channels);
                top.Add(_channels);
                _play = new Button(Toggle) { tooltip = "Hear the cue (edits replay it as you make them)." };
                _play.style.minWidth = 70;
                _play.style.marginLeft = 8;
                top.Add(_play);
                _loop = new Button(() => { SoundCuePreview.Loop = !SoundCuePreview.Loop; Refresh(); }) { text = "⟲ Loop", tooltip = "Play the cue again from the start when it ends." };
                top.Add(_loop);
                _time = new Label { style = { marginLeft = 8, minWidth = 110, unityTextAlign = TextAnchor.MiddleLeft, opacity = 0.8f } };
                top.Add(_time);
                var space = new EnumField(SoundCuePreview.Space)
                {
                    tooltip = "Surroundings to hear the cue in: Spatial Reverb and Delay ring and echo like that place (rooms ring from where they are, echoes come off the walls and stay there as the sound moves). None: they use their own settings.",
                };
                space.style.width = 110;
                space.style.marginLeft = 6;
                space.RegisterValueChangedCallback(evt => SoundCuePreview.Space = (PreviewSpace)evt.newValue);
                top.Add(new Label("Space") { style = { marginLeft = 6, opacity = 0.8f, unityTextAlign = TextAnchor.MiddleLeft } });
                top.Add(space);
                Add(top);

                _timeline = new Slider(0f, 1f) { tooltip = "Where the cue is. Drag to hear it from there." };
                _timeline.style.marginTop = 2;
                _timeline.RegisterValueChangedCallback(evt => _pendingSeek = evt.newValue);
                // While it's held, the playhead doesn't pull the handle away from the mouse.
                _timeline.RegisterCallback<PointerDownEvent>(_ => _scrubbing = true, TrickleDown.TrickleDown);
                _timeline.RegisterCallback<PointerUpEvent>(_ => _scrubbing = false, TrickleDown.TrickleDown);
                _timeline.RegisterCallback<PointerCaptureOutEvent>(_ => _scrubbing = false, TrickleDown.TrickleDown);
                Add(_timeline);

                _spatial = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
                _radar = new RadarPad();
                _spatial.Add(_radar);

                var controls = new VisualElement { style = { marginLeft = 8, width = 230 } };
                var motion = new EnumField("Motion", SoundCuePreview.Motion) { tooltip = "How the sound moves around the listener (Still: where you put it on the pad)." };
                motion.RegisterValueChangedCallback(evt =>
                {
                    SoundCuePreview.Motion = (SoundCuePreview.PreviewMotion)evt.newValue;
                    SoundCuePreview.RestartMotion();
                });
                controls.Add(Narrow(motion));
                _radar.Dragged += () => motion.SetValueWithoutNotify(SoundCuePreview.PreviewMotion.Still);

                var radius = new Slider("Radius", 1f, 100f) { value = SoundCuePreview.Radius, showInputField = true, tooltip = "How far the motion reaches (meters)." };
                radius.RegisterValueChangedCallback(evt => SoundCuePreview.Radius = evt.newValue);
                controls.Add(Narrow(radius));

                var speed = new Slider("Speed", 0.2f, 60f) { value = SoundCuePreview.Speed, showInputField = true, tooltip = "How fast the motion moves (meters a second)." };
                speed.RegisterValueChangedCallback(evt => SoundCuePreview.Speed = evt.newValue);
                controls.Add(Narrow(speed));

                _where = new Label { style = { opacity = 0.8f, marginTop = 2 } };
                controls.Add(_where);
                _spatial.Add(controls);
                Add(_spatial);

                _warnings = new Label { style = { color = new Color(1f, 0.75f, 0.3f), whiteSpace = WhiteSpace.Normal, maxWidth = 520, display = DisplayStyle.None } };
                Add(_warnings);

                Refresh();
                SoundCuePreview.Changed += Refresh;
                Undo.undoRedoPerformed += Refresh;
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    SoundCuePreview.Changed -= Refresh;
                    Undo.undoRedoPerformed -= Refresh;
                    // Leaving the graph (another one opened in the window, or the window closed) stops what it played.
                    // Checked a moment later: the canvas is also rebuilt while staying on the same graph.
                    var window = _window;
                    var cue = _cue;
                    EditorApplication.delayCall += () =>
                    {
                        if (SoundCuePreview.Owner is SchematicGraphEditorWindow owner && owner == window
                            && (window == null || window.SchematicGraph != cue))
                            SoundCuePreview.Stop();
                    };
                });
                schedule.Execute(Tick).Every(33);
            }

            private static void Pill(Button button)
            {
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.fontSize = 10;
                button.style.minWidth = 54;
                button.style.borderTopLeftRadius = button.style.borderTopRightRadius = button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 8;
            }

            private static T Narrow<T>(T field) where T : VisualElement
            {
                var label = field.Q<Label>(className: "unity-base-field__label");
                if (label != null)
                {
                    label.style.minWidth = 52;
                    label.style.width = 52;
                }
                return field;
            }

            private bool IsMine => SoundCuePreview.Playing == _cue;

            private void Toggle()
            {
                if (IsMine) SoundCuePreview.Stop();
                else SoundCuePreview.Play(_cue, owner: _window);
                Refresh();
            }

            private void ToggleChannels()
            {
                Undo.RecordObject(_cue, "Sound Cue Mono / Stereo");
                _cue.Channels = _cue.IsSpatial ? SoundCueGraph.SoundCueChannels.Mono : SoundCueGraph.SoundCueChannels.Stereo;
                EditorUtility.SetDirty(_cue);
                // Heard differently straight away, without starting over.
                SoundCuePreview.AcceptEdits(_cue);
                Refresh();
            }

            private void Refresh()
            {
                _play.text = IsMine ? "■ Stop" : "▶ Preview";
                bool spatial = _cue.IsSpatial;
                _channels.text = spatial ? "STEREO" : "MONO";
                _channels.style.color = spatial ? Pink : new Color(0.85f, 0.85f, 0.85f);
                _spatial.style.display = spatial ? DisplayStyle.Flex : DisplayStyle.None;
                bool loop = SoundCuePreview.Loop;
                _loop.style.color = loop ? Pink : new Color(0.85f, 0.85f, 0.85f, 0.6f);
                _loop.style.unityFontStyleAndWeight = loop ? FontStyle.Bold : FontStyle.Normal;
                var warnings = IsMine ? SoundCuePreview.Voice?.Warnings : null;
                bool any = warnings != null && warnings.Count > 0;
                _warnings.text = any ? string.Join("\n", warnings.Distinct().Select(w => "⚠ " + w)) : "";
                _warnings.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // The cue's length (from a voice, built once per edit), for the timeline.
            private void UpdateLength(SoundCueVoice playing)
            {
                if (playing != null)
                {
                    _length = playing.Context.Duration;
                    _lengthDirty = EditorUtility.GetDirtyCount(_cue);
                    return;
                }
                int dirty = EditorUtility.GetDirtyCount(_cue);
                if (_length >= 0f && dirty == _lengthDirty)
                    return;
                _lengthDirty = dirty;
                int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
                var voice = SoundCueVoicePool.Rent(_cue, rate);
                _length = voice != null ? voice.Context.Duration : 0f;
                SoundCueVoicePool.Return(voice);
            }

            private void Tick()
            {
                var position = SoundCuePreview.CurrentPosition;
                _where.text = $"{position.magnitude:0.0} m away, {Side(position)}";
                _radar.MarkDirtyRepaint();

                if (_pendingSeek.HasValue)
                {
                    float seek = _pendingSeek.Value;
                    _pendingSeek = null;
                    if (_length > 0f)
                    {
                        SoundCuePreview.Seek(_cue, Mathf.Min(seek, _length - 0.01f), owner: _window);
                        Refresh();
                    }
                }

                var voice = IsMine ? SoundCuePreview.Voice : null;
                UpdateLength(voice);
                bool endless = _length <= 0f;
                _timeline.SetEnabled(!endless);
                if (!endless && !Mathf.Approximately(_timeline.highValue, _length))
                    _timeline.highValue = _length;

                if (voice == null)
                {
                    _time.text = endless ? "endless" : $"{_length:0.00} s";
                    if (_play.text != "▶ Preview")
                        Refresh();
                    return;
                }
                float heard = SoundCuePreview.HeardTime;
                if (!endless && !_scrubbing)
                    _timeline.SetValueWithoutNotify(Mathf.Min(heard, _length));
                _time.text = !endless ? $"{heard:0.00} / {_length:0.00} s" : $"{heard:0.00} s (endless)";
            }

            private static string Side(Vector2 p)
            {
                if (p.magnitude < 0.25f) return "on the listener";
                float angle = Mathf.Atan2(p.x, p.y) * Mathf.Rad2Deg;
                if (Mathf.Abs(angle) < 22.5f) return "ahead";
                if (Mathf.Abs(angle) > 157.5f) return "behind";
                string side = angle > 0 ? "right" : "left";
                return Mathf.Abs(angle) < 67.5f ? $"ahead {side}" : Mathf.Abs(angle) > 112.5f ? $"behind {side}" : side;
            }
        }

        /// <summary>
        /// A top-down view around the listener: rings every 10 m, the listener in the middle facing up, and the sound
        /// as a dot. Drag it to place the sound (which stops any motion).
        /// </summary>
        private sealed class RadarPad : VisualElement
        {
            private const float Size = 120f;
            public event System.Action Dragged;
            private bool _dragging;

            public RadarPad()
            {
                style.width = style.height = Size;
                style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
                style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = Size / 2f;
                tooltip = "Drag the dot to place the sound around the listener (the listener faces up).";
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    _dragging = true;
                    this.CapturePointer(evt.pointerId);
                    Place(evt.localPosition);
                    evt.StopPropagation();
                });
                RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (_dragging)
                        Place(evt.localPosition);
                });
                RegisterCallback<PointerUpEvent>(evt =>
                {
                    _dragging = false;
                    this.ReleasePointer(evt.pointerId);
                });
            }

            // How many meters the pad's edge is from the listener (far enough for the sound, and the surroundings' walls).
            private static float Range
            {
                get
                {
                    float range = Mathf.Max(15f, SoundCuePreview.Motion == SoundCuePreview.PreviewMotion.Still
                        ? SoundCuePreview.Position.magnitude * 1.25f
                        : SoundCuePreview.Radius * (SoundCuePreview.Motion == SoundCuePreview.PreviewMotion.FlyBy ? 2.2f : 1.2f));
                    var simulation = SoundCuePreview.Simulation;
                    if (simulation != null)
                        foreach (var wall in simulation.Walls)
                            range = Mathf.Max(range, Mathf.Min(wall.Foot.magnitude * 1.15f, 90f));
                    return range;
                }
            }

            private void Place(Vector2 local)
            {
                float half = Size / 2f;
                var offset = new Vector2(local.x - half, half - local.y) / half; // up is ahead
                if (offset.magnitude > 1f)
                    offset.Normalize();
                SoundCuePreview.Motion = SoundCuePreview.PreviewMotion.Still;
                SoundCuePreview.Position = offset * Range;
                Dragged?.Invoke();
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                float half = Size / 2f;
                var center = new Vector2(half, half);
                float range = Range, scale = (half - 4f) / range;

                painter.lineWidth = 1f;
                painter.strokeColor = new Color(1f, 1f, 1f, 0.15f);
                for (float ring = 10f; ring < range; ring += 10f)
                {
                    painter.BeginPath();
                    painter.Arc(center, ring * scale, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Stroke();
                }

                // The surroundings: the room's floor, and the walls (glowing as they echo).
                var simulation = SoundCuePreview.Simulation;
                if (simulation != null)
                {
                    Vector2 ToPad(Vector2 p) => center + new Vector2(p.x, -p.y) * scale;
                    if (simulation.Room is Rect room && simulation.RoomBus != null)
                    {
                        float glow = Mathf.Clamp01(simulation.RoomBus.Peak * 4f);
                        painter.fillColor = new Color(0.55f, 0.6f, 1f, 0.08f + 0.25f * glow);
                        painter.BeginPath();
                        painter.MoveTo(ToPad(new Vector2(room.xMin, room.yMin)));
                        painter.LineTo(ToPad(new Vector2(room.xMax, room.yMin)));
                        painter.LineTo(ToPad(new Vector2(room.xMax, room.yMax)));
                        painter.LineTo(ToPad(new Vector2(room.xMin, room.yMax)));
                        painter.ClosePath();
                        painter.Fill();
                    }
                    foreach (var wall in simulation.Walls)
                    {
                        float glow = wall.Bus != null ? Mathf.Clamp01(wall.Bus.Peak * 4f) : 0f;
                        painter.strokeColor = Color.Lerp(new Color(0.85f, 0.8f, 0.7f, 0.7f), new Color(1f, 0.55f, 0.25f, 1f), glow);
                        painter.lineWidth = 2f + glow * 3f;
                        painter.BeginPath();
                        painter.MoveTo(ToPad(wall.A));
                        painter.LineTo(ToPad(wall.B));
                        painter.Stroke();
                    }
                    painter.lineWidth = 1f;
                    // Tails and trails: the spots left ringing where the sound has been (brighter the louder).
                    foreach (var bus in simulation.Tails.Buses)
                    {
                        if (bus == null || !(bus.Owner is TailPool.Spot spot)) continue;
                        float glow = Mathf.Clamp01(bus.Peak * 4f);
                        painter.fillColor = new Color(0.95f, 0.45f, 0.75f, 0.12f + 0.6f * glow);
                        painter.BeginPath();
                        painter.Arc(ToPad(new Vector2(spot.Position.x, spot.Position.z)), 2.5f + 3f * glow, Angle.Degrees(0f), Angle.Degrees(360f));
                        painter.Fill();
                    }
                }

                // The listener: a little arrow pointing ahead.
                painter.fillColor = new Color(1f, 1f, 1f, 0.8f);
                painter.BeginPath();
                painter.MoveTo(center + new Vector2(0f, -7f));
                painter.LineTo(center + new Vector2(5f, 5f));
                painter.LineTo(center + new Vector2(-5f, 5f));
                painter.ClosePath();
                painter.Fill();

                // The sound.
                var p = SoundCuePreview.CurrentPosition * scale;
                var dot = center + new Vector2(p.x, -p.y);

                // Round a corner: the way it comes, through the corner it's heard from.
                if (simulation != null && Time.realtimeSinceStartupAsDouble - simulation.LastCornerAt < 0.3)
                {
                    var bendAt = center + new Vector2(simulation.LastCorner.x, -simulation.LastCorner.z) * scale;
                    painter.strokeColor = new Color(0.95f, 0.45f, 0.75f, 0.55f);
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.MoveTo(dot);
                    painter.LineTo(bendAt);
                    painter.LineTo(center);
                    painter.Stroke();
                    painter.fillColor = new Color(1f, 0.8f, 0.9f, 0.9f);
                    painter.BeginPath();
                    painter.Arc(bendAt, 3.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                    painter.Fill();
                    painter.lineWidth = 1f;
                }
                painter.fillColor = new Color(0.95f, 0.45f, 0.75f, 1f);
                painter.BeginPath();
                painter.Arc(dot, 5f, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Fill();
            }
        }
    }
}
