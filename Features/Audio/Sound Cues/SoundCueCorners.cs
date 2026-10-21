using BlueGraph;
using Remedy.Schematics;
using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Around Corners"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Effects"", ""description"": ""Sound that bends round corners. When walls block the straight line to the listener but there's a way round (a doorway, the end of a corridor), the sound is heard from the last corner it comes round: quieter for the longer way, a little later, and duller the sharper it bends (Bend Cutoff at a right angle). What still gets straight through the walls is Through Walls loud, muffled to Through Cutoff (Hz). Fully closed in, only that is heard. In the game the ways round come from the Sound Field baked there; in the preview from its surroundings (try Doorway or Corner). Put it last, so everything before it goes round the corner."", ""inputs"": [{""name"": ""Audio"", ""type"": ""audio""}, {""name"": ""Through Walls"", ""type"": ""float""}, {""name"": ""Through Cutoff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""audio""}], ""use_for"": [""a sound in the next room heard through the doorway"", ""footsteps coming round a corridor corner""], ""notes"": ""Only for 3D (Stereo) cues. Sound Fields baked before this was added need baking again.""}")]
    [Serializable]
    [Node(Name = "Around Corners", Path = "Effects"), Tags(SoundNode.Tag)]
    public class SoundCornersNode : SoundNode
    {
        [Input("Audio", Editable = false)] public float AudioIn;
        [Input("Through Walls", Editable = true)] public float ThroughWalls = 0.2f;
        [Input("Through Cutoff", Editable = true)] public float ThroughCutoff = 700f;
        [Editable, Tooltip("How dull (Hz) the sound is when it bends a right angle round a corner (sharper bends are duller).")]
        public float BendCutoff = 2500f;
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
                if (world.Previewing)
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

                if (route == SoundRoute.Sealed)
                {
                    Set(null, 1f);
                    return;
                }
                var bus = route == SoundRoute.Around && pool != null
                    ? pool.Take(_claim, corner, Cutoff(bend))
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
                    CornerGain = world.Hearing != null ? world.Hearing(length) : 1f / Mathf.Max(1f, length),
                    // Later than the sound itself by the longer way.
                    CornerDelay = (int)(Mathf.Max(0f, length - straight) / SoundSpaces.SpeedOfSound * bus.Rate),
                };
                // The straight sound is only turned down to what gets through the walls once the corner is playing it.
                Set(sends, bus.Healthy && Volatile.Read(ref Sending) != 0 ? 1f : 0f);
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
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] In, ThroughWalls, ThroughCutoff, Blocked;
            public Probe Place;
            private readonly SpatialSender _sender;
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
