using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Team chat on the "chat" channel, so nobody needs another app open. The host keeps the last 200 messages in the
    /// shared store ("chat") for people who join later; each editor also keeps its copy across script reloads.
    /// Chat works in Play Mode too (it doesn't touch the scene).
    /// </summary>
    [InitializeOnLoad]
    public static class ChatService
    {
        public const int MaxMessages = 200;
        private const string SessionKey = "Remedy.Collab.Chat";

        private static readonly List<ChatMsg> _messages = new();
        private static readonly HashSet<string> _ids = new();

        /// <summary>Raised for each new message (local or remote).</summary>
        public static event Action<ChatMsg> MessageAdded;

        /// <summary>Messages, oldest first.</summary>
        public static IReadOnlyList<ChatMsg> Messages => _messages;

        static ChatService()
        {
            try
            {
                var saved = JsonUtility.FromJson<ChatLog>(SessionState.GetString(SessionKey, ""));
                if (saved?.messages != null) foreach (var m in saved.messages) AddInternal(m, notify: false, share: false);
            }
            catch (Exception) { }
            CollabSession.Subscribe(CollabChannels.Chat, OnMessage);
            CollabSession.Connected += () => CollabFeatures.Safe(LoadShared);
            CollabSession.SharedChanged += key => { if (key == CollabChannels.SharedChat && !CollabSession.IsHost) CollabFeatures.Safe(LoadShared); };
            if (CollabSession.IsConnected) EditorApplication.delayCall += () => CollabFeatures.Safe(LoadShared);
        }

        /// <summary>Sends a message to everyone.</summary>
        public static void Send(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || !CollabSession.IsConnected) return;
            var local = CollabSession.Local;
            var msg = new ChatMsg
            {
                id = CollabFeatures.NewMessageId(),
                from = CollabFeatures.LocalId,
                name = local?.Name ?? "Me",
                color = CollabFeatures.Hex(local?.Color ?? Color.white),
                text = text.Length > 2000 ? text.Substring(0, 2000) : text,
                t = CollabFeatures.Now,
            };
            CollabSession.Send(CollabChannels.Chat, JsonUtility.ToJson(msg));
            AddInternal(msg, notify: true);
        }

        private static void OnMessage(CollabPeer peer, string json)
        {
            ChatMsg msg;
            try { msg = JsonUtility.FromJson<ChatMsg>(json); }
            catch (Exception) { return; }
            if (msg == null || string.IsNullOrEmpty(msg.text)) return;
            if (peer != null)
            {
                msg.from = peer.Id;
                if (string.IsNullOrEmpty(msg.name)) msg.name = peer.Name;
                if (string.IsNullOrEmpty(msg.color)) msg.color = CollabFeatures.Hex(peer.Color);
            }
            AddInternal(msg, notify: true);
        }

        private static void LoadShared()
        {
            var json = CollabSession.GetShared(CollabChannels.SharedChat);
            if (string.IsNullOrEmpty(json)) return;
            var log = JsonUtility.FromJson<ChatLog>(json);
            if (log?.messages == null) return;
            foreach (var m in log.messages) AddInternal(m, notify: false, share: false);
            _messages.Sort((a, b) => a.t.CompareTo(b.t));
        }

        private static void AddInternal(ChatMsg msg, bool notify, bool share = true)
        {
            if (msg == null || string.IsNullOrEmpty(msg.id) || !_ids.Add(msg.id)) return;
            _messages.Add(msg);
            while (_messages.Count > MaxMessages)
            {
                _ids.Remove(_messages[0].id);
                _messages.RemoveAt(0);
            }
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new ChatLog { messages = _messages }));
            if (share && CollabSession.IsConnected && CollabSession.IsHost)
                CollabSession.SetShared(CollabChannels.SharedChat, JsonUtility.ToJson(new ChatLog { messages = _messages }));
            if (notify) MessageAdded?.Invoke(msg);
        }
    }
}
