using System.Diagnostics;
using System.Text;
using Remedy.Schematics.Collaboration;

namespace SchematicsCollab;

/// <summary>
/// `SchematicsCollab --selftest [--keep] [--work DIR]`: starts real helper processes on localhost, plays the editor for
/// each one over its control port, and checks host election, fan-out, presence, the shared store, replay after an
/// editor reconnect, host migration when the host is killed, rejoining, a graceful host exit, LAN discovery, and the git
/// automation against a throwaway bare repository.
/// </summary>
static class SelfTest
{
    private static int _passed, _failed;
    private static readonly List<string> Failures = new();
    private static string _work;
    private static readonly List<Process> Processes = new();

    public static async Task<int> RunAsync(Args args)
    {
        Log.Console = false;
        _work = Path.GetFullPath(args.Get("--work") ?? Path.Combine(Path.GetTempPath(), "schematics-collab-selftest-" + Environment.ProcessId));
        if (Directory.Exists(_work)) Directory.Delete(_work, true);
        Directory.CreateDirectory(_work);
        Console.WriteLine($"Self test, working in {_work}");
        int basePort = 42000 + Random.Shared.Next(0, 400) * 10;
        try
        {
            await SessionTests(basePort);
            await DiscoveryTest(basePort + 100);
            await RelayTest(basePort + 200);
            await GitTests(basePort + 300);
        }
        catch (Exception e)
        {
            Fail($"test crashed: {e}");
        }
        finally
        {
            foreach (var p in Processes) { try { if (!p.HasExited) p.Kill(true); } catch { } }
        }
        Console.WriteLine();
        Console.WriteLine($"{_passed} passed, {_failed} failed");
        foreach (var f in Failures) Console.WriteLine("  FAILED: " + f);
        if (!args.Has("--keep") && _failed == 0) { try { Directory.Delete(_work, true); } catch { } }
        else Console.WriteLine($"Logs kept in {_work}");
        return _failed == 0 ? 0 : 1;
    }

    #region Harness

    private static void Pass(string what) { _passed++; Console.WriteLine("  ok   " + what); }
    private static void Fail(string what) { _failed++; Failures.Add(what); Console.WriteLine("  FAIL " + what); }
    private static void Check(bool condition, string what) { if (condition) Pass(what); else Fail(what); }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(100);
        }
        return condition();
    }

    /// <summary>A helper process plus a fake editor attached to it.</summary>
    sealed class Node
    {
        public string Name, StateDir;
        public Process Process;
        public FakeEditor Editor;
        public int PeerPort;
        public string Id => Editor.State?.id;
        public override string ToString() => Name;
    }

    private static async Task<Node> StartNode(string name, string project, int peerPort, int discoveryPort, string key, IEnumerable<int> seeds, params string[] extra)
    {
        string state = Path.Combine(_work, "state-" + name);
        Directory.CreateDirectory(state);
        var arguments = new List<string> { "--project", project, "--state", state, "--peer-port", peerPort.ToString(), "--discovery-port", discoveryPort.ToString(), "--timeout-ms", "6000", "--parent-pid", Environment.ProcessId.ToString() };
        if (key != null) { arguments.Add("--key"); arguments.Add(key); }
        foreach (var seed in seeds) { arguments.Add("--peer"); arguments.Add($"127.0.0.1:{seed}"); }
        arguments.AddRange(extra);
        var process = Launch(arguments);
        CollabHelperState helper = null;
        await Until(() =>
        {
            try
            {
                string file = Path.Combine(state, "helper.json");
                if (!File.Exists(file)) return false;
                helper = Json.Parse<CollabHelperState>(File.ReadAllText(file));
                return helper?.pid == process.Id;
            }
            catch { return false; }
        }, 15000);
        if (helper?.pid != process.Id) throw new Exception($"{name} didn't start (see {state}/collab.log)");
        var node = new Node { Name = name, StateDir = state, Process = process, PeerPort = peerPort, Editor = new FakeEditor(name, helper.controlPort) };
        await node.Editor.ConnectAsync();
        return node;
    }

    private static Process Launch(List<string> arguments)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine("  [helper stderr] " + e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        Processes.Add(process);
        return process;
    }

    /// <summary>Plays the Unity editor: hello with the last seq, acks pulls, records everything.</summary>
    sealed class FakeEditor
    {
        public readonly string Name;
        public readonly int Port;
        private FrameConn _conn;
        public string Instance;
        public long LastSeq;
        public CollabFrame State;
        public CollabGitInfo Git;
        public readonly List<CollabFrame> Frames = new();
        public readonly Dictionary<string, string> Shared = new();
        public string[] LiveScenes = Array.Empty<string>();
        public bool AckPulls = true;

        public FakeEditor(string name, int port) { Name = name; Port = port; }

        public async Task ConnectAsync()
        {
            _conn = await FrameConn.ConnectAsync("127.0.0.1", Port, 3000) ?? throw new Exception($"{Name}: editor port {Port} refused");
            _conn.Received = OnFrame;
            _conn.Start();
            _conn.Send(new CollabFrame
            {
                type = CollabFrameType.Hello,
                id = Instance,
                seq = LastSeq,
                profile = new CollabProfile { name = Name, avatarId = "robot" },
                paths = LiveScenes,
                entries = new[] { new CollabKV { key = "gitPoll", value = "5" }, new CollabKV { key = "autoPull", value = "1" }, new CollabKV { key = "autoPush", value = "1" } },
            });
        }

        public void Disconnect() => _conn.Close();

        private void OnFrame(FrameConn conn, CollabFrame frame)
        {
            lock (this)
            {
                Frames.Add(frame);
                if (frame.seq > 0 && frame.type != CollabFrameType.Hello) LastSeq = Math.Max(LastSeq, frame.seq);
                switch (frame.type)
                {
                    case CollabFrameType.Hello: Instance = frame.id; break;
                    case CollabFrameType.State: State = frame; break;
                    case CollabFrameType.Git: Git = frame.git; break;
                    case CollabFrameType.Shared:
                        if (frame.flag) Shared.Remove(frame.key); else Shared[frame.key] = frame.json;
                        break;
                    case CollabFrameType.SharedAll:
                        Shared.Clear();
                        foreach (var e in frame.entries ?? Array.Empty<CollabKV>()) Shared[e.key] = e.value;
                        break;
                    case CollabFrameType.PullBegin:
                        if (AckPulls) conn.Send(new CollabFrame { type = CollabFrameType.PullReady, id = frame.id });
                        break;
                }
            }
        }

        public void Send(CollabFrame frame) => _conn.Send(frame);

        public List<CollabFrame> Of(string type, string channel = null)
        {
            lock (this) return Frames.Where(f => f.type == type && (channel == null || f.channel == channel)).ToList();
        }

        public CollabPeerInfo[] Peers { get { lock (this) return State?.peers ?? Array.Empty<CollabPeerInfo>(); } }
        public bool InSession { get { lock (this) return State?.flag == true; } }
        public string HostId { get { lock (this) return State?.host; } }
        public string SharedValue(string key) { lock (this) return Shared.TryGetValue(key, out var v) ? v : null; }
    }

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"git {string.Join(' ', args)} failed in {dir}: {output}");
        return output.Trim();
    }

    #endregion

    #region Session

    private static async Task SessionTests(int port)
    {
        Console.WriteLine("Session: election, fan-out, presence, shared store, replay, migration");
        int discovery = port + 50;
        string key = "selftest-" + Guid.NewGuid().ToString("N")[..8];
        string project = Path.Combine(_work, "nogit-project");
        Directory.CreateDirectory(project);
        int[] ports = { port, port + 1, port + 2, port + 3 };
        var a = await StartNode("A", project, ports[0], discovery, key, new[] { ports[1], ports[2], ports[3] }, "--no-git");
        await Task.Delay(800);
        var b = await StartNode("B", project, ports[1], discovery, key, new[] { ports[0], ports[2], ports[3] }, "--no-git");
        var c = await StartNode("C", project, ports[2], discovery, key, new[] { ports[0], ports[1], ports[3] }, "--no-git");
        var nodes = new[] { a, b, c };

        bool formed = await Until(() => nodes.All(n => n.Editor.InSession && n.Editor.Peers.Length == 3 && n.Editor.HostId == a.Id && a.Id != null), 15000);
        Check(formed, "3 helpers form one session; the first one up (A) hosts");
        if (!formed) { foreach (var n in nodes) Console.WriteLine($"    {n}: {Json.Text(n.Editor.State)}"); return; }
        Check(nodes.All(n => n.Editor.Peers.Count(p => p.isHost) == 1), "everyone sees exactly one host");
        var colors = a.Editor.Peers.Select(p => p.color).ToList();
        Check(colors.Distinct().Count() == 3, $"colours are unique ({string.Join(", ", colors)})");
        Check(b.Editor.Peers.Any(p => p.id == c.Id && p.name == "C" && p.avatarId == "robot"), "profiles (name, avatar) replicate");

        // Presence
        b.Editor.Send(new CollabFrame { type = CollabFrameType.Presence, key = "camera", json = "{\"x\":1}" });
        Check(await Until(() => new[] { a, c }.All(n => n.Editor.Peers.FirstOrDefault(p => p.id == b.Id)?.presence?.Any(kv => kv.key == "camera" && kv.value == "{\"x\":1}") == true), 5000),
            "presence set by B reaches A and C");

        // Fan-out and targeted messages
        a.Editor.Send(new CollabFrame { type = CollabFrameType.Send, channel = "chat", json = "{\"text\":\"hi\"}" });
        Check(await Until(() => b.Editor.Of(CollabFrameType.Msg, "chat").Count == 1 && c.Editor.Of(CollabFrameType.Msg, "chat").Count == 1, 5000),
            "a message from A fans out to B and C");
        Check(b.Editor.Of(CollabFrameType.Msg, "chat")[0].from == a.Id, "messages carry the sender id");
        Check(a.Editor.Of(CollabFrameType.Msg, "chat").Count == 0, "the sender doesn't get its own message back");
        c.Editor.Send(new CollabFrame { type = CollabFrameType.Send, channel = "dm", json = "1", to = b.Id });
        await Until(() => b.Editor.Of(CollabFrameType.Msg, "dm").Count == 1, 5000);
        await Task.Delay(300);
        Check(b.Editor.Of(CollabFrameType.Msg, "dm").Count == 1 && a.Editor.Of(CollabFrameType.Msg, "dm").Count == 0, "a targeted message reaches only its target");
        for (int i = 0; i < 50; i++) b.Editor.Send(new CollabFrame { type = CollabFrameType.Send, channel = "order", json = i.ToString() });
        await Until(() => c.Editor.Of(CollabFrameType.Msg, "order").Count == 50, 5000);
        Check(c.Editor.Of(CollabFrameType.Msg, "order").Select(f => int.Parse(f.json)).SequenceEqual(Enumerable.Range(0, 50)), "50 messages arrive complete and in order");

        // Shared store
        a.Editor.Send(new CollabFrame { type = CollabFrameType.Shared, key = "scene", json = "{\"v\":1}" });
        b.Editor.Send(new CollabFrame { type = CollabFrameType.Shared, key = "k2", json = "\"from B\"" });
        Check(await Until(() => nodes.All(n => n.Editor.SharedValue("scene") == "{\"v\":1}" && n.Editor.SharedValue("k2") == "\"from B\""), 5000),
            "shared store values (set by host and by a peer) replicate to everyone");
        c.Editor.Send(new CollabFrame { type = CollabFrameType.Shared, key = "k2", flag = true });
        Check(await Until(() => nodes.All(n => n.Editor.SharedValue("k2") == null), 5000), "removing a shared key replicates");
        b.Editor.Send(new CollabFrame { type = CollabFrameType.Shared, key = "k2", json = "\"again\"" });
        await Until(() => nodes.All(n => n.Editor.SharedValue("k2") == "\"again\""), 5000);

        // Pings
        Check(await Until(() => a.Editor.Peers.All(p => p.avgPingMs >= 0) && b.Editor.Peers.Where(p => p.id != b.Id).All(p => p.pingMs >= 0), 10000),
            $"round trips are measured (avg: {string.Join(", ", a.Editor.Peers.Select(p => $"{p.name}={p.avgPingMs}ms"))}; B sees {string.Join(", ", b.Editor.Peers.Select(p => $"{p.name}={p.pingMs}ms@{p.address}:{p.port}"))})");

        // Editor reconnect (a script reload): missed messages are replayed, once.
        b.Editor.Disconnect();
        await Task.Delay(300);
        for (int i = 0; i < 3; i++) c.Editor.Send(new CollabFrame { type = CollabFrameType.Send, channel = "replay", json = i.ToString() });
        await Until(() => a.Editor.Of(CollabFrameType.Msg, "replay").Count == 3, 5000);
        await Task.Delay(300);
        int before = b.Editor.Of(CollabFrameType.Msg, "replay").Count;
        await b.Editor.ConnectAsync();
        Check(await Until(() => b.Editor.Of(CollabFrameType.Msg, "replay").Count == 3, 5000) && before == 0,
            "an editor that reconnects (domain reload) gets the 3 messages it missed");
        await Task.Delay(300);
        Check(b.Editor.Of(CollabFrameType.Msg, "replay").Count == 3 && b.Editor.Of(CollabFrameType.Msg, "chat").Count == 1, "...and nothing twice");

        // Host migration: kill the host process.
        var lastRoster = b.Editor.Peers;
        var expected = lastRoster.Where(p => p.id != a.Id).OrderBy(p => p.avgPingMs < 0 ? int.MaxValue : p.avgPingMs).ThenBy(p => p.id, StringComparer.Ordinal).First();
        var killedAt = DateTime.UtcNow;
        a.Process.Kill(true);
        var survivors = new[] { b, c };
        bool migrated = await Until(() => survivors.All(n => n.Editor.InSession && n.Editor.Peers.Length == 2 && n.Editor.HostId == expected.id), 15000);
        Check(migrated, $"killing the host: B and C agree on the new host {(expected.id == b.Id ? "B" : "C")} (best ping, then id) in {(DateTime.UtcNow - killedAt).TotalSeconds:0.0} s");
        Check(survivors.All(n => n.Editor.SharedValue("scene") == "{\"v\":1}" && n.Editor.SharedValue("k2") == "\"again\""), "the shared store survives the migration");
        Check(survivors.All(n => n.Editor.Peers.Any(p => p.presence?.Any(kv => kv.key == "camera") == true)), "presence survives the migration");
        var newHost = expected.id == b.Id ? b : c;
        var other = newHost == b ? c : b;
        // The new host's own store (what a fresh editor would get) still has everything.
        newHost.Editor.Disconnect();
        newHost.Editor.Instance = null;
        newHost.Editor.LastSeq = 0;
        await Task.Delay(200);
        await newHost.Editor.ConnectAsync();
        Check(await Until(() => newHost.Editor.SharedValue("scene") == "{\"v\":1}", 3000), "the new host holds the shared store (fresh editor snapshot)");
        other.Editor.Send(new CollabFrame { type = CollabFrameType.Send, channel = "after", json = "1" });
        Check(await Until(() => newHost.Editor.Of(CollabFrameType.Msg, "after").Count == 1, 5000), "messages flow through the new host");
        Check(other.Editor.Peers.Select(p => p.color).Distinct().Count() == 2, "colours stay unique after migration");

        // A comes back (a new process): it joins the running session.
        var a2 = await StartNode("A2", project, ports[0], discovery, key, new[] { ports[1], ports[2], ports[3] }, "--no-git");
        Check(await Until(() => new[] { a2, b, c }.All(n => n.Editor.InSession && n.Editor.Peers.Length == 3 && n.Editor.HostId == newHost.Id), 15000),
            "a returning peer joins the running session (hop in)");

        // A graceful exit of the host: the others move on right away.
        var hostBefore = newHost;
        var rest = new[] { a2, b, c }.Where(n => n != hostBefore).ToArray();
        var leftAt = DateTime.UtcNow;
        hostBefore.Editor.Send(new CollabFrame { type = CollabFrameType.Shutdown });
        Check(await Until(() => rest.All(n => n.Editor.InSession && n.Editor.Peers.Length == 2 && n.Editor.HostId != hostBefore.Id && n.Editor.HostId == rest[0].Editor.HostId), 10000),
            $"the host leaving politely hands over in {(DateTime.UtcNow - leftAt).TotalSeconds:0.0} s (hop out)");
        Check(rest.All(n => n.Editor.SharedValue("scene") == "{\"v\":1}"), "the shared store survives a second migration");
        Check(await Until(() => hostBefore.Process.HasExited, 5000), "the helper exits on Shutdown");

        foreach (var n in new[] { a2, b, c }) { try { n.Process.Kill(true); } catch { } }
    }

    private static async Task DiscoveryTest(int port)
    {
        Console.WriteLine("LAN discovery without --peer (UDP multicast/broadcast)");
        string key = "disc-" + Guid.NewGuid().ToString("N")[..8];
        string project = Path.Combine(_work, "nogit-project");
        var x = await StartNode("X", project, port, port + 50, key, Array.Empty<int>(), "--no-git");
        await Task.Delay(500);
        var y = await StartNode("Y", project, port + 1, port + 50, key, Array.Empty<int>(), "--no-git");
        bool found = await Until(() => x.Editor.Peers.Length == 2 && y.Editor.Peers.Length == 2, 12000);
        if (found) Pass("two helpers find each other with beacons alone");
        else Console.WriteLine("  skip two helpers didn't find each other by beacon alone (this machine has no multicast/broadcast route; --peer and the relay still work)");
        var z = await StartNode("Z", project, port + 2, port + 50, "other-" + key, new[] { port, port + 1 }, "--no-git");
        await Task.Delay(4000);
        Check(z.Editor.Peers.Length == 1 && x.Editor.Peers.All(p => p.id != z.Id), "a helper with a different project key stays apart");
        foreach (var n in new[] { x, y, z }) { try { n.Process.Kill(true); } catch { } }
    }

    private static async Task RelayTest(int port)
    {
        Console.WriteLine("Relay mode");
        var relay = Launch(new List<string> { "--relay", "--port", port.ToString(), "--timeout-ms", "6000", "--log", Path.Combine(_work, "relay.log") });
        await Task.Delay(1500);
        string key = "relay-" + Guid.NewGuid().ToString("N")[..8];
        string project = Path.Combine(_work, "nogit-project");
        var p = await StartNode("P", project, port + 1, port + 50, key, Array.Empty<int>(), "--no-git", "--relay-address", $"127.0.0.1:{port}");
        var q = await StartNode("Q", project, port + 2, port + 50, key, Array.Empty<int>(), "--no-git", "--relay-address", $"127.0.0.1:{port}");
        Check(await Until(() => p.Editor.InSession && q.Editor.InSession && p.Editor.Peers.Length == 2 && q.Editor.Peers.Length == 2, 10000), "two helpers meet through the relay");
        Check(p.Editor.Peers.Count(x => x.isHost) == 1 && p.Editor.HostId == q.Editor.HostId, "one of them leads (the peer editors see as host)");
        p.Editor.Send(new CollabFrame { type = CollabFrameType.Send, channel = "r", json = "1" });
        p.Editor.Send(new CollabFrame { type = CollabFrameType.Shared, key = "rk", json = "2" });
        Check(await Until(() => q.Editor.Of(CollabFrameType.Msg, "r").Count == 1 && q.Editor.SharedValue("rk") == "2", 5000), "messages and the shared store go through the relay");
        var lead = p.Editor.HostId == p.Id ? p : q;
        var stays = lead == p ? q : p;
        lead.Process.Kill(true);
        Check(await Until(() => stays.Editor.Peers.Length == 1 && stays.Editor.HostId == stays.Id, 10000), "when the lead goes, the relay makes the remaining peer lead");
        Check(stays.Editor.SharedValue("rk") == "2", "the relay keeps the shared store");
        try { relay.Kill(true); } catch { }
        try { stays.Process.Kill(true); } catch { }
    }

    #endregion

    #region Git

    private static async Task GitTests(int port)
    {
        Console.WriteLine("Git: asset announce/push/pull, scene snapshots, live scene protection, safety");
        string origin = Path.Combine(_work, "origin.git");
        string one = Path.Combine(_work, "clone1"), two = Path.Combine(_work, "clone2");
        Git(_work, "init", "--bare", "-b", "main", origin);
        Directory.CreateDirectory(one);
        Git(one, "init", "-b", "main");
        Git(one, "config", "user.name", "Alice");
        Git(one, "config", "user.email", "alice@example.com");
        Directory.CreateDirectory(Path.Combine(one, "Assets"));
        File.WriteAllText(Path.Combine(one, "Assets", "Scene.unity"), "scene v1\n");
        File.WriteAllText(Path.Combine(one, "Assets", "Scene.unity.meta"), "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n");
        File.WriteAllText(Path.Combine(one, "Assets", "Notes.txt"), "notes v1\n");
        File.WriteAllText(Path.Combine(one, "Assets", "Shared.txt"), "shared v1\n");
        Git(one, "add", "-A");
        Git(one, "commit", "-m", "initial");
        Git(one, "remote", "add", "origin", origin);
        Git(one, "push", "-u", "origin", "main");
        Git(_work, "clone", origin, two);
        Git(two, "config", "user.name", "Bob");
        Git(two, "config", "user.email", "bob@example.com");

        int discovery = port + 50;
        var g1 = await StartNode("G1", one, port, discovery, null, new[] { port + 1 });
        var g2 = await StartNode("G2", two, port + 1, discovery, null, new[] { port });
        Check(await Until(() => g1.Editor.Git?.hasRepo == true && g1.Editor.Git.branch == "main" && g1.Editor.Git.upstream == "origin/main", 5000),
            $"finds the repository, branch and remote ({g1.Editor.Git?.branch}, {g1.Editor.Git?.upstream})");
        Check(await Until(() => g1.Editor.Peers.Length == 2 && g2.Editor.Peers.Length == 2, 15000),
            "two clones of the same remote + branch get the same project key and join one session");
        Check(g2.Editor.Peers.Any(p => p.id == g1.Id && p.gitEmail == "alice@example.com"), "peers share their git name and email");

        // Both have the scene open (live).
        foreach (var n in new[] { g1, g2 })
            n.Editor.Send(new CollabFrame { type = CollabFrameType.LiveScenes, paths = new[] { "Assets/Scene.unity" } });

        // Unrelated local edits that must never be committed or touched.
        File.WriteAllText(Path.Combine(one, "Assets", "Notes.txt"), "alice's unrelated edit\n");
        File.WriteAllText(Path.Combine(two, "Assets", "Notes.txt"), "bob's unrelated edit\n");

        // Alice adds an asset.
        string guid = "abcdefabcdefabcdefabcdefabcdef12";
        File.WriteAllText(Path.Combine(one, "Assets", "Tex.png"), "PNGDATA");
        File.WriteAllText(Path.Combine(one, "Assets", "Tex.png.meta"), $"fileFormatVersion: 2\nguid: {guid}\n");
        g1.Editor.Send(new CollabFrame { type = CollabFrameType.Announce, key = guid, text = "Assets/Tex.png", paths = Array.Empty<string>(), bounds = new[] { 1f, 2f, 3f, 4f, 5f, 6f } });
        Check(await Until(() => g2.Editor.Of(CollabFrameType.Announced).Any(f => f.key == guid && f.bounds?.Length == 6 && f.bounds[3] == 4f), 3000),
            "the other peer hears about the new asset at once (guid, bounds) for a placeholder");
        var announcedAt = DateTime.UtcNow;
        Check(await Until(() => g2.Editor.Of(CollabFrameType.Resolved).Any(f => f.key == guid), 30000),
            $"...and pulls it automatically ({(DateTime.UtcNow - announcedAt).TotalSeconds:0.0} s), then the guid is no longer pending");
        Check(File.Exists(Path.Combine(two, "Assets", "Tex.png")), "the asset is in the other clone");
        var pullEnd = g2.Editor.Of(CollabFrameType.PullEnd).LastOrDefault();
        Check(pullEnd?.paths?.Contains("Assets/Tex.png") == true && g2.Editor.Of(CollabFrameType.PullBegin).Count >= 1,
            "the pull is wrapped in PullBegin/PullEnd (auto refresh off) and reports the changed paths");
        string files = Git(one, "show", "--name-only", "--format=", "HEAD");
        Check(files.Split('\n').OrderBy(x => x).SequenceEqual(new[] { "Assets/Tex.png", "Assets/Tex.png.meta" }), $"the asset commit has only the asset and its .meta ({files.Replace('\n', ' ')})");
        Check(Git(one, "show", "-s", "--format=%B", "HEAD").Contains("Co-authored-by: Alice <alice@example.com>"), "the asset commit has the Co-authored-by trailer");
        Check(File.ReadAllText(Path.Combine(one, "Assets", "Notes.txt")) == "alice's unrelated edit\n" && Git(one, "status", "--porcelain").Contains("Assets/Notes.txt"),
            "unrelated local edits stay uncommitted");
        Check(File.ReadAllText(Path.Combine(two, "Assets", "Notes.txt")) == "bob's unrelated edit\n", "the pull leaves the other peer's unrelated edits alone");

        // Scene snapshot by the host while Bob has the same scene open with different bytes on disk.
        File.WriteAllText(Path.Combine(two, "Assets", "Scene.unity"), "scene as bob's editor saved it\n");
        File.WriteAllText(Path.Combine(one, "Assets", "Scene.unity"), "scene v2 by the session\n");
        g1.Editor.Send(new CollabFrame
        {
            type = CollabFrameType.CommitScene,
            text = "Assets/Scene.unity",
            json = "Snapshot of Scene",
            coAuthors = new[] { new CollabCoAuthor { name = "Bob", email = "bob@example.com" }, new CollabCoAuthor { name = "Alice", email = "alice@example.com" } },
        });
        Check(await Until(() => { try { return Git(origin, "log", "-1", "--format=%s", "main").StartsWith("Snapshot of Scene"); } catch { return false; } }, 20000),
            "the scene snapshot is committed and pushed");
        string body = Git(origin, "log", "-1", "--format=%B", "main");
        Check(body.Contains("Co-authored-by: Bob <bob@example.com>") && !body.Contains("Co-authored-by: Alice") && body.Contains(CollabProtocol.CommitTrailer),
            "the snapshot names the other participants as co-authors (not the committer)");
        string originHead = Git(origin, "rev-parse", "main");
        g2.Editor.Send(new CollabFrame { type = CollabFrameType.PullNow });
        Check(await Until(() => Git(two, "rev-parse", "HEAD") == originHead, 20000), "the other peer pulls past the snapshot");
        Check(File.ReadAllText(Path.Combine(two, "Assets", "Scene.unity")) == "scene as bob's editor saved it\n",
            "...but its live scene file is left exactly as it was (never pulled while live)");
        Check(g2.Editor.Of(CollabFrameType.PullEnd).All(f => f.paths == null || !f.paths.Contains("Assets/Scene.unity")), "...and isn't reported as pulled");

        // A file changed both locally and upstream: the pull waits and says why.
        File.WriteAllText(Path.Combine(two, "Assets", "Shared.txt"), "bob's local change\n");
        File.WriteAllText(Path.Combine(one, "Assets", "Shared.txt"), "alice's change\n");
        string guid2 = "22222222222222222222222222222222";
        File.WriteAllText(Path.Combine(one, "Assets", "Mat.mat"), "material");
        File.WriteAllText(Path.Combine(one, "Assets", "Mat.mat.meta"), $"fileFormatVersion: 2\nguid: {guid2}\n");
        g1.Editor.Send(new CollabFrame { type = CollabFrameType.Announce, key = guid2, text = "Assets/Mat.mat", paths = new[] { "Assets/Shared.txt" }, bounds = new float[6] });
        Check(await Until(() => g2.Editor.Of(CollabFrameType.Log).Any(f => f.key == "warning" && f.text.Contains("Assets/Shared.txt")), 20000),
            "a pull that would overwrite local edits is postponed with a warning");
        Check(File.ReadAllText(Path.Combine(two, "Assets", "Shared.txt")) == "bob's local change\n" && !File.Exists(Path.Combine(two, "Assets", "Mat.mat")),
            "...and nothing was overwritten");
        Git(two, "checkout", "--", "Assets/Shared.txt");
        Check(await Until(() => g2.Editor.Of(CollabFrameType.Resolved).Any(f => f.key == guid2), 20000), "once the local edit is reverted, the pull goes through by itself");

        // Collab never pushes commits it didn't make.
        File.WriteAllText(Path.Combine(one, "Assets", "Manual.txt"), "manual");
        Git(one, "add", "Assets/Manual.txt");
        Git(one, "commit", "-m", "my own commit");
        File.WriteAllText(Path.Combine(one, "Assets", "Late.png"), "x");
        File.WriteAllText(Path.Combine(one, "Assets", "Late.png.meta"), "guid: 33333333333333333333333333333333\n");
        g1.Editor.Send(new CollabFrame { type = CollabFrameType.Announce, key = "33333333333333333333333333333333", text = "Assets/Late.png", paths = Array.Empty<string>(), bounds = new float[6] });
        Check(await Until(() => g1.Editor.Of(CollabFrameType.Log).Any(f => f.text.Contains("Not pushing")), 15000), "it won't push the user's own unpushed commits");
        Check(!Git(origin, "log", "--format=%s", "main").Contains("my own commit"), "...which stay unpushed");

        foreach (var n in new[] { g1, g2 }) { try { n.Process.Kill(true); } catch { } }
    }

    #endregion
}
