using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Connects the editor to its collaboration helper (Collab~/SchematicsCollab, a .NET program). The helper does the
    /// networking and git work and outlives script reloads; this side starts it (building it with dotnet the first time),
    /// reconnects right after every reload, and turns its messages into <see cref="CollabSession"/> and
    /// <see cref="CollabGit"/> events on the main thread.
    /// </summary>
    [InitializeOnLoad]
    internal static class CollabBridge
    {
        private const string SeqKey = "Schematics.Collab.LastSeq";
        private const string InstanceKey = "Schematics.Collab.Instance";
        private const string RebuiltKey = "Schematics.Collab.RebuiltForVersion";
        private const string StartedPidKey = "Schematics.Collab.StartedPid";
        private const string StartedAtKey = "Schematics.Collab.StartedAtUtc";
        private const string SweptKey = "Schematics.Collab.SweptOrphans";
        /// <summary>How long a helper we started may take to write helper.json before we give up on it.</summary>
        private const double StartupGraceSeconds = 30;
        private static Task<int> _sweeping;
        // Read once on the main thread (the sweep runs on a worker thread).
        private static readonly bool IsWindowsEditor = Application.platform == RuntimePlatform.WindowsEditor;

        private static readonly ConcurrentQueue<CollabFrame> _incoming = new ConcurrentQueue<CollabFrame>();
        private static readonly List<CollabFrame> _outbox = new List<CollabFrame>();
        private static readonly object _writeLock = new object();
        private static TcpClient _client;
        private static NetworkStream _stream;
        private static bool _greeted;
        private static volatile bool _connecting;
        private static int _failedConnects;
        private static double _nextAttempt;
        private static Task _building;
        private static bool _buildFailed;
        private static double _startedAt;

        private static bool _inSession;
        private static string _hostId;
        private static readonly Dictionary<string, CollabPeer> _peers = new Dictionary<string, CollabPeer>();
        private static readonly List<CollabPeer> _peerList = new List<CollabPeer>();
        private static readonly CollabPeer _local = new CollabPeer { IsLocal = true, PingMs = 0 };
        private static readonly Dictionary<string, string> _localPresence = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _shared = new Dictionary<string, string>();
        private static string _pullId;
        private static string _relayInUse;
        private static string _projectKey;

        static CollabBridge()
        {
            _local.Name = CollabSettings.EffectiveName;
            _local.AvatarId = CollabSettings.AvatarId;
            _local.Color = new Color(0.3f, 0.6f, 1f);
            _peerList.Add(_local);
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.quitting += OnQuit;
            CollabSettings.Changed += OnSettingsChanged;
            EditorSceneManager.sceneOpened += (s, m) => SendLiveScenes();
            EditorSceneManager.sceneClosed += s => SendLiveScenes();
            EditorSceneManager.newSceneCreated += (s, setup, mode) => SendLiveScenes();
            EditorSceneManager.sceneSaved += s => SendLiveScenes();
            // Play Mode makes this editor a playtester (others see the role, and live edits reach its game).
            EditorApplication.playModeStateChanged += change =>
            {
                if (change != PlayModeStateChange.EnteredPlayMode && change != PlayModeStateChange.EnteredEditMode) return;
                _local.Role = EditorApplication.isPlaying ? CollabRole.Playtester : CollabRole.Editor;
                if (_greeted) Write(new CollabFrame { type = CollabFrameType.Profile, profile = Profile() });
            };
            _local.Role = EditorApplication.isPlayingOrWillChangePlaymode ? CollabRole.Playtester : CollabRole.Editor;
            // Warnings and errors point at the helper's log, where the full story is.
            CollabLog.Footer = "Collaboration helper log: <a href=\"Library/SchematicsCollab/collab.log\">Library/SchematicsCollab/collab.log</a>";
            _relayInUse = CollabSettings.RelayAddress;
            // Reconnect at once (this runs right after a script reload).
            _nextAttempt = 0;
        }

        #region State for the public API

        internal static bool InSession => _inSession && _greeted;

        /// <summary>
        /// The project key (the room) the helper uses, or null before it said: from its state, else its helper.json.
        /// Builds carry it so they join the same room.
        /// </summary>
        internal static string ProjectKey
        {
            get
            {
                if (!string.IsNullOrEmpty(_projectKey)) return _projectKey;
                var state = ReadState();
                return state != null && !string.IsNullOrEmpty(state.key) ? state.key : null;
            }
        }
        internal static CollabPeer Local => _local;
        internal static IReadOnlyList<CollabPeer> PeerList => _peerList;

        internal static CollabPeer Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id == _local.Id) return _local;
            return _peers.TryGetValue(id, out var peer) ? peer : null;
        }

        internal static void SetLocalPresence(string key, string json)
        {
            if (json == null) _localPresence.Remove(key);
            else _localPresence[key] = json;
            _local.Presence = MergedPresence(_localPresence, CollabGit.Info.userName, CollabGit.Info.userEmail);
        }

        internal static void SetSharedLocal(string key, string json)
        {
            bool changed = json == null ? _shared.Remove(key) : !_shared.TryGetValue(key, out var old) || old != json;
            if (json != null) _shared[key] = json;
            if (changed) CollabSession.RaiseSharedChanged(key);
        }

        internal static string GetShared(string key) => key != null && _shared.TryGetValue(key, out var value) ? value : null;

        /// <summary>Sends to the helper now, or as soon as it's connected.</summary>
        internal static void Post(CollabFrame frame)
        {
            if (!CollabSettings.Enabled) return;
            if (_greeted && Write(frame)) return;
            if (_outbox.Count > 5000) _outbox.RemoveAt(0);
            _outbox.Add(frame);
        }

        #endregion

        #region Locating and starting the helper

        /// <summary>The Collab~ folder: next to the toolkit's own folders, wherever the toolkit is installed.</summary>
        internal static string CollabFolder
        {
            get
            {
                foreach (var guid in AssetDatabase.FindAssets("CollabBridge t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/CollabBridge.cs", StringComparison.Ordinal)) continue;
                    for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))); dir != null; dir = dir.Parent)
                    {
                        string candidate = Path.Combine(dir.FullName, "Collab~");
                        if (Directory.Exists(candidate)) return candidate;
                    }
                }
                return null;
            }
        }

        private static string ProjectPath => Path.GetDirectoryName(Application.dataPath);
        private static string StateFolder => Path.Combine(ProjectPath, "Library", "SchematicsCollab");
        private static string StateFile => Path.Combine(StateFolder, "helper.json");
        internal static string LogPath => Path.Combine(StateFolder, "collab.log");
        private static string HelperDll(string folder) => Path.Combine(folder, "bin", "SchematicsCollab.dll");

        /// <summary>
        /// The dotnet to use: the one the assistant's setup found or installed (Agent~/dotnet-path.txt, which also covers
        /// an install on another drive), its private copy in Agent~/.dotnet, else a usual install location or the PATH.
        /// </summary>
        private static string DotnetPath(string collabFolder)
        {
            string toolkit = Path.GetDirectoryName(collabFolder);
            string agent = Path.Combine(toolkit ?? "", "Agent~");
            try
            {
                string recorded = Path.Combine(agent, "dotnet-path.txt");
                if (File.Exists(recorded))
                {
                    string path = File.ReadAllText(recorded).Trim();
                    if (File.Exists(path)) return path;
                }
                string exe = Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet";
                string privateCopy = Path.Combine(agent, ".dotnet", exe);
                if (File.Exists(privateCopy)) return privateCopy;
            }
            catch { }
            return InstalledDotnet();
        }

        // Unity started from the Hub or a desktop launcher doesn't get the shell's PATH (a ~/.dotnet install, or one a
        // profile script adds), so the usual install locations are checked before falling back to plain "dotnet".
        private static string InstalledDotnet()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
                return "dotnet";

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string[] candidates =
            {
                string.IsNullOrEmpty(root) ? null : Path.Combine(root, "dotnet"),
                Path.Combine(home, ".dotnet", "dotnet"),
                "/usr/local/share/dotnet/dotnet",
                "/usr/share/dotnet/dotnet",
                "/usr/lib/dotnet/dotnet",
                "/usr/local/bin/dotnet",
                "/usr/bin/dotnet",
                "/snap/bin/dotnet",
                "/opt/homebrew/bin/dotnet",
            };
            foreach (var path in candidates)
                if (path != null && File.Exists(path))
                    return path;
            return "dotnet";
        }

        private static CollabHelperState ReadState()
        {
            try { return File.Exists(StateFile) ? JsonUtility.FromJson<CollabHelperState>(File.ReadAllText(StateFile)) : null; }
            catch { return null; }
        }

        private static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; }
            catch { return false; }
        }

        private static void EnsureHelper()
        {
            var state = ReadState();
            if (state != null && IsAlive(state.pid))
            {
                Connect(state.controlPort);
                SweepOrphansOnce(state.pid);
                return;
            }

            // A helper we started is still starting (dotnet's start-up and the git checks take a few seconds, longer on
            // a cold Windows machine): wait for it. Starting another one every half second meanwhile is what made
            // people appear in the session many times over (each extra helper joined as another copy of them).
            int startedPid = SessionState.GetInt(StartedPidKey, 0);
            if (startedPid > 0 && IsAlive(startedPid))
            {
                long.TryParse(SessionState.GetString(StartedAtKey, "0"), out long startedTicks);
                double age = (DateTime.UtcNow - new DateTime(Math.Max(0, startedTicks), DateTimeKind.Utc)).TotalSeconds;
                if (age >= 0 && age < StartupGraceSeconds)
                {
                    if (CollabStatus.State != CollabState.Starting) CollabStatus.Set(CollabState.Starting, "Starting the collaboration helper...");
                    return;
                }
                KillHelper(startedPid, "it didn't get ready within 30 s");
            }
            SessionState.EraseInt(StartedPidKey);

            // Helpers left behind by older versions (see SweepOrphans) hold the helper's files and sit in the session.
            if (_sweeping == null && SessionState.GetString(SweptKey, "") != "1")
            {
                SweepOrphansOnce(0);
                return;
            }
            if (_sweeping != null && !_sweeping.IsCompleted) return;
            string folder = CollabFolder;
            if (folder == null)
            {
                CollabStatus.Set(CollabState.Error, "Collab~ folder not found next to the toolkit.");
                return;
            }
            bool stale = SessionState.GetInt(RebuiltKey, 0) != CollabProtocol.Version && File.Exists(HelperDll(folder)) && HelperIsOlder(folder);
            if (!File.Exists(HelperDll(folder)) || stale)
            {
                Build(folder);
                return;
            }
            StartHelper(folder);
        }

        // The helper is built from Collab~ and from CollabProtocol.cs: newer sources than the build mean rebuild.
        private static bool HelperIsOlder(string folder)
        {
            try
            {
                var built = File.GetLastWriteTimeUtc(HelperDll(folder));
                var sources = Directory.GetFiles(Path.Combine(folder, "SchematicsCollab"), "*.cs")
                    .Concat(Directory.GetFiles(Path.Combine(folder, "SchematicsCollab"), "*.csproj")).ToList();
                var protocol = AssetDatabase.FindAssets("CollabProtocol t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => p.EndsWith("/CollabProtocol.cs", StringComparison.Ordinal));
                if (protocol != null)
                {
                    // The protocol and the playtest client next to it (CollabRuntimeClient, CollabLiveEditCodec) are compiled into the helper too.
                    string folderOfProtocol = Path.GetDirectoryName(Path.GetFullPath(protocol));
                    foreach (var name in new[] { "CollabProtocol.cs", "CollabRuntimeClient.cs", "CollabLiveEditCodec.cs" })
                    {
                        string file = Path.Combine(folderOfProtocol, name);
                        if (File.Exists(file)) sources.Add(file);
                    }
                }
                return sources.Any(s => File.GetLastWriteTimeUtc(s) > built);
            }
            catch { return false; }
        }

        private static void Build(string folder)
        {
            if (_building != null || _buildFailed) return;
            string dotnet = DotnetPath(folder);
            string project = Path.Combine(folder, "SchematicsCollab", "SchematicsCollab.csproj");
            string output = Path.Combine(folder, "bin");
            string log = Path.Combine(folder, "build.log");
            CollabStatus.Set(CollabState.Building, "Building the collaboration helper (first time only)...");
            Debug.Log($"[Collab] Building the collaboration helper with {dotnet} (first time only; log: {log}).");
            string stateFolder = StateFolder;
            _building = Task.Run(() =>
            {
                // Running helpers keep the old build's files open (on Windows the build then can't replace them).
                try { SweepOrphans(stateFolder, 0); } catch { }
                var info = new ProcessStartInfo(dotnet, $"build \"{project}\" -c Release -o \"{output}\" -nologo")
                {
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                info.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
                info.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
                using (var process = Process.Start(info))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10 * 60 * 1000)) { try { process.Kill(); } catch { } throw new Exception("the build took over 10 minutes"); }
                    File.WriteAllText(log, stdout.Result + stderr.Result);
                    if (process.ExitCode != 0)
                    {
                        string firstError = (stdout.Result + stderr.Result).Split('\n').FirstOrDefault(l => l.Contains(": error ")) ?? $"exit code {process.ExitCode}";
                        throw new Exception(firstError.Trim());
                    }
                }
            });
            _building.ContinueWith(t => EditorApplication.delayCall += () =>
            {
                _building = null;
                if (t.IsFaulted)
                {
                    _buildFailed = true;
                    string why = t.Exception?.GetBaseException().Message;
                    CollabStatus.Set(CollabState.Error, "The helper didn't build: " + why);
                    Debug.LogError($"[Collab] Couldn't build the collaboration helper: {why}. Is the .NET 10 SDK installed? "
                        + $"(Sprocket's setup in Agent~ installs one.) Log: {log}. Fix it, then Preferences > Schematics > Collaboration > Restart Helper.");
                    return;
                }
                SessionState.SetInt(RebuiltKey, CollabProtocol.Version);
                Debug.Log("[Collab] Helper built.");
                StartHelper(folder);
            });
        }

        private static void StartHelper(string folder)
        {
            StopStale();
            Directory.CreateDirectory(StateFolder);
            var args = new StringBuilder();
            args.Append($"\"{HelperDll(folder)}\" --project \"{ProjectPath}\" --state \"{StateFolder}\" --collab \"{folder}\"");
            args.Append($" --parent-pid {Process.GetCurrentProcess().Id} --product \"{Application.productName.Replace("\"", "'")}\"");
            if (!string.IsNullOrWhiteSpace(CollabSettings.RelayAddress)) args.Append($" --relay-address \"{CollabSettings.RelayAddress.Trim()}\"");
            _relayInUse = CollabSettings.RelayAddress;
            // Not redirected: the helper outlives this script domain and writes its own log (Library/SchematicsCollab).
            var info = new ProcessStartInfo(DotnetPath(folder), args.ToString())
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                var process = Process.Start(info);
                SessionState.SetInt(StartedPidKey, process.Id);
                SessionState.SetString(StartedAtKey, DateTime.UtcNow.Ticks.ToString());
                _startedAt = EditorApplication.timeSinceStartup;
                CollabStatus.Set(CollabState.Starting, "Starting the collaboration helper...");
            }
            catch (Exception e)
            {
                CollabStatus.Set(CollabState.Error, "Couldn't start the helper: " + e.Message);
                Debug.LogError($"[Collab] Couldn't start the collaboration helper: {e.Message}. Is the .NET 10 runtime installed?");
                _nextAttempt = EditorApplication.timeSinceStartup + 30;
            }
        }

        /// <summary>A helper this project lost track of (the editor crashed, or it stopped answering): stop it.</summary>
        private static void StopStale()
        {
            var state = ReadState();
            try { File.Delete(StateFile); } catch { }
            if (state == null || !IsAlive(state.pid)) return;
            try
            {
                using (var old = Process.GetProcessById(state.pid))
                {
                    // Only a .NET process (the id may belong to something else by now).
                    if (!old.ProcessName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase)
                        && !old.ProcessName.StartsWith("SchematicsCollab", StringComparison.OrdinalIgnoreCase)) return;
                    old.Kill();
                    old.WaitForExit(3000);
                    Debug.Log($"[Collab] Stopped an old collaboration helper that wasn't answering (process {state.pid}).");
                }
            }
            catch { }
        }

        private static void KillHelper(int pid, string why)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (process.HasExited) return;
                    process.Kill();
                    process.WaitForExit(2000);
                }
                CollabLog.Warning($"Stopped the collaboration helper process {pid}: {why}.");
            }
            catch { }
        }

        /// <summary>Runs <see cref="SweepOrphans"/> once per editor session, in the background.</summary>
        private static void SweepOrphansOnce(int keepPid)
        {
            if (_sweeping != null || SessionState.GetString(SweptKey, "") == "1") return;
            SessionState.SetString(SweptKey, "1");
            string stateFolder = StateFolder;
            _sweeping = Task.Run(() => SweepOrphans(stateFolder, keepPid));
            _sweeping.ContinueWith(t =>
            {
                if (t.IsFaulted) CollabLog.Warning("Looking for leftover collaboration helpers failed: " + t.Exception?.GetBaseException().Message);
                else if (t.Result > 0) CollabLog.Info($"Stopped {t.Result} leftover collaboration helper(s) of this project (they showed up as extra copies of you in the session).");
            });
        }

        /// <summary>
        /// Stops every helper process of this project except <paramref name="keepPid"/>: processes running
        /// SchematicsCollab.dll whose command line names this project's state folder. Older versions of this bridge
        /// could start several helpers while the first was still starting; they stayed until the editor quit, each in
        /// the session as another copy of the user, and they lock the helper's files so it can't be rebuilt.
        /// Returns how many were stopped.
        /// </summary>
        internal static int SweepOrphans(string stateFolder, int keepPid)
        {
            int stopped = 0;
            int self = Process.GetCurrentProcess().Id;
            string needle = Normalize(stateFolder);
            foreach (var (pid, commandLine) in ListProcesses())
            {
                if (pid == keepPid || pid == self || string.IsNullOrEmpty(commandLine)) continue;
                if (commandLine.IndexOf("SchematicsCollab", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (Normalize(commandLine).IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    using (var process = Process.GetProcessById(pid))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                        stopped++;
                    }
                }
                catch { }
            }
            return stopped;
        }

        private static string Normalize(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');

        /// <summary>Process ids with their command lines (PowerShell/CIM on Windows, ps elsewhere). Empty if neither works.</summary>
        private static List<(int pid, string commandLine)> ListProcesses()
        {
            var result = new List<(int, string)>();
            bool windows = IsWindowsEditor;
            ProcessStartInfo info;
            if (windows)
            {
                string script = "Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe' OR Name='SchematicsCollab.exe'\" | ForEach-Object { [string]$_.ProcessId + '|' + $_.CommandLine }";
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                info = new ProcessStartInfo("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded);
            }
            else info = new ProcessStartInfo("ps", "-A -ww -o pid= -o args=");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            try
            {
                using (var process = Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(15000)) { try { process.Kill(); } catch { } return result; }
                    foreach (var raw in output.Result.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        int split = windows ? line.IndexOf('|') : line.IndexOf(' ');
                        if (split <= 0) continue;
                        if (int.TryParse(line.Substring(0, split).Trim(), out int pid)) result.Add((pid, line.Substring(split + 1)));
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>Stops the helper and starts a fresh one (and rebuilds it if its sources changed).</summary>
        internal static void Restart()
        {
            if (_greeted) Write(new CollabFrame { type = CollabFrameType.Shutdown });
            Disconnect();
            _buildFailed = false;
            var state = ReadState();
            EditorApplication.delayCall += () =>
            {
                if (state != null && IsAlive(state.pid))
                    try { using (var p = Process.GetProcessById(state.pid)) p.WaitForExit(2000); } catch { }
                StopStale();
                SessionState.EraseInt(RebuiltKey);
                SessionState.EraseString(InstanceKey);
                SessionState.EraseInt(StartedPidKey);
                SessionState.EraseString(SweptKey);
                _sweeping = null;
                _nextAttempt = 0;
            };
        }

        internal static void OpenLog()
        {
            if (File.Exists(LogPath)) EditorUtility.OpenWithDefaultApp(LogPath);
            else Debug.Log($"[Collab] No log yet ({LogPath}).");
        }

        #endregion

        #region Connection

        private static void Connect(int port)
        {
            if (_connecting) return;
            _connecting = true;
            CollabStatus.Set(_inSession ? CollabStatus.State : CollabState.Starting, _inSession ? CollabStatus.Message : "Connecting to the collaboration helper...");
            var thread = new Thread(() =>
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient { NoDelay = true };
                    var task = client.ConnectAsync("127.0.0.1", port);
                    if (!task.Wait(1500) || !client.Connected) throw new IOException("no answer");
                    var stream = client.GetStream();
                    lock (_writeLock) { _client = client; _stream = stream; }
                    _connecting = false;
                    _incoming.Enqueue(new CollabFrame { type = "_connected" });
                    ReadLoop(client, stream);
                }
                catch
                {
                    bool wasConnected;
                    lock (_writeLock) wasConnected = _client == client && client != null;
                    try { client?.Close(); } catch { }
                    if (!wasConnected) _incoming.Enqueue(new CollabFrame { type = "_failed" });
                }
                finally { _connecting = false; }
            }) { IsBackground = true, Name = "Collab helper link" };
            thread.Start();
        }

        private static void ReadLoop(TcpClient client, NetworkStream stream)
        {
            var header = new byte[4];
            try
            {
                while (true)
                {
                    ReadExact(stream, header);
                    int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    if (length < 0 || length > CollabProtocol.MaxFrameBytes) throw new IOException("bad frame");
                    var body = new byte[length];
                    ReadExact(stream, body);
                    var frame = JsonUtility.FromJson<CollabFrame>(Encoding.UTF8.GetString(body));
                    if (frame?.type != null) _incoming.Enqueue(frame);
                }
            }
            catch { }
            lock (_writeLock)
                if (_client == client) _incoming.Enqueue(new CollabFrame { type = "_closed" });
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

        private static bool Write(CollabFrame frame)
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(frame));
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
                    try { _client?.Close(); } catch { }
                    return false;
                }
            }
        }

        private static void Disconnect()
        {
            lock (_writeLock)
            {
                try { _client?.Close(); } catch { }
                _client = null;
                _stream = null;
            }
            _greeted = false;
        }

        private static void SendHello()
        {
            long.TryParse(SessionState.GetString(SeqKey, "0"), out long lastSeq);
            Write(new CollabFrame
            {
                type = CollabFrameType.Hello,
                id = SessionState.GetString(InstanceKey, ""),
                seq = lastSeq,
                profile = Profile(),
                paths = LiveScenes(),
                entries = SettingsEntries(),
            });
        }

        private static CollabProfile Profile() => new CollabProfile
        {
            name = CollabSettings.EffectiveName,
            avatarId = CollabSettings.AvatarId,
            role = EditorApplication.isPlayingOrWillChangePlaymode ? CollabProtocol.RolePlaytester : CollabProtocol.RoleEditor,
            client = CollabProtocol.ClientEditor,
            version = CollabProtocol.Version,
        };

        private static CollabKV[] SettingsEntries() => new[]
        {
            new CollabKV { key = "gitPoll", value = CollabSettings.GitPollSeconds.ToString() },
            new CollabKV { key = "autoPull", value = CollabSettings.AutoPull ? "1" : "0" },
            new CollabKV { key = "autoPush", value = CollabSettings.AutoPushAssets ? "1" : "0" },
        };

        private static string[] LiveScenes()
        {
            var paths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) paths.Add(scene.path);
            }
            return paths.ToArray();
        }

        private static void SendLiveScenes()
        {
            if (_greeted) Write(new CollabFrame { type = CollabFrameType.LiveScenes, paths = LiveScenes() });
        }

        private static void OnSettingsChanged()
        {
            _local.Name = CollabSettings.EffectiveName;
            _local.AvatarId = CollabSettings.AvatarId;
            if (!CollabSettings.Enabled)
            {
                if (_greeted) Write(new CollabFrame { type = CollabFrameType.Shutdown });
                Disconnect();
                SetOffline();
                CollabStatus.Set(CollabState.Disabled, "Off");
                return;
            }
            if ((CollabSettings.RelayAddress ?? "") != (_relayInUse ?? ""))
            {
                // A different relay (or none) means a different session: start over.
                Restart();
                return;
            }
            if (!_greeted) return;
            Write(new CollabFrame { type = CollabFrameType.Profile, profile = Profile() });
            Write(new CollabFrame { type = CollabFrameType.Settings, entries = SettingsEntries() });
        }

        #endregion

        #region Main thread

        private static void Update()
        {
            if (!CollabSettings.Enabled)
            {
                if (CollabStatus.State != CollabState.Disabled) CollabStatus.Set(CollabState.Disabled, "Off");
                while (_incoming.TryDequeue(out _)) { }
                return;
            }
            int handled = 0;
            while (handled++ < 500 && _incoming.TryDequeue(out var frame))
            {
                try { Handle(frame); }
                catch (Exception e) { CollabLog.Exception($"Handling '{frame.type}' from the collaboration helper failed", e); }
            }
            if (_client == null && !_connecting && _building == null && EditorApplication.timeSinceStartup >= _nextAttempt)
            {
                _nextAttempt = EditorApplication.timeSinceStartup + 0.5;
                // A helper that was started but never wrote its state file within 20 s: look at its log.
                if (CollabStatus.State == CollabState.Starting && _startedAt > 0 && EditorApplication.timeSinceStartup - _startedAt > 20 && ReadState() == null)
                {
                    _startedAt = 0;
                    CollabStatus.Set(CollabState.Error, "The helper didn't start. See its log.");
                    CollabLog.Error($"The collaboration helper didn't start. Its log: {LogPath}");
                    _nextAttempt = EditorApplication.timeSinceStartup + 30;
                    return;
                }
                EnsureHelper();
            }
        }

        private static void Handle(CollabFrame frame)
        {
            switch (frame.type)
            {
                case "_connected":
                    _failedConnects = 0;
                    SendHello();
                    return;
                case "_failed":
                    // A helper that's alive but doesn't answer for a few seconds is stuck: replace it.
                    if (++_failedConnects >= 6)
                    {
                        _failedConnects = 0;
                        Debug.LogWarning("[Collab] The collaboration helper isn't answering; restarting it.");
                        StopStale();
                    }
                    _nextAttempt = EditorApplication.timeSinceStartup + 0.5;
                    return;
                case "_closed":
                    Disconnect();
                    _nextAttempt = EditorApplication.timeSinceStartup + 0.3;
                    if (_inSession) CollabStatus.Set(CollabState.Starting, "Reconnecting to the collaboration helper...");
                    return;
            }

            // Sequenced events: skip what was already handled (a replay overlapping what we saw).
            if (frame.seq > 0 && frame.type != CollabFrameType.Hello)
            {
                long.TryParse(SessionState.GetString(SeqKey, "0"), out long last);
                if (frame.seq <= last) return;
                SessionState.SetString(SeqKey, frame.seq.ToString());
            }

            switch (frame.type)
            {
                case CollabFrameType.Hello:
                    OnHello(frame);
                    break;
                case CollabFrameType.State:
                    ApplyState(frame);
                    break;
                case CollabFrameType.Git:
                    CollabGit.SetInfo(frame.git);
                    _local.Name = CollabSettings.EffectiveName;
                    _local.Presence = MergedPresence(_localPresence, CollabGit.Info.userName, CollabGit.Info.userEmail);
                    break;
                case CollabFrameType.Msg:
                    var from = Find(frame.from) ?? new CollabPeer { Id = frame.from, Name = "?", PingMs = -1 };
                    CollabSession.Deliver(frame.channel, from, frame.json);
                    break;
                case CollabFrameType.Shared:
                    ApplyShared(frame.key, frame.flag ? null : (frame.json ?? ""));
                    break;
                case CollabFrameType.SharedAll:
                    var incoming = new Dictionary<string, string>();
                    foreach (var e in frame.entries ?? Array.Empty<CollabKV>())
                        if (!string.IsNullOrEmpty(e?.key)) incoming[e.key] = e.value ?? "";
                    foreach (var key in _shared.Keys.Where(k => !incoming.ContainsKey(k)).ToList()) ApplyShared(key, null);
                    foreach (var pair in incoming) ApplyShared(pair.Key, pair.Value);
                    break;
                case CollabFrameType.PendingAll:
                    CollabGit.SetPending(frame.entries);
                    break;
                case CollabFrameType.Announced:
                    CollabGit.OnAnnounced(frame.key, frame.text, frame.bounds);
                    break;
                case CollabFrameType.Resolved:
                    CollabGit.OnResolved(frame.key);
                    break;
                case CollabFrameType.PullBegin:
                    if (_pullId == null)
                    {
                        AssetDatabase.DisallowAutoRefresh();
                        _pullId = frame.id;
                    }
                    Write(new CollabFrame { type = CollabFrameType.PullReady, id = frame.id });
                    break;
                case CollabFrameType.PullEnd:
                    if (_pullId != null && _pullId == frame.id)
                    {
                        _pullId = null;
                        AssetDatabase.AllowAutoRefresh();
                    }
                    if (!string.IsNullOrEmpty(frame.text) && frame.text != "postponed") CollabLog.Error(StripPrefix(frame.text));
                    if (frame.paths != null && frame.paths.Length > 0)
                    {
                        AssetDatabase.Refresh();
                        CollabGit.OnPathsArrived(frame.paths);
                        CollabGit.RaisePulled(frame.paths);
                    }
                    break;
                case CollabFrameType.Log:
                    // Every warning and error of the helper lands here (it folds repeats; CollabLog folds again and
                    // adds the link to the helper's log).
                    if (frame.flag) CollabLog.Error(StripPrefix(frame.text));
                    else if (frame.key == "warning") CollabLog.Warning(StripPrefix(frame.text));
                    else CollabLog.Info(StripPrefix(frame.text));
                    break;
            }
        }

        private static string StripPrefix(string text)
        {
            text = text ?? "";
            return text.StartsWith(CollabLog.Prefix, StringComparison.Ordinal) ? text.Substring(CollabLog.Prefix.Length) : text;
        }

        private static void OnHello(CollabFrame hello)
        {
            if (hello.count != CollabProtocol.Version)
            {
                // Built from older sources: rebuild once this editor session.
                if (SessionState.GetInt(RebuiltKey, 0) != CollabProtocol.Version)
                {
                    Debug.Log($"[Collab] The collaboration helper is out of date (protocol {hello.count}, expected {CollabProtocol.Version}); rebuilding it.");
                    Write(new CollabFrame { type = CollabFrameType.Shutdown });
                    Disconnect();
                    try { File.Delete(StateFile); } catch { }
                    var folder = CollabFolder;
                    if (folder != null) EditorApplication.delayCall += () => Build(folder);
                    _nextAttempt = EditorApplication.timeSinceStartup + 5;
                    return;
                }
                Debug.LogWarning($"[Collab] The collaboration helper speaks protocol {hello.count}, the editor {CollabProtocol.Version}. See {Path.Combine(CollabFolder ?? "Collab~", "build.log")}.");
            }
            // A new helper (or one that can't replay that far back) starts the sequence over.
            if (hello.flag || SessionState.GetString(InstanceKey, "") != hello.id)
                SessionState.SetString(SeqKey, hello.seq.ToString());
            SessionState.SetString(InstanceKey, hello.id);
            _greeted = true;
            var queued = _outbox.ToList();
            _outbox.Clear();
            foreach (var frame in queued) Write(frame);
        }

        private static void ApplyShared(string key, string json)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool changed = json == null ? _shared.Remove(key) : !_shared.TryGetValue(key, out var old) || old != json;
            if (json != null) _shared[key] = json;
            if (changed) CollabSession.RaiseSharedChanged(key);
        }

        private static void ApplyState(CollabFrame state)
        {
            bool wasInSession = _inSession;
            string oldHost = _hostId;
            _local.Id = state.id;
            _inSession = state.flag;
            if (!string.IsNullOrEmpty(state.key)) _projectKey = state.key;
            _hostId = state.host;

            var seen = new HashSet<string>();
            var joined = new List<CollabPeer>();
            var changed = new List<CollabPeer>();
            foreach (var info in state.peers ?? Array.Empty<CollabPeerInfo>())
            {
                if (string.IsNullOrEmpty(info?.id)) continue;
                seen.Add(info.id);
                bool isLocal = info.id == state.id;
                CollabPeer peer;
                bool isNew = false;
                if (isLocal) peer = _local;
                else if (!_peers.TryGetValue(info.id, out peer))
                {
                    peer = new CollabPeer { Id = info.id };
                    _peers[info.id] = peer;
                    isNew = true;
                }
                if (Apply(peer, info, isLocal) && !isNew) changed.Add(peer);
                if (isNew) joined.Add(peer);
            }
            var left = _peers.Values.Where(p => !seen.Contains(p.Id)).ToList();
            foreach (var peer in left) _peers.Remove(peer.Id);

            _peerList.Clear();
            _peerList.Add(_local);
            _peerList.AddRange(_peers.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase));

            if (_inSession && !wasInSession) CollabSession.RaiseConnected();
            foreach (var peer in left) CollabSession.RaisePeerLeft(peer);
            foreach (var peer in joined) CollabSession.RaisePeerJoined(peer);
            foreach (var peer in changed) CollabSession.RaisePeerChanged(peer);
            if (_inSession && oldHost != _hostId && _hostId != null) CollabSession.RaiseHostChanged();
            if (!_inSession && wasInSession) CollabSession.RaiseDisconnected();

            int count = _peerList.Count;
            if (_inSession)
                CollabStatus.Set(count > 1 ? CollabState.InSession : CollabState.Alone,
                    count > 1 ? $"{(_local.IsHost ? "Hosting" : "In a session with")} {count - 1} other{(count == 2 ? "" : "s")}" : "Waiting for others (hosting)");
            else
                CollabStatus.Set(CollabState.Searching, state.text == "migrating" ? "The host left; switching to a new host..." : "Looking for a session...");
        }

        // Copies the helper's view into the peer; true when anything others care about changed.
        private static bool Apply(CollabPeer peer, CollabPeerInfo info, bool isLocal)
        {
            bool changed = false;
            void Set<T>(ref T field, T value) { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; changed = true; } }
            Set(ref peer.Name, string.IsNullOrEmpty(info.name) ? (isLocal ? CollabSettings.EffectiveName : "?") : info.name);
            Set(ref peer.AvatarId, info.avatarId ?? "");
            Set(ref peer.IsHost, info.isHost);
            Set(ref peer.PingMs, isLocal ? 0 : info.pingMs);
            Set(ref peer.IsLocal, isLocal);
            if (isLocal) Set(ref peer.Role, EditorApplication.isPlayingOrWillChangePlaymode ? CollabRole.Playtester : CollabRole.Editor);
            else Set(ref peer.Role, info.role == CollabProtocol.RolePlaytester ? CollabRole.Playtester : CollabRole.Editor);
            Set(ref peer.IsBuild, info.client == CollabProtocol.ClientBuild);
            Set(ref peer.ProtocolVersion, isLocal ? CollabProtocol.Version : info.version);
            if (!string.IsNullOrEmpty(info.color) && ColorUtility.TryParseHtmlString(info.color, out var color)) Set(ref peer.Color, color);

            var presence = new Dictionary<string, string>();
            foreach (var kv in info.presence ?? Array.Empty<CollabKV>())
                if (!string.IsNullOrEmpty(kv?.key)) presence[kv.key] = kv.value ?? "";
            if (isLocal)
                foreach (var pair in _localPresence) presence[pair.Key] = pair.Value;   // ours is newest here
            var merged = MergedPresence(presence, info.gitName, info.gitEmail);
            if (!SameDictionary(peer.Presence, merged)) { peer.Presence = merged; changed = true; }
            return changed;
        }

        private static Dictionary<string, string> MergedPresence(Dictionary<string, string> presence, string gitName, string gitEmail)
        {
            var merged = new Dictionary<string, string>(presence);
            if (!string.IsNullOrEmpty(gitName)) merged["git.name"] = gitName;
            if (!string.IsNullOrEmpty(gitEmail)) merged["git.email"] = gitEmail;
            return merged;
        }

        private static bool SameDictionary(IReadOnlyDictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a == null || a.Count != b.Count) return false;
            foreach (var pair in b)
                if (!a.TryGetValue(pair.Key, out var value) || value != pair.Value) return false;
            return true;
        }

        private static void SetOffline()
        {
            bool was = _inSession;
            _inSession = false;
            var left = _peers.Values.ToList();
            _peers.Clear();
            _peerList.Clear();
            _peerList.Add(_local);
            foreach (var peer in left) CollabSession.RaisePeerLeft(peer);
            if (was) CollabSession.RaiseDisconnected();
        }

        #endregion

        #region Reload and quit

        private static void BeforeReload()
        {
            // Keep auto refresh balanced within this domain; the helper keeps buffering while we're gone.
            if (_pullId != null)
            {
                _pullId = null;
                AssetDatabase.AllowAutoRefresh();
            }
            Disconnect();
        }

        private static void OnQuit()
        {
            // The helper also notices the editor is gone, but telling it lets the others pick a new host at once.
            if (_greeted) Write(new CollabFrame { type = CollabFrameType.Shutdown });
            Disconnect();
        }

        #endregion
    }
}
