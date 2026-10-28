using BlueGraph;
using Remedy.Schematics;
using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Around Corners"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Sound that bends round corners. When walls block the straight line to the listener but there's a way round (a doorway, the end of a corridor), the sound is heard both ways at once. From the last corner it comes round (fading in as it slips behind the corner, fully round by Fade Angle degrees past the edge, seen from the listener): quieter for the longer way and the sharper it has to bend (Bend Loss at a right angle), a little later, and duller the sharper the bend (Bend Cutoff at a right angle). And straight through the walls: Through Walls loud, muffled to Through Cutoff (Hz) (steeply by default: Wall Slope), duller still the further apart they are (Distance Dulling), with a little low rumble lifted (Rumble Boost dB around Rumble Frequency Hz), and fading exponentially with the distance between them (to about a third every Through Falloff meters), so a wall right beside you lets more through than a building's worth of them. Fully closed in, only the through-walls sound is heard. In the game the ways round come from the Sound Field baked there; in the preview from its surroundings (try Doorway or Corner). Put it last, so everything before it goes round the corner."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Through Walls"", ""type"": ""float""}, {""name"": ""Through Cutoff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""a sound in the next room heard through the doorway"", ""footsteps coming round a corridor corner""], ""notes"": ""Only for 3D (Stereo) cues. Sound Fields baked before this was added need baking again.""}")]
    [Serializable]
    [Node(Name = "Around Corners", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundCornersNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Through Walls", Editable = true)] public float ThroughWalls = 0.2f;
        [Input("Through Cutoff", Editable = true)] public float ThroughCutoff = 700f;
        [Editable, Tooltip("How dull (Hz) the sound is when it bends a right angle round a corner (sharper bends are duller).")]
        public float BendCutoff = 2500f;
        [Editable, Tooltip("How much quieter the sound coming round the corner is when it bends a right angle (0: no quieter, 1: silent). Sharper bends lose more, gentle ones hardly any.")]
        public float BendLoss = 0.5f;
        [Editable, Tooltip("Through walls: the distance (meters) over which what gets through drops to about a third, and to a third of that again over the next, and so on (on top of the sound's own fading with distance).")]
        public float ThroughFalloff = 10f;
        [Editable, Tooltip("Through walls: how hard the highs are cut above Through Cutoff. Walls let lows through far more easily than highs, so steeper sounds more like the next room.")]
        public WallFilter WallSlope = WallFilter.Steepest;
        [Editable, Tooltip("Through walls: how much duller it gets the further apart they are (0: not at all; 1: the cutoff falls as fast as the volume does). At 0.5, one Through Falloff away the cutoff is about 60% of Through Cutoff.")]
        public float DistanceDulling = 0.5f;
        [Editable, Tooltip("Through walls: how much the low rumble is lifted (dB; 0 for none), the way walls and floors carry bass.")]
        public float RumbleBoost = 4f;
        [Editable, Tooltip("Through walls: the low frequency (Hz) the rumble is lifted around.")]
        public float RumbleFrequency = 90f;
        [Editable, Tooltip("How far the sound must slip behind the corner (in degrees, seen from the listener: the angle between the corner and the sound) before it's heard fully round it. Closer to the edge, it fades between heard straight and heard round.")]
        public float FadeAngle = 35f;
        [Editable, Tooltip("Seconds to glide between heard straight and heard round a corner.")]
        public float Smoothing = 0.15f;
        [Output("Out")] public float Out;

        /// <summary>How hard the through-walls sound's highs are cut: 6, 12 or 24 dB an octave above Through Cutoff.</summary>
        public enum WallFilter { Gentle, Steep, Steepest }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var audio = builder.Input(this, "Audio", 0f);
            // Flat (2D) cues aren't anywhere: nothing to go round.
            if (!(Graph is SoundCueGraph cue) || !cue.IsSpatial)
                return new PassProcessor { In = audio };
            var probe = new Probe { Node = this, Targets = new float[] { 0f, 1f } };
            builder.Context.Probes.Add(probe);
            var blocked = new ProbedProcessor { Ports = new[] { "Blocked", "Through" }, Targets = probe.Targets, Smoothing = Mathf.Max(0f, Smoothing) };
            builder.AddHelper(blocked);
            return new Processor(builder.SampleRate)
            {
                In = audio,
                ThroughWalls = builder.Input(this, "Through Walls", ThroughWalls),
                ThroughCutoff = builder.Input(this, "Through Cutoff", ThroughCutoff),
                Blocked = blocked.Output("Blocked"),
                Through = blocked.Output("Through"),
                Place = probe,
                Slope = WallSlope,
                Dulling = Mathf.Clamp01(DistanceDulling),
                RumbleBoost = RumbleBoost,
                RumbleFrequency = RumbleFrequency,
            };
        }

        // The world side: how the sound reaches the listener, and the bus at the corner it comes round.
        private sealed class Probe : SoundProbe
        {
            public SoundCornersNode Node;
            /// <summary>
            /// [0] How blocked the straight way is (1: only what gets through the walls is heard straight); [1] how much
            /// of Through Walls gets through over the distance between them.
            /// </summary>
            public float[] Targets;
            /// <summary>The corner to send the sound to, read by the processor.</summary>
            public SpaceSends Sends;
            /// <summary>Whether the voice's sends can be placed in time, from the audio thread.</summary>
            public int Sending;
            private readonly CornerPool.Claim _claim = new();

            public override void Update(in SoundProbeWorld world)
            {
                SoundRoute route;
                Vector3 corner;
                float length, bend;
                CornerPool pool;
                float straight = Vector3.Distance(world.Position, world.ListenerPosition);
                // Through the walls: exponentially less the further apart they are.
                Volatile.Write(ref Targets[1], Mathf.Exp(-straight / Mathf.Max(0.1f, Node.ThroughFalloff)));
                if (world.Simulated)
                {
                    var simulation = SoundSpaceSimulation.Current;
                    if (simulation == null)
                    {
                        Set(null, 0f);
                        return;
                    }
                    route = simulation.Around(world.Position, world.ListenerPosition, out corner, out length, out bend);
                    pool = simulation.Corners;
                    if (route == SoundRoute.Around)
                    {
                        simulation.LastCorner = corner;
                        simulation.LastCornerAt = Time.realtimeSinceStartupAsDouble;
                    }
                }
                else
                {
                    route = SoundPaths.Around(world.Position, world.ListenerPosition, out corner, out length, out bend);
                    pool = route == SoundRoute.Around ? SoundSpaces.Get()?.Corners : null;
                }

                // Blocked for a moment only (a check that grazes something as the sound or listener moves): still
                // heard straight until it's stayed blocked for HoldClear, so a sound doesn't drop out and back.
                float now = Time.realtimeSinceStartup;
                if (route == SoundRoute.Clear || _clearAt < 0f)
                    _clearAt = now;
                else if (now - _clearAt < HoldClear)
                {
                    _route = SoundRoute.Clear;
                    _held = true;
                    _behind = 0f;
                    Set(null, 0f);
                    return;
                }
                _held = false;
                _route = route;
                _behind = 0f;
                if (route == SoundRoute.Sealed)
                {
                    Set(null, 1f);
                    return;
                }
                // How far behind the corner the sound has slipped, seen from the listener: just past the edge it's still
                // mostly heard straight (sound spreads round an edge), fading to fully round the corner by Fade Angle.
                float behind = route == SoundRoute.Around ? Behind(world.ListenerPosition, corner, world.Position, Node.FadeAngle) : 0f;
                _behind = behind;
                var bus = route == SoundRoute.Around && pool != null && behind > 0f
                    ? pool.Take(_claim, corner, Cutoff(bend * behind))
                    : null;
                if (bus == null)
                {
                    // Clear (or no bus free to go round with): heard straight.
                    Set(null, 0f);
                    return;
                }
                var sends = new SpaceSends
                {
                    Corner = bus,
                    // As loud as the sound itself would be the whole way round (the corner's bus has no falloff of its own),
                    // less for the sharper it bends.
                    CornerGain = (world.Hearing != null ? world.Hearing(length) : 1f / Mathf.Max(1f, length)) * behind * BendGain(bend),
                    // Later than the sound itself by the longer way.
                    CornerDelay = (int)(Mathf.Max(0f, length - straight) / SoundSpaces.SpeedOfSound * bus.Rate),
                };
                // The straight sound is only turned down to what gets through the walls once the corner is playing it.
                Set(sends, bus.Healthy && Volatile.Read(ref Sending) != 0 ? behind : 0f);
            }

            /// <summary>
            /// How far a sound has slipped behind a corner, 0 (at its edge) to 1 (<paramref name="fadeAngle"/> degrees or
            /// more past it), from the angle between the corner and the sound as seen from <paramref name="from"/>.
            /// </summary>
            internal static float Behind(Vector3 from, Vector3 corner, Vector3 sound, float fadeAngle)
            {
                float angle = Vector3.Angle(corner - from, sound - from);
                return Mathf.SmoothStep(0f, 1f, angle / Mathf.Max(1f, fadeAngle));
            }

            // Quieter the sharper it bends: Bend Loss at a right angle (bend 0.5), compounding past it, nothing straight on.
            private float BendGain(float bend)
                => Mathf.Pow(1f - Mathf.Clamp01(Node.BendLoss), Mathf.Clamp(bend * 2f, 0f, 2f));

            // Duller the sharper it bends: Bend Cutoff at a right angle, evenly in pitch, down to a quarter of it bent back.
            private float Cutoff(float bend)
            {
                float right = Mathf.Clamp(Node.BendCutoff, 200f, 20000f);
                return 20000f * Mathf.Pow(right / 20000f, Mathf.Clamp(bend * 2f, 0f, 1.6f));
            }

            private void Set(SpaceSends sends, float blocked)
            {
                Volatile.Write(ref Sends, sends);
                Volatile.Write(ref Targets[0], blocked);
            }

            private SoundRoute _route;
            private float _behind, _clearAt = -1f;
            private bool _held;
            /// <summary>Seconds the way must stay blocked before the sound stops being heard straight.</summary>
            public const float HoldClear = 0.3f;

            public override string DirectReport
            {
                get
                {
                    float blocked = Volatile.Read(ref Targets[0]);
                    float through = Volatile.Read(ref Targets[1]);
                    string straight = blocked >= 0.99f ? $"straight sound down to Through Walls ({Node.ThroughWalls:0.00} × {through:0.00} over the distance, muffled)"
                        : blocked > 0.01f ? $"straight sound {Mathf.Round((1f - blocked) * 10f) * 10f:0}% (the rest through walls)" : "straight sound in full";
                    return _route == SoundRoute.Sealed ? $"Around Corners: SEALED (no way through the Sound Field between them): {straight}"
                        : _route == SoundRoute.Around ? $"Around Corners: round a corner ({Mathf.Round(_behind * 10f) * 10f:0}% behind it): {straight}"
                        : _held ? $"Around Corners: clear (held: blocked under {HoldClear} s): {straight}"
                        : $"Around Corners: clear: {straight}";
                }
            }
        }

        private sealed class Processor : SoundProcessor, ISpatialSends
        {
            public float[] In, ThroughWalls, ThroughCutoff, Blocked, Through;
            public Probe Place;
            public WallFilter Slope;
            public float Dulling, RumbleBoost, RumbleFrequency;
            private readonly SpatialSender _sender;
            SpatialSender ISpatialSends.Sender => _sender;
            private float _muffled, _cutoff = -1f;
            private int _rate;
            private Biquad _low1 = Biquad.Flat, _low2 = Biquad.Flat, _rumble = Biquad.Flat;

            public Processor(int sampleRate) => _sender = new SpatialSender(sampleRate);

            public override void Reset(SoundContext context)
            {
                _muffled = 0f;
                _low1.Clear();
                _low2.Clear();
                _rumble.Clear();
                _sender.Reset();
            }

            // The filters for this block: the cutoff lower the further apart they are (by Distance Dulling).
            private void Tune(SoundContext context, int count)
            {
                int rate = Mathf.Max(8000, context.SampleRate);
                float through = Mathf.Clamp(Through[count - 1], 1e-4f, 1f);
                float cutoff = Mathf.Clamp(ThroughCutoff[0] * Mathf.Pow(through, Dulling), 40f, 20000f);
                if (rate != _rate)
                {
                    _rate = rate;
                    _cutoff = -1f;
                    if (RumbleBoost != 0f)
                        _rumble.SetPeak(Mathf.Clamp(RumbleFrequency, 20f, 400f), 0.8f, Mathf.Clamp(RumbleBoost, -12f, 12f), rate);
                }
                if (Mathf.Abs(cutoff - _cutoff) < _cutoff * 0.01f)
                    return;
                _cutoff = cutoff;
                // (Two in a row: a Butterworth pair, flat up to the cutoff then 24 dB an octave.)
                _low1.SetLowPass(cutoff, Slope == WallFilter.Steepest ? 0.541f : 0.707f, rate);
                _low2.SetLowPass(cutoff, 1.307f, rate);
            }

            private float Muffle(float x, float onePole)
            {
                switch (Slope)
                {
                    case WallFilter.Gentle:
                        _muffled += (x - _muffled) * onePole;
                        x = _muffled;
                        break;
                    case WallFilter.Steep:
                        x = _low1.Process(x);
                        break;
                    default:
                        x = _low2.Process(_low1.Process(x));
                        break;
                }
                return RumbleBoost != 0f ? _rumble.Process(x) : x;
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                if (count <= 0)
                    return;
                // What gets through the walls: muffled (duller the further apart), its rumble lifted, turned down.
                Tune(context, count);
                float onePole = 1f - Mathf.Exp(-2f * Mathf.PI * _cutoff / _rate);
                for (int i = 0; i < count; i++)
                {
                    float x = In[i];
                    float muffled = Muffle(x, onePole);
                    float blocked = Mathf.Clamp01(Blocked[i]);
                    o[i] = x * (1f - blocked) + muffled * Mathf.Max(0f, ThroughWalls[i]) * Through[i] * blocked;
                }
                Volatile.Write(ref Place.Sending, context.HeardStart >= 0 ? 1 : 0);
                _sender.Send(Volatile.Read(ref Place.Sends), context, In, count, 1f);
            }
        }

        private sealed class PassProcessor : SoundProcessor
        {
            public float[] In;

            public override void Process(SoundContext context, int count) => Array.Copy(In, Output("Out"), count);
        }
    }
}
