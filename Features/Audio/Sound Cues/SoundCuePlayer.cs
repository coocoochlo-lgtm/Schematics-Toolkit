using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

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
        [Tooltip("Send the sound through the Sound Room it's in: the room's mixer group, whose effects every sound in the room shares.")]
        public bool UseRooms = true;

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

        /// <summary>
        /// Start on the audio clock outside Play Mode too (the editor's preview, when it simulates surroundings: their
        /// rooms and echo walls need to know when each sample is heard).
        /// </summary>
        [NonSerialized] public bool ScheduleInEditor;

        #region Preview spatializing (editor)

        /// <summary>
        /// Where the preview hears the sound from, in meters around the listener (x right, y ahead): sets its
        /// distance (for Distance To Listener), its pan, and its falloff (as an AudioSource's logarithmic falloff
        /// from 1 m). The pan and falloff go on the AudioSource (Stereo Pan, Volume), which apply as the sound is
        /// heard: the streamed clip is rendered about a second ahead, so anything done to its samples lags that much.
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
            var source = Source;
            if (source == null)
                return;
            source.volume = falloff ? 1f / Mathf.Max(1f, distance) : 1f;
            // From the direction (right = +1); straight ahead or behind is centered.
            source.panStereo = distance > 0.001f ? Mathf.Clamp(position.x / distance, -1f, 1f) : 0f;
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
            _busyHosts.Remove(this);
            if (_clip != null)
            {
                if (Application.isPlaying) Destroy(_clip);
                else DestroyImmediate(_clip);
                _clip = null;
            }
        }

        /// <summary>Plays the cue from the start (stopping it first if it's playing).</summary>
        public void Play() => Play(0f);

        /// <summary>Plays the cue from <paramref name="startTime"/> seconds in (stopping it first if it's playing).</summary>
        public void Play(float startTime)
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
            long start = (long)(Mathf.Max(0f, startTime) * (double)rate);
            if (voice.LengthSamples > 0)
                start = Math.Min(start, voice.LengthSamples - 1);
            voice.Context.SamplePosition = Math.Max(0, start);
            // In the game, started a moment ahead on the audio clock: then it's known exactly when each sample is heard,
            // which Spatial reverb needs to feed rooms and echoes in time with it. Known before the clip is touched:
            // Unity reads the start of a streamed clip as soon as it's set up, and sends from samples rendered before
            // this was known were dropped (a short hit could lose all of its echoes).
            bool scheduled = (Application.isPlaying || ScheduleInEditor) && Mathf.Approximately(Source.pitch, 1f);
            double at = AudioSettings.dspTime + 0.08;
            voice.Context.HeardStart = scheduled ? at - start / (double)rate : -1;
            _voice = voice;
            UpdateRoom(immediate: true);
            RunProbes(voice);

            // Endless cues: a long clip that loops (the voice keeps counting time across loops). The clip is kept
            // and reused while its rate and length stay the same.
            bool endless = voice.LengthSamples <= 0;
            int length = endless ? rate * 10 : (int)Math.Min(int.MaxValue, Math.Max(1, voice.LengthSamples));
            if (_clip == null || _clipRate != rate || _clipLength != length || _clipLoops != endless)
            {
                if (_clip != null)
                {
                    if (Application.isPlaying) Destroy(_clip);
                    else DestroyImmediate(_clip);
                }
                _clip = AudioClip.Create($"Sound Cue: {Cue.name}", length, 1, rate, true, Read, _ => { });
                _clipRate = rate;
                _clipLength = length;
                _clipLoops = endless;
            }
            Source.clip = _clip;
            Source.loop = endless;
            UpdateControls(voice);
            // The clip's own position follows, so a cue started part way through ends when it should.
            Source.timeSamples = endless ? 0 : (int)Math.Min(start, length - 1);
            if (scheduled)
                Source.PlayScheduled(at);
            else
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
            LeaveRoom();
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
            voice.Render(data, 0, data.Length);
        }

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
            UpdateRoom(immediate: false);
            UpdateRoomFade();
            voice.Culled = !DistanceOverride.HasValue && _fade == RoomFade.None && Inaudible(distance);
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
                Listener = DistanceOverride.HasValue || _listener == null ? null : _listener.gameObject,
                // The preview's imaginary listener is at the middle, facing ahead (+Z); the sound is placed around it.
                ListenerPosition = DistanceOverride.HasValue ? Vector3.zero : _listener != null ? _listener.transform.position : transform.position,
                Previewing = DistanceOverride.HasValue,
                Room = _room,
                // The preview fades a sound by 1 / distance itself (its source is flat).
                Hearing = DistanceOverride.HasValue ? PreviewHearing : _hearing ??= HearingAt,
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
            return (_listener != null ? HearingAt(distance) : source.volume) < CullBelow;
        }

        private Func<float, float> _hearing;
        private static readonly Func<float, float> PreviewHearing = distance => 1f / Mathf.Max(1f, distance);

        // How loud the source makes the sound at a distance: its volume, times its distance falloff when it's 3D.
        private float HearingAt(float distance)
        {
            var source = Source;
            float gain = source.volume;
            if (source.spatialBlend >= 0.99f)
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
            return gain;
        }

        private float CurrentDistance()
        {
            if (DistanceOverride.HasValue)
                return DistanceOverride.Value;
            if (_listener == null && Time.unscaledTime >= _nextListenerSearch)
            {
                _listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
                _nextListenerSearch = Time.unscaledTime + 1f;
                if (_listener == null && Application.isPlaying && !_warnedNoListener)
                {
                    _warnedNoListener = true;
                    Debug.LogWarning("Sound Cues: there's no Audio Listener in the scene, so nothing can be heard. Add one (usually on the camera).");
                }
            }
            return _listener != null ? Vector3.Distance(transform.position, _listener.transform.position) : 0f;
        }

        #region Sound Rooms

        /// <summary>The Sound Room the sound is in (null when it's in none, or doesn't use rooms).</summary>
        public SoundRoom Room => _room;

        private enum RoomFade { None, Out, In }

        private SoundRoom _room;
        private bool _routed;              // the source's own mixer group is remembered in _homeGroup
        private AudioMixerGroup _homeGroup, _nextGroup;
        private RoomFade _fade;
        private float _fadeVolume, _fadeTime;
        private float _nextRoomCheck;

        // Which room the sound is in (checked as often as the probes), and its mixer group when that changes: at once
        // before it starts, otherwise dipping the volume out and back in around the switch so it doesn't click. (The
        // audio thread renders ahead of what's heard, so the dip is done here, on the AudioSource, not in the samples.)
        private void UpdateRoom(bool immediate)
        {
            if (!UseRooms || DistanceOverride.HasValue || !Application.isPlaying)
                return;
            if (!immediate && Time.unscaledTime < _nextRoomCheck)
                return;
            _nextRoomCheck = Time.unscaledTime + ProbeInterval * UnityEngine.Random.Range(0.85f, 1.15f);
            var room = SoundRoom.Count > 0 ? SoundRoom.At(transform.position) : null;
            if (room == _room)
                return;
            var source = Source;
            if (!_routed)
            {
                _homeGroup = source.outputAudioMixerGroup;
                _routed = true;
            }
            float fadeTime = Mathf.Max(room != null ? room.FadeTime : 0f, _room != null ? _room.FadeTime : 0f);
            _room = room;
            var group = room != null && room.Output != null ? room.Output : _homeGroup;
            if (group == (_fade == RoomFade.None ? source.outputAudioMixerGroup : _nextGroup))
                return;
            if (immediate || !source.isPlaying || fadeTime <= 0f)
            {
                EndRoomFade();
                source.outputAudioMixerGroup = group;
                return;
            }
            if (_fade == RoomFade.None)
                _fadeVolume = source.volume;
            _nextGroup = group;
            _fadeTime = fadeTime * 0.5f;
            _fade = RoomFade.Out;
        }

        private void UpdateRoomFade()
        {
            if (_fade == RoomFade.None)
                return;
            var source = Source;
            float step = _fadeVolume * Time.unscaledDeltaTime / Mathf.Max(0.001f, _fadeTime);
            if (_fade == RoomFade.Out)
            {
                source.volume = Mathf.Max(0f, source.volume - step);
                if (source.volume <= 0f)
                {
                    source.outputAudioMixerGroup = _nextGroup;
                    _fade = RoomFade.In;
                }
            }
            else
            {
                source.volume = Mathf.Min(_fadeVolume, source.volume + step);
                if (source.volume >= _fadeVolume)
                    _fade = RoomFade.None;
            }
        }

        // Finishes a fade at once (its volume back, in the group it was heading for).
        private void EndRoomFade()
        {
            if (_fade == RoomFade.None)
                return;
            Source.outputAudioMixerGroup = _nextGroup;
            Source.volume = _fadeVolume;
            _fade = RoomFade.None;
        }

        // Back to the source's own mixer group (when stopped, so a reused player starts out of every room).
        private void LeaveRoom()
        {
            if (_source == null)
                return;
            EndRoomFade();
            if (_routed)
                _source.outputAudioMixerGroup = _homeGroup;
            _routed = false;
            _room = null;
        }

        #endregion

        #region One-shot players (pooled)

        private static readonly Stack<SoundCuePlayer> _idleHosts = new();
        private static readonly List<SoundCuePlayer> _busyHosts = new();
        private static Transform _poolRoot;
        private static bool _warnedNoListener;

        /// <summary>
        /// What a one-shot player (<see cref="PlayAt"/>) moves with. It follows by copying the position each frame
        /// rather than being parented, so the sound carries on (where it last was) when the object is destroyed: a
        /// pickup or projectile that plays a sound and removes itself.
        /// </summary>
        [NonSerialized] public Transform Follow;

        /// <summary>
        /// The Sound Cue players on an object: on it and its children, and the one-shots following it (or a child).
        /// </summary>
        public static List<SoundCuePlayer> On(GameObject target)
        {
            var found = new List<SoundCuePlayer>();
            if (target == null)
                return found;
            found.AddRange(target.GetComponentsInChildren<SoundCuePlayer>());
            foreach (var host in _busyHosts)
                if (host != null && host.Follow != null && host.Follow.IsChildOf(target.transform) && !found.Contains(host))
                    found.Add(host);
            return found;
        }

        private void LateUpdate()
        {
            if (Follow != null)
                transform.position = Follow.position;
            else if (!ReferenceEquals(Follow, null))
                Follow = null; // destroyed: stay where it last was
        }

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
            host.transform.SetParent(PoolRoot(), worldPositionStays: false);
            host.transform.position = position;
            player.Follow = follow;
            if (!_busyHosts.Contains(player))
                _busyHosts.Add(player);
            host.SetActive(true);

            var source = player.Source;
            source.volume = volume;
            // Mono cues are flat wherever they play.
            source.spatialBlend = cue.IsSpatial ? spatialBlend : 0f;
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
            // (Not "GetComponent() ?? AddComponent()": a missing component isn't C# null in the editor, so ?? never adds it.)
            if (!host.TryGetComponent<AudioSource>(out var source))
                source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            if (!host.TryGetComponent<SoundCuePlayer>(out var player))
                player = host.AddComponent<SoundCuePlayer>();
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
            Cue = null;
            Follow = null;
            _busyHosts.Remove(this);
            transform.SetParent(PoolRoot(), worldPositionStays: false);
            gameObject.SetActive(false);
            if (!_idleHosts.Contains(this))
                _idleHosts.Push(this);
        }

        // Where one-shot players live (kept across scene loads), playing or idle.
        private static Transform PoolRoot()
        {
            if (_poolRoot == null)
            {
                var root = new GameObject("Sound Cue Players");
                DontDestroyOnLoad(root);
                _poolRoot = root.transform;
            }
            return _poolRoot;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewSession()
        {
            _idleHosts.Clear();
            _busyHosts.Clear();
            _poolRoot = null;
            _warnedNoListener = false;
        }

        private static GameObject SfxPrefab()
        {
            try { return AudioData.SFXSourcePrefab; }
            catch { return null; } // no Audio Data in the project yet
        }

        #endregion
    }
}
