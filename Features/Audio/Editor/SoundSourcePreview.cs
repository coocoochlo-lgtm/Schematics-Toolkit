using System;
using System.Collections.Generic;
using BlueGraph;
using Remedy.Schematics;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Remedy.Audio
{
    /// <summary>
    /// Sound Sources in the Scene view: outside Play Mode, while the Scene view's Sound Sources toggle is on, every
    /// active Schematic Object whose graph plays a sound On Create (Play SFX, Play Music, Play Sound, Play Sound Cue:
    /// what dropping audio into the scene makes) loops that sound, heard from where it is around the Scene view's
    /// camera (panned, and falling off with distance as much as the node's Spatial Blend says). Each object can be
    /// left out in its Inspector (Loop In Scene View). It starts as an object is enabled and stops as it's disabled,
    /// and everything stops for Play Mode (where the graph plays it for real).
    /// </summary>
    [InitializeOnLoad]
    public static class SoundSourcePreview
    {
        private const string EnabledKey = "Remedy.Audio.SoundSourcePreview";

        /// <summary>The Scene view's Sound Sources toggle (kept between sessions).</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set
            {
                if (value == Enabled) return;
                EditorPrefs.SetBool(EnabledKey, value);
                _nextScan = 0;
                EnabledChanged?.Invoke();
            }
        }

        /// <summary>The toggle changed (the toolbar button follows it).</summary>
        public static event Action EnabledChanged;

        // One looping sound: its own hidden host (an Audio Source, and a Sound Cue Player for a cue).
        private sealed class Loop
        {
            public Node Node;
            public Object Sound;
            public GameObject Host;
            public AudioSource Source;
            public SoundCuePlayer Player;
            public double PlayedAt;
        }

        private static readonly Dictionary<SchematicObject, Loop> _loops = new();
        private static double _nextScan;

        static SoundSourcePreview()
        {
            EditorApplication.update += Update;
            EditorApplication.hierarchyChanged += () => _nextScan = 0;
            Undo.undoRedoPerformed += () => _nextScan = 0;
            EditorApplication.playModeStateChanged += _ => StopAll();
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
        }

        /// <summary>Whether an object's sound is looping in the Scene view now.</summary>
        public static bool IsLooping(SchematicObject source) => source != null && _loops.ContainsKey(source);

        /// <summary>
        /// The Play node its graph runs On Create (directly, or further along the flow from it) and the sound it plays,
        /// or null: it isn't a sound source.
        /// </summary>
        public static Node SoundOf(SchematicObject source, out Object sound)
        {
            sound = null;
            var graph = source != null ? source.SchematicGraph : null;
            if (graph == null)
                return null;
            var queue = new Queue<Node>();
            var seen = new HashSet<Node>();
            foreach (var node in graph.Nodes)
                if (node is OnCreate)
                    queue.Enqueue(node);
            while (queue.Count > 0 && seen.Count < 64)
            {
                var node = queue.Dequeue();
                if (!seen.Add(node))
                    continue;
                if (node is PlayClipNode clip && clip.Clip != null)
                {
                    sound = clip.Clip;
                    return node;
                }
                if (node is PlaySoundCueNode cue && cue.Cue != null)
                {
                    sound = cue.Cue;
                    return node;
                }
                var next = node.GetPort("▶");
                if (next == null)
                    continue;
                foreach (var port in next.ConnectedPorts)
                    if (port?.Node != null)
                        queue.Enqueue(port.Node);
            }
            return null;
        }

        private static void Update()
        {
            // (isPlaying too: leaving Play Mode, it's still true for a moment after "will change" has cleared.)
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (_loops.Count > 0) StopAll();
                return;
            }
            if (EditorApplication.timeSinceStartup >= _nextScan)
            {
                _nextScan = EditorApplication.timeSinceStartup + 0.5;
                Scan();
            }
            if (_loops.Count == 0)
            {
                SoundSpaces.EndEditorPreview();
                return;
            }
            var camera = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            // The scene's rooms, echo walls and spots, heard from the Scene view's camera (as in the game, from its listener).
            var spaces = SoundSpaces.BeginEditorPreview();
            if (camera != null)
            {
                SoundSpaces.ListenerOverride = camera.transform.position;
                SoundSpaces.ListenerRotation = camera.transform.rotation;
            }
            // Heard only while a Scene view is the tab showing in its window (another tab in front, the Graph or the
            // Game view: silent, carrying on so it picks up where it is).
            bool heard = SceneViewShowing();
            foreach (var pair in _loops)
            {
                Hear(pair.Key, pair.Value, camera);
                if (pair.Value.Source != null)
                    pair.Value.Source.mute = !heard;
            }
            SoundSpaces.EditorMuted = !heard;
            if (spaces != null)
                spaces.EditorTick();
            // The echo walls' glow follows what's heard.
            if (SoundField.ShowEchoWalls && EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                _nextRepaint = EditorApplication.timeSinceStartup + 0.1;
                SceneView.RepaintAll();
            }
        }

        private static double _nextRepaint;

        // Whether a Scene view is the tab showing in its window (Unity's hasFocus: its dock area's current tab).
        private static bool SceneViewShowing()
        {
            foreach (var view in SceneView.sceneViews)
                if (view is SceneView sceneView && sceneView != null && sceneView.hasFocus)
                    return true;
            return false;
        }

        // Which objects should be looping, and start or stop them to match.
        private static void Scan()
        {
            var wanted = new Dictionary<SchematicObject, (Node node, Object sound)>();
            if (Enabled)
                foreach (var source in Object.FindObjectsByType<SchematicObject>(FindObjectsSortMode.None))
                {
                    if (source == null || !source.isActiveAndEnabled || !source.PreviewSound)
                        continue;
                    var node = SoundOf(source, out var sound);
                    if (node != null)
                        wanted[source] = (node, sound);
                }

            var stale = new List<SchematicObject>();
            foreach (var pair in _loops)
                if (pair.Key == null || !wanted.TryGetValue(pair.Key, out var want) || want.node != pair.Value.Node || want.sound != pair.Value.Sound)
                    stale.Add(pair.Key);
            foreach (var source in stale)
            {
                End(_loops[source]);
                _loops.Remove(source);
            }
            foreach (var pair in wanted)
                if (!_loops.ContainsKey(pair.Key))
                {
                    var loop = Begin(pair.Key, pair.Value.node, pair.Value.sound);
                    if (loop != null)
                        _loops[pair.Key] = loop;
                }
        }

        private static Loop Begin(SchematicObject owner, Node node, Object sound)
        {
            // (Where the sound is: its Spatial effects and Around Corners ask the scene's Sound Fields from here.)
            var host = new GameObject("Sound Source Preview") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.position = owner.transform.position;
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            // Flat as far as Unity's concerned: panned and faded here, from where it is around the Scene view's camera.
            source.spatialBlend = 0f;
            var loop = new Loop { Node = node, Sound = sound, Host = host, Source = source };
            if (sound is AudioClip clip)
            {
                source.clip = clip;
                source.loop = true;
                source.pitch = node is PlayClipNode play ? Mathf.Clamp(play.Pitch, 0.1f, 3f) : 1f;
                source.Play();
            }
            else if (sound is SoundCueGraph cue)
            {
                var player = host.AddComponent<SoundCuePlayer>();
                player.hideFlags = HideFlags.HideAndDontSave;
                player.PreloadVoices = 0;
                player.Cue = cue;
                // In the scene: its sends to rooms and echo walls are timed by the audio clock, as in the game.
                player.WorldPreview = true;
                player.ScheduleInEditor = true;
                if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
                    player.WorldListener = SceneView.lastActiveSceneView.camera.transform.position;
                SoundSpaces.BeginEditorPreview();
                loop.Player = player;
                Restart(loop);
            }
            else
            {
                Object.DestroyImmediate(host);
                return null;
            }
            return loop;
        }

        private static void Restart(Loop loop)
        {
            loop.Player.Play(0f);
            loop.PlayedAt = EditorApplication.timeSinceStartup;
        }

        private static void End(Loop loop)
        {
            if (loop.Player != null)
                loop.Player.Stop();
            if (loop.Source != null)
                loop.Source.Stop();
            if (loop.Host != null)
                Object.DestroyImmediate(loop.Host);
        }

        private static void StopAll()
        {
            foreach (var loop in _loops.Values)
                End(loop);
            _loops.Clear();
            SoundSpaces.EndEditorPreview();
        }

        // Where it is around the Scene view's camera (x right, y ahead, its whole distance), and a cue played again
        // once it's over.
        private static void Hear(SchematicObject owner, Loop loop, Camera camera)
        {
            if (owner == null || loop.Source == null)
                return;
            var local = camera != null ? camera.transform.InverseTransformPoint(owner.transform.position) : Vector3.forward;
            var flat = new Vector2(local.x, local.z);
            float distance = local.magnitude;
            var around = flat.sqrMagnitude > 1e-6f ? flat.normalized * distance : new Vector2(0f, distance);

            if (loop.Player == null)
            {
                var play = loop.Node as PlayClipNode;
                float blend = play != null ? Mathf.Clamp01(play.SpatialBlend) : 1f;
                float volume = play != null ? Mathf.Clamp01(play.Volume) : 1f;
                loop.Source.volume = volume * Mathf.Lerp(1f, 1f / Mathf.Max(1f, distance), blend);
                loop.Source.panStereo = distance > 0.001f ? Mathf.Clamp(around.x / distance, -1f, 1f) * blend : 0f;
                return;
            }

            var player = loop.Player;
            // Where it is in the scene and where it's heard from (for its Spatial effects and Around Corners).
            loop.Host.transform.position = owner.transform.position;
            if (camera != null)
                player.WorldListener = camera.transform.position;
            var cueNode = loop.Node as PlaySoundCueNode;
            float cueVolume = cueNode != null ? Mathf.Max(0f, cueNode.Volume) : 1f;
            if (player.Cue != null && player.Cue.IsSpatial)
                player.SetPreviewPosition(around);
            else
            {
                player.SetPreviewPosition(Vector2.zero, falloff: false);
                player.DistanceOverride = distance;
                if (player.Voice != null)
                    player.Voice.Context.Distance = distance;
            }
            loop.Source.volume *= cueVolume;

            // Over: again from the start (a cue's streamed clip can keep "playing" silence past its end, so by the clock too).
            var voice = player.Voice;
            float duration = voice != null ? voice.Context.Duration : 0f;
            bool over = voice == null
                || (voice.Finished && (!player.Source.isPlaying || player.IsLive))
                || (duration > 0f && voice.Context.AfterTrigger <= 0f && EditorApplication.timeSinceStartup - loop.PlayedAt >= duration);
            if (over)
                Restart(loop);
        }

        // The Inspector: a row on a sound source's own object to leave it out of (or put it back in) the Scene view.
        [InitializeOnLoadMethod]
        private static void RegisterInspector() => Editor.finishedDefaultHeaderGUI += OnHeaderGUI;

        private static void OnHeaderGUI(Editor editor)
        {
            if (Application.isPlaying || editor.targets.Length != 1 || !(editor.target is GameObject gameObject)
                || !gameObject.TryGetComponent(out SchematicObject source) || PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return;
            var node = SoundOf(source, out var sound);
            if (node == null)
                return;
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Sound Source", $"Its graph plays {sound.name} On Create."), EditorStyles.boldLabel, GUILayout.Width(100));
                bool on = EditorGUILayout.ToggleLeft(new GUIContent("Loop In Scene View",
                    "Loop its sound outside Play Mode, heard from where it is around the Scene view's camera, while the Scene view's Sound Sources toggle is on."),
                    source.PreviewSound);
                if (on != source.PreviewSound)
                {
                    Undo.RecordObject(source, on ? "Loop Sound In Scene View" : "Stop Sound In Scene View");
                    source.PreviewSound = on;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                    _nextScan = 0;
                }
            }
        }
    }

    /// <summary>The Sound Sources toggle in the Scene view's toolbar (see <see cref="SoundSourcePreview"/>).</summary>
    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class SoundSourcesToggle : EditorToolbarToggle
    {
        public const string Id = "Remedy/Sound Sources";

        public SoundSourcesToggle()
        {
            icon = EditorGUIUtility.IconContent("AudioSource Icon").image as Texture2D;
            tooltip = "Sound Sources: loop the sounds objects play On Create (dropped audio, for one) in the Scene view, "
                + "heard from where they are around its camera. Each object can be left out in its Inspector.";
            SetValueWithoutNotify(SoundSourcePreview.Enabled);
            this.RegisterValueChangedCallback(change => SoundSourcePreview.Enabled = change.newValue);
            RegisterCallback<AttachToPanelEvent>(_ => SoundSourcePreview.EnabledChanged += Sync);
            RegisterCallback<DetachFromPanelEvent>(_ => SoundSourcePreview.EnabledChanged -= Sync);
        }

        private void Sync() => SetValueWithoutNotify(SoundSourcePreview.Enabled);
    }

    /// <summary>Holds the Sound Sources toggle, docked in the Scene view's top toolbar.</summary>
    [Overlay(typeof(SceneView), Id, "Sound Sources", true,
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top)]
    internal sealed class SoundSourcesOverlay : ToolbarOverlay
    {
        public const string Id = "remedy-sound-sources";

        public SoundSourcesOverlay() : base(SoundSourcesToggle.Id) { }
    }
}
