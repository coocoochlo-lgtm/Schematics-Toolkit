// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    // The Output, and the controls: values that change over time or with the game, for any input to follow. Every
    // wire carries one value per sample, like a shader's wires carry one per pixel.

    [SchematicDoc(@"{""name"": ""Output"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue"", ""description"": ""The finished sound: its Audio, times its Volume. Length: Auto plays as long as the longest Sound Wave or Generator in the cue; Fixed plays for Duration seconds; Endless plays until it's stopped. Heard By AI: how far away AI can hear it in the open, in meters (0: AI never hear it); walls, corners and doorways between change that as they would the sound."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Volume"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Output", Path = "Output", Deletable = false), Tags(SoundNode.Tag)]
    public class SoundCueOutput : SoundNode
    {
        public const string AudioPort = "Audio";
        public const string VolumePort = "Volume";

        public enum LengthMode { Auto, Fixed, Endless }

        [Input(AudioPort, Editable = false)]
        public float Audio;
        [Input(VolumePort, Editable = true)]
        public float Volume = 1f;

        [Editable]
        public LengthMode Length = LengthMode.Auto;
        /// <summary>Seconds, for Fixed length.</summary>
        [Editable]
        public float Duration = 1f;
        /// <summary>How far away AI can hear it in the open, in meters (0: never). See <see cref="SoundHearing"/>.</summary>
        [Editable, Tooltip("How far away AI can hear it in the open, in meters (0: AI never hear it). Walls, corners and doorways between change that as they would the sound.")]
        public float HeardByAI = 30f;

        // The voice reads the Output's inputs itself.
        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new StaticProcessor();
    }

    [SchematicDoc(@"{""name"": ""Time"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""The cue's playback time in seconds, and its Progress (0 at the start, 1 at the end of its length). Do math on Time and plug it into a Sound Wave or Generator to play it faster, slower, backwards, delayed or looped. Unconnected Time inputs already use it."", ""outputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Progress"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Time", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundTimeNode : SoundNode
    {
        [Output("Time")] public float Time;
        [Output("Progress")] public float Progress;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var processor = new StaticProcessor();
            processor.Outputs["Time"] = builder.Time;
            processor.Outputs["Progress"] = builder.Progress;
            return processor;
        }
    }

    [SchematicDoc(@"{""name"": ""Distance To Listener"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""How far the sound is from the Audio Listener (the camera, usually), in meters. Feed it through Map Range or Curve into a Low Pass's Cutoff to muffle far sounds, or into a Reverb's Mix."", ""outputs"": [{""name"": ""Distance"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Distance To Listener", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundDistanceNode : SoundNode
    {
        [Output("Distance")] public float Distance;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor();

        private sealed class Processor : SoundProcessor
        {
            public override void Process(SoundContext context, int count)
            {
                var output = Output("Distance");
                float distance = context.Distance;
                for (int i = 0; i < count; i++)
                    output[i] = distance;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Parameter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A value the game sets while the cue plays (Set Sound Cue Parameter node, or SoundCuePlayer.SetParameter), by Name: an engine's RPM, a character's speed, how hurt they are... It's Default until it's set."", ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Parameter", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundParameterNode : SoundNode
    {
        [Editable] public string ParameterName = "Intensity";
        [Editable] public float Default;
        [Output("Value")] public float Value;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { Index = builder.ParameterIndex(ParameterName, Default) };

        private sealed class Processor : SoundProcessor
        {
            public int Index;

            public override void Process(SoundContext context, int count)
            {
                var output = Output("Value");
                float value = Index < context.Parameters.Length ? context.Parameters[Index] : 0f;
                for (int i = 0; i < count; i++)
                    output[i] = value;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Random"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A random number between Min and Max, picked each time the cue plays (the same for the whole play): vary pitch (multiply Time by it), volume or a filter so repeated sounds don't sound identical."", ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Random", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundRandomNode : SoundNode
    {
        [Editable] public float Min = 0.9f;
        [Editable] public float Max = 1.1f;
        [Output("Value")] public float Value;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor { Min = Min, Max = Max };

        // Picked when the voice starts (and again each time a pooled voice is played again).
        private sealed class Processor : SoundProcessor
        {
            public float Min, Max;

            public override void Reset(SoundContext context)
            {
                var output = Output("Value");
                float value = Mathf.Lerp(Min, Max, (float)context.Random.NextDouble());
                for (int i = 0; i < output.Length; i++)
                    output[i] = value;
            }

            public override void Process(SoundContext context, int count) { }
        }
    }

    [SchematicDoc(@"{""name"": ""Curve"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A timeline: draw a curve, and Y is its height at X. Connect Time's Progress to X to shape something over the whole sound (a fade, a pitch drop), or Distance to shape it by distance."", ""inputs"": [{""name"": ""X"", ""type"": ""float""}], ""outputs"": [{""name"": ""Y"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Curve", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundCurveNode : SoundNode
    {
        [Input("X", Editable = true)] public float X;
        [Editable] public AnimationCurve Curve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Output("Y")] public float Y;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { X = builder.Input(this, "X", X), Table = new CurveTable(Curve) };

        private sealed class Processor : SoundProcessor
        {
            public float[] X;
            public CurveTable Table;

            public override void Process(SoundContext context, int count)
            {
                var y = Output("Y");
                for (int i = 0; i < count; i++)
                    y[i] = Table.Evaluate(X[i]);
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Map Range"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""Maps a value from one range to another: In Min becomes Out Min, In Max becomes Out Max, and everything between in proportion (clamped to the out range unless Clamp is off). E.g. Distance 0 to 50 meters into Cutoff 20000 to 800 Hz."", ""inputs"": [{""name"": ""Value"", ""type"": ""float""}, {""name"": ""In Min"", ""type"": ""float""}, {""name"": ""In Max"", ""type"": ""float""}, {""name"": ""Out Min"", ""type"": ""float""}, {""name"": ""Out Max"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Map Range", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundMapRangeNode : SoundNode
    {
        [Input("Value", Editable = true)] public float Value;
        [Input("In Min", Editable = true)] public float InMin;
        [Input("In Max", Editable = true)] public float InMax = 1f;
        [Input("Out Min", Editable = true)] public float OutMin;
        [Input("Out Max", Editable = true)] public float OutMax = 1f;
        [Editable] public bool Clamp = true;
        [Output("Result")] public float Result;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            Value = builder.Input(this, "Value", Value),
            InMin = builder.Input(this, "In Min", InMin),
            InMax = builder.Input(this, "In Max", InMax),
            OutMin = builder.Input(this, "Out Min", OutMin),
            OutMax = builder.Input(this, "Out Max", OutMax),
            Clamp = Clamp,
        };

        private sealed class Processor : SoundProcessor
        {
            public float[] Value, InMin, InMax, OutMin, OutMax;
            public bool Clamp;

            public override void Process(SoundContext context, int count)
            {
                var result = Output("Result");
                for (int i = 0; i < count; i++)
                {
                    float span = InMax[i] - InMin[i];
                    float t = span != 0f ? (Value[i] - InMin[i]) / span : 0f;
                    if (Clamp)
                        t = Mathf.Clamp01(t);
                    result[i] = OutMin[i] + (OutMax[i] - OutMin[i]) * t;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Math"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A and B combined, sample by sample: add, subtract, multiply, divide, modulo (Time modulo 2 loops every 2 seconds), power, min or max. Multiplying audio by a value changes its volume; multiplying Time by one changes the speed (and pitch) of what it plays."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Math", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundMathNode : SoundNode
    {
        public enum Operation { Add, Subtract, Multiply, Divide, Modulo, Power, Min, Max }

        [Input("A", Editable = true)] public float A;
        [Input("B", Editable = true)] public float B = 1f;
        [Editable] public Operation Operator = Operation.Multiply;
        [Output("Result")] public float Result;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { A = builder.Input(this, "A", A), B = builder.Input(this, "B", B), Op = Operator };

        private sealed class Processor : SoundProcessor
        {
            public float[] A, B;
            public Operation Op;

            public override void Process(SoundContext context, int count)
            {
                var r = Output("Result");
                switch (Op)
                {
                    case Operation.Add: for (int i = 0; i < count; i++) r[i] = A[i] + B[i]; break;
                    case Operation.Subtract: for (int i = 0; i < count; i++) r[i] = A[i] - B[i]; break;
                    case Operation.Multiply: for (int i = 0; i < count; i++) r[i] = A[i] * B[i]; break;
                    case Operation.Divide: for (int i = 0; i < count; i++) r[i] = B[i] != 0f ? A[i] / B[i] : 0f; break;
                    // Always positive, so a looped Time never goes negative.
                    case Operation.Modulo: for (int i = 0; i < count; i++) r[i] = B[i] != 0f ? A[i] - B[i] * Mathf.Floor(A[i] / B[i]) : 0f; break;
                    case Operation.Power: for (int i = 0; i < count; i++) r[i] = Mathf.Pow(A[i], B[i]); break;
                    case Operation.Min: for (int i = 0; i < count; i++) r[i] = Mathf.Min(A[i], B[i]); break;
                    case Operation.Max: for (int i = 0; i < count; i++) r[i] = Mathf.Max(A[i], B[i]); break;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Function"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""One value through a function, sample by sample: Sine Wave (one full wave per 1 of input), sine/cosine of radians, absolute, floor, fraction (loops 0 to 1), square root, negate, one minus, clamp 0 to 1, sign, decibels to volume..."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Function", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundFunctionNode : SoundNode
    {
        public enum Function { SineWave, Sin, Cos, Abs, Floor, Fraction, Sqrt, Negate, OneMinus, Clamp01, Sign, DecibelsToVolume, VolumeToDecibels }

        [Input("In", Editable = true)] public float In;
        [Editable] public Function Operator = Function.SineWave;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { In = builder.Input(this, "In", In), Op = Operator };

        private sealed class Processor : SoundProcessor
        {
            public float[] In;
            public Function Op;

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                for (int i = 0; i < count; i++)
                {
                    float x = In[i];
                    o[i] = Op switch
                    {
                        Function.SineWave => Mathf.Sin(x * 2f * Mathf.PI),
                        Function.Sin => Mathf.Sin(x),
                        Function.Cos => Mathf.Cos(x),
                        Function.Abs => Mathf.Abs(x),
                        Function.Floor => Mathf.Floor(x),
                        Function.Fraction => x - Mathf.Floor(x),
                        Function.Sqrt => x > 0f ? Mathf.Sqrt(x) : 0f,
                        Function.Negate => -x,
                        Function.OneMinus => 1f - x,
                        Function.Clamp01 => Mathf.Clamp01(x),
                        Function.Sign => x > 0f ? 1f : x < 0f ? -1f : 0f,
                        Function.DecibelsToVolume => Mathf.Pow(10f, x / 20f),
                        Function.VolumeToDecibels => x > 0f ? 20f * Mathf.Log10(x) : -80f,
                        _ => x,
                    };
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Oscillator"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A repeating wave (sine, square, saw, triangle, or noise) at Frequency times a second, scaled by Amplitude and moved by Offset. Slow ones (1 to 10 Hz) wobble a control: multiply audio by one for tremolo, add one to Time for vibrato. Fast ones (100+ Hz) are tones to listen to. Follows its Time input (the cue's time unconnected)."", ""inputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Frequency"", ""type"": ""float""}, {""name"": ""Amplitude"", ""type"": ""float""}, {""name"": ""Offset"", ""type"": ""float""}], ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Oscillator", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundOscillatorNode : SoundNode
    {
        public enum Shape { Sine, Square, Saw, Triangle, Noise }

        [Input("Time", Editable = false)] public float Time;
        [Input("Frequency", Editable = true)] public float Frequency = 5f;
        [Input("Amplitude", Editable = true)] public float Amplitude = 1f;
        [Input("Offset", Editable = true)] public float Offset;
        [Editable] public Shape Wave = Shape.Sine;
        [Output("Value")] public float Value;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder) => new Processor
        {
            Time = builder.TimeInput(this, "Time"),
            Frequency = builder.Input(this, "Frequency", Frequency),
            Amplitude = builder.Input(this, "Amplitude", Amplitude),
            Offset = builder.Input(this, "Offset", Offset),
            Wave = Wave,
            Random = new System.Random(builder.Context.Random.Next()),
        };

        private sealed class Processor : SoundProcessor
        {
            public float[] Time, Frequency, Amplitude, Offset;
            public Shape Wave;
            public System.Random Random;

            public override void Reset(SoundContext context) => Random = new System.Random(context.Random.Next());

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Value");
                for (int i = 0; i < count; i++)
                {
                    // The phase straight from time x frequency, as a shader would: no state, so time math just works.
                    double cycles = (double)Time[i] * Frequency[i];
                    float phase = (float)(cycles - Math.Floor(cycles));
                    float v = Wave switch
                    {
                        Shape.Sine => Mathf.Sin(phase * 2f * Mathf.PI),
                        Shape.Square => phase < 0.5f ? 1f : -1f,
                        Shape.Saw => phase * 2f - 1f,
                        Shape.Triangle => phase < 0.5f ? phase * 4f - 1f : 3f - phase * 4f,
                        _ => (float)Random.NextDouble() * 2f - 1f,
                    };
                    o[i] = v * Amplitude[i] + Offset[i];
                }
            }
        }
    }
}
