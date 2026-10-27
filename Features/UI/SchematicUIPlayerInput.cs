using Remedy.Framework;
using Remedy.Schematics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.UI
{
    /// <summary>
    /// Drives a possessed <see cref="SchematicUI"/> with its controller's input: the graph's Navigate Signal moves a
    /// selection between the elements that can be selected, Submit presses the selected one and Cancel fires On UI
    /// Cancel. Possession routes input, so only the player who possesses the UI reaches it: in split-screen each player
    /// works their own menu with their own gamepad.
    /// <para>The selection is this UI's own (a USS class on the element), not UI Toolkit's focus, which a panel shares
    /// between every document on it: two players' menus on one panel each keep their own.</para>
    /// </summary>
    internal sealed class SchematicUIPlayerInput
    {
        private const float RepeatDelay = 0.4f, RepeatInterval = 0.12f;

        private readonly SchematicUI _ui;
        private readonly List<(SignalData signal, SignalListener listener)> _listening = new();
        private int _uid;
        private VisualElement _selected;
        private Vector2 _heldDirection;
        private float _nextRepeat;
        private bool _submitHeld, _cancelHeld;

        public SchematicUIPlayerInput(SchematicUI ui) => _ui = ui;

        public bool Active => _uid != 0;

        /// <summary>The selected element, or null.</summary>
        public VisualElement Selected => _selected;

        /// <summary>Starts listening to the possessing controller's input (it's routed to this UI's object alone).</summary>
        public void Begin()
        {
            End();
            var graph = _ui.UIGraph;
            if (graph == null || _ui.UID == 0)
                return;
            _uid = _ui.UID;
            Listen(graph.Navigate, args => Navigate(args.Length > 0 ? SignalArgs.FromUnion<Vector2>(args[0]) : Vector2.zero));
            Listen(graph.Submit, args => Press(args, ref _submitHeld, Submit));
            Listen(graph.Cancel, args => Press(args, ref _cancelHeld, CancelPressed));
            Select(FirstSelectable());
        }

        /// <summary>Stops listening and clears the selection.</summary>
        public void End()
        {
            foreach (var (signal, listener) in _listening)
                if (signal != null)
                    signal.Unsubscribe(_uid, listener);
            _listening.Clear();
            _uid = 0;
            Select(null);
            _heldDirection = Vector2.zero;
            _submitHeld = _cancelHeld = false;
        }

        private void Listen(SignalData signal, System.Action<Union[]> receive)
        {
            if (signal == null)
                return;
            var listener = new GraphSignalListener(receive);
            signal.Subscribe(_uid, listener);
            _listening.Add((signal, listener));
        }

        // A button: acts once when pressed (held inputs are sent again every frame, and released ones send false).
        private static void Press(Union[] args, ref bool held, System.Action action)
        {
            bool down = args.Length == 0 || SignalArgs.FromUnion<bool>(args[0]);
            if (args.Length == 0)
            {
                action();
                return;
            }
            if (down && !held)
                action();
            held = down;
        }

        #region Selection

        private void Navigate(Vector2 value)
        {
            Vector2 direction = value.magnitude < 0.5f ? Vector2.zero
                : Mathf.Abs(value.x) > Mathf.Abs(value.y) ? new Vector2(Mathf.Sign(value.x), 0f) : new Vector2(0f, Mathf.Sign(value.y));
            if (direction == Vector2.zero)
            {
                _heldDirection = Vector2.zero;
                return;
            }
            // A move when pushed, then repeats while held.
            float now = Time.unscaledTime;
            if (direction != _heldDirection)
            {
                _heldDirection = direction;
                _nextRepeat = now + RepeatDelay;
                Move(direction);
            }
            else if (now >= _nextRepeat)
            {
                _nextRepeat = now + RepeatInterval;
                Move(direction);
            }
        }

        private void Move(Vector2 direction)
        {
            var candidates = Selectables();
            if (candidates.Count == 0)
                return;
            if (_selected == null || !candidates.Contains(_selected))
            {
                Select(candidates[0]);
                return;
            }
            // Up on a stick is up on screen, where y goes down.
            var screen = new Vector2(direction.x, -direction.y);
            Vector2 from = _selected.worldBound.center;
            VisualElement best = null;
            float bestScore = float.MaxValue;
            foreach (var candidate in candidates)
            {
                if (candidate == _selected)
                    continue;
                Vector2 offset = candidate.worldBound.center - from;
                float along = Vector2.Dot(offset, screen);
                if (along <= 1f)
                    continue;
                float across = Mathf.Abs(screen.x * offset.y - screen.y * offset.x);
                float score = along + across * 2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            if (best != null)
                Select(best);
        }

        private VisualElement FirstSelectable()
        {
            var graph = _ui.UIGraph;
            var root = Root;
            if (root == null)
                return null;
            if (graph != null && !string.IsNullOrEmpty(graph.FirstSelected))
            {
                var named = root.Q(graph.FirstSelected);
                if (named != null && IsSelectable(named))
                    return named;
            }
            var all = Selectables();
            return all.Count > 0 ? all[0] : null;
        }

        private List<VisualElement> Selectables()
        {
            var list = new List<VisualElement>();
            Root?.Query<VisualElement>().ForEach(element =>
            {
                if (IsSelectable(element))
                    list.Add(element);
            });
            return list;
        }

        private static bool IsSelectable(VisualElement element)
            => (element.focusable || element.ClassListContains("selectable")) && element.enabledInHierarchy && element.visible
               && element.resolvedStyle.display != DisplayStyle.None && element.worldBound.width > 0f && element.worldBound.height > 0f
               // Only the elements people press (not a field's inner text input or a scroll view's own parts), or any
               // element given the "selectable" class in UI Builder.
               && (element is Button || element is Toggle || element is Slider || element is SliderInt || element is TextField
                   || element is DropdownField || element is Foldout || element.ClassListContains("selectable"));

        private VisualElement Root => _ui.GetComponent<UIDocument>()?.rootVisualElement;

        private void Select(VisualElement element)
        {
            if (_selected == element)
                return;
            var graph = _ui.UIGraph;
            string selectedClass = graph != null && !string.IsNullOrEmpty(graph.SelectedClass) ? graph.SelectedClass : "schematic-ui-selected";
            if (_selected != null)
            {
                _selected.RemoveFromClassList(selectedClass);
                if (graph != null && graph.OutlineSelected)
                    Outline(_selected, false);
            }
            _selected = element;
            if (_selected == null)
                return;
            _selected.AddToClassList(selectedClass);
            if (graph != null && graph.OutlineSelected)
                Outline(_selected, true);
            _selected.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(_selected);
            graph?.TriggerEvent<OnUISelectionChanged>(_ui.gameObject, new Union(_selected.name ?? ""));
        }

        // A plain outline (until the selected class is styled): set inline, and taken off again (back to its style).
        private static void Outline(VisualElement element, bool on)
        {
            if (on)
            {
                var color = new Color(1f, 1f, 1f, 0.9f);
                element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
                element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = 2f;
            }
            else
            {
                element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = StyleKeyword.Null;
                element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = StyleKeyword.Null;
            }
        }

        #endregion

        #region Pressing

        private void Submit()
        {
            var element = _selected;
            if (element == null)
            {
                Select(FirstSelectable());
                return;
            }
            switch (element)
            {
                case Button button:
                    // Its own click handlers (button.clicked) through UI Toolkit's submit, and the graph's On UI Button
                    // Clicked for it.
                    SendSubmit(button);
                    _ui.ButtonPressed(button.name);
                    break;
                case Toggle toggle:
                    toggle.value = !toggle.value;
                    break;
                case Foldout foldout:
                    foldout.value = !foldout.value;
                    break;
                default:
                    SendSubmit(element);
                    break;
            }
        }

        private static void SendSubmit(VisualElement element)
        {
            using var submit = NavigationSubmitEvent.GetPooled();
            submit.target = element;
            element.SendEvent(submit);
        }

        private void CancelPressed()
            => _ui.UIGraph?.TriggerEvent<OnUICancel>(_ui.gameObject, new Union((Component)Possession.Current));

        #endregion
    }
}
