using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Remedy.Schematics
{
    /// <summary>
    /// The IO Dock, inside Unity's Hierarchy window: a "Hierarchy | IO Dock" switch over the Hierarchy, and the IO Dock
    /// (the Global, Prefab and Graph tabs of the graph window last used) covering the object list while it's chosen. Being
    /// part of the Hierarchy, it goes wherever the Hierarchy goes: Unity takes the Hierarchy down and puts it back when a
    /// window is maximized and restored, and the switch and dock come back with it, where they were. Unity has no public way
    /// to add to the Hierarchy, so they're put into its window by finding it (a few times a second, as it can rebuild at any
    /// time). Opening a graph switches to the IO Dock. Tools > Remedy > Schematics > IO Dock.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyIODock
    {
        private const string ShowingKey = "Remedy.Schematics.HierarchyIODock.Showing";
        private const string BarName = "schematics-iodock-switch";
        private const float BarHeight = 22f;

        // Unity 6.3's Hierarchy is "HierarchyWindow" (UI Toolkit), earlier versions' "SceneHierarchyWindow" (IMGUI).
        private static readonly Type HierarchyType = typeof(Editor).Assembly.GetType("UnityEditor.HierarchyWindow")
            ?? typeof(Editor).Assembly.GetType("UnityEditor.SceneHierarchyWindow");

        private static double _nextCheck;

        static HierarchyIODock() => EditorApplication.update += Update;

        /// <summary>The IO Dock is chosen in the Hierarchy (rather than the object list).</summary>
        internal static bool Showing
        {
            get => SessionState.GetBool(ShowingKey, false);
            private set
            {
                if (Showing == value)
                    return;
                SessionState.SetBool(ShowingKey, value);
                _nextCheck = 0;
                Update();
            }
        }

        /// <summary>An IO Dock is showing in a Hierarchy window: the graph windows' docks are its to show.</summary>
        internal static bool InUse => IODockPanel.Attached.Count > 0;

        [MenuItem("Tools/Remedy/Schematics/IO Dock", false, 21)]
        private static void Open() => Show(openHierarchy: true);

        /// <summary>
        /// Switches the Hierarchy to the IO Dock; <paramref name="openHierarchy"/>: the Hierarchy opened (and brought to the
        /// front) if it isn't. (With a window maximized, it shows once that's restored and the Hierarchy is back.)
        /// </summary>
        internal static void Show(bool openHierarchy)
        {
            Showing = true;
            if (openHierarchy && HierarchyType != null)
                EditorWindow.GetWindow(HierarchyType, false, null, true);
        }

        private static void Update()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck || HierarchyType == null)
                return;
            _nextCheck = EditorApplication.timeSinceStartup + 0.25;
            bool showing = Showing;
            foreach (var found in Resources.FindObjectsOfTypeAll(HierarchyType))
            {
                if (!(found is EditorWindow window) || window == null)
                    continue;
                var root = window.rootVisualElement;
                // (The UI Toolkit Hierarchy: once it has built its UI. The IMGUI one draws under rootVisualElement.)
                bool toolkit = window.GetType().Name == "HierarchyWindow";
                if (root == null || (toolkit && root.childCount == 0))
                    continue;
                var bar = root.Q<SwitchBar>(BarName);
                if (bar == null)
                {
                    bar = new SwitchBar(toolkit);
                    if (toolkit)
                        root.Insert(0, bar); // above the Hierarchy's toolbar
                    else
                        root.Add(bar); // over the bottom edge (the IMGUI Hierarchy can't be pushed down)
                }
                else if (toolkit && root.IndexOf(bar) != 0)
                    bar.SendToBack(); // (first again if the Hierarchy added to its top)
                bar.Refresh(showing);

                var panel = root.Q<IODockPanel>();
                if (showing && panel == null)
                {
                    panel = new IODockPanel();
                    panel.style.position = Position.Absolute;
                    panel.style.left = panel.style.right = 0;
                    panel.style.top = toolkit ? BarHeight : 0;
                    panel.style.bottom = toolkit ? 0 : BarHeight;
                    root.Add(panel);
                }
                else if (!showing)
                    panel?.RemoveFromHierarchy();
                if (panel != null && !toolkit)
                    bar.BringToFront();
            }
        }

        // "Hierarchy | IO Dock".
        private sealed class SwitchBar : VisualElement
        {
            private readonly Button _hierarchy, _dock;

            public SwitchBar(bool toolkit)
            {
                name = BarName;
                style.flexDirection = FlexDirection.Row;
                style.flexShrink = 0;
                style.height = BarHeight;
                style.paddingLeft = style.paddingRight = 4;
                style.paddingTop = style.paddingBottom = 2;
                style.backgroundColor = Background;
                if (toolkit)
                {
                    style.borderBottomWidth = 1;
                    style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);
                }
                else
                {
                    style.position = Position.Absolute;
                    style.left = style.right = style.bottom = 0;
                    style.borderTopWidth = 1;
                    style.borderTopColor = new Color(0f, 0f, 0f, 0.3f);
                }
                _hierarchy = Segment("Hierarchy", null, "The scene's objects.", () => Showing = false);
                _dock = Segment("IO Dock", SchematicTabIcons.Get("io-dock"), "The behaviors, Variables and Signals of what the graph window is on.", () => Showing = true);
                Add(_hierarchy);
                Add(_dock);
            }

            private static Button Segment(string text, Texture2D icon, string tooltip, Action click)
            {
                var button = new Button(click) { tooltip = tooltip };
                button.style.flexGrow = 1;
                button.style.flexDirection = FlexDirection.Row;
                button.style.justifyContent = Justify.Center;
                button.style.alignItems = Align.Center;
                button.style.marginLeft = button.style.marginRight = 0;
                button.style.marginTop = button.style.marginBottom = 0;
                if (icon != null)
                {
                    var image = new Image { image = icon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                    image.style.width = image.style.height = 14;
                    image.style.marginRight = 4;
                    button.Add(image);
                }
                button.Add(new Label(text) { pickingMode = PickingMode.Ignore });
                return button;
            }

            // The chosen side highlighted.
            public void Refresh(bool showing)
            {
                var accent = EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);
                foreach (var button in new[] { _hierarchy, _dock })
                {
                    bool on = (button == _dock) == showing;
                    button.style.backgroundColor = on ? accent : new StyleColor(StyleKeyword.Null);
                    button.style.color = on ? Color.white : new StyleColor(StyleKeyword.Null);
                    button.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        // The window background, so the dock covers the object list underneath.
        private static Color Background => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.78f, 0.78f, 0.78f);

        /// <summary>
        /// The IO Dock over the Hierarchy's object list: the dock of the graph window in use (the Graph tab, opened behind
        /// the Scene view, if no graph window is open), so dragging rows into the graph, finding their nodes and everything
        /// else works as in the graph window. Keys, commands and clicks stop here, so typing in it doesn't reach the
        /// Hierarchy (Delete deleting the selected objects...).
        /// </summary>
        private sealed class IODockPanel : VisualElement
        {
            internal static readonly List<IODockPanel> Attached = new List<IODockPanel>();

            private readonly VisualElement _holder;
            private readonly Label _empty;

            public IODockPanel()
            {
                style.backgroundColor = Background;
                var sheet = Resources.Load<StyleSheet>("IODock");
                if (sheet != null)
                    styleSheets.Add(sheet);
                Add(new SchematicTargetHeader(forDock: true));
                var scroll = new ScrollView(ScrollViewMode.Vertical);
                scroll.style.flexGrow = 1;
                scroll.AddToClassList("iodock");
                _holder = new VisualElement();
                scroll.Add(_holder);
                Add(scroll);
                _empty = new Label("Select a Schematic Object to see its behaviors, Variables and Signals here.");
                _empty.style.whiteSpace = WhiteSpace.Normal;
                _empty.style.opacity = 0.6f;
                _empty.style.paddingLeft = _empty.style.paddingRight = 12;
                _empty.style.paddingTop = 12;

                RegisterCallback<KeyDownEvent>(Stop);
                RegisterCallback<KeyUpEvent>(Stop);
                RegisterCallback<ValidateCommandEvent>(Stop);
                RegisterCallback<ExecuteCommandEvent>(Stop);
                RegisterCallback<PointerUpEvent>(Stop);
                RegisterCallback<ContextClickEvent>(Stop);
                RegisterCallback<DragUpdatedEvent>(Stop);
                RegisterCallback<DragPerformEvent>(Stop);
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    Attached.Add(this);
                    SchematicGraphEditorWindow.FollowerChanged += OnFollowerChanged;
                    // (Not while the Hierarchy is still being put together: opening the Graph tab then upsets the layout.)
                    EditorApplication.delayCall += Take;
                });
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    Attached.Remove(this);
                    SchematicGraphEditorWindow.FollowerChanged -= OnFollowerChanged;
                    // Switched back to the Hierarchy (or the Hierarchy taken down): the docks go back in their windows'
                    // sidebars, unless another IO Dock shows them.
                    foreach (var graphWindow in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
                        if (graphWindow != null)
                            graphWindow.ReturnDock();
                    foreach (var other in Attached)
                        other.Take();
                });
            }

            private static void Stop(EventBase evt) => evt.StopPropagation();

            private void OnFollowerChanged(SchematicGraphEditorWindow _) => Take();

            // The dock of the graph window in use, shown here.
            internal void Take()
            {
                if (panel == null)
                    return;
                var follower = SchematicGraphEditorWindow.ActiveDock;
                if (follower == null)
                    follower = SchematicGraphEditorWindow.EnsureFollower(focus: false);
                var tabs = follower != null ? follower.DockTabs : null;
                if (tabs == null)
                {
                    if (_empty.parent != _holder)
                    {
                        _holder.Clear();
                        _holder.Add(_empty);
                    }
                    return;
                }
                follower.LendDock();
                if (tabs.parent != _holder)
                {
                    _holder.Clear();
                    _holder.Add(tabs);
                }
            }
        }
    }
}
