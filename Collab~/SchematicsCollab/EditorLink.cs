using System.Net;
using System.Net.Sockets;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>
/// The localhost connection to the Unity editor. The editor disconnects on every script reload and reconnects a moment
/// later; meanwhile events keep their sequence numbers in a buffer, and the editor's hello says the last one it handled,
/// so it gets exactly what it missed. Snapshots (state, shared store, git, pending assets) are resent on every hello.
/// </summary>
sealed class EditorLink
{
    private const int BufferLimit = 20000;
    private TcpListener _listener;
    private FrameConn _conn;
    private bool _greeted;
    private long _seq;
    private readonly LinkedList<CollabFrame> _buffer = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pullAcks = new();

    public readonly string Instance = Guid.NewGuid().ToString("N");
    public int Port { get; private set; }

    /// <summary>Frames from the editor (called under <see cref="Gate.Lock"/>).</summary>
    public Action<CollabFrame> Received;
    /// <summary>Called under the lock to send snapshots before (state) and after (store, git, pending) the replay.</summary>
    public Func<IEnumerable<CollabFrame>> SnapshotBefore, SnapshotAfter;

    public bool Attached
    {
        get { lock (Gate.Lock) return _conn != null && _greeted && !_conn.IsClosed; }
    }

    public void Start(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    private async Task AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch { return; }
            var conn = new FrameConn(client);
            conn.Received = OnFrame;
            conn.Closed = c =>
            {
                lock (Gate.Lock)
                    if (_conn == c) { _conn = null; _greeted = false; Log.Write("Editor disconnected."); }
            };
            lock (Gate.Lock)
            {
                // The newest editor connection wins (the old one is from before a script reload).
                var old = _conn;
                _conn = conn;
                _greeted = false;
                if (old != null) { old.Closed = null; old.Close(); }
            }
            conn.Start();
        }
    }

    private void OnFrame(FrameConn conn, CollabFrame frame)
    {
        lock (Gate.Lock)
        {
            if (conn != _conn) return;
            if (frame.type == CollabFrameType.Hello)
            {
                Greet(conn, frame);
                return;
            }
            if (frame.type == CollabFrameType.PullReady && frame.id != null && _pullAcks.TryGetValue(frame.id, out var ack))
            {
                ack.TrySetResult(true);
                return;
            }
            Received?.Invoke(frame);
        }
    }

    private void Greet(FrameConn conn, CollabFrame hello)
    {
        long oldest = _buffer.First?.Value.seq ?? _seq + 1;
        bool sameHelper = hello.id == Instance;
        // Everything after its last seq is still buffered: replay it. Otherwise it starts over from the snapshots.
        bool canReplay = sameHelper && hello.seq + 1 >= oldest;
        conn.Send(new CollabFrame { type = CollabFrameType.Hello, id = Instance, seq = _seq, flag = !canReplay, count = CollabProtocol.Version });
        foreach (var frame in SnapshotBefore?.Invoke() ?? Enumerable.Empty<CollabFrame>()) conn.Send(frame);
        int replayed = 0;
        if (canReplay)
            foreach (var frame in _buffer)
                if (frame.seq > hello.seq) { conn.Send(frame); replayed++; }
        foreach (var frame in SnapshotAfter?.Invoke() ?? Enumerable.Empty<CollabFrame>()) conn.Send(frame);
        _greeted = true;
        Log.Write($"Editor connected ({(canReplay ? $"replayed {replayed} missed events" : "fresh start")}).");
        Received?.Invoke(hello);
    }

    /// <summary>Sends to the editor; sequenced events are buffered for replay after a reload.</summary>
    public void Emit(CollabFrame frame, bool sequenced = true)
    {
        lock (Gate.Lock)
        {
            if (sequenced)
            {
                frame.seq = ++_seq;
                _buffer.AddLast(frame);
                while (_buffer.Count > BufferLimit) _buffer.RemoveFirst();
            }
            if (_conn != null && _greeted) _conn.Send(frame);
        }
    }

    public void Info(string text) => Emit(new CollabFrame { type = CollabFrameType.Log, text = text });
    public void Warn(string text) { Log.Write("WARNING " + text); Emit(new CollabFrame { type = CollabFrameType.Log, text = text, key = "warning" }); }
    public void Error(string text) { Log.Write("ERROR " + text); Emit(new CollabFrame { type = CollabFrameType.Log, text = text, flag = true }); }

    /// <summary>
    /// Asks the editor to hold off importing (AssetDatabase.DisallowAutoRefresh) while git rewrites files. False when no
    /// editor is attached or it doesn't answer: the pull is postponed rather than done behind its back.
    /// </summary>
    public async Task<bool> BeginPullAsync(string id, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!Attached && DateTime.UtcNow < deadline) await Task.Delay(100);
        if (!Attached) return false;
        var ack = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate.Lock) _pullAcks[id] = ack;
        Emit(new CollabFrame { type = CollabFrameType.PullBegin, id = id });
        var done = await Task.WhenAny(ack.Task, Task.Delay(Math.Max(1000, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)));
        lock (Gate.Lock) _pullAcks.Remove(id);
        if (done == ack.Task) return true;
        // Balance the editor's side in case it does see the begin later.
        Emit(new CollabFrame { type = CollabFrameType.PullEnd, id = id, text = "postponed" });
        return false;
    }

    public void EndPull(string id, string[] paths, string error) =>
        Emit(new CollabFrame { type = CollabFrameType.PullEnd, id = id, paths = paths ?? Array.Empty<string>(), text = error });
}
