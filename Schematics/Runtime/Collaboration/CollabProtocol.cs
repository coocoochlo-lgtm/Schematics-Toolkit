using System;
using System.Security.Cryptography;
using System.Text;

// The JSON shapes below are filled in by the deserializer, which the compiler can't see.
#pragma warning disable 0649

// This file is the one source of truth for the wire protocol. It's compiled three times: by Unity into the runtime
// assembly Remedy.Schematics.Collaboration.Runtime (used by the editor bridge and by the playtest client inside builds),
// and by the SchematicsCollab helper in Collab~ (linked from its .csproj). Keep it free of UnityEngine/UnityEditor and
// of newer C#/.NET features (C# 9, .NET Standard 2.1) so every compiler accepts it. Because all sides share it word for
// word, a helper built from older sources reports an older Version and gets rebuilt.

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
        public const int Version = 3;

        /// <summary>UDP port the LAN beacons go to (broadcast and multicast).</summary>
        public const int DiscoveryPort = 47811;
        public const string MulticastGroup = "239.255.71.11";
        public const int MaxFrameBytes = 64 * 1024 * 1024;

        /// <summary>Channels starting with this are the helper's own (asset announcements); never shown to tools.</summary>
        public const string SystemChannelPrefix = "__collab/";
        public const string AssetChannel = "__collab/asset";

        /// <summary>Marks commits the helper made, so it only ever pushes its own commits automatically.</summary>
        public const string CommitTrailer = "Schematics-Collab: auto";

        /// <summary>Peer roles (<see cref="CollabProfile.role"/>): editing in a Unity editor, or playing the game.</summary>
        public const string RoleEditor = "editor";
        public const string RolePlaytester = "playtester";

        /// <summary>What runs a peer (<see cref="CollabProfile.client"/>): an editor's helper, or the client inside a build.</summary>
        public const string ClientEditor = "editor";
        public const string ClientBuild = "build";

        /// <summary>
        /// The room a project lives in. Peers only see each other when this matches. With git: the normalized remote URL
        /// + branch + the project's folder inside the repository, so every clone of the repo on that branch (and every
        /// build made from it) meets in one room. Without git: the product name + Unity version file + a project id file
        /// created once in Collab~. The helper, the editor (at build time) and builds all use this one function.
        /// </summary>
        public static string ProjectKey(bool hasRepo, string remoteUrl, string branch, string prefix,
            string productName, string unityVersionFile, string projectId)
        {
            string source;
            if (hasRepo && !string.IsNullOrEmpty(remoteUrl))
                source = "git|" + NormalizeUrl(remoteUrl) + "|" + branch + "|" + prefix;
            else
                source = "local|" + productName + "|" + unityVersionFile + "|" + projectId + "|" + (hasRepo ? branch : "");
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(source));
            var hex = new StringBuilder(16);
            for (int i = 0; i < 8; i++) hex.Append(hash[i].ToString("x2"));
            return hex.ToString();
        }

        /// <summary>A remote URL reduced to host/path, so https, ssh and scp-style URLs of one repository compare equal.</summary>
        public static string NormalizeUrl(string url)
        {
            string u = (url ?? "").Trim().ToLowerInvariant();
            int scheme = u.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) u = u.Substring(scheme + 3);
            int at = u.IndexOf('@');
            if (at >= 0 && (u.IndexOf('/') < 0 || at < u.IndexOf('/'))) u = u.Substring(at + 1);   // credentials / git@
            // scp-like host:path -> host/path (but keep host:port/path)
            int colon = u.IndexOf(':');
            if (colon > 0 && (u.IndexOf('/') < 0 || colon < u.IndexOf('/')))
            {
                string after = u.Substring(colon + 1);
                int slash = after.IndexOf('/');
                string maybePort = slash >= 0 ? after.Substring(0, slash) : after;
                bool digits = maybePort.Length > 0;
                foreach (char c in maybePort) if (!char.IsDigit(c)) digits = false;
                u = digits ? u.Substring(0, colon) + after.Substring(maybePort.Length) : u.Substring(0, colon) + "/" + after;
            }
            u = u.TrimEnd('/');
            if (u.EndsWith(".git", StringComparison.Ordinal)) u = u.Substring(0, u.Length - 4);
            return u;
        }

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
        public const string State = "state";             // helper: flag = in a session, id = local peer id, host = host id, text = role, key = project key, peers
        public const string SharedAll = "sharedAll";     // helper: entries = whole shared store
        public const string Git = "git";                 // helper: git = repository info
        public const string PendingAll = "pendingAll";   // helper: entries = guid -> path of announced assets not pulled yet
        public const string Announced = "announced";     // helper: key = guid, text = path, bounds, from
        public const string Resolved = "resolved";       // helper: key = guid (pulled now)
        public const string PullBegin = "pullBegin";     // helper: id. The editor stops auto refresh and answers PullReady
        public const string PullReady = "pullReady";     // editor: id
        public const string PullEnd = "pullEnd";         // helper: id, paths (project-relative), text = error
        public const string Log = "log";                 // helper: text, flag = error, key = "warning" for warnings, count = repeats folded into it
        public const string Send = "send";               // editor: channel, json, to
        public const string Profile = "profile";         // editor: profile
        public const string Settings = "settings";       // editor: entries (relay, gitPoll, autoPull, autoPush)
        public const string LiveScenes = "liveScenes";   // editor: paths (project-relative scene paths open in the editor)
        public const string PullNow = "pullNow";         // editor
        public const string Announce = "announce";       // editor: key = guid, text = asset path, paths = asset + dependencies, bounds
        public const string CommitScene = "commitScene"; // editor: text = scene path, json = summary, coAuthors
        public const string Shutdown = "shutdown";       // editor

        // Helper <-> helper (and both directions of the editor link for these)
        public const string Join = "join";               // peer->host: id, key, profile, presence, port (0 = can't host: a build), seq = startedAt, epoch, entries (store to merge)
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
        // A build asking "who hosts this project?" (UDP, to broadcast, multicast, 127.0.0.1 and named helpers): key, id.
        // Any helper in that project's session answers the sender with a Beacon naming the host (flag = true, port =
        // the host's peer port, text = the host's address, or empty when the answering helper is the host). Unlike
        // beacons it needs no open inbound UDP port on the build's machine: the answer comes back to the socket that
        // asked, which firewalls let through.
        public const string Discover = "discover";
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
        public string role;         // CollabProtocol.RoleEditor / RolePlaytester ("" from old peers = editor)
        public string client;       // CollabProtocol.ClientEditor / ClientBuild; builds never host
        public int version;         // the peer's protocol version (0 = older than 3)
        public CollabKV[] presence;
    }

    [Serializable]
    internal sealed class CollabProfile
    {
        public string name;
        public string avatarId;
        public string gitName;
        public string gitEmail;
        public string role;         // CollabProtocol.RoleEditor / RolePlaytester (an editor in Play Mode is a playtester)
        public string client;       // CollabProtocol.ClientEditor / ClientBuild
        public int version;         // the sender's CollabProtocol.Version (0 from peers older than version 3)
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
        public string key;          // the project key (the room), for builds made while the helper runs
    }
}
