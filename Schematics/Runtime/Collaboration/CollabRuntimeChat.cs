using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>A chat message: the same shape as the editors' ChatMsg (Features/CollabMessages.cs).</summary>
    [Serializable]
    public sealed class CollabChatMessage
    {
        public string id;
        public string from;
        public string name;
        /// <summary>"#RRGGBB".</summary>
        public string color;
        public string text;
        /// <summary>Unix time in ms.</summary>
        public long t;
    }

    [Serializable]
    internal sealed class CollabChatLog
    {
        public List<CollabChatMessage> messages = new List<CollabChatMessage>();
    }

    /// <summary>
    /// A small chat inside playtest builds, on the "chat" channel the editors' Team window uses, so a developer can talk
    /// to someone while watching them play. Enter opens it, Enter sends, Escape closes. New messages show for a few
    /// seconds in the bottom-left corner while it's closed. Drawn with IMGUI, so it works with either input system.
    /// While typing, keys still reach the game as well (the game's input isn't paused).
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class CollabRuntimeChat : MonoBehaviour
    {
        private const int MaxMessages = 100;
        private const float ShowSeconds = 10f;
        private const string InputName = "SchematicsCollabChatInput";

        /// <summary>Whether the chat is available (build info: on by default in builds; off in the editor, which has the Team window).</summary>
        public static bool Enabled = true;

        /// <summary>The key that opens the chat.</summary>
        public static KeyCode OpenKey = KeyCode.Return;

        private readonly List<(CollabChatMessage msg, float at)> _messages = new List<(CollabChatMessage, float)>();
        private readonly HashSet<string> _ids = new HashSet<string>();
        private bool _open, _focus;
        private string _draft = "";
        private int _counter;
        private GUIStyle _style, _box;

        private void OnEnable()
        {
            CollabRuntime.Subscribe(CollabRuntime.ChatChannel, OnMessage);
            CollabRuntime.Connected += LoadHistory;
            CollabRuntime.SharedChanged += OnShared;
            if (CollabRuntime.IsConnected) LoadHistory();
        }

        private void OnDisable()
        {
            CollabRuntime.Unsubscribe(CollabRuntime.ChatChannel, OnMessage);
            CollabRuntime.Connected -= LoadHistory;
            CollabRuntime.SharedChanged -= OnShared;
        }

        private void OnShared(string key)
        {
            if (key == CollabRuntime.ChatChannel) LoadHistory();
        }

        private void LoadHistory()
        {
            CollabLog.Safe("Loading the chat history", () =>
            {
                var json = CollabRuntime.GetShared(CollabRuntime.ChatChannel);
                if (string.IsNullOrEmpty(json)) return;
                var log = JsonUtility.FromJson<CollabChatLog>(json);
                if (log?.messages == null) return;
                foreach (var msg in log.messages) Add(msg, fresh: false);
                _messages.Sort((a, b) => a.msg.t.CompareTo(b.msg.t));
            });
        }

        private void OnMessage(string peerId, string json)
        {
            CollabChatMessage msg;
            try { msg = JsonUtility.FromJson<CollabChatMessage>(json); }
            catch (Exception e) { CollabLog.Warning("Ignored an unreadable chat message (" + e.Message + ")."); return; }
            if (msg == null || string.IsNullOrEmpty(msg.text)) return;
            var peer = CollabRuntime.FindPeer(peerId);
            if (peer != null)
            {
                msg.from = peer.Id;
                if (string.IsNullOrEmpty(msg.name)) msg.name = peer.Name;
                if (string.IsNullOrEmpty(msg.color)) msg.color = "#" + ColorUtility.ToHtmlStringRGB(peer.Color);
            }
            if (string.IsNullOrEmpty(msg.id)) msg.id = peerId + ":" + msg.t;
            Add(msg, fresh: true);
        }

        private void Add(CollabChatMessage msg, bool fresh)
        {
            if (msg == null || string.IsNullOrEmpty(msg.id) || !_ids.Add(msg.id)) return;
            _messages.Add((msg, fresh ? Time.unscaledTime : -100f));
            while (_messages.Count > MaxMessages)
            {
                _ids.Remove(_messages[0].msg.id);
                _messages.RemoveAt(0);
            }
        }

        private void Send(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || !CollabRuntime.IsConnected) return;
            var msg = new CollabChatMessage
            {
                id = (CollabRuntime.LocalPeerId ?? "game") + ":" + DateTime.UtcNow.Ticks.ToString("x") + ":" + (++_counter),
                from = CollabRuntime.LocalPeerId,
                name = CollabRuntime.LocalName,
                color = "#" + ColorUtility.ToHtmlStringRGB(CollabRuntime.LocalColor),
                text = text.Length > 2000 ? text.Substring(0, 2000) : text,
                t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            CollabRuntime.Send(CollabRuntime.ChatChannel, JsonUtility.ToJson(msg));
            Add(msg, fresh: true);
        }

        private void OnGUI()
        {
            if (!Enabled || !CollabRuntime.IsAvailable) return;
            try { Draw(); }
            catch (Exception e) { CollabLog.Exception("Drawing the chat failed", e); }
        }

        private void Draw()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13 };
                _box = new GUIStyle(GUI.skin.box);
            }
            var e = Event.current;
            if (!_open && e.type == EventType.KeyDown && (e.keyCode == OpenKey || (OpenKey == KeyCode.Return && e.keyCode == KeyCode.KeypadEnter)) && CollabRuntime.IsConnected)
            {
                _open = true;
                _focus = true;
                e.Use();
                return;
            }

            float width = Mathf.Min(460f, Screen.width - 20f);
            float now = Time.unscaledTime;
            var shown = new List<CollabChatMessage>();
            for (int i = _messages.Count - 1; i >= 0 && shown.Count < (_open ? 12 : 5); i--)
                if (_open || now - _messages[i].at < ShowSeconds) shown.Insert(0, _messages[i].msg);
            if (!_open && shown.Count == 0) return;

            float lineHeight = 20f;
            float height = shown.Count * lineHeight + (_open ? 34f : 8f);
            var area = new Rect(10f, Screen.height - height - 10f, width, height);
            if (_open) GUI.Box(area, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(area.x + 6f, area.y + 4f, area.width - 12f, area.height - 8f));
            foreach (var msg in shown)
            {
                string color = string.IsNullOrEmpty(msg.color) ? "#FFFFFF" : msg.color;
                GUILayout.Label($"<b><color={color}>{Escape(msg.name)}</color></b>  {Escape(msg.text)}", _style, GUILayout.Height(lineHeight));
            }
            if (_open)
            {
                if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == InputName)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    {
                        Send(_draft);
                        _draft = "";
                        _open = false;
                        GUI.FocusControl(null);
                        e.Use();
                    }
                    else if (e.keyCode == KeyCode.Escape)
                    {
                        _open = false;
                        GUI.FocusControl(null);
                        e.Use();
                    }
                }
                if (_open)
                {
                    GUI.SetNextControlName(InputName);
                    _draft = GUILayout.TextField(_draft ?? "", 500);
                    if (_focus && e.type == EventType.Repaint)
                    {
                        _focus = false;
                        GUI.FocusControl(InputName);
                    }
                }
            }
            GUILayout.EndArea();
        }

        private static string Escape(string text) => (text ?? "").Replace("<", "<​");
    }
}
