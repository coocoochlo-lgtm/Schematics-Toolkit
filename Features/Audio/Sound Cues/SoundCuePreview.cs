#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Editor only: plays a Sound Cue without entering Play Mode (the graph window's Preview and the Audio page's
    /// cards), through a hidden 2D player. It's played again whenever the cue is edited while previewing, so changes
    /// are heard as they're made, and stops at the end of the cue.
    /// </summary>
    public static class SoundCuePreview
    {
        private static GameObject _host;
        private static SoundCuePlayer _player;
        private static int _dirtyCount;
        private static float _distance;

        /// <summary>The cue being previewed (null when nothing is).</summary>
        public static SoundCueGraph Playing => _player != null && _player.Voice != null ? _player.Cue : null;

        /// <summary>The previewing voice (for its time and warnings), or null.</summary>
        public static SoundCueVoice Voice => _player != null ? _player.Voice : null;

        /// <summary>Raised when a preview starts or stops.</summary>
        public static event System.Action Changed;

        /// <summary>The distance to the listener Distance To Listener nodes read while previewing.</summary>
        public static float Distance
        {
            get => _distance;
            set
            {
                _distance = value;
                if (_player != null)
                {
                    _player.DistanceOverride = value;
                    if (_player.Voice != null)
                        _player.Voice.Context.Distance = value;
                }
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
                source.spatialBlend = 0f;
                _player = _host.AddComponent<SoundCuePlayer>();
                _player.hideFlags = HideFlags.HideAndDontSave;
                EditorApplication.update -= Update;
                EditorApplication.update += Update;
                AssemblyReloadEvents.beforeAssemblyReload -= Stop;
                AssemblyReloadEvents.beforeAssemblyReload += Stop;
            }
            _player.Cue = cue;
            _player.DistanceOverride = _distance;
            // Audio files are read again, in case they were reimported.
            SoundClipCache.Clear();
            SoundCueVoicePool.Clear(cue);
            _player.Play();
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
            // Edited while playing: play it again with the changes.
            int dirty = EditorUtility.GetDirtyCount(_player.Cue);
            if (dirty != _dirtyCount)
            {
                _dirtyCount = dirty;
                var cue = _player.Cue;
                _player.Play();
                Changed?.Invoke();
            }
        }
    }
}
#endif
