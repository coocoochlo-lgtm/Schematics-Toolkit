using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Runs the assistant's local server (Agent~/, a .NET program with the language, speech and voice models) in the
    /// background and talks to it over localhost. Started on demand, kept across script reloads, stopped with the editor.
    /// </summary>
    [InitializeOnLoad]
    internal static class AssistantServer
    {
        private const string PidKey = "Schematics.Assistant.ServerPid";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
        // Asking, speaking and listening run the models, which take minutes on a slow CPU.
        private static readonly HttpClient _slow = new() { Timeout = TimeSpan.FromMinutes(10) };

        /// <summary>The server version this editor code expects (AgentSettings.Version on the server).</summary>
        private const int ServerVersion = 9;
        private const string RebuiltKey = "Schematics.Assistant.RebuiltForVersion";

        // A slow CPU (the server says) gets the fast model, when the choice is Auto: set up, then started with it.
        private static void NoteCpu(HealthReply health)
        {
            if (!health.slow || AssistantSettings.SlowCpu)
                return;
            AssistantSettings.SlowCpu = true;
            if (AssistantSettings.Model != AssistantSettings.ModelChoice.Auto)
                return;
            Debug.Log($"[Assistant] This CPU is slow for the standard language model ({health.cpu}). Switching to the fast one (about 400 MB to download); "
                + "change it in Preferences > Schematics Assistant > Language Model.");
            EditorApplication.delayCall += () =>
            {
                Stop();
                AssistantInstaller.Reinstall();
            };
        }

        /// <summary>What the server is doing for a request ("reading 1200 tokens", ...), while it does it.</summary>
        public static string Activity { get; private set; }
        private static bool _polling;

        /// <summary>Asks the server what it's doing (for Sprocket's bubble while it thinks). Cheap; one at a time.</summary>
        public static async void PollActivity()
        {
            if (_polling) return;
            _polling = true;
            try { Activity = (await GetStateAsync()).health?.activity; }
            finally { _polling = false; }
        }
        private static Process _process;
        private static Task<bool> _starting;

        public static string BaseUrl => $"http://127.0.0.1:{AssistantSettings.Port}";

        static AssistantServer()
        {
            // A server started before a script reload is still ours.
            int pid = SessionState.GetInt(PidKey, 0);
            if (pid != 0)
            {
                try
                {
                    _process = Process.GetProcessById(pid);
                    if (_process.HasExited) _process = null;
                    else WatchExit(_process);
                }
                catch { _process = null; }
                if (_process == null)
                    SessionState.EraseInt(PidKey);
            }
            EditorApplication.quitting += Stop;
        }

        #region Locating

        /// <summary>The Agent~ folder: next to the toolkit's own folders, wherever the toolkit is installed.</summary>
        public static string AgentFolder
        {
            get
            {
                foreach (var guid in AssetDatabase.FindAssets("AssistantServer t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/AssistantServer.cs", StringComparison.Ordinal))
                        continue;
                    for (var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))); dir != null; dir = dir.Parent)
                    {
                        string candidate = Path.Combine(dir.FullName, "Agent~");
                        if (Directory.Exists(candidate))
                            return candidate;
                    }
                }
                return null;
            }
        }

        /// <summary>
        /// Where the big files are (.NET, models, server): the Install Folder from Preferences, or Agent~. Setup's status
        /// and log stay in Agent~ either way.
        /// </summary>
        public static string InstallRoot
        {
            get
            {
                string chosen = AssistantSettings.InstallFolder;
                return !string.IsNullOrEmpty(chosen) ? Path.GetFullPath(chosen) : AgentFolder;
            }
        }

        public static string ModelsFolder => InstallRoot != null ? Path.Combine(InstallRoot, "models") : null;

        private static string ServerDll(string root) => Path.Combine(root, "bin", "SchematicsAgent.dll");

        public static bool IsInstalled
        {
            get
            {
                string root = InstallRoot;
                return root != null && File.Exists(ServerDll(root))
                    && File.Exists(Path.Combine(root, "models", "kokoro-en-v0_19", "model.onnx"))
                    && File.Exists(Path.Combine(root, "models", AssistantSettings.LlmFile));
            }
        }

        // What setup puts in an install folder.
        private static readonly string[] InstallParts = { ".dotnet", "models", "bin", "nuget", "tmp" };

        /// <summary>How many bytes an install takes in a folder (0 if there's none).</summary>
        public static long InstallSize(string root)
        {
            long total = 0;
            foreach (var part in InstallParts)
            {
                string path = Path.Combine(root, part);
                if (!Directory.Exists(path)) continue;
                try
                {
                    foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
                        total += file.Length;
                }
                catch (Exception) { }
            }
            return total;
        }

        /// <summary>Deletes an install from a folder (.NET, models, server, packages), keeping the folder's other files.</summary>
        public static void DeleteInstall(string root)
        {
            if (root == InstallRoot) Stop();
            foreach (var part in InstallParts)
            {
                string path = Path.Combine(root, part);
                try { if (Directory.Exists(path)) Directory.Delete(path, true); }
                catch (Exception exception) { Debug.LogWarning($"[Assistant] Couldn't delete {path}: {exception.Message}"); }
            }
            // A dotnet-path.txt pointing into the deleted copy would be stale.
            string recorded = AgentFolder != null ? Path.Combine(AgentFolder, "dotnet-path.txt") : null;
            if (recorded != null && File.Exists(recorded) && File.ReadAllText(recorded).Trim().StartsWith(Path.GetFullPath(root)))
                File.Delete(recorded);
        }

        public static bool IsRunning => _process != null && !SafeHasExited(_process);

        private static bool SafeHasExited(Process process)
        {
            try { return process.HasExited; }
            catch { return true; }
        }

        #endregion

        #region Setup and lifetime

        /// <summary>Starts the server if it isn't running, and waits until it answers. False if it can't.</summary>
        public static Task<bool> EnsureRunningAsync()
        {
            if (_starting != null && !_starting.IsCompleted)
                return _starting;
            _starting = StartAsync();
            return _starting;
        }

        /// <summary>What the server is loading while it starts ("the language model"), or null.</summary>
        public static string StartupStatus { get; private set; }

        /// <summary>How far the server's loading is (0-1), which step of how many, and for how long (seconds).</summary>
        public static float StartupProgress { get; private set; }
        public static int StartupStep { get; private set; }
        public static int StartupSteps { get; private set; }
        public static double StartupElapsed { get; private set; }

        // Loading can take minutes on a slow disk or CPU; it only counts as stuck when nothing moves for this long.
        private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(3);

        private static async Task<bool> StartAsync()
        {
            var state = await GetStateAsync();
            if (state.alive && state.health != null && state.health.version != ServerVersion)
            {
                // Left from before an update (it'd lack fixes and hold the port): replace it.
                Debug.Log($"[Assistant] An older server is running (version {state.health.version}, expected {ServerVersion}); restarting it.");
                Stop();
                if (InstallRoot is { } installed) StopStale(installed);
                for (int i = 0; i < 20 && !PortFree(AssistantSettings.Port); i++)
                    await Task.Delay(250);
                state = default;
            }
            else if (state.ready)
                return true;

            // Mid-reinstall the old files are about to be replaced; a server started now would lock them and fail the build.
            if (AssistantInstaller.IsInstalling)
                return false;

            string agent = AgentFolder;
            if (agent == null || !IsInstalled)
            {
                // Installs itself; the overlay shows the progress and the server starts when it's done.
                AssistantInstaller.EnsureInstalled();
                return false;
            }

            bool launched = false;
            if (!state.alive && !IsRunning)
            {
                string root = InstallRoot;
                // A server left from before (an editor crash, an older version) can hold the port without answering.
                StopStale(root);
                if (!PortFree(AssistantSettings.Port))
                {
                    Debug.LogError($"[Assistant] Port {AssistantSettings.Port} is in use by another program, so the server can't start. "
                        + "Pick another Port in Preferences > Schematics Assistant (or close whatever is using it).");
                    return false;
                }
                var info = new ProcessStartInfo(AssistantInstaller.DotnetPath,
                    $"\"{ServerDll(root)}\" --port {AssistantSettings.Port} --models \"{Path.Combine(root, "models")}\" --reference \"{Path.Combine(agent, "reference")}\" --llm {AssistantSettings.LlmFile}")
                {
                    WorkingDirectory = agent,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                try
                {
                    _stopping = false;
                    _process = Process.Start(info);
                    WatchExit(_process);
                    launched = true;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Assistant] Couldn't start the server: {exception.Message}. Is the .NET 10 runtime installed?");
                    return false;
                }
                SessionState.SetInt(PidKey, _process.Id);
                _process.OutputDataReceived += (_, e) => Log(e.Data, error: false);
                _process.ErrorDataReceived += (_, e) => Log(e.Data, error: true);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            // The server answers within seconds and loads its models in the background; wait until it says it's ready.
            var started = DateTime.UtcNow;
            var lastChange = started;
            string lastSeen = null;
            bool everAnswered = false, warned = false;
            StartupStatus = "starting";
            StartupProgress = 0f;
            StartupStep = StartupSteps = 0;
            StartupElapsed = 0;
            try
            {
                while (DateTime.UtcNow - lastChange < StallTimeout)
                {
                    await Task.Delay(500);
                    state = await GetStateAsync();
                    if (state.ready && state.health != null && state.health.version != ServerVersion)
                    {
                        // The installed build is older than this toolkit: rebuild it (once per editor session, in case
                        // the build itself keeps failing).
                        Stop();
                        if (!SessionState.GetBool(RebuiltKey, false))
                        {
                            SessionState.SetBool(RebuiltKey, true);
                            Debug.Log($"[Assistant] The installed server is out of date (version {state.health.version}, expected {ServerVersion}); rebuilding it.");
                            AssistantInstaller.Reinstall();
                        }
                        else
                            Debug.LogError($"[Assistant] The server is still out of date after rebuilding (version {state.health.version}, expected {ServerVersion}). See {Path.Combine(AgentFolder ?? "", "setup.log")}.");
                        return false;
                    }
                    if (state.ready && state.health != null)
                        NoteCpu(state.health);
                    if (state.ready)
                    {
                        if (!string.IsNullOrEmpty(state.error))
                            Debug.LogWarning("[Assistant] " + state.error);
                        return true;
                    }
                    if (!state.alive)
                    {
                        // Not answering yet: the bar can't move, but the clock does.
                        StartupStatus = "starting";
                        StartupStep = StartupSteps = 0;
                        StartupElapsed = (DateTime.UtcNow - started).TotalSeconds;
                        if (!warned && StartupElapsed > 30)
                        {
                            warned = true;
                            Debug.LogWarning(launched
                                ? $"[Assistant] The server has been running for 30 s but doesn't answer on port {AssistantSettings.Port}. A firewall or antivirus may be blocking it, or it's stuck starting up. Everything it printed is in {LogPath}."
                                : $"[Assistant] The server is running but has stopped answering on port {AssistantSettings.Port}. Press Stop, then Start, in Preferences > Schematics Assistant. Everything it printed is in {LogPath}.");
                        }
                    }
                    if (state.alive)
                    {
                        everAnswered = true;
                        StartupStatus = string.IsNullOrEmpty(state.loading) ? "starting" : state.loading;
                        StartupProgress = state.health?.progress ?? 0f;
                        StartupStep = state.health?.step ?? 0;
                        StartupSteps = state.health?.steps ?? 0;
                        StartupElapsed = state.health?.elapsed ?? (DateTime.UtcNow - started).TotalSeconds;
                        string seen = $"{StartupStatus}|{StartupProgress:0.000}";
                        if (seen != lastSeen)
                        {
                            lastSeen = seen;
                            lastChange = DateTime.UtcNow;
                        }
                    }
                    else if (!IsRunning)
                    {
                        Debug.LogError("[Assistant] The server stopped while starting; see the messages above.");
                        return false;
                    }
                }
                if (!everAnswered)
                {
                    Debug.LogError($"[Assistant] The server never answered on port {AssistantSettings.Port} in {StallTimeout.TotalMinutes:0} minutes, so I stopped it. "
                        + "Check for a firewall or antivirus prompt, or try another Port in Preferences > Schematics Assistant. Turn on Log Server Output there to see what it printed.");
                    Stop();
                    return false;
                }
                Debug.LogError($"[Assistant] The server has been stuck loading {StartupStatus} ({StartupProgress * 100f:0}%) for {StallTimeout.TotalMinutes:0} minutes. "
                    + "Is the disk or CPU very busy (an antivirus scanning the model files)? Turn on Log Server Output in Preferences > Schematics Assistant and press Start to see more.");
                return false;
            }
            finally
            {
                StartupStatus = null;
            }
        }

        /// <summary>The server's own record of everything it printed (it writes it in the install folder).</summary>
        public static string LogPath => InstallRoot is { } root ? Path.Combine(root, "server.log") : "server.log";

        // Set when we stop the server on purpose, so its exit isn't reported as a crash.
        private static bool _stopping;

        private static void WatchExit(Process process)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) =>
                {
                    int code;
                    try { code = process.ExitCode; }
                    catch { return; }
                    if (_stopping) return;
                    EditorApplication.delayCall += () => Debug.LogError(ExitMessage(code));
                };
            }
            catch { }
        }

        private static string ExitMessage(int code)
        {
            string why = unchecked((uint)code) switch
            {
                0xC000001D => " This CPU is missing an instruction one of the models needs (an older CPU without AVX, or a virtual machine).",
                0xC0000005 => " A native library crashed (an access violation).",
                0xC0000409 => " A native library crashed (a stack buffer overrun).",
                0xE0434352 => " An unhandled .NET exception; it's at the end of the log.",
                _ => "",
            };
            return $"[Assistant] The server stopped unexpectedly (exit code 0x{code:X8}).{why} Its log: {LogPath}";
        }

        private static void Log(string line, bool error)
        {
            if (string.IsNullOrEmpty(line))
                return;
            // Native libraries write their progress to the error stream too; only real problems are warnings.
            bool problem = error && System.Text.RegularExpressions.Regex.IsMatch(line, @"error|fail|exception|unhandled|cannot|could not|couldn't", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool show = problem || AssistantSettings.VerboseLogging || (!error && line.StartsWith("["));
            if (!show)
                return;
            // Called on a reader thread.
            EditorApplication.delayCall += () =>
            {
                if (problem) Debug.LogWarning("[Assistant server] " + line);
                else Debug.Log("[Assistant server] " + line);
            };
        }

        // The server writes its process id here, so one this editor lost track of can still be stopped.
        private static string PidFile(string root) => Path.Combine(root, "server.pid");

        private static void StopStale(string root)
        {
            try
            {
                string file = PidFile(root);
                if (!File.Exists(file) || !int.TryParse(File.ReadAllText(file).Trim(), out int pid))
                    return;
                File.Delete(file);
                if (_process != null && SafeId(_process) == pid)
                    return;
                var old = Process.GetProcessById(pid);
                // Only a .NET process (the id may have been reused by something else since).
                if (old.HasExited || !old.ProcessName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
                    return;
                old.Kill();
                old.WaitForExit(3000);
                Debug.Log($"[Assistant] Stopped an old server that was still running (process {pid}).");
            }
            catch { }
        }

        private static int SafeId(Process process)
        {
            try { return process.Id; }
            catch { return -1; }
        }

        private static bool PortFree(int port)
        {
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch { return false; }
        }

        public static void Stop()
        {
            if (_process == null)
            {
                if (InstallRoot is { } root)
                    StopStale(root);
                return;
            }
            _stopping = true;
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch { }
            _process = null;
            SessionState.EraseInt(PidKey);
        }

        #endregion

        #region Requests

        /// <summary>Whether the server answers (alive), has loaded its models (ready), and what it's loading.</summary>
        public static async Task<(bool alive, bool ready, string loading, string error, HealthReply health)> GetStateAsync()
        {
            try
            {
                // Generous: a busy CPU (the model answering someone) can be slow to answer this too.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _http.GetAsync(BaseUrl + "/health", cts.Token);
                if (!response.IsSuccessStatusCode)
                    return (false, false, null, null, null);
                string text = await response.Content.ReadAsStringAsync();
                var health = JsonUtility.FromJson<HealthReply>(text);
                // Older servers didn't say "ready": answering meant loaded.
                bool ready = health == null || health.ready || !text.Contains("\"ready\"");
                return (true, ready, health?.loading, health?.error, health);
            }
            catch
            {
                return (false, false, null, null, null);
            }
        }

        // HttpClient's timeout shows up as a cancellation (.NET) or a WebException inside an HttpRequestException (Mono).
        private static bool IsTimeout(Exception exception)
        {
            for (var e = exception; e != null; e = e.InnerException)
                if (e is TaskCanceledException || e is TimeoutException || (e is WebException web && web.Status == WebExceptionStatus.Timeout))
                    return true;
            return false;
        }

        [Serializable]
        private class ErrorReply { public string error; }

        /// <summary>What /health says.</summary>
        [Serializable]
        public class HealthReply
        {
            public bool ready;
            public string loading, error;
            public float progress;
            public int version;
            public bool slow;
            public string cpu, activity;
            public int step, steps;
            public double elapsed;
        }

        /// <summary>Whether the server answers at all (it may still be loading its models).</summary>
        public static async Task<bool> IsHealthyAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                var response = await _http.GetAsync(BaseUrl + "/health", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> GetHealthAsync()
        {
            try { return await _http.GetStringAsync(BaseUrl + "/health"); }
            catch { return null; }
        }

        /// <summary>Sends a request and what's open in the editor; returns the reply JSON ({ say, commands[] }).</summary>
        public static async Task<string> AskAsync(string utterance, string contextJson, CancellationToken cancellation)
        {
            string body = "{\"utterance\":\"" + AssistantJson.Escape(utterance) + "\",\"context\":" + contextJson + "}";
            HttpResponseMessage response;
            try
            {
                response = await _slow.PostAsync(BaseUrl + "/ask", new StringContent(body, Encoding.UTF8, "application/json"), cancellation);
            }
            catch (Exception exception) when (!cancellation.IsCancellationRequested && IsTimeout(exception))
            {
                throw new TimeoutException($"I took longer than {_slow.Timeout.TotalMinutes:0} minutes to think, so I gave up. This computer runs my model slowly; "
                    + $"the [llm] lines in {LogPath} say how fast.");
            }
            catch (HttpRequestException) when (!IsRunning)
            {
                throw new Exception($"My server stopped while I was thinking (see {LogPath}).");
            }
            string text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"The assistant server failed ({(int)response.StatusCode}): {text}");
            return text;
        }

        /// <summary>Speech to text: 16 kHz mono WAV in, the words out.</summary>
        public static async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellation)
        {
            var content = new ByteArrayContent(wav);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            var response = await _slow.PostAsync(BaseUrl + "/transcribe", content, cancellation);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Transcription failed ({(int)response.StatusCode}): {json}");
            return JsonUtility.FromJson<TranscribeReply>(json)?.text ?? "";
        }

        /// <summary>Text to speech with a Kokoro voice: WAV bytes, or null if the voice model isn't available.</summary>
        public static async Task<byte[]> SpeakAsync(string text, int voice, float speed, CancellationToken cancellation)
        {
            string body = "{\"text\":\"" + AssistantJson.Escape(text) + "\",\"voice\":" + voice + ",\"speed\":"
                + speed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            HttpResponseMessage response;
            try
            {
                response = await _slow.PostAsync(BaseUrl + "/speak", new StringContent(body, Encoding.UTF8, "application/json"), cancellation);
            }
            catch (HttpRequestException) when (!IsRunning)
            {
                throw new Exception($"the server stopped while making the speech (see {LogPath})");
            }
            catch (Exception exception) when (!cancellation.IsCancellationRequested && IsTimeout(exception))
            {
                throw new Exception($"the server took longer than {_slow.Timeout.TotalMinutes:0} minutes to make the speech");
            }
            if (!response.IsSuccessStatusCode)
            {
                // The server says why (as {"error": ...}), or its log does.
                string reply = await response.Content.ReadAsStringAsync();
                string error = reply.Contains("\"error\"") ? JsonUtility.FromJson<ErrorReply>(reply)?.error : null;
                throw new Exception(string.IsNullOrEmpty(error) ? $"the server answered {(int)response.StatusCode} (see {LogPath})" : error);
            }
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>Clears the answers the server remembers (answers.json).</summary>
        public static async Task ForgetAnswersAsync()
        {
            try
            {
                var response = await _http.PostAsync(BaseUrl + "/answers/clear", new StringContent("", Encoding.UTF8, "application/json"));
                Debug.Log(response.IsSuccessStatusCode ? "[Assistant] Sprocket forgot his remembered answers." : $"[Assistant] Couldn't clear the answers ({(int)response.StatusCode}).");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Assistant] Couldn't clear the answers: " + exception.Message);
            }
        }

        /// <summary>Empties the server's voice cache (speech it kept so saying it again is instant).</summary>
        public static async Task ClearVoiceCacheAsync()
        {
            try
            {
                var response = await _http.PostAsync(BaseUrl + "/voice/clear", new StringContent("", Encoding.UTF8, "application/json"));
                Debug.Log(response.IsSuccessStatusCode ? "[Assistant] Cleared Sprocket's voice cache." : $"[Assistant] Couldn't clear the voice cache ({(int)response.StatusCode}).");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Assistant] Couldn't clear the voice cache: " + exception.Message);
            }
        }

        /// <summary>Hands the server every [SchematicDoc] (see SchematicApiReference); returns its counts.</summary>
        public static async Task<string> SyncReferenceAsync(string manifest)
        {
            try
            {
                var response = await _http.PostAsync(BaseUrl + "/reference/sync", new StringContent(manifest, Encoding.UTF8, "application/json"));
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string[]> VoicesAsync()
        {
            try
            {
                string json = await _http.GetStringAsync(BaseUrl + "/voices");
                return JsonUtility.FromJson<VoicesReply>(json)?.voices ?? Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        [Serializable] private class TranscribeReply { public string text; }
        [Serializable] private class VoicesReply { public string[] voices; }

        #endregion
    }
}
