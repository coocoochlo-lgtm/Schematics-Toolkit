using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A row of tabs over their contents. Each tab's content can be made the first time it's opened
/// (<see cref="Tab.UseFactory"/>). Collapsible: clicking the open tab folds it away (no tab open), like a dropdown,
/// and nothing is open to begin with. The tab row scrolls sideways when it doesn't fit: with a scrollbar above it
/// (topScrollBar) or below, and with the mouse wheel.
/// </summary>
public class TabView : VisualElement
{
    private readonly VisualElement _tabScrollBar;
    private readonly VisualElement _tabBar;
    private readonly VisualElement _actionBar;
    private readonly VisualElement _tabContentContainer;
    private readonly Dictionary<string, Tab> _tabContentMap = new();
    private readonly List<string> _order = new();
    private string _selectedTab;
    private readonly string _tabBarName = string.Empty;
    private readonly bool _hideLabels;
    private readonly Label _caption;

    /// <summary>The text at the right end of the tab row (the open tab's name, say); none when null.</summary>
    public string Caption
    {
        get => _caption?.text;
        set { if (_caption != null) _caption.text = value?.Trim() ?? ""; }
    }

    /// <summary>Clicking the open tab folds it away; nothing is open to begin with.</summary>
    public bool Collapsible { get; }

    /// <summary>The open tab's id, or null when none is (a collapsible one folded away).</summary>
    public string SelectedTab => _selectedTab;

    /// <summary>A tab was opened (its id), or the open one folded away (null).</summary>
    public event Action<string> SelectionChanged;

    public Tab this[string tabID] => _tabContentMap[tabID];

    /// <summary>Whether there's a tab with this id.</summary>
    public bool HasTab(string tabId) => _tabContentMap.ContainsKey(tabId);

    /// <summary>How many tabs there are.</summary>
    public int TabCount => _order.Count;

    public TabView(string labelText = "", string className = "", bool topScrollBar = false, bool hideTabLabels = false, bool collapsible = false)
    {
        _tabBarName = className.Replace(" ", "");
        _hideLabels = hideTabLabels;
        Collapsible = collapsible;

        style.flexDirection = FlexDirection.Column;

        var scroller = new FlippedHorizontalScrollView(scrollbarOnTop: topScrollBar);
        _tabScrollBar = scroller;
        _tabScrollBar.AddToClassList(className + "-tab-scrollbar");

        // The tabs, then any buttons that aren't tabs ("+"), in one scrolling row.
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 0, flexShrink = 0 } };
        _tabBar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 0, flexShrink = 0 } };
        _tabBar.AddToClassList(className + "-tabbar");
        _actionBar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 0, flexShrink = 0 } };
        row.Add(_tabBar);
        row.Add(_actionBar);
        scroller.Add(row);

        _tabContentContainer = new VisualElement
        {
            style =
            {
                flexDirection = FlexDirection.Column,
                flexGrow = 1
            }
        };
        _tabContentContainer.AddToClassList(className + "-tab-content");
        if (collapsible)
            _tabContentContainer.style.display = DisplayStyle.None;

        Add(_tabScrollBar);
        Add(_tabContentContainer);

        if (className != string.Empty)
        {
            var label = _caption = new Label(labelText)
            {
                pickingMode = PickingMode.Ignore,
                style = {
                    position = Position.Absolute,
                    right = 0,
                    bottom = 0
                }
            };
            _tabScrollBar.hierarchy.Add(label);
            label.AddToClassList(className + "-tab-label");
        }
    }

    public Tab AddTab(string tabId, Texture icon = null) => AddTab(tabId, icon, showLabel: !_hideLabels || icon == null);

    /// <summary>A tab, with its icon and, if <paramref name="showLabel"/>, its name beside it.</summary>
    public Tab AddTab(string tabId, Texture icon, bool showLabel)
    {
        if (_tabContentMap.ContainsKey(tabId))
            throw new ArgumentException($"Tab '{tabId}' already exists.");

        return new Tab(this, tabId, icon, showLabel);
    }

    /// <summary>A button in the tab row that isn't a tab (it does something when clicked: "+" adding one...), after the tabs.</summary>
    public Button AddActionButton(string text, string tooltip, Action clicked)
    {
        var button = new Button(clicked) { text = text, tooltip = tooltip };
        button.AddToClassList(_tabBarName + "-tab");
        button.AddToClassList(_tabBarName + "-tab-action");
        button.style.marginLeft = button.style.marginRight = 0;
        button.style.borderTopLeftRadius = button.style.borderTopRightRadius = 0;
        button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 0;
        button.style.borderBottomWidth = 0;
        _actionBar.Add(button);
        return button;
    }

    public Button GetTabButton(string tabID)
    {
        return this[tabID].Button;
    }

    public void RemoveTab(string tabId)
    {
        if (!_tabContentMap.TryGetValue(tabId, out var tab)) return;

        tab.Content.RemoveFromHierarchy();
        _tabContentMap.Remove(tabId);
        _order.Remove(tabId);
        tab.Button.RemoveFromHierarchy();

        if (_selectedTab == tabId)
        {
            _selectedTab = null;
            if (_order.Count > 0 && !Collapsible)
                SelectTab(_order[0]);
            else
                Collapse();
        }
    }

    public void SelectTab(string tabId)
    {
        if (!_tabContentMap.TryGetValue(tabId, out var selected)) return;

        foreach (var kvp in _tabContentMap)
        {
            kvp.Value.SetVisible(false);
            kvp.Value.Button.RemoveFromClassList(_tabBarName + "-tab-selected");
        }

        selected.EnsureLoaded();
        selected.SetVisible(true);
        selected.Button.AddToClassList(_tabBarName + "-tab-selected");
        _tabContentContainer.style.display = DisplayStyle.Flex;
        bool changed = _selectedTab != tabId;
        _selectedTab = tabId;
        if (changed)
            SelectionChanged?.Invoke(tabId);
    }

    /// <summary>Folds the open tab away: no tab open, no content shown.</summary>
    public void Collapse()
    {
        foreach (var kvp in _tabContentMap)
        {
            kvp.Value.SetVisible(false);
            kvp.Value.Button.RemoveFromClassList(_tabBarName + "-tab-selected");
        }
        _tabContentContainer.style.display = DisplayStyle.None;
        bool changed = _selectedTab != null;
        _selectedTab = null;
        if (changed)
            SelectionChanged?.Invoke(null);
    }

    // A tab's button was clicked: open it, or (collapsible) fold it away if it's the open one.
    private void Clicked(string tabId)
    {
        if (Collapsible && _selectedTab == tabId)
            Collapse();
        else
            SelectTab(tabId);
    }

    public class Tab : VisualElement
    {
        private bool _alreadyLoaded = false;
        private Func<VisualElement> _factory;
        public VisualElement Content;
        public Button Button;

        public Tab(TabView tabView, string tabId, Texture icon = null) : this(tabView, tabId, icon, !tabView._hideLabels || icon == null) { }

        public Tab(TabView tabView, string tabId, Texture icon, bool showLabel)
        {
            Content = new VisualElement();

            // === Tab Content ===
            Content.style.display = DisplayStyle.None;
            // (The content goes in the tab view, not in this, so Clear on a tab leaves it be.)
            tabView._tabContentContainer.Add(Content);
            tabView._tabContentMap[tabId] = this;
            tabView._order.Add(tabId);

            // === Tab Button ===
            Button = new Button
            {
                text = "",
                tooltip = tabId.Trim(),
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,

                    marginLeft = 0,
                    marginRight = 0,

                    borderTopLeftRadius = 0,
                    borderTopRightRadius = 0,
                    borderBottomLeftRadius = 0,
                    borderBottomRightRadius = 0,
                    borderBottomWidth = 0,
                    bottom = 0
                }
            };

            if (icon != null)
            {
                var img = new Image { image = icon, pickingMode = PickingMode.Ignore };
                img.style.width = 16;
                img.style.height = 16;
                if (showLabel)
                    img.style.marginRight = 3;
                Button.Add(img);
            }

            if (showLabel)
            {
                var label = new Label(tabId.Trim())
                {
                    pickingMode = PickingMode.Ignore,
                    style = { unityFontStyleAndWeight = FontStyle.Bold }
                };
                Button.Add(label);
            }

            Button.AddToClassList(tabView._tabBarName + "-tab");
            Button.RegisterCallback<MouseEnterEvent>(_ => Button.AddToClassList(tabView._tabBarName + "-tab-hover"));
            Button.RegisterCallback<MouseLeaveEvent>(_ => Button.RemoveFromClassList(tabView._tabBarName + "-tab-hover"));
            Button.clicked += () => tabView.Clicked(tabId);

            tabView._tabBar.Add(Button);

            // Auto-select first tab
            if (!tabView.Collapsible && tabView._order.Count == 1)
                tabView.SelectTab(tabId);
        }

        /// <summary>Its content, made the first time the tab is opened (<paramref name="reuseGenerated"/>: kept after).</summary>
        public void UseFactory(Func<VisualElement> factory, bool reuseGenerated = true)
        {
            _factory = factory;
            _alreadyLoaded = false;
            // (Already open: made now.)
            if (Content.style.display == DisplayStyle.Flex)
                EnsureLoaded();
        }

        internal void EnsureLoaded()
        {
            if (_alreadyLoaded || _factory == null)
                return;
            _alreadyLoaded = true;
            Content.Clear();
            var made = _factory();
            if (made != null)
                Content.Add(made);
        }

        public void SetVisible(bool visible)
        {
            Content.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Set(VisualElement content)
        {
            Content.Clear();
            Content.Add(content);
        }

        public new void Add(VisualElement content)
        {
            Content.Add(content);
        }
    }
}
