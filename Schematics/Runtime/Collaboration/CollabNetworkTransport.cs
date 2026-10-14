using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// The connection a build uses: a <see cref="CollabRuntimeClient"/> (networking on background threads) whose events
    /// are handed to <see cref="CollabRuntime"/> on the main thread, once a frame.
    ///
    /// Command line options for a build (all optional):
    ///   -collab-off            don't connect
    ///   -collab-name NAME      the name editors see (default: the computer's user name)
    ///   -collab-relay H:P      use this relay instead of the one baked in / the local network
    ///   -collab-peer H:P       ask this editor's helper directly (repeatable; when UDP broadcast is blocked)
    ///   -collab-live-edits     receive editors' live edits from the start
    /// </summary>
    internal sealed class CollabNetworkTransport : ICollabTransport
    {
        private const string NameKey = "SchematicsCollab.Name";

        private readonly CollabRuntimeClient _client;
        private CollabStableId _identity;
        private readonly HashSet<string> _channels = new HashSet<string>();
        private readonly List<CollabRuntimePeer> _peers = new List<CollabRuntimePeer>();
        private Color _localColor = Color.white;

        private CollabNetworkTransport(CollabRuntimeClient client)
        {
            _client = client;
        }

        /// <summary>The build's transport, or null when this build doesn't collaborate (no build info, switched off, WebGL).</summary>
        public static CollabNetworkTransport TryCreate(CollabBuildInfo info)
        {
            // Every step says what it found in the log (Player.log in builds), so "the build doesn't connect" can be
            // read off the log instead of guessed.
            if (info == null) { CollabLog.Info("This build has no collaboration build info: it doesn't join editors."); return null; }
            if (!info.Enabled || string.IsNullOrEmpty(info.ProjectKey))
            {
                CollabLog.Info("Collaboration is switched off in this build (Project Settings > Schematics > Collaboration Features > Playtest builds).");
                return null;
            }
            var args = Environment.GetCommandLineArgs();
            if (HasArg(args, "-collab-off")) { CollabLog.Info("Collaboration is off for this run (-collab-off)."); return null; }
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                CollabLog.Info("Collaboration isn't available in WebGL builds (browsers don't allow the sockets it needs).");
                return null;
            }

            CollabStableId identity = null;
            try
            {
                identity = CollabStableId.Claim(System.IO.Path.Combine(Application.persistentDataPath, "SchematicsCollab"),
                    System.Diagnostics.Process.GetCurrentProcess().Id, IsAlive);
            }
            catch (Exception e) { CollabLog.Info("No stable peer id for this run (" + e.Message + "); using a fresh one."); }

            var options = new CollabRuntimeClientOptions
            {
                Id = identity?.Id ?? Guid.NewGuid().ToString("N"),
                Key = info.ProjectKey,
                Name = PlayerName(args),
                Role = CollabProtocol.RolePlaytester,
                Relay = ArgValue(args, "-collab-relay") ?? (string.IsNullOrWhiteSpace(info.Relay) ? null : info.Relay.Trim()),
                ToJson = JsonUtility.ToJson,
                FromJson = json => JsonUtility.FromJson<CollabFrame>(json),
                Log = OnClientLog,
            };
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-collab-peer" && !string.IsNullOrWhiteSpace(args[i + 1])) options.Seeds.Add(args[i + 1].Trim());
            if (HasArg(args, "-collab-live-edits")) CollabLiveEdits.Enabled = true;

            var client = new CollabRuntimeClient(options);
            try { client.Start(); }
            catch (Exception e)
            {
                CollabLog.Exception("The collaboration client couldn't start", e);
                return null;
            }
            CollabLog.Info($"Looking for the editors of {Describe(info)} as {options.Name}"
                           + (options.Relay != null ? $" through the relay {options.Relay}." : " on the local network (beacons on UDP " + CollabProtocol.DiscoveryPort + ", and asking)")
                           + $" Room key {info.ProjectKey}, built from {(string.IsNullOrEmpty(info.Commit) ? "?" : info.Commit)} by {info.BuiltBy} at {info.BuiltAt}; peer id {Short(options.Id)}.");
            return new CollabNetworkTransport(client) { _identity = identity };
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) || id.Length <= 12 ? id : id.Substring(0, 8) + "…" + id.Substring(id.Length - 2);

        private static bool IsAlive(int pid)
        {
            try { using (var p = System.Diagnostics.Process.GetProcessById(pid)) return !p.HasExited; }
            catch (ArgumentException) { return false; }   // no such process
            catch { return true; }                         // can't tell on this platform: treat the slot as taken
        }

        private static string Describe(CollabBuildInfo info) =>
            string.IsNullOrEmpty(info.Repository) ? "this project" : $"{info.Repository} ({info.Branch})";

        private static void OnClientLog(string level, string text)
        {
            // Called from network threads; Debug.Log* are thread-safe.
            switch (level)
            {
                case "error": CollabLog.Error(text); break;
                case "warning": CollabLog.Warning(text); break;
                default: CollabLog.Info(text); break;
            }
        }

        private static string PlayerName(string[] args)
        {
            string name = ArgValue(args, "-collab-name");
            if (string.IsNullOrWhiteSpace(name))
                try { name = PlayerPrefs.GetString(NameKey, ""); } catch { name = ""; }
            if (string.IsNullOrWhiteSpace(name))
                try { name = Environment.UserName; } catch { name = ""; }
            if (string.IsNullOrWhiteSpace(name))
                try { name = SystemInfo.deviceName; } catch { name = ""; }
            return string.IsNullOrWhiteSpace(name) ? "Playtester" : name.Trim();
        }

        private static bool HasArg(string[] args, string name) => Array.IndexOf(args, name) >= 0;

        private static string ArgValue(string[] args, string name)
        {
            int i = Array.LastIndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        #region ICollabTransport

        public bool IsConnected => _client.IsConnected;
        public CollabRole LocalRole => CollabRole.Playtester;
        public string LocalPeerId => _client.Id;
        public string LocalName => _client.Options.Name;
        public Color LocalColor => _localColor;
        public IReadOnlyList<CollabRuntimePeer> Peers => _peers;
        public bool IsBuild => true;

        public void Send(string channel, string json) => _client.Send(channel, json);
        public void Listen(string channel) => _channels.Add(channel);
        public void SetShared(string key, string json) => _client.SetShared(key, json);
        public string GetShared(string key) => _client.GetShared(key);
        public void SetPresence(string key, string json) => _client.SetPresence(key, json);
        public void Shutdown()
        {
            _client.Stop();
            _identity?.Dispose();
            _identity = null;
        }

        public void Update()
        {
            _client.Poll(e =>
            {
                switch (e.Kind)
                {
                    case CollabRuntimeClient.EventKind.Connected:
                        CollabRuntime.RaiseConnected();
                        break;
                    case CollabRuntimeClient.EventKind.Disconnected:
                        CollabRuntime.RaiseDisconnected();
                        break;
                    case CollabRuntimeClient.EventKind.PeersChanged:
                        RebuildPeers();
                        CollabRuntime.RaisePeersChanged();
                        break;
                    case CollabRuntimeClient.EventKind.SharedChanged:
                        CollabRuntime.RaiseSharedChanged(e.Key);
                        break;
                    case CollabRuntimeClient.EventKind.Message:
                        if (_channels.Contains(e.Channel)) CollabRuntime.Deliver(e.Channel, e.From, e.Json);
                        break;
                }
            }, 2000);
        }

        private void RebuildPeers()
        {
            _peers.Clear();
            foreach (var info in _client.Peers)
            {
                if (info == null || string.IsNullOrEmpty(info.id)) continue;
                var peer = ToPeer(info, info.id == _client.Id);
                if (peer.IsLocal) _localColor = peer.Color;
                _peers.Add(peer);
            }
        }

        /// <summary>A roster entry as the game sees it.</summary>
        internal static CollabRuntimePeer ToPeer(CollabPeerInfo info, bool isLocal)
        {
            if (string.IsNullOrEmpty(info.color) || !ColorUtility.TryParseHtmlString(info.color, out var color)) color = Color.white;
            return new CollabRuntimePeer
            {
                Id = info.id,
                Name = string.IsNullOrEmpty(info.name) ? "?" : info.name,
                Color = color,
                Role = info.role == CollabProtocol.RolePlaytester ? CollabRole.Playtester : CollabRole.Editor,
                IsBuild = info.client == CollabProtocol.ClientBuild,
                IsHost = info.isHost,
                IsLocal = isLocal,
                Presence = Presence(info.presence),
            };
        }

        private static Dictionary<string, string> Presence(CollabKV[] entries)
        {
            var map = new Dictionary<string, string>();
            if (entries == null) return map;
            foreach (var kv in entries)
                if (kv != null && !string.IsNullOrEmpty(kv.key)) map[kv.key] = kv.value ?? "";
            return map;
        }

        #endregion
    }
}
