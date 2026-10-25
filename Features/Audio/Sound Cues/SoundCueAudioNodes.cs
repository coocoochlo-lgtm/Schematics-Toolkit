// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

    [SchematicDoc(@"{""name"": ""Sound Cue"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Sources"", ""description"": ""Another Sound Cue, played inside this one: after Delay seconds, at Volume, once or looping. Build sounds out of sounds: a footstep cue reused in a run cycle, a layered explosion made of smaller cues. The inner cue's Parameters appear as pins: wire this cue's own values in (a Parameter, a Curve...) and they drive it as it plays. The inner cue hears the same distance to the listener (and its Occlusion, Surface and Room controls still work). Length is how long the inner cue is."", ""inputs"": [{""name"": ""Volume"", ""type"": ""float""}], ""outputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Length"", ""type"": ""float""}], ""notes"": ""Drop a Sound Cue asset into a Sound Cue graph to make one. A cue that ends up inside itself plays silence (with a warning).""}")]
    [Serializable]
    [Node(Name = "Sound Cue", Path = "Sources"), Tags(SoundNode.Tag)]
    [AssetNode(typeof(SoundCueGraph), Title = "Sound Cue: {0}", Field = nameof(Cue))]
    public class SoundCueSourceNode : SoundNode, IDynamicPorts
    {
        [Editable] public SoundCueGraph Cue;
        [Input("Volume", Editable = true)] public float Volume = 1f;
        [Editable, Tooltip("Seconds before it starts.")] public float Delay;
        [Editable, Tooltip("Start it again each time it ends.")] public bool Loop;
        [Output("Audio")] public float Audio;
        [Output("Length")] public float Length;

        private static readonly string[] FixedInputs = { "Volume" };

        /// <summary>One pin per Parameter of the inner cue: what's wired in drives it as it plays (unwired: its default).</summary>
        public bool SyncPorts()
        {
            var parameters = Cue != null ? Cue.ParameterList : new List<(string name, float value)>();
            return DynamicPorts.SyncInputs(this, parameters.Select(p => (PlaySoundCueNode.ParameterPort(p.name, FixedInputs), typeof(float))),
                port => port.Type == typeof(float) && Array.IndexOf(FixedInputs, port.Name) < 0);
        }

        public override void OnValidate()
        {
            base.OnValidate();
            if (SyncPorts())
                DynamicPorts.Notify(this);
        }

        // The cues being built right now, outermost first: a cue inside itself (A in B in A) is caught here.
        [ThreadStatic] private static HashSet<SoundCueGraph> _building;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var silent = new StaticProcessor();
            silent.Outputs["Audio"] = builder.Constant(0f);
            silent.Outputs["Length"] = builder.Constant(0f);
            if (Cue == null)
            {
                builder.Warnings.Add($"\"{Name}\" has no Sound Cue.");
                return silent;
            }
            var outer = Graph as SoundCueGraph;
            _building ??= new HashSet<SoundCueGraph>();
            if (Cue == outer || _building.Contains(Cue))
            {
                builder.Warnings.Add($"\"{Name}\" plays \"{Cue.name}\", which is (or contains) this cue: it would play itself forever, so it's silent.");
                return silent;
            }

            SoundCueVoice inner;
            bool added = outer != null && _building.Add(outer);
            try { inner = SoundCueVoice.Create(Cue, builder.SampleRate, builder.Context.Random.Next()); }
            finally { if (added) _building.Remove(outer); }
            if (inner == null)
                return silent;

            foreach (var warning in inner.Warnings)
                builder.Warnings.Add($"In \"{Cue.name}\": {warning}");
            // The inner cue's world controls (Occlusion, Surface, Room) are answered by this cue's player.
            builder.Context.Probes.AddRange(inner.Context.Probes);

            float length = inner.LengthSamples > 0 ? inner.LengthSamples / (float)builder.SampleRate : 0f;
            float delay = Mathf.Max(0f, Delay);
            if (!Loop && length > 0f)
                builder.NoteSourceLength(delay + length);
            // Parameters wired in: forwarded into the inner cue as it plays.
            var forwarded = new List<(string name, float[] values)>();
            foreach (var (name, value) in Cue.ParameterList)
            {
                string port = PlaySoundCueNode.ParameterPort(name, FixedInputs);
                if (GetPort(port) != null && builder.IsConnected(this, port))
                    forwarded.Add((name, builder.Input(this, port, value)));
            }
            var processor = new Processor
            {
                Inner = inner,
                Volume = builder.Input(this, "Volume", Volume),
                StartSample = (long)(delay * builder.SampleRate),
                Loop = Loop,
                Forwarded = forwarded.ToArray(),
            };
            processor.Outputs["Length"] = builder.Constant(length);
            return processor;
        }

        private sealed class Processor : SoundProcessor
        {
            public SoundCueVoice Inner;
            public float[] Volume;
            public long StartSample;
            public bool Loop;
            public (string name, float[] values)[] Forwarded;

            public override void Reset(SoundContext context) => Inner.Restart(context.Random.Next());

            public override void Process(SoundContext context, int count)
            {
                var output = Output("Audio");
                // Before the delay's up: silence.
                int i = (int)Math.Max(0L, Math.Min(count, StartSample - context.SamplePosition));
                Array.Clear(output, 0, i);
                Inner.Context.Distance = context.Distance;
                // (A block at a time: the inner cue glides its own Parameters where it needs to.)
                foreach (var (name, values) in Forwarded)
                    Inner.SetParameter(name, values[count > 0 ? count - 1 : 0]);
                while (i < count)
                {
                    long length = Inner.LengthSamples;
                    bool ended = length > 0 && Inner.Context.SamplePosition >= length;
                    if (ended)
                    {
                        if (!Loop)
                        {
                            Array.Clear(output, i, count - i);
                            break;
                        }
                        Inner.Restart(context.Random.Next());
                    }
                    int n = count - i;
                    if (length > 0)
                        n = (int)Math.Min(n, length - Inner.Context.SamplePosition);
                    Inner.Render(output, i, n);
                    i += n;
                }
                for (int s = 0; s < count; s++)
                    output[s] *= Volume[s];
            }
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

        [NonSerialized] private float[] _rendered, _looped;
        [NonSerialized] private int _renderedSignature, _loopedSignature;

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

        /// <summary>Longest crossfade at a looping Generator's seam, in seconds.</summary>
        public const float LoopCrossfade = 0.05f;

        /// <summary>
        /// The sound made to loop without a click (main thread): its end crossfades into what came just before its
        /// start, so the last sample flows into the first rather than jumping (a wave is rarely at the same point of its
        /// cycle at both ends). A little shorter than <see cref="Rendered"/>.
        /// </summary>
        public float[] Looped
        {
            get
            {
                var rendered = Rendered;
                if (_looped == null || _loopedSignature != _renderedSignature)
                {
                    _looped = Seamless(rendered, Mathf.Min((int)(LoopCrossfade * SfxrSynth.SampleRate), rendered.Length / 3));
                    _loopedSignature = _renderedSignature;
                }
                return _looped;
            }
        }

        // Drops the first `fade` samples and blends them into the end: the loop is samples [fade, length), whose last
        // `fade` samples turn smoothly into samples [0, fade) -- which led straight into sample `fade`, its start.
        private static float[] Seamless(float[] samples, int fade)
        {
            if (fade < 2)
                return samples;
            int length = samples.Length - fade;
            var loop = new float[length];
            Array.Copy(samples, fade, loop, 0, length);
            for (int j = 0; j < fade; j++)
            {
                float t = (j + 0.5f) / fade;
                t = t * t * (3f - 2f * t);
                int i = length - fade + j;
                loop[i] = loop[i] * (1f - t) + samples[j] * t;
            }
            return loop;
        }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var samples = Loop ? Looped : Rendered;
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

    [SchematicDoc(@"{""name"": ""Delay"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""An echo: the audio again after Delay seconds (up to 2; a typed-in Delay only keeps as much memory as it needs), each echo Feedback times as loud as the last (0 one echo, near 1 many). Mix is how much echo is heard (0 none, 1 only echo). Mode Spatial echoes the place the sound is in, from the Sound Field baked there: a slapback off the nearest wall (fluttering between parallel hard walls), and, in the game, the echo walls nearby (cliffs, canyon sides, a hall's far wall: Echoes sets how much), each as late as sound takes to get there and back. Mix then scales the place's amount. Outside every Sound Field it's open air (no slapback); in the preview with Space set to None it uses its own Delay and Feedback. In Spatial mode the sound itself is always heard whole, with the echoes added on top; Source Blend sets how far they duck under it while it plays (0: all the way, like sidechain compression; 1: never). Transition is how many seconds it takes to change over when the sound moves somewhere new (into or out of a room, past other walls): 0 at once, about 0.5 smooth. Mode Trail leaves its own echo (Delay and Feedback) repeating where the sound has been, a new spot every Trail Spacing meters, so the echoes trail behind a moving sound; a Spatial Reverb or Delay anywhere before or after it in the cue treats each spot as a sound where it is: rings the room it's in and echoes it off the walls from there."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Delay"", ""type"": ""float""}, {""name"": ""Feedback"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
    [Serializable]
    [Node(Name = "Delay", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundDelayNode : SoundNode
    {
        public const float MaxDelay = 2f;

        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Delay", Editable = true)] public float Delay = 0.25f;
        [Input("Feedback", Editable = true)] public float Feedback = 0.4f;
        [Input("Mix", Editable = true)] public float Mix = 0.35f;
        /// <summary>
        /// Own: Delay and Feedback as set. Spatial: the echo of the place the sound is in -- a slapback off its nearest
        /// wall (Delay is the trip there and back; Feedback how reflective and closed in it is: parallel hard walls
        /// flutter), and the echo walls it can be heard by, each as late as sound takes to get there and to the
        /// listener. Mix scales the place's amount. Outside every Sound Field it's open air (no slapback); in the preview with Space
        /// set to None, Own's settings apply. Trail: its own echo (Delay and Feedback), left repeating where the sound
        /// has been: a new spot every Trail Spacing meters, so the echoes trail behind a moving sound (in the game and the
        /// preview). A Spatial effect linked to a Trail (before or after it) echoes each spot off the walls from where
        /// it is.
        /// </summary>
        [Editable] public DelayMode Mode = DelayMode.Own;
        /// <summary>Spatial: how much echo walls (cliffs, canyon sides, buildings, a hall's far wall) throw the sound back, 0 to 1.</summary>
        [Editable] public float Echoes = 1f;
        [Editable, Tooltip("Spatial: how much the place's slapback and echoes may drown out the sound itself, which is always heard in full from where it is. 0: they duck under the sound while it plays (like sidechain compression: the louder it is, the further), swelling back in its gaps and after it; 1: never ducked.")]
        public float SourceBlend = 0.5f;
        [Editable, Tooltip("Spatial: seconds the echo (its slapback spot and echo walls) takes to change over when the sound moves somewhere new (into or out of a room, past other walls): the old place fades out while the new one fades in. 0: at once (dramatic: a street into a tunnel); about 0.5 to 1: smooth (a small room you step in and out of).")]
        public float Transition = 0.5f;
        [Editable, Tooltip("Trail: meters the sound moves before it leaves a new echo spot behind (smaller: a smoother trail, more spots).")]
        public float TrailSpacing = 1f;
        [Editable, Tooltip("Trail: the most spots repeating at once for one playing sound; past this its oldest is taken for the next.")]
        public int MaxTrailSpots = 16;
        [Output("Out")] public float Out;

        public enum DelayMode { Own, Spatial, Trail }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            if (Mode == DelayMode.Spatial)
            {
                // What it hears first (so Trails before it are built, and their probes run before this one's).
                var audio = builder.Input(this, "Audio", 0f);
                var probe = new PlaceProbe { Node = this, Targets = new float[3], EchoDelay = SpatialProbe.EchoDelayOf(Graph) };
                TrailProbe.Link(builder.Upstream(this, "Audio"), null, probe);
                probe.Fallback();
                builder.Context.Probes.Add(probe);
                var place = new ProbedProcessor { Ports = new[] { "Delay", "Feedback", "Wet" }, Targets = probe.Targets, Smoothing = 0.5f };
                builder.AddHelper(place);
                return new Processor(builder.SampleRate)
                {
                    In = audio,
                    Delay = place.Output("Delay"),
                    Feedback = place.Output("Feedback"),
                    WetScale = place.Output("Wet"),
                    Mix = builder.Input(this, "Mix", Mix),
                    Line = new float[(int)(MaxDelay * builder.SampleRate) + 4],
                    Place = probe,
                    SourceBlend = Mathf.Clamp01(SourceBlend),
                    Transition = Mathf.Max(0f, Transition),
                };
            }
            TrailProbe trail = null;
            if (Mode == DelayMode.Trail)
            {
                trail = new TrailProbe { Kind = SpaceBus.BusKind.Slap, Spacing = Mathf.Max(0.1f, TrailSpacing), MaxSpots = Mathf.Max(1, MaxTrailSpots), A = Mathf.Clamp(Delay, 0.001f, MaxDelay), B = Mathf.Clamp(Feedback, 0f, 0.95f) };
            }
            var input = builder.Input(this, "Audio", 0f);
            if (trail != null)
            {
                // Spatial effects before it hear its spots too.
                TrailProbe.Link(builder.Upstream(this, "Audio"), trail, null);
                builder.Context.Probes.Add(trail);
            }
            return new Processor(builder.SampleRate)
            {
                In = input,
                Delay = builder.Input(this, "Delay", Delay),
                Feedback = builder.Input(this, "Feedback", Feedback),
                Mix = builder.Input(this, "Mix", Mix),
                // Only as long as it needs: the typed-in delay, or the most a connected (changing) one can be.
                Line = new float[(int)(Mathf.Clamp(builder.IsConnected(this, "Delay") ? MaxDelay : Delay, 0.001f, MaxDelay) * builder.SampleRate) + 4],
                Trail = trail,
            };
        }

        // Spatial: the place's slapback (delay, feedback, how much) and the echo walls it can be heard by.
        private sealed class PlaceProbe : SpatialProbe
        {
            public SoundDelayNode Node;

            protected override bool UseRooms => false;
            protected override float EchoAmount => Node.Echoes;

            protected override void Shape(in SoundAcoustics place, bool elsewhere)
            {
                Set(0, Mathf.Min(MaxDelay, place.SlapbackDelay * EchoDelay));
                Set(1, place.SlapbackFeedback);
                Set(2, elsewhere ? 0f : place.SlapbackWet);
            }

            // A slapback left where the sound is, coming back off the nearest wall there.
            protected override SpaceBus Tail(TailPool pool, Vector3 position, in SoundAcoustics place, out float amount)
            {
                amount = place.SlapbackWet;
                return amount > 0.001f ? pool.Slap(position, Mathf.Min(MaxDelay, place.SlapbackDelay * EchoDelay), place.SlapbackFeedback, place.Damping) : null;
            }

            public override void Fallback()
            {
                Set(0, Mathf.Clamp(Node.Delay, 0.001f, MaxDelay));
                Set(1, Mathf.Clamp(Node.Feedback, 0f, 0.98f));
                Set(2, 1f);
            }
        }

        private sealed class Processor : SoundProcessor, ITrailed, ISpatial, ISpatialSends
        {
            public float[] In, Delay, Feedback, Mix, Line;
            // Spatial: the place's amount (which Mix scales), and the sends to echo walls; the sound itself stays whole,
            // and the echoes duck under it as far as SourceBlend lets them.
            public float[] WetScale;
            public PlaceProbe Place;
            public float SourceBlend = 1f;
            public float Transition;
            private readonly SourceDucker _ducker;
            // Trail: the echo repeats at the trail's spots instead (the sound only sends to them), when it can leave them.
            public TrailProbe Trail;
            TrailProbe ITrailed.Trail => Trail;
            SpatialProbe ISpatial.Place => Place;
            SpatialSender ISpatialSends.Sender => _sender;
            private readonly SpatialSender _sender;
            private int _write;

            public Processor(int sampleRate)
            {
                _sender = new SpatialSender(sampleRate);
                _ducker = new SourceDucker(sampleRate);
            }

            public override void Reset(SoundContext context)
            {
                Array.Clear(Line, 0, Line.Length);
                _write = 0;
                _sender.Reset();
                _ducker.Reset();
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                _sender.Transition = Transition;
                if (Trail != null && count > 0)
                {
                    Volatile.Write(ref Trail.A, Mathf.Clamp(Delay[count - 1], 0.001f, MaxDelay));
                    Volatile.Write(ref Trail.B, Mathf.Clamp(Feedback[count - 1], 0f, 0.95f));
                    var sends = Volatile.Read(ref Trail.Sends);
                    if (sends != null && context.HeardStart >= 0)
                    {
                        for (int i = 0; i < count; i++)
                            o[i] = In[i] * (1f - Mathf.Clamp01(Mix[i]));
                        _sender.Send(sends, context, In, count, Mathf.Clamp01(Mix[count - 1]));
                        return;
                    }
                }
                int length = Line.Length;
                _ducker.Begin();
                for (int i = 0; i < count; i++)
                {
                    // In doubles, with the index wrapped explicitly: in a long line a float can't tell "just before the
                    // end" from the end itself, and reading there ran off the line.
                    double delaySamples = Delay[i] * (double)context.SampleRate;
                    if (double.IsNaN(delaySamples)) delaySamples = 1.0;
                    delaySamples = Math.Max(1.0, Math.Min(length - 2, delaySamples));
                    double readPos = _write - delaySamples;
                    if (readPos < 0.0) readPos += length;
                    int a = (int)readPos;
                    float f = (float)(readPos - a);
                    if (a >= length) a -= length;
                    int next = a + 1 == length ? 0 : a + 1;
                    float echo = Line[a] + (Line[next] - Line[a]) * f;
                    float x = In[i];
                    Line[_write] = x + echo * Mathf.Clamp(Feedback[i], 0f, 0.98f);
                    _write = (_write + 1) % length;
                    float mix = Mathf.Clamp01(WetScale != null ? Mix[i] * WetScale[i] : Mix[i]);
                    // Spatial: the sound whole, its echo on top (ducked under it); otherwise a blend.
                    o[i] = Place != null ? x + echo * mix * _ducker.Next(x, SourceBlend) : x * (1f - mix) + echo * mix;
                }
                if (Place != null && count > 0)
                    _sender.Send(Place, context, In, count, Mathf.Clamp01(Mix[count - 1]), _ducker.Lowest);
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

    [SchematicDoc(@"{""name"": ""Reverb"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Room echo: Room Size (0 small, 1 a cathedral), Damping (how soon the high end dies away: 0 bright, 1 dull), Mix (0 dry, 1 only the room). Mode Shared sends to the scene's reverb (Reverb Zones) instead of running its own: far cheaper for sounds played often or many at once (Room Size and Damping then come from the zone). Mode Spatial rings like the place the sound is in, from the Sound Field baked there: a small tiled bathroom, a dead carpeted office, a huge stone hall, open air (Mix then scales the place's reverb: 1 is as much as the place makes; outside every Sound Field it's open air, nearly dry; in the preview with Space set to None the place is this node's Room Size and Damping). In the game, Spatial sounds in a room make the room itself ring (heard from the room, carrying on after the sound moves away or stops), and in the open they're thrown back by the echo walls nearby (cliffs, canyon sides, buildings: Echoes sets how much), as late as sound takes to get there and back. In Spatial mode the sound itself is always heard whole, with the reverb added on top; Source Blend sets how far the reverb (its own, the room's, a tail's, the echoes) ducks under it while it plays (0: all the way, like sidechain compression; 1: never). Transition is how many seconds it takes to change over when the sound moves somewhere new (into or out of a room, past other walls): 0 at once, about 0.5 smooth. Mode Trail leaves its own reverb (Room Size and Damping) ringing where the sound has been, a new spot every Trail Spacing meters, so the tail trails behind a moving sound (Mix 1: only the trail); a Spatial Reverb or Delay anywhere before or after it in the cue treats each spot as a sound where it is: rings the room it's in and echoes it off the walls from there."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Room Size"", ""type"": ""float""}, {""name"": ""Damping"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}]}")]
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
        /// often or many at once. Spatial: its own reverb, shaped by the <see cref="SoundField"/> where the sound is
        /// (how long, how dark, how much), following it as it moves. Trail: its own reverb (Room Size and Damping), left
        /// ringing where the sound has been: a new spot every Trail Spacing meters, so the tail trails behind a moving
        /// sound (in the game and the preview). A Spatial effect linked to a Trail (before or after it) rings the place
        /// with each spot, and echoes it off the walls from where it is.
        /// </summary>
        [Editable] public ReverbMode Mode = ReverbMode.Own;
        /// <summary>Spatial: how much echo walls (cliffs, canyon sides, buildings) throw the sound back in the open, 0 to 1.</summary>
        [Editable] public float Echoes = 1f;
        [Editable, Tooltip("Spatial: how much the place's reverb (its room's, a tail's, and echoes) may drown out the sound itself, which is always heard in full from where it is. 0: they duck under the sound while it plays (like sidechain compression: the louder it is, the further), swelling back in its gaps and after it; 1: never ducked.")]
        public float SourceBlend = 0.5f;
        [Editable, Tooltip("Spatial: seconds the reverb (its room, tail spot and echo walls) takes to change over when the sound moves somewhere new (into or out of a room, past other walls): the old place fades out while the new one fades in. 0: at once (dramatic: a street into a tunnel); about 0.5 to 1: smooth (a small room you step in and out of).")]
        public float Transition = 0.5f;
        [Editable, Tooltip("Trail: meters the sound moves before it leaves a new reverb spot behind (smaller: a smoother trail, more spots).")]
        public float TrailSpacing = 1f;
        [Editable, Tooltip("Trail: the most spots ringing at once for one playing sound; past this its oldest is taken for the next.")]
        public int MaxTrailSpots = 16;
        [Output("Out")] public float Out;

        public enum ReverbMode { Own, Shared, Spatial, Trail }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            if (Mode == ReverbMode.Shared && Application.isPlaying)
            {
                builder.Context.UsesSharedReverb = true;
                SoundCueReverbZone.Ensure();
                return new SendProcessor { In = builder.Input(this, "Audio", 0f), Mix = builder.Input(this, "Mix", Mix) };
            }
            if (Mode == ReverbMode.Spatial)
            {
                // What it hears first (so Trails before it are built, and their probes run before this one's).
                var audio = builder.Input(this, "Audio", 0f);
                // The place's reverb, asked of the Sound Field by the player (main thread) and glided between.
                var probe = new PlaceProbe { Node = this, Targets = new float[3], EchoDelay = SpatialProbe.EchoDelayOf(Graph) };
                TrailProbe.Link(builder.Upstream(this, "Audio"), null, probe);
                probe.Fallback();
                builder.Context.Probes.Add(probe);
                var place = new ProbedProcessor { Ports = new[] { "Feedback", "Damping", "Wet" }, Targets = probe.Targets, Smoothing = 0.5f };
                builder.AddHelper(place);
                return new Processor(builder.SampleRate)
                {
                    In = audio,
                    Feedback = place.Output("Feedback"),
                    Damping = place.Output("Damping"),
                    WetScale = place.Output("Wet"),
                    Mix = builder.Input(this, "Mix", Mix),
                    Place = probe,
                    SourceBlend = Mathf.Clamp01(SourceBlend),
                    Transition = Mathf.Max(0f, Transition),
                };
            }
            if (Mode == ReverbMode.Trail)
            {
                var trail = new TrailProbe { Kind = SpaceBus.BusKind.Room, Spacing = Mathf.Max(0.1f, TrailSpacing), MaxSpots = Mathf.Max(1, MaxTrailSpots), A = FeedbackFor(RoomSize), B = Mathf.Clamp01(Damping) };
                var audio = builder.Input(this, "Audio", 0f);
                // Spatial effects before it hear its spots too.
                TrailProbe.Link(builder.Upstream(this, "Audio"), trail, null);
                builder.Context.Probes.Add(trail);
                return new Processor(builder.SampleRate)
                {
                    In = audio,
                    RoomSize = builder.Input(this, "Room Size", RoomSize),
                    Damping = builder.Input(this, "Damping", Damping),
                    Mix = builder.Input(this, "Mix", Mix),
                    Trail = trail,
                };
            }
            return new Processor(builder.SampleRate)
            {
                In = builder.Input(this, "Audio", 0f),
                RoomSize = builder.Input(this, "Room Size", RoomSize),
                Damping = builder.Input(this, "Damping", Damping),
                Mix = builder.Input(this, "Mix", Mix),
            };
        }

        /// <summary>The comb feedback a Room Size makes (Own mode's mapping).</summary>
        internal static float FeedbackFor(float roomSize) => 0.7f + Mathf.Clamp01(roomSize) * 0.28f;

        // Spatial: the place's reverb where the sound is -- feedback (its length), damping, and how much is heard -- and
        // the room it rings (in a room the sound's own reverb fades: the room's carries it) or, in the open, the echo walls.
        private sealed class PlaceProbe : SpatialProbe
        {
            public SoundReverbNode Node;

            protected override bool UseRooms => true;
            protected override float EchoAmount => Node.Echoes;

            protected override void Shape(in SoundAcoustics place, bool elsewhere)
            {
                Set(0, place.Feedback);
                Set(1, place.Damping);
                Set(2, elsewhere ? 0f : place.Wet);
            }

            // Outside rooms: a reverb left where the sound is, ringing like the place.
            protected override SpaceBus Tail(TailPool pool, Vector3 position, in SoundAcoustics place, out float amount)
            {
                amount = place.Wet;
                return amount > 0.001f ? pool.Reverb(position, place.Feedback, place.Damping, place.ReverbTime) : null;
            }

            // The preview with no surroundings (Space: None): the node's own Room Size and Damping, heard a modest amount.
            public override void Fallback()
            {
                Set(0, FeedbackFor(Node.RoomSize));
                Set(1, Mathf.Clamp01(Node.Damping));
                Set(2, 0.3f);
            }
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

        // Freeverb (ReverbCore). Spatial: shaped by the place, and sending to the place's room or echo walls. Trail: the
        // reverb is made at the trail's spots instead (the sound only sends to them), when it can leave them.
        private sealed class Processor : SoundProcessor, ITrailed, ISpatial, ISpatialSends
        {
            public float[] In, RoomSize, Damping, Mix;
            // Spatial: the feedback itself (instead of Room Size), the place's amount (which Mix scales), and the sends.
            public float[] Feedback, WetScale;
            public PlaceProbe Place;
            // Spatial: the sound itself stays whole, and the reverb ducks under it as far as SourceBlend lets it.
            public float SourceBlend = 1f;
            public float Transition;
            private readonly SourceDucker _ducker;
            public TrailProbe Trail;
            TrailProbe ITrailed.Trail => Trail;
            SpatialProbe ISpatial.Place => Place;
            SpatialSender ISpatialSends.Sender => _sender;
            private readonly ReverbCore _core;
            private readonly SpatialSender _sender;

            public Processor(int sampleRate)
            {
                _core = new ReverbCore(sampleRate);
                _sender = new SpatialSender(sampleRate);
                _ducker = new SourceDucker(sampleRate);
            }

            public override void Reset(SoundContext context)
            {
                _core.Clear();
                _sender.Reset();
                _ducker.Reset();
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                _sender.Transition = Transition;
                if (Trail != null && count > 0)
                {
                    // The node's settings for the spot (the world side sets them on it).
                    Volatile.Write(ref Trail.A, FeedbackFor(RoomSize[count - 1]));
                    Volatile.Write(ref Trail.B, Mathf.Clamp01(Damping[count - 1]));
                    var sends = Volatile.Read(ref Trail.Sends);
                    if (sends != null && context.HeardStart >= 0)
                    {
                        // Only the dry part here: the reverb rings at the spot, from the part sent to it.
                        for (int i = 0; i < count; i++)
                            o[i] = In[i] * (1f - Mathf.Clamp01(Mix[i]));
                        _sender.Send(sends, context, In, count, Mathf.Clamp01(Mix[count - 1]));
                        return;
                    }
                    // No spot to leave (a pitched source, the pool's full): its own reverb, as Own.
                }
                _ducker.Begin();
                for (int i = 0; i < count; i++)
                {
                    float feedback = Feedback != null ? Mathf.Clamp(Feedback[i], 0f, 0.985f) : FeedbackFor(RoomSize[i]);
                    float damp = Mathf.Clamp01(Damping[i]) * 0.4f;
                    float x = In[i];
                    float wet = _core.Process(x * 0.015f, feedback, damp);
                    float mix = Mathf.Clamp01(WetScale != null ? Mix[i] * WetScale[i] : Mix[i]);
                    // Spatial: the sound whole, its reverb on top (ducked under it); otherwise a blend.
                    o[i] = Place != null ? x + wet * 3f * mix * _ducker.Next(x, SourceBlend) : x * (1f - mix) + wet * 3f * mix;
                }
                if (Place != null && count > 0)
                    _sender.Send(Place, context, In, count, Mathf.Clamp01(Mix[count - 1]), _ducker.Lowest);
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
