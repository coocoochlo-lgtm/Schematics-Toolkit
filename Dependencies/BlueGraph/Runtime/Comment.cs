using System;
using UnityEngine;

namespace BlueGraph
{
    public enum CommentTheme
    {
        Primary = 0,
        Secondary = 1,
        Tertiary = 3
    }
       
    /// <summary>
    /// Comments placed within the CanvasView to document and group placed nodes.
    /// 
    /// These are typically ignored during runtime and only used for documentation.
    /// </summary>
    [Serializable]
    public class Comment
    {
        [SerializeField] private string text;

        /// <summary>
        /// Comment content
        /// </summary>
        public string Text
        {
            get { return text; }
            set { text = value; }
        }
        
        [SerializeField] private CommentTheme theme;

        /// <summary>
        /// Theme used to display the comment in CanvasView
        /// </summary>
        public CommentTheme Theme
        {
            get { return theme; }
            set { theme = value; }
        }

        /// <summary>The color a new comment gets: the warm yellow BlueGraph comments have always had.</summary>
        public static readonly Color DefaultColor = new Color32(253, 199, 39, 255);

        /// <summary>A few ready-made colors offered in the comment's menu (any other color can be picked too).</summary>
        public static readonly (string name, Color color)[] Palette =
        {
            ("Yellow", DefaultColor),
            ("Orange", (Color)new Color32(240, 128, 40, 255)),
            ("Red", (Color)new Color32(200, 45, 45, 255)),
            ("Pink", (Color)new Color32(225, 90, 160, 255)),
            ("Purple", (Color)new Color32(140, 90, 210, 255)),
            ("Blue", (Color)new Color32(60, 130, 225, 255)),
            ("Teal", (Color)new Color32(40, 170, 160, 255)),
            ("Green", (Color)new Color32(80, 175, 70, 255)),
            ("Gray", (Color)new Color32(130, 130, 130, 255)),
        };

        // Comments saved before colors existed have no color in their data, which loads as transparent black: those
        // keep the look their old theme gave them (see Color).
        [SerializeField] private Color color = DefaultColor;

        /// <summary>
        /// The comment's color: it tints the comment on the canvas (a solid title bar over a see-through body) and
        /// its box on the minimap. Always opaque; the views decide how see-through each part is.
        /// </summary>
        public Color Color
        {
            get
            {
                if (color.a > 0f)
                    return color;
                switch (theme)
                {
                    case CommentTheme.Secondary: return new Color32(148, 0, 8, 255);
                    case CommentTheme.Tertiary: return new Color32(40, 40, 40, 255);
                    default: return DefaultColor;
                }
            }
            set
            {
                value.a = 1f;
                color = value;
            }
        }

        [SerializeField] private bool fixedSize;

        /// <summary>
        /// When false (the default), the comment resizes itself to fit the nodes inside it whenever they move or are added.
        /// </summary>
        public bool FixedSize
        {
            get { return fixedSize; }
            set { fixedSize = value; }
        }

        [SerializeField] private Rect region;

        /// <summary>
        /// Region covered by this comment in the CanvasView
        /// </summary>
        public Rect Region
        {
            get { return region; }
            set { region = value; }
        }
    }
}
