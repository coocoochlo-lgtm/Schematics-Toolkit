using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>
/// SchematicsCollab: the helper the Unity editor starts for live collaboration (see ../README.md).
///   --project DIR        the Unity project (git is found from here)            --state DIR    runtime files (default DIR/Library/SchematicsCollab)
///   --parent-pid N       exit when this process (the editor) exits              --product NAME Application.productName (key without git)
///   --collab DIR         the Collab~ folder (for project-id)                    --control-port N  editor port (0 = any; written to helper.json)
///   --peer-port N        TCP+UDP port for peers (0 = any)                       --discovery-port N  LAN beacon port (default 47811)
///   --peer HOST:PORT     also send beacons straight to this peer (repeatable)   --relay-address HOST:PORT  use a relay instead of the LAN
///   --key K              override the project key                               --no-git       don't touch git
///   --timeout-ms N       peer timeout (default 20000)                           --console      log to stdout too
///   --relay --port N     run as a relay for internet sessions
///   --selftest           run the multi-process self test
/// </summary>
static class Program
{
    private static readonly CancellationTokenSource Stop = new();

    public static async Task<int> Main(string[] args)
    {
        var a = new Args(args);
        if (a.Has("--selftest")) return await SelfTest.RunAsync(a);
        Log.Console = a.Has("--console");

        if (a.Has("--relay"))
        {
            Log.Console = true;
            Log.Init(a.Get("--log") ?? Path.Combine(AppContext.BaseDirectory, "relay.log"));
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };
            await new RelayServer(int.Parse(a.Get("--port") ?? "47812"), int.Parse(a.Get("--timeout-ms") ?? "20000")).RunAsync(Stop.Token);
            return 0;
        }

        string project = Path.GetFullPath(a.Get("--project") ?? Directory.GetCurrentDirectory());
        string stateDir = Path.GetFullPath(a.Get("--state") ?? Path.Combine(project, "Library", "SchematicsCollab"));
        Directory.CreateDirectory(stateDir);
        Log.Init(Path.Combine(stateDir, "collab.log"));
        string stateFile = Path.Combine(stateDir, "helper.json");

        // One helper per project: if another one is alive and answering, it's the one.
        if (OtherHelperRunning(stateFile))
        {
            Log.Write("Another helper is already running for this project; exiting.");
            return 3;
        }

        var editor = new EditorLink();
        editor.Start(int.Parse(a.Get("--control-port") ?? "0"));
        var git = new GitAgent(project, editor);
        if (!a.Has("--no-git")) await git.DetectAsync();
        if (git.HasRepo) Log.Write($"Git: {git.Info.root} on {git.Info.branch} ({git.Info.remoteUrl}), project folder '{git.Info.prefix}'.");
        else Log.Write("No git repository found for the project; asset sync and snapshots are off.");

        var options = new SessionOptions
        {
            Key = a.Get("--key") ?? git.ProjectKey(a.Get("--product") ?? Path.GetFileName(project), a.Get("--collab")),
            PeerPort = int.Parse(a.Get("--peer-port") ?? "0"),
            DiscoveryPort = int.Parse(a.Get("--discovery-port") ?? CollabProtocol.DiscoveryPort.ToString()),
            Relay = string.IsNullOrWhiteSpace(a.Get("--relay-address")) ? null : a.Get("--relay-address").Trim(),
            TimeoutMs = int.Parse(a.Get("--timeout-ms") ?? "20000"),
        };
        if (a.Get("--id") is { } id) options.Id = id;
        foreach (var seed in a.All("--peer"))
        {
            var (host, port) = Session.ParseAddress(seed);
            try
            {
                var address = IPAddress.TryParse(host, out var ip) ? ip : Dns.GetHostAddresses(host).First(x => x.AddressFamily == AddressFamily.InterNetwork);
                options.Seeds.Add(new IPEndPoint(address, port));
            }
            catch (Exception e) { Log.Write($"Ignoring --peer {seed}: {e.Message}"); }
        }
        var session = new Session(options);
        Log.Prefix = $"[{options.Id[..6]}] ";

        CollabFrame GitFrame() => new() { type = CollabFrameType.Git, git = git.Info };
        CollabProfile profile = new();

        void ApplyProfile(CollabProfile from)
        {
            if (from != null) profile = new CollabProfile { name = from.name, avatarId = from.avatarId };
            profile.gitName = git.Info.userName;
            profile.gitEmail = git.Info.userEmail;
            if (Kv.Empty(profile.name)) profile.name = !Kv.Empty(git.Info.userName) ? git.Info.userName : Environment.UserName;
            session.SetProfile(profile);
        }

        session.ToEditor = frame => editor.Emit(frame, sequenced: frame.type != CollabFrameType.SharedAll);
        session.StateChanged = () => editor.Emit(session.StateFrame(), sequenced: false);
        session.SystemMessage = frame =>
        {
            if (frame.channel != CollabProtocol.AssetChannel || Kv.Empty(frame.json)) return;
            CollabFrame announcement;
            try { announcement = Json.Parse<CollabFrame>(frame.json); }
            catch { return; }
            if (Kv.Empty(announcement?.key) || Kv.Empty(announcement.text)) return;
            editor.Emit(new CollabFrame { type = CollabFrameType.Announced, key = announcement.key, text = announcement.text, bounds = announcement.bounds, from = frame.from });
            _ = Task.Run(() => git.AddPending(announcement.key, announcement.text));
        };
        editor.SnapshotBefore = () => new[] { session.StateFrame(), GitFrame() };
        editor.SnapshotAfter = () => new[]
        {
            new CollabFrame { type = CollabFrameType.SharedAll, entries = Kv.From(session.Store) },
            new CollabFrame { type = CollabFrameType.PendingAll, entries = git.PendingEntries() },
        };
        git.InfoChanged = () =>
        {
            editor.Emit(GitFrame(), sequenced: false);
            lock (Gate.Lock) ApplyProfile(null);
        };
        git.SessionIsShared = () => session.InSession && session.PeerCount > 1;

        editor.Received = frame =>
        {
            switch (frame.type)
            {
                case CollabFrameType.Hello:
                    ApplyProfile(frame.profile);
                    ApplySettings(git, frame.entries);
                    if (frame.paths != null) git.LiveScenes = new HashSet<string>(frame.paths);
                    break;
                case CollabFrameType.Profile:
                    ApplyProfile(frame.profile);
                    break;
                case CollabFrameType.Settings:
                    ApplySettings(git, frame.entries);
                    break;
                case CollabFrameType.Presence:
                    session.SetPresence(frame.key, frame.flag ? null : frame.json ?? "");
                    break;
                case CollabFrameType.Send:
                    if (!Kv.Empty(frame.channel) && !frame.channel.StartsWith(CollabProtocol.SystemChannelPrefix, StringComparison.Ordinal))
                        session.Send(frame.channel, frame.json, frame.to);
                    break;
                case CollabFrameType.Shared:
                    session.SetShared(frame.key, frame.json, frame.flag);
                    break;
                case CollabFrameType.LiveScenes:
                    git.LiveScenes = new HashSet<string>(frame.paths ?? Array.Empty<string>());
                    break;
                case CollabFrameType.PullNow:
                    git.PullSoon();
                    break;
                case CollabFrameType.Announce:
                    if (Kv.Empty(frame.key) || Kv.Empty(frame.text)) break;
                    // Tell everyone first (they show a placeholder at once), then commit and push.
                    session.Send(CollabProtocol.AssetChannel, Json.Text(new CollabFrame { key = frame.key, text = frame.text, bounds = frame.bounds }), null);
                    var paths = (frame.paths ?? Array.Empty<string>()).Append(frame.text)
                        .SelectMany(p => p.EndsWith(".meta") ? new[] { p } : new[] { p, p + ".meta" }).Distinct().ToArray();
                    _ = Task.Run(() => git.AnnounceAsync(frame.text, paths));
                    break;
                case CollabFrameType.CommitScene:
                    if (Kv.Empty(frame.text)) break;
                    _ = Task.Run(() => git.CommitSceneAsync(frame.text, frame.json, frame.coAuthors));
                    break;
                case CollabFrameType.Shutdown:
                    Log.Write("The editor asked the helper to stop.");
                    Stop.Cancel();
                    break;
            }
        };

        lock (Gate.Lock) ApplyProfile(null);
        session.Start();
        File.WriteAllText(stateFile, Json.Text(new CollabHelperState
        {
            pid = Environment.ProcessId,
            controlPort = editor.Port,
            peerPort = session.PeerPort,
            version = CollabProtocol.Version,
            instance = editor.Instance,
        }));
        Log.Write($"Helper ready: editor port {editor.Port}, peer port {session.PeerPort}, project {project}.");

        if (git.HasRepo) _ = Task.Run(() => git.PollLoopAsync(Stop.Token));
        if (int.TryParse(a.Get("--parent-pid"), out int parentPid)) _ = Task.Run(() => WatchParent(parentPid));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop.Cancel();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };

        try { await Task.Delay(Timeout.Infinite, Stop.Token); } catch (OperationCanceledException) { }
        session.Stop();
        try
        {
            // Only remove the state file if it's still ours.
            if (File.Exists(stateFile) && Json.Parse<CollabHelperState>(File.ReadAllText(stateFile))?.pid == Environment.ProcessId)
                File.Delete(stateFile);
        }
        catch { }
        Log.Write("Helper stopped.");
        return 0;
    }

    private static void ApplySettings(GitAgent git, CollabKV[] entries)
    {
        var map = Kv.To(entries);
        if (map.TryGetValue("gitPoll", out var poll) && int.TryParse(poll, out int seconds)) git.PollSeconds = Math.Clamp(seconds, 5, 3600);
        if (map.TryGetValue("autoPull", out var pull)) git.AutoPull = pull == "1" || pull.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (map.TryGetValue("autoPush", out var push)) git.AutoPush = push == "1" || push.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WatchParent(int pid)
    {
        while (!Stop.IsCancellationRequested)
        {
            await Task.Delay(2000);
            bool alive;
            try { using var parent = Process.GetProcessById(pid); alive = !parent.HasExited; }
            catch { alive = false; }
            if (!alive)
            {
                Log.Write("The editor exited; stopping.");
                Stop.Cancel();
                return;
            }
        }
    }

    private static bool OtherHelperRunning(string stateFile)
    {
        try
        {
            if (!File.Exists(stateFile)) return false;
            var state = Json.Parse<CollabHelperState>(File.ReadAllText(stateFile));
            if (state == null || state.pid == Environment.ProcessId) return false;
            using var other = Process.GetProcessById(state.pid);
            if (other.HasExited) return false;
            using var client = new TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, state.controlPort).Wait(1000) && client.Connected;
        }
        catch { return false; }
    }
}

sealed class Args
{
    private readonly string[] _args;
    public Args(string[] args) { _args = args; }
    public bool Has(string name) => _args.Contains(name);
    public string Get(string name)
    {
        int i = Array.LastIndexOf(_args, name);
        return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : null;
    }
    public IEnumerable<string> All(string name)
    {
        for (int i = 0; i < _args.Length - 1; i++)
            if (_args[i] == name) yield return _args[i + 1];
    }
}
