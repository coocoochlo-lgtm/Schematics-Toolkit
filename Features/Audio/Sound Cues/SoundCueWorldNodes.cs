// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Surfaces;
using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    // Controls that follow the world around the sound: what's between it and the listener, and what it's on or near.
    // They ask the world on the main thread (a SoundProbe, about 10 times a second) and glide between the answers.

    /// <summary>
    /// A probe's answers, read by its processor on the audio thread: each output glides towards its latest value over
    /// Smoothing seconds (and starts on the first answer of a play, rather than gliding in from the last play's).
    /// </summary>
    internal sealed class ProbedProcessor : SoundProcessor
    {
        public string[] Ports;
        public float[] Targets;
        public float Smoothing;

        private float[] _current;
        private bool _primed;

        public override void Reset(SoundContext context) => _primed = false;

        public override void Process(SoundContext context, int count)
        {
            _current ??= new float[Ports.Length];
            if (!_primed)
            {
                for (int p = 0; p < Ports.Length; p++)
                    _current[p] = Volatile.Read(ref Targets[p]);
                _primed = true;
            }
            float step = Smoothing > 0f ? 1f - Mathf.Exp(-1f / (Smoothing * Mathf.Max(1, context.SampleRate))) : 1f;
            for (int p = 0; p < Ports.Length; p++)
            {
                var output = Output(Ports[p]);
                float target = Volatile.Read(ref Targets[p]), value = _current[p];
                if (Mathf.Abs(target - value) < 1e-6f)
                {
                    for (int i = 0; i < count; i++)
                        output[i] = target;
                    value = target;
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        value += (target - value) * step;
                        output[i] = value;
                    }
                }
                _current[p] = value;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Occlusion"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""How blocked the line from the sound to the Audio Listener is, from 0 (clear) to 1 (behind solid walls): each surface in the way blocks by its Surface Material's Absorption (painted surfaces, tagged objects, or the default material), at least Minimum Per Wall, up to Max Walls. Muffled Cutoff goes from 20000 Hz (clear) down to Blocked Cutoff: plug it straight into a Low Pass's Cutoff. Volume goes from 1 down to Blocked Volume: multiply the audio by it. Checked about 10 times a second, gliding over Smoothing seconds."", ""outputs"": [{""name"": ""Occlusion"", ""type"": ""float""}, {""name"": ""Muffled Cutoff"", ""type"": ""float""}, {""name"": ""Volume"", ""type"": ""float""}], ""use_for"": [""muffling sounds behind walls"", ""quieter sounds in the next room""], ""notes"": ""The editor's preview has no world: it uses Preview Occlusion.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Occlusion", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundOcclusionNode : SoundNode
    {
        [Output("Occlusion")] public float Occlusion;
        [Output("Muffled Cutoff")] public float MuffledCutoff;
        [Output("Volume")] public float Volume;

        [Editable, Tooltip("Low Pass cutoff (Hz) when fully blocked.")] public float BlockedCutoff = 700f;
        [Editable, Tooltip("Volume when fully blocked.")] public float BlockedVolume = 0.4f;
        [Editable, Tooltip("How many surfaces in the way count.")] public int MaxWalls = 3;
        [Editable, Tooltip("The least each surface in the way blocks (0 to 1), whatever its Absorption.")] public float MinimumPerWall = 0.35f;
        [Editable, Tooltip("Seconds to glide to a new value.")] public float Smoothing = 0.15f;
        [Editable, Tooltip("The occlusion the editor's preview uses (0 to 1).")] public float PreviewOcclusion;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var probe = new Probe { Node = this, Targets = new float[3] };
            probe.Set(0f);
            builder.Context.Probes.Add(probe);
            return new ProbedProcessor { Ports = new[] { "Occlusion", "Muffled Cutoff", "Volume" }, Targets = probe.Targets, Smoothing = Smoothing };
        }

        private sealed class Probe : SoundProbe
        {
            public SoundOcclusionNode Node;
            public float[] Targets;

            public override void Update(in SoundProbeWorld world)
            {
                float occlusion;
                if (world.Previewing)
                    occlusion = Mathf.Clamp01(Node.PreviewOcclusion);
                else if (world.Listener == null)
                    occlusion = 0f;
                else
                    occlusion = SurfaceQuery.Occlusion(world.Position, world.ListenerPosition, world.Self, world.Listener,
                        Mathf.Max(1, Node.MaxWalls), Mathf.Clamp01(Node.MinimumPerWall));
                Set(occlusion);
            }

            public void Set(float occlusion)
            {
                // The cutoff falls evenly in pitch (octaves), not in Hz, so half-blocked sounds half-muffled.
                float blocked = Mathf.Clamp(Node.BlockedCutoff, 20f, 20000f);
                Volatile.Write(ref Targets[0], occlusion);
                Volatile.Write(ref Targets[1], 20000f * Mathf.Pow(blocked / 20000f, occlusion));
                Volatile.Write(ref Targets[2], Mathf.Lerp(1f, Mathf.Clamp01(Node.BlockedVolume), occlusion));
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Surface"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""What the surface is made of in a direction from the sound (Down: what it's on), up to Distance meters away: its Surface Material's Wetness, Absorption, Hardness and Friction, one more property by name (Value), whether it's the material picked in Is (Is Match, 1 or 0), whether anything was hit (Has Hit, 1 or 0), and how far (Hit Distance). Painted surfaces, tagged objects, or the default material. Checked when the cue starts, then about 10 times a second."", ""outputs"": [{""name"": ""Has Hit"", ""type"": ""float""}, {""name"": ""Hit Distance"", ""type"": ""float""}, {""name"": ""Wetness"", ""type"": ""float""}, {""name"": ""Absorption"", ""type"": ""float""}, {""name"": ""Hardness"", ""type"": ""float""}, {""name"": ""Friction"", ""type"": ""float""}, {""name"": ""Value"", ""type"": ""float""}, {""name"": ""Is Match"", ""type"": ""float""}], ""use_for"": [""footsteps that sound like the floor"", ""splashy steps on wet ground"", ""more echo near hard walls""], ""notes"": ""Forward, Back, Left and Right turn with the sound's object. The editor's preview has no world: it uses Preview Material.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Surface", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundSurfaceNode : SoundNode
    {
        public enum Toward { Down, Up, Forward, Back, Left, Right, Listener, Custom }

        [Output("Has Hit")] public float HasHit;
        [Output("Hit Distance")] public float HitDistance;
        [Output("Wetness")] public float Wetness;
        [Output("Absorption")] public float Absorption;
        [Output("Hardness")] public float Hardness;
        [Output("Friction")] public float Friction;
        [Output("Value")] public float Value;
        [Output("Is Match")] public float IsMatch;

        [Editable, Tooltip("Which way to look.")] public Toward Direction = Toward.Down;
        [Editable, Tooltip("The direction for Custom (in the object's space when Custom Local is on).")] public Vector3 CustomDirection = Vector3.down;
        [Editable] public bool CustomLocal;
        [Editable, Tooltip("How far to look, in meters.")] public float Distance = 2f;
        [Editable] public SurfaceLayers Layers = SurfaceLayers.All;
        [Editable, Tooltip("A property for Value (one of the material's own, or Wetness, Absorption...).")] public string Property = "";
        [Editable, Tooltip("The material Is Match checks for.")] public SurfaceMaterial Is;
        [Editable, Tooltip("Seconds to glide to new values (0: jump).")] public float Smoothing = 0.05f;
        [Editable, Tooltip("The material the editor's preview pretends it found (none: nothing hit).")] public SurfaceMaterial PreviewMaterial;

        private static readonly string[] Ports = { "Has Hit", "Hit Distance", "Wetness", "Absorption", "Hardness", "Friction", "Value", "Is Match" };

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var probe = new Probe { Node = this, Targets = new float[Ports.Length] };
            probe.Set(null, Distance, false);
            builder.Context.Probes.Add(probe);
            return new ProbedProcessor { Ports = Ports, Targets = probe.Targets, Smoothing = Smoothing };
        }

        private sealed class Probe : SoundProbe
        {
            public SoundSurfaceNode Node;
            public float[] Targets;

            public override void Update(in SoundProbeWorld world)
            {
                if (world.Previewing)
                {
                    Set(Node.PreviewMaterial, Node.PreviewMaterial != null ? Mathf.Min(0.5f, Node.Distance) : Node.Distance, Node.PreviewMaterial != null);
                    return;
                }
                var direction = Node.Direction switch
                {
                    Toward.Down => Vector3.down,
                    Toward.Up => Vector3.up,
                    Toward.Forward => world.Rotation * Vector3.forward,
                    Toward.Back => world.Rotation * Vector3.back,
                    Toward.Left => world.Rotation * Vector3.left,
                    Toward.Right => world.Rotation * Vector3.right,
                    Toward.Listener => world.ListenerPosition - world.Position,
                    _ => Node.CustomLocal ? world.Rotation * Node.CustomDirection : Node.CustomDirection,
                };
                if (SurfaceQuery.Cast(world.Position, direction, Node.Distance, out var hit, Node.Layers, world.Self))
                    Set(hit.Material, hit.Distance, true);
                else
                    Set(null, Node.Distance, false);
            }

            public void Set(SurfaceMaterial material, float distance, bool hit)
            {
                Volatile.Write(ref Targets[0], hit ? 1f : 0f);
                Volatile.Write(ref Targets[1], distance);
                Volatile.Write(ref Targets[2], material != null ? material.Wetness : 0f);
                Volatile.Write(ref Targets[3], material != null ? material.Absorption : 0f);
                Volatile.Write(ref Targets[4], material != null ? material.Hardness : 0f);
                Volatile.Write(ref Targets[5], material != null ? material.Friction : 0f);
                Volatile.Write(ref Targets[6], material != null && !string.IsNullOrEmpty(Node.Property) ? material.Get(Node.Property) : 0f);
                Volatile.Write(ref Targets[7], hit && material != null && material == Node.Is ? 1f : 0f);
            }
        }
    }
    [SchematicDoc(@"{""name"": ""Room"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""The Sound Room the sound is in: In Room (1 inside one, 0 outside, gliding over Smoothing seconds as it moves in or out), the room's Size in meters, and one of its Properties by name (Value). The room's shared effects are its mixer group; use this for what changes per sound, like a Reverb's Mix or a Delay's Feedback."", ""outputs"": [{""name"": ""In Room"", ""type"": ""float""}, {""name"": ""Size"", ""type"": ""float""}, {""name"": ""Value"", ""type"": ""float""}], ""use_for"": [""more echo in big rooms"", ""a cave's damp, a hall's ring""], ""notes"": ""The editor's preview has no world: it uses the Preview values.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Room", Path = "Controls"), Tags(SoundNode.Tag)]
    public class SoundRoomNode : SoundNode
    {
        [Output("In Room")] public float InRoom;
        [Output("Size")] public float Size;
        [Output("Value")] public float Value;

        [Editable, Tooltip("A room Property for Value.")] public string Property = "";
        [Editable, Tooltip("Seconds to glide to new values.")] public float Smoothing = 0.3f;
        [Editable, Tooltip("Whether the editor's preview is in a room.")] public bool PreviewInRoom;
        [Editable, Tooltip("The room Size the editor's preview uses.")] public float PreviewSize = 10f;
        [Editable, Tooltip("The Value the editor's preview uses.")] public float PreviewValue;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var probe = new Probe { Node = this, Targets = new float[3] };
            builder.Context.Probes.Add(probe);
            return new ProbedProcessor { Ports = new[] { "In Room", "Size", "Value" }, Targets = probe.Targets, Smoothing = Smoothing };
        }

        private sealed class Probe : SoundProbe
        {
            public SoundRoomNode Node;
            public float[] Targets;

            public override void Update(in SoundProbeWorld world)
            {
                bool inRoom = world.Previewing ? Node.PreviewInRoom : world.Room != null;
                float size = !inRoom ? 0f : world.Previewing ? Node.PreviewSize : world.Room.WorldSize;
                float value = !inRoom ? 0f : world.Previewing ? Node.PreviewValue
                    : string.IsNullOrEmpty(Node.Property) ? 0f : world.Room.Get(Node.Property);
                Volatile.Write(ref Targets[0], inRoom ? 1f : 0f);
                Volatile.Write(ref Targets[1], size);
                Volatile.Write(ref Targets[2], value);
            }
        }
    }
}
