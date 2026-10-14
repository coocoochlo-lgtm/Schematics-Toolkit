using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>What someone in the room is doing: editing in a Unity editor, or playing the game (a build, or an editor in Play Mode).</summary>
    public enum CollabRole { Editor, Playtester }

    /// <summary>Someone in the room, as the game sees them.</summary>
    public sealed class CollabRuntimePeer
    {
        public string Id;
        public string Name;
        public Color Color;
        public CollabRole Role;
        /// <summary>True for a build (a playtest client); false for a Unity editor (in Edit or Play Mode).</summary>
        public bool IsBuild;
        /// <summary>The peer editors see as host (it keeps the shared store). Never a build.</summary>
        public bool IsHost;
        public bool IsLocal;
        /// <summary>Their replicated presence (camera, location, control...), as JSON per key. Never null.</summary>
        public IReadOnlyDictionary<string, string> Presence = new Dictionary<string, string>();
        public override string ToString() => $"{Name} ({Role}{(IsBuild ? ", build" : "")})";
    }

    /// <summary>
    /// The collaboration session from inside the game: in a build, and in the editor's Play Mode.
    ///
    /// The repository is the room. A build carries its project key (repository + branch + project folder, baked in at
    /// build time), finds the editors working on that repo and branch on the local network (or through the relay the
    /// builder uses) and joins them as a playtester: no setup, no addresses to type. Messages sent here arrive in
    /// editors through CollabSession.Subscribe on the same channel, and the other way round; shared keys go through the
    /// room's host like the editors' own.
    ///
    /// In the editor this doesn't open a second connection: it goes through the editor's own session (Play Mode makes
    /// that editor a playtester). Everything is raised on the main thread, and a handler that throws is logged
    /// (see <see cref="CollabLog"/>), never passed back into the game loop.
    /// </summary>
    public static class CollabRuntime
    {
        private static ICollabTransport _transport;
        private static readonly Dictionary<string, List<Action<string, string>>> _handlers = new Dictionary<string, List<Action<string, string>>>();
        private static readonly IReadOnlyList<CollabRuntimePeer> NoPeers = new CollabRuntimePeer[0];

        /// <summary>In the room right now (messages sent while not connected are held and sent on joining).</summary>
        public static bool IsConnected => _transport != null && SafeGet(() => _transport.IsConnected, false);

        /// <summary>Playtester in builds and in Play Mode; Editor when used from editor code in Edit Mode.</summary>
        public static CollabRole LocalRole => _transport != null ? SafeGet(() => _transport.LocalRole, CollabRole.Playtester) : CollabRole.Playtester;

        public static string LocalPeerId => _transport != null ? SafeGet(() => _transport.LocalPeerId, null) : null;
        public static string LocalName => _transport != null ? SafeGet(() => _transport.LocalName, "") : "";
        public static Color LocalColor => _transport != null ? SafeGet(() => _transport.LocalColor, Color.white) : Color.white;

        /// <summary>Everyone in the room, this game included.</summary>
        public static IReadOnlyList<CollabRuntimePeer> Peers => _transport != null ? SafeGet(() => _transport.Peers, NoPeers) ?? NoPeers : NoPeers;

        /// <summary>The build's collaboration info (null in the editor and in builds made without it).</summary>
        public static CollabBuildInfo BuildInfo { get; private set; }

        /// <summary>True when collaboration is available in this run at all (a build made with it, or the editor).</summary>
        public static bool IsAvailable => _transport != null;

        public static event Action Connected;
        public static event Action Disconnected;
        /// <summary>A shared store key changed (by anyone, this game included).</summary>
        public static event Action<string> SharedChanged;
        /// <summary>Someone joined or left, or a name, colour or role changed.</summary>
        public static event Action PeersChanged;

        /// <summary>To everyone in the room (editors and other playtesters). Reliable and in order per channel.</summary>
        public static void Send(string channel, string json)
        {
            if (string.IsNullOrEmpty(channel) || _transport == null) return;
            if (channel.StartsWith(CollabProtocol.SystemChannelPrefix, StringComparison.Ordinal))
            {
                CollabLog.Warning($"Channels starting with {CollabProtocol.SystemChannelPrefix} are reserved; '{channel}' wasn't sent.");
                return;
            }
            CollabLog.Safe("Sending on '" + channel + "'", () => _transport.Send(channel, json ?? ""));
        }

        /// <summary>Calls <paramref name="handler"/> with the sender's peer id and the JSON for every message on the channel.</summary>
        public static void Subscribe(string channel, Action<string, string> handler)
        {
            if (string.IsNullOrEmpty(channel) || handler == null) return;
            if (!_handlers.TryGetValue(channel, out var list))
            {
                _handlers[channel] = list = new List<Action<string, string>>();
                if (_transport != null) CollabLog.Safe("Listening on '" + channel + "'", () => _transport.Listen(channel));
            }
            if (!list.Contains(handler)) list.Add(handler);
        }

        public static void Unsubscribe(string channel, Action<string, string> handler)
        {
            if (channel != null && _handlers.TryGetValue(channel, out var list)) list.Remove(handler);
        }

        /// <summary>Sets (or with null removes) a key of the room's shared store. It goes through the host, like editors' keys.</summary>
        public static void SetShared(string key, string json)
        {
            if (string.IsNullOrEmpty(key) || _transport == null) return;
            CollabLog.Safe("Setting shared key '" + key + "'", () => _transport.SetShared(key, json));
        }

        /// <summary>Channel the player ghosts travel on (<see cref="CollabGhostFrame"/>).</summary>
        public const string GhostChannel = "ghost";

        /// <summary>Chat channel shared with the editors' Team window.</summary>
        public const string ChatChannel = "chat";

        /// <summary>
        /// Marks an object as controlled by this game's player, so editors and other players see it as a ghost. Objects
        /// driven by a Remedy Input Behavior are found by themselves; this is for anything else (or to stop sharing one).
        /// </summary>
        public static void SetControlled(GameObject go, bool controlled) => CollabRuntimeGhosts.SetControlled(go, controlled);

        /// <summary>The value of a shared store key, or null.</summary>
        public static string GetShared(string key)
        {
            if (string.IsNullOrEmpty(key) || _transport == null) return null;
            return SafeGet(() => _transport.GetShared(key), null);
        }

        /// <summary>Sets (or with null removes) one key of this game's presence: what editors see about it (camera, location).</summary>
        public static void SetPresence(string key, string json)
        {
            if (string.IsNullOrEmpty(key) || _transport == null) return;
            CollabLog.Safe("Setting presence '" + key + "'", () => _transport.SetPresence(key, json));
        }

        /// <summary>
        /// The id editors use for a scene object ("g:" + its GlobalObjectId), so game code can talk about objects with
        /// editors (sticky notes, comments...). Works in builds (ids baked at build time, see <see cref="CollabSceneIds"/>)
        /// and in the editor's Play Mode. Null for objects created at runtime or unknown to the editors.
        /// </summary>
        public static string GetObjectId(GameObject go)
        {
            if (go == null) return null;
            var id = CollabSceneIds.IdOf(go);
            if (id != null) return id;
            var resolver = EditorObjectIdProvider;
            if (resolver == null) return null;
            try { return resolver(go); }
            catch (Exception e)
            {
                CollabLog.Exception("Finding an object's collaboration id failed", e);
                return null;
            }
        }

        /// <summary>The live object an editor id names (builds: baked ids; Play Mode: the editor's lookup), or null.</summary>
        public static GameObject FindObject(string id) => CollabLiveEdits.Resolve(id);

        public static CollabRuntimePeer FindPeer(string peerId)
        {
            if (string.IsNullOrEmpty(peerId)) return null;
            foreach (var peer in Peers)
                if (peer.Id == peerId) return peer;
            return null;
        }

        #region Transport (internal)

        /// <summary>
        /// Installed by the editor (an [InitializeOnLoad] class in the editor bridge) so the game in Play Mode, and editor
        /// code, use the editor's own session instead of opening a second connection.
        /// </summary>
        internal static ICollabTransport EditorTransport
        {
            get => _editorTransport;
            set
            {
                _editorTransport = value;
                if (Application.isEditor) Use(value);
            }
        }
        private static ICollabTransport _editorTransport;

        /// <summary>
        /// In the editor's Play Mode: finds the play-mode copy of an object by its editor id (the editor installs it).
        /// Builds use the ids baked into each scene instead (<see cref="CollabSceneIds"/>).
        /// </summary>
        internal static Func<string, GameObject> EditorObjectResolver;

        /// <summary>The reverse of <see cref="EditorObjectResolver"/>: an object's editor id (installed by the editor).</summary>
        internal static Func<GameObject, string> EditorObjectIdProvider;

        internal static ICollabTransport Transport => _transport;

        internal static void Use(ICollabTransport transport)
        {
            if (_transport == transport) return;
            var old = _transport;
            _transport = transport;
            if (old != null && old != _editorTransport) CollabLog.Safe("Stopping the collaboration client", old.Shutdown);
            if (transport == null) return;
            foreach (var channel in _handlers.Keys) CollabLog.Safe("Listening on '" + channel + "'", () => transport.Listen(channel));
        }

        internal static void Deliver(string channel, string fromPeerId, string json)
        {
            if (channel == null || !_handlers.TryGetValue(channel, out var list)) return;
            foreach (var handler in list.ToArray())
            {
                try { handler(fromPeerId, json); }
                catch (Exception e) { CollabLog.Exception($"A handler for channel '{channel}' threw", e); }
            }
        }

        internal static void RaiseConnected() => Raise(Connected, "Connected");
        internal static void RaiseDisconnected() => Raise(Disconnected, "Disconnected");
        internal static void RaisePeersChanged() => Raise(PeersChanged, "PeersChanged");

        internal static void RaiseSharedChanged(string key)
        {
            if (SharedChanged == null) return;
            foreach (Action<string> handler in SharedChanged.GetInvocationList())
            {
                try { handler(key); }
                catch (Exception e) { CollabLog.Exception("A CollabRuntime.SharedChanged handler threw", e); }
            }
        }

        private static void Raise(Action action, string name)
        {
            if (action == null) return;
            foreach (Action handler in action.GetInvocationList())
            {
                try { handler(); }
                catch (Exception e) { CollabLog.Exception($"A CollabRuntime.{name} handler threw", e); }
            }
        }

        private static T SafeGet<T>(Func<T> get, T fallback)
        {
            try { return get(); }
            catch (Exception e)
            {
                CollabLog.Exception("Reading the collaboration state failed", e);
                return fallback;
            }
        }

        #endregion

        #region Startup

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            try
            {
                if (Application.isEditor)
                {
                    // Play Mode: the editor's session is the connection (installed by the editor bridge).
                    if (_editorTransport != null) Use(_editorTransport);
                }
                else
                {
                    if (_transport == null)
                    {
                        var info = CollabBuildInfo.Load();
                        if (info == null)
                        {
                            // Maybe the scenes carry it (CollabSceneIds.BuildInfoJson): they boot us when they load.
                            CollabLog.Info("No collaboration build info in Resources (" + CollabBuildInfo.ResourceName + "); checking the first scene for it.");
                            return;
                        }
                        StartBuildClient(info);
                    }
                }
                if (_transport == null) return;
                CollabLiveEdits.Init();
                CollabRuntimeRunner.Ensure();
            }
            catch (Exception e)
            {
                CollabLog.Exception("Starting collaboration in the game failed (the game itself is unaffected)", e);
            }
        }

        private static bool _sceneBootTried;

        /// <summary>Fallback start from the build info a built scene carries (see <see cref="CollabSceneIds.BuildInfoJson"/>).</summary>
        internal static void BootFromScene(string json)
        {
            if (_sceneBootTried || _transport != null || Application.isEditor) return;
            _sceneBootTried = true;
            try
            {
                var info = CollabBuildInfo.FromJson(json);
                if (info == null) return;
                CollabLog.Info("Using the collaboration build info stored in the scene.");
                StartBuildClient(info);
                if (_transport == null) return;
                CollabLiveEdits.Init();
                CollabRuntimeRunner.Ensure();
            }
            catch (Exception e)
            {
                CollabLog.Exception("Starting collaboration in the game failed (the game itself is unaffected)", e);
            }
        }

        private static void StartBuildClient(CollabBuildInfo info)
        {
            BuildInfo = info;
            var transport = CollabNetworkTransport.TryCreate(info);
            if (transport != null) Use(transport);
        }

        #endregion
    }

    /// <summary>
    /// Where <see cref="CollabRuntime"/> sends and gets things: the network client in builds, the editor's session in
    /// the editor. Implementations raise events through CollabRuntime's internal Raise/Deliver methods on the main thread.
    /// </summary>
    internal interface ICollabTransport
    {
        bool IsConnected { get; }
        CollabRole LocalRole { get; }
        string LocalPeerId { get; }
        string LocalName { get; }
        Color LocalColor { get; }
        IReadOnlyList<CollabRuntimePeer> Peers { get; }
        /// <summary>True for the network client inside a build (it publishes the game camera as presence).</summary>
        bool IsBuild { get; }
        void Send(string channel, string json);
        /// <summary>Start passing this channel's messages to <see cref="CollabRuntime.Deliver"/>.</summary>
        void Listen(string channel);
        void SetShared(string key, string json);
        string GetShared(string key);
        void SetPresence(string key, string json);
        /// <summary>Called every frame on the main thread while the game runs.</summary>
        void Update();
        void Shutdown();
    }
}
