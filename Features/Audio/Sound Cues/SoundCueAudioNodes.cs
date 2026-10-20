// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    // Sources (sounds read at a time, like textures at a UV) and effects (which change audio as it streams through).

    /// <summary>Reads a block of samples at the given times, linearly between samples.</summary>
    internal static class SampleReader
    {
        public static void Read(float[] samples, int frequency, float[] time, float[] output, int count, bool loop)
        {
            int length = samples?.Length ?? 0;
            if (length == 0 || frequency <= 0)
            {
                Array.Clear(output, 0, count);
                return;
            }
            for (int i = 0; i < count; i++)
            {
                double position = time[i] * (double)frequency;
                if (loop)
                    position -= Math.Floor(position / length) * length;
                else if (position < 0 || position >= length - 1)
                {
                    output[i] = position >= length - 1 && position < length ? samples[length - 1] : 0f;
                    continue;
                }
                int index = (int)position;
                float fraction = (float)(position - index);
                float a = samples[index];
                float b = samples[index + 1 < length ? index + 1 : (loop ? 0 : index)];
                output[i] = a + (b - a) * fraction;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Sound Wave"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Sources"", ""description"": ""An audio file, read at its Time input like a texture at a UV: unconnected it plays from the start at normal speed; connect math on Time to play it faster or slower (higher or lower), backwards, from later on, or looped. Loop repeats it past its end. Length is how long the file is, in seconds."", ""inputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Clip"", ""type"": ""AudioClip""}], ""outputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Length"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sound Wave", Path = "Sources"), Tags(SoundNode.Tag)]
    [AssetNode(typeof(AudioClip), Title = "Sound Wave: {0}", Field = nameof(Clip))]
    public class SoundWaveNode : SoundNode
    {
        [Input("Time", Editable = false)] public float Time;
        [Editable] public AudioClip Clip;
        [Editable] public bool Loop;
        [Output("Audio")] public float Audio;
        [Output("Length")] public float Length;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var clip = SoundClipCache.Get(Clip);
            if (Clip == null)
                builder.Warnings.Add($"\"{Name}\" has no audio file.");
            float length = clip?.Length ?? 0f;
            if (!Loop)
                builder.NoteSourceLength(length);
            var processor = new Processor { Time = builder.TimeInput(this, "Time"), Clip = clip, Loop = Loop };
            processor.Outputs["Length"] = builder.Constant(length);
            return processor;
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] Time;
            public SoundClipCache.Entry Clip;
            public bool Loop;

            public override void Process(SoundContext context, int count)
                => SampleReader.Read(Clip?.Samples, Clip?.Frequency ?? 0, Time, Output("Audio"), count, Loop);
        }
    }

    [SchematicDoc(@"{""name"": ""Generator"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Sources"", ""description"": ""A retro sound made from settings (sfxr style): pick a preset on the node (Pickup, Laser, Explosion, Power Up, Hit, Jump, Blip, Random), then Mutate or tweak its settings. Read at its Time input like a Sound Wave, so the same time math works on it. Length is how long the sound is."", ""inputs"": [{""name"": ""Time"", ""type"": ""float""}], ""outputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Length"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Generator", Path = "Sources"), Tags(SoundNode.Tag)]
    public class SoundGeneratorNode : SoundNode
    {
        [Input("Time", Editable = false)] public float Time;
        [Editable] public bool Loop;
        public SfxrSettings Settings = SfxrSettings.FromPreset(SfxrSettings.Preset.PickupCoin, 1);
        [Output("Audio")] public float Audio;
        [Output("Length")] public float Length;

        [NonSerialized] private float[] _rendered;
        [NonSerialized] private int _renderedSignature;

        /// <summary>The sound, made again when the settings change (main thread).</summary>
        public float[] Rendered
        {
            get
            {
                Settings ??= new SfxrSettings();
                int signature = Settings.Signature();
                if (_rendered == null || signature != _renderedSignature)
                {
                    _rendered = SfxrSynth.Render(Settings);
                    _renderedSignature = signature;
                }
                return _rendered;
            }
        }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var samples = Rendered;
            float length = samples.Length / (float)SfxrSynth.SampleRate;
            if (!Loop)
                builder.NoteSourceLength(length);
            var processor = new Processor { Time = builder.TimeInput(this, "Time"), Samples = samples, Loop = Loop };
            processor.Outputs["Length"] = builder.Constant(length);
            return processor;
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] Time, Samples;
            public bool Loop;

            public override void Process(SoundContext context, int count)
                => SampleReader.Read(Samples, SfxrSynth.SampleRate, Time, Output("Audio"), count, Loop);
        }
    }

    [SchematicDoc(@"{""name"": ""Volume"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Audio times Volume (0 silent, 1 as it is, above 1 louder)."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Volume"", ""type"": ""float""}], ""outputs"": [{""name"": ""Audio"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Volume", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundVolumeNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Volume", Editable = true)] public float Volume = 1f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { In = builder.Input(this, "Audio", 0f), Volume = builder.Input(this, "Volume", Volume) };

        private sealed class Processor : SoundProcessor
        {
            public float[] In, Volume;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                for (int i = 0; i < count; i++)
                    o[i] = In[i] * Volume[i];
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Filter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Low Pass keeps what's below Cutoff (Hz): lower sounds muffled, far away or underwater. High Pass keeps what's above: lower sounds thin, like a radio or phone. Band Pass keeps what's around Cutoff. Resonance boosts right at the cutoff (0.7 is flat; higher whistles)."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Cutoff"", ""type"": ""float""}, {""name"": ""Resonance"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Filter", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundFilterNode : SoundNode
    {
        public enum Kind { LowPass, HighPass, BandPass }

        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Cutoff", Editable = true)] public float Cutoff = 1200f;
        [Input("Resonance", Editable = true)] public float Resonance = 0.707f;
        [Editable] public Kind Type = Kind.LowPass;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            In = builder.Input(this, "Audio", 0f),
            Cutoff = builder.Input(this, "Cutoff", Cutoff),
            Resonance = builder.Input(this, "Resonance", Resonance),
            Type = Type,
        };

        // A biquad (RBJ cookbook), its coefficients worked out again only when cutoff or resonance move.
        private sealed class Processor : SoundProcessor
        {
            public float[] In, Cutoff, Resonance;
            public Kind Type;
            private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;
            private float _lastCutoff = -1f, _lastQ = -1f;

            public override void Reset(SoundContext context)
            {
                _x1 = _x2 = _y1 = _y2 = 0f;
                _lastCutoff = _lastQ = -1f;
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                float nyquist = context.SampleRate * 0.49f;
                for (int i = 0; i < count; i++)
                {
                    float cutoff = Mathf.Clamp(Cutoff[i], 10f, nyquist);
                    float q = Mathf.Max(0.05f, Resonance[i]);
                    if (Mathf.Abs(cutoff - _lastCutoff) > 0.5f || q != _lastQ)
                        Coefficients(cutoff, q, context.SampleRate);

                    float x = In[i];
                    float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                    if (float.IsNaN(y) || float.IsInfinity(y))
                        y = _x1 = _x2 = _y1 = _y2 = 0f;
                    _x2 = _x1; _x1 = x;
                    _y2 = _y1; _y1 = y;
                    o[i] = y;
                }
            }

            private void Coefficients(float cutoff, float q, int rate)
            {
                _lastCutoff = cutoff;
                _lastQ = q;
                float w = 2f * Mathf.PI * cutoff / rate;
                float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                float b0, b1, b2;
                switch (Type)
                {
                    case Kind.HighPass: b0 = (1f + cos) / 2f; b1 = -(1f + cos); b2 = b0; break;
                    case Kind.BandPass: b0 = alpha; b1 = 0f; b2 = -alpha; break;
                    default: b0 = (1f - cos) / 2f; b1 = 1f - cos; b2 = b0; break;
                }
                _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
                _a1 = -2f * cos / a0; _a2 = (1f - alpha) / a0;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Delay"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""An echo: the audio again after Delay seconds (up to 2; a typed-in Delay only keeps as much memory as it needs), each echo Feedback times as loud as the last (0 one echo, near 1 many). Mix is how much echo is heard (0 none, 1 only echo)."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Delay"", ""type"": ""float""}, {""name"": ""Feedback"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Delay", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundDelayNode : SoundNode
    {
        public const float MaxDelay = 2f;

        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Delay", Editable = true)] public float Delay = 0.25f;
        [Input("Feedback", Editable = true)] public float Feedback = 0.4f;
        [Input("Mix", Editable = true)] public float Mix = 0.35f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            In = builder.Input(this, "Audio", 0f),
            Delay = builder.Input(this, "Delay", Delay),
            Feedback = builder.Input(this, "Feedback", Feedback),
            Mix = builder.Input(this, "Mix", Mix),
            // Only as long as it needs: the typed-in delay, or the most a connected (changing) one can be.
            Line = new float[(int)(Mathf.Clamp(builder.IsConnected(this, "Delay") ? MaxDelay : Delay, 0.001f, MaxDelay) * builder.SampleRate) + 4],
        };

        private sealed class Processor : SoundProcessor
        {
            public float[] In, Delay, Feedback, Mix, Line;
            private int _write;

            public override void Reset(SoundContext context)
            {
                Array.Clear(Line, 0, Line.Length);
                _write = 0;
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                int length = Line.Length;
                for (int i = 0; i < count; i++)
                {
                    float delaySamples = Mathf.Clamp(Delay[i] * context.SampleRate, 1f, length - 2);
                    float readPos = _write - delaySamples;
                    if (readPos < 0f) readPos += length;
                    int a = (int)readPos;
                    float f = readPos - a;
                    float echo = Line[a] + (Line[(a + 1) % length] - Line[a]) * f;
                    float x = In[i];
                    Line[_write] = x + echo * Mathf.Clamp(Feedback[i], 0f, 0.98f);
                    _write = (_write + 1) % length;
                    float mix = Mathf.Clamp01(Mix[i]);
                    o[i] = x * (1f - mix) + echo * mix;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Distortion"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Overdrives the audio: Drive pushes it into soft clipping (1 clean, 10+ crunchy). Mix blends it with the clean sound."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Drive"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Distortion", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundDistortionNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Drive", Editable = true)] public float Drive = 4f;
        [Input("Mix", Editable = true)] public float Mix = 1f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            In = builder.Input(this, "Audio", 0f),
            Drive = builder.Input(this, "Drive", Drive),
            Mix = builder.Input(this, "Mix", Mix),
        };

        private sealed class Processor : SoundProcessor
        {
            public float[] In, Drive, Mix;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                for (int i = 0; i < count; i++)
                {
                    float drive = Mathf.Max(0.01f, Drive[i]);
                    float x = In[i];
                    // tanh soft clipping, scaled so a full-scale input stays near full scale.
                    float wet = (float)(Math.Tanh(x * drive) / Math.Tanh(drive));
                    float mix = Mathf.Clamp01(Mix[i]);
                    o[i] = x * (1f - mix) + wet * mix;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Bitcrush"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Lo-fi: Bits (16 clean, 4 harsh) cuts how finely the volume is stored, and Downsample holds each sample this many times (1 off, 8+ gritty), for old console and radio sounds."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Bits"", ""type"": ""float""}, {""name"": ""Downsample"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Bitcrush", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundBitcrushNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Bits", Editable = true)] public float Bits = 6f;
        [Input("Downsample", Editable = true)] public float Downsample = 4f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            In = builder.Input(this, "Audio", 0f),
            Bits = builder.Input(this, "Bits", Bits),
            Downsample = builder.Input(this, "Downsample", Downsample),
        };

        private sealed class Processor : SoundProcessor
        {
            public float[] In, Bits, Downsample;
            private float _held, _counter;

            public override void Reset(SoundContext context) => _held = _counter = 0f;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                for (int i = 0; i < count; i++)
                {
                    _counter += 1f;
                    if (_counter >= Mathf.Max(1f, Downsample[i]))
                    {
                        _counter = 0f;
                        float steps = Mathf.Pow(2f, Mathf.Clamp(Bits[i], 1f, 24f) - 1f);
                        _held = Mathf.Round(In[i] * steps) / steps;
                    }
                    o[i] = _held;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Reverb"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Room echo: Room Size (0 small, 1 a cathedral), Damping (how soon the high end dies away: 0 bright, 1 dull), Mix (0 dry, 1 only the room). Mode Shared sends to the scene's reverb (Reverb Zones) instead of running its own: far cheaper for sounds played often or many at once (Room Size and Damping then come from the zone)."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Room Size"", ""type"": ""float""}, {""name"": ""Damping"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Reverb", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundReverbNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Room Size", Editable = true)] public float RoomSize = 0.6f;
        [Input("Damping", Editable = true)] public float Damping = 0.4f;
        [Input("Mix", Editable = true)] public float Mix = 0.3f;
        /// <summary>
        /// Own: this voice runs its own reverb (Room Size and Damping apply). Shared: it sends to the scene's reverb
        /// (Unity's Reverb Zones), which runs once for every sound, however many play: much cheaper for sounds played
        /// often or many at once. Previews (outside Play Mode) use Own either way, so it can be heard.
        /// </summary>
        [Editable] public ReverbMode Mode = ReverbMode.Own;
        [Output("Out")] public float Out;

        public enum ReverbMode { Own, Shared }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            if (Mode == ReverbMode.Shared && Application.isPlaying)
            {
                builder.Context.UsesSharedReverb = true;
                SoundCueReverbZone.Ensure();
                return new SendProcessor { In = builder.Input(this, "Audio", 0f), Mix = builder.Input(this, "Mix", Mix) };
            }
            return new Processor(builder.SampleRate)
            {
                In = builder.Input(this, "Audio", 0f),
                RoomSize = builder.Input(this, "Room Size", RoomSize),
                Damping = builder.Input(this, "Damping", Damping),
                Mix = builder.Input(this, "Mix", Mix),
            };
        }

        // Shared: the audio passes through dry, and Mix becomes how much the player sends to the scene's reverb.
        private sealed class SendProcessor : SoundProcessor
        {
            public float[] In, Mix;

            public override void Process(SoundContext context, int count)
            {
                Array.Copy(In, Output("Out"), count);
                // Reverb Zone Mix goes 0 to 1.1; a Mix of 1 sends fully.
                context.ReverbSend = count > 0 ? Mathf.Clamp(Mix[count - 1], 0f, 1.1f) : 0f;
            }
        }

        // Freeverb's mono path: eight damped feedback combs in parallel, then four allpasses in series.
        private sealed class Processor : SoundProcessor
        {
            private static readonly int[] CombTunings = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
            private static readonly int[] AllpassTunings = { 556, 441, 341, 225 };

            public float[] In, RoomSize, Damping, Mix;
            private readonly float[][] _combs, _allpasses;
            private readonly int[] _combIndex, _allpassIndex;
            private readonly float[] _combStore;

            public override void Reset(SoundContext context)
            {
                foreach (var line in _combs) Array.Clear(line, 0, line.Length);
                foreach (var line in _allpasses) Array.Clear(line, 0, line.Length);
                Array.Clear(_combIndex, 0, _combIndex.Length);
                Array.Clear(_allpassIndex, 0, _allpassIndex.Length);
                Array.Clear(_combStore, 0, _combStore.Length);
            }

            public Processor(int sampleRate)
            {
                float scale = sampleRate / 44100f;
                _combs = new float[CombTunings.Length][];
                for (int i = 0; i < _combs.Length; i++)
                    _combs[i] = new float[Mathf.Max(1, (int)(CombTunings[i] * scale))];
                _allpasses = new float[AllpassTunings.Length][];
                for (int i = 0; i < _allpasses.Length; i++)
                    _allpasses[i] = new float[Mathf.Max(1, (int)(AllpassTunings[i] * scale))];
                _combIndex = new int[_combs.Length];
                _allpassIndex = new int[_allpasses.Length];
                _combStore = new float[_combs.Length];
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                for (int i = 0; i < count; i++)
                {
                    float feedback = 0.7f + Mathf.Clamp01(RoomSize[i]) * 0.28f;
                    float damp = Mathf.Clamp01(Damping[i]) * 0.4f;
                    float x = In[i];
                    float input = x * 0.015f;
                    float wet = 0f;
                    for (int c = 0; c < _combs.Length; c++)
                    {
                        var line = _combs[c];
                        int index = _combIndex[c];
                        float output = line[index];
                        _combStore[c] = output * (1f - damp) + _combStore[c] * damp;
                        line[index] = input + _combStore[c] * feedback;
                        _combIndex[c] = (index + 1) % line.Length;
                        wet += output;
                    }
                    for (int a = 0; a < _allpasses.Length; a++)
                    {
                        var line = _allpasses[a];
                        int index = _allpassIndex[a];
                        float buffered = line[index];
                        float output = -wet + buffered;
                        line[index] = wet + buffered * 0.5f;
                        _allpassIndex[a] = (index + 1) % line.Length;
                        wet = output;
                    }
                    float mix = Mathf.Clamp01(Mix[i]);
                    o[i] = x * (1f - mix) + wet * 3f * mix;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Crossfade"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Blends between A and B: Blend 0 is only A, 1 only B, keeping the loudness even in between. Drive Blend with a parameter (calm to combat music) or Distance (near to far versions of a sound)."", ""inputs"": [{""name"": ""A"", ""type"": ""audio""}, {""name"": ""B"", ""type"": ""audio""}, {""name"": ""Blend"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Crossfade", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundCrossfadeNode : SoundNode
    {
        [Input("A", Editable = false)] public float A;
        [Input("B", Editable = false)] public float B;
        [Input("Blend", Editable = true)] public float Blend = 0.5f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            A = builder.Input(this, "A", 0f),
            B = builder.Input(this, "B", 0f),
            Blend = builder.Input(this, "Blend", Blend),
        };

        private sealed class Processor : SoundProcessor
        {
            public float[] A, B, Blend;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                for (int i = 0; i < count; i++)
                {
                    // Equal power: cos/sin, so the middle isn't quieter than either end.
                    float t = Mathf.Clamp01(Blend[i]) * Mathf.PI * 0.5f;
                    o[i] = A[i] * Mathf.Cos(t) + B[i] * Mathf.Sin(t);
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Mixer"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Adds sounds together. Add or remove inputs with + and − on the node. Unconnected inputs are silent; use Volume nodes before it to balance them."", ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Mixer", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundMixerNode : SoundNode
    {
        public const int MinInputs = 2, MaxInputs = 16;

        [Output("Out")] public float Out;

        /// <summary>The name of an input, by its position (0 is the first).</summary>
        public static string InputName(int index) => "In " + (index + 1);

        public int InputCount
        {
            get
            {
                int count = 0;
                while (count < MaxInputs && GetPort(InputName(count)) != null)
                    count++;
                return count;
            }
        }

        public override void OnAddedToGraph()
        {
            base.OnAddedToGraph();
            EnsureInputs();
        }

        public override void OnValidate()
        {
            EnsureInputs();
            base.OnValidate();
        }

        public void AddInput()
        {
            int count = InputCount;
            if (count >= MaxInputs)
                return;
            AddPort(new Port { Name = InputName(count), Type = typeof(float), Direction = PortDirection.Input, Capacity = PortCapacity.Single });
            IsDirty = true;
        }

        public void RemoveInput()
        {
            int count = InputCount;
            if (count <= MinInputs)
                return;
            var port = GetPort(InputName(count - 1));
            if (port != null)
                RemovePort(port);
            IsDirty = true;
        }

        private void EnsureInputs()
        {
            while (InputCount < MinInputs)
                AddInput();
        }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var inputs = new List<float[]>();
            for (int i = 0; i < InputCount; i++)
                if (builder.IsConnected(this, InputName(i)))
                    inputs.Add(builder.Input(this, InputName(i), 0f));
            return new Processor { Inputs = inputs.ToArray() };
        }

        private sealed class Processor : SoundProcessor
        {
            public float[][] Inputs;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                Array.Clear(o, 0, count);
                foreach (var input in Inputs)
                    for (int i = 0; i < count; i++)
                        o[i] += input[i];
            }
        }
    }
}
