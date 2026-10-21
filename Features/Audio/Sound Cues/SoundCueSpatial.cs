using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    // What Spatial effects (Reverb, Delay) share: the world side, which asks the Sound Field what the place is like and
    // where else the sound goes (the room it's in, the echo walls it can be heard by), and the audio side, which sends
    // the sound there, stamped with when it's heard.

    /// <summary>
    /// The world side of a Spatial effect (main thread, run by the player about 10 times a second): the place where the
    /// sound is (from its <see cref="SoundField"/>), and where its sound also goes -- the ringing room it's in (with
    /// <see cref="UseRooms"/>), and the echo walls its place can see, each as late as sound takes to get there and on
    /// to the listener. The effect turns the place into its own settings (<see cref="Shape"/>).
    /// </summary>
    internal abstract class SpatialProbe : SoundProbe
    {
        /// <summary>Where the sound also goes, read by the effect's <see cref="SpatialSender"/> (audio thread).</summary>
        public SpaceSends Sends;
        /// <summary>The effect's settings the place makes, glided between by a <see cref="ProbedProcessor"/>.</summary>
        public float[] Targets;

        /// <summary>Send to the ringing room the sound is in (then no echo walls).</summary>
        protected abstract bool UseRooms { get; }
        /// <summary>How much echo walls throw the sound back, 0 to 1.</summary>
        protected abstract float EchoAmount { get; }

        /// <summary>The effect's settings for a place (<paramref name="inRoom"/>: a room is ringing with the sound).</summary>
        protected abstract void Shape(in SoundAcoustics place, bool inRoom);
        /// <summary>The effect's settings with no Sound Field (or in the editor's preview): its own typed-in ones.</summary>
        public abstract void Fallback();

        public override void Update(in SoundProbeWorld world)
        {
            if (world.Previewing || !SoundField.Sample(world.Position, out var place))
            {
                Volatile.Write(ref Sends, null);
                Fallback();
                return;
            }
            SpaceSends sends = null;
            bool inRoom = false;
            var spaces = SoundSpaces.Get();
            if (spaces != null)
            {
                var room = UseRooms ? spaces.RoomAt(world.Position) : null;
                if (room != null)
                {
                    // In a room: the room rings (and keeps ringing after the sound's gone). The sound only hands its own
                    // reverb over once the room's is playing and taking its sends in time; until then it keeps both.
                    sends = new SpaceSends { Room = room.Bus, RoomGain = room.Acoustics.Wet * SoundSpaces.Exaggerate };
                    inRoom = room.Bus != null && room.Bus.Healthy;
                }
                else if (EchoAmount > 0f && SoundField.TryGetCell(world.Position, out var field, out int index) && field.Data.TryGet(index, out var cell))
                {
                    sends = new SpaceSends();
                    Echo(spaces, field, cell.AnchorA, world, ref sends.EchoA, ref sends.DelayA, ref sends.GainA);
                    Echo(spaces, field, cell.AnchorB, world, ref sends.EchoB, ref sends.DelayB, ref sends.GainB);
                }
            }
            Volatile.Write(ref Sends, sends);
            Shape(place, inRoom);
            // Testing (the Sound Spaces window's Exaggerate): the effect's own amount too (index 2 is it, in both).
            if (SoundSpaces.Exaggerate != 1f)
                Set(WetIndex, Mathf.Clamp01(Volatile.Read(ref Targets[WetIndex]) * SoundSpaces.Exaggerate));
        }

        /// <summary>Which of the targets is the effect's own amount (Exaggerate scales it).</summary>
        protected const int WetIndex = 2;

        // An echo wall the place can see: its bus (if it's one of the live ones), the path's delay and how much it
        // throws back (more for reflective walls, less the farther the sound is from it).
        private void Echo(SoundSpaces spaces, SoundField field, ushort anchor, in SoundProbeWorld world, ref SpaceBus bus, ref int delay, ref float gain)
        {
            if (anchor == SoundFieldData.NoAnchor || anchor >= field.Data.Anchors.Count)
                return;
            var data = field.Data.Anchors[anchor];
            var wall = field.transform.TransformPoint(data.Position);
            float toWall = Vector3.Distance(world.Position, wall), toListener = Vector3.Distance(wall, world.ListenerPosition);
            // An echo arriving soon after the sound itself is heard as the same sound doubled, out of step (under ~50 ms
            // the ear merges it in anyway, and the reverb covers it): none until 50 ms behind, fading in until 100 ms.
            float lag = (toWall + toListener - Vector3.Distance(world.Position, world.ListenerPosition)) / SoundSpaces.SpeedOfSound;
            float gate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EchoGateStart, EchoGateFull, lag));
            if (gate <= 0f)
                return;
            bus = spaces.RequestEcho(field, anchor);
            if (bus == null)
                return;
            delay = (int)((toWall + toListener) / SoundSpaces.SpeedOfSound * spaces.Rate);
            gain = data.Reflectivity / (1f + toWall / 25f) * Mathf.Clamp01(EchoAmount) * gate * SoundSpaces.Exaggerate;
        }

        /// <summary>How far behind the sound itself an echo starts being heard, and is heard fully, in seconds.</summary>
        public const float EchoGateStart = 0.05f, EchoGateFull = 0.1f;

        protected void Set(int index, float value) => Volatile.Write(ref Targets[index], value);
    }

    /// <summary>
    /// The audio side of a Spatial effect: sends each block of the sound to its probe's room and echo walls, at the
    /// audio-clock sample it's heard at (plus each wall's path delay). Moving to another room or wall fades the old one
    /// out as the new one fades in; a drifting delay glides (a slight, natural doppler).
    /// </summary>
    internal sealed class SpatialSender
    {
        private readonly int _rate;
        private SpaceBus _room, _echoA, _echoB;
        private float _roomGain, _gainA, _gainB;
        private int _roomDelay, _delayA, _delayB;

        public SpatialSender(int sampleRate) => _rate = sampleRate;

        public void Reset()
        {
            _room = _echoA = _echoB = null;
            _roomGain = _gainA = _gainB = 0f;
        }

        /// <summary>Sends <paramref name="sound"/>'s block (Mix times each send's own amount).</summary>
        public void Send(SpatialProbe probe, SoundContext context, float[] sound, int count, float mix)
        {
            if (probe == null || context.HeardStart < 0 || count <= 0)
                return;
            var sends = Volatile.Read(ref probe.Sends);
            long at = context.HeardSample(context.SamplePosition);
            SendTo(ref _room, ref _roomGain, ref _roomDelay, sends?.Room, (sends?.RoomGain ?? 0f) * mix, 0, sound, at, count);
            SendTo(ref _echoA, ref _gainA, ref _delayA, sends?.EchoA, (sends?.GainA ?? 0f) * mix, sends?.DelayA ?? 0, sound, at, count);
            SendTo(ref _echoB, ref _gainB, ref _delayB, sends?.EchoB, (sends?.GainB ?? 0f) * mix, sends?.DelayB ?? 0, sound, at, count);
        }

        private void SendTo(ref SpaceBus current, ref float gain, ref int delay, SpaceBus target, float targetGain, int targetDelay, float[] sound, long at, int count)
        {
            if (target != current)
            {
                if (current != null && gain > 0f)
                    current.Add(at + delay, sound, count, gain, 0f);
                current = target;
                delay = targetDelay;
                if (current != null)
                    current.Add(at + delay, sound, count, 0f, targetGain);
                gain = current != null ? targetGain : 0f;
                return;
            }
            if (current == null)
                return;
            int difference = targetDelay - delay;
            delay = Math.Abs(difference) > _rate / 20 ? targetDelay : delay + Mathf.Clamp(difference, -16, 16);
            current.Add(at + delay, sound, count, gain, targetGain);
            gain = targetGain;
        }
    }
}
