using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Work mode: the edits Sprocket was asked to make, as a to-do list you control. Each step is its own Undo group,
    /// so steps can be taken back (and done again) one at a time; play, pause and step decide when the next one runs.
    /// Keep folds his steps into a single Undo; Undo All takes every one of them back.
    /// </summary>
    internal sealed class AssistantWork
    {
        public enum StepState { Pending, Running, Done, Failed }

        public sealed class Step
        {
            public AssistantCommand Command;
            public string Label;
            public StepState State;
            public string Problem;
            public int UndoGroup = -1;
        }

        /// <summary>The work in progress (null when there's none).</summary>
        public static AssistantWork Current { get; private set; }

        public readonly List<Step> Steps;
        private readonly AssistantExecutor _executor;
        private readonly AssistantOverlay _overlay;
        private readonly WorkPanel _panel;
        private int _firstGroup = -1;
        private bool _busy;          // a step (or a step back) is happening
        private bool _closed;

        /// <summary>Running steps one after another (vs. paused).</summary>
        public bool Playing { get; private set; }

        /// <summary>A step is being done right now.</summary>
        public bool Running => Steps.Any(s => s.State == StepState.Running);

        /// <summary>How many steps are done (or tried): the next one to run is at this index.</summary>
        public int Next => Steps.FindIndex(s => s.State == StepState.Pending || s.State == StepState.Running) is var i && i >= 0 ? i : Steps.Count;

        public bool Finished => Next >= Steps.Count;
        public SchematicGraphEditorWindow Window => _executor.Window;
        public event Action Changed;

        private AssistantWork(AssistantExecutor executor, AssistantOverlay overlay, IReadOnlyList<AssistantCommand> commands)
        {
            _executor = executor;
            _overlay = overlay;
            Steps = commands.Where(c => c != null).Select(c => new Step { Command = c, Label = executor.Describe(c, commands) }).ToList();
            _panel = new WorkPanel(this);
        }

        /// <summary>Starts work on a list of commands (finishing any work before it, keeping it).</summary>
        public static AssistantWork Begin(AssistantExecutor executor, AssistantOverlay overlay, IReadOnlyList<AssistantCommand> commands)
        {
            Current?.Keep();
            var work = new AssistantWork(executor, overlay, commands);
            if (work.Steps.Count == 0)
                return null;
            Current = work;
            work._panel.Attach(executor.Window);
            if (AssistantSettings.WorkStartsPaused)
                work.Notify();
            else
                work.Play();
            return work;
        }

        #region Controls

        public void Play()
        {
            if (_closed || Finished) return;
            Playing = true;
            Notify();
            _ = PumpAsync();
        }

        public void Pause()
        {
            Playing = false;
            Notify();
        }

        /// <summary>Does the next step (only that one).</summary>
        public void StepForward()
        {
            if (_closed || _busy || Finished) return;
            Playing = false;
            _ = RunNextAsync();
        }

        /// <summary>Takes the last done step back.</summary>
        public void StepBack()
        {
            if (_closed || _busy) return;
            Playing = false;
            int last = Steps.FindLastIndex(s => s.State == StepState.Done || s.State == StepState.Failed);
            if (last < 0) { Notify(); return; }
            Revert(last);
            Notify();
        }

        /// <summary>Goes forward or back until the list is done up to (and including) a step.</summary>
        public async void JumpTo(int index)
        {
            if (_closed || _busy) return;
            Playing = false;
            int done = Next;
            if (index + 1 < done)
            {
                for (int i = done - 1; i > index; i--)
                    Revert(i);
                Notify();
            }
            else
                while (!_closed && Next <= index && Next < Steps.Count)
                    if (!await RunNextAsync()) break;
        }

        /// <summary>Done: his steps stay, as one Undo ("Sprocket").</summary>
        public void Keep()
        {
            if (_closed) return;
            Playing = false;
            Close();
            if (_firstGroup >= 0 && Steps.Any(s => s.State == StepState.Done))
            {
                Undo.CollapseUndoOperations(_firstGroup);
                Undo.SetCurrentGroupName("Sprocket");
                Undo.IncrementCurrentGroup();
            }
        }

        /// <summary>Takes back every step and closes the list.</summary>
        public void UndoAll()
        {
            if (_closed) return;
            Playing = false;
            int first = Steps.FindIndex(s => s.UndoGroup >= 0);
            if (first >= 0)
                Revert(first);
            Close();
        }

        private void Close()
        {
            _closed = true;
            _panel.RemoveFromHierarchy();
            if (Current == this) Current = null;
            Notify();
        }

        #endregion

        #region Doing

        private async Task PumpAsync()
        {
            while (Playing && !_closed && !Finished)
            {
                if (_busy) { await Task.Delay(50); continue; }
                if (!await RunNextAsync()) break;
                if (Playing && !Finished && AssistantSettings.StepDelay > 0f)
                    await Task.Delay(TimeSpan.FromSeconds(AssistantSettings.StepDelay));
            }
            if (Finished) Playing = false;
            Notify();
        }

        // One step, in its own Undo group. False when it couldn't run at all (the window's gone).
        private async Task<bool> RunNextAsync()
        {
            if (_busy || _closed || Finished) return false;
            if (Window == null) { Close(); return false; }
            _busy = true;
            var step = Steps[Next];
            step.State = StepState.Running;
            step.Problem = null;
            Notify();

            Undo.IncrementCurrentGroup();
            step.UndoGroup = Undo.GetCurrentGroup();
            if (_firstGroup < 0) _firstGroup = step.UndoGroup;
            Undo.SetCurrentGroupName("Sprocket: " + StripTags(step.Label));
            _overlay?.BeginWorkStep();
            string problem;
            try
            {
                problem = await _executor.RunStepAsync(step.Command);
            }
            finally
            {
                _overlay?.EndWorkStep();
            }
            Undo.CollapseUndoOperations(step.UndoGroup);
            Undo.IncrementCurrentGroup();

            step.State = problem == null ? StepState.Done : StepState.Failed;
            step.Problem = problem;
            if (problem != null)
                Debug.LogWarning($"[Assistant] Step \"{StripTags(step.Label)}\" didn't work: {problem}");
            _busy = false;
            Notify();
            return true;
        }

        // Takes back a step and everything after it.
        private void Revert(int index)
        {
            var step = Steps[index];
            if (step.UndoGroup >= 0)
                Undo.RevertAllDownToGroup(step.UndoGroup);
            for (int i = index; i < Steps.Count; i++)
            {
                if (Steps[i].State == StepState.Pending) continue;
                Steps[i].State = StepState.Pending;
                Steps[i].Problem = null;
                Steps[i].UndoGroup = -1;
                _executor.Forget(Steps[i].Command);
            }
            if (_firstGroup >= 0 && Steps.All(s => s.UndoGroup < 0))
                _firstGroup = -1;
            _executor.Refresh();
        }

        private void Notify()
        {
            _panel.Refresh();
            Changed?.Invoke();
        }

        internal static string StripTags(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]+>", "");

        #endregion

        /// <summary>The to-do list card, at the top right of the graph window.</summary>
        private sealed class WorkPanel : VisualElement
        {
            private static readonly Color Accent = new(0.47f, 0.38f, 0.72f);
            private static readonly Color Pink = new(0.93f, 0.52f, 0.78f);
            private readonly AssistantWork _work;
            private readonly Label _title, _status;
            private readonly VisualElement _list;
            private readonly Button _back, _play, _step, _keep, _undoAll;
            private readonly ScrollView _scroll;

            public WorkPanel(AssistantWork work)
            {
                _work = work;
                bool dark = EditorGUIUtility.isProSkin;
                Color text = dark ? new Color(0.92f, 0.9f, 1f) : new Color(0.15f, 0.12f, 0.25f);
                name = "sprocket-work";
                style.position = Position.Absolute;
                style.top = 34;
                style.right = 12;
                style.width = 270;
                style.maxHeight = 360;
                style.backgroundColor = dark ? new Color(0.15f, 0.14f, 0.2f, 0.96f) : new Color(1f, 0.98f, 1f, 0.97f);
                SetBorder(this, Accent, 1.5f, 10f);
                style.paddingLeft = 8; style.paddingRight = 8; style.paddingTop = 6; style.paddingBottom = 8;
                // Clicks here are for the list, not the canvas behind it.
                RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
                RegisterCallback<WheelEvent>(e => e.StopPropagation());

                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.Center;
                _title = new Label("Sprocket's To-Do") { enableRichText = true };
                _title.style.unityFontStyleAndWeight = FontStyle.Bold;
                _title.style.color = text;
                _title.style.flexGrow = 1;
                _status = new Label();
                _status.style.fontSize = 10;
                _status.style.color = Pink;
                header.Add(_title);
                header.Add(_status);
                Add(header);

                _scroll = new ScrollView(ScrollViewMode.Vertical);
                _scroll.style.marginTop = 4;
                _scroll.style.flexShrink = 1;
                _list = _scroll.contentContainer;
                Add(_scroll);

                var bar = new VisualElement();
                bar.style.flexDirection = FlexDirection.Row;
                bar.style.marginTop = 6;
                _back = Tool("⏮", "Take back the last step (Undo it)", () => _work.StepBack());
                _play = Tool("▶", "Play: do the steps one after another", () => { if (_work.Playing) _work.Pause(); else _work.Play(); });
                _step = Tool("⏭", "Do the next step", () => _work.StepForward());
                bar.Add(_back); bar.Add(_play); bar.Add(_step);
                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                bar.Add(spacer);
                _undoAll = Tool("Undo All", "Take back every step and close the list", () => _work.UndoAll());
                _keep = Tool("Keep ✓", "Keep his changes (one Undo takes them all back) and close the list", () => _work.Keep());
                _keep.style.backgroundColor = Accent;
                _keep.style.color = Color.white;
                bar.Add(_undoAll); bar.Add(_keep);
                Add(bar);
            }

            public void Attach(EditorWindow window)
            {
                RemoveFromHierarchy();
                window?.rootVisualElement.Add(this);
                Refresh();
            }

            private static Button Tool(string label, string tooltip, Action click)
            {
                var button = new Button(click) { text = label, tooltip = tooltip };
                button.style.minWidth = 26;
                button.style.height = 20;
                button.style.marginLeft = 0; button.style.marginRight = 3;
                button.style.paddingLeft = 5; button.style.paddingRight = 5;
                SetBorder(button, new Color(0, 0, 0, 0.15f), 1f, 5f);
                return button;
            }

            public void Refresh()
            {
                if (_work.Steps == null) return;
                int done = _work.Steps.Count(s => s.State == StepState.Done);
                int failed = _work.Steps.Count(s => s.State == StepState.Failed);
                _status.text = $"{done}/{_work.Steps.Count}" + (failed > 0 ? $" · {failed} failed" : "")
                    + (_work.Running ? " · working" : _work.Playing ? " · playing" : _work.Finished ? " · done" : " · paused");
                _play.text = _work.Playing ? "⏸" : "▶";
                _play.tooltip = _work.Playing ? "Pause after this step" : "Play: do the steps one after another";
                _play.SetEnabled(!_work.Finished);
                _step.SetEnabled(!_work.Finished && !_work._busy);
                _back.SetEnabled(!_work._busy && _work.Steps.Any(s => s.State == StepState.Done || s.State == StepState.Failed));

                _list.Clear();
                bool dark = EditorGUIUtility.isProSkin;
                for (int i = 0; i < _work.Steps.Count; i++)
                {
                    var step = _work.Steps[i];
                    int index = i;
                    var row = new VisualElement { tooltip = step.Problem ?? "Click to go to here: steps before it are done, steps after it are taken back." };
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.FlexStart;
                    row.style.paddingTop = 2; row.style.paddingBottom = 2; row.style.paddingLeft = 3; row.style.paddingRight = 3;
                    SetBorder(row, new Color(0, 0, 0, 0), 0f, 5f);
                    if (step.State == StepState.Running)
                        row.style.backgroundColor = new Color(Pink.r, Pink.g, Pink.b, 0.18f);
                    row.RegisterCallback<ClickEvent>(_ => _work.JumpTo(index));
                    row.RegisterCallback<PointerEnterEvent>(_ => { if (step.State != StepState.Running) row.style.backgroundColor = new Color(Accent.r, Accent.g, Accent.b, 0.15f); });
                    row.RegisterCallback<PointerLeaveEvent>(_ => { if (step.State != StepState.Running) row.style.backgroundColor = new Color(0, 0, 0, 0); });

                    var box = new Label(step.State switch
                    {
                        StepState.Done => "✔",
                        StepState.Failed => "✖",
                        StepState.Running => "➜",
                        _ => "○",
                    });
                    box.style.width = 16;
                    box.style.unityTextAlign = TextAnchor.UpperCenter;
                    box.style.color = step.State switch
                    {
                        StepState.Done => new Color(0.45f, 0.8f, 0.5f),
                        StepState.Failed => new Color(0.9f, 0.45f, 0.5f),
                        StepState.Running => Pink,
                        _ => new Color(0.6f, 0.58f, 0.68f),
                    };
                    var label = new Label(step.Label) { enableRichText = true };
                    label.style.whiteSpace = WhiteSpace.Normal;
                    label.style.flexShrink = 1;
                    label.style.color = dark ? new Color(0.92f, 0.9f, 1f) : new Color(0.15f, 0.12f, 0.25f);
                    if (step.State == StepState.Done)
                        label.style.opacity = 0.6f;
                    if (step.State == StepState.Failed)
                        label.text = $"<s>{step.Label}</s>  <size=10><color=#e08a9b>{step.Problem}</color></size>";
                    row.Add(box);
                    row.Add(label);
                    _list.Add(row);
                    if (step.State == StepState.Running)
                        _scroll.schedule.Execute(() => _scroll.ScrollTo(row));
                }
            }

            private static void SetBorder(VisualElement element, Color color, float width, float radius)
            {
                element.style.borderLeftColor = color; element.style.borderRightColor = color;
                element.style.borderTopColor = color; element.style.borderBottomColor = color;
                element.style.borderLeftWidth = width; element.style.borderRightWidth = width;
                element.style.borderTopWidth = width; element.style.borderBottomWidth = width;
                element.style.borderTopLeftRadius = radius; element.style.borderTopRightRadius = radius;
                element.style.borderBottomLeftRadius = radius; element.style.borderBottomRightRadius = radius;
            }
        }
    }
}
