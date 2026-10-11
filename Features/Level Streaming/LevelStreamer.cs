using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// Shows and hides level chunks for the zones their targets are in. Showing or hiding a big chunk at once can
    /// cause a frame spike, so changes are queued and applied a few objects at a time within
    /// <see cref="FrameBudgetMilliseconds"/> per frame.
    /// </summary>
    public static class LevelStreamer
    {
        /// <summary>Time per frame spent showing and hiding chunk contents.</summary>
        public static float FrameBudgetMilliseconds = 1.5f;

        /// <summary>How often zones are checked against the targets, in seconds.</summary>
        public static float ZoneCheckInterval = 0.2f;

        /// <summary>Positions that show chunks. With none, the main camera is used.</summary>
        public static readonly List<Transform> Targets = new List<Transform>();

        private static readonly List<LevelStreamingZone> _zones = new List<LevelStreamingZone>();
        // How many occupied zones want each chunk.
        private static readonly Dictionary<LevelChunk, int> _wanted = new Dictionary<LevelChunk, int>();

        private struct Work
        {
            public LevelChunk Chunk;
            public bool Show;
            public int Next;     // next child / renderer to process
        }

        private static readonly List<Work> _queue = new List<Work>();
        private static StreamerRunner _runner;
        private static float _nextZoneCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Targets.Clear();
            _zones.Clear();
            _wanted.Clear();
            _queue.Clear();
            _runner = null;
            _nextZoneCheck = 0f;
        }

        internal static void Register(LevelStreamingZone zone)
        {
            if (!_zones.Contains(zone))
                _zones.Add(zone);
            EnsureRunner();
        }

        internal static void Unregister(LevelStreamingZone zone)
        {
            if (zone.Occupied)
                Release(zone);
            _zones.Remove(zone);
        }

        /// <summary>Asks for a chunk to be shown or hidden (hiding is skipped while an occupied zone wants it).</summary>
        public static void Request(LevelChunk chunk, bool show)
        {
            if (chunk == null)
                return;
            if (!show && _wanted.TryGetValue(chunk, out int count) && count > 0)
                return;
            SetVisible(chunk, show, immediate: false);
        }

        /// <summary>Shows or hides a chunk now (immediate) or spread over the next frames.</summary>
        public static void SetVisible(LevelChunk chunk, bool show, bool immediate)
        {
            if (chunk == null)
                return;
            // A newer request for the same chunk replaces any queued one.
            for (int i = _queue.Count - 1; i >= 0; i--)
                if (_queue[i].Chunk == chunk)
                    _queue.RemoveAt(i);

            if (chunk.Visible == show && !immediate)
                return;
            chunk.Visible = show;

            var work = new Work { Chunk = chunk, Show = show };
            if (immediate)
            {
                while (!Step(ref work, long.MaxValue, Stopwatch.StartNew())) { }
                return;
            }
            _queue.Add(work);
            EnsureRunner();
        }

        private static void EnsureRunner()
        {
            if (_runner != null || !Application.isPlaying)
                return;
            var go = new GameObject("Level Streamer") { hideFlags = HideFlags.HideInHierarchy };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<StreamerRunner>();
        }

        internal static void Tick()
        {
            if (Time.unscaledTime >= _nextZoneCheck)
            {
                _nextZoneCheck = Time.unscaledTime + ZoneCheckInterval;
                CheckZones();
            }

            if (_queue.Count == 0)
                return;
            var clock = Stopwatch.StartNew();
            long budget = (long)(FrameBudgetMilliseconds * Stopwatch.Frequency / 1000f);
            while (_queue.Count > 0 && clock.ElapsedTicks < budget)
            {
                var work = _queue[0];
                bool done = Step(ref work, budget, clock);
                if (done) _queue.RemoveAt(0);
                else _queue[0] = work;
            }
        }

        // Applies as much of one chunk's change as the budget allows. True when finished.
        private static bool Step(ref Work work, long budget, Stopwatch clock)
        {
            var chunk = work.Chunk;
            if (chunk == null)
                return true;

            if (chunk.Hiding == LevelChunk.HideMode.RenderersOnly)
            {
                var renderers = chunk.Renderers;
                for (; work.Next < renderers.Count; work.Next++)
                {
                    if (renderers[work.Next] != null)
                        renderers[work.Next].enabled = work.Show;
                    if ((work.Next & 15) == 15 && clock.ElapsedTicks >= budget)
                    {
                        work.Next++;
                        return false;
                    }
                }
                return true;
            }

            var root = chunk.transform;
            for (; work.Next < root.childCount; work.Next++)
            {
                root.GetChild(work.Next).gameObject.SetActive(work.Show);
                if (clock.ElapsedTicks >= budget)
                {
                    work.Next++;
                    return false;
                }
            }
            return true;
        }

        private static void CheckZones()
        {
            Transform fallback = Targets.Count == 0 && Camera.main != null ? Camera.main.transform : null;
            float now = Time.unscaledTime;

            foreach (var zone in _zones)
            {
                if (zone == null)
                    continue;
                bool inside = false;
                if (fallback != null)
                    inside = zone.Contains(fallback.position);
                else
                    foreach (var target in Targets)
                        if (target != null && zone.Contains(target.position)) { inside = true; break; }

                if (inside)
                {
                    zone.LeftAt = -1f;
                    if (!zone.Occupied)
                        Occupy(zone);
                }
                else if (zone.Occupied)
                {
                    if (zone.LeftAt < 0f)
                        zone.LeftAt = now;
                    else if (now - zone.LeftAt >= zone.ExitDelay)
                        Release(zone);
                }
            }
        }

        private static void Occupy(LevelStreamingZone zone)
        {
            zone.Occupied = true;
            foreach (var chunk in zone.Chunks)
            {
                if (chunk == null) continue;
                _wanted.TryGetValue(chunk, out int count);
                _wanted[chunk] = count + 1;
                if (count == 0)
                    SetVisible(chunk, true, immediate: false);
            }
        }

        private static void Release(LevelStreamingZone zone)
        {
            zone.Occupied = false;
            zone.LeftAt = -1f;
            foreach (var chunk in zone.Chunks)
            {
                if (chunk == null) continue;
                _wanted.TryGetValue(chunk, out int count);
                count = Mathf.Max(0, count - 1);
                _wanted[chunk] = count;
                if (count == 0)
                    SetVisible(chunk, false, immediate: false);
            }
        }

        private sealed class StreamerRunner : MonoBehaviour
        {
            private void Update() => Tick();
        }
    }
}
