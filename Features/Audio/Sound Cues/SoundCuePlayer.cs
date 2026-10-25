using Remedy.Schematics;
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
    public class SoundCuePlayer : SchematicGraphRunner
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

        /// <summary>
        /// The editor's Scene view preview: previewing (panned and faded by <see cref="SetPreviewPosition"/>), but this
        /// player stands where the sound is in the scene and <see cref="WorldListener"/> is the Scene view's camera, so
        /// its Spatial effects and Around Corners hear the scene's real Sound Fields.
        /// </summary>
        [NonSerialized] public bool WorldPreview;
        /// <summary>Where the Scene view's camera is, for <see cref="WorldPreview"/>.</summary>
        [NonSerialized] public Vector3 WorldListener;
        private float _nextWorldProbe;

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
            _previewPosition = position;
            if (_voice != null)
            {
                _voice.Context.Distance = distance;
                // (The Scene view's preview asks the real Sound Fields: about as often as in the game, not every frame.)
                if (!WorldPreview || Time.realtimeSinceStartup >= _nextWorldProbe)
                {
                    _nextWorldProbe = Time.realtimeSinceStartup + ProbeInterval;
                    RunProbes(_voice);
                }
            }
            var source = Source;
            if (source == null)
                return;
            source.volume = falloff ? PreviewGain(distance) : 1f;
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

        private SoundNoise _noise;

        /// <summary>Parameters to set as the next play starts (before a sample is made), then cleared.</summary>
        [NonSerialized] public List<KeyValuePair<string, float>> StartParameters;

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
            _lastListenerDistance = -1f;
            long start = (long)(Mathf.Max(0f, startTime) * (double)rate);
            if (voice.LengthSamples > 0)
                start = Math.Min(start, voice.LengthSamples - 1);
            voice.Context.SamplePosition = Math.Max(0, start);
            if (StartParameters != null)
            {
                foreach (var pair in StartParameters)
                    voice.SetParameter(pair.Key, pair.Value);
                StartParameters = null;
            }
            // A cue that reacts to Signals can be sent them: its object gets an address of its own when it isn't a
            // Schematic Object (a pooled one-shot), so a Signal sent To it reaches only it.
            if (voice.Context.LowLatency && Application.isPlaying)
                SignalData.MakeAddressable(gameObject);
            // In the game, started a moment ahead on the audio clock: then it's known exactly when each sample is heard,
            // which Spatial reverb needs to feed rooms and echoes in time with it. Known before the clip is touched:
            // Unity reads the start of a streamed clip as soon as it's set up, and sends from samples rendered before
            // this was known were dropped (a short hit could lose all of its echoes).
            // A cue that changes as it plays (a Trigger, Parameters) is rendered as it's heard instead (see SoundCueLiveRender).
            bool live = voice.Context.Live;
            bool scheduled = !live && (Application.isPlaying || ScheduleInEditor) && Mathf.Approximately(Source.pitch, 1f);
            double at = AudioSettings.dspTime + 0.08;
            voice.Context.HeardStart = scheduled ? at - start / (double)rate : -1;
            _voice = voice;
            ApplyFalloff();
            UpdateRoom(immediate: true);
            RunProbes(voice);

            if (live)
            {
                if (_live == null && !TryGetComponent(out _live))
                    _live = gameObject.AddComponent<SoundCueLiveRender>();
                _live.Begin(voice);
                // Unity places a clip of ones and the cue is multiplied in after: only a plain left/right volume
                // works on that. A spatializer plugin (Spatialize, from the SFX Source Prefab) filters by direction
                // instead, and its filters pass next to nothing of a steady signal straight ahead or behind: the
                // sound vanished whenever it was in front of or behind the listener.
                if (Source.spatialize)
                {
                    _spatializeWas = true;
                    Source.spatialize = false;
                }
                Source.clip = SpaceBus.DcClip(rate);
                Source.loop = true;
                UpdateControls(voice);
                Source.Play();
                MakeNoise(voice, start, rate, endless: true);
                return;
            }
            if (_live != null)
                _live.End();
            // (A streamed cue is real sound: a spatializer plugin is fine on it, if the source had one.)
            if (_spatializeWas)
            {
                Source.spatialize = true;
                _spatializeWas = false;
            }

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

            MakeNoise(voice, start, rate, endless);
        }

        // What AI hear (in the game): a noise where it plays, for as long as it plays.
        private void MakeNoise(SoundCueVoice voice, long start, int rate, bool endless)
        {
            var output = Cue.Output;
            if (Application.isPlaying && output != null && output.HeardByAI > 0f)
                _noise = SoundHearing.Make(transform.position, output.HeardByAI,
                    endless ? float.PositiveInfinity : (voice.LengthSamples - start) / (float)rate,
                    Follow != null ? Follow.gameObject : gameObject, Cue.name, transform);
        }

        private SoundCueLiveRender _live;

        /// <summary>Whether the cue's being rendered as it's heard (it has a Trigger).</summary>
        public bool IsLive => _live != null && _live.Voice != null && _live.Voice == _voice;

        /// <summary>Stops the cue (its voice goes back to the pool).</summary>
        public void Stop()
        {
            var voice = _voice;
            _voice = null;
            _noise?.End();
            _noise = null;
            if (_live != null)
                _live.End();
            if (_source != null)
                _source.Stop();
            SoundCueVoicePool.Return(voice);
            LeaveRoom();
        }

        /// <summary>Sets one of the cue's Parameters while it plays.</summary>
        public void SetParameter(string name, float value) => _voice?.SetParameter(name, value);

        /// <summary>The cue it plays (what a Graph Variable holding it runs).</summary>
        public override SchematicGraph RunningGraph => Cue;

        /// <summary>A one-shot following an object belongs to it.</summary>
        public override GameObject BelongsTo => Follow != null ? Follow.gameObject : null;

        /// <summary>Set Graph Parameters: a number (or true/false) for one of the cue's Parameters.</summary>
        public override bool SetGraphParameter(string name, object value)
        {
            if (_voice == null || !_voice.HasParameter(name))
                return false;
            float number = value switch
            {
                float f => f,
                int i => i,
                double d => (float)d,
                bool b => b ? 1f : 0f,
                _ => float.NaN,
            };
            if (float.IsNaN(number))
                return false;
            _voice.SetParameter(name, number);
            return true;
        }

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
            // (A live cue's source plays on: its end is the voice's.)
            if (voice.Finished && (!Source.isPlaying || IsLive))
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
            if (voice.Context.FollowsListener)
                UpdateListenerControls(voice.Context, distance);
            UpdateRoom(immediate: false);
            UpdateRoomFade();
            voice.Culled = !DistanceOverride.HasValue && _fade == RoomFade.None && Inaudible(distance);
            if (voice.Context.UsesSharedReverb)
                Source.reverbZoneMix = voice.Context.ReverbSend;
            if (voice.Context.Probes.Count > 0 && !voice.Culled && Time.unscaledTime >= _nextProbe)
                RunProbes(voice);
            if (LogDirect && Application.isPlaying)
                ReportDirect(voice, distance);
        }

        /// <summary>
        /// Logs, for every playing cue, each change in what decides how loud the sound itself is heard: Unity setting
        /// its source aside (out of real voices), a Sound Room fade, being culled as inaudible, and what its Around
        /// Corners and Occlusion nodes decided (Sound Spaces window: Log Direct Sound). For finding why it cuts out.
        /// </summary>
        public static bool LogDirect;

        private string _lastDirect;
        // Spatialize was on (from the SFX Source Prefab) and is off while the cue renders live (see Play).
        private bool _spatializeWas;

        private void ReportDirect(SoundCueVoice voice, float distance)
        {
            var source = Source;
            var text = new System.Text.StringBuilder();
            text.Append(source.isVirtual ? "SET ASIDE BY UNITY (virtual: out of real voices, or too quiet to it)" : "playing");
            if (!source.isPlaying) text.Append(", source NOT playing");
            if (_fade != RoomFade.None) text.Append($", Sound Room fade {_fade}");
            if (voice.Culled) text.Append(", CULLED (too quiet to render)");
            if (source.mute) text.Append(", muted");
            text.Append(IsLive ? ", rendered live" : ", streamed");
            if (IsLive && _live != null)
            {
                // (In tenths, and the cue in 6 dB steps, so it's logged as it changes, not as it wobbles.)
                float left = System.Threading.Volatile.Read(ref _live.PlacedLeft), right = System.Threading.Volatile.Read(ref _live.PlacedRight);
                float level = System.Threading.Volatile.Read(ref _live.CueLevel);
                float db = level > 1e-5f ? Mathf.Round(20f * Mathf.Log10(level) / 6f) * 6f : -100f;
                text.Append($" (Unity places it at left {Mathf.Round(left * 10f) / 10f:0.0}, right {Mathf.Round(right * 10f) / 10f:0.0}; "
                    + $"the cue's own peak {(db <= -99f ? "silent" : $"{db:0} dB")})");
            }
            if (source.outputAudioMixerGroup != null) text.Append($", mixer group {source.outputAudioMixerGroup.name}");
            if (source.spatialBlend < 0.99f) text.Append($", spatial blend {source.spatialBlend:0.00}");
            if (_spatializeWas) text.Append($", spatializer plugin turned off for live rendering ({AudioSettings.GetSpatializerPluginName()})");
            else if (source.spatialize) text.Append($", spatializer plugin ON ({AudioSettings.GetSpatializerPluginName()})");
            if (source.spread > 0f) text.Append($", spread {source.spread:0}");
            foreach (var filter in GetComponents<Behaviour>())
                if (filter is AudioLowPassFilter || filter is AudioHighPassFilter || filter is AudioEchoFilter || filter is AudioReverbFilter
                    || filter is AudioDistortionFilter || filter is AudioChorusFilter)
                    text.Append($", {filter.GetType().Name}{(filter.enabled ? "" : " (off)")}");
            foreach (var probe in voice.Context.Probes)
            {
                var report = probe.DirectReport;
                if (report != null) text.Append("; ").Append(report);
            }
            string state = text.ToString();
            if (state == _lastDirect)
                return;
            _lastDirect = state;
            Debug.Log($"[Direct] {(Cue != null ? Cue.name : "?")} on {(Follow != null ? Follow.name : name)}, {distance:0.0} m from the listener, "
                + $"volume {source.volume:0.00}, heard at {HearingAt(distance):0.000}: {state}", this);
        }

        private Vector2 _previewPosition;
        private float _lastListenerDistance = -1f, _nextListenerRoomCheck;

        // What a Listener node reads: where the sound is from the listener (in the preview, from its imaginary one at
        // the middle facing ahead), how fast they're closing in, and whether they share a Sound Room.
        private void UpdateListenerControls(SoundContext context, float distance)
        {
            Vector3 direction;
            if (DistanceOverride.HasValue)
            {
                float length = _previewPosition.magnitude;
                direction = length > 0.0001f ? new Vector3(_previewPosition.x, 0f, _previewPosition.y) / length : Vector3.forward;
            }
            else if (_listener != null)
            {
                var listener = _listener.transform;
                Vector3 to = listener.InverseTransformDirection(transform.position - listener.position);
                float length = to.magnitude;
                direction = length > 0.0001f ? to / length : Vector3.forward;
            }
            else
                return;
            context.ListenerAngle = Vector3.Angle(Vector3.forward, direction);
            context.ListenerPan = direction.x;
            context.ListenerFront = direction.z;
            context.ListenerElevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;

            float dt = Time.unscaledDeltaTime;
            if (_lastListenerDistance >= 0f && dt > 0f)
            {
                float closing = (_lastListenerDistance - distance) / dt;
                context.ApproachSpeed = Mathf.Lerp(context.ApproachSpeed, closing, 1f - Mathf.Exp(-dt / 0.1f));
            }
            _lastListenerDistance = distance;

            if (!DistanceOverride.HasValue && _listener != null && Application.isPlaying && Time.unscaledTime >= _nextListenerRoomCheck)
            {
                _nextListenerRoomCheck = Time.unscaledTime + ProbeInterval;
                context.SameRoom = SoundRoom.Count == 0 || SoundRoom.At(transform.position) == SoundRoom.At(_listener.transform.position) ? 1f : 0f;
            }
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
                // What it belongs to: the object a one-shot follows, else its own (Signals to it reach Triggers; casts
                // from it don't hit it).
                Self = Follow != null ? Follow.gameObject : gameObject,
                Position = transform.position,
                Rotation = transform.rotation,
                Listener = DistanceOverride.HasValue || _listener == null ? null : _listener.gameObject,
                // The preview's imaginary listener is at the middle, facing ahead (+Z); the sound is placed around it.
                // The Scene view's preview: its camera, in the scene.
                ListenerPosition = WorldPreview ? WorldListener
                    : DistanceOverride.HasValue ? Vector3.zero : _listener != null ? _listener.transform.position : transform.position,
                Previewing = DistanceOverride.HasValue,
                InWorld = WorldPreview,
                Host = gameObject,
                Room = _room,
                // The preview fades a sound by 1 / distance itself (its source is flat).
                Hearing = DistanceOverride.HasValue ? _previewHearing ??= PreviewGain : _hearing ??= HearingAt,
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

        private Func<float, float> _hearing, _previewHearing;

        // The cue's Output, when it sets the falloff itself (Falloff isn't Audio Source).
        private SoundCueOutput OwnFalloff => Cue != null && Cue.Output is SoundCueOutput output && output.OwnsFalloff ? output : null;

        // The cue's own falloff onto the AudioSource (a custom rolloff out to its Audible Range), for 3D sources.
        private void ApplyFalloff()
        {
            var output = OwnFalloff;
            var source = Source;
            if (output == null || source == null)
                return;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.maxDistance = Mathf.Max(0.01f, output.AudibleRange);
            source.minDistance = Mathf.Clamp(output.FullVolumeWithin, 0.01f, source.maxDistance);
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, output.RolloffCurve());
        }

        // The preview's falloff (its source is flat, so it fades the sound itself): the cue's own, else 1 / distance.
        private float PreviewGain(float distance)
        {
            var output = OwnFalloff;
            return output != null ? output.FalloffAt(distance) : 1f / Mathf.Max(1f, distance);
        }

        // How loud the source makes the sound at a distance: its volume, times its distance falloff when it's 3D.
        private float HearingAt(float distance)
        {
            var source = Source;
            float gain = source.volume;
            if (source.spatialBlend >= 0.99f && OwnFalloff is SoundCueOutput own)
                gain *= own.FalloffAt(distance);
            else if (source.spatialBlend >= 0.99f)
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
        public static SoundCuePlayer PlayAt(SoundCueGraph cue, Vector3 position, Transform follow = null, float volume = 1f, float spatialBlend = 1f,
            List<KeyValuePair<string, float>> parameters = null)
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
            player.StartParameters = parameters;
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
            // Its address (and every Trigger listening on it) goes: the next cue it plays is someone else's.
            SignalData.ForgetAddress(gameObject);
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
            // Pooled one-shots live under their own root: they're found for the objects they follow.
            SchematicGraphRunner.Elsewhere -= BusyHosts;
            SchematicGraphRunner.Elsewhere += BusyHosts;
            _idleHosts.Clear();
            _busyHosts.Clear();
            _poolRoot = null;
            _warnedNoListener = false;
        }

        private static IEnumerable<SchematicGraphRunner> BusyHosts() => _busyHosts;

        private static GameObject SfxPrefab()
        {
            try { return AudioData.SFXSourcePrefab; }
            catch { return null; } // no Audio Data in the project yet
        }

        #endregion
    }
}
