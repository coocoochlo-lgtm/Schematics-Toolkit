using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>Everything that touches session state runs under this one lock: simple, and plenty fast for a few peers.</summary>
static class Gate
{
    public static readonly object Lock = new();
}

static class Log
{
    private static readonly object _file = new();
    private static string _path;
    public static bool Console;
    public static string Prefix = "";

    public static void Init(string path)
    {
        _path = path;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Keep the last run's log next to this one, and the log small.
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
                File.Move(path, path + ".old", true);
        }
        catch { }
    }

    public static void Write(string line)
    {
        string stamped = $"{DateTime.Now:HH:mm:ss.fff} {Prefix}{line}";
        if (Console) System.Console.WriteLine(stamped);
        if (_path == null) return;
        lock (_file)
        {
            try { File.AppendAllText(_path, stamped + Environment.NewLine); }
            catch { }
        }
    }
}

static class Json
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Bytes(CollabFrame frame) => JsonSerializer.SerializeToUtf8Bytes(frame, Options);
    public static CollabFrame Frame(ReadOnlySpan<byte> bytes) => JsonSerializer.Deserialize<CollabFrame>(bytes, Options);
    public static string Text<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Parse<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
}

static class Kv
{
    public static CollabKV[] From(IEnumerable<KeyValuePair<string, string>> pairs) =>
        pairs.Select(p => new CollabKV { key = p.Key, value = p.Value }).ToArray();

    public static Dictionary<string, string> To(CollabKV[] entries)
    {
        var map = new Dictionary<string, string>();
        if (entries == null) return map;
        foreach (var e in entries)
            if (!string.IsNullOrEmpty(e?.key)) map[e.key] = e.value ?? "";
        return map;
    }

    public static bool Empty(string s) => string.IsNullOrEmpty(s);
}

/// <summary>
/// One TCP connection carrying length-prefixed JSON frames. Reading runs on its own task; sending is queued and written
/// by one writer task, so callers never block (even while holding <see cref="Gate.Lock"/>) and order is kept.
/// </summary>
sealed class FrameConn
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<byte[]> _out = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private int _closed;

    public readonly string RemoteIp;
    public DateTime LastReceived = DateTime.UtcNow;
    public string PeerId;          // set once the other side said who it is
    public bool SuppressLoss;      // closed on purpose: don't treat it as the host vanishing
    public Action<FrameConn, CollabFrame> Received;
    public Action<FrameConn> Closed;

    public FrameConn(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        RemoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? "";
    }

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public void Start()
    {
        _ = Task.Run(ReadLoop);
        _ = Task.Run(WriteLoop);
    }

    public void Send(CollabFrame frame)
    {
        if (IsClosed) return;
        _out.Writer.TryWrite(Json.Bytes(frame));
    }

    private async Task WriteLoop()
    {
        var header = new byte[4];
        try
        {
            await foreach (var body in _out.Reader.ReadAllAsync())
            {
                BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
                await _stream.WriteAsync(header);
                await _stream.WriteAsync(body);
            }
        }
        catch { }
        Close();
    }

    private async Task ReadLoop()
    {
        var header = new byte[4];
        try
        {
            while (true)
            {
                await _stream.ReadExactlyAsync(header);
                int length = BinaryPrimitives.ReadInt32BigEndian(header);
                if (length < 0 || length > CollabProtocol.MaxFrameBytes)
                    throw new InvalidDataException($"frame of {length} bytes");
                var body = new byte[length];
                await _stream.ReadExactlyAsync(body);
                LastReceived = DateTime.UtcNow;
                CollabFrame frame;
                try { frame = Json.Frame(body); }
                catch (Exception e) { Log.Write($"Bad frame from {RemoteIp}: {e.Message}"); continue; }
                if (frame?.type == null) continue;
                try { Received?.Invoke(this, frame); }
                catch (Exception e) { Log.Write($"Handling {frame.type} failed: {e}"); }
            }
        }
        catch { }
        Close();
    }

    /// <summary>Sends what's queued (briefly), then closes.</summary>
    public void CloseAfterFlush()
    {
        _out.Writer.TryComplete();
        _ = Task.Delay(300).ContinueWith(_ => Close());
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _out.Writer.TryComplete();
        try { _client.Close(); } catch { }
        try { Closed?.Invoke(this); }
        catch (Exception e) { Log.Write($"Close handler failed: {e}"); }
    }

    public static async Task<FrameConn> ConnectAsync(string host, int port, int timeoutMs)
    {
        var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return new FrameConn(client);
        }
        catch
        {
            client.Dispose();
            return null;
        }
    }
}
