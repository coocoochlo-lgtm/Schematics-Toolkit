using System.Net;
using System.Net.Sockets;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>
/// `SchematicsCollab --relay --port N`: a stand-in host for sessions across the internet. Run it somewhere everyone can
/// reach (a server, or a machine with the port forwarded); each editor sets Preferences > Schematics > Collaboration >
/// Relay Address to host:N. One room per project key; the relay never leaves, so there's no host migration, and one of
/// the members leads (the peer editors see as host).
/// </summary>
sealed class RelayServer
{
    private readonly int _port;
    private readonly int _timeoutMs;
    private readonly Dictionary<string, HostRoom> _rooms = new();

    public RelayServer(int port, int timeoutMs)
    {
        _port = port;
        _timeoutMs = timeoutMs;
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        Log.Write($"Relay listening on TCP port {_port}.");
        _ = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(50);
                lock (Gate.Lock)
                {
                    foreach (var pair in _rooms.ToList())
                    {
                        pair.Value.Tick();
                        if (pair.Value.Count == 0) _rooms.Remove(pair.Key);
                    }
                }
            }
        });
        using var registration = stop.Register(() => listener.Stop());
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop); }
            catch { break; }
            var conn = new FrameConn(client);
            conn.Received = OnFrame;
            conn.Closed = c =>
            {
                lock (Gate.Lock)
                    foreach (var room in _rooms.Values) room.ConnectionClosed(c);
            };
            conn.Start();
        }
    }

    private readonly Dictionary<FrameConn, HostRoom> _roomOf = new();

    private void OnFrame(FrameConn conn, CollabFrame frame)
    {
        lock (Gate.Lock)
        {
            if (conn.PeerId == null)
            {
                if (frame.type != CollabFrameType.Join || Kv.Empty(frame.key) || Kv.Empty(frame.id)) { conn.Close(); return; }
                if (!_rooms.TryGetValue(frame.key, out var room))
                {
                    room = new HostRoom(frame.key, 1, "relay", null, _timeoutMs, null, null);
                    _rooms[frame.key] = room;
                    Log.Write($"New relayed session for project {frame.key}.");
                }
                _roomOf[conn] = room;
                room.Join(conn, frame);
                return;
            }
            if (_roomOf.TryGetValue(conn, out var current)) current.Handle(conn.PeerId, frame);
            if (conn.IsClosed) _roomOf.Remove(conn);
        }
    }
}
