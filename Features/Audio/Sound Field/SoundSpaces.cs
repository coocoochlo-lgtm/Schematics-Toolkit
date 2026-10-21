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
    public sealed class SpaceBus : MonoBehaviour
    {
        private const int RingSize = 1 << 18; // ~5 s at 48 kHz
        private const int RingMask = RingSize - 1;

        private readonly float[] _ring = new float[RingSize];
        private readonly object _lock = new();
        private long _readHead = -1;
        private long _lastWrite = long.MinValue;
        private int _rate;

        // Room: a reverb. Echo: a soft lowpass and two short allpasses (a wall smears an echo a little).
        public bool IsRoom { get; private set; }
        private ReverbCore _reverb;
        private float _feedback = 0.85f, _damp = 0.15f;
        private float _lowpass, _lowpassAmount = 0.5f;
        private float[] _allpassA = new float[1], _allpassB = new float[1];
        private int _allpassIndexA, _allpassIndexB;

        // Flutter: what an echo wall sends on to the wall facing it.
        private SpaceBus _partner;
        private int _partnerDelay;
        private float _partnerGain;
        private float[] _scratch = new float[2048];

        /// <summary>The room this bus is ringing for, or the field and anchor it echoes for (set by the manager).</summary>
        public object Owner;
        public AudioSource Source { get; private set; }

        internal static SpaceBus Create(Transform parent, int rate)
        {
            var host = new GameObject("Space Bus");
            host.transform.SetParent(parent, false);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.clip = DcClip(rate);
            source.spatialBlend = 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
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
            bus._allpassA = new float[Mathf.Max(1, (int)(0.0047f * rate))];
            bus._allpassB = new float[Mathf.Max(1, (int)(0.0113f * rate))];
            host.SetActive(false);
            return bus;
        }

        // A clip of ones: Unity places and attenuates it, and the filter below multiplies in the bus's own sound, so it
        // comes out placed and attenuated the same.
        private static AudioClip _dc;
        private static AudioClip DcClip(int rate)
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
            IsRoom = true;
            Source.minDistance = Mathf.Max(2f, radius);
            Source.maxDistance = Source.minDistance * 10f + 60f;
            Source.spread = 180f;
            Source.Play();
        }

        /// <summary>Starts it echoing as a wall, at a place, about <paramref name="size"/> meters across.</summary>
        internal void BeginEcho(object owner, Vector3 position, float size, float hardness)
        {
            Begin(owner, position);
            IsRoom = false;
            Source.minDistance = Mathf.Clamp(size, 3f, 40f);
            Source.maxDistance = 600f;
            Source.spread = 60f;
            SetEcho(hardness);
            Source.Play();
        }

        private void Begin(object owner, Vector3 position)
        {
            Owner = owner;
            transform.position = position;
            lock (_lock)
            {
                Array.Clear(_ring, 0, _ring.Length);
                _readHead = -1;
                _reverb.Clear();
                _lowpass = 0f;
                Array.Clear(_allpassA, 0, _allpassA.Length);
                Array.Clear(_allpassB, 0, _allpassB.Length);
                _partner = null;
            }
            Interlocked.Exchange(ref _lastWrite, long.MinValue);
            gameObject.SetActive(true);
        }

        internal void End()
        {
            Owner = null;
            if (Source != null) Source.Stop();
            gameObject.SetActive(false);
        }

        /// <summary>A room's reverb: <paramref name="feedback"/> (how long) and <paramref name="damping"/> (0 bright, 1 dull).</summary>
        internal void SetRoom(float feedback, float damping)
        {
            _feedback = Mathf.Clamp(feedback, 0f, 0.985f);
            _damp = Mathf.Clamp01(damping) * 0.4f;
        }

        // An echo wall: harder walls throw back brighter echoes.
        internal void SetEcho(float hardness)
        {
            float cutoff = Mathf.Lerp(1800f, 11000f, Mathf.Clamp01(hardness));
            _lowpassAmount = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Mathf.Max(8000, _rate));
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

        /// <summary>The audio-clock sample it's playing now (below 0 before it starts).</summary>
        public long ReadHead => Interlocked.Read(ref _readHead);

        /// <summary>The audio-clock sample of the latest send (long.MinValue when none).</summary>
        public long LastWrite => Interlocked.Read(ref _lastWrite);

        public int Rate => _rate;

        /// <summary>
        /// Adds a block to what the bus will play at audio-clock sample <paramref name="at"/> on (any thread), its gain
        /// gliding from <paramref name="gainFrom"/> to <paramref name="gainTo"/>. Too late (already played) or too far
        /// ahead: dropped.
        /// </summary>
        public void Add(long at, float[] block, int count, float gainFrom, float gainTo)
        {
            if (count <= 0 || (gainFrom == 0f && gainTo == 0f))
                return;
            lock (_lock)
            {
                long head = _readHead;
                if (head < 0 || at < head || at + count >= head + RingSize - 4096)
                    return;
                float step = (gainTo - gainFrom) / count;
                for (int i = 0; i < count; i++)
                    _ring[(at + i) & RingMask] += block[i] * (gainFrom + step * i);
            }
            long end = at + count;
            long last;
            do { last = Interlocked.Read(ref _lastWrite); }
            while (end > last && Interlocked.CompareExchange(ref _lastWrite, end, last) != last);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            if (_scratch.Length < frames)
                _scratch = new float[frames];
            SpaceBus partner;
            int partnerDelay;
            float partnerGain;
            long head;
            lock (_lock)
            {
                if (_readHead < 0)
                    _readHead = (long)Math.Round(AudioSettings.dspTime * _rate);
                head = _readHead;
                for (int f = 0; f < frames; f++)
                {
                    int slot = (int)((head + f) & RingMask);
                    float input = _ring[slot];
                    _ring[slot] = 0f;
                    float output;
                    if (IsRoom)
                        output = _reverb.Process(input * 0.015f, _feedback, _damp) * 3f;
                    else
                    {
                        _lowpass += (input - _lowpass) * _lowpassAmount;
                        output = Allpass(Allpass(_lowpass, _allpassA, ref _allpassIndexA), _allpassB, ref _allpassIndexB);
                    }
                    _scratch[f] = float.IsNaN(output) || float.IsInfinity(output) ? 0f : output;
                }
                _readHead = head + frames;
                partner = _partner;
                partnerDelay = _partnerDelay;
                partnerGain = _partnerGain;
            }
            for (int f = 0; f < frames; f++)
            {
                float sample = _scratch[f];
                for (int c = 0; c < channels; c++)
                    data[f * channels + c] *= sample; // the DC clip's placed, attenuated ones, times the bus's sound
            }
            // A canyon: the echo carries on to the facing wall (outside this bus's lock).
            if (partner != null && partnerGain > 0f)
                partner.Add(head + partnerDelay, _scratch, frames, partnerGain, partnerGain);
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

    /// <summary>Where a Spatial reverb sends a block: a room, and up to two echo walls (each with its own delay).</summary>
    public sealed class SpaceSends
    {
        public SpaceBus Room;
        public float RoomGain;
        public SpaceBus EchoA, EchoB;
        public int DelayA, DelayB;
        public float GainA, GainB;
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
        public const int MaxRooms = 6;
        /// <summary>Most echo walls at once.</summary>
        public const int MaxEchoes = 8;
        /// <summary>How far from the listener echo walls are kept, in meters.</summary>
        public const float EchoRange = 220f;
        public const float SpeedOfSound = 343f;

        private static SoundSpaces _instance;
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
        private static void ResetStatics() => _instance = null;

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
            bus.BeginRoom(room, room.Center, room.Radius);
            bus.SetRoom(room.Acoustics.Feedback, room.Acoustics.Damping);
            return bus;
        }

        // Quiet: nothing sent for longer than its reverb takes to die away.
        private bool IsQuiet(SpaceBus bus, float tail)
        {
            long last = bus.LastWrite, head = bus.ReadHead;
            return last == long.MinValue || head < 0 || head - last > (long)((tail * 1.2f + 0.5f) * _rate);
        }

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

        /// <summary>The echo wall ringing for a field's anchor, if it's one of the live ones near the listener (else null).</summary>
        public SpaceBus EchoBus(SoundField field, int anchor)
            => _liveAnchors.TryGetValue((field, anchor), out var bus) && bus != null && Equals(bus.Owner, (field, anchor)) ? bus : null;

        private float _nextEchoUpdate;

        private void Update()
        {
            if (Time.unscaledTime < _nextEchoUpdate)
                return;
            _nextEchoUpdate = Time.unscaledTime + 0.25f;
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
                for (int a = 0; a < data.Anchors.Count; a++)
                {
                    var anchor = data.Anchors[a];
                    var position = toWorld.MultiplyPoint3x4(anchor.Position);
                    float distance = Vector3.Distance(listener, position);
                    if (distance > EchoRange) continue;
                    float score = anchor.Reflectivity * Mathf.Sqrt(anchor.Area) / (1f + distance / 30f);
                    wanted.Add((score, field, a, position, toWorld.MultiplyVector(anchor.Normal).normalized, anchor));
                }
            }
            wanted.Sort((p, q) => q.score.CompareTo(p.score));
            if (wanted.Count > MaxEchoes)
                wanted.RemoveRange(MaxEchoes, wanted.Count - MaxEchoes);

            var keep = new HashSet<(SoundField, int)>();
            foreach (var w in wanted) keep.Add((w.field, w.anchor));
            // Let go of walls no longer wanted (once they've rung out).
            foreach (var bus in _echoes)
                if (bus.Owner is ValueTuple<SoundField, int> key && !keep.Contains(key) && IsQuiet(bus, 1.5f))
                {
                    _liveAnchors.Remove(key);
                    bus.End();
                }
            // Start the new ones.
            foreach (var w in wanted)
            {
                var key = (w.field, w.anchor);
                if (EchoBus(w.field, w.anchor) != null) continue;
                SpaceBus bus = null;
                foreach (var candidate in _echoes)
                    if (candidate.Owner == null) { bus = candidate; break; }
                if (bus == null && _echoes.Count < MaxEchoes * 2)
                    _echoes.Add(bus = SpaceBus.Create(transform, _rate));
                if (bus == null) break;
                bus.BeginEcho(key, w.position, Mathf.Sqrt(w.data.Area), w.data.Hardness);
                _liveAnchors[key] = bus;
            }
            // Facing walls echo into each other (a canyon's flutter): each to its nearest face-to-face partner.
            foreach (var w in wanted)
            {
                var bus = EchoBus(w.field, w.anchor);
                if (bus == null) continue;
                SpaceBus partner = null;
                float best = float.MaxValue;
                foreach (var other in wanted)
                {
                    if (other.field == w.field && other.anchor == w.anchor) continue;
                    var between = other.position - w.position;
                    float distance = between.magnitude;
                    if (distance < 4f || Vector3.Dot(w.normal, between) <= 0f || Vector3.Dot(other.normal, -between) <= 0f) continue;
                    if (distance < best) { best = distance; partner = EchoBus(other.field, other.anchor); }
                }
                if (partner != null)
                    bus.SetPartner(partner, (int)(best / SpeedOfSound * _rate), 0.35f * w.data.Reflectivity);
                else
                    bus.SetPartner(null, 1, 0f);
            }
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
