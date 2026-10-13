using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>
/// The host's side of a session: who's in it, their presence and colours, the shared store, and fanning messages out.
/// A hosting peer has one (with itself as the local member, whose frames are delivered in process); a relay has one per
/// project key and no local member. All calls happen under <see cref="Gate.Lock"/>.
/// </summary>
sealed class HostRoom
{
    sealed class Member
    {
        public FrameConn Conn;            // null for the local member
        public CollabPeerInfo Info;
        public Dictionary<string, int> Pings = new();   // this member's round trips to the others, in ms
    }

    public readonly string Key;
    public readonly long Epoch;
    public readonly string HostId;        // the hosting peer's id, or "relay"
    private readonly bool _isRelay;
    private string _lead;                 // relay only: the member editors treat as host (scene snapshots, authority)

    /// <summary>The peer editors see as host: the hosting peer, or on a relay the member chosen to lead.</summary>
    public string LeadId => _isRelay ? _lead : HostId;
    private readonly Action<CollabFrame> _local;
    private readonly Dictionary<string, Member> _members = new();
    public readonly Dictionary<string, string> Store = new();
    private readonly Dictionary<string, int> _colors = new();   // kept for people who leave and come back
    private readonly int _timeoutMs;
    private bool _rosterDirty;
    private string _lastRosterSignature = "";
    private DateTime _nextHeartbeat, _nextRosterCheck;

    public HostRoom(string key, long epoch, string hostId, Action<CollabFrame> local, int timeoutMs,
        IDictionary<string, string> store, IEnumerable<CollabPeerInfo> previousRoster)
    {
        Key = key;
        Epoch = epoch;
        HostId = hostId;
        _local = local;
        _timeoutMs = timeoutMs;
        _isRelay = local == null;
        if (store != null)
            foreach (var pair in store) Store[pair.Key] = pair.Value;
        if (previousRoster != null)
            foreach (var peer in previousRoster)
                if (!Kv.Empty(peer?.id)) _colors[peer.id] = peer.colorIndex;
    }

    public int Count => _members.Count;
    public IEnumerable<string> MemberIds => _members.Keys;

    public void AddLocal(CollabPeerInfo info)
    {
        var member = new Member { Info = info };
        _members[info.id] = member;
        AssignColor(member);
        _rosterDirty = true;
        Flush();
    }

    /// <summary>Where the local member's frames land when it is the host (Send, presence, shared).</summary>
    public void FromLocal(string localId, CollabFrame frame) => Handle(localId, frame);

    public void Join(FrameConn conn, CollabFrame join)
    {
        string id = join.id;
        if (_members.TryGetValue(id, out var old) && old.Conn != null && old.Conn != conn)
        {
            old.Conn.SuppressLoss = true;
            old.Conn.Close();
        }
        var info = new CollabPeerInfo
        {
            id = id,
            address = conn.RemoteIp,
            port = join.port,
            startedAt = join.seq,
            presence = join.presence ?? Array.Empty<CollabKV>(),
        };
        ApplyProfile(info, join.profile);
        var member = new Member { Conn = conn, Info = info };
        _members[id] = member;
        conn.PeerId = id;
        AssignColor(member);

        // A peer coming over from an old host (or a host that gave way) brings the store it had: keep what we lack.
        if (join.entries != null)
            foreach (var entry in join.entries)
                if (!Kv.Empty(entry.key) && !Store.ContainsKey(entry.key))
                {
                    Store[entry.key] = entry.value ?? "";
                    Broadcast(new CollabFrame { type = CollabFrameType.Shared, key = entry.key, json = entry.value }, except: id);
                }

        conn.Send(new CollabFrame
        {
            type = CollabFrameType.Welcome,
            id = HostId,
            host = LeadId,
            epoch = Epoch,
            entries = Kv.From(Store),
            peers = Roster(),
        });
        Log.Write($"[{Key[..Math.Min(6, Key.Length)]}] {info.name} ({id[..8]}) joined from {conn.RemoteIp}; {Count} in the session.");
        _rosterDirty = true;
        Flush();
    }

    public void Leave(string id, string why)
    {
        if (!_members.TryGetValue(id, out var member) || member.Conn == null) return;
        _members.Remove(id);
        foreach (var other in _members.Values) other.Pings.Remove(id);
        Log.Write($"{member.Info.name} ({id[..Math.Min(8, id.Length)]}) left ({why}); {Count} in the session.");
        _rosterDirty = true;
        Flush();
    }

    public void ConnectionClosed(FrameConn conn)
    {
        if (conn.PeerId != null && _members.TryGetValue(conn.PeerId, out var member) && member.Conn == conn)
            Leave(conn.PeerId, "disconnected");
    }

    public void Handle(string fromId, CollabFrame frame)
    {
        if (!_members.TryGetValue(fromId, out var member)) return;
        switch (frame.type)
        {
            case CollabFrameType.Msg:
                frame.from = fromId;
                if (!Kv.Empty(frame.to))
                {
                    if (_members.TryGetValue(frame.to, out var target) && frame.to != fromId) Deliver(target, frame);
                }
                else Broadcast(frame, except: fromId);
                break;
            case CollabFrameType.Presence:
                if (frame.presence != null) member.Info.presence = frame.presence;
                ApplyProfile(member.Info, frame.profile);
                _rosterDirty = true;
                break;
            case CollabFrameType.Shared:
                if (Kv.Empty(frame.key)) break;
                if (frame.flag) Store.Remove(frame.key);
                else Store[frame.key] = frame.json ?? "";
                // Everyone, the sender too, sees changes in the host's order.
                Broadcast(new CollabFrame { type = CollabFrameType.Shared, key = frame.key, json = frame.flag ? null : frame.json, flag = frame.flag, from = fromId });
                break;
            case CollabFrameType.Heartbeat:
                member.Pings = Kv.To(frame.entries).ToDictionary(p => p.Key, p => int.TryParse(p.Value, out int ms) ? ms : -1);
                if (member.Conn != null)
                    member.Conn.Send(new CollabFrame { type = CollabFrameType.HeartbeatAck, seq = frame.seq });
                break;
            case CollabFrameType.Leave:
                if (member.Conn != null)
                {
                    Leave(fromId, "left");
                    member.Conn.SuppressLoss = true;
                    member.Conn.CloseAfterFlush();
                }
                break;
        }
    }

    /// <summary>The local member's own ping table (the hosting helper measures it like everyone else).</summary>
    public void SetLocalPings(string localId, Dictionary<string, int> pings)
    {
        if (_members.TryGetValue(localId, out var member)) member.Pings = pings;
    }

    /// <summary>Heartbeats, timeouts and roster updates; called every 50 ms.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        foreach (var member in _members.Values.ToList())
        {
            if (member.Conn == null) continue;
            if (member.Conn.IsClosed) { Leave(member.Info.id, "disconnected"); continue; }
            if ((now - member.Conn.LastReceived).TotalMilliseconds > _timeoutMs)
            {
                Leave(member.Info.id, "timed out");
                member.Conn.SuppressLoss = true;
                member.Conn.Close();
            }
        }
        if (now >= _nextHeartbeat)
        {
            _nextHeartbeat = now.AddSeconds(1);
            Broadcast(new CollabFrame { type = CollabFrameType.Heartbeat, seq = DateTime.UtcNow.Ticks }, remoteOnly: true);
        }
        if (now >= _nextRosterCheck)
        {
            // Ping averages drift; resend when they changed (the successor choice needs everyone to agree on them).
            _nextRosterCheck = now.AddSeconds(2);
            _rosterDirty = true;
        }
        Flush();
    }

    private void Flush()
    {
        if (!_rosterDirty) return;
        _rosterDirty = false;
        var roster = Roster();
        string signature = Json.Text(roster);
        if (signature == _lastRosterSignature) return;
        _lastRosterSignature = signature;
        Broadcast(new CollabFrame { type = CollabFrameType.Roster, peers = roster, epoch = Epoch, host = LeadId });
    }

    /// <summary>Tells everyone to go to another host (when two hosts of one project find each other).</summary>
    public void RedirectAll(string address, int port)
    {
        Broadcast(new CollabFrame { type = CollabFrameType.Redirect, text = address, port = port }, remoteOnly: true);
        foreach (var member in _members.Values)
            if (member.Conn != null) { member.Conn.SuppressLoss = true; member.Conn.CloseAfterFlush(); }
    }

    public void CloseAll()
    {
        foreach (var member in _members.Values)
            if (member.Conn != null)
            {
                member.Conn.Send(new CollabFrame { type = CollabFrameType.Leave, id = HostId });
                member.Conn.SuppressLoss = true;
                member.Conn.CloseAfterFlush();
            }
    }

    public CollabPeerInfo[] Roster()
    {
        if (_isRelay && (_lead == null || !_members.ContainsKey(_lead)))
        {
            // The relay never edits, so someone else leads: the best average ping, then the lowest id. They keep the
            // role while they stay, so it doesn't flip back and forth as pings change.
            _lead = _members.Values
                .OrderBy(m => AveragePing(m.Info.id) < 0 ? int.MaxValue : AveragePing(m.Info.id))
                .ThenBy(m => m.Info.id, StringComparer.Ordinal)
                .Select(m => m.Info.id).FirstOrDefault();
        }
        var list = new List<CollabPeerInfo>();
        foreach (var member in _members.Values)
        {
            var info = member.Info;
            info.isHost = info.id == LeadId;
            info.color = CollabProtocol.Palette[info.colorIndex % CollabProtocol.Palette.Length];
            info.avgPingMs = AveragePing(info.id);
            list.Add(info);
        }
        list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return list.ToArray();
    }

    // Everything measured about this peer: its round trips to others and theirs to it. -1 when nothing is known yet.
    private int AveragePing(string id)
    {
        long sum = 0;
        int count = 0;
        foreach (var member in _members.Values)
        {
            if (member.Info.id == id)
            {
                foreach (var pair in member.Pings)
                    if (pair.Value >= 0 && _members.ContainsKey(pair.Key)) { sum += pair.Value; count++; }
            }
            else if (member.Pings.TryGetValue(id, out int ms) && ms >= 0) { sum += ms; count++; }
        }
        // Rounded to 5 ms so tiny jitter doesn't keep reshuffling the order.
        return count == 0 ? -1 : (int)(Math.Round(sum / (double)count / 5.0) * 5);
    }

    private void AssignColor(Member member)
    {
        var used = new HashSet<int>(_members.Values.Where(m => m != member).Select(m => m.Info.colorIndex));
        if (_colors.TryGetValue(member.Info.id, out int previous) && !used.Contains(previous))
        {
            member.Info.colorIndex = previous;
            return;
        }
        int index = 0;
        while (used.Contains(index)) index++;
        member.Info.colorIndex = index;
        _colors[member.Info.id] = index;
    }

    private static void ApplyProfile(CollabPeerInfo info, CollabProfile profile)
    {
        if (profile == null) return;
        if (!Kv.Empty(profile.name)) info.name = profile.name;
        if (profile.avatarId != null) info.avatarId = profile.avatarId;
        if (!Kv.Empty(profile.gitName)) info.gitName = profile.gitName;
        if (!Kv.Empty(profile.gitEmail)) info.gitEmail = profile.gitEmail;
    }

    private void Deliver(Member member, CollabFrame frame)
    {
        if (member.Conn == null) _local?.Invoke(frame);
        else member.Conn.Send(frame);
    }

    private void Broadcast(CollabFrame frame, string except = null, bool remoteOnly = false)
    {
        foreach (var member in _members.Values)
        {
            if (member.Info.id == except) continue;
            if (remoteOnly && member.Conn == null) continue;
            Deliver(member, frame);
        }
    }
}
