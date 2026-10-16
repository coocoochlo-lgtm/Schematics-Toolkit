using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Lets <see cref="CollabRuntime"/> (game code, sticky notes...) work in the editor through the editor's own
    /// <see cref="CollabSession"/>: in Play Mode the game doesn't open a second connection, it shares this editor's (and
    /// the editor shows as a playtester meanwhile). A message sent with CollabRuntime.Send reaches the same channel's
    /// CollabSession subscribers everywhere, and the other way round.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class CollabRuntimeEditorTransport : ICollabTransport
    {
        private readonly HashSet<string> _listening = new HashSet<string>();
        private readonly List<CollabRuntimePeer> _peers = new List<CollabRuntimePeer>();
        private bool _peersDirty = true;

        static CollabRuntimeEditorTransport()
        {
            var transport = new CollabRuntimeEditorTransport();
            CollabSession.Connected += () => { transport._peersDirty = true; CollabRuntime.RaiseConnected(); };
            CollabSession.Disconnected += () => { transport._peersDirty = true; CollabRuntime.RaiseDisconnected(); };
            CollabSession.PeerJoined += _ => transport.PeersChanged();
            CollabSession.PeerLeft += _ => transport.PeersChanged();
            CollabSession.PeerChanged += _ => transport.PeersChanged();
            CollabSession.HostChanged += transport.PeersChanged;
            CollabSession.SharedChanged += CollabRuntime.RaiseSharedChanged;
            EditorApplication.playModeStateChanged += _ => transport.PeersChanged();
            CollabRuntime.EditorTransport = transport;
        }

        private void PeersChanged()
        {
            _peersDirty = true;
            CollabRuntime.RaisePeersChanged();
        }

        public bool IsConnected => CollabSession.IsConnected;
        public CollabRole LocalRole => EditorApplication.isPlayingOrWillChangePlaymode ? CollabRole.Playtester : CollabRole.Editor;
        public string LocalPeerId => CollabSession.Local?.Id;
        public string LocalName => CollabSession.Local?.Name ?? "";
        public Color LocalColor => CollabSession.Local?.Color ?? Color.white;
        public bool IsBuild => false;

        public IReadOnlyList<CollabRuntimePeer> Peers
        {
            get
            {
                if (!_peersDirty) return _peers;
                _peersDirty = false;
                _peers.Clear();
                foreach (var peer in CollabSession.Peers)
                {
                    if (peer == null) continue;
                    _peers.Add(new CollabRuntimePeer
                    {
                        Id = peer.Id,
                        Name = peer.Name,
                        Color = peer.Color,
                        Role = peer.IsLocal ? LocalRole : peer.Role,
                        IsBuild = peer.IsBuild,
                        IsHost = peer.IsHost,
                        IsLocal = peer.IsLocal,
                        Presence = peer.Presence ?? new Dictionary<string, string>(),
                    });
                }
                return _peers;
            }
        }

        public void Send(string channel, string json) => CollabSession.Send(channel, json);

        public void Listen(string channel)
        {
            if (!_listening.Add(channel)) return;
            CollabSession.Subscribe(channel, (peer, json) => CollabRuntime.Deliver(channel, peer?.Id, json));
        }

        public void SetShared(string key, string json) => CollabSession.SetShared(key, json);
        public string GetShared(string key) => CollabSession.GetShared(key);
        public void SetPresence(string key, string json) => CollabSession.SetPresence(key, json);
        public void Update() { }
        public void Shutdown() { }
    }
}
