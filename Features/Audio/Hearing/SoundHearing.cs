using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// Something AI can hear: a Sound Cue playing (each one is, by its Output's Heard By AI range), or a noise made
    /// with Make Noise. It's heard while it lasts.
    /// </summary>
    public sealed class SoundNoise
    {
        /// <summary>Where it is (following <see cref="Follow"/> while that's set).</summary>
        public Vector3 Position;
        public Transform Follow;
        /// <summary>How far away it can be heard in the open, in meters.</summary>
        public float Range;
        /// <summary>What made it (AI don't hear their own), and a word saying what it is ("footstep", "gunshot").</summary>
        public GameObject Source;
        public string Kind;
        /// <summary>When it started, and until when it's heard (game time, seconds; infinity: until <see cref="End"/>).</summary>
        public float Started, Until;

        public bool Ended => Time.time > Until;

        /// <summary>Stops it being heard.</summary>
        public void End() => Until = Mathf.Min(Until, Time.time);

        internal void Track()
        {
            if (Follow != null)
                Position = Follow.position;
        }
    }

    /// <summary>What an ear heard: the loudest noise, and how it got there.</summary>
    public struct HeardNoise
    {
        public SoundNoise Noise;
        /// <summary>How loud it is here, 0 (not heard) to 1 (right beside it).</summary>
        public float Loudness;
        /// <summary>How muffled, 0 (clear) to 1 (only through walls).</summary>
        public float Muffled;
        public SoundRoute Route;
        /// <summary>Where it's heard from: the noise itself, or the corner or doorway it comes round.</summary>
        public Vector3 HeardFrom;
        /// <summary>How far it travelled to get here (the whole way round, when it went round).</summary>
        public float Distance;
        /// <summary>The doorway it came through nearest the ear (-1: none), in <see cref="Field"/>'s doorways.</summary>
        public int Doorway;
        public SoundField Field;
    }

    /// <summary>
    /// What AI hear. Sounds (Sound Cues as they play, and Make Noise) leave noises; an ear asks for the loudest it can
    /// hear. Each noise reaches the ear the way sound would, through the Sound Field (<see cref="SoundPaths"/>): straight,
    /// round a corner or through a doorway (heard from there, quieter for the longer way and a little muffled), or
    /// only through the walls (much quieter, fully muffled). So AI know not just that something made a noise, but
    /// which way the opening is that let them hear it.
    /// </summary>
    public static class SoundHearing
    {
        /// <summary>How loud what gets through a wall is, next to hearing it in the open.</summary>
        public const float ThroughWalls = 0.25f;
        /// <summary>The most noises kept at once (the oldest go first).</summary>
        public const int MaxNoises = 256;

        private static readonly List<SoundNoise> _noises = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _noises.Clear();

        /// <summary>The noises being heard now.</summary>
        public static IReadOnlyList<SoundNoise> Noises
        {
            get
            {
                Prune();
                return _noises;
            }
        }

        /// <summary>
        /// Makes a noise AI can hear for <paramref name="duration"/> seconds (infinity: until it's ended), audible up
        /// to <paramref name="range"/> meters away in the open.
        /// </summary>
        public static SoundNoise Make(Vector3 position, float range, float duration, GameObject source = null, string kind = null, Transform follow = null)
        {
            if (range <= 0f || !Application.isPlaying)
                return null;
            Prune();
            if (_noises.Count >= MaxNoises)
                _noises.RemoveAt(0);
            var noise = new SoundNoise
            {
                Position = position, Follow = follow, Range = range, Source = source, Kind = string.IsNullOrEmpty(kind) ? null : kind,
                Started = Time.time, Until = float.IsPositiveInfinity(duration) ? float.PositiveInfinity : Time.time + Mathf.Max(0.05f, duration),
            };
            _noises.Add(noise);
            return noise;
        }

        private static void Prune()
        {
            for (int i = _noises.Count - 1; i >= 0; i--)
                if (_noises[i].Ended)
                    _noises.RemoveAt(i);
        }

        /// <summary>
        /// The loudest noise an ear at <paramref name="ear"/> hears (<paramref name="sensitivity"/> scales every
        /// noise's range; <paramref name="self"/>'s own noises, and those of its children, aren't heard; with a
        /// <paramref name="kind"/>, only noises of that kind). False when it hears nothing.
        /// </summary>
        public static bool Loudest(Vector3 ear, float sensitivity, GameObject self, out HeardNoise heard, string kind = null)
        {
            heard = default;
            heard.Doorway = -1;
            Prune();
            bool any = false;
            foreach (var noise in _noises)
            {
                if (!string.IsNullOrEmpty(kind) && noise.Kind != kind)
                    continue;
                if (self != null && noise.Source != null && (noise.Source == self || noise.Source.transform.IsChildOf(self.transform)))
                    continue;
                noise.Track();
                if (Hear(noise, ear, sensitivity, out var candidate) && (!any || candidate.Loudness > heard.Loudness))
                {
                    heard = candidate;
                    any = true;
                }
            }
            return any;
        }

        /// <summary>How one noise reaches an ear (false: too far or too quiet to hear).</summary>
        public static bool Hear(SoundNoise noise, Vector3 ear, float sensitivity, out HeardNoise heard)
        {
            heard = default;
            heard.Noise = noise;
            heard.Doorway = -1;
            float range = noise.Range * Mathf.Max(0.01f, sensitivity);
            float straight = Vector3.Distance(ear, noise.Position);
            // Out of reach even in a straight line: nothing to work out.
            if (straight > range)
                return false;
            // From the noise's end (its flood is shared by every ear listening for it).
            var way = SoundPaths.Between(ear, noise.Position);
            heard.Route = way.Route;
            heard.Field = way.Field;
            heard.Doorway = way.DoorwayA;
            switch (way.Route)
            {
                case SoundRoute.Around:
                    heard.Distance = way.Length;
                    heard.HeardFrom = way.CornerA;
                    // A sharper bend loses more, and dulls it.
                    heard.Loudness = Falloff(way.Length, range) * Mathf.Lerp(1f, 0.5f, way.BendA);
                    heard.Muffled = Mathf.Clamp01(way.BendA * 1.2f) * 0.6f;
                    break;
                case SoundRoute.Sealed:
                    heard.Distance = straight;
                    heard.HeardFrom = noise.Position;
                    heard.Loudness = Falloff(straight, range) * ThroughWalls;
                    heard.Muffled = 1f;
                    break;
                default:
                    heard.Distance = straight;
                    heard.HeardFrom = noise.Position;
                    heard.Loudness = Falloff(straight, range);
                    break;
            }
            return heard.Loudness > 0.001f;
        }

        // 1 right beside it, fading to 0 at its range (steeper near, like sound does).
        private static float Falloff(float distance, float range)
        {
            float t = Mathf.Clamp01(distance / Mathf.Max(0.01f, range));
            return (1f - t) * (1f - t);
        }

        /// <summary>
        /// The doorways nearest an ear that it can see straight (through the Sound Field's cells), nearest first, up to
        /// <paramref name="into"/>'s length; returns how many. For AI that want to know the ways out of where they are.
        /// </summary>
        public static int Openings(Vector3 ear, float range, (SoundField field, int doorway, float distance)[] into)
        {
            int count = 0;
            if (into == null || into.Length == 0)
                return 0;
            foreach (var field in SoundField.All)
            {
                if (field == null || !field.isActiveAndEnabled || field.Data?.Doorways == null || !field.Contains(ear))
                    continue;
                for (int d = 0; d < field.Data.Doorways.Count; d++)
                {
                    if (!SoundPaths.TryGetDoorway(field, d, out var position, out _, out _))
                        continue;
                    float distance = Vector3.Distance(ear, position);
                    if (distance > range || (count == into.Length && distance >= into[count - 1].distance))
                        continue;
                    if (!SoundPaths.InSight(field, ear, position))
                        continue;
                    // Into its place among the nearest.
                    int at = Mathf.Min(count, into.Length - 1);
                    while (at > 0 && into[at - 1].distance > distance)
                    {
                        into[at] = into[at - 1];
                        at--;
                    }
                    into[at] = (field, d, distance);
                    if (count < into.Length) count++;
                }
            }
            return count;
        }
    }
}
