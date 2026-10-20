using System;
using System.Collections.Generic;
using System.Reflection;
using Remedy.Schematics.Collaboration;
using UnityEngine;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// Notes over the editor's collaboration session (<see cref="CollabSession"/>). Installed as the store's transport
    /// whenever the editor loads, play mode included, so an editor sends each note once (the runtime client the editor
    /// may also run in play mode is left alone).
    /// </summary>
    public sealed class CollabSessionTransport : IStickyNotesTransport
    {
        private readonly Dictionary<Action<string, string>, Action<CollabPeer, string>> _adapters = new Dictionary<Action<string, string>, Action<CollabPeer, string>>();

        public bool IsConnected => CollabSession.IsConnected;
        public bool CanSaveToRepository => true;
        public string LocalPeerId => CollabSession.Local?.Id ?? "";
        public string LocalName => !string.IsNullOrEmpty(CollabSession.Local?.Name) ? CollabSession.Local.Name : StickyNoteStore.FallbackName;
        public Color LocalColor => CollabSession.Local != null ? CollabSession.Local.Color : Color.white;

        public event Action<string> SharedChanged;
        public event Action Connected;
        public event Action Disconnected;

        public CollabSessionTransport()
        {
            CollabSession.SharedChanged += key => SharedChanged?.Invoke(key);
            CollabSession.Connected += () => Connected?.Invoke();
            CollabSession.Disconnected += () => Disconnected?.Invoke();
        }

        public void Send(string channel, string json)
        {
            if (CollabSession.IsConnected) CollabSession.Send(channel, json);
        }

        public void Subscribe(string channel, Action<string, string> handler)
        {
            if (handler == null || _adapters.ContainsKey(handler)) return;
            Action<CollabPeer, string> adapter = (peer, json) => handler(peer?.Id ?? "", json);
            _adapters[handler] = adapter;
            CollabSession.Subscribe(channel, adapter);
        }

        public void Unsubscribe(string channel, Action<string, string> handler)
        {
            if (handler == null || !_adapters.TryGetValue(handler, out var adapter)) return;
            CollabSession.Unsubscribe(channel, adapter);
            _adapters.Remove(handler);
        }

        public void SetShared(string key, string json)
        {
            if (CollabSession.IsConnected) CollabSession.SetShared(key, json);
        }

        public string GetShared(string key) => CollabSession.GetShared(key);

        /// <summary>
        /// True for a peer that's playing a build (or an editor in Play Mode's runtime client) rather than editing. The
        /// session's peer record may carry a Role (read by name, so this works whichever way the collaboration team
        /// exposes it); otherwise a "role" presence key is checked. Unknown means editor.
        /// </summary>
        public static bool IsPlaytester(CollabPeer peer)
        {
            if (peer == null) return false;
            try
            {
                var type = typeof(CollabPeer);
                object role = type.GetField("Role", BindingFlags.Public | BindingFlags.Instance)?.GetValue(peer)
                              ?? type.GetProperty("Role", BindingFlags.Public | BindingFlags.Instance)?.GetValue(peer);
                if (role != null) return role.ToString().IndexOf("playtester", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { }
            if (peer.Presence != null && peer.Presence.TryGetValue("role", out var presence) && presence != null)
                return presence.IndexOf("playtester", StringComparison.OrdinalIgnoreCase) >= 0;
            return false;
        }
    }
}
