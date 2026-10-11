using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Sprocket, the assistant: a little robot that floats over any editor window. Click it to type a request, or hold
    /// Q to say one. It tells you what it's going to do, then hops into the graph and flies around doing it. Drag it
    /// anywhere while it's idle (drop it over another window to move it there); Esc stops it mid-task.
    /// <para>
    /// Kept light: it's one sprite moved with a transform (no layout), animated a few times a second, and it stops
    /// animating while the editor isn't in focus.
    /// </para>
    /// </summary>
    internal sealed class AssistantOverlay : VisualElement
    {
        public enum Mood { Idle, Listening, Thinking, Working, Talking, Confused }

        private const string HostKey = "Schematics.Assistant.Host";
        private const string PositionKey = "Schematics.Assistant.Position";
        private const int TickMs = 33;          // ~30 updates a second, and only while the editor has focus
        private const float BobHeight = 3f, BobSpeed = 2.2f;

        /// <summary>The one Sprocket, wherever it is (null when hidden).</summary>
        public static AssistantOverlay Instance { get; private set; }

        private EditorWindow _host;
        private readonly VisualElement _sprite, _shadow, _bubble;
        private readonly Label _bubbleText;
        private readonly TextField _input;
        private readonly float _width, _height;

        private Mood _mood = Mood.Idle;
        private Vector2 _position;           // top-left, in the host window's root space
        private Vector2 _flyFrom, _flyTo;
        private float _flyTime = -1f, _flyDuration;
        private TaskCompletionSource<bool> _flight;
        private float _time, _hideBubbleAt = -1f;
        private bool _dragging, _pressed;
        private Vector2 _dragOffset;
        private CancellationTokenSource _work;
        private double _lastTick;
        private Texture2D _shownFrame;
        private Vector3 _shownTranslate = new(float.NaN, 0, 0);
        private Color _shownTint;
        private SchematicGraphEditorWindow _lastGraph;

        private bool Busy => _mood == Mood.Thinking || _mood == Mood.Working || _mood == Mood.Talking;

        /// <summary>The graph it works on: the one you used last.</summary>
        public SchematicGraphEditorWindow GraphWindow
        {
            get
            {
                if (_lastGraph == null || _lastGraph.SchematicGraph == null)
                    _lastGraph = Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>().FirstOrDefault(w => w.SchematicGraph != null);
                return _lastGraph;
            }
        }

        #region Sprites

        private static readonly Dictionary<string, Texture2D[]> _animations = new();

        // Frames named sprocket_<name><number> in Resources/Textures/sprocket/<name>, in number order.
        private static Texture2D[] Frames(string name)
        {
            if (_animations.TryGetValue(name, out var frames) && frames.Length > 0 && frames[0] != null)
                return frames;
            var pattern = new Regex($"^sprocket_{name}(\\d+)$");
            frames = Resources.LoadAll<Texture2D>($"Textures/sprocket/{name}")
                .Select(t => (texture: t, match: pattern.Match(t.name)))
                .Where(f => f.match.Success)
                .OrderBy(f => int.Parse(f.match.Groups[1].Value, CultureInfo.InvariantCulture))
                .Select(f => f.texture)
                .ToArray();
            if (frames.Length == 0)
                frames = Resources.LoadAll<Texture2D>($"Textures/sprocket/{name}").Take(1).ToArray();
            foreach (var frame in frames)
                frame.filterMode = FilterMode.Point; // crisp pixel art at any size
            _animations[name] = frames;
            return frames;
        }

        private (Texture2D[] frames, float fps) Animation => _mood switch
        {
            Mood.Thinking or Mood.Working => (Frames("thinking"), 12f),
            Mood.Talking => (Frames("talking"), 8f),
            Mood.Confused => (Frames("confused"), 1f),
            _ => (Frames("idle"), 12f),
        };

        #endregion

        #region Attaching

        /// <summary>
        /// Shows Sprocket (once): in the given window, or wherever it was last time. Called when a graph window opens.
        /// </summary>
        public static AssistantOverlay Attach(EditorWindow window)
        {
            if (!AssistantSettings.Enabled)
                return null;
            if (Instance != null)
                return Instance;
            var host = RememberedHost() ?? window;
            if (host == null)
                return null;
            Instance = new AssistantOverlay();
            Instance.HopTo(host, RememberedPosition(host));
            // Out of the box: the first time it appears, it installs what it needs in the background.
            AssistantInstaller.EnsureInstalled();
            return Instance;
        }

        /// <summary>Brings Sprocket to a window (the focused one, from the menu).</summary>
        public static void Summon(EditorWindow window)
        {
            if (window == null) return;
            AssistantSettings.Enabled = true;
            if (Instance == null)
            {
                Instance = new AssistantOverlay();
                AssistantInstaller.EnsureInstalled();
            }
            var size = window.rootVisualElement.layout.size;
            Instance.HopTo(window, float.IsNaN(size.x) ? new Vector2(40, 40) : new Vector2(size.x * 0.5f, size.y * 0.5f));
        }

        public static void Hide()
        {
            if (Instance == null) return;
            var overlay = Instance;
            Instance = null;
            AssistantInstaller.Finished -= overlay.OnInstallFinished;
            overlay.Detach();
            overlay.RemoveFromHierarchy();
        }

        private static EditorWindow RememberedHost()
        {
            string type = SessionState.GetString(HostKey, "");
            if (type.Length == 0) return null;
            return Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(w => w.GetType().FullName == type);
        }

        private static Vector2 RememberedPosition(EditorWindow host)
        {
            var parts = SessionState.GetString(PositionKey, "").Split(',');
            if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
            var size = host != null ? host.rootVisualElement.layout.size : Vector2.zero;
            return float.IsNaN(size.x) || size.x < 200 ? new Vector2(320, 60) : new Vector2(size.x - 100, size.y - 120);
        }

        private void Remember()
        {
            if (_host == null) return;
            SessionState.SetString(HostKey, _host.GetType().FullName);
            SessionState.SetString(PositionKey, string.Format(CultureInfo.InvariantCulture, "{0},{1}", _position.x, _position.y));
        }

        // After a script reload, Sprocket comes back where it was.
        [InitializeOnLoadMethod]
        private static void Restore() => EditorApplication.delayCall += () =>
        {
            if (Instance == null && AssistantSettings.Enabled && RememberedHost() != null)
                Attach(null);
        };

        /// <summary>Moves into another window at a position in its root space (instantly).</summary>
        public void HopTo(EditorWindow window, Vector2 position)
        {
            if (window == null)
                return;
            if (window != _host)
            {
                Detach();
                _host = window;
                window.rootVisualElement.Add(this);
                window.rootVisualElement.RegisterCallback<KeyDownEvent>(OnHostKeyDown, TrickleDown.TrickleDown);
                window.rootVisualElement.RegisterCallback<KeyUpEvent>(OnHostKeyUp, TrickleDown.TrickleDown);
                window.rootVisualElement.RegisterCallback<PointerDownEvent>(OnHostPointerDown, TrickleDown.TrickleDown);
                if (window is SchematicGraphEditorWindow graph)
                    _lastGraph = graph;
            }
            else
                BringToFront();
            _flyTime = -1f;
            _flight?.TrySetResult(false);
            _flight = null;
            _position = position;
            schedule.Execute(() => { _position = Clamp(_position); Place(force: true); Remember(); });
            Place(force: true);
            Remember();
        }

        /// <summary>Hops into a window (if it isn't there yet), entering from its nearest edge.</summary>
        public void EnsureIn(EditorWindow window)
        {
            if (window == null || window == _host)
                return;
            // From the same screen spot, kept inside the new window.
            Vector2 screen = _host != null ? _host.position.position + _position : window.position.center;
            HopTo(window, screen - window.position.position);
        }

        private void Detach()
        {
            if (_host != null && _host.rootVisualElement != null)
            {
                _host.rootVisualElement.UnregisterCallback<KeyDownEvent>(OnHostKeyDown, TrickleDown.TrickleDown);
                _host.rootVisualElement.UnregisterCallback<KeyUpEvent>(OnHostKeyUp, TrickleDown.TrickleDown);
                _host.rootVisualElement.UnregisterCallback<PointerDownEvent>(OnHostPointerDown, TrickleDown.TrickleDown);
            }
            RemoveFromHierarchy();
            _host = null;
        }

        // If its window closes, it moves to another (a graph window if one is open) instead of vanishing.
        private void OnDetached(DetachFromPanelEvent evt)
        {
            if (Instance != this || _rehoming)
                return;
            _rehoming = true;
            EditorApplication.delayCall += () =>
            {
                _rehoming = false;
                if (Instance != this || panel != null) return;
                _host = null;
                var next = (EditorWindow)GraphWindow ?? EditorWindow.focusedWindow;
                if (next != null) HopTo(next, new Vector2(40, 40));
            };
        }

        private bool _rehoming;

        #endregion

        private AssistantOverlay()
        {
            name = "schematics-assistant";
            pickingMode = PickingMode.Ignore;
            // A zero-size anchor at the window's top-left; everything inside is placed with transforms.
            style.position = Position.Absolute;
            style.left = 0;
            style.top = 0;
            style.width = 0;
            style.height = 0;
            style.overflow = Overflow.Visible;

            var idle = Frames("idle");
            float scale = Mathf.Max(1, AssistantSettings.SpriteScale);
            _width = (idle.Length > 0 ? idle[0].width : 76) * scale;
            _height = (idle.Length > 0 ? idle[0].height : 92) * scale;

            bool dark = EditorGUIUtility.isProSkin;
            Color edge = new Color(0.47f, 0.38f, 0.72f);

            // A soft shadow below, so it reads as floating.
            _shadow = new VisualElement { pickingMode = PickingMode.Ignore };
            _shadow.style.position = Position.Absolute;
            _shadow.style.width = _width * 0.45f;
            _shadow.style.height = Mathf.Max(3f, 5f * scale);
            _shadow.style.backgroundColor = new Color(0f, 0f, 0f, 0.22f);
            SetRadius(_shadow, 6f * scale);
            Add(_shadow);

            _sprite = new VisualElement { name = "assistant-sprite", tooltip = "Sprocket: click to ask, hold Q to talk, drag to move (drop on any window)" };
            _sprite.style.position = Position.Absolute;
            _sprite.style.width = _width;
            _sprite.style.height = _height;
            Add(_sprite);

            _bubble = new VisualElement { name = "assistant-bubble", pickingMode = PickingMode.Position };
            _bubble.style.position = Position.Absolute;
            _bubble.style.maxWidth = 280;
            _bubble.style.minWidth = 160;
            _bubble.style.paddingLeft = 10; _bubble.style.paddingRight = 10;
            _bubble.style.paddingTop = 7; _bubble.style.paddingBottom = 7;
            _bubble.style.backgroundColor = dark ? new Color(0.17f, 0.16f, 0.22f, 0.97f) : new Color(1f, 0.98f, 1f, 0.98f);
            Border(_bubble, edge, 1.5f);
            SetRadius(_bubble, 10);
            _bubble.style.display = DisplayStyle.None;
            Add(_bubble);

            // A little nameplate on top of the bubble, so everyone knows who's talking.
            _bubble.style.marginTop = 0;
            _bubble.style.paddingTop = 12;
            var nameplate = new Label("Sprocket") { name = "assistant-nameplate", pickingMode = PickingMode.Ignore };
            nameplate.style.position = Position.Absolute;
            nameplate.style.top = -9;
            nameplate.style.left = 10;
            nameplate.style.paddingLeft = 7; nameplate.style.paddingRight = 7;
            nameplate.style.paddingTop = 1; nameplate.style.paddingBottom = 2;
            nameplate.style.fontSize = 10;
            nameplate.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameplate.style.color = Color.white;
            nameplate.style.backgroundColor = edge;
            SetRadius(nameplate, 6);
            _bubble.Add(nameplate);

            _bubbleText = new Label { enableRichText = true };
            _bubbleText.style.whiteSpace = WhiteSpace.Normal;
            _bubbleText.style.color = dark ? new Color(0.92f, 0.9f, 1f) : new Color(0.15f, 0.12f, 0.25f);
            _bubble.Add(_bubbleText);

            _input = new TextField { name = "assistant-input" };
            _input.style.marginTop = 6;
            _input.style.display = DisplayStyle.None;
            _input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);
            _bubble.Add(_input);
            _bubble.RegisterCallback<GeometryChangedEvent>(_ => Place(force: true));

            _sprite.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _sprite.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Ask…", _ => OpenInput());
                evt.menu.AppendAction("Stop", _ => Cancel(), Busy ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Preferences…", _ => SettingsService.OpenUserPreferences("Preferences/Schematics Assistant"));
                evt.menu.AppendAction(AssistantServer.IsInstalled ? "Reinstall" : "Install", _ => { AssistantServer.Stop(); AssistantInstaller.Reinstall(); },
                    AssistantInstaller.IsInstalling ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("Hide", _ => Hide());
                evt.StopPropagation();
            }));

            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            AssistantInstaller.Finished += OnInstallFinished;

            _lastTick = EditorApplication.timeSinceStartup;
            _lastFocused = EditorWindow.focusedWindow;
            schedule.Execute(Tick).Every(TickMs);
        }

        #region Keys

        // A click anywhere but the bubble (or Sprocket) puts the bubble away.
        private void OnHostPointerDown(PointerDownEvent evt)
        {
            if (_bubble.style.display == DisplayStyle.None || _mood == Mood.Listening)
                return;
            var target = evt.target as VisualElement;
            if (target != null && (target == _bubble || _bubble.Contains(target) || target == _sprite))
                return;
            HideBubble();
        }

        // ...and so does clicking into another window.
        private EditorWindow _lastFocused;

        private void HideOnFocusChange()
        {
            var focused = EditorWindow.focusedWindow;
            if (focused == _lastFocused)
                return;
            _lastFocused = focused;
            if (focused != null && focused != _host && !_pressed && !Busy && _mood != Mood.Listening && _bubble.style.display != DisplayStyle.None)
                HideBubble();
        }

        private void OnHostKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && Busy)
            {
                Cancel();
                evt.StopPropagation();
                return;
            }
            // Push-to-talk outside graph windows (graph windows use the Q shortcut). Not in the Scene and Game views,
            // where Q is a tool key, and not while typing.
            if (PushToTalkHere(evt) && !_heldQ)
            {
                _heldQ = true;
                BeginListening();
                evt.StopPropagation();
            }
        }

        private void OnHostKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode != KeyCode.Q || !_heldQ)
                return;
            _heldQ = false;
            EndListening();
            evt.StopPropagation();
        }

        private bool _heldQ;

        private bool PushToTalkHere(KeyDownEvent evt)
            => evt.keyCode == KeyCode.Q && !evt.ctrlKey && !evt.altKey && !evt.commandKey
               && !(_host is SchematicGraphEditorWindow) && !(_host is SceneView)
               && _host.GetType().Name != "GameView"
               && !(evt.target is TextField) && (evt.target as VisualElement)?.GetFirstAncestorOfType<TextField>() == null
               && !EditorGUIUtility.editingTextField;

        #endregion

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
            var homeHost = _host;
            var home = _position;
            _mood = Mood.Thinking;
            ShowBubble($"<i>“{Escape(utterance)}”</i>\nHmm, let me think…", seconds: -1);

            try
            {
                if (!await AssistantServer.EnsureRunningAsync())
                {
                    SetupNeeded();
                    return;
                }

                var context = new AssistantContext(GraphWindow);
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

            // Back to where it was before the task.
            if (homeHost != null && homeHost != _host)
                HopTo(homeHost, home);
            else
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

        private Vector2 HostSize
        {
            get
            {
                var size = _host != null ? _host.rootVisualElement.layout.size : Vector2.zero;
                return float.IsNaN(size.x) ? Vector2.zero : size;
            }
        }

        private Vector2 Clamp(Vector2 position)
        {
            var size = HostSize;
            if (size.x <= _width || size.y <= _height)
                return position;
            return new Vector2(Mathf.Clamp(position.x, 0, size.x - _width), Mathf.Clamp(position.y, 0, size.y - _height));
        }

        /// <summary>Flies to a point (top-left of the robot, in the host window's root space). Completes on arrival.</summary>
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

        // A drag is followed from the window it started in: while the button is held, the system keeps sending that
        // window the mouse, even over other windows. So Sprocket can move into whichever window is under the pointer
        // right away, while the drag carries on.
        private VisualElement _dragRoot;
        private EditorWindow _dragWindow;
        private int _dragPointer;

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || _host == null)
                return;
            _pressed = true;
            _dragging = false;
            _dragOffset = (Vector2)evt.localPosition;
            _dragWindow = _host;
            _dragRoot = _host.rootVisualElement;
            _dragPointer = evt.pointerId;
            _dragRoot.CapturePointer(evt.pointerId);
            _dragRoot.RegisterCallback<PointerMoveEvent>(OnDragMove);
            _dragRoot.RegisterCallback<PointerUpEvent>(OnDragUp);
            _dragRoot.RegisterCallback<PointerCaptureOutEvent>(OnDragLost);
            evt.StopPropagation();
        }

        // The pointer in screen space (from the window the drag started in).
        private Vector2 ScreenPoint(IPointerEvent evt) => _dragWindow.position.position + (Vector2)_dragRoot.WorldToLocal(evt.position);

        private void OnDragMove(PointerMoveEvent evt)
        {
            if (!_pressed || Busy || _dragWindow == null)
                return;
            Vector2 screen = ScreenPoint(evt) - _dragOffset;
            if (!_dragging && _host != null && Vector2.Distance(screen - _host.position.position, _position) < 4f)
                return;
            _dragging = true;

            // Over another window: move into it now.
            var under = WindowAt(ScreenPoint(evt));
            if (under != null && under != _host)
            {
                HopTo(under, screen - under.position.position);
                under.Repaint();
            }
            else if (_host != null)
            {
                _position = Clamp(screen - _host.position.position);
                Place(force: true);
                if (_host != _dragWindow) _host.Repaint();
            }
            evt.StopPropagation();
        }

        private void OnDragUp(PointerUpEvent evt)
        {
            if (!_pressed)
                return;
            bool dragged = _dragging;
            EndDrag();
            if (dragged)
                Remember();
            else if (_mood == Mood.Listening) { }
            else if (Busy) Cancel();
            else if (_input.style.display == DisplayStyle.Flex) { _input.style.display = DisplayStyle.None; HideBubble(); }
            else OpenInput();
            evt.StopPropagation();
        }

        private void OnDragLost(PointerCaptureOutEvent evt)
        {
            if (_pressed)
            {
                EndDrag();
                Remember();
            }
        }

        private void EndDrag()
        {
            _pressed = false;
            _dragging = false;
            if (_dragRoot != null)
            {
                _dragRoot.UnregisterCallback<PointerMoveEvent>(OnDragMove);
                _dragRoot.UnregisterCallback<PointerUpEvent>(OnDragUp);
                _dragRoot.UnregisterCallback<PointerCaptureOutEvent>(OnDragLost);
                if (_dragRoot.HasPointerCapture(_dragPointer))
                    _dragRoot.ReleasePointer(_dragPointer);
            }
            _dragRoot = null;
            _dragWindow = null;
        }

        /// <summary>The editor window showing at a screen point (the visible tab, if several are docked there).</summary>
        private static EditorWindow WindowAt(Vector2 screen)
        {
            var over = EditorWindow.mouseOverWindow;
            if (over != null && over.position.Contains(screen))
                return over;
            EditorWindow found = null;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (!window.position.Contains(screen) || !IsShownTab(window))
                    continue;
                // Floating windows sit above docked ones; the focused one above the rest.
                if (found == null || window == EditorWindow.focusedWindow || (!window.docked && found.docked))
                    found = window;
            }
            return found;
        }

        private static readonly System.Reflection.FieldInfo _parentField =
            typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // A docked window is only showing if it's its dock area's current tab.
        private static bool IsShownTab(EditorWindow window)
        {
            var parent = _parentField?.GetValue(window);
            if (parent == null)
                return false;
            var actualView = parent.GetType().GetProperty("actualView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return actualView == null || ReferenceEquals(actualView.GetValue(parent), window);
        }

        #endregion

        #region Animation

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = Mathf.Min(0.1f, (float)(now - _lastTick));
            _lastTick = now;

            // Nothing moves while the editor is in the background, so nothing redraws.
            if (!InternalEditorUtility.isApplicationActive && _flyTime < 0f)
                return;
            _time += dt;

            HideOnFocusChange();
            var focused = EditorWindow.focusedWindow as SchematicGraphEditorWindow;
            if (focused != null)
                _lastGraph = focused;

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

            // The current animation frame (only touched when it changes).
            var (frames, fps) = Animation;
            if (frames.Length > 0)
            {
                var frame = frames[(int)(_time * fps) % frames.Length];
                if (frame != _shownFrame)
                {
                    _shownFrame = frame;
                    _sprite.style.backgroundImage = frame;
                }
            }

            // Listening: a soft pink pulse.
            Color tint = _mood == Mood.Listening ? Color.Lerp(Color.white, new Color(1f, 0.6f, 0.75f), 0.5f + 0.5f * Mathf.Sin(_time * 8f)) : Color.white;
            if (tint != _shownTint)
            {
                _shownTint = tint;
                _sprite.style.unityBackgroundImageTintColor = tint;
            }

            Place(force: false);
        }

        /// <summary>
        /// Positions everything with transforms (no layout). The bob moves in whole pixels of the sprite, so it only
        /// changes a few times a second.
        /// </summary>
        private void Place(bool force)
        {
            float scale = Mathf.Max(1, AssistantSettings.SpriteScale);
            float bob = _flyTime < 0f && !_dragging ? Mathf.Round(Mathf.Sin(_time * BobSpeed) * BobHeight) * scale : 0f;
            var translate = new Vector3(Mathf.Round(_position.x), Mathf.Round(_position.y + bob), 0f);
            if (!force && translate == _shownTranslate)
                return;
            _shownTranslate = translate;

            _sprite.style.translate = new Translate(translate.x, translate.y, 0f);
            float shadowY = _position.y + _height + 2f;
            float lift = 1f - bob / (BobHeight * scale * 4f);
            _shadow.style.translate = new Translate(Mathf.Round(_position.x + _width * 0.275f), Mathf.Round(shadowY), 0f);
            _shadow.style.scale = new Scale(new Vector3(lift, 1f, 1f));

            if (_bubble.style.display == DisplayStyle.None)
                return;
            var size = HostSize;
            float bubbleWidth = float.IsNaN(_bubble.layout.width) ? 220f : _bubble.layout.width;
            float bubbleHeight = float.IsNaN(_bubble.layout.height) ? 40f : _bubble.layout.height;
            bool left = size.x > 0 && _position.x + _width + 8 + bubbleWidth > size.x;
            float x = left ? _position.x - bubbleWidth - 6 : _position.x + _width + 6;
            float y = Mathf.Max(4f, _position.y + _height * 0.3f - bubbleHeight / 2f);
            _bubble.style.translate = new Translate(Mathf.Round(x), Mathf.Round(y), 0f);
        }

        private void ShowBubble(string text, float seconds)
        {
            _bubbleText.text = text;
            _bubble.style.display = DisplayStyle.Flex;
            _hideBubbleAt = seconds > 0 ? _time + seconds : -1f;
            Place(force: true);
        }

        private void HideBubble()
        {
            _bubble.style.display = DisplayStyle.None;
            _input.style.display = DisplayStyle.None;
        }

        #endregion

        #region Drawing helpers

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
            var overlay = AssistantOverlay.Instance ?? AssistantOverlay.Attach(window);
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

        [MenuItem(Menu + "Bring Sprocket Here", priority = 1)]
        private static void BringHere() => AssistantOverlay.Summon(EditorWindow.focusedWindow ?? EditorWindow.mouseOverWindow);

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
