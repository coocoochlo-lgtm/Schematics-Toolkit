using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// The Project Manager's sidebar width, shared by its pages (Schematics, Audio, Levels) so switching tabs doesn't
    /// move the layout. Pages put their sidebar in a <see cref="TwoPaneSplitView"/> (made by <see cref="Split"/>, or
    /// their own); <see cref="Follow"/> keeps any such split in step: dragging its divider changes the shared width.
    /// </summary>
    public static class ProjectManagerLayout
    {
        private const string SidebarWidthKey = "Remedy.ProjectManager.SidebarWidth";
        private const float MinWidth = 140f, MaxWidth = 480f;

        public static float SidebarWidth
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(SidebarWidthKey, 220f), MinWidth, MaxWidth);
            set => EditorPrefs.SetFloat(SidebarWidthKey, Mathf.Clamp(value, MinWidth, MaxWidth));
        }

        /// <summary>A sidebar and its content side by side, the sidebar at the shared width (drag the divider to change it).</summary>
        public static TwoPaneSplitView Split(VisualElement sidebar, VisualElement content)
        {
            var split = new TwoPaneSplitView(0, SidebarWidth, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("project-manager__split");
            split.Add(sidebar);
            split.Add(content);
            Follow(split);
            return split;
        }

        /// <summary>
        /// Puts every horizontal split under <paramref name="root"/> at the shared width and makes them update it when
        /// their divider is dragged. Safe to call repeatedly (pages that rebuild get their new splits picked up).
        /// </summary>
        public static void SyncAll(VisualElement root)
        {
            if (root == null) return;
            root.Query<TwoPaneSplitView>().ForEach(split =>
            {
                if (split.orientation == TwoPaneSplitViewOrientation.Horizontal && split.fixedPaneIndex == 0)
                    Follow(split);
            });
        }

        private sealed class Followed { }

        private static void Follow(TwoPaneSplitView split)
        {
            var pane = split.fixedPane;
            if (pane == null)
            {
                // Panes are known once the split is laid out.
                split.schedule.Execute(() => Follow(split)).ExecuteLater(10);
                return;
            }
            float width = SidebarWidth;
            if (Mathf.Abs(pane.resolvedStyle.width - width) > 0.5f)
                pane.style.width = width;
            if (pane.userData is Followed)
                return;
            pane.userData = new Followed();
            pane.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                // A hidden page lays out at zero width; only real widths count.
                if (evt.newRect.width >= MinWidth && Mathf.Abs(evt.newRect.width - evt.oldRect.width) > 0.5f && evt.oldRect.width > 0f)
                    SidebarWidth = evt.newRect.width;
            });
        }
    }
}
