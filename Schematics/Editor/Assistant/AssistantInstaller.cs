using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// Installs the assistant without anyone having to ask: the first time it appears, Agent~/setup runs in the
    /// background (fetching .NET if needed, the models, and building the server). It's a separate process, so script
    /// reloads don't interrupt it; the editor just watches its progress and starts the server once it's done.
    /// </summary>
    [InitializeOnLoad]
    internal static class AssistantInstaller
    {
        private const string PidKey = "Schematics.Assistant.InstallPid";

        // Roughly how big each download is, for the progress bar.
        private static (string file, long bytes)[] Downloads => new[]
        {
            AssistantSettings.LlmFile == AssistantSettings.FastModel ? (AssistantSettings.FastModel, 398_000_000L) : (AssistantSettings.StandardModel, 1_117_000_000L),
            ("ggml-base.en.bin", 147_960_000),
            ("kokoro-en-v0_19.tar.bz2", 319_625_534),
        };

        private static Process _process;
        private static double _nextPoll;

        /// <summary>What the install is doing now ("Downloading ... 42%"), or null when it isn't running.</summary>
        public static string Progress { get; private set; }

        /// <summary>0-1 over the whole install, for the bubble's bar.</summary>
        public static float Fraction { get; private set; }

        /// <summary>Raised when the install finishes; true if it succeeded.</summary>
        public static event Action<bool> Finished;

        public static bool IsInstalling => _process != null;

        static AssistantInstaller()
        {
            // An install started before a script reload is still running.
            int pid = SessionState.GetInt(PidKey, 0);
            if (pid != 0)
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (!process.HasExited)
                        Watch(process);
                }
                catch { }
                if (_process == null)
                    SessionState.EraseInt(PidKey);
            }
        }

        /// <summary>Starts installing if the assistant isn't installed yet (and auto-install is on).</summary>
        public static void EnsureInstalled()
        {
            if (AssistantServer.IsInstalled || IsInstalling || !AssistantSettings.AutoInstall)
                return;
            // Don't loop on a broken setup: after a failure, it waits for an explicit retry.
            if (ReadStatus(out string step, out _) && step == "error" && !_retrying)
                return;
            Start();
        }

        private static bool _retrying;

        /// <summary>Runs setup again (after a failure, or to rebuild with another model backend).</summary>
        public static void Reinstall()
        {
            if (IsInstalling)
                return;
            _retrying = true;
            Start();
        }

        private static void Start()
        {
            string agent = AssistantServer.AgentFolder;
            if (agent == null)
            {
                Debug.LogError("[Assistant] Couldn't find the Agent~ folder next to the toolkit.");
                return;
            }

            File.WriteAllText(Path.Combine(agent, "install-status.txt"), "start|Getting ready");
            string backend = AssistantSettings.Backend;
            string root = AssistantServer.InstallRoot;
            try { Directory.CreateDirectory(root); }
            catch (Exception exception)
            {
                Debug.LogError($"[Assistant] Can't use the install folder {root}: {exception.Message}. Pick another in Preferences > Schematics Assistant.");
                File.WriteAllText(Path.Combine(agent, "install-status.txt"), $"error|Can't use the install folder ({exception.Message})");
                return;
            }
            ProcessStartInfo info;
            if (Application.platform == RuntimePlatform.WindowsEditor)
                info = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"& '{Path.Combine(agent, "setup.ps1")}' -Backend {backend} -InstallDir '{root}' -Llm {AssistantSettings.LlmFile} *> '{Path.Combine(agent, "setup.log")}'\"");
            else
                info = new ProcessStartInfo("/bin/bash",
                    $"-c \"bash '{Path.Combine(agent, "setup.sh")}' {backend} '{root}' {AssistantSettings.LlmFile} > '{Path.Combine(agent, "setup.log")}' 2>&1\"");
            info.WorkingDirectory = agent;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            try
            {
                var process = Process.Start(info);
                SessionState.SetInt(PidKey, process.Id);
                Watch(process);
                Debug.Log($"[Assistant] Setting up in the background, in {root}: .NET (if needed), the models (about 1.6 GB) and the server. Sprocket shows the progress.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Assistant] Couldn't run setup: {exception.Message}");
            }
        }

        private static void Watch(Process process)
        {
            _process = process;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            Poll();
        }

        private static void Poll()
        {
            if (_process == null || EditorApplication.timeSinceStartup < _nextPoll)
                return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.5;

            bool exited;
            try { exited = _process.HasExited; }
            catch { exited = true; }

            ReadStatus(out string step, out string message);
            UpdateProgress(step, message);
            if (!exited)
                return;

            EditorApplication.update -= Poll;
            _process = null;
            _retrying = false;
            SessionState.EraseInt(PidKey);
            Progress = null;

            bool ok = step == "done" && AssistantServer.IsInstalled;
            if (ok)
            {
                Debug.Log("[Assistant] Set up. Starting the assistant.");
                _ = AssistantServer.EnsureRunningAsync();
            }
            else
                Debug.LogError($"[Assistant] Setup didn't finish: {message ?? "unknown error"}. Log: {Path.Combine(AssistantServer.AgentFolder ?? "", "setup.log")}");
            Finished?.Invoke(ok);
        }

        private static void UpdateProgress(string step, string message)
        {
            string models = AssistantServer.ModelsFolder;
            long done = 0, total = 0;
            string current = null;
            foreach (var (file, bytes) in Downloads)
            {
                total += bytes;
                if (models == null) continue;
                string path = Path.Combine(models, file);
                if (File.Exists(path) || (file.EndsWith(".tar.bz2") && File.Exists(Path.Combine(models, "kokoro-en-v0_19", "model.onnx"))))
                    done += bytes;
                else if (File.Exists(path + ".part"))
                {
                    long size = new FileInfo(path + ".part").Length;
                    done += Math.Min(size, bytes);
                    current = $"{Math.Min(99, size * 100 / bytes)}%";
                }
            }

            // Downloads are most of the time; .NET and the build share the rest.
            float downloads = total > 0 ? (float)done / total : 0f;
            Fraction = step switch
            {
                "dotnet" => 0.02f,
                "download" => 0.05f + 0.8f * downloads,
                "extract" => 0.87f,
                "build" => 0.9f,
                "done" => 1f,
                _ => Fraction,
            };
            Progress = message == null ? "Setting up…" : current != null && step == "download" ? $"{message} ({current})" : message;
        }

        private static bool ReadStatus(out string step, out string message)
        {
            step = null;
            message = null;
            string agent = AssistantServer.AgentFolder;
            if (agent == null)
                return false;
            string path = Path.Combine(agent, "install-status.txt");
            if (!File.Exists(path))
                return false;
            try
            {
                string line = File.ReadAllText(path).Trim();
                int bar = line.IndexOf('|');
                step = bar >= 0 ? line.Substring(0, bar) : line;
                message = bar >= 0 ? line.Substring(bar + 1) : line;
                return true;
            }
            catch (IOException)
            {
                return false; // being written
            }
        }

        /// <summary>The dotnet setup found or installed (the SDK may be a private copy in Agent~/.dotnet).</summary>
        public static string DotnetPath
        {
            get
            {
                string agent = AssistantServer.AgentFolder;
                string recorded = agent != null ? Path.Combine(agent, "dotnet-path.txt") : null;
                if (recorded != null && File.Exists(recorded))
                {
                    string path = File.ReadAllText(recorded).Trim();
                    if (File.Exists(path))
                        return path;
                }
                return "dotnet";
            }
        }
    }
}
