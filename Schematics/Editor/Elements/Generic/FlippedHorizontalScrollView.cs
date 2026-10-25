using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// A row that scrolls sideways when it's wider than its room, with a slim scrollbar above it (or below), shown only
/// then, and the mouse wheel scrolling it sideways too. (The tab rows of the IO Dock.)
/// </summary>
public class FlippedHorizontalScrollView : VisualElement
{
    private readonly ScrollView _contentScroll;
    private readonly Scroller _scroller;

    public VisualElement ContentContainer => _contentScroll.contentContainer;

    public FlippedHorizontalScrollView() : this(scrollbarOnTop: true) { }

    public FlippedHorizontalScrollView(bool scrollbarOnTop)
    {
        style.flexDirection = FlexDirection.Column;
        style.flexShrink = 0;

        // The content, with Unity's own scrollbars hidden: the one below stands in for them.
        _contentScroll = new ScrollView(ScrollViewMode.Horizontal) { name = "content-scroll" };
        _contentScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        _contentScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        _contentScroll.style.flexShrink = 0;

        _scroller = new Scroller(0f, 0f, value => _contentScroll.scrollOffset = new Vector2(value, 0f), SliderDirection.Horizontal)
        {
            name = "top-scrollbar-proxy"
        };
        _scroller.style.height = 8;
        _scroller.style.display = DisplayStyle.None;
        // (Just the bar: no arrow buttons at this size.)
        _scroller.lowButton.style.display = DisplayStyle.None;
        _scroller.highButton.style.display = DisplayStyle.None;

        if (scrollbarOnTop)
        {
            hierarchy.Add(_scroller);
            hierarchy.Add(_contentScroll);
        }
        else
        {
            hierarchy.Add(_contentScroll);
            hierarchy.Add(_scroller);
        }

        // The bar's reach follows the content's width and the room it has.
        _contentScroll.RegisterCallback<GeometryChangedEvent>(_ => UpdateScroller());
        _contentScroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdateScroller());

        // The wheel scrolls it sideways (and then it isn't the panel around it that scrolls).
        _contentScroll.RegisterCallback<WheelEvent>(evt =>
        {
            if (_scroller.highValue <= 0f)
                return;
            float delta = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y) ? evt.delta.x : evt.delta.y;
            _scroller.value = Mathf.Clamp(_scroller.value + delta * 20f, 0f, _scroller.highValue);
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
    }

    private void UpdateScroller()
    {
        float room = _contentScroll.contentViewport.layout.width;
        float width = _contentScroll.contentContainer.layout.width;
        if (float.IsNaN(room) || float.IsNaN(width))
            return;
        float overflow = Mathf.Max(0f, width - room);
        _scroller.highValue = overflow;
        _scroller.Adjust(width > 0f ? Mathf.Clamp01(room / width) : 1f);
        if (_scroller.value > overflow)
            _scroller.value = overflow;
        _scroller.style.display = overflow > 0.5f ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public new void Add(VisualElement visualElement)
    {
        _contentScroll.Add(visualElement);
    }
}
