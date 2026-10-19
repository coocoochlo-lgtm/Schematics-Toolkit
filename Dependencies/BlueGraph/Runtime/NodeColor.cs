using System;
using UnityEngine;

namespace BlueGraph
{
    /// <summary>
    /// Colors a node's title bar in the graph editor, so kinds of node are easy to tell apart at a glance. Takes a
    /// hex color ("#B5413A") or one of the <see cref="NodeColors"/>. Inherited: put it on a base class to color every
    /// node derived from it, and on a node to override that.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class NodeColorAttribute : Attribute
    {
        public Color Color { get; }

        /// <param name="hex">An HTML color: "#RRGGBB", "#RRGGBBAA" or a name like "red".</param>
        public NodeColorAttribute(string hex)
        {
            Color = ColorUtility.TryParseHtmlString(hex, out var color) ? color : new Color(0.47f, 0.67f, 1f);
        }

        /// <param name="r">Red, 0 to 1.</param>
        /// <param name="g">Green, 0 to 1.</param>
        /// <param name="b">Blue, 0 to 1.</param>
        public NodeColorAttribute(float r, float g, float b)
        {
            Color = new Color(r, g, b);
        }
    }

    /// <summary>
    /// The title bar colors the toolkit's nodes use, by kind of node, for <see cref="NodeColorAttribute"/>. Use these
    /// for new nodes so the same kind always looks the same.
    /// </summary>
    public static class NodeColors
    {
        /// <summary>Where flow starts: On Start, On Collision, On Signal Received...</summary>
        public const string Event = "#B5413A";
        /// <summary>Steers the flow: If, Random, Split, Switch, loops...</summary>
        public const string Flow = "#7E57C2";
        /// <summary>True/false checks: Comparison, Greater Than, Is Null...</summary>
        public const string Condition = "#B8862B";
        /// <summary>Does something: the default for nodes the flow runs.</summary>
        public const string Action = "#3D78C2";
        /// <summary>Gives a value: the default for nodes with no flow arrows (numbers, strings, lists, casts...).</summary>
        public const string Value = "#3E8E5E";
        /// <summary>Works numbers out: math, logic, vectors.</summary>
        public const string Math = "#2A8A8A";
        /// <summary>Scene things: GameObjects, transforms, tags, colliders, splines, levels.</summary>
        public const string Object = "#C06A32";
        /// <summary>Time and timelines: Wait, Every X Seconds, Timeline playback.</summary>
        public const string Time = "#4F5FC4";
        /// <summary>Sound and music.</summary>
        public const string Audio = "#C2508C";
        /// <summary>AI: brains and senses.</summary>
        public const string AI = "#2E9DB5";
        /// <summary>Schematic plumbing: Behaviors, variables, signals sent, prefab objects.</summary>
        public const string Schematic = "#6F8F2F";
        /// <summary>Debugging help: Log...</summary>
        public const string Debug = "#5F6670";
    }
}
