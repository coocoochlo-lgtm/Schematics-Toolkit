using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Plays a <see cref="SoundCueGraph"/> through this object's AudioSource, so it's positioned in 3D, routed to its
    /// mixer group and attenuated like any other sound. The cue is rendered as it plays (a streamed clip asks for
    /// samples a little ahead), and keeps track of where it is in the sound. Made by the Play Sound Cue node, or add it
    /// yourself.
    /// </summary>
    /// <remarks>
    /// Cheap to play often: voices come from <see cref="SoundCueVoicePool"/> (built once, then reused), its streamed
    /// clip is kept between plays, and one-shot players (<see cref="PlayAt"/>) are pooled objects rather than made and
    /// destroyed each time. A voice too far away to be heard isn't rendered (its time carries on).
    /// </remarks>
    [AddComponentMenu("Remedy/Audio/Sound Cue Player")]
    [RequireComponent(typeof(AudioSource))]
    public class SoundCuePlayer : MonoBehaviour
    {
        [Tooltip("The Sound Cue to play.")]
        public SoundCueGraph Cue;
        [Tooltip("Play when the object is enabled.")]
        public bool PlayOnEnable;
        [Tooltip("Destroy this object once the cue has finished.")]
        public bool DestroyWhenDone;
        [Tooltip("Voices built for the cue when this player is created, so its first plays don't build them (0: none).")]
        public int PreloadVoices = 1;

        /// <summary>Below this loudness (about -60 dB, after distance falloff) a voice isn't rendered.</summary>
        public const float CullBelow = 0.001f;

        private AudioSource _source;
        private AudioClip _clip;
        private int _clipRate, _clipLength;
        private bool _clipLoops;
        private volatile SoundCueVoice _voice;
        private bool _pooled; // a one-shot host from PlayAt: back to the pool when done
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

        #region Preview spatializing (editor)

        /// <summary>
        /// Renders in stereo, panned and attenuated as if heard from <see cref="SetPreviewPosition"/> (for the editor's
        /// preview, which has no Audio Listener to be heard by). Takes effect on the next <see cref="Play"/>.
        /// </summary>
        [NonSerialized] public bool PreviewStereo;

        // Left/right gains the audio thread ramps towards, so a moving sound doesn't click.
        private volatile float _targetLeft = 1f, _targetRight = 1f;
        private float _left = 1f, _right = 1f;
        private float[] _mono = Array.Empty<float>();

        /// <summary>
        /// Where the preview hears the sound from, in meters around the listener (x right, y ahead): sets its
        /// distance (for Distance To Listener), its pan, and its falloff (as an AudioSource's logarithmic falloff
        /// from 1 m).
        /// </summary>
        public void SetPreviewPosition(Vector2 position, bool falloff = true)
        {
            float distance = position.magnitude;
            DistanceOverride = distance;
            if (_voice != null)
            {
                _voice.Context.Distance = distance;
                RunProbes(_voice);
            }
            float gain = falloff ? 1f / Mathf.Max(1f, distance) : 1f;
            // Equal-power pan from the direction (right = +1); straight ahead or behind is centered.
            float pan = distance > 0.001f ? Mathf.Clamp(position.x / distance, -1f, 1f) : 0f;
            float angle = (pan + 1f) * Mathf.PI * 0.25f;
            _targetLeft = Mathf.Cos(angle) * gain * 1.41421356f;
            _targetRight = Mathf.Sin(angle) * gain * 1.41421356f;
        }

        #endregion

        private void Awake()
        {
            if (Cue != null && PreloadVoices > 0 && Application.isPlaying)
                Cue.Preload(PreloadVoices);
        }

        private void OnEnable()
        {
            if (PlayOnEnable && Cue != null)
                Play();
        }

        private void OnDisable() => Stop();

        private void OnDestroy()
        {
            Stop();
            if (_clip != null)
            {
                if (Application.isPlaying) Destroy(_clip);
                else DestroyImmediate(_clip);
                _clip = null;
            }
        }

        /// <summary>Plays the cue from the start (stopping it first if it's playing).</summary>
        public void Play()
        {
            Stop();
            if (Cue == null)
                return;

            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var voice = SoundCueVoicePool.Rent(Cue, rate);
            if (voice == null)
                return;
            foreach (var warning in voice.Warnings)
                Debug.LogWarning($"Sound Cue \"{Cue.name}\": {warning}", Cue);
            voice.Context.Distance = CurrentDistance();
            _voice = voice;
            RunProbes(voice);

            // Endless cues: a long clip that loops (the voice keeps counting time across loops). The clip is kept
            // and reused while its rate and length stay the same.
            bool endless = voice.LengthSamples <= 0;
            int length = endless ? rate * 10 : (int)Math.Min(int.MaxValue, Math.Max(1, voice.LengthSamples));
            int channels = PreviewStereo ? 2 : 1;
            if (_clip == null || _clipRate != rate || _clipLength != length || _clipLoops != endless || _clip.channels != channels)
            {
                if (_clip != null)
                {
                    if (Application.isPlaying) Destroy(_clip);
                    else DestroyImmediate(_clip);
                }
                _clip = AudioClip.Create($"Sound Cue: {Cue.name}", length, channels, rate, true, Read, _ => { });
                _clipRate = rate;
                _clipLength = length;
                _clipLoops = endless;
                _clipChannels = channels;
            }
            Source.clip = _clip;
            Source.loop = endless;
            UpdateControls(voice);
            Source.Play();
        }

        /// <summary>Stops the cue (its voice goes back to the pool).</summary>
        public void Stop()
        {
            var voice = _voice;
            _voice = null;
            if (_source != null)
                _source.Stop();
            SoundCueVoicePool.Return(voice);
        }

        /// <summary>Sets one of the cue's Parameters while it plays.</summary>
        public void SetParameter(string name, float value) => _voice?.SetParameter(name, value);

        // The streamed clip asks for its next samples (from the audio thread).
        private void Read(float[] data)
        {
            var voice = _voice;
            if (voice == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            if (_clipChannels < 2)
            {
                voice.Render(data, 0, data.Length);
                return;
            }

            // Stereo preview: the voice renders mono, then it's panned (gains eased across the chunk).
            int frames = data.Length / 2;
            if (_mono.Length < frames)
                _mono = new float[frames];
            voice.Render(_mono, 0, frames);
            float left = _left, right = _right, toLeft = _targetLeft, toRight = _targetRight;
            float step = frames > 0 ? 1f / frames : 1f;
            for (int i = 0; i < frames; i++)
            {
                float t = (i + 1) * step;
                float sample = _mono[i];
                data[i * 2] = sample * (left + (toLeft - left) * t);
                data[i * 2 + 1] = sample * (right + (toRight - right) * t);
            }
            _left = toLeft;
            _right = toRight;
        }

        // The clip's channel count, kept as a plain field: the audio thread mustn't touch Unity objects.
        private volatile int _clipChannels = 1;

        private void Update()
        {
            var voice = _voice;
            if (voice == null)
            {
                if (_pooled || (DestroyWhenDone && Application.isPlaying))
                    Finish();
                return;
            }
            UpdateControls(voice);
            if (voice.Finished && !Source.isPlaying)
            {
                Stop();
                if (_pooled || (DestroyWhenDone && Application.isPlaying))
                    Finish();
            }
        }

        // What the voice reads from the world (distance), whether it's heard at all, and its shared reverb send.
        private void UpdateControls(SoundCueVoice voice)
        {
            float distance = CurrentDistance();
            voice.Context.Distance = distance;
            voice.Culled = !DistanceOverride.HasValue && Inaudible(distance);
            if (voice.Context.UsesSharedReverb)
                Source.reverbZoneMix = voice.Context.ReverbSend;
            if (voice.Context.Probes.Count > 0 && !voice.Culled && Time.unscaledTime >= _nextProbe)
                RunProbes(voice);
        }

        /// <summary>How often the world is probed for the cue's Occlusion and Surface controls, in seconds.</summary>
        public const float ProbeInterval = 0.1f;

        private float _nextProbe;

        // Answers the voice's questions about the world (main thread); in the preview, the nodes' preview values.
        private void RunProbes(SoundCueVoice voice)
        {
            var probes = voice.Context.Probes;
            if (probes.Count == 0)
                return;
            // Spread players' probes over the interval so they don't all cast on the same frame.
            _nextProbe = Time.unscaledTime + ProbeInterval * UnityEngine.Random.Range(0.85f, 1.15f);
            if (!DistanceOverride.HasValue)
                CurrentDistance(); // finds the listener
            var world = new SoundProbeWorld
            {
                Self = gameObject,
                Position = transform.position,
                Rotation = transform.rotation,
                Listener = _listener != null ? _listener.gameObject : null,
                ListenerPosition = _listener != null ? _listener.transform.position : transform.position,
                Previewing = DistanceOverride.HasValue,
            };
            for (int i = 0; i < probes.Count; i++)
            {
                try { probes[i].Update(world); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        // Whether the source's own distance falloff (and volume) leaves the cue too quiet to hear.
        private bool Inaudible(float distance)
        {
            var source = Source;
            if (source.mute)
                return true;
            float gain = source.volume;
            if (source.spatialBlend >= 0.99f && _listener != null)
            {
                float min = Mathf.Max(0.01f, source.minDistance), max = Mathf.Max(min, source.maxDistance);
                switch (source.rolloffMode)
                {
                    case AudioRolloffMode.Linear:
                        gain *= Mathf.Clamp01(1f - (distance - min) / Mathf.Max(0.001f, max - min));
                        break;
                    case AudioRolloffMode.Logarithmic:
                        // Unity stops attenuating past Max Distance, so it's never silent: just very quiet.
                        gain *= min / Mathf.Clamp(distance, min, max);
                        break;
                    default:
                        var curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                        if (curve != null)
                            gain *= curve.Evaluate(Mathf.Clamp01(distance / max));
                        break;
                }
            }
            return gain < CullBelow;
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

        #region One-shot players (pooled)

        private static readonly Stack<SoundCuePlayer> _idleHosts = new();
        private static Transform _poolRoot;

        /// <summary>
        /// Plays a cue at a position (following <paramref name="follow"/> when given) on a pooled player, set up like
        /// the Audio Data's SFX source (its mixer group and 3D settings) when there is one. It goes back to the pool
        /// when the cue ends.
        /// </summary>
        public static SoundCuePlayer PlayAt(SoundCueGraph cue, Vector3 position, Transform follow = null, float volume = 1f, float spatialBlend = 1f)
        {
            if (cue == null)
                return null;

            var player = TakeHost(cue);
            var host = player.gameObject;
            host.transform.SetParent(follow, worldPositionStays: false);
            host.transform.position = position;
            host.SetActive(true);

            var source = player.Source;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            player.Cue = cue;
            player.Play();
            return player;
        }

        private static SoundCuePlayer TakeHost(SoundCueGraph cue)
        {
            while (_idleHosts.Count > 0)
            {
                var idle = _idleHosts.Pop();
                if (idle != null)
                {
                    idle.name = $"Sound Cue: {cue.name}";
                    return idle;
                }
            }

            GameObject host = null;
            var prefab = SfxPrefab();
            if (prefab != null && prefab.GetComponent<AudioSource>() != null)
            {
                host = Instantiate(prefab);
            }
            host ??= new GameObject();
            host.SetActive(false);
            host.name = $"Sound Cue: {cue.name}";
            var source = host.GetComponent<AudioSource>() ?? host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var player = host.GetComponent<SoundCuePlayer>() ?? host.AddComponent<SoundCuePlayer>();
            player._pooled = true;
            player.PreloadVoices = 0;
            return player;
        }

        // A pooled one-shot is parked for reuse; anything else is destroyed (Destroy When Done).
        private void Finish()
        {
            if (!_pooled)
            {
                Destroy(gameObject);
                return;
            }
            if (!Application.isPlaying)
                return;
            if (_poolRoot == null)
            {
                var root = new GameObject("Sound Cue Players (idle)");
                DontDestroyOnLoad(root);
                _poolRoot = root.transform;
            }
            Cue = null;
            transform.SetParent(_poolRoot, worldPositionStays: false);
            gameObject.SetActive(false);
            if (!_idleHosts.Contains(this))
                _idleHosts.Push(this);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewSession()
        {
            _idleHosts.Clear();
            _poolRoot = null;
        }

        private static GameObject SfxPrefab()
        {
            try { return AudioData.SFXSourcePrefab; }
            catch { return null; } // no Audio Data in the project yet
        }

        #endregion
    }
}
