using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>The kinds of ruler the blockout editor can place.</summary>
    public enum BlockoutRulerKind
    {
        /// <summary>A tape measure between two points: total, horizontal and vertical distance. (Hold Shift while
        /// dragging one out to measure straight out of the surface it starts on: that covers what the old Height
        /// ruler did.)</summary>
        Distance = 0,
        // 1 was "Height", retired: Distance rulers do the same job. The numbers are fixed because scenes store
        // them; an old Height ruler loads as a Distance ruler (see BlockoutRuler.OnValidate).
        /// <summary>A character's running jump arc: green when it lands on something, red when it falls short.</summary>
        JumpArc = 2,
        /// <summary>How far a character runs in a number of seconds, ticked every second.</summary>
        RunDistance = 3,
    }

    /// <summary>
    /// A measurement kept in a blockout: the ruler starts at this object's position. Distance rulers end at
    /// <see cref="End"/>; Jump Arc and Run Distance rulers point along the object's forward axis and take their
    /// shape from <see cref="Metrics"/> (a "locked" ruler: it follows the character, not the mouse), so dragging one
    /// around the level shows at once whether a gap can be cleared or a run fits.<br/>
    /// Rulers are editor-only (their object is tagged EditorOnly, so builds leave them out); the drawing lives in the
    /// editor assembly.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Designer/Blockout Ruler")]
    public class BlockoutRuler : MonoBehaviour
    {
        public BlockoutRulerKind Kind = BlockoutRulerKind.Distance;

        /// <summary>The end point, relative to the ruler's position (world axes, not rotated). Distance rulers.</summary>
        public Vector3 End = new Vector3(0f, 0f, 4f);

        public Color Color = new Color(1f, 0.85f, 0.2f);

        /// <summary>An optional name shown next to the measurement.</summary>
        public string Label;

        /// <summary>Jump Arc / Run Distance: the character the ruler was made from (to refresh its numbers).</summary>
        public GameObject Character;

        /// <summary>Jump Arc / Run Distance: the character's movement numbers.</summary>
        public CharacterMetrics Metrics;

        /// <summary>Run Distance: how many seconds of running to show.</summary>
        [Min(0.1f)] public float Seconds = 1f;

        /// <summary>Jump Arc: how far below the take-off the arc is followed (for jumps down to lower ground).</summary>
        [Min(0f)] public float MaxDrop = 8f;

        /// <summary>Jump Arc: include a full hover after the top of the jump (characters that can hover).</summary>
        public bool WithHover;

        /// <summary>The world position the ruler ends at (Distance rulers).</summary>
        public Vector3 WorldEnd => transform.position + End;

        /// <summary>The value the retired Height kind was stored as.</summary>
        private const int RetiredHeightKind = 1;

#if UNITY_EDITOR
        // Height rulers were retired: a vertical Distance ruler measures the same thing (its End already points
        // straight up), so old ones simply become Distance rulers when their scene loads.
        private void OnValidate()
        {
            if ((int)Kind == RetiredHeightKind)
                Kind = BlockoutRulerKind.Distance;
        }
#endif
    }
}
