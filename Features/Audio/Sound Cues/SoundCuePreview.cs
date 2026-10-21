#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Editor only: plays a Sound Cue without entering Play Mode (the graph window's Preview and the Audio page's
    /// cards). A Stereo cue is heard in 3D around an imaginary listener: placed by hand on the preview's radar pad, or
    /// moved by a motion (orbiting, flying by, coming closer...), panned and fading with distance as it goes; a Mono
    /// cue is heard flat and centered. Distance To Listener nodes follow the sound either way. It's played again
    /// whenever the cue is edited while previewing, so changes are heard as they're made, and stops at the end of the
    /// cue (or starts over, with Loop on). Seek plays it from part way through.
    /// </summary>
    public static class SoundCuePreview
    {
        /// <summary>How the previewed sound moves around the listener.</summary>
        public enum PreviewMotion
        {
            /// <summary>Stays where it's put on the radar pad.</summary>
            Still,
            /// <summary>Circles the listener at Radius.</summary>
            Orbit,
            /// <summary>Passes from left to right in front, then again.</summary>
            FlyBy,
            /// <summary>Comes from Radius ahead to the listener, then again.</summary>
            Approach,
            /// <summary>Goes from the listener to Radius ahead, then again.</summary>
            Recede,
        }

        private static GameObject _host;
        private static SoundCuePlayer _player;
        private static int _dirtyCount;
        private static double _startedAt;
        // When the current play started (editor clock) and from how far into the cue, for when it reaches the end.
        private static double _playedAt;
        private static float _playedFrom;
        private static Vector2 _position = new(0f, 1f);

        /// <summary>The cue being previewed (null when nothing is).</summary>
        public static SoundCueGraph Playing => _player != null && _player.Voice != null ? _player.Cue : null;

        /// <summary>
        /// How far into the cue the preview is, as heard, in seconds (0 when nothing's playing). The voice renders ahead
        /// of what's heard (a whole short cue at once), so its own time runs early: show this one.
        /// </summary>
        public static float HeardTime
        {
            get
            {
                var voice = Voice;
                if (voice == null || Playing == null)
                    return 0f;
                float time = _playedFrom + (float)(EditorApplication.timeSinceStartup - _playedAt);
                float duration = voice.Context.Duration;
                return duration > 0f ? Mathf.Clamp(time, 0f, duration) : Mathf.Max(0f, time);
            }
        }

        /// <summary>The previewing voice (for its warnings), or null.</summary>
        public static SoundCueVoice Voice => _player != null ? _player.Voice : null;

        /// <summary>Raised when a preview starts or stops.</summary>
        public static event System.Action Changed;

        /// <summary>Plays the cue again from the start when it ends.</summary>
        public static bool Loop
        {
            get => EditorPrefs.GetBool("Remedy.SoundCuePreview.Loop", false);
            set => EditorPrefs.SetBool("Remedy.SoundCuePreview.Loop", value);
        }
        public static PreviewMotion Motion = PreviewMotion.Still;
        /// <summary>How far the motions reach, in meters.</summary>
        public static float Radius = 10f;
        /// <summary>How fast the motions move, in meters per second.</summary>
        public static float Speed = 5f;

        /// <summary>Where the sound is placed by hand (Still), in meters: x right of the listener, y ahead of it.</summary>
        public static Vector2 Position
        {
            get => _position;
            set
            {
                _position = value;
                Apply();
            }
        }

        /// <summary>Where the sound is right now (moving with the motion), in meters around the listener.</summary>
        public static Vector2 CurrentPosition
        {
            get
            {
                float t = (float)(EditorApplication.timeSinceStartup - _startedAt);
                if (_orbit is { } orbit)
                {
                    float turn = orbit.turnsPerSecond * t * 2f * Mathf.PI;
                    return new Vector2(Mathf.Sin(turn), Mathf.Cos(turn)) * orbit.radius;
                }
                if (Motion == PreviewMotion.Still)
                    return _position;
                float radius = Mathf.Max(0.5f, Radius), speed = Mathf.Max(0.01f, Speed);
                switch (Motion)
                {
                    case PreviewMotion.Orbit:
                        float angle = speed / radius * t;
                        return new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                    case PreviewMotion.FlyBy:
                        float span = radius * 2f;
                        return new Vector2(Mathf.Repeat(speed * t, span * 2f) - span, radius * 0.25f);
                    case PreviewMotion.Approach:
                        return new Vector2(0f, radius - Mathf.Repeat(speed * t, radius));
                    default:
                        return new Vector2(0f, Mathf.Repeat(speed * t, radius));
                }
            }
        }

        /// <summary>The distance Distance To Listener nodes read (setting it places the sound that far ahead).</summary>
        public static float Distance
        {
            get => CurrentPosition.magnitude;
            set
            {
                Motion = PreviewMotion.Still;
                Position = new Vector2(0f, Mathf.Max(0f, value));
            }
        }

        /// <summary>
        /// Who started the playing preview (a graph's preview bar, the Audio browser...), so each can stop only what it
        /// started. Null when nothing's playing.
        /// </summary>
        public static object Owner { get; private set; }

        /// <summary>Previews the cue, with the preview's own motion (the graph's radar pad).</summary>
        public static void Play(SoundCueGraph cue, object owner = null)
        {
            _orbit = null;
            Start(cue, 0f, owner);
        }

        /// <summary>
        /// Previews the cue circling the listener at <paramref name="radius"/> meters, <paramref name="turnsPerSecond"/>
        /// times a second (whatever the radar pad's motion is): how a Stereo cue sounds moving around you. Lasts until
        /// it stops, or another Play.
        /// </summary>
        public static void PlayOrbiting(SoundCueGraph cue, float radius, float turnsPerSecond, object owner = null)
        {
            _orbit = (Mathf.Max(0.5f, radius), turnsPerSecond);
            Start(cue, 0f, owner);
        }

        /// <summary>Previews the cue from <paramref name="time"/> seconds in (starting it if it isn't playing).</summary>
        public static void Seek(SoundCueGraph cue, float time, object owner = null)
        {
            if (Playing != cue)
                _orbit = null;
            Start(cue, time, owner ?? (Playing == cue ? Owner : null));
        }

        // A browser's orbit for this play, in place of the preview's own motion.
        private static (float radius, float turnsPerSecond)? _orbit;

        /// <summary>
        /// Takes the cue's current edits as already heard, so a change that doesn't affect the sound (Mono / Stereo)
        /// doesn't play it again from the start.
        /// </summary>
        public static void AcceptEdits(SoundCueGraph cue)
        {
            if (_player != null && _player.Cue == cue)
                _dirtyCount = EditorUtility.GetDirtyCount(cue);
        }

        private static void Start(SoundCueGraph cue, float time, object owner)
        {
            if (cue == null)
                return;
            Owner = owner;
            bool already = Playing == cue;
            if (_host == null)
            {
                _host = new GameObject("Sound Cue Preview") { hideFlags = HideFlags.HideAndDontSave };
                var source = _host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                // Flat as far as Unity's concerned: the preview pans and fades it (Stereo Pan, Volume) itself.
                source.spatialBlend = 0f;
                _player = _host.AddComponent<SoundCuePlayer>();
                _player.hideFlags = HideFlags.HideAndDontSave;
                _player.PreloadVoices = 0;
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                AssemblyReloadEvents.beforeAssemblyReload -= Stop;
                AssemblyReloadEvents.beforeAssemblyReload += Stop;
            }
            _player.Cue = cue;
            EnsureSpace();
            // Audio files are read again, in case they were reimported (not on every seek of a playing preview).
            if (!already)
            {
                SoundClipCache.Clear();
                SoundCueVoicePool.Clear(cue);
            }
            if (time <= 0f || !already)
                _startedAt = EditorApplication.timeSinceStartup;
            Restart(time);
            Apply();
            _dirtyCount = EditorUtility.GetDirtyCount(cue);
            Changed?.Invoke();
        }

        public static void Stop()
        {
            bool was = Playing != null;
            if (_player != null)
                _player.Stop();
            if (_host != null)
                Object.DestroyImmediate(_host);
            _host = null;
            _player = null;
            ClearSpace();
            Owner = null;
            _orbit = null;
            EditorApplication.update -= Update;
            if (was)
                Changed?.Invoke();
        }

        // Plays the cue again from a time, and notes when, for when it reaches its end.
        private static void Restart(float time)
        {
            _player.Play(time);
            _playedAt = EditorApplication.timeSinceStartup;
            _playedFrom = Mathf.Max(0f, time);
        }

        #region Simulated surroundings

        /// <summary>
        /// The surroundings the preview plays in: Spatial Reverb and Delay hear them like a baked Sound Field (rooms ring
        /// from where they are, echoes come off the walls). None: they use their own settings. Changing it replays.
        /// </summary>
        public static PreviewSpace Space
        {
            get => (PreviewSpace)EditorPrefs.GetInt("Remedy.SoundCuePreview.Space", (int)PreviewSpace.None);
            set
            {
                if (value == Space) return;
                EditorPrefs.SetInt("Remedy.SoundCuePreview.Space", (int)value);
                var cue = Playing;
                var owner = Owner;
                if (cue != null)
                {
                    var orbit = _orbit;
                    Start(cue, 0f, owner);
                    _orbit = orbit;
                }
            }
        }

        /// <summary>The surroundings playing now (for drawing them), or null.</summary>
        public static PreviewSpaceSimulation Simulation { get; private set; }

        private static GameObject _spaceRoot;

        // The picked surroundings, made (or remade) for this play; their room and walls are sources of their own.
        private static void EnsureSpace()
        {
            var space = Space;
            if (Simulation != null && Simulation.Space == space && _spaceRoot != null)
                return;
            ClearSpace();
            if (space != PreviewSpace.None)
            {
                _spaceRoot = new GameObject("Sound Cue Preview Space") { hideFlags = HideFlags.HideAndDontSave };
                Simulation = new PreviewSpaceSimulation(space, _spaceRoot.transform);
            }
            SoundSpaceSimulation.Current = Simulation;
            if (_player != null)
                _player.ScheduleInEditor = Simulation != null;
        }

        private static void ClearSpace()
        {
            if (_spaceRoot != null)
                Object.DestroyImmediate(_spaceRoot);
            _spaceRoot = null;
            Simulation = null;
            SoundSpaceSimulation.Current = null;
        }

        #endregion

        /// <summary>Starts the motion over from its beginning.</summary>
        public static void RestartMotion() => _startedAt = EditorApplication.timeSinceStartup;

        // Hands the current position to the player (its pan, falloff and distance).
        private static void Apply()
        {
            if (_player == null)
                return;
            var position = CurrentPosition;
            // Where the sound is around the imaginary listener (middle, facing +Z), for the simulated surroundings.
            bool spatial = _player.Cue == null || _player.Cue.IsSpatial;
            _host.transform.position = spatial ? new Vector3(position.x, 0f, position.y) : Vector3.zero;
            if (spatial)
                _player.SetPreviewPosition(position);
            else
            {
                _player.SetPreviewPosition(Vector2.zero, falloff: false);
                _player.DistanceOverride = position.magnitude;
                if (_player.Voice != null)
                    _player.Voice.Context.Distance = position.magnitude;
            }
        }

        private static void Update()
        {
            if (_player == null || _player.Cue == null)
            {
                Stop();
                return;
            }
            var voice = _player.Voice;
            if (voice == null || (voice.Finished && !_player.Source.isPlaying))
            {
                if (voice != null && Loop)
                {
                    Restart(0f);
                    Apply();
                    return;
                }
                Stop();
                return;
            }
            // Past the end of the cue, by the clock (the AudioSource can keep "playing" its streamed clip's silence):
            // start over with Loop on, otherwise stop.
            float duration = voice.Context.Duration;
            if (duration > 0f && _playedFrom + (EditorApplication.timeSinceStartup - _playedAt) >= duration)
            {
                if (Loop)
                {
                    Restart(0f);
                    Apply();
                }
                else
                    Stop();
                return;
            }
            Apply();
            // Edited while playing: play it again with the changes.
            int dirty = EditorUtility.GetDirtyCount(_player.Cue);
            if (dirty != _dirtyCount)
            {
                _dirtyCount = dirty;
                Restart(0f);
                Apply();
                Changed?.Invoke();
            }
        }
    }
}
#endif
