// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    // The Output, and the controls: values that change over time or with the game, for any input to follow. Every
    // wire carries one value per sample, like a shader's wires carry one per pixel.

    [SchematicDoc(@"{""name"": ""Output"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue"", ""description"": ""The finished sound: its Audio, times its Volume. Length: Auto plays as long as the longest Sound Wave or Generator in the cue; Fixed plays for Duration seconds; Endless plays until it's stopped. Heard By AI: how far away AI can hear it in the open, in meters (0: AI never hear it); walls, corners and doorways between change that as they would the sound. Echo Delay: how late its Spatial echoes and slapback come back, as a multiple of real sound (1 as sound travels, 2 twice as late, 0.5 half)."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Volume"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Output", Path = "Output", Deletable = false), Tags(SoundCueGraph.OutputOnlyTag)]
    public class SoundCueOutput : SoundNode
    {
        public const string AudioPort = "Audio";
        public const string VolumePort = "Volume";

        public enum LengthMode { Auto, Fixed, Endless }
        /// <summary>How the cue is rendered (see <see cref="SoundContext.Live"/>).</summary>
        public enum RenderMode { Auto, Live, Ahead }

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
        [Editable, Tooltip("Live: rendered as it's heard, so changes to its Parameters are heard at once (it works on the audio thread as it plays). Ahead: rendered about a second ahead in a streamed clip (cheapest for one-shots; changes come late). Auto: Live when the game sets its Parameters or it has a Trigger.")]
        public RenderMode Rendering = RenderMode.Auto;

        /// <summary>How the cue fades with distance (see <see cref="FalloffAt"/>).</summary>
        public enum FalloffShape { Natural, Linear, Smooth, Custom, AudioSource }

        [Editable, Tooltip("How it fades with distance, from Full Volume Within to silent at Audible Range (Volume still scales it on top). Natural: like real sound, quick at first then a long tail. Linear: evenly. Smooth: holds, then eases out. Custom: Falloff Curve. Audio Source: the player's AudioSource's own 3D settings.")]
        public FalloffShape Falloff = FalloffShape.Natural;
        [Editable, Tooltip("Within this distance (meters) it's at full volume.")]
        public float FullVolumeWithin = 2f;
        [Editable, Tooltip("How far away it can be heard at all (meters): silent from here on (and not rendered).")]
        public float AudibleRange = 60f;
        [Editable, Tooltip("Custom falloff: the volume (1 = full) across the range, from Full Volume Within (0) to Audible Range (1).")]
        public AnimationCurve FalloffCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [Editable, Tooltip("How late its echoes come back, as a multiple of real sound (Spatial Reverb and Delay): 1 as sound travels; 2 twice as late, as if sound were half as fast (bigger, slower echoes); 0.5 half as late. Echo walls and slapback; the sound itself and its reverb aren't delayed.")]
        public float EchoDelay = 1f;

        /// <summary>Whether the cue sets its own falloff (rather than leaving it to the AudioSource).</summary>
        public bool OwnsFalloff => Falloff != FalloffShape.AudioSource;

        /// <summary>How loud the falloff leaves it at a distance (0 to 1; main thread, for Custom).</summary>
        public float FalloffAt(float distance)
        {
            float near = Mathf.Max(0f, FullVolumeWithin), range = Mathf.Max(near + 0.01f, AudibleRange);
            if (distance <= near)
                return 1f;
            if (distance >= range)
                return 0f;
            float t = (distance - near) / (range - near);
            switch (Falloff)
            {
                case FalloffShape.Linear:
                    return 1f - t;
                case FalloffShape.Smooth:
                    return 1f - t * t * (3f - 2f * t);
                case FalloffShape.Custom:
                    return FalloffCurve != null ? Mathf.Clamp01(FalloffCurve.Evaluate(t)) : 1f - t;
                default:
                    // Natural: the inverse-distance fade of real sound (from Full Volume Within), bent down to reach
                    // silence at Audible Range.
                    float reference = Mathf.Max(0.5f, near);
                    float inverse = reference / Mathf.Max(reference, distance - near + reference);
                    float end = reference / (range - near + reference);
                    return Mathf.Clamp01((inverse - end) / (1f - end));
            }
        }

        [NonSerialized] private AnimationCurve _rolloff;
        [NonSerialized] private int _rolloffFor;

        /// <summary>
        /// The falloff as an AudioSource's custom rolloff curve (across 0 to its Max Distance, which is
        /// <see cref="AudibleRange"/>). Made again only when the settings change.
        /// </summary>
        public AnimationCurve RolloffCurve()
        {
            int settings = HashCode.Combine(Falloff, FullVolumeWithin, AudibleRange, FalloffCurve?.GetHashCode() ?? 0);
            if (_rolloff != null && _rolloffFor == settings && Falloff != FalloffShape.Custom)
                return _rolloff;
            const int points = 32;
            var keys = new Keyframe[points];
            float range = Mathf.Max(0.01f, AudibleRange);
            for (int i = 0; i < points; i++)
            {
                float x = i / (float)(points - 1);
                keys[i] = new Keyframe(x, FalloffAt(x * range));
            }
            _rolloff = new AnimationCurve(keys);
            for (int i = 0; i < points; i++)
                _rolloff.SmoothTangents(i, 0f);
            _rolloffFor = settings;
            return _rolloff;
        }

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

    [SchematicDoc(@"{""name"": ""Listener"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""Where the sound is from the Audio Listener (the camera, usually), like Shader Graph's Camera node: Distance (meters), Angle (degrees off where the listener faces: 0 ahead, 180 behind), Pan (-1 left to 1 right), Front (1 ahead, 0 beside, -1 behind), Elevation (degrees above, negative below), Approach Speed (m/s it's closing in; negative going away) and Same Room (1 when both are in the same Sound Room, else 0). Updated each frame, gliding across each block; a cue that uses it is rendered as it's heard."", ""outputs"": [{""name"": ""Distance"", ""type"": ""float""}, {""name"": ""Angle"", ""type"": ""float""}, {""name"": ""Pan"", ""type"": ""float""}, {""name"": ""Front"", ""type"": ""float""}, {""name"": ""Elevation"", ""type"": ""float""}, {""name"": ""Approach Speed"", ""type"": ""float""}, {""name"": ""Same Room"", ""type"": ""float""}], ""use_for"": [""muffling sounds behind the listener (Front into a Filter's Cutoff)"", ""a siren that's brighter coming closer (Approach Speed)"", ""quieter or wetter through a wall (Same Room)""]}")]
    [Serializable]
    [Node(Name = "Listener", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundListenerNode : SoundNode
    {
        [Output("Distance")] public float Distance;
        [Output("Angle")] public float Angle;
        [Output("Pan")] public float Pan;
        [Output("Front")] public float Front;
        [Output("Elevation")] public float Elevation;
        [Output("Approach Speed")] public float ApproachSpeed;
        [Output("Same Room")] public float SameRoom;

        private static readonly string[] Ports = { "Distance", "Angle", "Pan", "Front", "Elevation", "Approach Speed", "Same Room" };

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            builder.Context.FollowsListener = true;
            return new Processor();
        }

        // Each output glides from its last value to the new one across the block (the player sets them once a frame),
        // and is left alone while its value stays the same.
        private sealed class Processor : SoundProcessor
        {
            private readonly float[] _last = new float[Ports.Length];
            private readonly int[] _filled = new int[Ports.Length];

            public override void Reset(SoundContext context)
            {
                for (int k = 0; k < Ports.Length; k++)
                {
                    _last[k] = float.NaN;
                    _filled[k] = 0;
                }
            }

            public override void Process(SoundContext context, int count)
            {
                for (int k = 0; k < Ports.Length; k++)
                {
                    if (!Outputs.ContainsKey(Ports[k]))
                        continue; // (nothing reads it)
                    float value = k switch
                    {
                        0 => context.Distance,
                        1 => context.ListenerAngle,
                        2 => context.ListenerPan,
                        3 => context.ListenerFront,
                        4 => context.ListenerElevation,
                        5 => context.ApproachSpeed,
                        _ => context.SameRoom,
                    };
                    var output = Output(Ports[k]);
                    float last = _last[k];
                    if (last == value && _filled[k] >= count)
                        continue;
                    if (float.IsNaN(last) || last == value)
                    {
                        for (int i = 0; i < count; i++)
                            output[i] = value;
                        _filled[k] = last == value ? Mathf.Max(_filled[k], count) : count;
                    }
                    else
                    {
                        float step = (value - last) / count;
                        for (int i = 0; i < count; i++)
                            output[i] = last + step * (i + 1);
                        _filled[k] = 0;
                    }
                    _last[k] = value;
                }
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Parameter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A value the game sets while the cue plays (the Set Graph Parameters node on its Player, or SoundCuePlayer.SetParameter), by Name: an engine's RPM, a character's speed, how hurt they are... It's Default until it's set. A new value glides there over Glide seconds (no zipper steps from values set once a frame). A cue with Parameters is rendered as it's heard (see Output's Rendering), so a change is heard at once."", ""outputs"": [{""name"": ""Value"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Parameter", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundParameterNode : SoundNode
    {
        [Editable] public string ParameterName = "Intensity";
        [Editable] public float Default;
        [Editable, Tooltip("How long a new value takes to glide in (seconds; most of the way there by then), so values set once a frame (an engine's RPM) sweep smoothly instead of stepping. 0 = jumps straight there.")]
        public float Glide = 0.05f;
        [Tooltip("How it's shown on Function nodes calling this cue: its control, range, group and description.")]
        public Remedy.Schematics.ParameterUI UI = new();
        [Output("Value")] public float Value;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            // Called as a function: the value the Function node gives (wired in, or set on it).
            if (builder.CallParameter(ParameterName, out var given))
            {
                var passed = new StaticProcessor();
                passed.Outputs["Value"] = given;
                return passed;
            }
            // What the game sets is heard as it changes: the cue is rendered as it's heard (Output's Rendering, Auto).
            builder.Context.HasParameters = true;
            return new Processor
            {
                Index = builder.ParameterIndex(ParameterName, Default),
                Coefficient = Glide > 0f ? 1f - Mathf.Exp(-1f / (Glide * 0.5f * builder.SampleRate)) : 1f,
            };
        }

        // Glides toward the value set, and once there does nothing: its block already holds the value, so it's left
        // as it is until the value changes. After a gap (culled while unheard, a restart) it starts at the value.
        private sealed class Processor : SoundProcessor
        {
            public int Index;
            public float Coefficient;
            private float _current, _filledValue = float.NaN;
            private int _filled;
            private long _next = -1;

            public override void Reset(SoundContext context)
            {
                _next = -1;
                _filledValue = float.NaN;
                _filled = 0;
            }

            public override void Process(SoundContext context, int count)
            {
                var output = Output("Value");
                float target = Index < context.Parameters.Length ? context.Parameters[Index] : 0f;
                bool continues = context.SamplePosition == _next;
                _next = context.SamplePosition + count;
                if (continues && _current != target && Coefficient < 1f)
                {
                    float close = 1e-5f * Mathf.Max(1f, Mathf.Abs(target));
                    for (int i = 0; i < count; i++)
                    {
                        _current += (target - _current) * Coefficient;
                        if (Mathf.Abs(target - _current) <= close)
                            _current = target;
                        output[i] = _current;
                    }
                    _filledValue = float.NaN;
                    return;
                }
                _current = target;
                if (_filledValue == target && _filled >= count)
                    return; // (already holds it)
                for (int i = 0; i < count; i++)
                    output[i] = target;
                _filled = _filledValue == target ? Mathf.Max(_filled, count) : count;
                _filledValue = target;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Random"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""A random number between Min and Max. Once Per Play (the default): picked when the cue starts and the same for the whole play, so every play sounds a tad different (multiply Time by it for pitch, or vary volume or a filter). While Playing: a new pick Changes Per Second times a second (0: every sample, white noise between Min and Max), gliding from one to the next with Glide on: wobble, flutter, crackle."", ""outputs"": [{""name"": ""Value"", ""type"": ""float""}], ""use_for"": [""footsteps or gunshots that never sound identical"", ""random wobble or flutter on pitch or a filter"", ""noise between two values""]}")]
    [Serializable]
    [Node(Name = "Random", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundRandomNode : SoundNode
    {
        public enum PickMode
        {
            /// <summary>One value for the whole play, picked when it starts.</summary>
            OncePerPlay,
            /// <summary>New values while it plays.</summary>
            WhilePlaying,
        }

        [Editable] public float Min = 0.9f;
        [Editable] public float Max = 1.1f;
        [Editable, Tooltip("Once Per Play: one value for the whole play (each play a tad different). While Playing: new values as it plays.")]
        public PickMode Mode = PickMode.OncePerPlay;
        [Editable, Tooltip("While Playing: how many new values a second (0: a new one every sample).")]
        public float ChangesPerSecond = 8f;
        [Editable, Tooltip("While Playing: glide from one value to the next instead of jumping.")]
        public bool Glide;
        [Output("Value")] public float Value;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { Min = Min, Max = Max, Mode = Mode, ChangesPerSecond = Mathf.Max(0f, ChangesPerSecond), Glide = Glide };

        // Picked when the voice starts (and again each time a pooled voice is played again).
        private sealed class Processor : SoundProcessor
        {
            public float Min, Max, ChangesPerSecond;
            public PickMode Mode;
            public bool Glide;
            private System.Random _random;
            private float _from, _to;
            private double _phase;

            private float Pick() => Mathf.Lerp(Min, Max, (float)_random.NextDouble());

            public override void Reset(SoundContext context)
            {
                _random = new System.Random(context.Random.Next());
                _from = Pick();
                _to = Pick();
                _phase = 0.0;
                var output = Output("Value");
                for (int i = 0; i < output.Length; i++)
                    output[i] = _from;
            }

            public override void Process(SoundContext context, int count)
            {
                if (Mode == PickMode.OncePerPlay)
                    return; // the value written at the start stays

                var output = Output("Value");
                if (ChangesPerSecond <= 0f)
                {
                    for (int i = 0; i < count; i++)
                        output[i] = Pick();
                    return;
                }
                double step = ChangesPerSecond / (double)context.SampleRate;
                for (int i = 0; i < count; i++)
                {
                    output[i] = Glide ? Mathf.SmoothStep(_from, _to, (float)_phase) : _from;
                    _phase += step;
                    if (_phase >= 1.0)
                    {
                        _phase -= Math.Floor(_phase);
                        _from = Glide ? _to : Pick();
                        _to = Pick();
                    }
                }
            }
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

    [Serializable]
    [NodeColor(NodeColors.Math)]
    // Replaced by a node per operation (SoundCueMath.cs): kept so cues saved with it still load and play, and turned
    // into its operation's node when the cue is opened (SoundMathUpgrade).
    [Node(Name = "Math (Old)", Path = "Math"), Tags(LegacyTag)]
    public class SoundMathNode : SoundNode
    {
        /// <summary>The tag of the old all-in-one math nodes: no graph offers it, so they aren't in the node search.</summary>
        public const string LegacyTag = "Sound Cue Legacy";

        public override void OnValidate()
        {
            base.OnValidate();
            Warning = "Old: each operation is its own node now (Add, Multiply...). This one is replaced by it when the cue is opened.";
        }

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

    [Serializable]
    [NodeColor(NodeColors.Math)]
    // Replaced by a node per function (SoundCueMath.cs), like SoundMathNode.
    [Node(Name = "Function (Old)", Path = "Math"), Tags(SoundMathNode.LegacyTag)]
    public class SoundFunctionNode : SoundNode
    {
        public override void OnValidate()
        {
            base.OnValidate();
            Warning = "Old: each function is its own node now (Sine Wave, Abs...). This one is replaced by it when the cue is opened.";
        }

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
