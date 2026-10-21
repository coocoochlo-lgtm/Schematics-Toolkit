using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Audio
{
    /// <summary>
    /// The look of a Sound Cue's controls and Output, in the style of the Send / Receive Signal nodes: a tinted glass
    /// card with a glossy icon over its top-left corner (each control its own icon and color). The icons are SVGs in
    /// Features/Audio/Editor/Icons~ (make_icons.py), rendered into Resources/SoundCueIcons.
    /// </summary>
    internal static class SoundNodeLook
    {
        private static readonly Dictionary<Type, (string icon, string color)> Looks = new()
        {
            { typeof(SoundCueOutput), ("output", "#ff4fa0") },
            { typeof(SoundTimeNode), ("time", "#17b3a6") },
            { typeof(SoundDistanceNode), ("distance", "#1d9fe0") },
            { typeof(SoundParameterNode), ("parameter", "#f0a01c") },
            { typeof(SoundRandomNode), ("random", "#9a5cf0") },
            { typeof(SoundCurveNode), ("curve", "#38bf5c") },
            { typeof(SoundOscillatorNode), ("oscillator", "#ff7a2e") },
            { typeof(SoundOcclusionNode), ("occlusion", "#6f7fa8") },
            { typeof(SoundSurfaceNode), ("surface", "#c47838") },
            { typeof(SoundRoomNode), ("room", "#5a68f0") },
        };

        private static readonly Dictionary<string, Texture2D> _icons = new();
        private static Texture2D _gradient;

        public static Color Tint(Node node)
            => Looks.TryGetValue(node.GetType(), out var look) && ColorUtility.TryParseHtmlString(look.color, out var color) ? color : Color.gray;

        /// <summary>Dresses a node view: <paramref name="big"/> for the Output.</summary>
        public static void Apply(NodeView view, bool big)
        {
            if (!Looks.TryGetValue(view.Target.GetType(), out var look))
                return;
            var tint = Tint(view.Target);
            view.AddToClassList("sound-cue-control");
            view.style.overflow = Overflow.Visible;
            if (big)
                view.style.minWidth = 200;

            // The card: rounded, edged in the node's color, glassy.
            var card = view.mainContainer;
            card.style.overflow = Overflow.Visible;
            Radius(card, 12);
            Border(card, 1.5f, new Color(tint.r, tint.g, tint.b, 0.85f));
            card.style.backgroundColor = new Color(tint.r * 0.35f, tint.g * 0.35f, tint.b * 0.35f, 0.55f);
            _gradient ??= Resources.Load<Texture2D>("Textures/io-graph-gradient");
            if (_gradient != null)
                card.style.backgroundImage = _gradient;
            var selection = view.Q("selection-border");
            if (selection != null)
                Radius(selection, 13);
            var contents = view.Q("contents");
            if (contents != null)
            {
                contents.style.borderBottomLeftRadius = contents.style.borderBottomRightRadius = 11;
                contents.style.overflow = Overflow.Hidden;
            }

            // The title: clear, taller, with the icon hanging over the corner.
            var title = view.titleContainer;
            title.style.overflow = Overflow.Visible;
            title.style.backgroundImage = StyleKeyword.None;
            title.style.height = big ? 50 : 38;
            title.style.borderTopLeftRadius = title.style.borderTopRightRadius = 11;
            title.style.backgroundColor = new Color(tint.r, tint.g, tint.b, 0.18f);

            var icon = new VisualElement { name = "sound-cue-icon", pickingMode = PickingMode.Ignore };
            icon.style.backgroundImage = Icon(look.icon);
            icon.style.width = big ? 66 : 50;
            icon.style.height = big ? 55 : 42;
            icon.style.flexShrink = 0;
            icon.style.marginLeft = big ? -18 : -14;
            icon.style.marginTop = big ? -14 : -10;
            icon.style.marginRight = 2;
            title.Insert(0, icon);

            var label = title.Q<Label>("title-label");
            if (label != null)
            {
                label.style.color = Color.white;
                label.style.fontSize = big ? 15 : 12;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.marginLeft = 0;
                label.style.paddingLeft = 0;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
        }

        private static Texture2D Icon(string name)
        {
            if (!_icons.TryGetValue(name, out var icon) || icon == null)
                _icons[name] = icon = Resources.Load<Texture2D>("SoundCueIcons/" + name);
            return icon;
        }

        public static void Border(VisualElement element, float width, Color color)
        {
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
        }

        private static void Radius(VisualElement element, float radius)
            => element.style.borderTopLeftRadius = element.style.borderTopRightRadius
                = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }

    /// <summary>A Sound Cue control (Time, Distance, Parameter, Random, Curve, Oscillator, Occlusion, Surface, Room).</summary>
    [CustomNodeView(typeof(SoundTimeNode))]
    [CustomNodeView(typeof(SoundDistanceNode))]
    [CustomNodeView(typeof(SoundParameterNode))]
    [CustomNodeView(typeof(SoundRandomNode))]
    [CustomNodeView(typeof(SoundCurveNode))]
    [CustomNodeView(typeof(SoundOscillatorNode))]
    [CustomNodeView(typeof(SoundOcclusionNode))]
    [CustomNodeView(typeof(SoundSurfaceNode))]
    [CustomNodeView(typeof(SoundRoomNode))]
    public class SoundControlNodeView : NodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            SoundNodeLook.Apply(this, big: false);
        }
    }

    /// <summary>The Output: the biggest card, its edge pulsing while the cue is previewed.</summary>
    [CustomNodeView(typeof(SoundCueOutput))]
    public class SoundOutputNodeView : NodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            SoundNodeLook.Apply(this, big: true);
            var tint = SoundNodeLook.Tint(Target);
            bool wasPlaying = false;
            schedule.Execute(() =>
            {
                bool playing = SoundCuePreview.Playing != null && SoundCuePreview.Playing == Target.Graph;
                if (!playing && !wasPlaying)
                    return;
                wasPlaying = playing;
                float glow = playing ? 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 6f) : 0f;
                SoundNodeLook.Border(mainContainer, 1.5f + glow * 1.5f, Color.Lerp(new Color(tint.r, tint.g, tint.b, 0.85f), Color.white, glow * 0.6f));
            }).Every(50);
        }
    }
}
