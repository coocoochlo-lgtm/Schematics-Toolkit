using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// A Sound Cue run on everything the player hears: it sits on the Audio Listener, and each channel of the game's
    /// whole mix goes into the cue through its Listener Input node and comes out of its Output. Ringing ears after an
    /// explosion, underwater, inside a helmet, low health: any Sound Cue node works on the whole mix. Its Parameters are
    /// set from gameplay (Set Graph Parameters on the chain), so an effect can be faded in and out.
    /// </summary>
    [AddComponentMenu("Remedy/Audio/Listener Chain")]
    [DisallowMultipleComponent]
    public sealed class SoundListenerChain : SchematicGraphRunner
    {
        [Tooltip("The cue the whole mix runs through (it takes the mix in through a Listener Input node). None: the mix is untouched.")]
        public SoundCueGraph Cue;
        [Tooltip("How much of the mix is the cue's (0: untouched, 1: only the cue's).")]
        [Range(0f, 1f)] public float Mix = 1f;

        private static SoundListenerChain _current;
        private static readonly Dictionary<string, float> _parameters = new();

        // One voice per channel (built on the main thread, swapped in whole for the audio thread).
        private SoundCueVoice[] _voices;
        private SoundCueGraph _builtCue;
        private int _builtVersion, _builtChannels;
        private float[] _in = Array.Empty<float>(), _out = Array.Empty<float>();
        private float _mix = 1f;
        private int _channels = 2;

        /// <summary>The chain on the Audio Listener (null when there's none).</summary>
        public static SoundListenerChain Current => _current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _parameters.Clear();
        }

        /// <summary>The chain on the Audio Listener, added if it has none (null when there's no listener).</summary>
        public static SoundListenerChain Ensure()
        {
            if (_current != null)
                return _current;
            var listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            if (listener == null)
                return null;
            if (!listener.TryGetComponent(out SoundListenerChain chain))
                chain = listener.gameObject.AddComponent<SoundListenerChain>();
            return chain;
        }

        /// <summary>Sets one of the chain's cue Parameters (kept, so it holds when the cue changes or the listener moves).</summary>
        public static void SetParameter(string name, float value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            _parameters[name.Trim()] = value;
            var voices = _current != null ? _current._voices : null;
            if (voices != null)
                foreach (var voice in voices)
                    voice?.SetParameter(name, value);
        }

        /// <summary>The chain's cue (what a Graph Variable holding the chain runs).</summary>
        public override SchematicGraph RunningGraph => Cue;

        /// <summary>Set Graph Parameters: a number (or true/false) for one of the chain cue's Parameters (kept).</summary>
        public override bool SetGraphParameter(string name, object value)
        {
            float? number = value switch { float f => f, int i => i, double d => (float)d, bool b => b ? 1f : 0f, _ => null };
            if (number == null)
                return false;
            SetParameter(name, number.Value);
            return true;
        }

        /// <summary>A Parameter's value as last set (or <paramref name="fallback"/>).</summary>
        public static float GetParameter(string name, float fallback = 0f)
            => name != null && _parameters.TryGetValue(name.Trim(), out float value) ? value : fallback;

        private void OnEnable()
        {
            if (TryGetComponent<AudioListener>(out _))
                _current = this;
            _channels = Mathf.Max(1, AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2);
        }

        private void OnDisable()
        {
            if (_current == this)
                _current = null;
            Volatile.Write(ref _voices, null);
        }

        private void Update()
        {
            _mix = Mix;
            // Built again when the cue, its edits or the channels change.
            int version = Cue != null ? SoundCueVoicePool.Version(Cue) : 0;
            int channels = Mathf.Clamp(_channels, 1, 8);
            var voices = _voices;
            // (A cue with a length ends: it starts again, so the chain goes on. Set its Output to Endless to avoid the seam.)
            bool ended = false;
            if (voices != null)
                foreach (var voice in voices)
                    ended |= voice.Finished;
            if (Cue != _builtCue || version != _builtVersion || channels != _builtChannels || ended)
                Build(channels, version);
            voices = _voices;
            if (voices == null)
                return;
            // The cue's world questions (Occlusion, Surface...) asked from the listener.
            var world = new SoundProbeWorld
            {
                Self = gameObject, Position = transform.position, Rotation = transform.rotation,
                Listener = gameObject, ListenerPosition = transform.position,
            };
            foreach (var voice in voices)
                foreach (var probe in voice.Context.Probes)
                {
                    try { probe.Update(world); }
                    catch (Exception e) { Debug.LogException(e, this); }
                }
        }

        private void Build(int channels, int version)
        {
            _builtCue = Cue;
            _builtVersion = version;
            _builtChannels = channels;
            if (Cue == null)
            {
                Volatile.Write(ref _voices, null);
                return;
            }
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var voices = new SoundCueVoice[channels];
            for (int c = 0; c < channels; c++)
            {
                voices[c] = SoundCueVoice.Create(Cue, rate, seed: c + 1);
                foreach (var pair in _parameters)
                    voices[c].SetParameter(pair.Key, pair.Value);
            }
            if (voices.Length > 0)
                foreach (var warning in voices[0].Warnings)
                    Debug.LogWarning($"Listener Chain \"{Cue.name}\": {warning}", Cue);
            Volatile.Write(ref _voices, voices);
        }

        // The whole mix, a block at a time (the audio thread).
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_channels != channels)
                _channels = channels; // noticed here; the voices are rebuilt to match on the main thread
            var voices = Volatile.Read(ref _voices);
            float mix = _mix;
            if (voices == null || mix <= 0f)
                return;
            int frames = data.Length / channels;
            if (_in.Length < frames)
            {
                _in = new float[frames];
                _out = new float[frames];
            }
            for (int c = 0; c < channels && c < voices.Length; c++)
            {
                var voice = voices[c];
                if (voice.Finished)
                    continue; // ended: the mix as it is until it's started again
                for (int f = 0; f < frames; f++)
                    _in[f] = data[f * channels + c];
                voice.Context.ListenerInput = _in;
                Array.Clear(_out, 0, frames);
                voice.Render(_out, 0, frames);
                for (int f = 0; f < frames; f++)
                {
                    float dry = data[f * channels + c];
                    data[f * channels + c] = dry + (_out[f] - dry) * mix;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Listener Input"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Sources"", ""description"": ""Everything the player hears, in a cue used as a Listener Chain (on the Audio Listener; set with Set Listener Chain): run it through effects to the Output to change the whole mix. Ringing ears (mix in a high tone and dull the rest), underwater (low pass, slow Reverb), a helmet (band pass, a little Distortion), low health (filter and slow it down with a Parameter). In the editor's preview it plays Preview Clip instead, so the effect can be heard while it's made."", ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""ringing ears after an explosion"", ""underwater, helmet and low-health effects on the whole mix""]}")]
    [Serializable]
    [Node(Name = "Listener Input", Path = "Sources"), Tags(SoundNode.Tag)]
    public class SoundListenerInputNode : SoundNode
    {
        [Editable, Tooltip("What the preview plays in place of the game's mix (looped).")]
        public AudioClip PreviewClip;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { Preview = PreviewClip != null ? SoundClipCache.Get(PreviewClip) : null };

        private sealed class Processor : SoundProcessor
        {
            public SoundClipCache.Entry Preview;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                var input = context.ListenerInput;
                if (input != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int at = context.BlockStart + i;
                        o[i] = at < input.Length ? input[at] : 0f;
                    }
                    return;
                }
                // Not running on the listener (the preview): the preview clip, looped.
                var samples = Preview?.Samples;
                if (samples == null || samples.Length == 0)
                {
                    Array.Clear(o, 0, count);
                    return;
                }
                double step = Preview.Frequency / (double)context.SampleRate;
                for (int i = 0; i < count; i++)
                    o[i] = samples[(int)((long)((context.SamplePosition + i) * step) % samples.Length)];
            }
        }
    }
}
