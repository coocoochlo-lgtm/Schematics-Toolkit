using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Assistant
{
    /// <summary>The assistant's preferences (per user, not per project): Preferences > Schematics Assistant.</summary>
    internal static class AssistantSettings
    {
        private const string Prefix = "Schematics.Assistant.";

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Prefix + "Enabled", true);
            set => EditorPrefs.SetBool(Prefix + "Enabled", value);
        }

        /// <summary>Install the assistant's models and server automatically the first time it appears.</summary>
        public static bool AutoInstall
        {
            get => EditorPrefs.GetBool(Prefix + "AutoInstall", true);
            set => EditorPrefs.SetBool(Prefix + "AutoInstall", value);
        }

        /// <summary>Sprocket's size: 1 = the sprite's own pixels, 2 = doubled, ...</summary>
        public static int SpriteScale
        {
            get => EditorPrefs.GetInt(Prefix + "SpriteScale", 1);
            set => EditorPrefs.SetInt(Prefix + "SpriteScale", value);
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(Prefix + "Port", 5317);
            set => EditorPrefs.SetInt(Prefix + "Port", value);
        }

        /// <summary>
        /// Where the assistant's big files go (.NET, the models, the server, the build's packages). Empty: the toolkit's
        /// Agent~ folder. Per machine, so each person can pick a drive with room.
        /// </summary>
        public static string InstallFolder
        {
            get => EditorPrefs.GetString(Prefix + "InstallFolder", "");
            set => EditorPrefs.SetString(Prefix + "InstallFolder", value ?? "");
        }

        /// <summary>Which Kokoro voice speaks (index into the server's voice list).</summary>
        public static int Voice
        {
            get => EditorPrefs.GetInt(Prefix + "Voice", 1);
            set => EditorPrefs.SetInt(Prefix + "Voice", value);
        }

        public static float SpeechSpeed
        {
            get => EditorPrefs.GetFloat(Prefix + "SpeechSpeed", 1.05f);
            set => EditorPrefs.SetFloat(Prefix + "SpeechSpeed", value);
        }

        public enum ModelChoice { Auto, Standard, Fast }

        /// <summary>
        /// The language model: Standard (Qwen2.5 1.5B, smarter) or Fast (0.5B, about three times quicker). Auto picks
        /// Fast on CPUs the server finds slow (no AVX2, or few cores).
        /// </summary>
        public static ModelChoice Model
        {
            get => (ModelChoice)EditorPrefs.GetInt(Prefix + "Model", 0);
            set => EditorPrefs.SetInt(Prefix + "Model", (int)value);
        }

        /// <summary>What the server last said about this CPU (it can't keep up with the standard model).</summary>
        public static bool SlowCpu
        {
            get => EditorPrefs.GetBool(Prefix + "SlowCpu", false);
            set => EditorPrefs.SetBool(Prefix + "SlowCpu", value);
        }

        public enum CompressionChoice { Q4, Q8 }

        /// <summary>
        /// How tightly the model is compressed. Q4: smaller, usually fastest at writing. Q8: bigger, but simpler to
        /// unpack, which can make reading prompts faster on CPUs without AVX. Compare them with the [llm] lines in server.log.
        /// </summary>
        public static CompressionChoice Compression
        {
            get => (CompressionChoice)EditorPrefs.GetInt(Prefix + "Compression", 0);
            set => EditorPrefs.SetInt(Prefix + "Compression", (int)value);
        }

        public static bool UsesFastModel => Model == ModelChoice.Fast || (Model == ModelChoice.Auto && SlowCpu);

        public const string StandardModel = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
        public const string FastModel = "qwen2.5-0.5b-instruct-q4_k_m.gguf";

        /// <summary>The language model file to download and run.</summary>
        public static string LlmFile => $"qwen2.5-{(UsesFastModel ? "0.5b" : "1.5b")}-instruct-{(Compression == CompressionChoice.Q8 ? "q8_0" : "q4_k_m")}.gguf";

        /// <summary>About how big <see cref="LlmFile"/> is, for the download progress bar.</summary>
        public static long LlmBytes => (UsesFastModel, Compression) switch
        {
            (true, CompressionChoice.Q4) => 491_000_000L,
            (true, CompressionChoice.Q8) => 676_000_000L,
            (false, CompressionChoice.Q4) => 1_117_000_000L,
            _ => 1_894_000_000L,
        };

        public static bool Speak
        {
            get => EditorPrefs.GetBool(Prefix + "Speak", true);
            set => EditorPrefs.SetBool(Prefix + "Speak", value);
        }

        public static string Microphone
        {
            get => EditorPrefs.GetString(Prefix + "Microphone", "");
            set => EditorPrefs.SetString(Prefix + "Microphone", value);
        }

        /// <summary>Seconds between the assistant's steps, so you can follow what it does.</summary>
        public static float StepDelay
        {
            get => EditorPrefs.GetFloat(Prefix + "StepDelay", 0.45f);
            set => EditorPrefs.SetFloat(Prefix + "StepDelay", value);
        }

        /// <summary>Work mode waits for Play (or Step) instead of starting on its own.</summary>
        public static bool WorkStartsPaused
        {
            get => EditorPrefs.GetBool(Prefix + "WorkStartsPaused", false);
            set => EditorPrefs.SetBool(Prefix + "WorkStartsPaused", value);
        }

        /// <summary>The language model build setup makes: Cpu, Cuda12 (NVIDIA) or Vulkan.</summary>
        public static string Backend
        {
            get => EditorPrefs.GetString(Prefix + "Backend", "Cpu");
            set => EditorPrefs.SetString(Prefix + "Backend", value);
        }

        public static bool VerboseLogging
        {
            get => EditorPrefs.GetBool(Prefix + "Verbose", false);
            set => EditorPrefs.SetBool(Prefix + "Verbose", value);
        }

        public static readonly string[] DefaultVoices =
        {
            "af (American female)", "af_bella", "af_nicole", "af_sarah", "af_sky",
            "am_adam", "am_michael", "bf_emma", "bf_isabella", "bm_george", "bm_lewis",
        };

        private static readonly string[] Backends = { "Cpu", "Cuda12", "Vulkan" };

        // The install folder: pick a drive with room. Moving it stops the server; the next install goes there.
        private static void DrawInstallFolder()
        {
            string agent = AssistantServer.AgentFolder;
            string shown = string.IsNullOrEmpty(InstallFolder) ? (agent != null ? agent + "  (default)" : "Agent~ (default)") : InstallFolder;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Install Folder", "Where .NET, the models (about 1.6 GB), the server and the build's packages go. Pick a drive with a few GB free."), new GUIContent(shown, shown));
                using (new EditorGUI.DisabledScope(AssistantInstaller.IsInstalling))
                {
                    if (GUILayout.Button("Choose…", GUILayout.Width(70)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Where should the assistant install?", string.IsNullOrEmpty(InstallFolder) ? "" : InstallFolder, "");
                        if (!string.IsNullOrEmpty(picked))
                            ChangeInstallFolder(System.IO.Path.Combine(picked, "SchematicsAssistant"));
                    }
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(InstallFolder)))
                        if (GUILayout.Button(new GUIContent("Default", "Back to the Agent~ folder"), GUILayout.Width(60)))
                            ChangeInstallFolder("");
                }
            }

            // What's left of an install somewhere it no longer goes (e.g. the old folder on a full C: drive).
            string old = AssistantServer.InstallRoot != agent ? agent : null;
            long leftover = old != null ? AssistantServer.InstallSize(old) : 0;
            if (leftover > 0)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox($"There's an older install in Agent~ ({leftover / 1048576f:0} MB).", MessageType.None);
                    if (GUILayout.Button("Delete It", GUILayout.Width(80), GUILayout.Height(38))
                        && EditorUtility.DisplayDialog("Delete the old install?", $"Deletes .NET, the models and the server from\n{old}\n(the new folder is unaffected).", "Delete", "Cancel"))
                        AssistantServer.DeleteInstall(old);
                }
        }

        private static void ChangeInstallFolder(string folder)
        {
            if (folder == InstallFolder)
                return;
            AssistantServer.Stop();
            InstallFolder = folder;
            if (!AssistantServer.IsInstalled && EditorUtility.DisplayDialog("Install there now?",
                    $"The assistant will install into\n{AssistantServer.InstallRoot}\n(.NET if needed, about 1.6 GB of models, and the server).", "Install", "Later"))
                AssistantInstaller.Reinstall();
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Preferences/Schematics Assistant", SettingsScope.User)
        {
            keywords = new HashSet<string> { "assistant", "voice", "kokoro", "whisper", "qwen", "push to talk", "Sprocket" },
            guiHandler = searchContext =>
            {
                EditorGUIUtility.labelWidth = 180;
                bool enabled = EditorGUILayout.Toggle(new GUIContent("Show Sprocket", "It appears with the first graph window, and stays wherever you drop it."), Enabled);
                if (enabled != Enabled)
                {
                    Enabled = enabled;
                    if (!enabled) AssistantOverlay.Hide();
                }
                int scale = EditorGUILayout.IntSlider(new GUIContent("Size", "Whole multiples keep the pixel art crisp. Applies the next time it appears."), SpriteScale, 1, 4);
                if (scale != SpriteScale) SpriteScale = scale;

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Voice", EditorStyles.boldLabel);
                Speak = EditorGUILayout.Toggle("Speak Replies", Speak);
                using (new EditorGUI.DisabledScope(!Speak))
                {
                    Voice = EditorGUILayout.Popup("Voice", Mathf.Clamp(Voice, 0, DefaultVoices.Length - 1), DefaultVoices);
                    SpeechSpeed = EditorGUILayout.Slider("Speed", SpeechSpeed, 0.6f, 1.6f);
                    if (GUILayout.Button("Preview Voice", GUILayout.Width(120)))
                        AssistantVoice.SayAsync("Hi! I'm Sprocket. Tell me what to build.");
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Listening", EditorStyles.boldLabel);
                var devices = UnityEngine.Microphone.devices;
                var options = new List<string> { "Default" };
                options.AddRange(devices);
                int current = Mathf.Max(0, options.IndexOf(Microphone));
                int picked = EditorGUILayout.Popup("Microphone", current, options.ToArray());
                Microphone = picked == 0 ? "" : options[picked];
                EditorGUILayout.HelpBox("Hold Q over a Schematic Graph window to talk (rebind it in Edit > Shortcuts > Schematics/Assistant).", MessageType.None);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Behavior", EditorStyles.boldLabel);
                StepDelay = EditorGUILayout.Slider(new GUIContent("Step Delay", "Pause between edits so you can follow along."), StepDelay, 0f, 1.5f);
                WorkStartsPaused = EditorGUILayout.Toggle(new GUIContent("Start Work Paused", "Show Sprocket's to-do list and wait for Play or Step before he changes anything."), WorkStartsPaused);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
                DrawInstallFolder();
                Port = EditorGUILayout.IntField("Port", Port);
                int backend = System.Array.IndexOf(Backends, Backend);
                Backend = Backends[EditorGUILayout.Popup(new GUIContent("Model Backend", "Cuda12 for NVIDIA GPUs, Vulkan for other GPUs. Reinstall after changing it."), Mathf.Max(0, backend), Backends)];
                EditorGUI.BeginChangeCheck();
                var model = (ModelChoice)EditorGUILayout.EnumPopup(new GUIContent("Language Model",
                    "Standard: Qwen2.5 1.5B, smarter. Fast: 0.5B, about three times quicker on a slow CPU. Auto: Fast when this CPU is slow"
                    + (SlowCpu ? " (it is)." : " (it isn't, as far as the server has seen).")), Model);
                var compression = (CompressionChoice)EditorGUILayout.EnumPopup(new GUIContent("Model Compression",
                    "Q4: smaller, usually fastest. Q8: bigger, simpler to unpack: can read prompts faster on CPUs without AVX. "
                    + "Compare the [llm] speed lines in server.log."), Compression);
                if (EditorGUI.EndChangeCheck() && (model != Model || compression != Compression))
                {
                    string before = LlmFile;
                    Model = model;
                    Compression = compression;
                    // A model that isn't downloaded yet: set it up now (the server restarts with it).
                    if (LlmFile != before)
                    {
                        AssistantServer.Stop();
                        if (!AssistantServer.IsInstalled) AssistantInstaller.Reinstall();
                    }
                }
                VerboseLogging = EditorGUILayout.Toggle("Log Server Output", VerboseLogging);
                AutoInstall = EditorGUILayout.Toggle(new GUIContent("Install Automatically", "Download the models (about 1.6 GB) and build the server the first time the assistant appears."), AutoInstall);
                EditorGUILayout.LabelField("Status", AssistantInstaller.IsInstalling ? AssistantInstaller.Progress ?? "Installing…"
                    : AssistantServer.StartupStatus != null ? $"Starting: loading {AssistantServer.StartupStatus} ({AssistantServer.StartupProgress * 100f:0}%)"
                    : AssistantServer.IsRunning ? "Running" : AssistantServer.IsInstalled ? "Installed, stopped" : "Not installed");
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(AssistantInstaller.IsInstalling))
                        if (GUILayout.Button(AssistantServer.IsInstalled ? "Reinstall" : "Install")) { AssistantServer.Stop(); AssistantInstaller.Reinstall(); }
                    if (GUILayout.Button("Start")) _ = AssistantServer.EnsureRunningAsync();
                    if (GUILayout.Button("Stop")) AssistantServer.Stop();
                    using (new EditorGUI.DisabledScope(!AssistantServer.IsRunning))
                        if (GUILayout.Button(new GUIContent("Forget Answers", "Sprocket remembers his chat replies and explanations so asking again is instant. This clears them.")))
                            _ = AssistantServer.ForgetAnswersAsync();
                }
            },
        };
    }

    /// <summary>A small JSON writer for the editor snapshot (JsonUtility can't write free-form objects).</summary>
    internal sealed class AssistantJson
    {
        private readonly System.Text.StringBuilder _sb = new();
        private readonly Stack<bool> _hasItems = new();

        public static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new System.Text.StringBuilder(text.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private void Comma()
        {
            if (_hasItems.Count == 0) return;
            if (_hasItems.Peek()) _sb.Append(',');
            else { _hasItems.Pop(); _hasItems.Push(true); }
        }

        private void Key(string name) => _sb.Append('"').Append(Escape(name)).Append("\":");

        public AssistantJson BeginObject(string name = null) { Comma(); if (name != null) Key(name); _sb.Append('{'); _hasItems.Push(false); return this; }
        public AssistantJson EndObject() { _sb.Append('}'); _hasItems.Pop(); return this; }
        public AssistantJson BeginArray(string name = null) { Comma(); if (name != null) Key(name); _sb.Append('['); _hasItems.Push(false); return this; }
        public AssistantJson EndArray() { _sb.Append(']'); _hasItems.Pop(); return this; }

        public AssistantJson String(string name, string value) { Comma(); Key(name); _sb.Append('"').Append(Escape(value)).Append('"'); return this; }
        public AssistantJson Value(string value) { Comma(); _sb.Append('"').Append(Escape(value)).Append('"'); return this; }
        public AssistantJson Number(string name, float value)
        {
            Comma(); Key(name);
            _sb.Append(Mathf.Round(value).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return this;
        }

        public override string ToString() => _sb.ToString();
    }
}
