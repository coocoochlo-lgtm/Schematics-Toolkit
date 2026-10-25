using System;
using UnityEngine;

namespace BlueGraph
{
    /// <summary>
    /// A note placed on the canvas: a solid box of text for explanations (resizable; its text scrolls when it doesn't
    /// fit), in any color. Editor-only documentation, like comments, but it doesn't group the nodes around it.
    /// </summary>
    [Serializable]
    public class Note
    {
        [SerializeField] private string text;

        /// <summary>The note's text.</summary>
        public string Text
        {
            get { return text; }
            set { text = value; }
        }

        /// <summary>The color a new note gets: a soft sticky-note yellow.</summary>
        public static readonly Color DefaultColor = new Color32(250, 225, 120, 255);

        [SerializeField] private Color color = DefaultColor;

        /// <summary>The note's color, on the canvas and the minimap (always opaque). The palette is the comments'.</summary>
        public Color Color
        {
            get { return color.a > 0f ? color : DefaultColor; }
            set
            {
                value.a = 1f;
                color = value;
            }
        }

        /// <summary>The text size a new note gets.</summary>
        public const float DefaultFontSize = 26f;

        [SerializeField] private float fontSize = DefaultFontSize;

        /// <summary>The note's text size (notes saved before it had one get the default).</summary>
        public float FontSize
        {
            get { return fontSize > 0f ? fontSize : DefaultFontSize; }
            set { fontSize = Mathf.Clamp(value, 8f, 96f); }
        }

        [SerializeField] private Rect region;

        /// <summary>Where the note is on the canvas, and its size.</summary>
        public Rect Region
        {
            get { return region; }
            set { region = value; }
        }
    }
}
