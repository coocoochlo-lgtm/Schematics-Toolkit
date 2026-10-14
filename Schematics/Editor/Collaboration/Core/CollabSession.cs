using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Someone in the live session (you included). The same object stays valid while they're in the session; its fields
    /// update in place and <see cref="CollabSession.PeerChanged"/> says when.
    /// </summary>
    public sealed class CollabPeer
    {
        /// <summary>Stable for as long as their helper runs (it survives their script reloads).</summary>
        public string Id;
        public string Name;
        /// <summary>Handed out by the host so everyone in the session has a different one.</summary>
        public Color Color;
        public string AvatarId;
        /// <summary>The host keeps the shared store and commits scene snapshots. It moves when the host leaves.</summary>
        public bool IsHost;
        /// <summary>Round trip from you to them in milliseconds; -1 until measured, 0 for yourself.</summary>
        public int PingMs;
        /// <summary>
        /// Their small replicated state (camera, selection, location...), set with <see cref="CollabSession.SetPresence"/>.
        /// Also holds "git.name" and "git.email" from their git config (used as Co-authored-by in scene snapshots).
        /// </summary>
        public IReadOnlyDictionary<string, string> Presence = new Dictionary<string, string>();
        public bool IsLocal;
        /// <summary>
        /// Editor: editing in a Unity editor. Playtester: playing the game, in a build or in an editor's Play Mode.
        /// Editors' scene edits reach playtesters who switched on "Receive live edits" (see CollabLiveEdits).
        /// </summary>
        public CollabRole Role;
        /// <summary>True for a build of the game (a playtest client). Builds join sessions but never host them.</summary>
        public bool IsBuild;

        public override string ToString() => $"{Name} ({(Id != null && Id.Length > 8 ? Id.Substring(0, 8) : Id)}{(IsHost ? ", host" : "")}{(Role == CollabRole.Playtester ? ", playtester" : "")})";
    }

    /// <summary>
    /// The live editing session: who's here, messages between editors, per-peer presence and a shared store.
    ///
    /// It needs no setup: everyone who opens the same project (same git remote and branch) on the same network ends up
    /// in one session. The first editor up hosts; when the host leaves, the peer with the best ping takes over and
    /// everyone reconnects to it by themselves. A helper process (Collab~/SchematicsCollab) does the networking, so the
    /// session survives script reloads (missed messages are replayed) and editor freezes during long imports.
    /// Everything is raised on the main thread.
    /// </summary>
    public static class CollabSession
    {
        /// <summary>In a session (even alone, as its host). False while the helper starts or between hosts.</summary>
        public static bool IsConnected => CollabBridge.InSession;

        /// <summary>This editor is the host (it keeps the shared store and commits scene snapshots).</summary>
        public static bool IsHost => CollabBridge.Local.IsHost && CollabBridge.InSession;

        /// <summary>You. Always available (with your settings' name before the session starts).</summary>
        public static CollabPeer Local => CollabBridge.Local;

        /// <summary>Everyone in the session, you included.</summary>
        public static IReadOnlyList<CollabPeer> Peers => CollabBridge.PeerList;

        public static event Action<CollabPeer> PeerJoined;
        public static event Action<CollabPeer> PeerLeft;
        /// <summary>Name, colour, presence, ping or host flag of a peer changed.</summary>
        public static event Action<CollabPeer> PeerChanged;
        public static event Action HostChanged;
        public static event Action Connected;
        public static event Action Disconnected;
        /// <summary>A shared store key changed (by anyone, you included).</summary>
        public static event Action<string> SharedChanged;

        /// <summary>
        /// Sends to everyone else (or only to <paramref name="toPeerId"/>). Reliable and in order. Sent while between
        /// hosts, it's held and delivered once the session is back. You don't receive your own messages.
        /// </summary>
        public static void Send(string channel, string json, string toPeerId = null)
        {
            if (string.IsNullOrEmpty(channel)) throw new ArgumentException("A channel name is needed.", nameof(channel));
            if (channel.StartsWith(CollabProtocol.SystemChannelPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"Channels starting with {CollabProtocol.SystemChannelPrefix} are reserved.", nameof(channel));
            CollabBridge.Post(new CollabFrame { type = CollabFrameType.Send, channel = channel, json = json ?? "", to = toPeerId ?? "" });
        }

        /// <summary>Calls <paramref name="handler"/> with the sender and the JSON for every message on the channel.</summary>
        public static void Subscribe(string channel, Action<CollabPeer, string> handler)
        {
            if (string.IsNullOrEmpty(channel) || handler == null) return;
            if (!_handlers.TryGetValue(channel, out var list)) _handlers[channel] = list = new List<Action<CollabPeer, string>>();
            if (!list.Contains(handler)) list.Add(handler);
        }

        public static void Unsubscribe(string channel, Action<CollabPeer, string> handler)
        {
            if (channel != null && _handlers.TryGetValue(channel, out var list)) list.Remove(handler);
        }

        /// <summary>Sets one key of your presence (null removes it). Keep it small: it goes to everyone on every change.</summary>
        public static void SetPresence(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) return;
            CollabBridge.SetLocalPresence(key, json);
            CollabBridge.Post(new CollabFrame { type = CollabFrameType.Presence, key = key, json = json ?? "", flag = json == null });
        }

        public static CollabPeer Find(string peerId) => CollabBridge.Find(peerId);

        /// <summary>
        /// Sets a key in the session's shared store (null removes it). The host keeps the store and every peer holds a
        /// copy, so a new host has it all: meant for scene state handed over when the host leaves. Anyone may call it;
        /// changes go through the host so everyone sees them in the same order.
        /// </summary>
        public static void SetShared(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) return;
            CollabBridge.SetSharedLocal(key, json);
            CollabBridge.Post(new CollabFrame { type = CollabFrameType.Shared, key = key, json = json ?? "", flag = json == null });
        }

        /// <summary>The value of a shared store key, or null.</summary>
        public static string GetShared(string key) => CollabBridge.GetShared(key);

        #region Raised by the bridge

        private static readonly Dictionary<string, List<Action<CollabPeer, string>>> _handlers = new Dictionary<string, List<Action<CollabPeer, string>>>();

        internal static void Deliver(string channel, CollabPeer from, string json)
        {
            if (channel == null || !_handlers.TryGetValue(channel, out var list)) return;
            foreach (var handler in list.ToArray())
            {
                try { handler(from, json); }
                catch (Exception e) { CollabLog.Exception($"A handler for channel '{channel}' threw", e); }
            }
        }

        internal static void RaisePeerJoined(CollabPeer peer) => Raise(PeerJoined, peer);
        internal static void RaisePeerLeft(CollabPeer peer) => Raise(PeerLeft, peer);
        internal static void RaisePeerChanged(CollabPeer peer) => Raise(PeerChanged, peer);
        internal static void RaiseHostChanged() => Raise(HostChanged);
        internal static void RaiseConnected() => Raise(Connected);
        internal static void RaiseDisconnected() => Raise(Disconnected);
        internal static void RaiseSharedChanged(string key) => Raise(SharedChanged, key);

        private static void Raise<T>(Action<T> action, T value)
        {
            if (action == null) return;
            foreach (Action<T> handler in action.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception e) { CollabLog.Exception("A collaboration event handler threw", e); }
            }
        }

        private static void Raise(Action action)
        {
            if (action == null) return;
            foreach (Action handler in action.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { CollabLog.Exception("A collaboration event handler threw", e); }
            }
        }

        #endregion
    }

    /// <summary>Where collaboration is at, for a status indicator (toolbar, team window).</summary>
    public enum CollabState { Disabled, Building, Starting, Searching, Alone, InSession, Error }

    public static class CollabStatus
    {
        public static CollabState State { get; private set; } = CollabState.Disabled;
        /// <summary>One line for people: "Hosting 3 people", "Building the helper...", the last error...</summary>
        public static string Message { get; private set; } = "Off";
        /// <summary>A colour for the state (green in a session, grey alone, yellow while starting, red on error).</summary>
        public static Color Color
        {
            get
            {
                switch (State)
                {
                    case CollabState.InSession: return new Color(0.3f, 0.8f, 0.4f);
                    case CollabState.Alone: return new Color(0.6f, 0.6f, 0.6f);
                    case CollabState.Error: return new Color(0.9f, 0.3f, 0.3f);
                    case CollabState.Disabled: return new Color(0.4f, 0.4f, 0.4f);
                    default: return new Color(0.95f, 0.75f, 0.2f);
                }
            }
        }
        public static event Action Changed;

        internal static void Set(CollabState state, string message)
        {
            if (state == State && message == Message) return;
            State = state;
            Message = message;
            try { Changed?.Invoke(); }
            catch (Exception e) { CollabLog.Exception("A CollabStatus.Changed handler threw", e); }
        }
    }
}
