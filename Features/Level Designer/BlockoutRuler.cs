using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>The kinds of ruler the blockout editor can place.</summary>
    public enum BlockoutRulerKind
    {
        /// <summary>A tape measure between two points: total, horizontal and vertical distance.</summary>
        Distance,
        /// <summary>A height pole from the surface below up to a height.</summary>
        Height,
        /// <summary>A character's running jump arc: green when it lands on something, red when it falls short.</summary>
        JumpArc,
        /// <summary>How far a character runs in a number of seconds, ticked every second.</summary>
        RunDistance,
    }

    /// <summary>
    /// A measurement kept in a blockout: the ruler starts at this object's position. Distance and Height rulers end
    /// at <see cref="End"/>; Jump Arc and Run Distance rulers point along the object's forward axis and take their
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

        /// <summary>The end point, relative to the ruler's position (world axes, not rotated). Distance and Height.</summary>
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

        /// <summary>The world position the ruler ends at (Distance and Height).</summary>
        public Vector3 WorldEnd => transform.position + End;
    }
}
