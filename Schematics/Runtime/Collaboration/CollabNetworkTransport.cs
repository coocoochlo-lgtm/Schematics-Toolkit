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
        private readonly HashSet<string> _channels = new HashSet<string>();
        private readonly List<CollabRuntimePeer> _peers = new List<CollabRuntimePeer>();
        private Color _localColor = Color.white;

        private CollabNetworkTransport(CollabRuntimeClient client)
        {
            _client = client;
        }

        /// <summary>The build's transport, or null when this build doesn't collaborate (no build info, switched off, WebGL).</summary>
        public static CollabNetworkTransport TryCreate(out CollabBuildInfo info)
        {
            info = CollabBuildInfo.Load();
            if (info == null || !info.Enabled || string.IsNullOrEmpty(info.ProjectKey)) return null;
            var args = Environment.GetCommandLineArgs();
            if (HasArg(args, "-collab-off")) return null;
            _verbose = Debug.isDebugBuild;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                CollabLog.Info("Collaboration isn't available in WebGL builds (browsers don't allow the sockets it needs).");
                return null;
            }

            var options = new CollabRuntimeClientOptions
            {
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
                           + (options.Relay != null ? $" through the relay {options.Relay}." : " on the local network."));
            return new CollabNetworkTransport(client);
        }

        // Connection chatter (joined, left) only in development builds; read once on the main thread.
        private static bool _verbose;

        private static string Describe(CollabBuildInfo info) =>
            string.IsNullOrEmpty(info.Repository) ? "this project" : $"{info.Repository} ({info.Branch})";

        private static void OnClientLog(string level, string text)
        {
            // Called from network threads; Debug.Log* are thread-safe.
            switch (level)
            {
                case "error": CollabLog.Error(text); break;
                case "warning": CollabLog.Warning(text); break;
                default: if (_verbose) CollabLog.Info(text); break;
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
        public void Shutdown() => _client.Stop();

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
            };
        }

        #endregion
    }
}
