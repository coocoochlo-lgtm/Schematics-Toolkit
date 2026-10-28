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
        [Tooltip("How many notes a second (12 is fast and rolling, 4 a steady pulse).")]
        [Input("Rate", Editable = true)] public float Rate = 12f;
        [Tooltip("Semitones every note is raised by (12 an octave up, -12 down).")]
        [Input("Base", Editable = true)] public float Base;
        [Tooltip("How much of each step the note sounds for, 0 to 1: 0.8 leaves a short gap, 1 joins them up.")]
        [Input("Gate Length", Editable = true)] public float GateLength = 0.8f;
        [Editable, Tooltip("The notes, in semitones from Base, separated by spaces or commas (e.g. 0 4 7 for a major chord, 0 3 7 for minor).")]
        public string Notes = "0 4 7 12";
        [Editable, Tooltip("The order the notes are played in (Up and Down sort them; As Written keeps your order).")]
        public ArpPattern Pattern = ArpPattern.Up;
        [Editable, Tooltip("How many octaves the pattern climbs before starting again.")]
        public int Octaves = 1;
        [Output("Note Time")] public float NoteTime;
        [Output("Pitch")] public float Pitch;
        [Tooltip("The current note in semitones from the original pitch (Base included).")]
        [Output("Semitones")] public float Semitones;
        [Tooltip("1 while the current note sounds, 0 in the gap after it: multiply the audio by it for separate notes.")]
        [Output("Gate")] public float Gate;
        [Tooltip("Which step of the pattern it's on, counting from 0.")]
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
        [Tooltip("The pitch as a ratio: 2 an octave up, 0.5 an octave down, 1 unchanged.")]
        [Input("Pitch", Editable = true)] public float Pitch = 1f;
        [Tooltip("Semitones more on top of Pitch (12 an octave, 7 a fifth; negative goes down).")]
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
        // a sweep apart, so one is always fading in as the other jumps back. (Set Pitch uses it too.)
        internal sealed class Processor : SoundProcessor
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
        [Tooltip("How far every frequency moves (Hz): 1 to 10 swirls, bigger sounds metallic or alien; negative moves down.")]
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

    [SchematicDoc(@"{""name"": ""Set Pitch"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Puts a sound at a pitch, rather than shifting it by an amount: tell it the pitch the sound is at (Root, in Hz, or Root Note like A3; Detect Root on the node works it out from the Sound Wave feeding it) and the pitch you want (Target, in Hz, or Target Note), and it shifts by the difference. Out: the Audio at the Target pitch, at the same speed (like Pitch Shift). Time: a playback Time running at the ratio, for a Sound Wave's or Generator's Time (faster and higher together, like a tape; often best for engines). Ratio: Target / Root. Only the outputs something reads do any work."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Target"", ""type"": ""float""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}, {""name"": ""Time"", ""type"": ""float""}, {""name"": ""Ratio"", ""type"": ""float""}], ""use_for"": [""an engine at the pitch its RPM gives (RPM / 60 x cylinders / 2 Hz into Target)"", ""tuning a sample to a note"", ""melodies from one recorded note""]}")]
    [Serializable]
    [Node(Name = "Set Pitch", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundSetPitchNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Target", Editable = true)] public float Target = 440f;
        [Input("Mix", Editable = true)] public float Mix = 1f;
        [Editable, Tooltip("The pitch the sound is at, in Hz (Detect Root on the node can work it out).")]
        public float Root = 440f;
        [Editable, Tooltip("The pitch the sound is at as a note (A3, C#4, Eb2...): used instead of Root when set.")]
        public string RootNote = "";
        [Editable, Tooltip("The pitch to put it at as a note: used instead of Target when nothing's wired into Target.")]
        public string TargetNote = "";
        [Editable, Tooltip("For Out: how long each slice is, in milliseconds (short: tighter for drums and hits; long: smoother for tones).")]
        public float Window = 60f;
        [Output("Out")] public float Out;
        [Output("Time")] public float Time;
        [Tooltip("The pitch ratio it's shifting by (Target ÷ Root): 2 is an octave up.")]
        [Output("Ratio")] public float Ratio;

        /// <summary>A note's pitch in Hz (A4 = 440), or NaN when it isn't one ("A3", "C#4", "Eb2", "G-1").</summary>
        public static float NoteToHz(string note)
        {
            note = (note ?? "").Trim();
            if (note.Length < 2)
                return float.NaN;
            int letter = "C D EF G A B".IndexOf(char.ToUpperInvariant(note[0]));
            if (letter < 0 || note[0] == ' ')
                return float.NaN;
            int at = 1;
            if (note[at] == '#') { letter++; at++; }
            else if (note[at] == 'b') { letter--; at++; }
            if (!int.TryParse(note.Substring(at), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int octave))
                return float.NaN;
            int midi = 12 * (octave + 1) + letter;
            return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
        }

        /// <summary>The nearest note to a pitch ("A4"), for showing a detected Root.</summary>
        public static string HzToNote(float hz)
        {
            if (!(hz > 0f))
                return "";
            int midi = Mathf.RoundToInt(69f + 12f * Mathf.Log(hz / 440f, 2f));
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            return names[((midi % 12) + 12) % 12] + (Mathf.FloorToInt(midi / 12f) - 1);
        }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            float root = NoteToHz(RootNote);
            if (float.IsNaN(root))
                root = Root;
            float target = NoteToHz(TargetNote);
            bool targetWired = builder.IsConnected(this, "Target");
            if (targetWired || float.IsNaN(target))
                target = Target;
            if (!(root > 0f))
                builder.Warnings.Add($"\"{Name}\" needs a Root above 0 Hz (the pitch the sound is at).");
            int window = Mathf.Max(32, (int)(Mathf.Clamp(Window, 5f, 250f) * 0.001f * builder.SampleRate));
            var processor = new Processor
            {
                Target = builder.Input(this, "Target", target),
                Root = Mathf.Max(0.001f, root),
                Shift = new SoundPitchShiftNode.Processor
                {
                    In = builder.Input(this, "Audio", 0f),
                    Semitones = builder.Constant(0f),
                    Mix = builder.Input(this, "Mix", Mix),
                    Window = window,
                    Line = new float[window + 8],
                },
            };
            processor.Shift.Pitch = processor.Output("Ratio");
            return processor;
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] Target;
            public float Root;
            public SoundPitchShiftNode.Processor Shift;
            private double _time;
            private bool _started;

            public override void Reset(SoundContext context)
            {
                _started = false;
                Shift.Reset(context);
            }

            public override void Process(SoundContext context, int count)
            {
                var ratio = Output("Ratio");
                float inverse = 1f / Root;
                for (int i = 0; i < count; i++)
                    ratio[i] = Target[i] * inverse;
                // Out: only when something reads it (the pitch shift is the costly part).
                if (Outputs.TryGetValue("Out", out var shifted))
                {
                    Shift.Outputs["Out"] = shifted;
                    Shift.Process(context, count);
                }
                // Time: built up a sample at a time at the ratio (a Varispeed), so a changing Target never jumps.
                if (Outputs.TryGetValue("Time", out var time))
                {
                    double step = 1.0 / context.SampleRate;
                    if (!_started)
                    {
                        _time = context.SamplePosition * step;
                        _started = true;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        time[i] = (float)_time;
                        float r = ratio[i];
                        if (!float.IsNaN(r) && !float.IsInfinity(r))
                            _time += r * step;
                    }
                }
            }
        }
    }

    /// <summary>Works out the pitch a sound is at (its fundamental), for Set Pitch's Detect Root.</summary>
    public static class PitchDetector
    {
        /// <summary>
        /// The fundamental of the loudest stretch of <paramref name="samples"/> (up to about a second of it), in Hz,
        /// between 30 and 2000 Hz; 0 when there's no clear pitch (noise, silence). A difference function with the
        /// cumulative-mean normalisation of YIN, and the first dip under a threshold.
        /// </summary>
        public static float Estimate(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length < 256 || sampleRate <= 0)
                return 0f;
            int window = Mathf.Min(samples.Length / 2, Mathf.Min(sampleRate / 2, 16384));
            int maxLag = Mathf.Min(window - 1, sampleRate / 30), minLag = Mathf.Max(2, sampleRate / 2000);
            if (maxLag <= minLag + 2)
                return 0f;
            // The loudest window (in steps of a quarter window).
            int start = 0;
            double loudest = -1.0;
            for (int at = 0; at + window + maxLag < samples.Length; at += Mathf.Max(1, window / 4))
            {
                double energy = 0.0;
                for (int i = at; i < at + window; i += 4)
                    energy += samples[i] * samples[i];
                if (energy > loudest)
                {
                    loudest = energy;
                    start = at;
                }
            }
            int length = Mathf.Min(window, samples.Length - start - maxLag - 1);
            if (length < 128)
                return 0f;
            var d = new float[maxLag + 2];
            for (int lag = 1; lag <= maxLag + 1; lag++)
            {
                double sum = 0.0;
                for (int i = start; i < start + length; i++)
                {
                    double diff = samples[i] - samples[i + lag];
                    sum += diff * diff;
                }
                d[lag] = (float)sum;
            }
            double running = 0.0;
            var normalised = new float[maxLag + 2];
            normalised[0] = 1f;
            for (int lag = 1; lag <= maxLag + 1; lag++)
            {
                running += d[lag];
                normalised[lag] = running > 0.0 ? (float)(d[lag] * lag / running) : 1f;
            }
            int best = -1;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                if (normalised[lag] < 0.15f)
                {
                    while (lag + 1 <= maxLag && normalised[lag + 1] < normalised[lag])
                        lag++;
                    best = lag;
                    break;
                }
            }
            if (best < 0)
            {
                // No clear dip: the deepest one, if it's deep enough.
                float lowest = 1f;
                for (int lag = minLag; lag <= maxLag; lag++)
                    if (normalised[lag] < lowest) { lowest = normalised[lag]; best = lag; }
                if (lowest > 0.4f)
                    return 0f;
            }
            // Between samples: a parabola through the dip and its neighbours.
            float a = normalised[best - 1], b = normalised[best], c = normalised[best + 1];
            float denominator = a - 2f * b + c;
            float offset = Mathf.Abs(denominator) > 1e-9f ? Mathf.Clamp(0.5f * (a - c) / denominator, -1f, 1f) : 0f;
            return sampleRate / (best + offset);
        }
    }
}
