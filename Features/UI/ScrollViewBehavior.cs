using Remedy.Schematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.UI
{
    /// <summary>Scrolls a named UI Builder ScrollView and exposes its scroll events as Schematic Signals.</summary>
    [SchematicBehaviorPath("UI/Scroll View")]
    [SchematicUIElementBehavior(typeof(ScrollView))]
    public sealed class ScrollViewBehavior : SchematicUIBehavior
    {
        [Tooltip("Sent whenever it scrolls, with how far it's scrolled (x across, y down).")]
        [OutputSignal, Parameter("Scroll Offset", typeof(Vector2))]
        public readonly SignalHandle<Vector2> Scrolled;

        [Tooltip("Sent when it's scrolled all the way to the top.")]
        [OutputSignal]
        public readonly SignalHandle ReachedTop;

        [Tooltip("Sent when it's scrolled all the way to the bottom.")]
        [OutputSignal]
        public readonly SignalHandle ReachedBottom;

        [Tooltip("Send a scroll position (x across, y down) to scroll there.")]
        [InputSignal, Parameter("Scroll Offset", typeof(Vector2))]
        public readonly SignalHandle<Vector2> ScrollToOffset;

        [Tooltip("Send an element's name (from UI Builder) to scroll until it's in view.")]
        [InputSignal, Parameter("Element Name", typeof(string))]
        public readonly SignalHandle<string> ScrollToElement;

        [Tooltip("Scrolls to the top.")]
        [InputSignal]
        public readonly SignalHandle ScrollToTop;

        [Tooltip("Scrolls to the bottom.")]
        [InputSignal]
        public readonly SignalHandle ScrollToBottom;

        private ScrollView _scrollView;
        private BehaviorInstance _instance;
        private float _lastVerticalValue;

        protected override void OnStart()
        {
            ScrollToOffset.Subscribe(SetScrollOffset);
            ScrollToElement.Subscribe(ScrollToNamedElement);
            ScrollToTop.Subscribe(OnScrollToTop);
            ScrollToBottom.Subscribe(OnScrollToBottom);
        }

        protected override void OnEnable()
        {
            _scrollView = Element as ScrollView;
            _instance = Self;
            if (_scrollView == null)
                return;

            _lastVerticalValue = _scrollView.verticalScroller.value;
            _scrollView.verticalScroller.valueChanged += OnVerticalScrollChanged;
            _scrollView.horizontalScroller.valueChanged += OnHorizontalScrollChanged;
        }

        protected override void OnDisable()
        {
            if (_scrollView != null)
            {
                _scrollView.verticalScroller.valueChanged -= OnVerticalScrollChanged;
                _scrollView.horizontalScroller.valueChanged -= OnHorizontalScrollChanged;
            }
            _scrollView = null;
            _instance = null;
        }

        private void OnVerticalScrollChanged(float value)
        {
            if (_scrollView == null || _instance == null)
                return;

            Scrolled.Invoke(_instance, _scrollView.scrollOffset);
            var scroller = _scrollView.verticalScroller;
            const float epsilon = 0.5f;
            if (scroller.highValue > scroller.lowValue + epsilon)
            {
                if (_lastVerticalValue > scroller.lowValue + epsilon && value <= scroller.lowValue + epsilon)
                    ReachedTop.Invoke(_instance);
                if (_lastVerticalValue < scroller.highValue - epsilon && value >= scroller.highValue - epsilon)
                    ReachedBottom.Invoke(_instance);
            }
            _lastVerticalValue = value;
        }

        private void OnHorizontalScrollChanged(float _)
        {
            if (_scrollView != null && _instance != null)
                Scrolled.Invoke(_instance, _scrollView.scrollOffset);
        }

        private void SetScrollOffset(Vector2 offset)
        {
            if (_scrollView != null)
                _scrollView.scrollOffset = offset;
        }

        private void ScrollToNamedElement(string elementName)
        {
            var target = Query<VisualElement>(elementName);
            if (_scrollView != null && target != null)
                _scrollView.ScrollTo(target);
        }

        private void OnScrollToTop()
        {
            if (_scrollView != null)
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, _scrollView.verticalScroller.lowValue);
        }

        private void OnScrollToBottom()
        {
            if (_scrollView != null)
                _scrollView.scrollOffset = new Vector2(_scrollView.scrollOffset.x, _scrollView.verticalScroller.highValue);
        }
    }
}
