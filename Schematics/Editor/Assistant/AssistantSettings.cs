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

        public static int Port
        {
            get => EditorPrefs.GetInt(Prefix + "Port", 5317);
            set => EditorPrefs.SetInt(Prefix + "Port", value);
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

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Preferences/Schematics Assistant", SettingsScope.User)
        {
            keywords = new HashSet<string> { "assistant", "voice", "kokoro", "whisper", "qwen", "push to talk", "Sprocket" },
            guiHandler = searchContext =>
            {
                EditorGUIUtility.labelWidth = 180;
                Enabled = EditorGUILayout.Toggle(new GUIContent("Show in Graph Windows"), Enabled);

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

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);
                Port = EditorGUILayout.IntField("Port", Port);
                int backend = System.Array.IndexOf(Backends, Backend);
                Backend = Backends[EditorGUILayout.Popup(new GUIContent("Model Backend", "Used by Set Up: Cuda12 for NVIDIA GPUs, Vulkan for other GPUs."), Mathf.Max(0, backend), Backends)];
                VerboseLogging = EditorGUILayout.Toggle("Log Server Output", VerboseLogging);
                EditorGUILayout.LabelField("Status", AssistantServer.IsRunning ? "Running" : AssistantServer.IsInstalled ? "Installed, stopped" : "Not set up");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Set Up…")) AssistantServer.RunSetup();
                    if (GUILayout.Button("Start")) _ = AssistantServer.EnsureRunningAsync();
                    if (GUILayout.Button("Stop")) AssistantServer.Stop();
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
