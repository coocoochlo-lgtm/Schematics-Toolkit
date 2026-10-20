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

        /// <summary>The bar along the top of a Sound Cue's canvas: preview it, and see where it is and what's wrong.</summary>
        private sealed class PreviewBar : VisualElement
        {
            private readonly SoundCueGraph _cue;
            private readonly Button _play;
            private readonly Label _time, _warnings;

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

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                row.Add(new Label("SOUND CUE") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8, color = new Color(0.95f, 0.6f, 0.8f) } });

                _play = new Button(Toggle) { tooltip = "Hear the cue (edits replay it as you make them)." };
                _play.style.minWidth = 70;
                row.Add(_play);

                var distance = new Slider("Distance", 0f, 100f) { value = SoundCuePreview.Distance, tooltip = "What Distance To Listener nodes read while previewing (meters)." };
                distance.style.width = 220;
                distance.style.marginLeft = 8;
                distance.labelElement.style.minWidth = 54;
                var distanceValue = new Label($"{SoundCuePreview.Distance:0} m") { style = { minWidth = 40, unityTextAlign = TextAnchor.MiddleLeft } };
                distance.RegisterValueChangedCallback(evt =>
                {
                    SoundCuePreview.Distance = evt.newValue;
                    distanceValue.text = $"{evt.newValue:0} m";
                });
                row.Add(distance);
                row.Add(distanceValue);

                _time = new Label { style = { marginLeft = 8, minWidth = 90, unityTextAlign = TextAnchor.MiddleLeft, opacity = 0.8f } };
                row.Add(_time);
                Add(row);

                _warnings = new Label { style = { color = new Color(1f, 0.75f, 0.3f), whiteSpace = WhiteSpace.Normal, maxWidth = 520, display = DisplayStyle.None } };
                Add(_warnings);

                Refresh();
                SoundCuePreview.Changed += Refresh;
                RegisterCallback<DetachFromPanelEvent>(_ => SoundCuePreview.Changed -= Refresh);
                schedule.Execute(Tick).Every(100);
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
        }
    }
}
