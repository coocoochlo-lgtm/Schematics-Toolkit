#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Editor only: plays a Sound Cue without entering Play Mode (the graph window's Preview and the Audio page's
    /// cards). It's heard in 3D around an imaginary listener: placed by hand on the preview's radar pad, or moved by a
    /// motion (orbiting, flying by, coming closer...), panned and fading with distance as it goes, and Distance To
    /// Listener nodes follow it. It's played again whenever the cue is edited while previewing, so changes are heard
    /// as they're made, and stops at the end of the cue.
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
        private static Vector2 _position = new(0f, 1f);

        /// <summary>The cue being previewed (null when nothing is).</summary>
        public static SoundCueGraph Playing => _player != null && _player.Voice != null ? _player.Cue : null;

        /// <summary>The previewing voice (for its time and warnings), or null.</summary>
        public static SoundCueVoice Voice => _player != null ? _player.Voice : null;

        /// <summary>Raised when a preview starts or stops.</summary>
        public static event System.Action Changed;

        /// <summary>Heard in 3D (panned, fading with distance); off, flat and centered as before.</summary>
        public static bool Spatial = true;
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
                if (Motion == PreviewMotion.Still)
                    return _position;
                float t = (float)(EditorApplication.timeSinceStartup - _startedAt);
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

        public static void Play(SoundCueGraph cue)
        {
            if (cue == null)
                return;
            if (_host == null)
            {
                _host = new GameObject("Sound Cue Preview") { hideFlags = HideFlags.HideAndDontSave };
                var source = _host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                // Flat as far as Unity's concerned: the preview pans and fades the sound itself.
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
            _player.PreviewStereo = true;
            // Audio files are read again, in case they were reimported.
            SoundClipCache.Clear();
            SoundCueVoicePool.Clear(cue);
            _startedAt = EditorApplication.timeSinceStartup;
            _player.Play();
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
            EditorApplication.update -= Update;
            if (was)
                Changed?.Invoke();
        }

        /// <summary>Starts the motion over from its beginning.</summary>
        public static void RestartMotion() => _startedAt = EditorApplication.timeSinceStartup;

        // Hands the current position to the player (its pan, falloff and distance).
        private static void Apply()
        {
            if (_player == null)
                return;
            var position = CurrentPosition;
            if (Spatial)
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
                Stop();
                return;
            }
            Apply();
            // Edited while playing: play it again with the changes.
            int dirty = EditorUtility.GetDirtyCount(_player.Cue);
            if (dirty != _dirtyCount)
            {
                _dirtyCount = dirty;
                _player.Play();
                Apply();
                Changed?.Invoke();
            }
        }
    }
}
#endif
