using System;
using System.Diagnostics;
using System.IO;
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
                    && File.Exists(Path.Combine(root, "models", "kokoro-en-v0_19", "model.onnx"));
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
            if (state.ready)
                return true;

            string agent = AgentFolder;
            if (agent == null || !IsInstalled)
            {
                // Installs itself; the overlay shows the progress and the server starts when it's done.
                AssistantInstaller.EnsureInstalled();
                return false;
            }

            if (!state.alive && !IsRunning)
            {
                string root = InstallRoot;
                var info = new ProcessStartInfo(AssistantInstaller.DotnetPath,
                    $"\"{ServerDll(root)}\" --port {AssistantSettings.Port} --models \"{Path.Combine(root, "models")}\" --reference \"{Path.Combine(agent, "reference")}\"")
                {
                    WorkingDirectory = agent,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                try
                {
                    _process = Process.Start(info);
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
            StartupStatus = "starting";
            try
            {
                while (DateTime.UtcNow - lastChange < StallTimeout)
                {
                    await Task.Delay(500);
                    state = await GetStateAsync();
                    if (state.ready)
                    {
                        if (!string.IsNullOrEmpty(state.error))
                            Debug.LogWarning("[Assistant] " + state.error);
                        return true;
                    }
                    if (state.alive)
                    {
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
                Debug.LogError($"[Assistant] The server has been stuck loading {StartupStatus} ({StartupProgress * 100f:0}%) for {StallTimeout.TotalMinutes:0} minutes. "
                    + "Is the disk or CPU very busy (an antivirus scanning the model files)? Turn on Log Server Output in Preferences > Schematics Assistant and press Start to see more.");
                return false;
            }
            finally
            {
                StartupStatus = null;
            }
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

        public static void Stop()
        {
            if (_process == null)
                return;
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
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

        /// <summary>What /health says.</summary>
        [Serializable]
        public class HealthReply
        {
            public bool ready;
            public string loading, error;
            public float progress;
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
            var response = await _http.PostAsync(BaseUrl + "/ask", new StringContent(body, Encoding.UTF8, "application/json"), cancellation);
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
            var response = await _http.PostAsync(BaseUrl + "/transcribe", content, cancellation);
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
            var response = await _http.PostAsync(BaseUrl + "/speak", new StringContent(body, Encoding.UTF8, "application/json"), cancellation);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsByteArrayAsync();
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
