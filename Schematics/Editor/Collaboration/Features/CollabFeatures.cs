using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Shared plumbing for the collaboration features: whether they should run right now, presence parsing, colours and
    /// small helpers. Every feature asks <see cref="IsActive"/> first, so nothing happens while disconnected or in Play
    /// Mode (scene objects in Play Mode are throwaway copies; syncing them would corrupt everyone's scene).
    /// </summary>
    [InitializeOnLoad]
    public static class CollabFeatures
    {
        /// <summary>Raised when collaboration pauses (Play Mode, disconnect) so features can undo temporary state (picking locks, overlays).</summary>
        public static event Action Paused;
        /// <summary>Raised when collaboration resumes (connected and back in Edit Mode).</summary>
        public static event Action Resumed;

        private static bool _wasActive;

        static CollabFeatures()
        {
            EditorApplication.playModeStateChanged += _ => CheckActive();
            CollabSession.Connected += CheckActive;
            CollabSession.Disconnected += CheckActive;
            EditorApplication.update += CheckActive;
        }

        /// <summary>True while connected and in Edit Mode: the only time features send or apply anything.</summary>
        public static bool IsActive => CollabSession.IsConnected && !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>True when a peer is someone else (messages may or may not be echoed back to their sender).</summary>
        public static bool IsRemote(CollabPeer peer)
        {
            if (peer == null) return false;
            if (peer.IsLocal) return false;
            var local = CollabSession.Local;
            return local == null || peer.Id != local.Id;
        }

        private static void CheckActive()
        {
            bool active = IsActive;
            if (active == _wasActive) return;
            _wasActive = active;
            try { (active ? Resumed : Paused)?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Unix time in milliseconds.</summary>
        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>The local peer id, or "local" before connecting.</summary>
        public static string LocalId => CollabSession.Local?.Id ?? "local";

        private static int _counter;
        /// <summary>A message id unique to this peer and editor session.</summary>
        public static string NewMessageId() => LocalId + ":" + DateTime.UtcNow.Ticks.ToString("x") + ":" + (++_counter);

        #region Presence

        private static readonly Dictionary<(string peer, string key), (string raw, object parsed)> _presenceCache = new();

        /// <summary>A peer's presence value parsed as T (cached until the raw JSON changes), or null.</summary>
        public static T GetPresence<T>(CollabPeer peer, string key) where T : class
        {
            if (peer?.Presence == null || !peer.Presence.TryGetValue(key, out var raw) || string.IsNullOrEmpty(raw))
                return null;
            var cacheKey = (peer.Id, key);
            if (_presenceCache.TryGetValue(cacheKey, out var cached) && cached.raw == raw)
                return cached.parsed as T;
            T parsed = null;
            try { parsed = JsonUtility.FromJson<T>(raw); }
            catch (Exception) { /* a peer on another version: ignore */ }
            _presenceCache[cacheKey] = (raw, parsed);
            return parsed;
        }

        private static readonly Dictionary<string, string> _lastPresence = new();

        /// <summary>Sets a presence value only when it changed (presence is replicated to everyone, so don't spam it).</summary>
        public static void PublishPresence(string key, object value)
        {
            if (!CollabSession.IsConnected) return;
            string json = value == null ? "" : JsonUtility.ToJson(value);
            if (_lastPresence.TryGetValue(key, out var last) && last == json) return;
            _lastPresence[key] = json;
            CollabSession.SetPresence(key, json);
        }

        /// <summary>Forgets what was published so everything is re-sent (after reconnecting).</summary>
        public static void ResetPublishedPresence() => _lastPresence.Clear();

        /// <summary>Remote peers (not the local one).</summary>
        public static IEnumerable<CollabPeer> RemotePeers()
        {
            var peers = CollabSession.Peers;
            if (peers == null) yield break;
            foreach (var peer in peers)
                if (IsRemote(peer))
                    yield return peer;
        }

        #endregion

        #region Helpers

        /// <summary>"#RRGGBB" for a colour.</summary>
        public static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color);

        /// <summary>Parses "#RRGGBB", falling back to white.</summary>
        public static Color FromHex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        /// <summary>Shows a short message in the last active Scene view (and logs nothing).</summary>
        public static void Notify(string message, double seconds = 1.5)
        {
            var view = SceneView.lastActiveSceneView;
            if (view != null) view.ShowNotification(new GUIContent(message), seconds);
        }

        /// <summary>Runs an action, logging (not throwing) any exception: callbacks from the session must never break it.</summary>
        public static void Safe(Action action)
        {
            try { action(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        #endregion
    }
}
