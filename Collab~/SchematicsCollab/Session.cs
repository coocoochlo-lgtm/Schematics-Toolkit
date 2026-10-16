using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

sealed class SessionOptions
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Key;
    public int PeerPort;                       // 0 picks a free one
    public int DiscoveryPort = CollabProtocol.DiscoveryPort;
    public List<IPEndPoint> Seeds = new();     // --peer host:port, beacons go to them directly too
    public string Relay;                       // host:port of a relay; replaces LAN discovery and election
    public int TimeoutMs = 20000;              // a peer is gone after this long without a word (helpers, not editors, send them)
    public int DiscoveryWindowMs = 2500;       // how long a new peer listens for a host before electing one
    public int OrphanTimeoutMs = 90000;        // host: an older helper with no editor attached is removed after this long
}

/// <summary>
/// This helper's place in the session. Peers of one project + branch find each other with UDP beacons; the first one
/// up hosts, the others connect to it over TCP. When the host goes, everyone picks the same successor from the last
/// roster the host sent (best average ping, then lowest id) and reconnects to it; the shared store is already on every
/// peer, so nothing is lost. All state changes happen under <see cref="Gate.Lock"/>.
/// </summary>
sealed class Session
{
    enum Role { Searching, Connecting, Client, Host, Migrating }

    sealed class Beacon
    {
        public string Id, Host, Address;
        public bool IsHost;
        public long Epoch, StartedAt;
        public int Port, Count;
        public DateTime Seen;
    }

    private readonly SessionOptions _o;
    public readonly string Id;
    public readonly long StartedAt = DateTime.UtcNow.Ticks;
    public int PeerPort { get; private set; }

    private Role _role = Role.Searching;
    private DateTime _roleSince = DateTime.UtcNow;
    private long _epoch;
    private string _hostId;
    private HostRoom _room;
    private FrameConn _hostConn;          // to the host, when a client
    private FrameConn _pendingConn;       // connected, waiting for the welcome
    private bool _connecting;
    private string _lostHost;
    private readonly HashSet<string> _failed = new();
    private DateTime _migrationStarted;

    private CollabPeerInfo[] _roster = Array.Empty<CollabPeerInfo>();
    private readonly Dictionary<string, string> _store = new();
    private CollabProfile _profile = new();
    private readonly Dictionary<string, string> _presence = new();
    private readonly Queue<CollabFrame> _outbox = new();

    private TcpListener _listener;
    private Socket _udp, _discovery;
    private readonly Dictionary<string, Beacon> _beacons = new();
    // Peers that just left: their last beacons are still in flight and must not look like a live host for a while.
    private readonly Dictionary<string, DateTime> _departed = new();
    private readonly Dictionary<string, int> _rtt = new();    // peer id -> smoothed round trip, ms
    private int _hostRtt = -1;
    private bool _rttChanged;      // the editor's ping numbers are refreshed with the next ping round, not on every pong
    private DateTime _nextBeacon, _nextPing, _nextHeartbeat;
    private bool _stateDirty = true;
    private bool _stopped;

    /// <summary>Messages, shared changes and snapshots for the editor (called under the lock).</summary>
    public Action<CollabFrame> ToEditor;
    /// <summary>Messages on the helper's own channels (asset announcements).</summary>
    public Action<CollabFrame> SystemMessage;
    /// <summary>Raised (under the lock) after roster or role changes, at most every 50 ms.</summary>
    public Action StateChanged;

    public Session(SessionOptions options)
    {
        _o = options;
        Id = options.Id;
    }

    public string Key => _o.Key;
    public bool IsHost => _role == Role.Host;
    public bool InSession => _role == Role.Host || _role == Role.Client;
    public int PeerCount => InSession ? Math.Max(1, _roster.Length) : 1;
    public string HostId => InSession ? _hostId : null;
    public IReadOnlyDictionary<string, string> Store => _store;

    #region Start / stop

    public void Start()
    {
        // TCP and UDP share one port number, so a beacon or roster entry only needs one.
        int wanted = _o.PeerPort;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, wanted);
                _listener.Start();
                PeerPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
                _udp.Bind(new IPEndPoint(IPAddress.Any, PeerPort));
                try
                {
                    _udp.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                    _udp.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);
                }
                catch { }
                break;
            }
            catch (SocketException e) when (attempt < 20)
            {
                try { _listener?.Stop(); } catch { }
                try { _udp?.Dispose(); } catch { }
                // A fixed port that's taken (another program, or a leftover helper): any free one works just as well,
                // because beacons and the roster carry the port.
                if (wanted != 0)
                {
                    Log.Warn($"Peer port {wanted} is in use ({e.Message}); using a free port instead.");
                    wanted = 0;
                }
            }
        }

        if (_o.Relay == null)
        {
            try
            {
                _discovery = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _discovery.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _discovery.Bind(new IPEndPoint(IPAddress.Any, _o.DiscoveryPort));
                try
                {
                    _discovery.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                        new MulticastOption(IPAddress.Parse(CollabProtocol.MulticastGroup), IPAddress.Any));
                }
                catch (Exception e) { Log.Warn($"Multicast is unavailable on this machine ({e.Message}); finding teammates by broadcast only."); }
                _ = Task.Run(() => UdpLoop(_discovery));
            }
            catch (Exception e)
            {
                Log.Warn($"Can't listen for LAN beacons on UDP {_o.DiscoveryPort} ({e.Message}); teammates on the local network won't be found automatically (a relay still works). Is another program using that port, or a firewall blocking it?");
            }
        }
        _ = Task.Run(() => UdpLoop(_udp));
        _ = Task.Run(AcceptLoop);
        _ = Task.Run(TickLoop);
        Log.Write($"Peer {Id[..8]} listening on port {PeerPort} for project key {Key}" + (_o.Relay != null ? $", relay {_o.Relay}" : ""));
    }

    /// <summary>Leaves politely, so the others pick a new host at once instead of waiting for a timeout.</summary>
    public void Stop()
    {
        lock (Gate.Lock)
        {
            if (_stopped) return;
            _stopped = true;
            if (_role == Role.Host) _room?.CloseAll();
            if (_hostConn != null)
            {
                _hostConn.Send(new CollabFrame { type = CollabFrameType.Leave, id = Id });
                _hostConn.SuppressLoss = true;
                _hostConn.CloseAfterFlush();
            }
        }
        Thread.Sleep(350);
        try { _listener.Stop(); } catch { }
        try { _udp.Dispose(); } catch { }
        try { _discovery?.Dispose(); } catch { }
    }

    #endregion

    #region Editor-facing operations (called under the lock)

    public void SetProfile(CollabProfile profile)
    {
        _profile = profile ?? new CollabProfile();
        PublishPresence();
    }

    public void SetPresence(string key, string json)
    {
        if (Kv.Empty(key)) return;
        if (json == null) _presence.Remove(key);
        else _presence[key] = json;
        PublishPresence();
    }

    public void Send(string channel, string json, string to)
    {
        ToHost(new CollabFrame { type = CollabFrameType.Msg, channel = channel, json = json, to = Kv.Empty(to) ? null : to, from = Id });
    }

    public void SetShared(string key, string json, bool remove)
    {
        if (Kv.Empty(key)) return;
        ToHost(new CollabFrame { type = CollabFrameType.Shared, key = key, json = json, flag = remove });
    }

    private void PublishPresence()
    {
        var frame = new CollabFrame { type = CollabFrameType.Presence, presence = Kv.From(_presence), profile = _profile };
        if (_role == Role.Host) _room.FromLocal(Id, frame);
        else if (_role == Role.Client) _hostConn.Send(frame);
        // Otherwise the join carries it.
        _stateDirty = true;
    }

    private void ToHost(CollabFrame frame)
    {
        if (_role == Role.Host) _room.FromLocal(Id, frame);
        else if (_role == Role.Client && _hostConn != null) _hostConn.Send(frame);
        else
        {
            // Between hosts (or before the first): keep it and send it once connected.
            if (_outbox.Count >= 20000) _outbox.Dequeue();
            _outbox.Enqueue(frame);
        }
    }

    /// <summary>The editor's view: everyone in the session (the local peer included), with this peer's own pings.</summary>
    public CollabFrame StateFrame()
    {
        var peers = new List<CollabPeerInfo>();
        if (InSession)
            foreach (var original in _roster)
            {
                var peer = Json.Parse<CollabPeerInfo>(Json.Text(original));
                if (peer.id == Id) { peer.presence = Kv.From(_presence); peer.pingMs = 0; }
                else if (peer.id == _hostId && _hostRtt >= 0 && _o.Relay == null) peer.pingMs = _hostRtt;
                else peer.pingMs = _rtt.TryGetValue(peer.id, out int ms) ? ms : -1;
                peers.Add(peer);
            }
        if (!peers.Any(p => p.id == Id)) peers.Insert(0, LocalInfo());
        return new CollabFrame
        {
            type = CollabFrameType.State,
            flag = InSession,
            id = Id,
            host = InSession ? _hostId : null,
            text = _role.ToString().ToLowerInvariant(),
            key = Key,
            epoch = _epoch,
            peers = peers.ToArray(),
        };
    }

    private CollabPeerInfo LocalInfo() => new()
    {
        id = Id,
        name = _profile.name,
        avatarId = _profile.avatarId,
        gitName = _profile.gitName,
        gitEmail = _profile.gitEmail,
        role = Kv.Empty(_profile.role) ? CollabProtocol.RoleEditor : _profile.role,
        client = CollabProtocol.ClientEditor,
        version = CollabProtocol.Version,
        port = PeerPort,
        startedAt = StartedAt,
        isHost = _role == Role.Host,
        color = CollabProtocol.Palette[0],
        presence = Kv.From(_presence),
    };

    #endregion

    #region Frames from the host (or from our own room when hosting)

    private void FromHost(CollabFrame frame)
    {
        switch (frame.type)
        {
            case CollabFrameType.Roster:
                var incoming = frame.peers ?? Array.Empty<CollabPeerInfo>();
                foreach (var gone in _roster.Where(p => incoming.All(q => q.id != p.id)))
                    Depart(gone.id);
                _roster = incoming;
                if (!Kv.Empty(frame.host)) _hostId = frame.host;
                FillHostAddress();
                _stateDirty = true;
                break;
            case CollabFrameType.Msg:
                if (frame.channel != null && frame.channel.StartsWith(CollabProtocol.SystemChannelPrefix, StringComparison.Ordinal))
                    SystemMessage?.Invoke(frame);
                else
                {
                    FlushState();   // the editor should know the sender before its message
                    ToEditor?.Invoke(frame);
                }
                break;
            case CollabFrameType.Shared:
                if (frame.flag) _store.Remove(frame.key);
                else _store[frame.key] = frame.json ?? "";
                ToEditor?.Invoke(new CollabFrame { type = CollabFrameType.Shared, key = frame.key, json = frame.json, flag = frame.flag, from = frame.from });
                break;
            case CollabFrameType.HeartbeatAck:
                int rtt = (int)Math.Max(0, (DateTime.UtcNow.Ticks - frame.seq) / TimeSpan.TicksPerMillisecond);
                int hostRtt = _hostRtt < 0 ? rtt : (_hostRtt * 3 + rtt) / 4;
                if (hostRtt != _hostRtt) _rttChanged = true;
                _hostRtt = hostRtt;
                break;
            case CollabFrameType.Leave:
                // The host is going: start looking for the next one right away.
                if (_hostConn != null) _hostConn.Close();
                break;
        }
    }

    // Our own roster entry for the host has no address (the host can't know which of its addresses we reach it on).
    private void FillHostAddress()
    {
        if (_hostConn == null) return;
        foreach (var peer in _roster)
            if (peer.id == _hostId && Kv.Empty(peer.address)) peer.address = _hostConn.RemoteIp;
    }

    #endregion

    #region Connecting

    private void AcceptLoop()
    {
        while (!_stopped)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch { if (_stopped) return; continue; }
            var conn = new FrameConn(client);
            conn.Received = OnIncoming;
            conn.Closed = c => { lock (Gate.Lock) _room?.ConnectionClosed(c); };
            conn.Start();
        }
    }

    private void OnIncoming(FrameConn conn, CollabFrame frame)
    {
        lock (Gate.Lock)
        {
            if (conn.PeerId == null)
            {
                if (frame.type != CollabFrameType.Join || frame.key != Key || _stopped)
                {
                    conn.Close();
                    return;
                }
                if (_role != Role.Host)
                {
                    // Not the host (any more, or not yet): say who is, so the peer can go there.
                    var host = _roster.FirstOrDefault(p => p.id == _hostId);
                    conn.Send(new CollabFrame
                    {
                        type = CollabFrameType.NotHost,
                        host = _role == Role.Client ? _hostId : null,
                        text = _role == Role.Client ? (host?.address ?? _hostConn?.RemoteIp) : null,
                        port = host?.port ?? 0,
                    });
                    conn.CloseAfterFlush();
                    return;
                }
                _room.Join(conn, frame);
                return;
            }
            if (_role == Role.Host && conn.PeerId != null) _room.Handle(conn.PeerId, frame);
        }
    }

    private void ConnectTo(string address, int port, bool bringStore, string expectedHost)
    {
        if (_connecting) return;
        _connecting = true;
        var join = new CollabFrame
        {
            type = CollabFrameType.Join,
            id = Id,
            key = Key,
            profile = _profile,
            presence = Kv.From(_presence),
            port = PeerPort,
            seq = StartedAt,
            epoch = _epoch,
            entries = bringStore ? Kv.From(_store) : null,
        };
        _ = Task.Run(async () =>
        {
            var conn = await FrameConn.ConnectAsync(address, port, 3000);
            lock (Gate.Lock)
            {
                if (conn == null || _stopped || _role == Role.Host || _role == Role.Client)
                {
                    conn?.Close();
                    _connecting = false;
                    if (conn == null) ConnectFailed(expectedHost, $"couldn't connect to {address}:{port}");
                    return;
                }
                _pendingConn = conn;
                conn.Received = OnFromHostConn;
                conn.Closed = OnHostConnClosed;
                conn.Start();
                conn.Send(join);
            }
            // No welcome within a few seconds: give up on this one.
            await Task.Delay(5000);
            lock (Gate.Lock)
                if (_pendingConn == conn) { _pendingConn = null; conn.SuppressLoss = true; conn.Close(); _connecting = false; ConnectFailed(expectedHost, "no welcome"); }
        });
    }

    private void ConnectFailed(string expectedHost, string why)
    {
        if (expectedHost != null && _role == Role.Migrating)
        {
            Log.Write($"Successor {expectedHost[..Math.Min(8, expectedHost.Length)]} unreachable ({why}).");
            _failed.Add(expectedHost);
        }
    }

    private void OnFromHostConn(FrameConn conn, CollabFrame frame)
    {
        lock (Gate.Lock)
        {
            if (conn == _pendingConn)
            {
                if (frame.type == CollabFrameType.Welcome)
                {
                    _pendingConn = null;
                    _connecting = false;
                    _hostConn = conn;
                    conn.PeerId = frame.id;
                    _hostId = Kv.Empty(frame.host) ? frame.id : frame.host;
                    _epoch = frame.epoch;
                    _store.Clear();
                    foreach (var pair in Kv.To(frame.entries)) _store[pair.Key] = pair.Value;
                    _roster = frame.peers ?? Array.Empty<CollabPeerInfo>();
                    FillHostAddress();
                    SetRole(Role.Client);
                    _failed.Clear();
                    _hostRtt = -1;
                    Log.Write($"Joined the session hosted by {_hostId[..Math.Min(8, _hostId.Length)]} (epoch {_epoch}).");
                    ToEditor?.Invoke(new CollabFrame { type = CollabFrameType.SharedAll, entries = Kv.From(_store) });
                    while (_outbox.Count > 0) conn.Send(_outbox.Dequeue());
                }
                else if (frame.type == CollabFrameType.NotHost)
                {
                    _pendingConn = null;
                    _connecting = false;
                    conn.SuppressLoss = true;
                    conn.Close();
                    // It knows the real host: go there (a peer that only lost its own connection lands back home).
                    // Right after losing the host, a peer that hasn't noticed yet still names it: ignore that for a few
                    // seconds (it will notice), after which the old host evidently is alive and we return to it.
                    bool stale = _role == Role.Migrating && frame.host == _lostHost && (DateTime.UtcNow - _migrationStarted).TotalSeconds < 5;
                    if (!stale && !Kv.Empty(frame.host) && frame.host != Id && !Kv.Empty(frame.text) && frame.port > 0)
                    {
                        Log.Write($"Redirected to host {frame.host[..Math.Min(8, frame.host.Length)]} at {frame.text}:{frame.port}.");
                        ConnectTo(frame.text, frame.port, bringStore: _role == Role.Migrating, expectedHost: null);
                    }
                }
                return;
            }
            if (conn != _hostConn) return;
            if (frame.type == CollabFrameType.Redirect)
            {
                Log.Write($"The host is merging into the session at {frame.text}:{frame.port}.");
                conn.SuppressLoss = true;
                conn.Close();
                _hostConn = null;
                SetRole(Role.Connecting);
                ConnectTo(frame.text, frame.port, bringStore: true, expectedHost: null);
                return;
            }
            FromHost(frame);
        }
    }

    private void OnHostConnClosed(FrameConn conn)
    {
        lock (Gate.Lock)
        {
            if (conn != _hostConn || _stopped) return;
            _hostConn = null;
            if (conn.SuppressLoss) return;
            if (_o.Relay != null)
            {
                Log.Write("Lost the relay; reconnecting.");
                SetRole(Role.Searching);
                return;
            }
            _lostHost = _hostId;
            if (_lostHost != null) Depart(_lostHost);
            _failed.Clear();
            _migrationStarted = DateTime.UtcNow;
            Log.Write($"Lost the host {(_lostHost ?? "?")[..Math.Min(8, (_lostHost ?? "?").Length)]}; choosing a new one.");
            SetRole(Role.Migrating);
            PickSuccessor();
        }
    }

    /// <summary>
    /// The same choice on every peer, from the last roster the old host sent: the best average ping (unknown counts as
    /// worst), then the lowest id. If that's us, we host; otherwise we connect to it (and skip it if it's unreachable).
    /// </summary>
    private void PickSuccessor()
    {
        if (_role != Role.Migrating || _connecting) return;
        var candidate = _roster
            .Where(p => p.id != _lostHost && !_failed.Contains(p.id) && CanHost(p))
            .OrderBy(p => p.avgPingMs < 0 ? int.MaxValue : p.avgPingMs)
            .ThenBy(p => p.id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate == null || candidate.id == Id)
        {
            BecomeHost(_epoch + 1);
            return;
        }
        string address = Kv.Empty(candidate.address) ? "127.0.0.1" : candidate.address;
        ConnectTo(address, candidate.port, bringStore: true, expectedHost: candidate.id);
    }

    /// <summary>Builds (playtest clients) join sessions but never host one: only editors' helpers can.</summary>
    public static bool CanHost(CollabPeerInfo peer) => peer != null && peer.client != CollabProtocol.ClientBuild && peer.port > 0;

    private void BecomeHost(long epoch)
    {
        _epoch = epoch;
        _hostId = Id;
        _hostConn = null;
        _room = new HostRoom(Key, epoch, Id, FromHost, _o.TimeoutMs, _store, _roster, _o.OrphanTimeoutMs);
        SetRole(Role.Host);
        _room.AddLocal(LocalInfo());
        Log.Write($"Hosting the session (epoch {epoch}).");
        ToEditor?.Invoke(new CollabFrame { type = CollabFrameType.SharedAll, entries = Kv.From(_store) });
        while (_outbox.Count > 0) _room.FromLocal(Id, _outbox.Dequeue());
    }

    private void Depart(string id)
    {
        _departed[id] = DateTime.UtcNow;
        _beacons.Remove(id);
    }

    private void SetRole(Role role)
    {
        if (role != Role.Host && _role == Role.Host) _room = null;
        _role = role;
        _roleSince = DateTime.UtcNow;
        _stateDirty = true;
    }

    #endregion

    #region Tick

    private async Task TickLoop()
    {
        while (!_stopped)
        {
            await Task.Delay(50);
            lock (Gate.Lock)
            {
                if (_stopped) return;
                try { Tick(); }
                catch (Exception e) { Log.Exception("The session tick failed", e); }
            }
        }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        if (now >= _nextBeacon) { _nextBeacon = now.AddMilliseconds(1000); SendBeacon(); }
        if (now >= _nextPing)
        {
            _nextPing = now.AddSeconds(2);
            SendPings();
            if (_rttChanged) { _rttChanged = false; _stateDirty = true; }
        }
        foreach (var stale in _beacons.Where(b => (now - b.Value.Seen).TotalSeconds > 3.5).Select(b => b.Key).ToList())
            _beacons.Remove(stale);
        foreach (var old in _departed.Where(d => (now - d.Value).TotalSeconds > 10).Select(d => d.Key).ToList())
            _departed.Remove(old);

        switch (_role)
        {
            case Role.Searching:
            case Role.Connecting:
                if (_connecting) break;
                if (_o.Relay != null)
                {
                    var (host, port) = ParseAddress(_o.Relay);
                    SetRole(Role.Connecting);
                    ConnectTo(host, port, bringStore: _store.Count > 0, expectedHost: null);
                    break;
                }
                var hostBeacon = _beacons.Values.Where(b => b.IsHost).OrderByDescending(b => b.Count).ThenBy(b => b.StartedAt).FirstOrDefault();
                if (hostBeacon != null)
                {
                    SetRole(Role.Connecting);
                    ConnectTo(hostBeacon.Address, hostBeacon.Port, bringStore: _store.Count > 0, expectedHost: null);
                    break;
                }
                if (_role == Role.Searching && (now - _roleSince).TotalMilliseconds >= _o.DiscoveryWindowMs)
                {
                    // Nobody hosts yet: the longest-running searching peer does (then the lowest id).
                    bool first = _beacons.Values.Where(b => Kv.Empty(b.Host))
                        .All(b => b.StartedAt > StartedAt || (b.StartedAt == StartedAt && string.CompareOrdinal(b.Id, Id) > 0));
                    if (first) BecomeHost(Math.Max(_epoch, _beacons.Values.Select(b => b.Epoch).DefaultIfEmpty(0).Max()) + 1);
                }
                if (_role == Role.Connecting && (now - _roleSince).TotalSeconds > 4) SetRole(Role.Searching);
                break;

            case Role.Migrating:
                // A host that announces itself wins over our own guess (someone may have been quicker).
                var announced = _beacons.Values.Where(b => b.IsHost && b.Id != _lostHost).OrderByDescending(b => b.Epoch).FirstOrDefault();
                if (!_connecting && announced != null)
                    ConnectTo(announced.Address, announced.Port, bringStore: true, expectedHost: null);
                else if (!_connecting)
                    PickSuccessor();
                if (_role == Role.Migrating && (now - _migrationStarted).TotalSeconds > 30)
                {
                    Log.Warn("The host left and no new host could be reached for 30 s; searching for the session again.");
                    SetRole(Role.Searching);
                }
                break;

            case Role.Client:
                if (now >= _nextHeartbeat)
                {
                    _nextHeartbeat = now.AddSeconds(1);
                    _hostConn.Send(new CollabFrame { type = CollabFrameType.Heartbeat, seq = DateTime.UtcNow.Ticks, entries = PingTable() });
                }
                if ((now - _hostConn.LastReceived).TotalMilliseconds > _o.TimeoutMs)
                {
                    Log.Write("The host stopped answering.");
                    _hostConn.Close();
                }
                break;

            case Role.Host:
                _room.SetLocalPings(Id, new Dictionary<string, int>(_rtt));
                _room.Tick();
                CheckForOtherHost();
                break;
        }
        FlushState();
    }

    private void FlushState()
    {
        if (!_stateDirty) return;
        _stateDirty = false;
        StateChanged?.Invoke();
    }

    public void MarkStateDirty() => _stateDirty = true;

    private CollabKV[] PingTable()
    {
        var table = new Dictionary<string, string>();
        foreach (var pair in _rtt) table[pair.Key] = pair.Value.ToString();
        if (_hostId != null && _hostRtt >= 0) table[_hostId] = _hostRtt.ToString();
        return Kv.From(table);
    }

    // Two hosts for one project (a network split healed, or a race at start): the smaller session gives way.
    private void CheckForOtherHost()
    {
        foreach (var other in _beacons.Values)
        {
            if (!other.IsHost || other.Id == Id) continue;
            int mine = _room.Count;
            bool yield = other.Count > mine
                || (other.Count == mine && other.StartedAt < StartedAt)
                || (other.Count == mine && other.StartedAt == StartedAt && string.CompareOrdinal(other.Id, Id) < 0);
            if (!yield) continue;
            Log.Write($"Another host ({other.Id[..8]}, {other.Count} peers) runs this project; merging into its session.");
            _room.RedirectAll(other.Address, other.Port);
            SetRole(Role.Connecting);
            ConnectTo(other.Address, other.Port, bringStore: true, expectedHost: null);
            return;
        }
    }

    #endregion

    #region UDP: beacons and pings

    private void SendBeacon()
    {
        if (_o.Relay != null) return;
        var beacon = new CollabFrame
        {
            type = CollabFrameType.Beacon,
            key = Key,
            id = Id,
            flag = _role == Role.Host,
            host = InSession ? _hostId : null,
            epoch = _epoch,
            seq = StartedAt,
            port = PeerPort,
            count = _role == Role.Host ? _room.Count : 0,
        };
        byte[] bytes = Json.Bytes(beacon);
        foreach (var target in BeaconTargets())
            try { _udp.SendTo(bytes, target); } catch { }
    }

    private IEnumerable<IPEndPoint> BeaconTargets()
    {
        yield return new IPEndPoint(IPAddress.Parse(CollabProtocol.MulticastGroup), _o.DiscoveryPort);
        yield return new IPEndPoint(IPAddress.Broadcast, _o.DiscoveryPort);
        foreach (var address in SubnetBroadcasts()) yield return new IPEndPoint(address, _o.DiscoveryPort);
        foreach (var seed in _o.Seeds) yield return seed;
    }

    private static List<IPAddress> _subnets;
    private static DateTime _subnetsAt;
    private static List<IPAddress> SubnetBroadcasts()
    {
        if (_subnets != null && (DateTime.UtcNow - _subnetsAt).TotalSeconds < 30) return _subnets;
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                    byte[] ip = unicast.Address.GetAddressBytes(), mask = unicast.IPv4Mask.GetAddressBytes();
                    if (mask.All(b => b == 0)) continue;
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    list.Add(new IPAddress(broadcast));
                }
            }
        }
        catch { }
        _subnets = list;
        _subnetsAt = DateTime.UtcNow;
        return list;
    }

    private void SendPings()
    {
        if (!InSession) return;
        byte[] bytes = Json.Bytes(new CollabFrame { type = CollabFrameType.Ping, id = Id, seq = DateTime.UtcNow.Ticks });
        foreach (var peer in _roster)
        {
            if (peer.id == Id || peer.port <= 0) continue;
            string address = Kv.Empty(peer.address) ? _hostConn?.RemoteIp : peer.address;
            if (Kv.Empty(address) || !IPAddress.TryParse(address, out var ip)) continue;
            try { _udp.SendTo(bytes, new IPEndPoint(ip, peer.port)); } catch { }
        }
    }

    private void UdpLoop(Socket socket)
    {
        var buffer = new byte[65536];
        while (!_stopped)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try { length = socket.ReceiveFrom(buffer, ref remote); }
            catch { if (_stopped) return; Thread.Sleep(50); continue; }
            CollabFrame frame;
            try { frame = Json.Frame(buffer.AsSpan(0, length)); }
            catch { continue; }
            if (frame?.type == null || frame.id == Id) continue;
            var from = (IPEndPoint)remote;
            lock (Gate.Lock)
            {
                try { OnDatagram(socket, frame, from); }
                catch (Exception e) { Log.Exception($"Handling a '{frame.type}' datagram failed", e); }
            }
        }
    }

    private void OnDatagram(Socket socket, CollabFrame frame, IPEndPoint from)
    {
        switch (frame.type)
        {
            case CollabFrameType.Beacon:
                if (frame.key != Key || Kv.Empty(frame.id) || _departed.ContainsKey(frame.id)) return;
                _beacons[frame.id] = new Beacon
                {
                    Id = frame.id,
                    Host = frame.host,
                    Address = from.Address.MapToIPv4().ToString(),
                    IsHost = frame.flag,
                    Epoch = frame.epoch,
                    StartedAt = frame.seq,
                    Port = frame.port,
                    Count = frame.count,
                    Seen = DateTime.UtcNow,
                };
                break;
            case CollabFrameType.Discover:
                AnswerDiscover(socket, frame, from);
                break;
            case CollabFrameType.Ping:
                try { socket.SendTo(Json.Bytes(new CollabFrame { type = CollabFrameType.Pong, id = Id, seq = frame.seq }), from); } catch { }
                break;
            case CollabFrameType.Pong:
                int rtt = (int)Math.Max(0, (DateTime.UtcNow.Ticks - frame.seq) / TimeSpan.TicksPerMillisecond);
                int smoothed = _rtt.TryGetValue(frame.id, out int old) ? (old * 3 + rtt) / 4 : rtt;
                if (!_rtt.ContainsKey(frame.id) || smoothed != old) _rttChanged = true;
                _rtt[frame.id] = smoothed;
                break;
        }
    }

    /// <summary>
    /// A build asks who hosts its project: anyone in the session answers with the host's address and port. The host
    /// leaves the address empty (the build uses where the answer came from); a client names the host as it reaches it,
    /// except a loopback address, which the build couldn't use from another machine (then it's the answering machine's).
    /// </summary>
    private void AnswerDiscover(Socket socket, CollabFrame frame, IPEndPoint from)
    {
        if (frame.key != Key || !InSession || _o.Relay != null) return;
        string address = "";
        int port = PeerPort;
        if (_role == Role.Client)
        {
            var host = _roster.FirstOrDefault(p => p.id == _hostId);
            address = host?.address ?? _hostConn?.RemoteIp ?? "";
            port = host?.port ?? 0;
            if (port <= 0 || Kv.Empty(address)) return;
            if (IPAddress.TryParse(address, out var ip) && IPAddress.IsLoopback(ip) && !IPAddress.IsLoopback(from.Address)) address = "";
        }
        var reply = new CollabFrame
        {
            type = CollabFrameType.Beacon,
            key = Key,
            id = Id,
            flag = true,
            host = _hostId,
            text = address,
            port = port,
            epoch = _epoch,
            count = _role == Role.Host ? _room.Count : _roster.Length,
        };
        try { socket.SendTo(Json.Bytes(reply), from); } catch { }
    }

    public static (string host, int port) ParseAddress(string text)
    {
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int port)) return (text[..colon], port);
        return (text, 47812);
    }

    #endregion
}
