using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>
    /// How a character moves, in plain numbers a level designer or animator can use: how fast it runs, how high and
    /// far it jumps, how long it stays in the air. Worked out from a character prefab's movement tuning by the
    /// editor's Character Metrics analyzer (or typed in by hand for characters it doesn't know), and stored on
    /// locked rulers so a jump arc keeps the shape it had even if the prefab isn't loaded.<br/>
    /// Heights are measured at the feet: 0 is the ground the jump starts from.
    /// </summary>
    [Serializable]
    public struct CharacterMetrics
    {
        /// <summary>Which character the numbers are for.</summary>
        public string CharacterName;

        /// <summary>Top running speed (m/s).</summary>
        [Header("Ground movement")]
        public float MoveSpeed;
        /// <summary>Speed gained per second when starting to run (m/s²).</summary>
        public float Acceleration;
        /// <summary>Speed lost per second when letting go (m/s²).</summary>
        public float Deceleration;
        /// <summary>Seconds from standing to top speed.</summary>
        public float TimeToTopSpeed;
        /// <summary>Meters covered while speeding up to top speed.</summary>
        public float DistanceToTopSpeed;
        /// <summary>Meters slid after letting go at top speed.</summary>
        public float StopDistance;

        /// <summary>How far the feet rise during a full jump (m).</summary>
        [Header("Jump")]
        public float JumpHeight;
        /// <summary>The upward speed while rising (m/s).</summary>
        public float RiseSpeed;
        /// <summary>Seconds spent rising.</summary>
        public float RiseTime;
        /// <summary>Seconds held at the top of the jump.</summary>
        public float HangTime;
        /// <summary>Downward acceleration while falling (m/s²), gravity included.</summary>
        public float FallAcceleration;
        /// <summary>Fastest fall speed (m/s); 0 when there's no limit.</summary>
        public float MaxFallSpeed;
        /// <summary>Seconds in the air for a jump that lands at the height it started from.</summary>
        public float AirTime;
        /// <summary>Meters travelled forward by a jump at top speed that lands at the starting height.</summary>
        public float JumpDistance;
        /// <summary>Minimum seconds between landing and the next jump.</summary>
        public float JumpCooldown;

        [Header("Hover (if the character has it)")]
        public bool CanHover;
        /// <summary>Seconds a hover lasts.</summary>
        public float HoverTime;
        /// <summary>Forward speed while hovering (m/s).</summary>
        public float HoverSpeed;
        /// <summary>Sinking speed while hovering (m/s).</summary>
        public float HoverFallSpeed;
        /// <summary>Extra meters a full hover adds after the top of a jump.</summary>
        public float HoverDistance;

        /// <summary>Gravity used for the numbers (m/s², positive).</summary>
        [Header("Source")]
        public float Gravity;
        /// <summary>The physics step the numbers were worked out with (forces in the controller are per step).</summary>
        public float FixedTimeStep;
        /// <summary>The character's collider height, for headroom checks (m); 0 if unknown.</summary>
        public float BodyHeight;

        /// <summary>True when the numbers have been filled in.</summary>
        public bool IsValid => MoveSpeed > 0f || JumpHeight > 0f;

        /// <summary>Meters covered in <paramref name="seconds"/> of running from a standstill.</summary>
        public float RunDistance(float seconds)
        {
            if (MoveSpeed <= 0f)
                return 0f;
            if (Acceleration <= 0f || seconds >= TimeToTopSpeed)
                return DistanceToTopSpeed + MoveSpeed * Mathf.Max(0f, seconds - TimeToTopSpeed);
            return 0.5f * Acceleration * seconds * seconds;
        }

        /// <summary>
        /// The feet height (relative to take-off) <paramref name="t"/> seconds after jumping, optionally hovering after
        /// the hang time. Keeps falling below 0, for jumps down to lower ground.
        /// </summary>
        public float HeightAt(float t, bool hover = false)
        {
            if (t <= RiseTime)
                return RiseTime > 0f ? JumpHeight * (t / RiseTime) : JumpHeight;
            t -= RiseTime;
            if (t <= HangTime)
                return JumpHeight;
            t -= HangTime;

            float height = JumpHeight;
            if (hover && CanHover)
            {
                float hoverT = Mathf.Min(t, HoverTime);
                height -= HoverFallSpeed * hoverT;
                t -= hoverT;
                if (t <= 0f)
                    return height;
            }

            float a = Mathf.Max(0.01f, FallAcceleration);
            if (MaxFallSpeed > 0f)
            {
                float reach = MaxFallSpeed / a;
                if (t > reach)
                    return height - 0.5f * a * reach * reach - MaxFallSpeed * (t - reach);
            }
            return height - 0.5f * a * t * t;
        }

        /// <summary>The forward distance <paramref name="t"/> seconds after a running jump.</summary>
        public float ForwardAt(float t, bool hover = false)
        {
            if (!hover || !CanHover)
                return MoveSpeed * t;
            float beforeHover = RiseTime + HangTime;
            if (t <= beforeHover)
                return MoveSpeed * t;
            float hoverT = Mathf.Min(t - beforeHover, HoverTime);
            float after = Mathf.Max(0f, t - beforeHover - HoverTime);
            return MoveSpeed * beforeHover + HoverSpeed * hoverT + MoveSpeed * after;
        }

        /// <summary>
        /// Points along a running jump from <paramref name="start"/> (the feet) in <paramref name="forward"/>
        /// direction, until the feet are <paramref name="maxDrop"/> below the start.
        /// </summary>
        public void SampleArc(Vector3 start, Vector3 forward, List<Vector3> points, float maxDrop = 10f, bool hover = false, float step = 0.02f)
        {
            points.Clear();
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            float maxTime = 30f;
            for (float t = 0f; t <= maxTime; t += step)
            {
                float y = HeightAt(t, hover);
                points.Add(start + forward * ForwardAt(t, hover) + Vector3.up * y);
                if (y < -maxDrop)
                    break;
            }
        }

        /// <summary>One line of the CSV export (matching <see cref="CsvHeader"/>).</summary>
        public string ToCsvRow()
        {
            string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string name = (CharacterName ?? "").Replace("\"", "\"\"");
            return $"\"{name}\",{F(MoveSpeed)},{F(Acceleration)},{F(Deceleration)},{F(TimeToTopSpeed)},{F(DistanceToTopSpeed)},{F(StopDistance)}," +
                   $"{F(JumpHeight)},{F(RiseSpeed)},{F(RiseTime)},{F(HangTime)},{F(FallAcceleration)},{F(MaxFallSpeed)},{F(AirTime)},{F(JumpDistance)},{F(JumpCooldown)}," +
                   $"{(CanHover ? 1 : 0)},{F(HoverTime)},{F(HoverSpeed)},{F(HoverFallSpeed)},{F(HoverDistance)},{F(Gravity)},{F(FixedTimeStep)},{F(BodyHeight)}";
        }

        /// <summary>The CSV export's header row.</summary>
        public const string CsvHeader =
            "Character,MoveSpeed,Acceleration,Deceleration,TimeToTopSpeed,DistanceToTopSpeed,StopDistance," +
            "JumpHeight,RiseSpeed,RiseTime,HangTime,FallAcceleration,MaxFallSpeed,AirTime,JumpDistance,JumpCooldown," +
            "CanHover,HoverTime,HoverSpeed,HoverFallSpeed,HoverDistance,Gravity,FixedTimeStep,BodyHeight";
    }
}
