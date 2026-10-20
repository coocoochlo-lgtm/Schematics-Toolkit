using Dreamteck.Splines;
using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>
    /// Spline lookups used by the spline Behaviors and graph nodes, also usable on their own (including with Dreamteck's
    /// own SplineFollower). Percents are Dreamteck's 0-1 position along a spline.
    /// </summary>
    public static class SplineQueries
    {
        /// <summary>The point on the spline closest to a world position.</summary>
        public static SplineSample Closest(this SplineComputer spline, Vector3 position)
        {
            var sample = new SplineSample();
            spline.Project(ref sample, position);
            return sample;
        }

        /// <summary>The 0-1 position on the spline closest to a world position.</summary>
        public static double ClosestPercent(this SplineComputer spline, Vector3 position) => spline.Closest(position).percent;

        /// <summary>
        /// The spline whose closest point is nearest to a position, among the given ones (or every spline in the loaded
        /// scenes if none are given). Null if there are none. Searching every spline is slow; pass a list when it's frequent.
        /// </summary>
        public static SplineComputer Nearest(Vector3 position, out SplineSample closest, SplineComputer[] candidates = null, SplineComputer exclude = null)
        {
            candidates ??= Object.FindObjectsByType<SplineComputer>(FindObjectsSortMode.None);
            SplineComputer best = null;
            closest = default;
            float bestDistance = float.MaxValue;
            foreach (var spline in candidates)
            {
                if (spline == null || spline == exclude || !spline.isActiveAndEnabled)
                    continue;
                var sample = spline.Closest(position);
                float distance = (sample.position - position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = spline;
                    closest = sample;
                }
            }
            return best;
        }

        /// <summary>
        /// Moves a Dreamteck follower onto another spline, at that spline's closest point to where the follower is now
        /// (branching paths).
        /// </summary>
        public static void JumpToClosest(this SplineFollower follower, SplineComputer other)
        {
            if (follower == null || other == null)
                return;
            double percent = other.ClosestPercent(follower.transform.position);
            follower.spline = other;
            follower.SetPercent(percent);
        }

        /// <summary>
        /// The position on the spline that is a straight-line <paramref name="distance"/> from the point at
        /// <paramref name="from"/>, going in <paramref name="direction"/> (for two points kept a set distance apart, like
        /// the wheels of a cart). Stops at the spline's end if it's too short.
        /// </summary>
        public static double PercentAtStraightDistance(this SplineComputer spline, double from, float distance, Spline.Direction direction)
        {
            if (distance <= 0f)
                return from;
            Vector3 origin = spline.EvaluatePosition(from);
            float squared = distance * distance;

            // Along the spline is never shorter than straight, so the answer lies at least `distance` along it.
            float low = 0f, high = distance;
            double end = direction == Spline.Direction.Forward ? 1.0 : 0.0;
            double highPercent = spline.Travel(from, high, out float moved, direction);
            while ((spline.EvaluatePosition(highPercent) - origin).sqrMagnitude < squared)
            {
                if (moved < high - 0.0001f || highPercent == end)
                    return highPercent; // ran out of spline
                low = high;
                high *= 2f;
                highPercent = spline.Travel(from, high, out moved, direction);
            }

            double best = highPercent;
            for (int i = 0; i < 18; i++)
            {
                float mid = (low + high) * 0.5f;
                double midPercent = spline.Travel(from, mid, direction);
                if ((spline.EvaluatePosition(midPercent) - origin).sqrMagnitude < squared)
                    low = mid;
                else
                {
                    high = mid;
                    best = midPercent;
                }
            }
            return best;
        }

        /// <summary>
        /// Moves <paramref name="percent"/> along the spline by a signed distance (negative goes backwards), wrapping at
        /// the ends as asked. Returns the new percent; <paramref name="hitEnd"/> is set when an end was reached, and
        /// <paramref name="reversed"/> when ping-ponging turned it around.
        /// </summary>
        public static double Advance(this SplineComputer spline, double percent, float distance, SplineWrap wrap, out bool hitEnd, out bool reversed)
        {
            hitEnd = false;
            reversed = false;
            if (distance == 0f)
                return percent;

            var direction = distance > 0f ? Spline.Direction.Forward : Spline.Direction.Backward;
            float remaining = Mathf.Abs(distance);

            // A few passes at most: a wrap can leave distance to travel from the other end.
            for (int pass = 0; pass < 4 && remaining > 0f; pass++)
            {
                percent = spline.Travel(percent, remaining, out float moved, direction);
                remaining -= moved;
                bool atEnd = direction == Spline.Direction.Forward ? percent >= 1.0 : percent <= 0.0;
                if (!atEnd || remaining <= 0.0001f)
                {
                    if (atEnd) hitEnd = true;
                    break;
                }

                hitEnd = true;
                switch (wrap)
                {
                    case SplineWrap.Loop:
                        percent = direction == Spline.Direction.Forward ? 0.0 : 1.0;
                        break;
                    case SplineWrap.PingPong:
                        direction = direction == Spline.Direction.Forward ? Spline.Direction.Backward : Spline.Direction.Forward;
                        reversed = !reversed;
                        break;
                    default:
                        return percent;
                }
            }
            return percent;
        }

        /// <summary>A world position offset from a spline sample: x to its right, y up, z forward.</summary>
        public static Vector3 Offset(this SplineSample sample, Vector3 offset)
            => sample.position + sample.right * offset.x + sample.up * offset.y + sample.forward * offset.z;
    }

    /// <summary>What a spline agent does at the ends of its spline.</summary>
    public enum SplineWrap
    {
        /// <summary>Stops at the end.</summary>
        Clamp,
        /// <summary>Starts again from the other end.</summary>
        Loop,
        /// <summary>Turns around.</summary>
        PingPong,
    }
}
