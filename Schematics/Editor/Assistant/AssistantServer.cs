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

        private static string ServerDll(string agent) => Path.Combine(agent, "bin", "SchematicsAgent.dll");

        public static bool IsInstalled
        {
            get
            {
                string agent = AgentFolder;
                return agent != null && File.Exists(ServerDll(agent))
                    && File.Exists(Path.Combine(agent, "models", "kokoro-en-v0_19", "model.onnx"));
            }
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

        private static async Task<bool> StartAsync()
        {
            if (await IsHealthyAsync())
                return true;

            string agent = AgentFolder;
            if (agent == null || !IsInstalled)
            {
                // Installs itself; the overlay shows the progress and the server starts when it's done.
                AssistantInstaller.EnsureInstalled();
                return false;
            }

            if (!IsRunning)
            {
                var info = new ProcessStartInfo(AssistantInstaller.DotnetPath,
                    $"\"{ServerDll(agent)}\" --port {AssistantSettings.Port} --models \"{Path.Combine(agent, "models")}\"")
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

            // Loading the models takes a few seconds.
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(500);
                if (await IsHealthyAsync())
                    return true;
                if (!IsRunning)
                {
                    Debug.LogError("[Assistant] The server stopped while starting; see the messages above.");
                    return false;
                }
            }
            Debug.LogError("[Assistant] The server didn't answer within a minute.");
            return false;
        }

        private static void Log(string line, bool error)
        {
            if (string.IsNullOrEmpty(line))
                return;
            // Called on a reader thread.
            EditorApplication.delayCall += () =>
            {
                if (error) Debug.LogWarning("[Assistant server] " + line);
                else if (AssistantSettings.VerboseLogging || line.StartsWith("[")) Debug.Log("[Assistant server] " + line);
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
