using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Arpeggiator"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""Steps through the Notes (semitones, e.g. 0 4 7 12) Rate times a second, in Pattern's order, over Octaves octaves, Base semitones up. Note Time starts again at each step, running faster for higher notes: plug it into a Sound Wave's or Generator's Time and every step plays the sound again at that note. Pitch is the step's pitch as a ratio (1 the original, 2 an octave up) for Pitch Shift, or to multiply a Time by; Semitones the same in semitones. Gate is 1 for the first Gate Length of each step and 0 after (with short fades): multiply the audio by it for separate notes. Step counts the steps."", ""inputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Rate"", ""type"": ""float""}, {""name"": ""Base"", ""type"": ""float""}, {""name"": ""Gate Length"", ""type"": ""float""}], ""outputs"": [{""name"": ""Note Time"", ""type"": ""float""}, {""name"": ""Pitch"", ""type"": ""float""}, {""name"": ""Semitones"", ""type"": ""float""}, {""name"": ""Gate"", ""type"": ""float""}, {""name"": ""Step"", ""type"": ""float""}], ""use_for"": [""retro pickup and power-up jingles"", ""chiptune arpeggios from one sound"", ""rising alarm and charge-up sounds""]}")]
    [Serializable]
    [Node(Name = "Arpeggiator", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundArpeggiatorNode : SoundNode
    {
        public enum ArpPattern { Up, Down, UpDown, DownUp, AsWritten, Random }

        [Input("Time", Editable = false)] public float Time;
        [Input("Rate", Editable = true)] public float Rate = 12f;
        [Input("Base", Editable = true)] public float Base;
        [Input("Gate Length", Editable = true)] public float GateLength = 0.8f;
        [Editable, Tooltip("The notes, in semitones from Base, separated by spaces or commas (e.g. 0 4 7 for a major chord, 0 3 7 for minor).")]
        public string Notes = "0 4 7 12";
        [Editable, Tooltip("The order the notes are played in (Up and Down sort them; As Written keeps your order).")]
        public ArpPattern Pattern = ArpPattern.Up;
        [Editable, Tooltip("How many octaves the pattern climbs before starting again.")]
        public int Octaves = 1;
        [Output("Note Time")] public float NoteTime;
        [Output("Pitch")] public float Pitch;
        [Output("Semitones")] public float Semitones;
        [Output("Gate")] public float Gate;
        [Output("Step")] public float Step;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            Time = builder.TimeInput(this, "Time"),
            Rate = builder.Input(this, "Rate", Rate),
            Base = builder.Input(this, "Base", Base),
            GateLength = builder.Input(this, "Gate Length", GateLength),
            Sequence = Sequence(ParseNotes(Notes), Pattern, Mathf.Clamp(Octaves, 1, 8)),
            Shuffle = Pattern == ArpPattern.Random,
        };

        /// <summary>The semitones in a Notes string (0 when there are none).</summary>
        public static float[] ParseNotes(string notes)
        {
            var found = new List<float>();
            foreach (var part in (notes ?? "").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                    found.Add(value);
            if (found.Count == 0)
                found.Add(0f);
            return found.ToArray();
        }

        // The notes in playing order, over the octaves (Random picks from all of them).
        private static float[] Sequence(float[] notes, ArpPattern pattern, int octaves)
        {
            var all = new List<float>();
            for (int o = 0; o < octaves; o++)
                foreach (float note in notes)
                    all.Add(note + 12f * o);
            if (pattern != ArpPattern.AsWritten && pattern != ArpPattern.Random)
                all.Sort();
            if (pattern == ArpPattern.Down || pattern == ArpPattern.DownUp)
                all.Reverse();
            if ((pattern == ArpPattern.UpDown || pattern == ArpPattern.DownUp) && all.Count > 2)
                // There and back, without playing the turning notes twice.
                for (int i = all.Count - 2; i >= 1; i--)
                    all.Add(all[i]);
            return all.ToArray();
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] Time, Rate, Base, GateLength, Sequence;
            public bool Shuffle;
            private uint _seed;

            public override void Reset(SoundContext context) => _seed = (uint)context.Random.Next();

            public override void Process(SoundContext context, int count)
            {
                var noteTime = Output("Note Time");
                var pitch = Output("Pitch");
                var semitones = Output("Semitones");
                var gate = Output("Gate");
                var step = Output("Step");
                int length = Sequence.Length;
                for (int i = 0; i < count; i++)
                {
                    float rate = Mathf.Max(0.01f, Rate[i]);
                    float t = Mathf.Max(0f, Time[i]);
                    int index = (int)Math.Floor(t * rate);
                    float local = t - index / rate;
                    int at = Shuffle ? (int)(Hash((uint)index ^ _seed) % (uint)length) : index % length;
                    float semis = Base[i] + Sequence[at];
                    float ratio = Mathf.Pow(2f, semis / 12f);
                    noteTime[i] = local * ratio;
                    pitch[i] = ratio;
                    semitones[i] = semis;
                    // On for the first Gate Length of the step, with 2 ms in and 5 ms out so it doesn't click.
                    float end = Mathf.Clamp01(GateLength[i]) / rate;
                    gate[i] = Mathf.Clamp01(local / 0.002f) * Mathf.Clamp01((end - local) / 0.005f);
                    step[i] = index;
                }
            }

            private static uint Hash(uint x)
            {
                x ^= x >> 16; x *= 0x7feb352d;
                x ^= x >> 15; x *= 0x846ca68b;
                x ^= x >> 16;
                return x;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Varispeed"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A playback Time that runs at Speed times real time (1 normal, 2 twice as fast, 0 stopped, negative backwards), built up a sample at a time, so changing Speed never makes it jump. Plug it into an Oscillator's Time (with Frequency 1, Speed is then its frequency: an engine's firing rate from its RPM), or a Sound Wave's or Generator's (smooth tape-style speed and pitch changes)."", ""inputs"": [{""name"": ""Speed"", ""type"": ""float""}], ""outputs"": [{""name"": ""Time"", ""type"": ""float""}], ""use_for"": [""engines and motors whose pitch follows a parameter"", ""tape slow-downs and speed-ups"", ""smoothly changing tones""]}")]
    [Serializable]
    [Node(Name = "Varispeed", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundVarispeedNode : SoundNode
    {
        [Input("Speed", Editable = true)] public float Speed = 1f;
        [Output("Time")] public float Time;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor { Speed = builder.Input(this, "Speed", Speed) };

        private sealed class Processor : SoundProcessor
        {
            public float[] Speed;
            private double _time;
            private bool _started;

            public override void Reset(SoundContext context) => _started = false;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Time");
                double step = 1.0 / context.SampleRate;
                if (!_started)
                {
                    // A cue started part way through starts its Varispeed there too.
                    _time = context.SamplePosition * step;
                    _started = true;
                }
                for (int i = 0; i < count; i++)
                {
                    o[i] = (float)_time;
                    float speed = Speed[i];
                    if (!float.IsNaN(speed) && !float.IsInfinity(speed))
                        _time += speed * step;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Pitch Shift"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Changes the pitch without changing the speed: Pitch is a ratio (2 an octave up, 0.5 an octave down; an Arpeggiator's Pitch plugs straight in), times Semitones more. Window (ms) is how long the slices it's made of are: short (20-40) for drums and hits, long (60-120) for tones and voices; very short or extreme shifts get grainy and robotic, on purpose if you like. Mix blends it with the original (a harmony)."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Pitch"", ""type"": ""float""}, {""name"": ""Semitones"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""higher or lower variations of one sound without it getting faster or slower"", ""harmonies (Mix 0.5)"", ""monster and robot voices"", ""shimmer: in a Reverb's trail""]}")]
    [Serializable]
    [Node(Name = "Pitch Shift", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundPitchShiftNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Pitch", Editable = true)] public float Pitch = 1f;
        [Input("Semitones", Editable = true)] public float Semitones;
        [Input("Mix", Editable = true)] public float Mix = 1f;
        [Editable, Tooltip("How long each slice is, in milliseconds (short: tighter for drums and hits; long: smoother for tones).")]
        public float Window = 60f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            int window = Mathf.Max(32, (int)(Mathf.Clamp(Window, 5f, 250f) * 0.001f * builder.SampleRate));
            return new Processor
            {
                In = builder.Input(this, "Audio", 0f),
                Pitch = builder.Input(this, "Pitch", Pitch),
                Semitones = builder.Input(this, "Semitones", Semitones),
                Mix = builder.Input(this, "Mix", Mix),
                Window = window,
                Line = new float[window + 8],
            };
        }

        // Two read heads sweep through a short delay line at the new speed, each faded in and out (a Hann window) half
        // a sweep apart, so one is always fading in as the other jumps back.
        private sealed class Processor : SoundProcessor
        {
            public float[] In, Pitch, Semitones, Mix, Line;
            public int Window;
            private int _write;
            private double _phase;

            public override void Reset(SoundContext context)
            {
                Array.Clear(Line, 0, Line.Length);
                _write = 0;
                _phase = 0.0;
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                int length = Line.Length;
                for (int i = 0; i < count; i++)
                {
                    float x = In[i];
                    Line[_write] = x;
                    double ratio = Mathf.Clamp(Pitch[i], 0.0625f, 8f) * Math.Pow(2.0, Mathf.Clamp(Semitones[i], -48f, 48f) / 12.0);
                    if (double.IsNaN(ratio)) ratio = 1.0;
                    // The delay shrinks (higher) or grows (lower) by (ratio - 1) samples a sample, wrapping round the window.
                    _phase -= (ratio - 1.0) / Window;
                    _phase -= Math.Floor(_phase);
                    float shifted = 0f;
                    for (int head = 0; head < 2; head++)
                    {
                        double phase = head == 0 ? _phase : (_phase + 0.5) % 1.0;
                        double read = _write - 2.0 - phase * Window;
                        if (read < 0.0) read += length;
                        int a = (int)read;
                        if (a >= length) a -= length;
                        float f = (float)(read - Math.Floor(read));
                        int b = a + 1 == length ? 0 : a + 1;
                        float s = (float)Math.Sin(Math.PI * phase);
                        shifted += (Line[a] + (Line[b] - Line[a]) * f) * s * s;
                    }
                    _write = _write + 1 == length ? 0 : _write + 1;
                    float mix = Mathf.Clamp01(Mix[i]);
                    o[i] = x * (1f - mix) + shifted * mix;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Freq Shift"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Moves every frequency in the sound up (or down, negative) by Shift Hz. Unlike Pitch Shift it doesn't keep the notes in tune with each other: small shifts (1-10 Hz) give a swirling, phasing movement; bigger ones a metallic, clangorous, alien sound. Mix blends it with the original."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Shift"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""sci-fi and alien sounds"", ""metallic, ringing textures"", ""slow swirls on pads and ambience""]}")]
    [Serializable]
    [Node(Name = "Freq Shift", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundFreqShiftNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Shift", Editable = true)] public float Shift = 100f;
        [Input("Mix", Editable = true)] public float Mix = 1f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            In = builder.Input(this, "Audio", 0f),
            Shift = builder.Input(this, "Shift", Shift),
            Mix = builder.Input(this, "Mix", Mix),
        };

        // Single-sideband modulation: two chains of allpass filters whose outputs are 90 degrees apart across the range
        // (a Hilbert transform, Olli Niemitalo's design), turned by a rotating phase.
        private sealed class Processor : SoundProcessor
        {
            private static readonly float[] ChainA = Squares(0.6923878f, 0.9360654322959f, 0.9882295226860f, 0.9987488452737f);
            private static readonly float[] ChainB = Squares(0.4021921162426f, 0.8561710882420f, 0.9722909545651f, 0.9952884791278f);

            public float[] In, Shift, Mix;
            // Each stage's last two inputs and outputs, per chain.
            private readonly float[] _ax1 = new float[4], _ax2 = new float[4], _ay1 = new float[4], _ay2 = new float[4];
            private readonly float[] _bx1 = new float[4], _bx2 = new float[4], _by1 = new float[4], _by2 = new float[4];
            private float _aLast;
            private double _phase;

            private static float[] Squares(params float[] a)
            {
                var squared = new float[a.Length];
                for (int i = 0; i < a.Length; i++) squared[i] = a[i] * a[i];
                return squared;
            }

            public override void Reset(SoundContext context)
            {
                Array.Clear(_ax1, 0, 4); Array.Clear(_ax2, 0, 4); Array.Clear(_ay1, 0, 4); Array.Clear(_ay2, 0, 4);
                Array.Clear(_bx1, 0, 4); Array.Clear(_bx2, 0, 4); Array.Clear(_by1, 0, 4); Array.Clear(_by2, 0, 4);
                _aLast = 0f;
                _phase = 0.0;
            }

            private static float Chain(float x, float[] c, float[] x1, float[] x2, float[] y1, float[] y2)
            {
                for (int s = 0; s < 4; s++)
                {
                    float y = c[s] * (x + y2[s]) - x2[s];
                    x2[s] = x1[s]; x1[s] = x;
                    y2[s] = y1[s]; y1[s] = y;
                    x = y;
                }
                return x;
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                double step = 2.0 * Math.PI / context.SampleRate;
                for (int i = 0; i < count; i++)
                {
                    float x = In[i];
                    // The first chain a sample later: with the second, 90 degrees apart (the other side of each frequency
                    // cancels to about 48 dB down across the range).
                    float real = _aLast;
                    _aLast = Chain(x, ChainA, _ax1, _ax2, _ay1, _ay2);
                    float imaginary = Chain(x, ChainB, _bx1, _bx2, _by1, _by2);
                    float shift = Shift[i];
                    if (float.IsNaN(shift)) shift = 0f;
                    _phase += step * Mathf.Clamp(shift, -20000f, 20000f);
                    if (_phase > Math.PI * 2.0) _phase -= Math.PI * 2.0;
                    else if (_phase < 0.0) _phase += Math.PI * 2.0;
                    float shifted = real * (float)Math.Cos(_phase) + imaginary * (float)Math.Sin(_phase);
                    float mix = Mathf.Clamp01(Mix[i]);
                    o[i] = x * (1f - mix) + shifted * mix;
                }
            }
        }
    }
}
