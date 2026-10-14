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
///   --id ID              override the peer id (default: Library/SchematicsCollab/peer-id, created once per project folder)
///   --editor-wait-s N    exit when no editor attached within N s of starting (default 120; 0 = wait forever)
///   --editor-idle-s N    exit when the editor has been gone for N s (default 600; 0 = never)
///   --orphan-timeout-ms N  host: remove a pre-protocol-3 helper that never published presence after N ms (default 90000)
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

        // One helper per project. The lock file is taken before anything else (it's released by the OS when the
        // process ends, however it ends), so helpers started at the same moment can't both get past this point: older
        // editors started a new helper every half second until the first one wrote helper.json, and every extra one
        // joined the session as another copy of the same person.
        FileStream instanceLock;
        try
        {
            instanceLock = new FileStream(Path.Combine(stateDir, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Log.Write("Another helper is already running for this project (helper.lock is taken); exiting.");
            return 3;
        }
        if (OtherHelperRunning(stateFile))
        {
            Log.Write("Another helper is already running for this project; exiting.");
            return 3;
        }

        var editor = new EditorLink();
        editor.Start(int.Parse(a.Get("--control-port") ?? "0"));
        // From here on every warning and error also reaches the Unity Console (with repeats folded).
        editor.ForwardLogs();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error($"The helper crashed: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Exception("A background task failed", e.Exception.GetBaseException()); e.SetObserved(); };
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
            OrphanTimeoutMs = int.Parse(a.Get("--orphan-timeout-ms") ?? "90000"),
        };
        // The same id every time this project folder's helper runs, so a restarted helper (editor restart, rebuild,
        // crash) takes its own place in the session again instead of appearing as one more person.
        options.Id = a.Get("--id") ?? StablePeerId(stateDir);
        foreach (var seed in a.All("--peer"))
        {
            var (host, port) = Session.ParseAddress(seed);
            try
            {
                var address = IPAddress.TryParse(host, out var ip) ? ip : Dns.GetHostAddresses(host).First(x => x.AddressFamily == AddressFamily.InterNetwork);
                options.Seeds.Add(new IPEndPoint(address, port));
            }
            catch (Exception e) { Log.Warn($"Ignoring --peer {seed}: {e.Message}"); }
        }
        var session = new Session(options);
        Log.Prefix = $"[{options.Id[..6]}] ";

        CollabFrame GitFrame() => new() { type = CollabFrameType.Git, git = git.Info };
        CollabProfile profile = new();

        void ApplyProfile(CollabProfile from)
        {
            if (from != null) profile = new CollabProfile { name = from.name, avatarId = from.avatarId, role = Kv.Empty(from.role) ? CollabProtocol.RoleEditor : from.role };
            profile.client = CollabProtocol.ClientEditor;
            profile.version = CollabProtocol.Version;
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
            catch (Exception e) { Log.Warn($"Ignored a malformed asset announcement from {frame.from} ({e.Message})."); return; }
            if (Kv.Empty(announcement?.key) || Kv.Empty(announcement.text)) return;
            editor.Emit(new CollabFrame { type = CollabFrameType.Announced, key = announcement.key, text = announcement.text, bounds = announcement.bounds, from = frame.from });
            Background("Tracking an announced asset", () => { git.AddPending(announcement.key, announcement.text); return Task.CompletedTask; });
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
                    Background($"Sharing {frame.text} through git", () => git.AnnounceAsync(frame.text, paths));
                    break;
                case CollabFrameType.CommitScene:
                    if (Kv.Empty(frame.text)) break;
                    Background($"Committing {frame.text}", () => git.CommitSceneAsync(frame.text, frame.json, frame.coAuthors));
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
            key = options.Key,
        }));
        Log.Write($"Helper ready: editor port {editor.Port}, peer port {session.PeerPort}, project {project}.");

        if (git.HasRepo) Background("The git poll loop", () => git.PollLoopAsync(Stop.Token));
        if (int.TryParse(a.Get("--parent-pid"), out int parentPid)) _ = Task.Run(() => WatchParent(parentPid));
        _ = Task.Run(() => WatchEditor(editor, int.Parse(a.Get("--editor-wait-s") ?? "120"), int.Parse(a.Get("--editor-idle-s") ?? "600")));
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
        GC.KeepAlive(instanceLock);
        instanceLock.Dispose();
        return 0;
    }

    /// <summary>Runs work in the background; if it throws, the error reaches the log and the Unity Console.</summary>
    private static void Background(string what, Func<Task> work)
    {
        _ = Task.Run(async () =>
        {
            try { await work(); }
            catch (OperationCanceledException) when (Stop.IsCancellationRequested) { }
            catch (Exception e) { Log.Exception(what + " failed", e); }
        });
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

    /// <summary>
    /// This project folder's peer id: made once and kept in the state folder (Library/SchematicsCollab/peer-id, so a
    /// copy of the project elsewhere, which has its own Library, gets its own id).
    /// </summary>
    private static string StablePeerId(string stateDir)
    {
        string file = Path.Combine(stateDir, "peer-id");
        try
        {
            if (File.Exists(file))
            {
                string existing = File.ReadAllText(file).Trim();
                if (existing.Length >= 16 && existing.All(char.IsLetterOrDigit)) return existing;
            }
            string id = Guid.NewGuid().ToString("N");
            File.WriteAllText(file, id + "\n");
            return id;
        }
        catch (Exception e)
        {
            Log.Warn($"Couldn't keep a peer id in {file} ({e.Message}); using a new one for this run.");
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// A helper nobody uses leaves: one no editor attached to within <paramref name="waitSeconds"/> of starting, or whose
    /// editor has been gone for <paramref name="idleSeconds"/> (script reloads take seconds; this is for helpers left
    /// behind). The editor starts a new one whenever it needs one.
    /// </summary>
    private static async Task WatchEditor(EditorLink editor, int waitSeconds, int idleSeconds)
    {
        var started = DateTime.UtcNow;
        DateTime? lastSeen = null;
        while (!Stop.IsCancellationRequested)
        {
            await Task.Delay(1000);
            var now = DateTime.UtcNow;
            if (editor.Attached) { lastSeen = now; continue; }
            if (lastSeen == null && waitSeconds > 0 && (now - started).TotalSeconds > waitSeconds)
            {
                Log.Write($"No editor attached within {waitSeconds} s; stopping.");
                Stop.Cancel();
                return;
            }
            if (lastSeen != null && idleSeconds > 0 && (now - lastSeen.Value).TotalSeconds > idleSeconds)
            {
                Log.Write($"The editor has been gone for {idleSeconds} s; stopping.");
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
