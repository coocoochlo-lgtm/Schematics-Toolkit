using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>Which builds get the scene notes embedded (and the in-game view available).</summary>
    public enum StickyNotesBuildInclusion
    {
        /// <summary>Development builds only (the default: playtest builds get them, release builds don't).</summary>
        DevelopmentBuildsOnly,
        /// <summary>Every build, release builds too (e.g. for an external playtest on a release build).</summary>
        AlwaysInclude,
        Never,
    }

    /// <summary>
    /// Project-wide sticky note settings (Project Settings &gt; Schematics &gt; Sticky Notes), kept in
    /// ProjectSettings/StickyNotesSettings.json so the whole team shares them. Per-person view options (show in the Scene
    /// view, show resolved) live in EditorPrefs instead.
    /// </summary>
    [Serializable]
    public sealed class StickyNotesSettings
    {
        private const string FilePath = "ProjectSettings/StickyNotesSettings.json";

        public StickyNotesBuildInclusion BuildInclusion = StickyNotesBuildInclusion.DevelopmentBuildsOnly;

        /// <summary>Commit the notes file when you leave as the last editor (quit, disconnect, or someone joins after you were alone).</summary>
        public bool CommitWhenLastEditorLeaves = true;

        private static StickyNotesSettings _instance;

        public static StickyNotesSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = new StickyNotesSettings();
                try { if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), _instance); }
                catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't read {FilePath}: {e.Message}"); }
                return _instance;
            }
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't save {FilePath}: {e.Message}"); }
        }

        #region Per-person view options

        private const string ShowKey = "Remedy.StickyNotes.ShowInSceneView";
        private const string ShowResolvedKey = "Remedy.StickyNotes.ShowResolved";

        /// <summary>Notes are drawn in the Scene view.</summary>
        public static bool ShowInSceneView
        {
            get => EditorPrefs.GetBool(ShowKey, true);
            set { EditorPrefs.SetBool(ShowKey, value); SceneView.RepaintAll(); }
        }

        /// <summary>Resolved notes are drawn too (muted green).</summary>
        public static bool ShowResolved
        {
            get => EditorPrefs.GetBool(ShowResolvedKey, true);
            set { EditorPrefs.SetBool(ShowResolvedKey, value); SceneView.RepaintAll(); }
        }

        #endregion

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Schematics/Sticky Notes", SettingsScope.Project)
            {
                label = "Sticky Notes",
                keywords = new HashSet<string> { "Sticky", "Notes", "Feedback", "Playtest" },
                guiHandler = _ =>
                {
                    var s = Instance;
                    EditorGUI.BeginChangeCheck();
                    s.BuildInclusion = (StickyNotesBuildInclusion)EditorGUILayout.EnumPopup(
                        new GUIContent("Include In Builds", "Which builds get each scene's notes embedded and the in-game view (F8) available."), s.BuildInclusion);
                    s.CommitWhenLastEditorLeaves = EditorGUILayout.Toggle(
                        new GUIContent("Commit When Last Editor Leaves", "The last editor in the session commits the notes files (via Collab git) when they leave."), s.CommitWhenLastEditorLeaves);
                    if (EditorGUI.EndChangeCheck()) s.Save();

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Your view", EditorStyles.boldLabel);
                    ShowInSceneView = EditorGUILayout.Toggle("Show In Scene View", ShowInSceneView);
                    ShowResolved = EditorGUILayout.Toggle("Show Resolved", ShowResolved);

                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        "Notes are saved next to each scene as <Scene>.notes.json. In game, F8 shows notes and F9 adds one " +
                        "(StickyNotesView.ToggleKey / AddKey from code).", MessageType.Info);
                },
            };
        }
    }
}
