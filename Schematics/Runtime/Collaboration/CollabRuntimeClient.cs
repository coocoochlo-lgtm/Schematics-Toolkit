using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Like CollabProtocol.cs, this file is plain C# (no UnityEngine): Unity compiles it into the runtime assembly for builds,
// and the SchematicsCollab helper compiles it too, so its self test can join a session exactly the way a build does.
// Keep it to C# 9 / .NET Standard 2.1.

// Filled in by the JSON deserializer or by callers, which the compiler can't see.
#pragma warning disable 0649

namespace Remedy.Schematics.Collaboration
{
    /// <summary>How a <see cref="CollabRuntimeClient"/> finds and joins its room.</summary>
    internal sealed class CollabRuntimeClientOptions
    {
        /// <summary>
        /// This client's peer id. Builds pass a stable one (<see cref="CollabStableId"/>), so a restarted game takes its
        /// own place in the room again; the default is fresh per run.
        /// </summary>
        public string Id = Guid.NewGuid().ToString("N");
        /// <summary>The project key (the room), baked into the build: see <see cref="CollabProtocol.ProjectKey"/>.</summary>
        public string Key;
        public string Name;
        public string AvatarId = "";
        public string Role = CollabProtocol.RolePlaytester;
        /// <summary>host:port of a relay. When set, the client goes there instead of looking for LAN beacons.</summary>
        public string Relay;
        /// <summary>host:port of helpers to ask directly when no beacon is heard (a helper that isn't host says who is).</summary>
        public List<string> Seeds = new List<string>();
        /// <summary>UDP ports to hear host beacons on (the LAN discovery port by default).</summary>
        public List<int> ListenPorts = new List<int> { CollabProtocol.DiscoveryPort };
        /// <summary>
        /// Ask actively: send "who hosts this project?" (<see cref="CollabFrameType.Discover"/>) to the LAN discovery
        /// port by multicast, broadcast, each subnet's broadcast and 127.0.0.1 (an editor on this machine). Answers come
        /// back to the asking socket, so this works even when the firewall blocks the game's inbound UDP (beacons).
        /// </summary>
        public bool DiscoverOnLan = true;
        /// <summary>The LAN discovery port helpers listen on (CollabProtocol.DiscoveryPort; tests use another).</summary>
        public int DiscoveryPort = CollabProtocol.DiscoveryPort;
        /// <summary>More "host:port" places to send discovery questions to (the -collab-peer helpers are added too).</summary>
        public List<string> DiscoverTargets = new List<string>();
        /// <summary>The host is considered gone after this long without a word (the host sends heartbeats every second).</summary>
        public int TimeoutMs = 20000;
        /// <summary>JSON for frames: JsonUtility in Unity, System.Text.Json in the helper. Both may be called off the main thread.</summary>
        public Func<CollabFrame, string> ToJson;
        public Func<string, CollabFrame> FromJson;
        /// <summary>Log lines: (level "info" / "warning" / "error", text). Called from background threads.</summary>
        public Action<string, string> Log;
    }

    /// <summary>
    /// The playtest client that runs inside builds: it finds the project's room (the host's LAN beacons for the same
    /// project key, a relay, or helpers named directly), joins it as a member and then sends and receives messages, the
    /// shared store and presence like any editor does.
    ///
    /// It never hosts. A build may run on a console or a phone, be paused in a debugger, or be killed without warning;
    /// the host keeps the session's shared store and commits scene snapshots, which only an editor's helper can do.
    /// So a build joins whatever room its editors (or a relay) keep, says so in its join (port 0, client "build"), and
    /// every helper leaves it out when choosing the next host. When the host leaves it simply waits for the next one's
    /// beacon and joins again.
    ///
    /// Networking runs on background threads; everything for the game arrives through <see cref="Poll"/>, which the
    /// main thread calls once a frame, so no game code ever runs on a network thread.
    /// </summary>
    internal sealed class CollabRuntimeClient
    {
        /// <summary>Events for the main thread (see <see cref="Poll"/>).</summary>
        public enum EventKind { Connected, Disconnected, Message, SharedChanged, PeersChanged, Log }

        public struct Event
        {
            public EventKind Kind;
            public string Channel;      // Message
            public string From;         // Message: sender id
            public string Json;         // Message
            public string Key;          // SharedChanged
        }

        private sealed class Target
        {
            public string Host;
            public int Port;
            public override string ToString() => Host + ":" + Port;
        }

        public readonly CollabRuntimeClientOptions Options;
        private readonly ConcurrentQueue<Event> _events = new ConcurrentQueue<Event>();
        private readonly object _lock = new object();
        private readonly object _writeLock = new object();
        private readonly List<Socket> _udp = new List<Socket>();
        private readonly Queue<CollabFrame> _outbox = new Queue<CollabFrame>();
        private readonly Dictionary<string, string> _store = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _presence = new Dictionary<string, string>();
        private CollabPeerInfo[] _roster = new CollabPeerInfo[0];
        private Thread _main, _heartbeat;
        private volatile bool _stopped;
        private volatile bool _connected;
        private TcpClient _tcp;
        private NetworkStream _stream;
        private long _lastReceivedTicks;
        private long _joinedAtTicks;
        private Target _hostBeacon;
        private DateTime _hostBeaconSeen;
        private Target _redirect;
        private int _seedIndex;
        private DateTime _searchingSince = DateTime.UtcNow;
        private string _hostId;
        private int _pingMs = -1;
        private string _lastError;
        private Socket _ask;                                  // sends discovery questions and hears the answers
        private DateTime _nextAsk;
        private readonly HashSet<string> _foreignKeys = new HashSet<string>();
        private string _lastHostHeard;
        private int _searchNotes;

        public CollabRuntimeClient(CollabRuntimeClientOptions options)
        {
            Options = options;
            if (string.IsNullOrEmpty(Options.Id)) Options.Id = Guid.NewGuid().ToString("N");
        }

        #region State (thread-safe)

        public string Id => Options.Id;
        public bool IsConnected => _connected;
        public int PingMs => _pingMs;
        public string HostId { get { lock (_lock) return _connected ? _hostId : null; } }

        /// <summary>Everyone in the room (this client included), as the host last sent it.</summary>
        public CollabPeerInfo[] Peers { get { lock (_lock) return _connected ? (CollabPeerInfo[])_roster.Clone() : new CollabPeerInfo[0]; } }

        public string GetShared(string key)
        {
            if (key == null) return null;
            lock (_lock) return _store.TryGetValue(key, out var value) ? value : null;
        }

        public Dictionary<string, string> SharedSnapshot()
        {
            lock (_lock) return new Dictionary<string, string>(_store);
        }

        /// <summary>Takes up to <paramref name="max"/> events (call on the main thread).</summary>
        public int Poll(Action<Event> handle, int max = 1000)
        {
            int count = 0;
            while (count < max && _events.TryDequeue(out var e))
            {
                count++;
                handle(e);
            }
            return count;
        }

        #endregion

        #region Start / stop

        public void Start()
        {
            if (string.IsNullOrEmpty(Options.Key)) throw new InvalidOperationException("A project key is needed to find the room.");
            if (Options.ToJson == null || Options.FromJson == null) throw new InvalidOperationException("JSON functions are needed.");
            if (string.IsNullOrEmpty(Options.Relay))
            {
                foreach (int port in Options.ListenPorts) ListenForBeacons(port);
                OpenAskSocket();
            }
            _main = new Thread(Run) { IsBackground = true, Name = "Collab client" };
            _main.Start();
            _heartbeat = new Thread(HeartbeatLoop) { IsBackground = true, Name = "Collab client heartbeat" };
            _heartbeat.Start();
        }

        /// <summary>Leaves politely (the room drops us at once instead of after a timeout) and stops every thread.</summary>
        public void Stop()
        {
            if (_stopped) return;
            if (_connected) Write(new CollabFrame { type = CollabFrameType.Leave, id = Id });
            _stopped = true;
            CloseConnection();
            lock (_udp)
            {
                foreach (var socket in _udp) { try { socket.Close(); } catch { } }
                _udp.Clear();
            }
        }

        #endregion

        #region Sending

        /// <summary>To everyone in the room (or one peer). Held while between hosts and sent once back in.</summary>
        public void Send(string channel, string json, string to = null)
        {
            if (string.IsNullOrEmpty(channel)) return;
            Post(new CollabFrame { type = CollabFrameType.Msg, channel = channel, json = json ?? "", to = to ?? "", from = Id });
        }

        /// <summary>Sets (or with null removes) a shared store key. The host orders it and sends it to everyone.</summary>
        public void SetShared(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool changed;
            lock (_lock)
            {
                // Seen locally at once (like the editor does); the host's echo confirms it.
                changed = json == null ? _store.Remove(key) : !_store.TryGetValue(key, out var old) || old != json;
                if (json != null) _store[key] = json;
            }
            if (changed) _events.Enqueue(new Event { Kind = EventKind.SharedChanged, Key = key });
            Post(new CollabFrame { type = CollabFrameType.Shared, key = key, json = json ?? "", flag = json == null });
        }

        /// <summary>Sets (or with null removes) one key of this client's presence (what editors see about it).</summary>
        public void SetPresence(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock)
            {
                if (json == null) _presence.Remove(key);
                else _presence[key] = json;
            }
            if (_connected) Write(PresenceFrame());
        }

        private void Post(CollabFrame frame)
        {
            if (_connected && Write(frame)) return;
            lock (_lock)
            {
                if (_outbox.Count >= 5000) _outbox.Dequeue();
                _outbox.Enqueue(frame);
            }
        }

        private CollabFrame PresenceFrame()
        {
            lock (_lock) return new CollabFrame { type = CollabFrameType.Presence, presence = Kv(_presence), profile = Profile() };
        }

        private CollabProfile Profile() => new CollabProfile
        {
            name = Options.Name ?? "",
            avatarId = Options.AvatarId ?? "",
            role = Options.Role ?? CollabProtocol.RolePlaytester,
            client = CollabProtocol.ClientBuild,
            version = CollabProtocol.Version,
        };

        private static CollabKV[] Kv(Dictionary<string, string> map)
        {
            var list = new CollabKV[map.Count];
            int i = 0;
            foreach (var pair in map) list[i++] = new CollabKV { key = pair.Key, value = pair.Value };
            return list;
        }

        #endregion

        #region Finding the room

        private void ListenForBeacons(int port)
        {
            Socket socket = null;
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(IPAddress.Parse(CollabProtocol.MulticastGroup), IPAddress.Any));
                }
                catch (Exception e) { Log("info", "Multicast unavailable (" + e.Message + "); listening for broadcast beacons only."); }
                lock (_udp) _udp.Add(socket);
                var thread = new Thread(() => BeaconLoop(socket)) { IsBackground = true, Name = "Collab client beacons" };
                thread.Start();
            }
            catch (Exception e)
            {
                try { socket?.Close(); } catch { }
                Log("warning", "Can't listen for editors' beacons on UDP " + port + " (" + e.Message + "). The game will only find editors "
                    + "through a relay or -collab-peer host:port.");
            }
        }

        private void BeaconLoop(Socket socket)
        {
            var buffer = new byte[65536];
            while (!_stopped)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                int length;
                try { length = socket.ReceiveFrom(buffer, ref remote); }
                catch
                {
                    if (_stopped) return;
                    Thread.Sleep(100);
                    continue;
                }
                CollabFrame beacon;
                try { beacon = Options.FromJson(Encoding.UTF8.GetString(buffer, 0, length)); }
                catch { continue; }   // not ours (anything may arrive on a broadcast port)
                if (beacon == null || beacon.type != CollabFrameType.Beacon) continue;
                var address = ((IPEndPoint)remote).Address;
                if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
                if (beacon.key != Options.Key)
                {
                    // Editors of another project or branch. Worth saying once: a build made from another branch than
                    // the editors are on is the most common reason a build doesn't find them.
                    bool first;
                    lock (_lock) first = !string.IsNullOrEmpty(beacon.key) && _foreignKeys.Count < 20 && _foreignKeys.Add(beacon.key);
                    if (first)
                        Log("info", "Heard collaboration helpers at " + address + " for another room (key " + beacon.key + "); this build's room is "
                            + Options.Key + ". Builds only meet editors of the same repository, branch and project folder they were built from.");
                    continue;
                }
                if (!beacon.flag || beacon.port <= 0) continue;
                // A discovery answer may name the host (text); a beacon or the host's own answer means "here".
                string host = address.ToString();
                if (!string.IsNullOrEmpty(beacon.text))
                {
                    IPAddress named;
                    host = IPAddress.TryParse(beacon.text, out named) && IPAddress.IsLoopback(named) && !IPAddress.IsLoopback(address) ? address.ToString() : beacon.text;
                }
                bool announce;
                lock (_lock)
                {
                    _hostBeacon = new Target { Host = host, Port = beacon.port };
                    _hostBeaconSeen = DateTime.UtcNow;
                    string heard = host + ":" + beacon.port;
                    announce = !_connected && heard != _lastHostHeard;
                    _lastHostHeard = heard;
                }
                if (announce) Log("info", "Found the room's host at " + host + ":" + beacon.port + ".");
            }
        }

        private void OpenAskSocket()
        {
            if (!Options.DiscoverOnLan && Options.DiscoverTargets.Count == 0 && Options.Seeds.Count == 0) return;
            try
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                try { socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2); } catch { }
                try { socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true); } catch { }
                lock (_udp) _udp.Add(socket);
                _ask = socket;
                var thread = new Thread(() => BeaconLoop(socket)) { IsBackground = true, Name = "Collab client discovery" };
                thread.Start();
            }
            catch (Exception e)
            {
                Log("warning", "Can't open a UDP socket to look for editors (" + e.Message + "); only beacons, a relay or -collab-peer can find them.");
            }
        }

        /// <summary>While searching: asks everywhere a helper could be who hosts this project (every 1.5 s).</summary>
        private void Ask()
        {
            var socket = _ask;
            if (socket == null || _connected || !string.IsNullOrEmpty(Options.Relay) || DateTime.UtcNow < _nextAsk) return;
            _nextAsk = DateTime.UtcNow.AddMilliseconds(1500);
            byte[] bytes;
            try { bytes = Encoding.UTF8.GetBytes(Options.ToJson(new CollabFrame { type = CollabFrameType.Discover, key = Options.Key, id = Id })); }
            catch { return; }
            foreach (var target in AskTargets())
                try { socket.SendTo(bytes, target); } catch { /* unreachable networks are normal */ }
        }

        private List<IPEndPoint> _lanTargets;
        private DateTime _lanTargetsAt;

        private IEnumerable<IPEndPoint> AskTargets()
        {
            var list = new List<IPEndPoint>();
            if (Options.DiscoverOnLan)
            {
                if (_lanTargets == null || (DateTime.UtcNow - _lanTargetsAt).TotalSeconds > 30)
                {
                    _lanTargetsAt = DateTime.UtcNow;
                    _lanTargets = new List<IPEndPoint>
                    {
                        new IPEndPoint(IPAddress.Parse(CollabProtocol.MulticastGroup), Options.DiscoveryPort),
                        new IPEndPoint(IPAddress.Broadcast, Options.DiscoveryPort),
                        new IPEndPoint(IPAddress.Loopback, Options.DiscoveryPort),
                    };
                    try
                    {
                        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                            {
                                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                                byte[] ip = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes();
                                bool anyMask = false;
                                foreach (var b in mask) if (b != 0) anyMask = true;
                                if (!anyMask) continue;
                                var broadcast = new byte[4];
                                for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                                _lanTargets.Add(new IPEndPoint(new IPAddress(broadcast), Options.DiscoveryPort));
                            }
                        }
                    }
                    catch { /* not every platform lists its network interfaces; the broadcast above still goes out */ }
                }
                list.AddRange(_lanTargets);
            }
            foreach (var text in Options.DiscoverTargets) AddTarget(list, text, Options.DiscoveryPort);
            foreach (var text in Options.Seeds) AddTarget(list, text, Options.DiscoveryPort);
            return list;
        }

        private static void AddTarget(List<IPEndPoint> list, string text, int defaultPort)
        {
            var target = ParseAddress(text, defaultPort);
            try
            {
                IPAddress address;
                if (!IPAddress.TryParse(target.Host, out address))
                {
                    address = null;
                    foreach (var candidate in Dns.GetHostAddresses(target.Host))
                        if (candidate.AddressFamily == AddressFamily.InterNetwork) { address = candidate; break; }
                }
                if (address != null) list.Add(new IPEndPoint(address, target.Port));
            }
            catch { /* a name that doesn't resolve right now */ }
        }

        private Target PickTarget()
        {
            lock (_lock)
            {
                if (_redirect != null)
                {
                    var redirect = _redirect;
                    _redirect = null;
                    return redirect;
                }
                if (!string.IsNullOrEmpty(Options.Relay)) return ParseAddress(Options.Relay, 47812);
                if (_hostBeacon != null && (DateTime.UtcNow - _hostBeaconSeen).TotalSeconds < 3.5) return _hostBeacon;
                // No beacon heard for a while: ask the named helpers in turn (they answer with the real host).
                if (Options.Seeds.Count > 0 && (DateTime.UtcNow - _searchingSince).TotalSeconds > 2)
                    return ParseAddress(Options.Seeds[_seedIndex++ % Options.Seeds.Count], CollabProtocol.DiscoveryPort);
                return null;
            }
        }

        private static Target ParseAddress(string text, int defaultPort)
        {
            text = (text ?? "").Trim();
            int colon = text.LastIndexOf(':');
            if (colon > 0 && int.TryParse(text.Substring(colon + 1), out int port)) return new Target { Host = text.Substring(0, colon), Port = port };
            return new Target { Host = text, Port = defaultPort };
        }

        #endregion

        #region Connection

        private void Run()
        {
            while (!_stopped)
            {
                Target target;
                try { target = PickTarget(); }
                catch (Exception e) { Log("error", "Choosing where to connect failed: " + e); target = null; }
                if (target == null)
                {
                    try { Ask(); } catch (Exception e) { Note("warning", "Asking for the room's host failed: " + e.Message); }
                    NoteStillSearching();
                    Thread.Sleep(250);
                    continue;
                }
                try { Session(target); }
                catch (Exception e)
                {
                    // Losing a room we were in is worth a warning; a connect attempt that fails while searching isn't.
                    if (!_stopped)
                        Note(_connected ? "warning" : "info", (_connected ? "Lost the connection to the room at " : "Couldn't join the room at ")
                            + target + " (" + (e.GetBaseException().Message) + ").");
                }
                CloseConnection();
                if (_stopped) return;
                bool redirected;
                lock (_lock) redirected = _redirect != null;
                if (!redirected) Thread.Sleep(1000);
            }
        }

        /// <summary>One connection: join, then read frames until it closes.</summary>
        private void Session(Target target)
        {
            var client = new TcpClient { NoDelay = true };
            var connect = client.ConnectAsync(target.Host, target.Port);
            if (!connect.Wait(3000) || !client.Connected)
            {
                try { client.Close(); } catch { }
                throw new IOException("no answer");
            }
            var stream = client.GetStream();
            lock (_writeLock)
            {
                _tcp = client;
                _stream = stream;
            }
            Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
            Interlocked.Exchange(ref _joinedAtTicks, DateTime.UtcNow.Ticks);
            CollabKV[] presence;
            lock (_lock) presence = Kv(_presence);
            if (!Write(new CollabFrame
                {
                    type = CollabFrameType.Join,
                    id = Id,
                    key = Options.Key,
                    profile = Profile(),
                    presence = presence,
                    port = 0,                         // can't be reached: never a host candidate
                    seq = DateTime.UtcNow.Ticks,
                }))
                throw new IOException("couldn't send the join");

            var header = new byte[4];
            while (!_stopped)
            {
                ReadExact(stream, header);
                int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                if (length < 0 || length > CollabProtocol.MaxFrameBytes) throw new IOException("bad frame length " + length);
                var body = new byte[length];
                ReadExact(stream, body);
                Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
                CollabFrame frame;
                try { frame = Options.FromJson(Encoding.UTF8.GetString(body)); }
                catch (Exception e) { Note("warning", "Ignored a malformed frame (" + e.Message + ")."); continue; }
                if (frame == null || frame.type == null) continue;
                bool keepGoing;
                try { keepGoing = Handle(frame); }
                catch (Exception e)
                {
                    Note("error", "Handling a '" + frame.type + "' frame failed: " + e);
                    keepGoing = true;
                }
                if (!keepGoing) return;
            }
        }

        /// <summary>Handles one frame from the room; false to end this connection.</summary>
        private bool Handle(CollabFrame frame)
        {
            switch (frame.type)
            {
                case CollabFrameType.Welcome:
                {
                    var changedKeys = new List<string>();
                    CollabFrame[] queued;
                    lock (_lock)
                    {
                        _hostId = string.IsNullOrEmpty(frame.host) ? frame.id : frame.host;
                        var incoming = new Dictionary<string, string>();
                        foreach (var e in frame.entries ?? new CollabKV[0])
                            if (e != null && !string.IsNullOrEmpty(e.key)) incoming[e.key] = e.value ?? "";
                        foreach (var key in _store.Keys) if (!incoming.ContainsKey(key)) changedKeys.Add(key);
                        foreach (var pair in incoming)
                            if (!_store.TryGetValue(pair.Key, out var old) || old != pair.Value) changedKeys.Add(pair.Key);
                        _store.Clear();
                        foreach (var pair in incoming) _store[pair.Key] = pair.Value;
                        _roster = frame.peers ?? new CollabPeerInfo[0];
                        queued = _outbox.ToArray();
                        _outbox.Clear();
                        _connected = true;
                        _lastError = null;
                        _searchNotes = 0;
                    }
                    Log("info", "Joined the room (host " + Short(_hostId) + ", " + _roster.Length + " in it).");
                    _events.Enqueue(new Event { Kind = EventKind.Connected });
                    _events.Enqueue(new Event { Kind = EventKind.PeersChanged });
                    foreach (var key in changedKeys) _events.Enqueue(new Event { Kind = EventKind.SharedChanged, Key = key });
                    foreach (var pending in queued) Write(pending);
                    return true;
                }
                case CollabFrameType.NotHost:
                    // Asked a helper that isn't host: it says who is.
                    if (!string.IsNullOrEmpty(frame.text) && frame.port > 0)
                        lock (_lock) _redirect = new Target { Host = frame.text, Port = frame.port };
                    return false;
                case CollabFrameType.Redirect:
                    // The host is merging into another session.
                    if (!string.IsNullOrEmpty(frame.text) && frame.port > 0)
                        lock (_lock) _redirect = new Target { Host = frame.text, Port = frame.port };
                    return false;
                case CollabFrameType.Leave:
                    // The host is going; the next host's beacon will bring us back in.
                    lock (_lock) _hostBeacon = null;
                    return false;
                case CollabFrameType.Roster:
                    lock (_lock)
                    {
                        _roster = frame.peers ?? new CollabPeerInfo[0];
                        if (!string.IsNullOrEmpty(frame.host)) _hostId = frame.host;
                    }
                    _events.Enqueue(new Event { Kind = EventKind.PeersChanged });
                    return true;
                case CollabFrameType.Msg:
                    if (string.IsNullOrEmpty(frame.channel) || frame.channel.StartsWith(CollabProtocol.SystemChannelPrefix, StringComparison.Ordinal)) return true;
                    _events.Enqueue(new Event { Kind = EventKind.Message, Channel = frame.channel, From = frame.from, Json = frame.json });
                    return true;
                case CollabFrameType.Shared:
                {
                    if (string.IsNullOrEmpty(frame.key)) return true;
                    bool changed;
                    lock (_lock)
                    {
                        changed = frame.flag ? _store.Remove(frame.key) : !_store.TryGetValue(frame.key, out var old) || old != (frame.json ?? "");
                        if (!frame.flag) _store[frame.key] = frame.json ?? "";
                    }
                    if (changed) _events.Enqueue(new Event { Kind = EventKind.SharedChanged, Key = frame.key });
                    return true;
                }
                case CollabFrameType.HeartbeatAck:
                {
                    int rtt = (int)Math.Max(0, (DateTime.UtcNow.Ticks - frame.seq) / TimeSpan.TicksPerMillisecond);
                    _pingMs = _pingMs < 0 ? rtt : (_pingMs * 3 + rtt) / 4;
                    return true;
                }
                default:
                    return true;   // heartbeats (only refresh the timeout) and anything newer
            }
        }

        /// <summary>Every second: a heartbeat to the host (so it keeps us), and give up on a silent host or a join nobody answers.</summary>
        private void HeartbeatLoop()
        {
            while (!_stopped)
            {
                Thread.Sleep(1000);
                try
                {
                    bool hasConnection;
                    lock (_writeLock) hasConnection = _tcp != null;
                    if (!hasConnection) continue;
                    long now = DateTime.UtcNow.Ticks;
                    if (_connected)
                    {
                        Write(new CollabFrame { type = CollabFrameType.Heartbeat, seq = now, entries = new CollabKV[0] });
                        if ((now - Interlocked.Read(ref _lastReceivedTicks)) / TimeSpan.TicksPerMillisecond > Options.TimeoutMs)
                        {
                            Note("warning", "The host stopped answering; looking for the room again.");
                            CloseConnection();
                        }
                    }
                    else if ((now - Interlocked.Read(ref _joinedAtTicks)) / TimeSpan.TicksPerMillisecond > 5000)
                        CloseConnection();   // connected but never welcomed
                }
                catch (Exception e) { Note("error", "Heartbeat failed: " + e.Message); }
            }
        }

        private bool Write(CollabFrame frame)
        {
            byte[] body;
            try { body = Encoding.UTF8.GetBytes(Options.ToJson(frame)); }
            catch (Exception e)
            {
                Note("error", "Couldn't encode a '" + frame.type + "' frame: " + e.Message);
                return false;
            }
            var header = new[] { (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length };
            lock (_writeLock)
            {
                if (_stream == null) return false;
                try
                {
                    _stream.WriteTimeout = 5000;
                    _stream.Write(header, 0, 4);
                    _stream.Write(body, 0, body.Length);
                    return true;
                }
                catch
                {
                    try { _tcp?.Close(); } catch { }
                    return false;
                }
            }
        }

        private void CloseConnection()
        {
            lock (_writeLock)
            {
                try { _tcp?.Close(); } catch { }
                _tcp = null;
                _stream = null;
            }
            bool was;
            lock (_lock)
            {
                was = _connected;
                _connected = false;
                if (was) _searchingSince = DateTime.UtcNow;
            }
            if (was)
            {
                Log("info", "Left the room; looking for it again.");
                _events.Enqueue(new Event { Kind = EventKind.Disconnected });
                _events.Enqueue(new Event { Kind = EventKind.PeersChanged });
            }
        }

        private static void ReadExact(NetworkStream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) throw new IOException("closed");
                read += n;
            }
        }

        #endregion

        #region Logging

        private void Log(string level, string text)
        {
            try { Options.Log?.Invoke(level, text); }
            catch { /* logging must never take the client down */ }
        }

        // Connection trouble repeats every second while it lasts: say it once until something changes.
        private void Note(string level, string text)
        {
            lock (_lock)
            {
                if (text == _lastError) return;
                _lastError = text;
            }
            Log(level, text);
        }

        // Every so often while nothing is found: say so in the log (Player.log in builds), with what to check.
        private void NoteStillSearching()
        {
            double seconds = (DateTime.UtcNow - _searchingSince).TotalSeconds;
            int[] marks = { 10, 30, 60, 180, 600 };
            if (_searchNotes >= marks.Length || seconds < marks[_searchNotes]) return;
            _searchNotes++;
            Log("info", "Still looking for the room after " + (int)seconds + " s (key " + Options.Key
                + (string.IsNullOrEmpty(Options.Relay) ? ", local network" : ", relay " + Options.Relay) + "). Check that an editor of this repository and "
                + "branch has collaboration on, that both machines are on the same network (or use a relay), and that the firewall lets the "
                + "editor's helper (dotnet) accept connections. -collab-peer HOST:PORT names an editor's helper directly.");
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "?" : id.Length > 8 ? id.Substring(0, 8) : id;

        #endregion
    }

    /// <summary>
    /// A peer id that stays the same across runs of a game on one machine, but differs between copies running at the
    /// same time: "&lt;install id&gt;-&lt;slot&gt;". The install id is made once and kept in <c>folder</c>; each running copy
    /// claims the lowest free slot (a lock file, plus the claiming process id written in it, because not every
    /// runtime enforces file locks between processes). A restarted game claims its old slot again and so rejoins as
    /// the same peer, replacing its old entry instead of adding one. Anything that fails falls back to a fresh id.
    /// </summary>
    internal sealed class CollabStableId : IDisposable
    {
        private static readonly HashSet<string> ClaimedInProcess = new HashSet<string>();
        private FileStream _lock;
        private string _lockPath;

        public string Id { get; private set; }
        public int Slot { get; private set; } = -1;

        /// <summary>Claims an id under <paramref name="folder"/>. <paramref name="isAlive"/> says whether a process id still runs (null: assume it does).</summary>
        public static CollabStableId Claim(string folder, int processId, Func<int, bool> isAlive)
        {
            var result = new CollabStableId();
            try
            {
                Directory.CreateDirectory(folder);
                string installFile = Path.Combine(folder, "install-id");
                string install = File.Exists(installFile) ? File.ReadAllText(installFile).Trim() : "";
                if (install.Length < 16)
                {
                    install = Guid.NewGuid().ToString("N");
                    File.WriteAllText(installFile, install);
                }
                for (int slot = 0; slot < 64; slot++)
                {
                    string path = Path.Combine(folder, "slot-" + slot + ".lock");
                    lock (ClaimedInProcess) if (ClaimedInProcess.Contains(path)) continue;
                    FileStream stream;
                    try { stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    string recorded;
                    try
                    {
                        var bytes = new byte[64];
                        int n = stream.Read(bytes, 0, bytes.Length);
                        recorded = Encoding.UTF8.GetString(bytes, 0, n).Trim();
                    }
                    catch { recorded = ""; }
                    int owner;
                    if (int.TryParse(recorded, out owner) && owner != processId && owner > 0)
                    {
                        bool alive;
                        try { alive = isAlive == null || isAlive(owner); } catch { alive = true; }
                        if (alive) { stream.Dispose(); continue; }
                    }
                    var mine = Encoding.UTF8.GetBytes(processId.ToString());
                    stream.SetLength(0);
                    stream.Write(mine, 0, mine.Length);
                    stream.Flush();
                    lock (ClaimedInProcess) ClaimedInProcess.Add(path);
                    result._lock = stream;
                    result._lockPath = path;
                    result.Slot = slot;
                    result.Id = install + "-" + slot;
                    return result;
                }
            }
            catch { /* fall through to a fresh id */ }
            result.Id = Guid.NewGuid().ToString("N");
            return result;
        }

        /// <summary>Gives the slot back (the next run, or another copy, may take it).</summary>
        public void Dispose()
        {
            if (_lock == null) return;
            try
            {
                _lock.SetLength(0);
                _lock.Dispose();
            }
            catch { }
            lock (ClaimedInProcess) ClaimedInProcess.Remove(_lockPath);
            _lock = null;
        }
    }
}
