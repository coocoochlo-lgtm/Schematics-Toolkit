using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Collaboration preferences (Preferences > Schematics > Collaboration). They're per user and machine (EditorPrefs),
    /// because who you are and how your network looks isn't part of the project.
    /// </summary>
    public static class CollabSettings
    {
        private const string Prefix = "Schematics.Collab.";

        /// <summary>Raised when any setting changes, so the bridge can tell the helper (or restart it).</summary>
        public static event Action Changed;

        /// <summary>Live collaboration on or off. On by default: it needs no setup.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Prefix + "Enabled", true);
            set { EditorPrefs.SetBool(Prefix + "Enabled", value); Changed?.Invoke(); }
        }

        /// <summary>The name others see. Empty means your git user.name, else your system user name.</summary>
        public static string DisplayName
        {
            get => EditorPrefs.GetString(Prefix + "DisplayName", "");
            set { EditorPrefs.SetString(Prefix + "DisplayName", value ?? ""); Changed?.Invoke(); }
        }

        /// <summary>
        /// Kept for the session API (<see cref="CollabPeer.AvatarId"/>); nothing draws it any more. Teammates are drawn as
        /// the simple avatar (a sphere with a camera frustum) in their colour.
        /// </summary>
        public static string AvatarId
        {
            get => EditorPrefs.GetString(Prefix + "AvatarId", "default");
            set { EditorPrefs.SetString(Prefix + "AvatarId", value ?? ""); Changed?.Invoke(); }
        }

        /// <summary>
        /// host:port of a relay (SchematicsCollab --relay) for sessions across the internet. Empty: find peers on the
        /// local network by themselves.
        /// </summary>
        public static string RelayAddress
        {
            get => EditorPrefs.GetString(Prefix + "RelayAddress", "");
            set { EditorPrefs.SetString(Prefix + "RelayAddress", (value ?? "").Trim()); Changed?.Invoke(); }
        }

        /// <summary>How often to check the remote for new commits, in seconds.</summary>
        public static int GitPollSeconds
        {
            get => EditorPrefs.GetInt(Prefix + "GitPollSeconds", 15);
            set { EditorPrefs.SetInt(Prefix + "GitPollSeconds", Mathf.Clamp(value, 5, 3600)); Changed?.Invoke(); }
        }

        /// <summary>Pull new commits automatically (never over local edits, never into a live scene).</summary>
        public static bool AutoPull
        {
            get => EditorPrefs.GetBool(Prefix + "AutoPull", true);
            set { EditorPrefs.SetBool(Prefix + "AutoPull", value); Changed?.Invoke(); }
        }

        /// <summary>Push the commit for a newly placed asset, so the others get it.</summary>
        public static bool AutoPushAssets
        {
            get => EditorPrefs.GetBool(Prefix + "AutoPushAssets", true);
            set { EditorPrefs.SetBool(Prefix + "AutoPushAssets", value); Changed?.Invoke(); }
        }

        /// <summary>The name actually used: the setting, else git's user.name, else the system user name.</summary>
        public static string EffectiveName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName.Trim();
                var gitName = CollabGit.Info?.userName;
                return !string.IsNullOrEmpty(gitName) ? gitName : Environment.UserName;
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() => new SettingsProvider("Preferences/Schematics/Collaboration", SettingsScope.User)
        {
            keywords = new HashSet<string> { "collaboration", "multiplayer", "session", "git", "relay", "peer", "host" },
            guiHandler = _ => DrawGui(),
        };

        private static void DrawGui()
        {
            EditorGUIUtility.labelWidth = 200;
            EditorGUILayout.HelpBox("Everyone who opens this project (same git remote and branch) on the same network joins one live "
                + "session automatically. Across the internet, someone runs the relay (see Collab~/README.md) and everyone enters its address below.",
                MessageType.None);

            bool enabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Start the collaboration helper with the editor."), Enabled);
            if (enabled != Enabled) Enabled = enabled;

            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("You", EditorStyles.boldLabel);
                string name = EditorGUILayout.DelayedTextField(new GUIContent("Display Name", $"Empty uses \"{EffectiveName}\"."), DisplayName);
                if (name != DisplayName) DisplayName = name;

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Network", EditorStyles.boldLabel);
                string relay = EditorGUILayout.DelayedTextField(new GUIContent("Relay Address", "host:port of a relay for internet sessions. Empty finds peers on the local network."), RelayAddress);
                if (relay != RelayAddress) RelayAddress = relay;

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Git", EditorStyles.boldLabel);
                int poll = EditorGUILayout.DelayedIntField(new GUIContent("Poll Interval (s)", "How often to check the remote for new commits."), GitPollSeconds);
                if (poll != GitPollSeconds) GitPollSeconds = poll;
                bool pull = EditorGUILayout.Toggle(new GUIContent("Auto Pull", "Pull new commits when they appear (never over your local edits or into a live scene)."), AutoPull);
                if (pull != AutoPull) AutoPull = pull;
                bool push = EditorGUILayout.Toggle(new GUIContent("Auto Push New Assets", "Commit and push assets you place, so the others get them."), AutoPushAssets);
                if (push != AutoPushAssets) AutoPushAssets = push;

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Session", CollabStatus.Message);
                var info = CollabGit.Info;
                EditorGUILayout.LabelField("Repository", CollabGit.HasRepository ? $"{info.branch} @ {info.remoteUrl} ({info.head})" : "none found");
                foreach (var peer in CollabSession.Peers)
                    EditorGUILayout.LabelField(peer.IsLocal ? "  (you)" : "", $"{peer.Name}{(peer.IsHost ? "  [host]" : "")}{(peer.IsLocal || peer.PingMs < 0 ? "" : $"  {peer.PingMs} ms")}");

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Pull Now")) CollabGit.PullNow();
                    if (GUILayout.Button("Restart Helper")) CollabBridge.Restart();
                    if (GUILayout.Button("Open Log")) CollabBridge.OpenLog();
                }
            }
        }
    }
}
