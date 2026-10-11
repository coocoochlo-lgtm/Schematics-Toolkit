using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Sprocket, the assistant: a little robot floating over a Schematic Graph window. Click it to type a request, or
    /// hold Q to say one. It tells you what it's going to do, then flies around the graph doing it. Drag it anywhere
    /// while it's idle; Esc stops it mid-task.
    /// </summary>
    internal sealed class AssistantOverlay : VisualElement
    {
        public enum Mood { Idle, Listening, Thinking, Working, Talking, Confused }

        private const float Width = 64f, Height = 76f;
        private const string HomeKey = "Schematics.Assistant.Home";

        private readonly SchematicGraphEditorWindow _window;
        private readonly VisualElement _bot, _antennaBall, _leftEye, _rightEye, _mouth, _shadow;
        private readonly VisualElement _bubble;
        private readonly Label _bubbleText;
        private readonly TextField _input;

        private Mood _mood = Mood.Idle;
        private Vector2 _position;           // top-left, in the window root's space
        private Vector2 _flyFrom, _flyTo;
        private float _flyTime = -1f, _flyDuration;
        private TaskCompletionSource<bool> _flight;
        private float _time, _blinkAt = 3f, _hideBubbleAt = -1f;
        private bool _dragging;
        private Vector2 _dragOffset;
        private CancellationTokenSource _work;
        private double _lastTick;

        private bool Busy => _mood == Mood.Thinking || _mood == Mood.Working || _mood == Mood.Talking;

        #region Attaching

        /// <summary>Adds the assistant to a graph window (once).</summary>
        public static AssistantOverlay Attach(SchematicGraphEditorWindow window)
        {
            if (window == null || !AssistantSettings.Enabled)
                return null;
            var existing = window.AssistantLayer.Q<AssistantOverlay>();
            if (existing != null)
                return existing;
            var overlay = new AssistantOverlay(window);
            window.AssistantLayer.Add(overlay);
            // Out of the box: the first time it appears, it installs what it needs in the background.
            AssistantInstaller.EnsureInstalled();
            return overlay;
        }

        public static AssistantOverlay For(SchematicGraphEditorWindow window)
            => window != null ? window.AssistantLayer.Q<AssistantOverlay>() : null;

        #endregion

        private AssistantOverlay(SchematicGraphEditorWindow window)
        {
            _window = window;
            name = "schematics-assistant";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;

            bool dark = EditorGUIUtility.isProSkin;
            Color shell = new Color(0.78f, 0.70f, 0.96f);
            Color shellEdge = new Color(0.47f, 0.38f, 0.72f);
            Color screen = new Color(0.12f, 0.11f, 0.2f);
            Color glow = new Color(0.45f, 0.95f, 0.95f);

            // The robot.
            _bot = new VisualElement { name = "assistant-bot", tooltip = "Sprocket: click to ask, hold Q to talk, drag to move" };
            _bot.style.position = Position.Absolute;
            _bot.style.width = Width;
            _bot.style.height = Height;
            Add(_bot);

            _shadow = Box(new Rect(14, 70, 36, 6), new Color(0, 0, 0, 0.25f), 3);
            _bot.Add(_shadow);

            var antenna = Box(new Rect(31, 4, 2, 12), shellEdge, 1);
            _bot.Add(antenna);
            _antennaBall = Box(new Rect(27, 0, 10, 10), new Color(1f, 0.55f, 0.75f), 5);
            _bot.Add(_antennaBall);

            var head = Box(new Rect(6, 14, 52, 44), shell, 16);
            Border(head, shellEdge, 2);
            _bot.Add(head);

            var face = Box(new Rect(6, 7, 36, 24), screen, 9);
            head.Add(face);
            _leftEye = Box(new Rect(8, 6, 7, 10), glow, 3.5f);
            _rightEye = Box(new Rect(21, 6, 7, 10), glow, 3.5f);
            _mouth = Box(new Rect(14, 18, 8, 3), glow, 1.5f);
            face.Add(_leftEye);
            face.Add(_rightEye);
            face.Add(_mouth);

            // Little cheeks, for charm.
            head.Add(Box(new Rect(2, 26, 6, 4), new Color(1f, 0.6f, 0.75f, 0.8f), 2));
            head.Add(Box(new Rect(40, 26, 6, 4), new Color(1f, 0.6f, 0.75f, 0.8f), 2));

            // Arms.
            _bot.Add(Box(new Rect(0, 34, 8, 14), shellEdge, 4));
            _bot.Add(Box(new Rect(56, 34, 8, 14), shellEdge, 4));

            // Speech bubble and request field.
            _bubble = new VisualElement { name = "assistant-bubble", pickingMode = PickingMode.Position };
            _bubble.style.position = Position.Absolute;
            _bubble.style.maxWidth = 280;
            _bubble.style.minWidth = 160;
            _bubble.style.paddingLeft = 10; _bubble.style.paddingRight = 10;
            _bubble.style.paddingTop = 7; _bubble.style.paddingBottom = 7;
            _bubble.style.backgroundColor = dark ? new Color(0.17f, 0.16f, 0.22f, 0.97f) : new Color(1f, 0.98f, 1f, 0.98f);
            Border(_bubble, shellEdge, 1.5f);
            SetRadius(_bubble, 10);
            _bubble.style.display = DisplayStyle.None;
            Add(_bubble);

            _bubbleText = new Label { enableRichText = true };
            _bubbleText.style.whiteSpace = WhiteSpace.Normal;
            _bubbleText.style.color = dark ? new Color(0.92f, 0.9f, 1f) : new Color(0.15f, 0.12f, 0.25f);
            _bubble.Add(_bubbleText);

            _input = new TextField { name = "assistant-input" };
            _input.style.marginTop = 6;
            _input.style.display = DisplayStyle.None;
            _input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);
            _bubble.Add(_input);

            // Dragging and clicking.
            _bot.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _bot.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _bot.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _bot.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Ask…", _ => OpenInput());
                evt.menu.AppendAction("Stop", _ => Cancel(), Busy ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Preferences…", _ => SettingsService.OpenUserPreferences("Preferences/Schematics Assistant"));
                evt.menu.AppendAction(AssistantServer.IsInstalled ? "Reinstall" : "Install", _ => { AssistantServer.Stop(); AssistantInstaller.Reinstall(); },
                    AssistantInstaller.IsInstalling ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("Hide", _ => RemoveFromHierarchy());
                evt.StopPropagation();
            }));

            RegisterCallback<GeometryChangedEvent>(_ => ClampHome());
            _window.AssistantLayer.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape && Busy)
                {
                    Cancel();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            AssistantInstaller.Finished += OnInstallFinished;
            RegisterCallback<DetachFromPanelEvent>(_ => AssistantInstaller.Finished -= OnInstallFinished);

            _position = Home;
            Place();
            _lastTick = EditorApplication.timeSinceStartup;
            schedule.Execute(Tick).Every(16);
        }

        #region Asking

        public void OpenInput()
        {
            if (Busy)
                return;
            ShowBubble("What should I make?", seconds: -1);
            _input.style.display = DisplayStyle.Flex;
            _input.value = "";
            _input.schedule.Execute(() => _input.Q(TextField.textInputUssName)?.Focus() ).ExecuteLater(10);
        }

        private void OnInputKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                string text = _input.value;
                _input.style.display = DisplayStyle.None;
                evt.StopPropagation();
                _ = AskAsync(text);
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                _input.style.display = DisplayStyle.None;
                HideBubble();
                evt.StopPropagation();
            }
        }

        public void BeginListening()
        {
            if (Busy)
                return;
            _input.style.display = DisplayStyle.None;
            if (!AssistantVoice.StartRecording())
            {
                ShowBubble("I can't hear anything. Is a microphone connected?", 4f);
                return;
            }
            _mood = Mood.Listening;
            ShowBubble("<i>Listening…</i>", seconds: -1);
            _ = AssistantServer.EnsureRunningAsync(); // warm up while you talk
        }

        public async void EndListening()
        {
            if (_mood != Mood.Listening)
                return;
            byte[] wav = AssistantVoice.StopRecording();
            if (wav == null)
            {
                _mood = Mood.Idle;
                HideBubble();
                return;
            }
            _mood = Mood.Thinking;
            ShowBubble("<i>…</i>", seconds: -1);
            string text;
            try
            {
                if (!await AssistantServer.EnsureRunningAsync())
                {
                    SetupNeeded();
                    return;
                }
                text = await AssistantServer.TranscribeAsync(wav, CancellationToken.None);
            }
            catch (Exception exception)
            {
                Confused("I couldn't make that out. " + exception.Message);
                return;
            }
            _mood = Mood.Idle;
            if (string.IsNullOrWhiteSpace(text))
            {
                Confused("I didn't catch that.");
                return;
            }
            await AskAsync(text.Trim());
        }

        /// <summary>Sends a request with what's on screen, says the reply and carries out its commands.</summary>
        public async Task AskAsync(string utterance)
        {
            if (string.IsNullOrWhiteSpace(utterance) || Busy)
                return;

            _work?.Dispose();
            _work = new CancellationTokenSource();
            var cancellation = _work.Token;
            var home = Home;
            _mood = Mood.Thinking;
            ShowBubble($"<i>“{Escape(utterance)}”</i>\nHmm, let me think…", seconds: -1);

            try
            {
                if (!await AssistantServer.EnsureRunningAsync())
                {
                    SetupNeeded();
                    return;
                }

                var context = new AssistantContext(_window);
                string json = await AssistantServer.AskAsync(utterance, context.Json, cancellation);
                var reply = JsonUtility.FromJson<AssistantReply>(json) ?? new AssistantReply();

                _mood = Mood.Talking;
                ShowBubble(Escape(reply.say ?? "Okay!"), seconds: -1);
                var speaking = AssistantVoice.SayAsync(reply.say, cancellation);

                if (reply.commands != null && reply.commands.Length > 0)
                {
                    _mood = Mood.Working;
                    var executor = new AssistantExecutor(context, this);
                    await executor.RunAsync(reply.commands, cancellation);
                    if (executor.Problems.Count > 0)
                        ShowBubble(Escape(reply.say) + $"\n<size=10><color=#e08a9b>{executor.Problems.Count} step(s) didn't work; see the Console.</color></size>", seconds: 8f);
                    else
                        ShowBubble(Escape(reply.say) + "\n<size=10>Done! (Undo with Ctrl+Z)</size>", seconds: 5f);
                }
                else
                    ShowBubble(Escape(reply.say), seconds: 8f);

                await speaking;
            }
            catch (OperationCanceledException)
            {
                AssistantVoice.StopSpeaking();
                ShowBubble("Okay, stopping.", 2f);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Confused("Something went wrong: " + exception.Message);
                return;
            }
            finally
            {
                if (_mood != Mood.Confused)
                    _mood = Mood.Idle;
            }

            await FlyToAsync(home);
        }

        public void Cancel()
        {
            _work?.Cancel();
            AssistantVoice.StopSpeaking();
        }

        private void SetupNeeded()
        {
            _mood = Mood.Confused;
            if (AssistantInstaller.IsInstalling)
                ShowBubble($"I'm still getting set up! {Escape(AssistantInstaller.Progress)}", 5f);
            else if (AssistantServer.IsInstalled)
                ShowBubble("My server didn't start. Check the Console for why.", 8f);
            else
                ShowBubble("My setup didn't finish. Check the Console, then right-click me and pick <b>Reinstall</b>.", 10f);
        }

        // Install progress, while nothing else is going on.
        private string _lastProgress;

        private void ShowInstallProgress()
        {
            if (Busy || _mood == Mood.Listening || _input.style.display == DisplayStyle.Flex)
                return;
            string progress = AssistantInstaller.Progress;
            if (progress == null || progress == _lastProgress)
                return;
            _lastProgress = progress;
            int percent = Mathf.RoundToInt(AssistantInstaller.Fraction * 100f);
            ShowBubble($"<size=10>Setting myself up… {percent}%</size>\n{Escape(progress)}", seconds: -1);
        }

        private void OnInstallFinished(bool ok)
        {
            _lastProgress = null;
            if (ok)
            {
                _mood = Mood.Talking;
                ShowBubble("All set! Click me to type, or hold <b>Q</b> to talk.", 6f);
                _ = AssistantVoice.SayAsync("All set! What should we build?");
                _mood = Mood.Idle;
            }
            else
                Confused("My setup hit a snag. Check the Console, then right-click me and pick Reinstall.");
        }

        private void Confused(string message)
        {
            _mood = Mood.Confused;
            ShowBubble(Escape(message), 6f);
        }

        private static string Escape(string text) => (text ?? "").Replace("<", "&lt;").Replace(">", "&gt;");

        #endregion

        #region Moving

        private Vector2 Home
        {
            get
            {
                string saved = SessionState.GetString(HomeKey, "");
                var parts = saved.Split(',');
                if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    return new Vector2(x, y);
                var size = _window.AssistantLayer.layout.size;
                return float.IsNaN(size.x) || size.x < 200 ? new Vector2(320, 60) : new Vector2(size.x - Width - 24, size.y - Height - 24);
            }
            set => SessionState.SetString(HomeKey, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1}", value.x, value.y));
        }

        private void ClampHome()
        {
            if (Busy || _dragging || _flight != null)
                return;
            _position = Clamp(_position);
            Place();
        }

        private Vector2 Clamp(Vector2 position)
        {
            var size = _window.AssistantLayer.layout.size;
            if (float.IsNaN(size.x) || size.x <= Width)
                return position;
            return new Vector2(Mathf.Clamp(position.x, 0, size.x - Width), Mathf.Clamp(position.y, 0, size.y - Height));
        }

        /// <summary>Flies to a point (top-left of the robot, in the window root's space). Completes on arrival.</summary>
        public Task FlyToAsync(Vector2 target)
        {
            _flight?.TrySetResult(false);
            _flyFrom = _position;
            _flyTo = Clamp(target);
            _flyTime = 0f;
            _flyDuration = Mathf.Clamp(Vector2.Distance(_flyFrom, _flyTo) / 900f, 0.2f, 0.8f);
            _flight = new TaskCompletionSource<bool>();
            return _flight.Task;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
                return;
            _dragging = false;
            _dragOffset = (Vector2)evt.position - _bot.worldBound.position;
            _bot.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_bot.HasPointerCapture(evt.pointerId) || Busy)
                return;
            var local = _window.AssistantLayer.WorldToLocal((Vector2)evt.position - _dragOffset);
            if (!_dragging && Vector2.Distance(local, _position) < 4f)
                return;
            _dragging = true;
            _position = Clamp(local);
            Place();
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_bot.HasPointerCapture(evt.pointerId))
                return;
            _bot.ReleasePointer(evt.pointerId);
            if (_dragging)
                Home = _position;
            else if (_mood == Mood.Listening) { }
            else if (Busy) Cancel();
            else if (_input.style.display == DisplayStyle.Flex) { _input.style.display = DisplayStyle.None; HideBubble(); }
            else OpenInput();
            _dragging = false;
            evt.StopPropagation();
        }

        #endregion

        #region Animation

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.1f, (float)(now - _lastTick));
            _lastTick = now;
            _time += dt;

            if (_flyTime >= 0f)
            {
                _flyTime += dt;
                float t = Mathf.Clamp01(_flyTime / _flyDuration);
                float eased = t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                // A little arc, like it's hopping over.
                var arc = Vector2.up * (-Mathf.Sin(t * Mathf.PI) * Mathf.Min(40f, Vector2.Distance(_flyFrom, _flyTo) * 0.2f));
                _position = Vector2.Lerp(_flyFrom, _flyTo, eased) + arc;
                if (t >= 1f)
                {
                    _flyTime = -1f;
                    _position = _flyTo;
                    var flight = _flight;
                    _flight = null;
                    flight?.TrySetResult(true);
                }
            }

            if (_hideBubbleAt > 0f && _time >= _hideBubbleAt && !Busy)
            {
                _hideBubbleAt = -1f;
                HideBubble();
                if (_mood == Mood.Confused)
                    _mood = Mood.Idle;
            }

            if (AssistantInstaller.IsInstalling)
                ShowInstallProgress();

            Place();
            AnimateFace();
        }

        private void AnimateFace()
        {
            // Blink now and then.
            float eyeHeight = 10f;
            if (_time >= _blinkAt)
            {
                float blink = (_time - _blinkAt) / 0.14f;
                eyeHeight = blink < 1f ? Mathf.Lerp(10f, 1.5f, Mathf.Sin(blink * Mathf.PI)) : 10f;
                if (blink >= 1f)
                    _blinkAt = _time + UnityEngine.Random.Range(2.5f, 5.5f);
            }

            float lookX = 0f, lookY = 0f, mouthWidth = 8f;
            Color ball = new Color(1f, 0.55f, 0.75f);
            switch (_mood)
            {
                case Mood.Listening:
                    ball = Color.Lerp(new Color(1f, 0.3f, 0.4f), new Color(1f, 0.75f, 0.8f), 0.5f + 0.5f * Mathf.Sin(_time * 10f));
                    eyeHeight = Mathf.Max(eyeHeight, 11f);
                    mouthWidth = 4f;
                    break;
                case Mood.Thinking:
                    lookX = Mathf.Sin(_time * 3f) * 3f;
                    lookY = -2f;
                    ball = Color.Lerp(new Color(0.55f, 0.75f, 1f), Color.white, 0.5f + 0.5f * Mathf.Sin(_time * 6f));
                    mouthWidth = 5f;
                    break;
                case Mood.Working:
                    lookX = 2f;
                    ball = new Color(0.55f, 1f, 0.7f);
                    break;
                case Mood.Talking:
                    mouthWidth = 6f + 5f * Mathf.Abs(Mathf.Sin(_time * 14f));
                    break;
                case Mood.Confused:
                    lookY = 1f;
                    mouthWidth = 4f;
                    ball = new Color(1f, 0.8f, 0.4f);
                    break;
            }

            _leftEye.style.height = eyeHeight;
            _rightEye.style.height = eyeHeight;
            _leftEye.style.top = 6f + (10f - eyeHeight) / 2f + lookY;
            _rightEye.style.top = 6f + (10f - eyeHeight) / 2f + lookY;
            _leftEye.style.left = 8f + lookX;
            _rightEye.style.left = 21f + lookX;
            _mouth.style.width = mouthWidth;
            _mouth.style.left = 18f - mouthWidth / 2f;
            _antennaBall.style.backgroundColor = ball;
        }

        private void Place()
        {
            // Bob gently while hovering in place.
            float bob = _flyTime < 0f && !_dragging ? Mathf.Sin(_time * 2.2f) * 3f : 0f;
            _bot.style.left = _position.x;
            _bot.style.top = _position.y + bob;
            _shadow.style.top = 70f - bob;
            _shadow.style.opacity = 0.6f + bob * 0.05f;

            if (_bubble.style.display == DisplayStyle.None)
                return;
            var size = _window.AssistantLayer.layout.size;
            float bubbleWidth = float.IsNaN(_bubble.layout.width) ? 220f : _bubble.layout.width;
            float bubbleHeight = float.IsNaN(_bubble.layout.height) ? 40f : _bubble.layout.height;
            bool left = !float.IsNaN(size.x) && _position.x + Width + 8 + bubbleWidth > size.x;
            _bubble.style.left = left ? _position.x - bubbleWidth - 6 : _position.x + Width + 6;
            _bubble.style.top = Mathf.Max(4f, _position.y + 10 - bubbleHeight / 2f);
        }

        private void ShowBubble(string text, float seconds)
        {
            _bubbleText.text = text;
            _bubble.style.display = DisplayStyle.Flex;
            _hideBubbleAt = seconds > 0 ? _time + seconds : -1f;
        }

        private void HideBubble()
        {
            _bubble.style.display = DisplayStyle.None;
            _input.style.display = DisplayStyle.None;
        }

        #endregion

        #region Drawing helpers

        private static VisualElement Box(Rect rect, Color color, float radius)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.position = Position.Absolute;
            box.style.left = rect.x; box.style.top = rect.y;
            box.style.width = rect.width; box.style.height = rect.height;
            box.style.backgroundColor = color;
            SetRadius(box, radius);
            return box;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius; element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius; element.style.borderBottomRightRadius = radius;
        }

        private static void Border(VisualElement element, Color color, float width)
        {
            element.style.borderLeftColor = color; element.style.borderRightColor = color;
            element.style.borderTopColor = color; element.style.borderBottomColor = color;
            element.style.borderLeftWidth = width; element.style.borderRightWidth = width;
            element.style.borderTopWidth = width; element.style.borderBottomWidth = width;
        }

        #endregion
    }

    /// <summary>Menu items and the push-to-talk shortcut.</summary>
    internal static class AssistantMenu
    {
        private const string Menu = "Tools/Remedy/Schematics/Assistant/";

        [ClutchShortcut("Schematics/Assistant/Push To Talk", typeof(SchematicGraphEditorWindow), KeyCode.Q)]
        private static void PushToTalk(ShortcutArguments args)
        {
            var window = args.context as SchematicGraphEditorWindow;
            var overlay = AssistantOverlay.For(window) ?? AssistantOverlay.Attach(window);
            if (overlay == null)
                return;
            if (args.stage == ShortcutStage.Begin) overlay.BeginListening();
            else overlay.EndListening();
        }

        /// <summary>Starts the server in the background and shows the assistant in the graph window.</summary>
        [MenuItem(Menu + "Open Assistant", priority = 0)]
        private static async void Open()
        {
            var window = EditorWindow.focusedWindow as SchematicGraphEditorWindow;
            if (window == null)
                foreach (var open in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
                    if (open.SchematicGraph != null) { window = open; break; }
            if (window == null)
            {
                EditorUtility.DisplayDialog("Schematics Assistant", "Open a Schematic Graph first; the assistant lives in its window.", "OK");
                return;
            }
            AssistantSettings.Enabled = true;
            window.Focus();
            var overlay = AssistantOverlay.Attach(window);
            if (!AssistantServer.IsInstalled)
            {
                AssistantInstaller.Reinstall();
                return;
            }
            if (await AssistantServer.EnsureRunningAsync())
                overlay?.OpenInput();
        }

        [MenuItem(Menu + "Reinstall", priority = 20)]
        private static void SetUp() { AssistantServer.Stop(); AssistantInstaller.Reinstall(); }

        [MenuItem(Menu + "Start Server", priority = 21)]
        private static void Start() => _ = AssistantServer.EnsureRunningAsync();

        [MenuItem(Menu + "Stop Server", priority = 22)]
        private static void Stop() => AssistantServer.Stop();

        [MenuItem(Menu + "Preferences…", priority = 40)]
        private static void Preferences() => SettingsService.OpenUserPreferences("Preferences/Schematics Assistant");
    }
}
