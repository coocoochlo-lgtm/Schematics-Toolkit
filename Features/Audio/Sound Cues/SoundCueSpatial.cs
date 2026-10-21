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
        /// <summary>
        /// Trails before this effect: their spots (where the sound is now) go on to this place's effects too, so a trail
        /// left behind also rings the room and comes back off the walls. Null: none.
        /// </summary>
        public TrailProbe[] Trails;
        /// <summary>The effect's Mix, from the audio thread (how much a Trail's spots send on).</summary>
        public float SendMix = 1f;

        /// <summary>Send to the ringing room the sound is in (then no echo walls).</summary>
        protected abstract bool UseRooms { get; }
        /// <summary>How much echo walls throw the sound back, 0 to 1.</summary>
        protected abstract float EchoAmount { get; }

        /// <summary>
        /// The effect's settings for a place. <paramref name="elsewhere"/>: a room or a tail spot is making the effect
        /// for the sound (so it stays where it happens), and the sound's own should fade out.
        /// </summary>
        protected abstract void Shape(in SoundAcoustics place, bool elsewhere);

        /// <summary>
        /// The tail spot for the effect where the sound is (a reverb or a slapback left there, from the pool), or null
        /// when the place makes none; and how much the sound feeds it.
        /// </summary>
        protected abstract SpaceBus Tail(TailPool pool, Vector3 position, in SoundAcoustics place, out float amount);

        // Outside rooms: the effect is made at a spot where the sound is, which stays there (and rings out there) when
        // the sound moves on. The sound's own effect only fades once the spot is playing and taking its sends in time.
        private bool AddTail(TailPool pool, Vector3 position, in SoundAcoustics place, ref SpaceSends sends)
        {
            if (pool == null)
                return false;
            var tail = Tail(pool, position, place, out float amount);
            if (tail == null)
                return false;
            sends ??= new SpaceSends();
            sends.Tail = tail;
            sends.TailGain = amount * SoundSpaces.Exaggerate;
            return tail.Healthy;
        }
        /// <summary>The effect's settings with no Sound Field (or in the editor's preview): its own typed-in ones.</summary>
        public abstract void Fallback();

        public override void Update(in SoundProbeWorld world)
        {
            if (world.Previewing)
            {
                Simulated(world);
                return;
            }
            // Outside every Sound Field: open air (nearly dry), the same as open ground inside one.
            if (!SoundField.Sample(world.Position, out var place))
                place = SoundAcoustics.OpenAir;
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
                if (!inRoom && AddTail(spaces.Tails, world.Position, place, ref sends))
                    inRoom = true;
            }
            Volatile.Write(ref Sends, sends);
            ForwardTrails(sends);
            Shape(place, inRoom);
            // Testing (the Sound Spaces window's Exaggerate): the effect's own amount too (index 2 is it, in both).
            if (SoundSpaces.Exaggerate != 1f)
                Set(WetIndex, Mathf.Clamp01(Volatile.Read(ref Targets[WetIndex]) * SoundSpaces.Exaggerate));
        }

        /// <summary>Which of the targets is the effect's own amount (Exaggerate scales it).</summary>
        protected const int WetIndex = 2;

        private readonly SpaceBus[] _simBuses = new SpaceBus[2];
        private readonly float[] _simPaths = new float[2], _simReflectivity = new float[2];

        // The editor's preview: the made-up surroundings picked in its bar (SoundSpaceSimulation), if any.
        private void Simulated(in SoundProbeWorld world)
        {
            var simulation = SoundSpaceSimulation.Current;
            if (simulation == null || !simulation.Sample(world.Position, out var place))
            {
                Volatile.Write(ref Sends, null);
                ForwardTrails(null);
                Fallback();
                return;
            }
            SpaceSends sends = null;
            bool inRoom = false;
            float roomWet = 0f;
            var room = UseRooms ? simulation.RoomAt(world.Position, out roomWet) : null;
            if (room != null)
            {
                sends = new SpaceSends { Room = room, RoomGain = roomWet * SoundSpaces.Exaggerate };
                inRoom = room.Healthy;
            }
            else if (EchoAmount > 0f)
            {
                int count = simulation.Echoes(world.Position, world.ListenerPosition, _simBuses, _simPaths, _simReflectivity);
                float direct = Vector3.Distance(world.Position, world.ListenerPosition);
                for (int i = 0; i < count; i++)
                {
                    float gate = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EchoGateStart, EchoGateFull, (_simPaths[i] - direct) / SoundSpaces.SpeedOfSound));
                    if (gate <= 0f) continue;
                    sends ??= new SpaceSends();
                    int delay = (int)(_simPaths[i] / SoundSpaces.SpeedOfSound * simulation.Rate);
                    float gain = _simReflectivity[i] / (1f + _simPaths[i] / 50f) * Mathf.Clamp01(EchoAmount) * gate * SoundSpaces.Exaggerate;
                    if (sends.EchoA == null) { sends.EchoA = _simBuses[i]; sends.DelayA = delay; sends.GainA = gain; }
                    else { sends.EchoB = _simBuses[i]; sends.DelayB = delay; sends.GainB = gain; }
                }
            }
            if (!inRoom && AddTail(simulation.Tails, world.Position, place, ref sends))
                inRoom = true;
            Volatile.Write(ref Sends, sends);
            ForwardTrails(sends);
            Shape(place, inRoom);
            if (SoundSpaces.Exaggerate != 1f)
                Set(WetIndex, Mathf.Clamp01(Volatile.Read(ref Targets[WetIndex]) * SoundSpaces.Exaggerate));
        }

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

        // The Trails' current spots send on to where this sound goes (its room, echo walls and tail spot), times Mix.
        private void ForwardTrails(SpaceSends sends)
        {
            if (Trails == null)
                return;
            float mix = Mathf.Clamp01(Volatile.Read(ref SendMix));
            foreach (var trail in Trails)
                trail.Spot?.SetForward(sends, mix);
        }

        /// <summary>How far behind the sound itself an echo starts being heard, and is heard fully, in seconds.</summary>
        public const float EchoGateStart = 0.05f, EchoGateFull = 0.1f;

        protected void Set(int index, float value) => Volatile.Write(ref Targets[index], value);
    }

    /// <summary>
    /// Made-up surroundings for the editor's preview (which has no world): what the place is like, the room ringing
    /// around a point, and the walls a sound echoes off -- each a fixed source, so echoes and tails stay where they are
    /// while the previewed sound moves. Set by the preview while it plays; positions are around its imaginary listener.
    /// </summary>
    public abstract class SoundSpaceSimulation
    {
        /// <summary>The surroundings the preview is playing in (null: none, the effects use their own settings).</summary>
        public static SoundSpaceSimulation Current;

        public abstract int Rate { get; }
        /// <summary>What the place at a position is like (false: no surroundings there).</summary>
        public abstract bool Sample(Vector3 position, out SoundAcoustics place);
        /// <summary>The ringing room a position is in, and how much it rings (null: in none).</summary>
        public abstract SpaceBus RoomAt(Vector3 position, out float wet);
        /// <summary>
        /// Up to two walls that throw a sound at <paramref name="position"/> back to the listener: their sources, the
        /// length of the reflected path (sound, wall, listener) and how reflective they are. Returns how many.
        /// </summary>
        public abstract int Echoes(Vector3 position, Vector3 listener, SpaceBus[] buses, float[] paths, float[] reflectivity);
        /// <summary>Reverb and slapback tails left where the previewed sound has been.</summary>
        public abstract TailPool Tails { get; }
    }

    /// <summary>
    /// The audio side of a Spatial effect: sends each block of the sound to its probe's room and echo walls, at the
    /// audio-clock sample it's heard at (plus each wall's path delay). Moving to another room or wall fades the old one
    /// out as the new one fades in; a drifting delay glides (a slight, natural doppler).
    /// </summary>
    internal sealed class SpatialSender
    {
        private readonly int _rate;
        private SpaceBus _room, _echoA, _echoB, _tail;
        private float _roomGain, _gainA, _gainB, _tailGain;
        private int _roomDelay, _delayA, _delayB, _tailDelay;

        public SpatialSender(int sampleRate) => _rate = sampleRate;

        public void Reset()
        {
            _room = _echoA = _echoB = _tail = null;
            _roomGain = _gainA = _gainB = _tailGain = 0f;
        }

        /// <summary>Sends <paramref name="sound"/>'s block (Mix times each send's own amount).</summary>
        public void Send(SpatialProbe probe, SoundContext context, float[] sound, int count, float mix)
        {
            if (probe == null)
                return;
            Volatile.Write(ref probe.SendMix, mix);
            Send(Volatile.Read(ref probe.Sends), context, sound, count, mix);
        }

        /// <summary>Sends <paramref name="sound"/>'s block to <paramref name="sends"/> (Mix times each send's own amount).</summary>
        public void Send(SpaceSends sends, SoundContext context, float[] sound, int count, float mix)
        {
            if (context.HeardStart < 0 || count <= 0)
                return;
            long at = context.HeardSample(context.SamplePosition);
            SendTo(ref _room, ref _roomGain, ref _roomDelay, sends?.Room, (sends?.RoomGain ?? 0f) * mix, 0, sound, at, count);
            SendTo(ref _tail, ref _tailGain, ref _tailDelay, sends?.Tail, (sends?.TailGain ?? 0f) * mix, 0, sound, at, count);
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

    /// <summary>
    /// The world side of a Trail (Reverb or Delay in Trail mode; main thread, with the other probes): the spot where the
    /// sound is now, from the game's tail pool or the preview's. Every <see cref="Spacing"/> meters the sound moves it
    /// takes a new one, and the last is left ringing where it was, so the effect trails behind the sound.
    /// </summary>
    internal sealed class TrailProbe : SoundProbe
    {
        /// <summary>Room: a reverb trail. Slap: an echo trail.</summary>
        public SpaceBus.BusKind Kind;
        public float Spacing = 1f;
        public int MaxSpots = 16;
        /// <summary>The node's settings, from the audio thread: reverb feedback and damping, or delay (seconds) and feedback.</summary>
        public float A, B;
        /// <summary>The spot to send to (as its Tail), read by the node's processor.</summary>
        public SpaceSends Sends;

        /// <summary>The spot where the sound is now (null: none: the node runs its own effect instead).</summary>
        public SpaceBus Spot => Volatile.Read(ref Sends)?.Tail;

        public override void Update(in SoundProbeWorld world)
        {
            var pool = world.Previewing ? SoundSpaceSimulation.Current?.Tails : SoundSpaces.Get()?.Tails;
            SpaceBus spot = null;
            if (pool != null)
            {
                float a = Volatile.Read(ref A), b = Volatile.Read(ref B);
                spot = Kind == SpaceBus.BusKind.Slap
                    ? pool.Slap(world.Position, a, b, 0f, this, Spacing, MaxSpots)
                    : pool.Reverb(world.Position, a, b, RingsFor(a), this, Spacing, MaxSpots);
            }
            if (spot != Spot)
                Volatile.Write(ref Sends, spot != null ? new SpaceSends { Tail = spot, TailGain = 1f } : null);
        }

        // How long a reverb with this feedback takes to die away (its combs average about 31 ms).
        private static float RingsFor(float feedback)
            => Mathf.Clamp(3f * 0.0308f / Mathf.Max(1e-4f, -Mathf.Log10(Mathf.Clamp(feedback, 0.01f, 0.995f))), 0.1f, 20f);

        /// <summary>The Trails among processors (a Spatial effect's upstream), or null when there are none.</summary>
        public static TrailProbe[] Among(System.Collections.Generic.List<SoundProcessor> processors)
        {
            System.Collections.Generic.List<TrailProbe> found = null;
            foreach (var processor in processors)
                if (processor is ITrailed trailed && trailed.Trail != null)
                    (found ??= new()).Add(trailed.Trail);
            return found?.ToArray();
        }
    }

    /// <summary>A processor that can leave a trail (a Reverb or Delay in Trail mode).</summary>
    internal interface ITrailed
    {
        TrailProbe Trail { get; }
    }
}
