using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Freeverb's mono path (eight damped feedback combs in parallel, then four allpasses in series): the Reverb node's
    /// own reverb, and each ringing room's.
    /// </summary>
    public sealed class ReverbCore
    {
        private static readonly int[] CombTunings = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] AllpassTunings = { 556, 441, 341, 225 };

        private readonly float[][] _combs, _allpasses;
        private readonly int[] _combIndex, _allpassIndex;
        private readonly float[] _combStore;

        public ReverbCore(int sampleRate)
        {
            float scale = sampleRate / 44100f;
            _combs = new float[CombTunings.Length][];
            for (int i = 0; i < _combs.Length; i++)
                _combs[i] = new float[Mathf.Max(1, (int)(CombTunings[i] * scale))];
            _allpasses = new float[AllpassTunings.Length][];
            for (int i = 0; i < _allpasses.Length; i++)
                _allpasses[i] = new float[Mathf.Max(1, (int)(AllpassTunings[i] * scale))];
            _combIndex = new int[_combs.Length];
            _allpassIndex = new int[_allpasses.Length];
            _combStore = new float[_combs.Length];
        }

        public void Clear()
        {
            foreach (var line in _combs) Array.Clear(line, 0, line.Length);
            foreach (var line in _allpasses) Array.Clear(line, 0, line.Length);
            Array.Clear(_combIndex, 0, _combIndex.Length);
            Array.Clear(_allpassIndex, 0, _allpassIndex.Length);
            Array.Clear(_combStore, 0, _combStore.Length);
        }

        /// <summary>
        /// One sample through the reverb: <paramref name="feedback"/> sets how long it rings (0.7 to 0.985),
        /// <paramref name="damp"/> how fast the highs fade (0 to 0.4). Returns the wet signal (about a third of full scale
        /// for a 0.015-scaled input, as Freeverb's are).
        /// </summary>
        public float Process(float input, float feedback, float damp)
        {
            float wet = 0f;
            for (int c = 0; c < _combs.Length; c++)
            {
                var line = _combs[c];
                int index = _combIndex[c];
                float output = line[index];
                _combStore[c] = output * (1f - damp) + _combStore[c] * damp;
                line[index] = input + _combStore[c] * feedback;
                _combIndex[c] = index + 1 == line.Length ? 0 : index + 1;
                wet += output;
            }
            for (int a = 0; a < _allpasses.Length; a++)
            {
                var line = _allpasses[a];
                int index = _allpassIndex[a];
                float buffered = line[index];
                float output = -wet + buffered;
                line[index] = wet + buffered * 0.5f;
                _allpassIndex[a] = index + 1 == line.Length ? 0 : index + 1;
                wet = output;
            }
            return wet;
        }
    }

    /// <summary>
    /// A place in the world that sounds: a ringing room's reverb, or an echo wall throwing sound back. It's an
    /// AudioSource placed there (so it's heard from there, fading with distance, like any sound) that plays what sounds
    /// send it, at the moment each sent sample is heard (sends are stamped on the audio clock), through its effect.
    /// Its ringing carries on after the sounds that fed it have stopped or moved on.
    /// </summary>
    [AddComponentMenu("")]
    [ExecuteAlways] // the editor's preview simulation runs them outside Play Mode too
    public sealed class SpaceBus : MonoBehaviour
    {
        private const int RingSize = 1 << 18; // ~5 s at 48 kHz
        private const int RingMask = RingSize - 1;

        private readonly float[] _ring = new float[RingSize];
        // Sidechain ducking (see Duck): how far under the sounds feeding it it's turned down, per stretch of 64 samples
        // (0 not at all, 1 silent), on the same clock as the ring; and the gain it's gliding at.
        private const int DuckShift = 6, DuckSize = RingSize >> DuckShift, DuckMask = DuckSize - 1;
        private readonly float[] _duck = new float[DuckSize];
        private float _duckGain = 1f;
        private readonly object _lock = new();
        private long _readHead = -1;
        private long _lastWrite = long.MinValue;
        private int _rate;

        /// <summary>What a bus does with what it's sent.</summary>
        public enum BusKind { Room, Echo, Slap, Corner }

        // Room: a reverb (a ringing room, or a reverb tail left where a sound was). Echo: darkened, diffused (three
        // allpasses), then half a short smear (a brief reverb), half the clean copy: a wall throws back a soft version
        // of a sound, not a sharp duplicate. Slap: a slapback, repeating (left where a sound was). Corner: the sound
        // itself, darkened by how sharply it bends, heard from the corner it comes round.
        public BusKind Kind { get; private set; }
        public bool IsRoom => Kind == BusKind.Room;
        private float[] _slapLine = Array.Empty<float>();
        private int _slapWrite, _slapDelay = 1;
        private float _slapFeedback, _slapLow, _slapLowAmount = 0.5f;
        private ReverbCore _reverb;
        private float _feedback = 0.85f, _damp = 0.15f;
        private float _lowpass, _lowpassAmount = 0.5f;
        private float[] _allpassA = new float[1], _allpassB = new float[1], _allpassC = new float[1];
        private int _allpassIndexC;
        /// <summary>An echo's blend: how much of the copy is left (the rest is the smear).</summary>
        public const float EchoDirect = 0.5f;
        // The smear's feedback (about a sixth of a second) and damping: enough to soften it, not to muddy it.
        private const float SmearFeedback = 0.5f, SmearDamp = 0.3f;
        private int _allpassIndexA, _allpassIndexB;

        // Flutter: what an echo wall sends on to the wall facing it.
        private SpaceBus _partner;
        private int _partnerDelay;
        private float _partnerGain;
        private float[] _scratch = new float[2048];
        // An echo wall's clean echo (before its smear): what it passes on to the facing wall. Passing the smear on too
        // made the two walls feed each other's ringing until it built into a howl.
        private float[] _clean = new float[2048];

        /// <summary>The room this bus is ringing for, or the field and anchor it echoes for (set by the manager).</summary>
        public object Owner;
        public AudioSource Source { get; private set; }

        internal static SpaceBus Create(Transform parent, int rate)
        {
            var host = new GameObject("Space Bus") { hideFlags = parent != null ? parent.gameObject.hideFlags : HideFlags.None };
            host.transform.SetParent(parent, false);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.clip = DcClip(rate);
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            // (Never a spatializer plugin: it plays a clip of ones that its sound is multiplied into, which only a
            // plain left/right volume keeps intact.)
            source.spatialize = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = IdlePriority;
            try
            {
                var prefab = AudioData.SFXSourcePrefab;
                if (prefab != null && prefab.TryGetComponent(out AudioSource sfx))
                    source.outputAudioMixerGroup = sfx.outputAudioMixerGroup;
            }
            catch { } // no Audio Data yet
            var bus = host.AddComponent<SpaceBus>();
            bus.Source = source;
            bus._rate = rate;
            bus._reverb = new ReverbCore(rate);
            bus._allpassA = new float[Mathf.Max(1, (int)(0.0071f * rate))];
            bus._allpassB = new float[Mathf.Max(1, (int)(0.0117f * rate))];
            bus._allpassC = new float[Mathf.Max(1, (int)(0.0173f * rate))];
            bus.SetRingTime(MaxRingTime);
            // Not playing until it's taken: an idle bus mustn't hold one of Unity's real voices (only 32 by default),
            // or enough of them silence every other sound in the game.
            return bus;
        }

        /// <summary>
        /// Where buses stand when Unity runs short of real voices (lower is kept first): after ordinary sounds (128), so
        /// a burst of echo walls can never take the music's or a sound's own voice (that cut every sound in the game for
        /// as long as they played); among themselves a room first (it carries every sound in it), then an echo wall,
        /// a corner, a tail or trail spot. Idle: last.
        /// </summary>
        public const int RoomPriority = 132, EchoPriority = 140, CornerPriority = 144, SpotPriority = 160, IdlePriority = 256;

        // The buses holding a voice now (every pool's, and the preview's).
        private static readonly HashSet<SpaceBus> _playing = new();
        private bool _holdsVoice;

        /// <summary>Buses playing now (each holds one of Unity's real voices).</summary>
        public static int Playing => _playing.Count;

        /// <summary>
        /// Most buses playing at once: half of Unity's real voices (Project Settings > Audio > Max Real Voices), so
        /// at least the other half is always left for ordinary sounds and music.
        /// </summary>
        public static int VoiceBudget
        {
            get
            {
                if (Time.frameCount != _budgetFrame)
                {
                    _budgetFrame = Time.frameCount;
                    _budget = Mathf.Max(4, AudioSettings.GetConfiguration().numRealVoices / 2);
                }
                return _budget;
            }
        }
        private static int _budget = 16, _budgetFrame = -1;

        /// <summary>Whether it can start (it's playing already, or there's a voice to spare); says why not, now and then.</summary>
        internal static bool CanStart(SpaceBus bus, string what)
        {
            if (bus != null && bus._holdsVoice) return true;
            if (Playing < VoiceBudget) return true;
            SoundSpaces.OutOfVoices(what);
            return false;
        }

        /// <summary>Logs each bus start and stop (Sound Spaces window: Log Buses), for finding what takes the voices.</summary>
        public static bool LogBuses;

        private void StartPlaying()
        {
            if (!Source.isPlaying) Source.Play();
            if (!_holdsVoice)
            {
                _holdsVoice = true;
                _playing.Add(this);
            }
            if (LogBuses)
                Debug.Log($"[Sound Spaces] Start {Kind} bus for {Describe(Owner)} at {transform.position:F1} (priority {Source.priority}): {Playing} playing of {VoiceBudget} allowed, {AudioSettings.GetConfiguration().numRealVoices} real voices", this);
        }

        private void ReleaseVoice()
        {
            if (!_holdsVoice) return;
            _holdsVoice = false;
            _playing.Remove(this);
        }

        private void OnDestroy() => ReleaseVoice();

        internal static string Describe(object owner) => owner switch
        {
            null => "nothing",
            AcousticRoom room => $"room at {room.Center:F0}",
            ValueTuple<SoundField, int> wall => $"wall {wall.Item2} of {(wall.Item1 != null ? wall.Item1.name : "a removed field")}",
            _ => owner.GetType().Name,
        };

        /// <summary>Its place when Unity runs short of real voices (see <see cref="RoomPriority"/>).</summary>
        internal void SetPriority(int priority) => Source.priority = Mathf.Clamp(priority, 0, 256);

        // A clip of ones: Unity places and attenuates it, and the filter below multiplies in the bus's own sound, so it
        // comes out placed and attenuated the same.
        private static AudioClip _dc;
        internal static AudioClip DcClip(int rate)
        {
            if (_dc != null) return _dc;
            var ones = new float[rate];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            _dc = AudioClip.Create("Space Bus (DC)", rate, 1, rate, false);
            _dc.SetData(ones, 0);
            return _dc;
        }

        /// <summary>Starts it ringing as a room's reverb, at a place, spread over a room of <paramref name="radius"/> meters.</summary>
        internal void BeginRoom(object owner, Vector3 position, float radius)
        {
            Begin(owner, position);
            Kind = BusKind.Room;
            Source.priority = RoomPriority;
            Source.minDistance = Mathf.Max(2f, radius);
            Source.maxDistance = Source.minDistance * 10f + 60f;
            Source.spread = 180f;
            StartPlaying();
        }

        /// <summary>
        /// An echo wall's: where on the wall it's heard from (the loudness-weighted bounce points of the sounds it
        /// carries). The bus itself plays from that way round the listener, at <see cref="SoundSpaces.EchoOrbit"/>.
        /// </summary>
        public Vector3 HeardFrom;

        /// <summary>
        /// Starts it echoing as a wall, heard from a place on it. It has no distance falloff of its own: it circles the
        /// listener, and what's sent already fell off over the whole way (the sound to the wall and on to the listener).
        /// </summary>
        internal void BeginEcho(object owner, Vector3 position, float size, float hardness)
        {
            Begin(owner, position);
            HeardFrom = position;
            Kind = BusKind.Echo;
            Source.priority = EchoPriority;
            _noFalloff ??= AnimationCurve.Constant(0f, 1f, 1f);
            Source.rolloffMode = AudioRolloffMode.Custom;
            Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _noFalloff);
            Source.minDistance = 1f;
            Source.maxDistance = 1000f;
            Source.spread = 60f;
            SetEcho(hardness);
            StartPlaying();
        }

        private void Begin(object owner, Vector3 position)
        {
            Owner = owner;
            Source.rolloffMode = AudioRolloffMode.Logarithmic; // (a corner's has none of its own)
            _begunAt = Time.realtimeSinceStartup;
            transform.position = position;
            lock (_lock)
            {
                // (The clock keeps running: only what it held is cleared.)
                Array.Clear(_ring, 0, _ring.Length);
                Array.Clear(_duck, 0, _duck.Length);
                _duckGain = 1f;
                _reverb.Clear();
                _lowpass = 0f;
                Array.Clear(_allpassA, 0, _allpassA.Length);
                Array.Clear(_allpassB, 0, _allpassB.Length);
                Array.Clear(_allpassC, 0, _allpassC.Length);
                _partner = null;
                _forward = null;
                _envelope = 1f;
                _silenced = false;
            }
            Interlocked.Exchange(ref _lastWrite, long.MinValue);
            Interlocked.Exchange(ref _resets, 0);
            Interlocked.Exchange(ref _accepted, 0);
            Interlocked.Exchange(ref _late, 0);
            Interlocked.Exchange(ref _lastAcceptedSend, 0);
            Interlocked.Exchange(ref _lastLateSend, 0);
            Interlocked.Exchange(ref _early, 0);
            Volatile.Write(ref _peak, 0f);
        }

        /// <summary>Lets it go: stopped (freeing its voice) and empty, ready to be taken again.</summary>
        internal void End()
        {
            if (LogBuses && _holdsVoice)
                Debug.Log($"[Sound Spaces] Stop {Kind} bus for {Describe(Owner)}: {Playing - 1} playing", this);
            Owner = null;
            lock (_lock)
            {
                Array.Clear(_ring, 0, _ring.Length);
                Array.Clear(_duck, 0, _duck.Length);
                _duckGain = 1f;
                _reverb.Clear();
                _partner = null;
                _forward = null;
                // Its clock starts again from the audio clock when it's next played.
                _readHead = -1;
            }
            if (Source != null)
            {
                Source.Stop();
                Source.priority = IdlePriority;
            }
            ReleaseVoice();
        }

        private static AnimationCurve _noFalloff;

        /// <summary>
        /// Starts it carrying a sound round a corner, heard from <paramref name="position"/> (the corner). It has no
        /// distance falloff of its own: what's sent already fell off over the whole way round.
        /// </summary>
        internal void BeginCorner(object owner, Vector3 position)
        {
            Begin(owner, position);
            Kind = BusKind.Corner;
            Source.priority = CornerPriority;
            _noFalloff ??= AnimationCurve.Constant(0f, 1f, 1f);
            Source.rolloffMode = AudioRolloffMode.Custom;
            Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _noFalloff);
            Source.minDistance = 1f;
            Source.maxDistance = 1000f;
            Source.spread = 30f;
            SetRingTime(0.3f);
            StartPlaying();
        }

        /// <summary>A corner's darkening: the highest pitch that gets round (Hz).</summary>
        internal void SetCorner(float cutoff)
            => _lowpassAmount = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Clamp(cutoff, 200f, 20000f) / Mathf.Max(8000, _rate));

        /// <summary>Moves it (a corner, as the way round changes).</summary>
        internal void MoveTo(Vector3 position) => transform.position = position;

        /// <summary>Starts it as a slapback left at a place: what's sent comes back after the delay, repeating.</summary>
        internal void BeginSlap(object owner, Vector3 position)
        {
            Begin(owner, position);
            lock (_lock)
            {
                // (Under the lock: the audio thread reads the line.) Long enough for a Delay node's longest.
                if (_slapLine.Length < (int)(2.1f * _rate))
                    _slapLine = new float[(int)(2.1f * _rate)];
                Array.Clear(_slapLine, 0, _slapLine.Length);
                _slapWrite = 0;
                _slapLow = 0f;
            }
            Kind = BusKind.Slap;
            Source.priority = SpotPriority;
            Source.minDistance = 2f;
            Source.maxDistance = 300f;
            Source.spread = 60f;
            StartPlaying();
        }

        /// <summary>A slapback's delay (seconds), how much comes round again, and how dull (0 bright, 1 dull) it is.</summary>
        internal void SetSlap(float delay, float feedback, float damping)
        {
            _slapDelay = Mathf.Clamp((int)(delay * _rate), 1, Mathf.Max(1, _slapLine.Length - 2));
            _slapFeedback = Mathf.Clamp(feedback, 0f, 0.95f); // (a place's is at most 0.5; a Trail's is its Delay node's)
            // Until its repeats have fallen 60 dB (and the first is back), at most MaxRingTime.
            float repeats = _slapFeedback > 0.01f ? Mathf.Log(0.001f) / Mathf.Log(_slapFeedback) : 1f;
            SetRingTime(delay * (repeats + 1f) + 0.1f);
            float cutoff = Mathf.Lerp(9000f, 1500f, Mathf.Clamp01(damping));
            _slapLowAmount = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Mathf.Max(8000, _rate));
        }

        /// <summary>Nothing sent to it for longer than <paramref name="tail"/> seconds (and a bit): it has rung out.</summary>
        public bool IsQuiet(float tail)
        {
            float hold = tail * 1.2f + 0.5f;
            long last = LastWrite, head = ReadHead;
            // Not started yet, or nothing sent: quiet once it's been that long since it was taken.
            if (last == long.MinValue || head < 0)
                return Time.realtimeSinceStartup - _begunAt > hold;
            return head - last > (long)(hold * _rate);
        }

        private float _begunAt;

        /// <summary>
        /// Heard flat, panned and at a set volume, instead of placed in 3D (the editor's preview, which has no listener
        /// in the world: it works out where things are around its imaginary one itself).
        /// </summary>
        internal void SetFlat(float pan, float volume)
        {
            Source.spatialBlend = 0f;
            Source.panStereo = Mathf.Clamp(pan, -1f, 1f);
            Source.volume = Mathf.Clamp01(volume);
        }

        /// <summary>A room's reverb: <paramref name="feedback"/> (how long) and <paramref name="damping"/> (0 bright, 1 dull).</summary>
        internal void SetRoom(float feedback, float damping)
        {
            _feedback = Mathf.Clamp(feedback, 0f, 0.98f); // (a place's is at most 0.95; a Trail's is its Reverb node's)
            // Its reverb time (60 dB down; its combs average about 31 ms), at most MaxRingTime.
            SetRingTime(3f * 0.0308f / Mathf.Max(1e-4f, -Mathf.Log10(Mathf.Max(0.01f, _feedback))));
            _damp = Mathf.Clamp01(damping) * 0.4f;
        }

        /// <summary>An echo wall's hardness (0 soft, 1 hard), as set when it started.</summary>
        public float Hardness { get; private set; }

        // An echo wall: darker than what it hears (more so for softer walls).
        internal void SetEcho(float hardness)
        {
            Hardness = Mathf.Clamp01(hardness);
            float cutoff = Mathf.Lerp(1200f, 6000f, Mathf.Clamp01(hardness));
            _lowpassAmount = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Mathf.Max(8000, _rate));
            // Its smear and a canyon's flutter have died away by then.
            SetRingTime(2.5f);
        }

        /// <summary>The wall this one echoes on to (a canyon's other side), how many samples later, and how much.</summary>
        internal void SetPartner(SpaceBus partner, int delay, float gain)
        {
            lock (_lock)
            {
                _partner = partner;
                _partnerDelay = Mathf.Max(1, delay);
                _partnerGain = gain;
            }
        }

        // A Trail spot heard by a Spatial effect after it: what the spot plays goes on to the place's room, echo walls and
        // tail spot too (as the Spatial node sends its sound), scaled by the node's Mix.
        private SpaceSends _forward;
        private float _forwardMix;

        /// <summary>Sends what it plays on to <paramref name="sends"/> (null: nowhere), times <paramref name="mix"/>.</summary>
        internal void SetForward(SpaceSends sends, float mix)
        {
            Volatile.Write(ref _forwardMix, mix);
            Volatile.Write(ref _forward, sends);
        }

        /// <summary>How far ahead of what it's playing a forwarded block is sent: past what the others are playing.</summary>
        private const int ForwardLead = 2048;

        /// <summary>The audio-clock sample it's playing now (below 0 before it starts).</summary>
        public long ReadHead => Interlocked.Read(ref _readHead);

        // What the debug window shows: sends taken in time, too late (already played) or too early (beyond the ring), how
        // far ahead the last one was, and how loud the bus is.
        private long _accepted, _late, _early;
        private float _lead, _peak;

        /// <summary>Sends taken in time.</summary>
        public long Accepted => Interlocked.Read(ref _accepted);
        /// <summary>Sends dropped because they arrived after their moment had already played.</summary>
        public long Late => Interlocked.Read(ref _late);
        /// <summary>Sends dropped because they were further ahead than the bus can hold.</summary>
        public long Early => Interlocked.Read(ref _early);
        /// <summary>How far ahead of being heard the latest send arrived, in seconds.</summary>
        public float LeadSeconds => Volatile.Read(ref _lead);
        /// <summary>How loud the bus is (its peak, fading), 0 to 1-ish.</summary>
        public float Peak => Volatile.Read(ref _peak);
        /// <summary>Playing (its first audio callback has come).</summary>
        public bool Running => ReadHead >= 0;
        /// <summary>
        /// Playing, and taking sends in time rather than dropping them: what a sound needs to hand its reverb over to it.
        /// </summary>
        public bool Healthy => Running && Interlocked.Read(ref _lastAcceptedSend) >= Interlocked.Read(ref _lastLateSend);

        // Which send (by a running count) was last taken, and last too late: healthy while the latest ones are taken.
        private static long _sendCount;
        private long _lastAcceptedSend, _lastLateSend;

        /// <summary>
        /// The audio-clock sample the latest sound feeding it was heard until (long.MinValue when none). Blocks passed on
        /// from other buses count as the sound behind them, not as new sound.
        /// </summary>
        public long LastWrite => Interlocked.Read(ref _lastWrite);

        public int Rate => _rate;

        /// <summary>
        /// Adds a block to what the bus will play at audio-clock sample <paramref name="at"/> on (any thread), its gain
        /// gliding from <paramref name="gainFrom"/> to <paramref name="gainTo"/>. Too late (already played) or too far
        /// ahead: dropped.
        /// </summary>
        public void Add(long at, float[] block, int count, float gainFrom, float gainTo)
            => Add(at, block, count, gainFrom, gainTo, at + count);

        // fedUntil: when the sound behind this block was last heard. A sound's own block: its end. A block passed on
        // from another bus (a facing wall, a Trail spot): that bus's, so buses feeding each other can't keep each other
        // ringing past their sounds (see MaxRingTime).
        private void Add(long at, float[] block, int count, float gainFrom, float gainTo, long fedUntil)
        {
            if (count <= 0 || (gainFrom == 0f && gainTo == 0f) || fedUntil == long.MinValue)
                return;
            lock (_lock)
            {
                // Before its first callback, the audio clock itself says what "now" is. Long unread (Unity set it aside,
                // and its clock stopped): back in step first, or sends would be judged against a stale moment.
                long now = (long)Math.Round(AudioSettings.dspTime * _rate);
                if (_readHead >= 0 && now - _readHead > RingSize / 4)
                    Resync(now);
                long head = _readHead >= 0 ? _readHead : now;
                if (at < head)
                {
                    Interlocked.Increment(ref _late);
                    Interlocked.Exchange(ref _lastLateSend, Interlocked.Increment(ref _sendCount));
                    return;
                }
                if (at + count >= head + RingSize - 4096)
                {
                    Interlocked.Increment(ref _early);
                    return;
                }
                Interlocked.Increment(ref _accepted);
                Interlocked.Exchange(ref _lastAcceptedSend, Interlocked.Increment(ref _sendCount));
                Volatile.Write(ref _lead, (at - head) / (float)Mathf.Max(1, _rate));
                float step = (gainTo - gainFrom) / count;
                for (int i = 0; i < count; i++)
                {
                    float value = block[i] * (gainFrom + step * i);
                    // A glitch upstream (not a number, or infinite) would stick in the effect for good: dropped here.
                    if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                    _ring[(at + i) & RingMask] += value;
                }
            }
            long last;
            do { last = Interlocked.Read(ref _lastWrite); }
            while (fedUntil > last && Interlocked.CompareExchange(ref _lastWrite, fedUntil, last) != last);
        }

        /// <summary>
        /// The longest anything rings after the sound that fed it was heard, in seconds, whatever its feedback: past its
        /// own ring time (and at most this) it fades out and goes silent, so nothing can ever feed back for good.
        /// </summary>
        public const float MaxRingTime = 8f;
        /// <summary>How long the fade at the end of its ring time takes, in seconds.</summary>
        public const float RingFade = 0.25f;

        // Its ring time (samples), its fade (1 playing, 0 silenced) and whether it's silenced until a sound feeds it.
        private long _ringLimit = long.MaxValue;
        private float _envelope = 1f;
        private bool _silenced;
        private long _resets;

        /// <summary>Times it ran away (a glitch, or too loud) and was cleared.</summary>
        public long Resets => Interlocked.Read(ref _resets);

        // How long a ring can go on (seconds), capped.
        private void SetRingTime(float seconds)
            => Interlocked.Exchange(ref _ringLimit, (long)(Mathf.Clamp(seconds, 0.2f, MaxRingTime) * Mathf.Max(1, _rate)));

        // Silent and empty (under the lock).
        // The ducking for stretches wholly played (a stretch only partly played is read again next time).
        private void ClearDucks(long head, int frames)
        {
            for (long c = head >> DuckShift; c < (head + frames) >> DuckShift; c++)
                _duck[c & DuckMask] = 0f;
        }

        private void ClearEffect()
        {
            _reverb.Clear();
            Array.Clear(_slapLine, 0, _slapLine.Length);
            Array.Clear(_allpassA, 0, _allpassA.Length);
            Array.Clear(_allpassB, 0, _allpassB.Length);
            Array.Clear(_allpassC, 0, _allpassC.Length);
            _slapLow = _lowpass = 0f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            if (_scratch.Length < frames)
            {
                _scratch = new float[frames];
                _clean = new float[frames];
            }
            SpaceBus partner;
            int partnerDelay;
            float partnerGain;
            long head;
            lock (_lock)
            {
                // Back in step with the audio clock: on its first call, and after Unity skipped calling it (a far or quiet
                // source is set aside to save time, and its clock stops). What it missed is cleared, so it isn't played
                // late (or again, when the ring comes round).
                long now = (long)Math.Round(AudioSettings.dspTime * _rate);
                if (_readHead < 0)
                    _readHead = now;
                else if (now - _readHead > frames * 2L)
                    Resync(now);
                head = _readHead;
                // Unused (in the pool): silent, and nothing to work out (its clock still runs, ready for the next use).
                if (Owner == null)
                {
                    _readHead = head + frames;
                    Array.Clear(data, 0, data.Length);
                    Volatile.Write(ref _peak, 0f);
                    return;
                }
                // Past its ring time since the sound that fed it was heard: it fades out, then stays silent (and costs
                // nothing) until a sound feeds it again.
                long fed = Interlocked.Read(ref _lastWrite);
                bool over = fed != long.MinValue && head - fed > Interlocked.Read(ref _ringLimit);
                if (!over)
                {
                    _envelope = 1f;
                    _silenced = false;
                }
                if (_silenced)
                {
                    for (int f = 0; f < frames; f++)
                        _ring[(head + f) & RingMask] = 0f;
                    ClearDucks(head, frames);
                    _readHead = head + frames;
                    Array.Clear(data, 0, data.Length);
                    Volatile.Write(ref _peak, 0f);
                    return;
                }
                float fade = over ? 1f / (RingFade * Mathf.Max(1, _rate)) : 0f;
                // Ducking glides down in 5 ms and back up in 80 (the sound's own level was already followed).
                float duckDown = 1f - Mathf.Exp(-1f / (0.005f * Mathf.Max(8000, _rate)));
                float duckUp = 1f - Mathf.Exp(-1f / (0.08f * Mathf.Max(8000, _rate)));
                for (int f = 0; f < frames; f++)
                {
                    int slot = (int)((head + f) & RingMask);
                    float input = _ring[slot];
                    _ring[slot] = 0f;
                    float output, clean = 0f;
                    if (Kind == BusKind.Room)
                        output = _reverb.Process(input * 0.015f, _feedback, _damp) * 3f;
                    else if (Kind == BusKind.Slap)
                    {
                        int length = _slapLine.Length;
                        int read = _slapWrite - _slapDelay;
                        if (read < 0) read += length;
                        float echo = _slapLine[read];
                        _slapLow += (echo - _slapLow) * _slapLowAmount;
                        _slapLine[_slapWrite] = input + _slapLow * _slapFeedback;
                        _slapWrite = _slapWrite + 1 == length ? 0 : _slapWrite + 1;
                        output = _slapLow;
                    }
                    else if (Kind == BusKind.Corner)
                    {
                        _lowpass += (input - _lowpass) * _lowpassAmount;
                        output = _lowpass;
                    }
                    else
                    {
                        _lowpass += (input - _lowpass) * _lowpassAmount;
                        float diffused = Allpass(Allpass(Allpass(_lowpass, _allpassA, ref _allpassIndexA), _allpassB, ref _allpassIndexB), _allpassC, ref _allpassIndexC);
                        float smear = _reverb.Process(diffused * 0.015f, SmearFeedback, SmearDamp) * 3f;
                        output = diffused * EchoDirect + smear * (1f - EchoDirect);
                        clean = diffused;
                    }
                    if (float.IsNaN(output) || float.IsInfinity(output) || Mathf.Abs(output) > 8f)
                    {
                        // Running away (it shouldn't): start clean rather than howl.
                        ClearEffect();
                        Interlocked.Increment(ref _resets);
                        output = clean = 0f;
                    }
                    _envelope = Mathf.Max(0f, _envelope - fade);
                    float duckTo = 1f - _duck[(int)(((head + f) >> DuckShift) & DuckMask)];
                    _duckGain += (duckTo - _duckGain) * (duckTo < _duckGain ? duckDown : duckUp);
                    _scratch[f] = output * _envelope * _duckGain;
                    _clean[f] = clean * _envelope * _duckGain;
                }
                ClearDucks(head, frames);
                if (over && _envelope <= 0f)
                {
                    ClearEffect();
                    _silenced = true;
                }
                _readHead = head + frames;
                partner = _partner;
                partnerDelay = _partnerDelay;
                partnerGain = _partnerGain;
            }
            var forward = Volatile.Read(ref _forward);
            float forwardMix = Volatile.Read(ref _forwardMix);
            float peak = Volatile.Read(ref _peak) * 0.9f;
            for (int f = 0; f < frames; f++)
                peak = Mathf.Max(peak, Mathf.Abs(_scratch[f]));
            Volatile.Write(ref _peak, peak);
            for (int f = 0; f < frames; f++)
            {
                float sample = _scratch[f];
                for (int c = 0; c < channels; c++)
                    data[f * channels + c] *= sample; // the DC clip's placed, attenuated ones, times the bus's sound
            }
            // A canyon: the echo carries on to the facing wall (outside this bus's lock).
            long fedUntil = Interlocked.Read(ref _lastWrite);
            if (partner != null && partnerGain > 0f)
                partner.Add(head + partnerDelay, _clean, frames, partnerGain, partnerGain, fedUntil);
            // A Trail spot: on to the place's effects (outside this bus's lock, as above).
            if (forward != null && forwardMix > 0f)
            {
                long at = head + Math.Max(ForwardLead, frames * 2);
                Forward(forward.Room, at, forward.RoomGain * forwardMix, frames, fedUntil);
                Forward(forward.Tail, at, forward.TailGain * forwardMix, frames, fedUntil);
                for (int e = 0; e < forward.EchoCount; e++)
                    Forward(forward.Echoes[e], head + Math.Max(forward.EchoDelays[e], ForwardLead), forward.EchoGains[e] * forwardMix, frames, fedUntil);
            }
        }

        private void Forward(SpaceBus target, long at, float gain, int frames, long fedUntil)
        {
            if (target != null && target != this && gain > 0f)
                target.Add(at, _scratch, frames, gain, gain, fedUntil);
        }

        // Back in step with the audio clock after its reads stopped: what it missed is cleared (under the lock).
        private void Resync(long now)
        {
            long gap = Math.Min(now - _readHead, RingSize);
            for (long i = 0; i < gap; i++)
                _ring[(_readHead + i) & RingMask] = 0f;
            for (long c = 0; c <= gap >> DuckShift; c++)
                _duck[((_readHead >> DuckShift) + c) & DuckMask] = 0f;
            _readHead = now;
        }

        /// <summary>
        /// Sidechain: a sound heard from <paramref name="at"/> (on the same clock as <see cref="Add(long, float[], int, float, float)"/>,
        /// without any echo's delay) for <paramref name="count"/> samples turns this bus down by <paramref name="amount"/>
        /// (0 not at all, 1 silent) meanwhile, so its reverb or echoes don't drown that sound out. Where several sounds
        /// duck it at once, the deepest counts. It glides down quickly and back up over a moment.
        /// </summary>
        internal void Duck(long at, int count, float amount)
        {
            if (count <= 0 || !(amount > 0.001f))
                return;
            amount = Mathf.Min(amount, 1f);
            lock (_lock)
            {
                long now = (long)Math.Round(AudioSettings.dspTime * _rate);
                long head = _readHead >= 0 ? _readHead : now;
                long end = at + count;
                if (end <= head || end >= head + RingSize - 4096)
                    return;
                for (long c = Math.Max(at, head) >> DuckShift; c <= (end - 1) >> DuckShift; c++)
                {
                    int slot = (int)(c & DuckMask);
                    if (amount > _duck[slot]) _duck[slot] = amount;
                }
            }
        }

        private static float Allpass(float x, float[] line, ref int index)
        {
            float buffered = line[index];
            float output = -x * 0.5f + buffered;
            line[index] = x + buffered * 0.5f;
            index = index + 1 == line.Length ? 0 : index + 1;
            return output;
        }
    }

    /// <summary>
    /// Reverb and slapback tails left where sounds are: a sound's Spatial Reverb (outside rooms) and Spatial Delay feed
    /// the spot where the sound is, which rings out there on its own; when the sound moves on (more than
    /// <see cref="Handoff"/> meters) it feeds a new spot, and the old one finishes ringing where it was. Spots near each
    /// other are shared. Placed in 3D in the game, or panned flat around the editor preview's listener.
    /// </summary>
    public sealed class TailPool
    {
        /// <summary>How far a sound moves before it leaves a tail behind and starts a new one, in meters.</summary>
        public const float Handoff = 4f;

        /// <summary>
        /// A tail spot: what it is, where, how long it takes to ring out, and whose Trail it is (null: a place's tail,
        /// shared by every sound there).
        /// </summary>
        public sealed class Spot
        {
            public SpaceBus.BusKind Kind;
            public Vector3 Position;
            public float RingsFor;
            public object Trail;
            public float TakenAt;
        }

        private readonly Transform _parent;
        private readonly int _rate;
        private readonly bool _flat;
        private readonly int _max;
        private readonly List<SpaceBus> _buses = new();

        public TailPool(Transform parent, int rate, bool flat, int max = 32)
        {
            _parent = parent;
            _rate = rate;
            _flat = flat;
            _max = max;
        }

        public IReadOnlyList<SpaceBus> Buses => _buses;

        /// <summary>
        /// A reverb spot at a position (the one there, or a new one), ringing as set. For a <paramref name="trail"/>:
        /// its own spots, a new one every <paramref name="spacing"/> meters, at most <paramref name="maxSpots"/> of them
        /// (its oldest is taken for the next).
        /// </summary>
        public SpaceBus Reverb(Vector3 position, float feedback, float damping, float ringsFor, object trail = null, float spacing = Handoff, int maxSpots = 0)
        {
            var bus = Acquire(SpaceBus.BusKind.Room, position, ringsFor, trail, spacing, maxSpots, out bool fresh);
            if (bus == null) return null;
            if (fresh)
            {
                bus.BeginRoom(bus.Owner, position, 3f);
                bus.SetPriority(SpaceBus.SpotPriority);
            }
            bus.SetRoom(feedback, damping);
            Place(bus, position);
            return bus;
        }

        /// <summary>A slapback spot at a position (the one there, or a new one); a Trail's as for <see cref="Reverb"/>.</summary>
        public SpaceBus Slap(Vector3 position, float delay, float feedback, float damping, object trail = null, float spacing = Handoff, int maxSpots = 0)
        {
            float ringsFor = delay * (feedback > 0.01f ? Mathf.Clamp(Mathf.Log(0.001f) / Mathf.Log(Mathf.Min(feedback, 0.95f)), 1f, 40f) : 1f) + 0.5f;
            var bus = Acquire(SpaceBus.BusKind.Slap, position, ringsFor, trail, spacing, maxSpots, out bool fresh);
            if (bus == null) return null;
            if (fresh) bus.BeginSlap(bus.Owner, position);
            bus.SetSlap(delay, feedback, damping);
            Place(bus, position);
            return bus;
        }

        private SpaceBus Acquire(SpaceBus.BusKind kind, Vector3 position, float ringsFor, object trail, float spacing, int maxSpots, out bool fresh)
        {
            fresh = false;
            SpaceBus nearest = null, oldest = null;
            float best = Mathf.Max(0.1f, spacing), oldestAt = float.MaxValue;
            int own = 0;
            foreach (var bus in _buses)
                if (bus.Owner is Spot spot && spot.Kind == kind && spot.Trail == trail)
                {
                    own++;
                    if (spot.TakenAt < oldestAt) { oldestAt = spot.TakenAt; oldest = bus; }
                    float distance = Vector3.Distance(spot.Position, position);
                    if (distance < best) { best = distance; nearest = bus; }
                }
            if (nearest != null)
            {
                // A Trail's spot is set again each time (the node's settings can change); a place's keeps its longest.
                var spot = (Spot)nearest.Owner;
                spot.RingsFor = trail != null ? ringsFor : Mathf.Max(spot.RingsFor, ringsFor);
                return nearest;
            }
            SpaceBus free = null;
            // A Trail at its most spots: its oldest goes on to the next.
            if (trail != null && maxSpots > 0 && own >= maxSpots)
                free = oldest;
            if (free == null)
                foreach (var bus in _buses)
                    if (bus.Owner == null) { free = bus; break; }
            if (free == null && _buses.Count < _max)
                _buses.Add(free = SpaceBus.Create(_parent, _rate));
            if (free == null && trail != null)
                free = oldest; // the pool's full: the trail carries on with its own oldest
            if (free == null)
                return null; // all ringing: the sound keeps its own effect for now
            if (!SpaceBus.CanStart(free, kind == SpaceBus.BusKind.Slap ? "a slapback spot" : "a reverb spot"))
                return null; // no voice to spare: the same
            free.Owner = new Spot { Kind = kind, Position = position, RingsFor = ringsFor, Trail = trail, TakenAt = Time.realtimeSinceStartup };
            fresh = true;
            return free;
        }

        // Where it's heard from: placed there in the world, or (the preview) panned and faded from the listener's middle.
        private void Place(SpaceBus bus, Vector3 position)
        {
            if (!_flat) return;
            var flat = new Vector2(position.x, position.z);
            float distance = flat.magnitude;
            bus.SetFlat(distance > 0.01f ? flat.x / distance : 0f, 1f / Mathf.Max(1f, distance));
        }

        /// <summary>Lets go of spots that have rung out.</summary>
        public void Update()
        {
            foreach (var bus in _buses)
                if (bus.Owner is Spot spot && bus.IsQuiet(spot.RingsFor))
                    bus.End();
        }

        public void Destroy()
        {
            foreach (var bus in _buses)
                if (bus != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(bus.gameObject);
                    else UnityEngine.Object.DestroyImmediate(bus.gameObject);
                }
            _buses.Clear();
        }
    }

    /// <summary>
    /// An acoustic room: a stretch of a Sound Field's cells that are closed in and sound alike (found when the field is
    /// first used, no bake needed), which rings with one shared reverb.
    /// </summary>
    public sealed class AcousticRoom
    {
        public SoundField Field;
        public int Id;
        /// <summary>The middle and rough radius of its cells, in the world.</summary>
        public Vector3 Center;
        public float Radius;
        public int Cells;
        public SoundAcoustics Acoustics;
        /// <summary>Its reverb, while one's ringing for it.</summary>
        public SpaceBus Bus;
    }

    /// <summary>Where a Spatial reverb sends a block: a room, and up to <see cref="MaxEchoes"/> echo walls (each with its own delay).</summary>
    public sealed class SpaceSends
    {
        /// <summary>The most echo walls one sound is heard off at once: one each way (four sides, floor, ceiling).</summary>
        public const int MaxEchoes = SoundFieldData.WallsPerCell;

        public SpaceBus Room;
        public float RoomGain;
        /// <summary>The tail spot where the sound is (outside rooms): its reverb or slapback, left there.</summary>
        public SpaceBus Tail;
        public float TailGain;
        /// <summary>The echo walls it's heard off: <see cref="EchoCount"/> of them, each with its delay and gain.</summary>
        public readonly SpaceBus[] Echoes = new SpaceBus[MaxEchoes];
        public readonly int[] EchoDelays = new int[MaxEchoes];
        public readonly float[] EchoGains = new float[MaxEchoes];
        public int EchoCount;

        /// <summary>Adds an echo wall (ignored past <see cref="MaxEchoes"/>).</summary>
        public void AddEcho(SpaceBus bus, int delay, float gain)
        {
            if (bus == null || EchoCount >= MaxEchoes)
                return;
            Echoes[EchoCount] = bus;
            EchoDelays[EchoCount] = delay;
            EchoGains[EchoCount] = gain;
            EchoCount++;
        }
        /// <summary>Round a corner: the bus at the corner, how much later than the sound itself, and how loud.</summary>
        public SpaceBus Corner;
        public int CornerDelay;
        public float CornerGain;
    }

    /// <summary>
    /// Buses carrying sounds round corners (one per sound that's going round one), each moved to the corner its sound
    /// comes round. A sound keeps its bus by touching its claim each time it looks for its way round; a bus whose claim
    /// hasn't been touched for a second (the sound stopped, or its way is clear again) is let go once it's quiet.
    /// </summary>
    public sealed class CornerPool
    {
        /// <summary>A sound's hold on a bus.</summary>
        public sealed class Claim
        {
            public float TouchedAt;
        }

        private readonly Transform _parent;
        private readonly int _rate;
        private readonly bool _flat;
        private readonly int _max;
        private readonly List<SpaceBus> _buses = new();

        public CornerPool(Transform parent, int rate, bool flat, int max = 16)
        {
            _parent = parent;
            _rate = rate;
            _flat = flat;
            _max = max;
        }

        public IReadOnlyList<SpaceBus> Buses => _buses;

        /// <summary>The claim's bus (or a free one), at <paramref name="corner"/>, letting through up to <paramref name="cutoff"/> Hz.</summary>
        public SpaceBus Take(Claim claim, Vector3 corner, float cutoff)
        {
            claim.TouchedAt = Time.realtimeSinceStartup;
            SpaceBus bus = null, free = null;
            foreach (var candidate in _buses)
            {
                if (candidate.Owner == claim) { bus = candidate; break; }
                if (free == null && candidate.Owner == null) free = candidate;
            }
            if (bus == null)
            {
                bus = free;
                if (bus == null && _buses.Count < _max)
                    _buses.Add(bus = SpaceBus.Create(_parent, _rate));
                if (bus == null || !SpaceBus.CanStart(bus, "a sound round a corner"))
                    return null; // all busy (or no voice to spare): the sound is heard straight, through the walls
                bus.BeginCorner(claim, corner);
            }
            bus.SetCorner(cutoff);
            if (_flat)
            {
                // The preview: panned from the corner's direction around its listener (the falloff is in what's sent).
                var flat = new Vector2(corner.x, corner.z);
                float distance = flat.magnitude;
                bus.SetFlat(distance > 0.01f ? flat.x / distance : 0f, 1f);
            }
            else
                bus.MoveTo(corner);
            return bus;
        }

        /// <summary>Lets go of buses whose sound has stopped asking (once they're quiet).</summary>
        public void Update()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var bus in _buses)
                if (bus.Owner is Claim claim && now - claim.TouchedAt > 1f && bus.IsQuiet(0.3f))
                    bus.End();
        }

        public void Destroy()
        {
            foreach (var bus in _buses)
                if (bus != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(bus.gameObject);
                    else UnityEngine.Object.DestroyImmediate(bus.gameObject);
                }
            _buses.Clear();
        }
    }

    /// <summary>
    /// Runs the world's spaces (Play Mode): finds each Sound Field's acoustic rooms, keeps a ringing reverb for the
    /// rooms sounds are played in, and echo walls on the strongest echo anchors near the listener (with facing walls
    /// echoing into each other: a canyon's flutter). Made the first time something asks.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class SoundSpaces : MonoBehaviour
    {
        /// <summary>Most rooms ringing at once.</summary>
        public const int MaxRooms = 8;
        /// <summary>Most echo walls at once.</summary>
        public const int MaxEchoes = 24;
        /// <summary>How far from the listener echo walls are kept, in meters.</summary>
        public const float EchoRange = 220f;
        /// <summary>
        /// How far from the listener an echo wall's bus plays, in meters: from its wall's way round the listener, always
        /// this far, so how loud it is comes only from the whole way the sound went (not from how near the wall is), and it
        /// turns smoothly as the listener passes the wall rather than flipping from ear to ear.
        /// </summary>
        public const float EchoOrbit = 3f;
        public const float SpeedOfSound = 343f;
        /// <summary>Facing walls echo into each other only this far apart (in seconds of travel), this strongly.</summary>
        public const float MinFlutterDelay = 0.05f, FlutterGain = 0.25f;

        private static SoundSpaces _instance;

        /// <summary>
        /// Multiplies every spatial effect (sounds' place reverb and slapback, and what they send to rooms and echo walls),
        /// for hearing clearly whether they work. 1 is as designed. Set from the Sound Spaces debug window.
        /// </summary>
        public static float Exaggerate = 1f;

        /// <summary>
        /// Logs, twice a second for each Spatial effect, every echo wall its sound is heard off: the way round, how late,
        /// the gate, the edge fade and the loudness that come of it, and which bus (Sound Spaces window: Log Echoes). For
        /// finding where an echo dips or drops out as the listener moves.
        /// </summary>
        public static bool LogEchoes;

        /// <summary>The room reverbs (in use or idle).</summary>
        public IReadOnlyList<SpaceBus> RoomBuses => _rooms;
        /// <summary>The echo walls (in use or idle).</summary>
        public IReadOnlyList<SpaceBus> EchoBuses => _echoes;

        /// <summary>The spaces, if they've been made (doesn't make them).</summary>
        public static SoundSpaces Current => _instance;

        private TailPool _tails;
        /// <summary>Reverb and slapback tails left where sounds are (outside rooms).</summary>
        public TailPool Tails => _tails ??= new TailPool(transform, _rate, flat: false);
        private CornerPool _corners;
        /// <summary>Sounds coming round corners (Around Corners).</summary>
        public CornerPool Corners => _corners ??= new CornerPool(transform, _rate, flat: false);
        private readonly List<SpaceBus> _rooms = new();
        private readonly List<SpaceBus> _echoes = new();
        private readonly Dictionary<SoundField, (SoundFieldData data, int[] regionOf, List<AcousticRoom> rooms)> _fields = new();
        private readonly Dictionary<(SoundField, int), SpaceBus> _liveAnchors = new();
        private AudioListener _listener;
        private float _nextListenerSearch;
        private int _rate;

        /// <summary>The spaces (made on first use, in Play Mode only; null otherwise).</summary>
        public static SoundSpaces Get()
        {
            if (!Application.isPlaying)
                return null;
            if (_instance == null)
            {
                var host = new GameObject("Sound Spaces");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<SoundSpaces>();
                _instance._rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            }
            return _instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            Exaggerate = 1f;
            _nextVoiceWarning = 0f;
        }

        private static float _nextVoiceWarning;

        /// <summary>
        /// A spatial effect couldn't start for want of a voice: said at most every 10 seconds, with what holds them.
        /// (The sound itself still plays, without that effect.)
        /// </summary>
        internal static void OutOfVoices(string what)
        {
            if (Time.realtimeSinceStartup < _nextVoiceWarning && !SpaceBus.LogBuses) return;
            _nextVoiceWarning = Time.realtimeSinceStartup + 10f;
            int rooms = 0, walls = 0, spots = 0, corners = 0;
            if (_instance != null)
            {
                foreach (var bus in _instance._rooms) if (bus != null && bus.Owner != null) rooms++;
                foreach (var bus in _instance._echoes) if (bus != null && bus.Owner != null) walls++;
                if (_instance._tails != null) foreach (var bus in _instance._tails.Buses) if (bus != null && bus.Owner != null) spots++;
                if (_instance._corners != null) foreach (var bus in _instance._corners.Buses) if (bus != null && bus.Owner != null) corners++;
            }
            int real = AudioSettings.GetConfiguration().numRealVoices;
            Debug.LogWarning($"[Sound Spaces] No voice to spare for {what}: spatial effects already use {SpaceBus.Playing} of the "
                + $"{SpaceBus.VoiceBudget} they may ({rooms} rooms, {walls} echo walls, {spots} tail spots, {corners} corners; "
                + $"half of Unity's {real} real voices, the rest kept for ordinary sounds and music). The sound plays without it. "
                + "Raise Max Real Voices (Project Settings > Audio) for more.");
        }

        // Rooms and echo walls in the Scene view while playing: brighter the louder they are.
        private void OnDrawGizmos()
        {
            foreach (var bus in _rooms)
            {
                if (!(bus.Owner is AcousticRoom room)) continue;
                Gizmos.color = Color.Lerp(new Color(0.4f, 0.6f, 1f, 0.35f), new Color(1f, 0.4f, 0.8f, 1f), Mathf.Clamp01(bus.Peak * 4f));
                Gizmos.DrawWireSphere(room.Center, room.Radius);
            }
            foreach (var bus in _echoes)
            {
                if (bus.Owner == null) continue;
                Gizmos.color = Color.Lerp(new Color(1f, 0.85f, 0.3f, 0.4f), new Color(1f, 0.4f, 0.2f, 1f), Mathf.Clamp01(bus.Peak * 4f));
                Gizmos.DrawSphere(bus.HeardFrom, 1f + bus.Peak * 6f);
            }
        }

        public Vector3 ListenerPosition
        {
            get
            {
                if (_listener == null && Time.unscaledTime >= _nextListenerSearch)
                {
                    _listener = FindFirstObjectByType<AudioListener>();
                    _nextListenerSearch = Time.unscaledTime + 1f;
                }
                return _listener != null ? _listener.transform.position : Vector3.zero;
            }
        }

        public int Rate => _rate;

        #region Rooms

        /// <summary>
        /// The acoustic room a world position is in, ringing (a reverb found or started for it), or null when it's in
        /// none, or every room reverb is busy with nearer rooms.
        /// </summary>
        public AcousticRoom RoomAt(Vector3 world)
        {
            if (!SoundField.TryGetCell(world, out var field, out int cell))
                return null;
            var (_, regionOf, rooms) = RoomsOf(field);
            int id = regionOf != null && cell >= 0 && cell < regionOf.Length ? regionOf[cell] : -1;
            if (id < 0)
                return null;
            var room = rooms[id];
            if (room.Bus == null || room.Bus.Owner != room)
                room.Bus = RoomBus(room);
            return room.Bus != null ? room : null;
        }

        private SpaceBus RoomBus(AcousticRoom room)
        {
            SpaceBus bus = null;
            foreach (var candidate in _rooms)
                if (candidate.Owner == null) { bus = candidate; break; }
            if (bus == null && _rooms.Count < MaxRooms)
                _rooms.Add(bus = SpaceBus.Create(transform, _rate));
            if (bus == null)
            {
                // All busy: take the farthest room's, if it has gone quiet.
                var listener = ListenerPosition;
                float farthest = -1f;
                foreach (var candidate in _rooms)
                {
                    if (!(candidate.Owner is AcousticRoom other) || !IsQuiet(candidate, other.Acoustics.ReverbTime)) continue;
                    float distance = Vector3.Distance(listener, other.Center);
                    if (distance > farthest) { farthest = distance; bus = candidate; }
                }
                if (bus == null)
                    return null;
                if (bus.Owner is AcousticRoom previous)
                    previous.Bus = null;
            }
            if (!SpaceBus.CanStart(bus, "a room's reverb"))
                return null;
            bus.BeginRoom(room, room.Center, room.Radius);
            bus.SetRoom(room.Acoustics.Feedback, room.Acoustics.Damping);
            return bus;
        }

        // Quiet: nothing sent for longer than its reverb takes to die away.
        private static bool IsQuiet(SpaceBus bus, float tail) => bus.IsQuiet(tail);

        private (SoundFieldData data, int[] regionOf, List<AcousticRoom> rooms) RoomsOf(SoundField field)
        {
            if (_fields.TryGetValue(field, out var known) && known.data == field.Data)
                return known;
            var found = FindRooms(field);
            _fields[field] = found;
            return found;
        }

        /// <summary>Closed-in cells' share of rays that hit something, at least.</summary>
        public const float RoomEnclosure = 0.7f;

        // Neighbouring closed-in cells that sound alike (size within a factor of 1.7, absorption within 0.25), joined
        // into rooms; tiny ones (under 6 cells) are left out.
        private static (SoundFieldData, int[], List<AcousticRoom>) FindRooms(SoundField field)
        {
            var data = field.Data;
            var rooms = new List<AcousticRoom>();
            if (data == null || data.Count == 0)
                return (data, null, rooms);
            var cells = data.Cells;
            var regionOf = new int[data.Count];
            for (int i = 0; i < regionOf.Length; i++) regionOf[i] = -1;
            var queue = new Queue<int>();
            var members = new List<int>();
            var toWorld = field.transform.localToWorldMatrix;
            for (int seed = 0; seed < data.Count; seed++)
            {
                if (regionOf[seed] != -1 || !data.TryGet(seed, out var start) || start.Enclosure < RoomEnclosure)
                    continue;
                int id = rooms.Count;
                members.Clear();
                regionOf[seed] = id;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    members.Add(index);
                    data.TryGet(index, out var here);
                    int x = index % cells.x, y = index / cells.x % cells.y, z = index / (cells.x * cells.y);
                    for (int n = 0; n < 6; n++)
                    {
                        int nx = x + (n == 0 ? 1 : n == 1 ? -1 : 0), ny = y + (n == 2 ? 1 : n == 3 ? -1 : 0), nz = z + (n == 4 ? 1 : n == 5 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nz < 0 || nx >= cells.x || ny >= cells.y || nz >= cells.z) continue;
                        int next = data.Index(nx, ny, nz);
                        if (regionOf[next] != -1 || !data.TryGet(next, out var there) || there.Enclosure < RoomEnclosure) continue;
                        if (Mathf.Abs(Mathf.Log(Mathf.Max(0.5f, there.Size) / Mathf.Max(0.5f, start.Size))) > 0.53f) continue; // ~1.7x
                        if (Mathf.Abs(there.Absorption - start.Absorption) > 0.25f) continue;
                        regionOf[next] = id;
                        queue.Enqueue(next);
                    }
                }
                if (members.Count < 6)
                {
                    foreach (int m in members) regionOf[m] = -2; // looked at; not a room
                    continue;
                }
                var sum = new SoundAcoustics();
                var center = Vector3.zero;
                var min = Vector3.positiveInfinity;
                var max = Vector3.negativeInfinity;
                foreach (int m in members)
                {
                    data.TryGet(m, out var c);
                    sum.Size += c.Size; sum.Enclosure += c.Enclosure; sum.Absorption += c.Absorption; sum.Hardness += c.Hardness; sum.Wetness += c.Wetness;
                    int x = m % cells.x, y = m / cells.x % cells.y, z = m / (cells.x * cells.y);
                    var world = toWorld.MultiplyPoint3x4(data.CellCenter(x, y, z));
                    center += world;
                    min = Vector3.Min(min, world);
                    max = Vector3.Max(max, world);
                }
                float k = 1f / members.Count;
                rooms.Add(new AcousticRoom
                {
                    Field = field,
                    Id = id,
                    Cells = members.Count,
                    Center = center * k,
                    Radius = Mathf.Max(data.CellSize, (max - min).magnitude * 0.5f),
                    Acoustics = new SoundAcoustics { Size = sum.Size * k, Enclosure = sum.Enclosure * k, Absorption = sum.Absorption * k, Hardness = sum.Hardness * k, Wetness = sum.Wetness * k },
                });
            }
            // Cells of rooms too small to count read as "no room".
            for (int i = 0; i < regionOf.Length; i++)
                if (regionOf[i] < -1) regionOf[i] = -1;
            // Ids were handed out before small rooms were dropped: number the kept rooms' cells again.
            var renumber = new Dictionary<int, int>();
            for (int r = 0; r < rooms.Count; r++) { renumber[rooms[r].Id] = r; rooms[r].Id = r; }
            for (int i = 0; i < regionOf.Length; i++)
                if (regionOf[i] >= 0) regionOf[i] = renumber.TryGetValue(regionOf[i], out int kept) ? kept : -1;
            return (data, regionOf, rooms);
        }

        #endregion

        #region Echo walls

        /// <summary>The echo wall ringing for a field's anchor (its own, or the one it's joined to), if it's live (else null).</summary>
        public SpaceBus EchoBus(SoundField field, int anchor)
        {
            var key = (field, anchor);
            if (_liveAnchors.TryGetValue(key, out var bus) && bus != null && Equals(bus.Owner, key))
                return bus;
            return _joined.TryGetValue(key, out bus) && bus != null && bus.Owner != null ? bus : null;
        }

        /// <summary>
        /// Walls within this many degrees of each other, seen from the listener, share one echo wall's bus: each sound
        /// still sends to it with its own wall's delay (so its echoes come as late as they should), and the bus is
        /// heard from between them (nearer the louder). Two echoes from about the same way, close together, are heard
        /// as coming from one place anyway; walls on different sides of the listener keep their own.
        /// </summary>
        public const float JoinAngle = 35f;
        // Only walls that sound alike join: hardness this close (the bus has one darkness), distance from the listener
        // within this factor (it has one falloff). A joined wall that's come this far round (the listener moved) leaves.
        private const float JoinHardness = 0.35f, JoinDistance = 1.6f, LeaveAngle = JoinAngle * 1.5f;

        /// <summary>Whether walls join (see <see cref="JoinAngle"/>): on as designed; off to hear each wall on its own (Sound Spaces window).</summary>
        public static bool JoinEchoes = true;

        // Walls echoing through another wall's bus: which.
        private readonly Dictionary<(SoundField, int), SpaceBus> _joined = new();

        /// <summary>How many other walls echo through this wall's bus.</summary>
        public int JoinedTo(SpaceBus bus)
        {
            int count = 0;
            foreach (var pair in _joined)
                if (pair.Value == bus) count++;
            return count;
        }

        // A live echo wall heard from about the same way as a point on a wall, sounding alike: the wall joins it.
        private SpaceBus TryJoin((SoundField, int) key, Vector3 at, float hardness)
        {
            if (!JoinEchoes)
                return null;
            var listener = ListenerPosition;
            var toWall = at - listener;
            float distance = toWall.magnitude;
            if (distance < 0.5f)
                return null;
            SpaceBus best = null;
            float bestAngle = JoinAngle;
            foreach (var bus in _echoes)
            {
                if (!(bus.Owner is ValueTuple<SoundField, int>) || Mathf.Abs(bus.Hardness - Mathf.Clamp01(hardness)) > JoinHardness)
                    continue;
                var toBus = bus.HeardFrom - listener;
                float busDistance = toBus.magnitude;
                if (busDistance < 0.5f || Mathf.Max(distance, busDistance) / Mathf.Min(distance, busDistance) > JoinDistance)
                    continue;
                float angle = Vector3.Angle(toWall, toBus);
                if (angle < bestAngle) { bestAngle = angle; best = bus; }
            }
            if (best == null)
                return null;
            _joined[key] = best;
            if (SpaceBus.LogBuses)
                Debug.Log($"[Sound Spaces] {SpaceBus.Describe(key)} joins the echo bus of {SpaceBus.Describe(best.Owner)} ({bestAngle:0}° apart): {JoinedTo(best)} joined to it", best);
            return best;
        }

        // Lets an echo wall's bus go, and every wall joined to it.
        private void EndEcho(SpaceBus bus)
        {
            if (bus.Owner is ValueTuple<SoundField, int> key)
                _liveAnchors.Remove(key);
            _leaving.Clear();
            foreach (var pair in _joined)
                if (pair.Value == bus) _leaving.Add(pair.Key);
            foreach (var member in _leaving)
                _joined.Remove(member);
            bus.End();
        }
        private readonly List<(SoundField, int)> _leaving = new();

        // Anchors sounds have asked for lately (when), which are kept over ones merely near the listener.
        private readonly Dictionary<(SoundField, int), float> _asked = new();
        /// <summary>Seconds an anchor stays wanted after a sound last asked for it.</summary>
        public const float AskedFor = 3f;

        // Where on each wall it's heard from: the bounce point of the loudest sound asking for it lately (a wall is one
        // voice, so one place; the sounds sharing it are heard from where the loudest bounces).
        private readonly Dictionary<(SoundField, int), (Vector3 at, float weight)> _heardAt = new();

        /// <summary>
        /// The echo wall for a wall a sound wants to be heard off, at <paramref name="at"/> (where it bounces) as loud as
        /// <paramref name="weight"/>: the live one, or one made for it now if there's room (within reach of the
        /// listener); and it's kept while sounds keep asking.
        /// </summary>
        public SpaceBus RequestEcho(SoundField field, int anchor, Vector3 at, float weight)
        {
            var key = (field, anchor);
            _asked[key] = Time.unscaledTime;
            if (!_heardAt.TryGetValue(key, out var heard) || weight >= heard.weight)
                _heardAt[key] = (at, weight);
            var bus = EchoBus(field, anchor);
            if (bus != null || field?.Data?.Anchors == null || anchor < 0 || anchor >= field.Data.Anchors.Count)
                return bus;
            var data = field.Data.Anchors[anchor];
            var position = at;
            if (Vector3.Distance(ListenerPosition, position) > EchoRange)
                return null;
            var joined = TryJoin(key, position, data.Hardness);
            if (joined != null)
                return joined;
            int live = 0;
            foreach (var candidate in _echoes)
                if (candidate.Owner != null) live++;
            if (live >= MaxEchoes)
                return null; // the next update makes room, if it's wanted more than one that's ringing
            return StartEcho(key, position, data);
        }

        private SpaceBus StartEcho((SoundField, int) key, Vector3 position, SoundFieldAnchor data)
        {
            SpaceBus bus = null;
            foreach (var candidate in _echoes)
                if (candidate.Owner == null) { bus = candidate; break; }
            if (bus == null && _echoes.Count < MaxEchoes * 2)
                _echoes.Add(bus = SpaceBus.Create(transform, _rate));
            if (bus == null || !SpaceBus.CanStart(bus, "an echo wall"))
                return null;
            bus.BeginEcho(key, position, Mathf.Sqrt(data.Area), data.Hardness);
            _liveAnchors[key] = bus;
            _joined.Remove(key);
            return bus;
        }

        private float _nextEchoUpdate;
        private readonly Dictionary<SpaceBus, (Vector3 sum, float weight)> _targets = new();

        private void Update()
        {
            // Each live echo wall glides to where it's heard from (sudden jumps would throw its sound across the room):
            // the bounce points of the walls it carries (its own and those joined to it), weighted by how loud each is.
            // Its bus plays from that way round the listener, EchoOrbit away.
            float glide = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.08f);
            _targets.Clear();
            foreach (var pair in _heardAt)
            {
                var bus = EchoBus(pair.Key.Item1, pair.Key.Item2);
                if (bus == null) continue;
                float weight = pair.Value.weight + 1e-4f;
                _targets.TryGetValue(bus, out var target);
                _targets[bus] = (target.sum + pair.Value.at * weight, target.weight + weight);
            }
            var listener = ListenerPosition;
            foreach (var bus in _echoes)
            {
                if (bus == null || !(bus.Owner is ValueTuple<SoundField, int>)) continue;
                if (_targets.TryGetValue(bus, out var target))
                    bus.HeardFrom = Vector3.Lerp(bus.HeardFrom, target.sum / target.weight, glide);
                var way = bus.HeardFrom - listener;
                bus.transform.position = listener + (way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.forward) * EchoOrbit;
            }
            if (Time.unscaledTime < _nextEchoUpdate)
                return;
            _nextEchoUpdate = Time.unscaledTime + 0.25f;
            _tails?.Update();
            _corners?.Update();
            UpdateEchoes();
            ReleaseFarRooms();
        }

        // The strongest anchors near the listener get echo walls; the rest let theirs go once quiet.
        private void UpdateEchoes()
        {
            var listener = ListenerPosition;
            var wanted = new List<(float score, SoundField field, int anchor, Vector3 position, Vector3 normal, SoundFieldAnchor data)>();
            foreach (var field in SoundField.All)
            {
                var data = field != null ? field.Data : null;
                if (data?.Anchors == null || !field.isActiveAndEnabled) continue;
                var toWorld = field.transform.localToWorldMatrix;
                var toLocal = field.transform.worldToLocalMatrix;
                var localListener = toLocal.MultiplyPoint3x4(listener);
                for (int a = 0; a < data.Anchors.Count; a++)
                {
                    var anchor = data.Anchors[a];
                    // (Its nearest point to the listener: a long wall is near however far its middle is.)
                    var position = toWorld.MultiplyPoint3x4(NearestOn(anchor, localListener));
                    float distance = Vector3.Distance(listener, position);
                    if (distance > EchoRange) continue;
                    float score = anchor.Reflectivity * Mathf.Sqrt(anchor.Area) / (1f + distance / 30f);
                    // Walls sounds are being heard off come first.
                    if (_asked.TryGetValue((field, a), out float asked) && Time.unscaledTime - asked < AskedFor)
                        score += 1000f;
                    wanted.Add((score, field, a, position, toWorld.MultiplyVector(anchor.Normal).normalized, anchor));
                }
            }
            wanted.Sort((p, q) => q.score.CompareTo(p.score));
            if (wanted.Count > MaxEchoes)
                wanted.RemoveRange(MaxEchoes, wanted.Count - MaxEchoes);

            var keep = new HashSet<(SoundField, int)>();
            foreach (var w in wanted) keep.Add((w.field, w.anchor));
            // Joined walls no longer wanted, or come too far round from their bus (the listener moved), leave it (and
            // join another, or get their own, when next asked for).
            _leaving.Clear();
            foreach (var pair in _joined)
            {
                var bus = pair.Value;
                bool gone = bus == null || bus.Owner == null || !keep.Contains(pair.Key);
                if (!gone && _heardAt.TryGetValue(pair.Key, out var heard))
                    gone = Vector3.Angle(heard.at - listener, bus.HeardFrom - listener) > LeaveAngle;
                if (gone) _leaving.Add(pair.Key);
            }
            foreach (var key in _leaving)
                _joined.Remove(key);
            // Let go of buses no wall wanted echoes through any more (once they've rung out).
            var kept = new HashSet<SpaceBus>();
            foreach (var w in wanted)
            {
                var bus = EchoBus(w.field, w.anchor);
                if (bus != null) kept.Add(bus);
            }
            for (int b = 0; b < _echoes.Count; b++)
            {
                var bus = _echoes[b];
                if (bus.Owner is ValueTuple<SoundField, int> && !kept.Contains(bus) && IsQuiet(bus, 1.5f))
                    EndEcho(bus);
            }
            // Start the new ones (or join them to a live one heard from about the same way). Walls no sound has asked
            // for yet (started early, ready for the next sound) leave a quarter of the voices spatial effects may use for
            // rooms, spots, corners and the walls sounds ask for.
            foreach (var w in wanted)
            {
                if (EchoBus(w.field, w.anchor) != null) continue;
                if (TryJoin((w.field, w.anchor), w.position, w.data.Hardness) != null) continue;
                bool asked = _asked.TryGetValue((w.field, w.anchor), out float at) && Time.unscaledTime - at < AskedFor;
                if (!asked && SpaceBus.Playing >= SpaceBus.VoiceBudget * 3 / 4) break;
                if (StartEcho((w.field, w.anchor), w.position, w.data) == null) break;
            }
            // Where each is heard from is asked afresh each round (the loudest of this round's).
            var heardKeys = new List<(SoundField, int)>(_heardAt.Keys);
            foreach (var key in heardKeys)
            {
                if (EchoBus(key.Item1, key.Item2) == null) _heardAt.Remove(key);
                else _heardAt[key] = (_heardAt[key].at, 0f);
            }
            // Forget old asks.
            if (_asked.Count > 256)
            {
                var stale = new List<(SoundField, int)>();
                foreach (var pair in _asked)
                    if (Time.unscaledTime - pair.Value > AskedFor || pair.Key.Item1 == null) stale.Add(pair.Key);
                foreach (var key in stale) _asked.Remove(key);
            }
            // Facing walls echo into each other (a canyon's flutter): each to its nearest face-to-face partner.
            foreach (var w in wanted)
            {
                // (Set by the wall a bus is for, not those joined to it.)
                var bus = EchoBus(w.field, w.anchor);
                if (bus == null || !Equals(bus.Owner, (w.field, w.anchor))) continue;
                SpaceBus partner = null;
                float best = float.MaxValue;
                foreach (var other in wanted)
                {
                    if (other.field == w.field && other.anchor == w.anchor) continue;
                    var between = other.position - w.position;
                    float distance = between.magnitude;
                    if (distance < 4f || Vector3.Dot(w.normal, between) <= 0f || Vector3.Dot(other.normal, -between) <= 0f) continue;
                    var otherBus = EchoBus(other.field, other.anchor);
                    if (distance < best && otherBus != bus) { best = distance; partner = otherBus; }
                }
                // Only walls far enough apart for a distinct echo: close ones bouncing at each other just buzz.
                if (partner != null && best / SpeedOfSound >= MinFlutterDelay)
                    bus.SetPartner(partner, (int)(best / SpeedOfSound * _rate), FlutterGain * w.data.Reflectivity);
                else
                    bus.SetPartner(null, 1, 0f);
            }
        }

        // The point of a wall nearest a point (both in the field's space); an older bake's point anchor is just itself.
        private static Vector3 NearestOn(in SoundFieldAnchor wall, Vector3 point)
        {
            if (wall.Tangent.sqrMagnitude < 0.5f)
                return wall.Position;
            var u = wall.Tangent;
            var v = Vector3.Cross(wall.Normal, u);
            var offset = point - wall.Position;
            return wall.Position + u * Mathf.Clamp(Vector3.Dot(offset, u), -wall.HalfSize.x, wall.HalfSize.x)
                + v * Mathf.Clamp(Vector3.Dot(offset, v), -wall.HalfSize.y, wall.HalfSize.y);
        }

        private void ReleaseFarRooms()
        {
            var listener = ListenerPosition;
            foreach (var bus in _rooms)
            {
                if (!(bus.Owner is AcousticRoom room)) continue;
                if (room.Field == null || !room.Field.isActiveAndEnabled
                    || (Vector3.Distance(listener, room.Center) > room.Radius + 150f && IsQuiet(bus, room.Acoustics.ReverbTime)))
                {
                    room.Bus = null;
                    bus.End();
                }
            }
        }

        #endregion
    }
}
