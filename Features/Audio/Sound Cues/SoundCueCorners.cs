using BlueGraph;
using Remedy.Schematics;
using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Around Corners"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Sound that bends round corners. When walls block the straight line to the listener but there's a way round (a doorway, the end of a corridor), the sound is heard from the last corner it comes round, fading in as it slips behind the corner (fully round by Fade Angle degrees past the edge, seen from the listener): quieter for the longer way, a little later, and duller the sharper it bends (Bend Cutoff at a right angle). What still gets straight through the walls is Through Walls loud, muffled to Through Cutoff (Hz). Fully closed in, only that is heard. In the game the ways round come from the Sound Field baked there; in the preview from its surroundings (try Doorway or Corner). Put it last, so everything before it goes round the corner."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Through Walls"", ""type"": ""float""}, {""name"": ""Through Cutoff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""a sound in the next room heard through the doorway"", ""footsteps coming round a corridor corner""], ""notes"": ""Only for 3D (Stereo) cues. Sound Fields baked before this was added need baking again.""}")]
    [Serializable]
    [Node(Name = "Around Corners", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundCornersNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Through Walls", Editable = true)] public float ThroughWalls = 0.2f;
        [Input("Through Cutoff", Editable = true)] public float ThroughCutoff = 700f;
        [Editable, Tooltip("How dull (Hz) the sound is when it bends a right angle round a corner (sharper bends are duller).")]
        public float BendCutoff = 2500f;
        [Editable, Tooltip("How far the sound must slip behind the corner (in degrees, seen from the listener: the angle between the corner and the sound) before it's heard fully round it. Closer to the edge, it fades between heard straight and heard round.")]
        public float FadeAngle = 35f;
        [Editable, Tooltip("Seconds to glide between heard straight and heard round a corner.")]
        public float Smoothing = 0.15f;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var audio = builder.Input(this, "Audio", 0f);
            // Flat (2D) cues aren't anywhere: nothing to go round.
            if (!(Graph is SoundCueGraph cue) || !cue.IsSpatial)
                return new PassProcessor { In = audio };
            var probe = new Probe { Node = this, Targets = new float[1] };
            builder.Context.Probes.Add(probe);
            var blocked = new ProbedProcessor { Ports = new[] { "Blocked" }, Targets = probe.Targets, Smoothing = Mathf.Max(0f, Smoothing) };
            builder.AddHelper(blocked);
            return new Processor(builder.SampleRate)
            {
                In = audio,
                ThroughWalls = builder.Input(this, "Through Walls", ThroughWalls),
                ThroughCutoff = builder.Input(this, "Through Cutoff", ThroughCutoff),
                Blocked = blocked.Output("Blocked"),
                Place = probe,
            };
        }

        // The world side: how the sound reaches the listener, and the bus at the corner it comes round.
        private sealed class Probe : SoundProbe
        {
            public SoundCornersNode Node;
            /// <summary>How blocked the straight way is (1: only what gets through the walls is heard straight).</summary>
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
                float straight = Vector3.Distance(world.Position, world.ListenerPosition);
                var sends = new SpaceSends
                {
                    Corner = bus,
                    // As loud as the sound itself would be the whole way round (the corner's bus has no falloff of its own).
                    CornerGain = (world.Hearing != null ? world.Hearing(length) : 1f / Mathf.Max(1f, length)) * behind,
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
                    string straight = blocked >= 0.99f ? $"straight sound down to Through Walls ({Node.ThroughWalls:0.00}, muffled)"
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
            public float[] In, ThroughWalls, ThroughCutoff, Blocked;
            public Probe Place;
            private readonly SpatialSender _sender;
            SpatialSender ISpatialSends.Sender => _sender;
            private float _muffled;

            public Processor(int sampleRate) => _sender = new SpatialSender(sampleRate);

            public override void Reset(SoundContext context)
            {
                _muffled = 0f;
                _sender.Reset();
            }

            public override void Process(SoundContext context, int count)
            {
                var o = Output("Out");
                if (count <= 0)
                    return;
                // What gets through the walls: muffled (a one-pole low pass), turned down.
                float cutoff = Mathf.Clamp(ThroughCutoff[0], 20f, 20000f);
                float amount = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Mathf.Max(8000, context.SampleRate));
                for (int i = 0; i < count; i++)
                {
                    float x = In[i];
                    _muffled += (x - _muffled) * amount;
                    float blocked = Mathf.Clamp01(Blocked[i]);
                    o[i] = x * (1f - blocked) + _muffled * Mathf.Max(0f, ThroughWalls[i]) * blocked;
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
