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

            SchematicGraphEditorWindow.CanvasLoaded += (window, canvas) =>
            {
                canvas.Q("sound-cue-bar")?.RemoveFromHierarchy();
                if (window.SchematicGraph is SoundCueGraph cue)
                    canvas.Add(new PreviewBar(cue));
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
        /// The bar along the top of a Sound Cue's canvas: preview it, and see where it is and what's wrong. The radar pad
        /// shows the listener (in the middle, facing up) and the sound: drag the dot to place it, or pick a motion.
        /// </summary>
        private sealed class PreviewBar : VisualElement
        {
            private readonly SoundCueGraph _cue;
            private readonly Button _play;
            private readonly Label _time, _warnings, _where;
            private readonly RadarPad _radar;

            public PreviewBar(SoundCueGraph cue)
            {
                _cue = cue;
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
                top.Add(new Label("SOUND CUE") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8, color = new Color(0.95f, 0.6f, 0.8f) } });
                _play = new Button(Toggle) { tooltip = "Hear the cue (edits replay it as you make them)." };
                _play.style.minWidth = 70;
                top.Add(_play);
                _time = new Label { style = { marginLeft = 8, minWidth = 90, unityTextAlign = TextAnchor.MiddleLeft, opacity = 0.8f } };
                top.Add(_time);
                Add(top);

                var body = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
                _radar = new RadarPad();
                body.Add(_radar);

                var controls = new VisualElement { style = { marginLeft = 8, width = 230 } };
                var spatial = new Toggle("3D") { value = SoundCuePreview.Spatial, tooltip = "Hear it panned and fading with distance, as in the game. Off: flat and centered." };
                spatial.RegisterValueChangedCallback(evt => SoundCuePreview.Spatial = evt.newValue);
                controls.Add(Narrow(spatial));

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
                body.Add(controls);
                Add(body);

                _warnings = new Label { style = { color = new Color(1f, 0.75f, 0.3f), whiteSpace = WhiteSpace.Normal, maxWidth = 520, display = DisplayStyle.None } };
                Add(_warnings);

                Refresh();
                SoundCuePreview.Changed += Refresh;
                RegisterCallback<DetachFromPanelEvent>(_ => SoundCuePreview.Changed -= Refresh);
                schedule.Execute(Tick).Every(33);
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
                else SoundCuePreview.Play(_cue);
                Refresh();
            }

            private void Refresh()
            {
                _play.text = IsMine ? "■ Stop" : "▶ Preview";
                var warnings = IsMine ? SoundCuePreview.Voice?.Warnings : null;
                bool any = warnings != null && warnings.Count > 0;
                _warnings.text = any ? string.Join("\n", warnings.Distinct().Select(w => "⚠ " + w)) : "";
                _warnings.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            }

            private void Tick()
            {
                var position = SoundCuePreview.CurrentPosition;
                _where.text = $"{position.magnitude:0.0} m away, {Side(position)}";
                _radar.MarkDirtyRepaint();

                var voice = IsMine ? SoundCuePreview.Voice : null;
                if (voice == null)
                {
                    _time.text = "";
                    if (_play.text != "▶ Preview")
                        Refresh();
                    return;
                }
                float length = voice.Context.Duration;
                _time.text = length > 0f ? $"{voice.Time:0.00} / {length:0.00} s" : $"{voice.Time:0.00} s (endless)";
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

            // How many meters the pad's edge is from the listener.
            private static float Range => Mathf.Max(15f, SoundCuePreview.Motion == SoundCuePreview.PreviewMotion.Still
                ? SoundCuePreview.Position.magnitude * 1.25f
                : SoundCuePreview.Radius * (SoundCuePreview.Motion == SoundCuePreview.PreviewMotion.FlyBy ? 2.2f : 1.2f));

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
                painter.fillColor = new Color(0.95f, 0.45f, 0.75f, 1f);
                painter.BeginPath();
                painter.Arc(dot, 5f, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Fill();
            }
        }
    }
}
