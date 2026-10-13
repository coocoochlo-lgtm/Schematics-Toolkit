using System;

// The JSON shapes below are filled in by the deserializer, which the compiler can't see.
#pragma warning disable 0649

// This file is compiled twice: by Unity (the editor bridge) and by the SchematicsCollab helper in Collab~ (linked from
// its .csproj). Keep it free of UnityEngine/UnityEditor and of newer C# features so both compilers accept it. Because
// both sides share it word for word, a helper built from older sources reports an older Version and gets rebuilt.

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// The wire protocol between the editor and its helper, and between helpers: every frame is a 4-byte big-endian
    /// length followed by that many bytes of UTF-8 JSON (one <see cref="CollabFrame"/>). UDP discovery beacons and pings
    /// are one JSON frame per datagram, without the length.
    /// </summary>
    internal static class CollabProtocol
    {
        /// <summary>Bumped whenever the protocol or the helper's behaviour changes, so stale helpers get rebuilt.</summary>
        public const int Version = 1;

        /// <summary>UDP port the LAN beacons go to (broadcast and multicast).</summary>
        public const int DiscoveryPort = 47811;
        public const string MulticastGroup = "239.255.71.11";
        public const int MaxFrameBytes = 64 * 1024 * 1024;

        /// <summary>Channels starting with this are the helper's own (asset announcements); never shown to tools.</summary>
        public const string SystemChannelPrefix = "__collab/";
        public const string AssetChannel = "__collab/asset";

        /// <summary>Marks commits the helper made, so it only ever pushes its own commits automatically.</summary>
        public const string CommitTrailer = "Schematics-Collab: auto";

        /// <summary>Session colours, handed out by the host so each participant has a different one.</summary>
        public static readonly string[] Palette =
        {
            "#4C9AFF", "#FF5630", "#36B37E", "#FFAB00", "#6554C0", "#00B8D9",
            "#FF7AB6", "#8BC34A", "#FF8B00", "#B388FF", "#00C7A5", "#E2B203",
        };
    }

    /// <summary>Frame type names (the <see cref="CollabFrame.type"/> field).</summary>
    internal static class CollabFrameType
    {
        // Editor <-> helper
        public const string Hello = "hello";             // editor: seq = last seq seen, id = helper instance seen. helper: id = instance, seq = newest, flag = resync
        public const string State = "state";             // helper: flag = in a session, id = local peer id, host = host id, text = role, peers
        public const string SharedAll = "sharedAll";     // helper: entries = whole shared store
        public const string Git = "git";                 // helper: git = repository info
        public const string PendingAll = "pendingAll";   // helper: entries = guid -> path of announced assets not pulled yet
        public const string Announced = "announced";     // helper: key = guid, text = path, bounds, from
        public const string Resolved = "resolved";       // helper: key = guid (pulled now)
        public const string PullBegin = "pullBegin";     // helper: id. The editor stops auto refresh and answers PullReady
        public const string PullReady = "pullReady";     // editor: id
        public const string PullEnd = "pullEnd";         // helper: id, paths (project-relative), text = error
        public const string Log = "log";                 // helper: text, flag = error, key = "warning" for warnings
        public const string Send = "send";               // editor: channel, json, to
        public const string Profile = "profile";         // editor: profile
        public const string Settings = "settings";       // editor: entries (relay, gitPoll, autoPull, autoPush)
        public const string LiveScenes = "liveScenes";   // editor: paths (project-relative scene paths open in the editor)
        public const string PullNow = "pullNow";         // editor
        public const string Announce = "announce";       // editor: key = guid, text = asset path, paths = asset + dependencies, bounds
        public const string CommitScene = "commitScene"; // editor: text = scene path, json = summary, coAuthors
        public const string Shutdown = "shutdown";       // editor

        // Helper <-> helper (and both directions of the editor link for these)
        public const string Join = "join";               // peer->host: id, key, profile, presence, port, seq = startedAt, epoch, entries (store to merge)
        public const string Welcome = "welcome";         // host->peer: id = host id, epoch, entries, peers
        public const string NotHost = "notHost";         // any->peer: host = the host it knows, text = address, port
        public const string Roster = "roster";           // host->peers: peers, epoch, host
        public const string Msg = "msg";                 // channel, json, from, to
        public const string Presence = "presence";       // presence (all of a peer's entries), profile; editor->helper: key, json
        public const string Shared = "shared";           // key, json, flag = removed
        public const string Heartbeat = "hb";            // seq = sender's clock (ticks), entries = sender's ping table
        public const string HeartbeatAck = "hbAck";      // seq echoed
        public const string Leave = "leave";
        public const string Redirect = "redirect";       // host->peers: text = address, port of the host to join instead
        public const string Beacon = "beacon";           // UDP: key, id, flag = is host, host, epoch, seq = startedAt, port, count
        public const string Ping = "ping";               // UDP: id, seq = ticks
        public const string Pong = "pong";               // UDP: id, seq echoed
    }

    /// <summary>One message. A single flat shape for everything keeps it readable by Unity's JsonUtility.</summary>
    [Serializable]
    internal sealed class CollabFrame
    {
        public string type;
        public long seq;
        public string id;
        public string key;
        public string host;
        public string from;
        public string to;
        public string channel;
        public string json;
        public string text;
        public bool flag;
        public long epoch;
        public int port;
        public int count;
        public string[] paths;
        public float[] bounds;                 // centre xyz, size xyz
        public CollabPeerInfo[] peers;
        public CollabKV[] entries;
        public CollabKV[] presence;
        public CollabProfile profile;
        public CollabGitInfo git;
        public CollabCoAuthor[] coAuthors;
    }

    [Serializable]
    internal sealed class CollabKV
    {
        public string key;
        public string value;
    }

    [Serializable]
    internal sealed class CollabPeerInfo
    {
        public string id;
        public string name;
        public string avatarId;
        public string color;
        public int colorIndex;
        public string gitName;
        public string gitEmail;
        public string address;
        public int port;
        public bool isHost;
        public int pingMs;          // from the receiving peer's point of view (filled in by its helper)
        public int avgPingMs;       // average round trip to everyone else; the best one takes over as host
        public long startedAt;
        public CollabKV[] presence;
    }

    [Serializable]
    internal sealed class CollabProfile
    {
        public string name;
        public string avatarId;
        public string gitName;
        public string gitEmail;
    }

    [Serializable]
    internal sealed class CollabGitInfo
    {
        public bool hasRepo;
        public string root;
        public string prefix;       // the Unity project's folder inside the repository ("" at the root)
        public string branch;
        public string remote;
        public string remoteUrl;
        public string upstream;
        public string head;
        public string userName;
        public string userEmail;
    }

    [Serializable]
    internal sealed class CollabCoAuthor
    {
        public string name;
        public string email;
    }

    /// <summary>What the helper writes to Library/SchematicsCollab/helper.json once it listens.</summary>
    [Serializable]
    internal sealed class CollabHelperState
    {
        public int pid;
        public int controlPort;
        public int peerPort;
        public int version;
        public string instance;
    }
}
