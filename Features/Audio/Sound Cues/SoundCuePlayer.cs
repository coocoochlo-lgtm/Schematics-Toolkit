using System;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Plays a <see cref="SoundCueGraph"/> through this object's AudioSource, so it's positioned in 3D, routed to its
    /// mixer group and attenuated like any other sound. The cue is rendered as it plays (a streamed clip asks for
    /// samples a little ahead), and keeps track of where it is in the sound. Made by the Play Sound Cue node, or add it
    /// yourself.
    /// </summary>
    [AddComponentMenu("Remedy/Audio/Sound Cue Player")]
    [RequireComponent(typeof(AudioSource))]
    public class SoundCuePlayer : MonoBehaviour
    {
        [Tooltip("The Sound Cue to play.")]
        public SoundCueGraph Cue;
        [Tooltip("Play when the object is enabled.")]
        public bool PlayOnEnable;
        [Tooltip("Destroy this object once the cue has finished (for one-shot players the Play Sound Cue node makes).")]
        public bool DestroyWhenDone;

        private AudioSource _source;
        private AudioClip _clip;
        private volatile SoundCueVoice _voice;
        private static AudioListener _listener;
        private static float _nextListenerSearch;

        /// <summary>The AudioSource the cue plays through.</summary>
        public AudioSource Source => _source != null ? _source : _source = GetComponent<AudioSource>();

        /// <summary>The playing voice (null when stopped).</summary>
        public SoundCueVoice Voice => _voice;

        /// <summary>Whether a cue is playing.</summary>
        public bool IsPlaying => _voice != null && !_voice.Finished && Source != null && Source.isPlaying;

        /// <summary>
        /// Used instead of the real distance to the listener when set (the editor's preview, where nothing moves).
        /// </summary>
        [NonSerialized] public float? DistanceOverride;

        private void OnEnable()
        {
            if (PlayOnEnable && Cue != null)
                Play();
        }

        private void OnDisable() => Stop();

        /// <summary>Plays the cue from the start (stopping it first if it's playing).</summary>
        public void Play()
        {
            Stop();
            if (Cue == null)
                return;

            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var voice = SoundCueVoice.Create(Cue, rate);
            if (voice == null)
                return;
            foreach (var warning in voice.Warnings)
                Debug.LogWarning($"Sound Cue \"{Cue.name}\": {warning}", Cue);
            voice.Context.Distance = CurrentDistance();
            _voice = voice;

            // Endless cues: a long clip that loops (the voice keeps counting time across loops).
            bool endless = voice.LengthSamples <= 0;
            int length = endless ? rate * 10 : (int)Math.Min(int.MaxValue, Math.Max(1, voice.LengthSamples));
            _clip = AudioClip.Create($"Sound Cue: {Cue.name}", length, 1, rate, true, Read);
            Source.clip = _clip;
            Source.loop = endless;
            Source.Play();
        }

        /// <summary>Stops the cue.</summary>
        public void Stop()
        {
            _voice = null;
            if (_source != null)
            {
                _source.Stop();
                if (_source.clip == _clip)
                    _source.clip = null;
            }
            if (_clip != null)
            {
                if (Application.isPlaying) Destroy(_clip);
                else DestroyImmediate(_clip);
                _clip = null;
            }
        }

        /// <summary>Sets one of the cue's Parameters while it plays.</summary>
        public void SetParameter(string name, float value) => _voice?.SetParameter(name, value);

        // The streamed clip asks for its next samples (from the audio thread).
        private void Read(float[] data)
        {
            var voice = _voice;
            if (voice == null)
                Array.Clear(data, 0, data.Length);
            else
                voice.Render(data, 0, data.Length);
        }

        private void Update()
        {
            var voice = _voice;
            if (voice == null)
            {
                if (DestroyWhenDone && Application.isPlaying)
                    Destroy(gameObject);
                return;
            }
            voice.Context.Distance = CurrentDistance();
            if (voice.Finished && !Source.isPlaying)
            {
                Stop();
                if (DestroyWhenDone && Application.isPlaying)
                    Destroy(gameObject);
            }
        }

        private float CurrentDistance()
        {
            if (DistanceOverride.HasValue)
                return DistanceOverride.Value;
            if (_listener == null && Time.unscaledTime >= _nextListenerSearch)
            {
                _listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
                _nextListenerSearch = Time.unscaledTime + 1f;
            }
            return _listener != null ? Vector3.Distance(transform.position, _listener.transform.position) : 0f;
        }

        /// <summary>
        /// Makes a player for a cue at a position (following <paramref name="follow"/> when given), set up like the Audio
        /// Data's SFX source (its mixer group and 3D settings) when there is one, and plays it. It's destroyed when the
        /// cue ends.
        /// </summary>
        public static SoundCuePlayer PlayAt(SoundCueGraph cue, Vector3 position, Transform follow = null, float volume = 1f, float spatialBlend = 1f)
        {
            if (cue == null)
                return null;

            GameObject host = null;
            var prefab = SfxPrefab();
            if (prefab != null && prefab.GetComponent<AudioSource>() != null)
                host = Instantiate(prefab);
            host ??= new GameObject();
            host.name = $"Sound Cue: {cue.name}";
            host.transform.position = position;
            if (follow != null)
                host.transform.SetParent(follow, worldPositionStays: true);

            var source = host.GetComponent<AudioSource>() ?? host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = spatialBlend;

            var player = host.GetComponent<SoundCuePlayer>() ?? host.AddComponent<SoundCuePlayer>();
            player.Cue = cue;
            player.DestroyWhenDone = true;
            player.Play();
            return player;
        }

        private static GameObject SfxPrefab()
        {
            try { return AudioData.SFXSourcePrefab; }
            catch { return null; } // no Audio Data in the project yet
        }
    }
}
