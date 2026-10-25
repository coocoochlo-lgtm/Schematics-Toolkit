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
        /// Trails linked to this effect (before or after it in the cue): each spot they leave goes on to this place's
        /// effects too, from where it is, so a trail left behind also rings the room and comes back off the walls. Null:
        /// none.
        /// </summary>
        public System.Collections.Generic.List<TrailProbe> Trails;
        /// <summary>The effect's Mix, from the audio thread (how much a Trail's spots send on).</summary>
        public float SendMix = 1f;
        /// <summary>
        /// Whether the voice's sends can be placed in time (it knows when it's heard), from the audio thread. Until
        /// they can, the effect never hands its own reverb or echo over to a room or tail spot: it would just go quiet.
        /// </summary>
        public int Sending;

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
            SoundAcoustics place;
            TailPool tails;
            if (world.Simulated)
            {
                // The editor's preview: the made-up surroundings picked in its bar (SoundSpaceSimulation), if any.
                var simulation = SoundSpaceSimulation.Current;
                if (simulation == null || !simulation.Sample(world.Position, out place))
                {
                    Volatile.Write(ref Sends, null);
                    ForwardTrails(world, previewing: true, active: false);
                    Fallback();
                    return;
                }
                tails = simulation.Tails;
            }
            else
            {
                // Outside every Sound Field: open air (nearly dry), the same as open ground inside one.
                if (!SoundField.Sample(world.Position, out place))
                    place = SoundAcoustics.OpenAir;
                tails = SoundSpaces.Get()?.Tails;
            }
            _hearing = world.Hearing;
            var sends = SendsFrom(world.Position, world.ListenerPosition, world.Simulated, out bool inRoom);
            if (!inRoom && AddTail(tails, world.Position, place, ref sends))
                inRoom = true;
            if (Volatile.Read(ref Sending) == 0)
                inRoom = false;
            Volatile.Write(ref Sends, sends);
            ForwardTrails(world, world.Simulated, active: true);
            Shape(place, inRoom);
            // Testing (the Sound Spaces window's Exaggerate): the effect's own amount too (index 2 is it, in both).
            if (SoundSpaces.Exaggerate != 1f)
                Set(WetIndex, Mathf.Clamp01(Volatile.Read(ref Targets[WetIndex]) * SoundSpaces.Exaggerate));
        }

        /// <summary>Which of the targets is the effect's own amount (Exaggerate scales it).</summary>
        protected const int WetIndex = 2;

        private readonly SpaceBus[] _simBuses = new SpaceBus[2];
        private readonly float[] _simPaths = new float[2], _simReflectivity = new float[2], _simHardness = new float[2];

        /// <summary>
        /// Where a sound at <paramref name="position"/> also goes: the ringing room it's in (then <paramref name="inRoom"/>
        /// says whether the room is playing in time to take over its reverb), and the echo walls that throw it back to
        /// the listener, each as late as the path there and on. In the game from the Sound Fields, in the preview from
        /// its surroundings. Null: nowhere.
        /// </summary>
        private SpaceSends SendsFrom(Vector3 position, Vector3 listener, bool previewing, out bool inRoom)
        {
            bool log = SoundSpaces.LogEchoes && !previewing && Time.realtimeSinceStartup >= _nextEchoLog;
            if (log)
            {
                _nextEchoLog = Time.realtimeSinceStartup + 0.5f;
                _echoLog ??= new System.Text.StringBuilder();
                _echoLog.Clear();
            }
            _logging = log;
            inRoom = false;
            if (previewing)
            {
                var simulation = SoundSpaceSimulation.Current;
                if (simulation == null)
                    return null;
                float roomWet = 0f;
                var room = UseRooms ? simulation.RoomAt(position, out roomWet) : null;
                SpaceSends sends = null;
                if (room != null)
                {
                    inRoom = room.Healthy;
                    sends = new SpaceSends { Room = room, RoomGain = roomWet * SoundSpaces.Exaggerate };
                }
                if (EchoAmount <= 0f)
                    return sends;
                int count = simulation.Echoes(position, listener, _simBuses, _simPaths, _simReflectivity, _simHardness);
                float direct = Vector3.Distance(position, listener);
                for (int i = 0; i < count; i++)
                {
                    float gate = Gate((_simPaths[i] - direct) / SoundSpaces.SpeedOfSound, _simReflectivity[i], _simHardness[i]);
                    if (gate <= 0f) continue;
                    sends ??= new SpaceSends();
                    // Behind the sound itself by the extra way round (the sound is heard with no travel time of its own).
                    int delay = (int)((_simPaths[i] - direct) / SoundSpaces.SpeedOfSound * simulation.Rate);
                    float gain = _simReflectivity[i] / (1f + _simPaths[i] / 50f) * Mathf.Clamp01(EchoAmount) * gate * SoundSpaces.Exaggerate;
                    sends.AddEcho(_simBuses[i], delay, gain);
                }
                return sends;
            }
            var spaces = SoundSpaces.Get();
            if (spaces == null)
                return null;
            SpaceSends found = null;
            var acoustic = UseRooms ? spaces.RoomAt(position) : null;
            if (acoustic != null)
            {
                // In a room: the room rings (and keeps ringing after the sound's gone). The sound only hands its own
                // reverb over once the room's is playing and taking its sends in time; until then it keeps both.
                inRoom = acoustic.Bus != null && acoustic.Bus.Healthy;
                found = new SpaceSends { Room = acoustic.Bus, RoomGain = acoustic.Acoustics.Wet * SoundSpaces.Exaggerate };
            }
            // Echo walls too, in a room or out of one: every wall the place hears (one each way: the sides, the floor,
            // the ceiling), in a fixed order so each keeps its send.
            if (EchoAmount > 0f && SoundField.TryGetCell(position, out var field, out int index) && field.Data.IsOpen(index))
            {
                int walls = field.Data.WallsOf(index, _walls);
                if (walls > 0)
                {
                    found ??= new SpaceSends();
                    Array.Sort(_walls, 0, walls);
                    for (int w = 0; w < walls; w++)
                        Echo(spaces, field, _walls[w], position, listener, found);
                }
            }
            if (log)
            {
                if (_echoLog.Length == 0)
                    _echoLog.Append("  no echo walls where the sound is");
                Debug.Log($"[Echoes] {GetType().DeclaringType?.Name ?? GetType().Name} at {position:F1}, listener {listener:F1} ({Vector3.Distance(position, listener):0.0} m):\n{_echoLog}");
            }
            _logging = false;
            return found;
        }

        private readonly ushort[] _walls = new ushort[SoundFieldData.WallsPerCell];
        // The voice's own falloff (its volume and distance falloff at a distance; see SoundProbeWorld.Hearing).
        private Func<float, float> _hearing;
        // Log Echoes (Sound Spaces window): this update's lines, and when next.
        private System.Text.StringBuilder _echoLog;
        private float _nextEchoLog;
        private bool _logging;

        // An echo is only heard as one once it comes far enough behind the sound itself: sooner, the ear takes it as
        // part of the sound (a sound on the floor, or right by a wall, has no echo off it). Silent until EchoGateStart
        // behind, fully heard by EchoGateFull, whatever the wall (soft ones are just quieter).
        private static float Gate(float lag, float reflectivity, float hardness)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EchoGateStart, EchoGateFull, lag));

        // An echo wall the place hears: sound bounces off it where a mirror would show the listener (kept on the wall),
        // so it comes from there, as late as that way round is longer than the way straight to the listener, and as
        // loud as the sound itself would be that far away (the whole way: to the wall and on to the listener, by the
        // voice's own falloff) times what the wall throws back, fading where the true bounce would fall past the wall's
        // edge. Its bus is one of the live ones (asked for here; it plays from that way round the listener, with no
        // falloff of its own, so nearing the wall doesn't make it louder).
        private void Echo(SoundSpaces spaces, SoundField field, ushort anchor, Vector3 position, Vector3 listener, SpaceSends sends)
        {
            if (anchor == SoundFieldData.NoAnchor || anchor >= field.Data.Anchors.Count)
                return;
            var data = field.Data.Anchors[anchor];
            var toLocal = field.transform.worldToLocalMatrix;
            if (!SoundFieldData.Reflection(data, toLocal.MultiplyPoint3x4(position), toLocal.MultiplyPoint3x4(listener), out var bounce, out float outside))
            {
                if (_logging) _echoLog.Append($"  wall {anchor}: no bounce towards the listener (behind it)\n");
                return;
            }
            var wall = field.transform.TransformPoint(bounce);
            float toWall = Vector3.Distance(position, wall), toListener = Vector3.Distance(wall, listener);
            float path = toWall + toListener;
            float extra = path - Vector3.Distance(position, listener);
            float gate = Gate(extra / SoundSpaces.SpeedOfSound, data.Reflectivity, data.Hardness);
            float edge = 1f / (1f + outside * outside * 0.25f);
            float heard = _hearing != null ? _hearing(path) : 1f / Mathf.Max(1f, path);
            float gain = data.Reflectivity * heard * Mathf.Clamp01(EchoAmount) * gate * edge * SoundSpaces.Exaggerate;
            var bus = gate > 0f ? spaces.RequestEcho(field, anchor, wall, gain) : null;
            if (_logging)
            {
                int joined = bus != null ? spaces.JoinedTo(bus) : 0;
                _echoLog.Append($"  wall {anchor}: {toWall:0.0} + {toListener:0.0} = {path:0.0} m ({extra / SoundSpaces.SpeedOfSound * 1000f:0} ms late), "
                    + $"falloff {heard:0.000} x reflects {data.Reflectivity:0.00} x gate {gate:0.00} x edge {edge:0.00} = {gain:0.0000} ({20f * Mathf.Log10(Mathf.Max(gain, 1e-6f)):0.0} dB) -> "
                    + (gate <= 0f ? "gated out (too soon after the sound)" : bus == null ? "NO BUS (none free, or out of voices)"
                        : $"bus #{bus.GetHashCode()}{(Equals(bus.Owner, (field, (int)anchor)) ? "" : " (joined to another wall's)")}{(joined > 0 ? $", {joined} joined" : "")}")
                    + "\n");
            }
            if (bus == null)
                return;
            // Behind the sound itself by the extra way round: Unity plays the sound with no travel time, so an echo
            // delayed by its whole path came later and later after it the farther away the sound was (a muddy smear).
            sends.AddEcho(bus, (int)(extra / SoundSpaces.SpeedOfSound * spaces.Rate), gain);
        }

        private readonly System.Collections.Generic.List<SpaceBus> _spots = new();

        // Each spot a linked Trail has left is a sound of its own where it is: it goes on to the room it's in, or the
        // echo walls that throw it back from there (each with its own path and delay), times this effect's Mix.
        private void ForwardTrails(in SoundProbeWorld world, bool previewing, bool active)
        {
            if (Trails == null)
                return;
            float mix = Mathf.Clamp01(Volatile.Read(ref SendMix));
            foreach (var trail in Trails)
            {
                trail.Spots(_spots);
                foreach (var spot in _spots)
                {
                    var sends = active ? SendsFrom(((TailPool.Spot)spot.Owner).Position, world.ListenerPosition, previewing, out _) : null;
                    spot.SetForward(sends, mix);
                }
            }
        }

        /// <summary>How far behind the sound itself an echo starts being heard, in seconds (sooner, it's part of the sound).</summary>
        public const float EchoGateStart = 0.005f;
        /// <summary>How far behind the sound itself an echo is heard fully, in seconds.</summary>
        public const float EchoGateFull = 0.025f;

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
        /// length of the reflected path (sound, wall, listener), and how reflective and hard they are. Returns how many.
        /// </summary>
        public abstract int Echoes(Vector3 position, Vector3 listener, SpaceBus[] buses, float[] paths, float[] reflectivity, float[] hardness);
        /// <summary>Reverb and slapback tails left where the previewed sound has been.</summary>
        public abstract TailPool Tails { get; }
        /// <summary>Sounds coming round corners (Around Corners).</summary>
        public abstract CornerPool Corners { get; }

        /// <summary>
        /// How a sound reaches the listener round the walls: like <see cref="SoundPaths.Around"/> in the game.
        /// </summary>
        public abstract SoundRoute Around(Vector3 source, Vector3 listener, out Vector3 heardFrom, out float length, out float bend);

        /// <summary>The last corner a previewed sound came round, and when (for drawing it).</summary>
        public Vector3 LastCorner;
        public double LastCornerAt = -1;
    }

    /// <summary>
    /// The audio side of a Spatial effect: sends each block of the sound to its probe's room and echo walls, at the
    /// audio-clock sample it's heard at (plus each wall's path delay). Moving to another room or wall fades the old one
    /// out as the new one fades in; a drifting delay glides (a slight, natural doppler).
    /// </summary>
    internal sealed class SpatialSender
    {
        /// <summary>
        /// A Travel Time after the effect: the sound is heard later by its travel time, so everything sent is too
        /// (null: none, or it's before the effect and what the effect hears is already late).
        /// </summary>
        public TravelDelay Travel;
        /// <summary>
        /// Seconds a send takes to fade in when the sound starts going somewhere new (another room, a tail spot, other
        /// echo walls), and the old one to fade out meanwhile: the place changes over this long rather than at once.
        /// 0: within one block (a sharp change, as from a street into a tunnel).
        /// </summary>
        public float Transition;
        private readonly int _rate;
        private SpaceBus _room, _tail, _corner;
        private float _roomGain, _tailGain, _cornerGain;
        // How far each send has faded in since it started (0 to 1; 1 at once with no Transition).
        private float _roomIn = 1f, _tailIn = 1f, _cornerIn = 1f;
        private int _roomDelay, _tailDelay, _cornerDelay;
        // One per echo wall slot (SpaceSends.Echoes).
        private readonly SpaceBus[] _echoes = new SpaceBus[SpaceSends.MaxEchoes];
        private readonly float[] _echoGains = new float[SpaceSends.MaxEchoes];
        private readonly float[] _echoIn = new float[SpaceSends.MaxEchoes];
        private readonly int[] _echoDelays = new int[SpaceSends.MaxEchoes];

        // Sends the sound has moved on from, fading out over Transition (the oldest goes at once when it's full).
        private struct Leaving
        {
            public SpaceBus Bus;
            public float Gain, PerSample;
            public int Delay;
        }
        private const int MaxLeaving = 24;
        private readonly Leaving[] _leaving = new Leaving[MaxLeaving];
        private int _leavingCount;

        public SpatialSender(int sampleRate) => _rate = sampleRate;

        public void Reset()
        {
            _room = _tail = _corner = null;
            _roomGain = _tailGain = _cornerGain = 0f;
            _roomIn = _tailIn = _cornerIn = 1f;
            for (int e = 0; e < _echoes.Length; e++)
            {
                _echoes[e] = null;
                _echoGains[e] = 0f;
                _echoIn[e] = 1f;
            }
            for (int i = 0; i < _leavingCount; i++)
                _leaving[i].Bus = null;
            _leavingCount = 0;
        }

        /// <summary>
        /// Sends <paramref name="sound"/>'s block (Mix times each send's own amount). <paramref name="duck"/>: the gain
        /// (1 none) the room, echo walls and tail spot it reaches are turned down by while this block is heard (see
        /// <see cref="SourceDucker"/>).
        /// </summary>
        public void Send(SpatialProbe probe, SoundContext context, float[] sound, int count, float mix, float duck = 1f)
        {
            if (probe == null)
                return;
            Volatile.Write(ref probe.SendMix, mix);
            Volatile.Write(ref probe.Sending, context.HeardStart >= 0 ? 1 : 0);
            Send(Volatile.Read(ref probe.Sends), context, sound, count, mix, duck);
        }

        /// <summary>Sends <paramref name="sound"/>'s block to <paramref name="sends"/> (Mix times each send's own amount; <paramref name="duck"/> as above).</summary>
        public void Send(SpaceSends sends, SoundContext context, float[] sound, int count, float mix, float duck = 1f)
        {
            if (context.HeardStart < 0 || count <= 0)
                return;
            long at = context.HeardSample(context.SamplePosition) + context.SendLead + (Travel != null ? Volatile.Read(ref Travel.Samples) : 0);
            if (sends != null && duck < 0.999f)
            {
                // Under the sound as it's heard (not its echoes' delays: they're what's ducked). Not a corner: that's
                // the sound itself, coming round.
                float amount = 1f - duck;
                sends.Room?.Duck(at, count, amount);
                sends.Tail?.Duck(at, count, amount);
                for (int e = 0; e < sends.EchoCount; e++)
                    sends.Echoes[e]?.Duck(at, count, amount);
                // (And the places it's leaving, while they still carry it.)
                for (int i = 0; i < _leavingCount; i++)
                    _leaving[i].Bus?.Duck(at, count, amount);
            }
            // (Fading in by this much more this block.)
            float fadeIn = Transition > 0.001f ? count / (Transition * _rate) : 1f;
            FadeOutLeaving(sound, at, count);
            SendTo(ref _room, ref _roomGain, ref _roomIn, ref _roomDelay, sends?.Room, (sends?.RoomGain ?? 0f) * mix, 0, sound, at, count, fadeIn);
            SendTo(ref _tail, ref _tailGain, ref _tailIn, ref _tailDelay, sends?.Tail, (sends?.TailGain ?? 0f) * mix, 0, sound, at, count, fadeIn);
            int echoes = sends?.EchoCount ?? 0;
            for (int e = 0; e < _echoes.Length; e++)
            {
                bool used = e < echoes;
                SendTo(ref _echoes[e], ref _echoGains[e], ref _echoIn[e], ref _echoDelays[e], used ? sends.Echoes[e] : null,
                    used ? sends.EchoGains[e] * mix : 0f, used ? sends.EchoDelays[e] : 0, sound, at, count, fadeIn);
            }
            SendTo(ref _corner, ref _cornerGain, ref _cornerIn, ref _cornerDelay, sends?.Corner, (sends?.CornerGain ?? 0f) * mix, sends?.CornerDelay ?? 0, sound, at, count, fadeIn);
        }

        // One send: on to where it should go now. A new place fades in (fadeIn more each block) while the old one
        // fades out (over Transition, among the leaving ones; within this block with none).
        private void SendTo(ref SpaceBus current, ref float gain, ref float faded, ref int delay, SpaceBus target, float targetGain, int targetDelay,
            float[] sound, long at, int count, float fadeIn)
        {
            if (target != current)
            {
                float was = gain * faded;
                if (current != null && was > 0f)
                {
                    if (fadeIn >= 1f)
                        current.Add(at + delay, sound, count, was, 0f);
                    else
                        Leave(current, was, delay, sound, at, count);
                }
                current = target;
                delay = targetDelay;
                gain = current != null ? targetGain : 0f;
                faded = 0f;
                if (current == null)
                    return;
            }
            if (current == null)
                return;
            int difference = targetDelay - delay;
            delay = Math.Abs(difference) > _rate / 20 ? targetDelay : delay + Mathf.Clamp(difference, -16, 16);
            float from = gain * faded;
            faded = Mathf.Min(1f, faded + fadeIn);
            current.Add(at + delay, sound, count, from, targetGain * faded);
            gain = targetGain;
        }

        // Keeps sending to a place the sound has moved on from, fading out over Transition from this block on.
        private void Leave(SpaceBus bus, float gain, int delay, float[] sound, long at, int count)
        {
            if (_leavingCount == MaxLeaving)
            {
                // Full: the oldest goes now (within this block).
                ref var oldest = ref _leaving[0];
                oldest.Bus.Add(at + oldest.Delay, sound, count, oldest.Gain, 0f);
                Array.Copy(_leaving, 1, _leaving, 0, MaxLeaving - 1);
                _leavingCount--;
            }
            float perSample = gain / Mathf.Max(1f, Transition * _rate);
            float next = Mathf.Max(0f, gain - perSample * count);
            bus.Add(at + delay, sound, count, gain, next);
            if (next > 0f)
                _leaving[_leavingCount++] = new Leaving { Bus = bus, Gain = next, PerSample = perSample, Delay = delay };
        }

        private void FadeOutLeaving(float[] sound, long at, int count)
        {
            int kept = 0;
            for (int i = 0; i < _leavingCount; i++)
            {
                var leaving = _leaving[i];
                float next = Mathf.Max(0f, leaving.Gain - leaving.PerSample * count);
                if (leaving.Bus != null)
                    leaving.Bus.Add(at + leaving.Delay, sound, count, leaving.Gain, next);
                if (next <= 0f || leaving.Bus == null)
                    continue;
                leaving.Gain = next;
                _leaving[kept++] = leaving;
            }
            for (int i = kept; i < _leavingCount; i++)
                _leaving[i].Bus = null;
            _leavingCount = kept;
        }
    }

    /// <summary>
    /// Sidechain ducking for a Spatial effect: follows the sound's own level, and says how far the effect (its own
    /// reverb or echo, and what it sends to rooms, echo walls and tail spots) is turned down under it while it plays,
    /// like a compressor keyed by the sound. Blend 1: never; 0: as far as the sound is loud (up to 30 dB), so the
    /// effect swells back in the gaps and the tail. Audio thread, one per effect per voice.
    /// </summary>
    internal sealed class SourceDucker
    {
        // Follows the sound in 5 ms, lets go over a quarter second. From -40 dB, each dB louder ducks 0.8 dB more.
        private const float Threshold = -40f, Slope = 0.8f, MostDb = 30f;
        private readonly float _attack, _release;
        private float _level, _gain = 1f, _target = 1f;
        private int _untilTarget;

        /// <summary>The lowest gain since <see cref="Begin"/> (what the buses it sends to duck by).</summary>
        public float Lowest { get; private set; } = 1f;

        public SourceDucker(int sampleRate)
        {
            float rate = Mathf.Max(8000, sampleRate);
            _attack = 1f - Mathf.Exp(-1f / (0.005f * rate));
            _release = 1f - Mathf.Exp(-1f / (0.25f * rate));
        }

        public void Reset()
        {
            _level = 0f;
            _gain = _target = Lowest = 1f;
            _untilTarget = 0;
        }

        /// <summary>A new block.</summary>
        public void Begin() => Lowest = 1f;

        /// <summary>The effect's gain for this sample of the sound (<paramref name="blend"/> as above).</summary>
        public float Next(float sound, float blend)
        {
            float level = Math.Abs(sound);
            if (float.IsNaN(level) || float.IsInfinity(level)) level = 0f;
            _level += (level - _level) * (level > _level ? _attack : _release);
            if (--_untilTarget <= 0)
            {
                _untilTarget = 32;
                _target = GainFor(_level, 1f - Mathf.Clamp01(blend));
            }
            // (Between the every-32-samples targets: a short glide, so the steps don't click.)
            _gain += (_target - _gain) * 0.1f;
            if (_gain < Lowest) Lowest = _gain;
            return _gain;
        }

        private static float GainFor(float level, float depth)
        {
            if (depth <= 0f || level <= 1e-6f)
                return 1f;
            float over = 20f * Mathf.Log10(level) - Threshold;
            return over <= 0f ? 1f : Mathf.Pow(10f, -Mathf.Min(over * Slope, MostDb) * depth / 20f);
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

        private TailPool _pool;

        /// <summary>The spots it has left that are still ringing (main thread).</summary>
        public void Spots(System.Collections.Generic.List<SpaceBus> into)
        {
            into.Clear();
            if (_pool == null)
                return;
            foreach (var bus in _pool.Buses)
                if (bus != null && bus.Owner is TailPool.Spot spot && spot.Trail == this)
                    into.Add(bus);
        }

        public override void Update(in SoundProbeWorld world)
        {
            var pool = world.Simulated ? SoundSpaceSimulation.Current?.Tails : SoundSpaces.Get()?.Tails;
            _pool = pool;
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

        /// <summary>
        /// Links Trails and Spatial effects that feed one another (either way round, however far apart in the chain):
        /// <paramref name="upstream"/> is what feeds the node being built, which is the Trail
        /// <paramref name="trail"/> or the Spatial effect <paramref name="spatial"/>.
        /// </summary>
        public static void Link(System.Collections.Generic.List<SoundProcessor> upstream, TrailProbe trail, SpatialProbe spatial)
        {
            foreach (var processor in upstream)
            {
                if (spatial != null && processor is ITrailed trailed && trailed.Trail != null)
                    Join(spatial, trailed.Trail);
                if (trail != null && processor is ISpatial placed && placed.Place != null)
                    Join(placed.Place, trail);
            }
        }

        private static void Join(SpatialProbe spatial, TrailProbe trail)
        {
            spatial.Trails ??= new();
            if (!spatial.Trails.Contains(trail))
                spatial.Trails.Add(trail);
        }
    }

    /// <summary>A processor that can leave a trail (a Reverb or Delay in Trail mode).</summary>
    internal interface ITrailed
    {
        TrailProbe Trail { get; }
    }

    /// <summary>A processor shaped by the place (a Reverb or Delay in Spatial mode).</summary>
    internal interface ISpatial
    {
        SpatialProbe Place { get; }
    }

    /// <summary>How late a Travel Time is making its sound now, in samples (written by it on the audio thread).</summary>
    internal sealed class TravelDelay
    {
        public int Samples;
    }

    /// <summary>A processor that sends to the world's spaces (a Spatial effect, Around Corners).</summary>
    internal interface ISpatialSends
    {
        SpatialSender Sender { get; }
    }
}
