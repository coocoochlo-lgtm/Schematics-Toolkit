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
    /// <summary>Where Play Mode Profiler captures attached to notes are written.</summary>
    public enum StickyNotesCaptureLocation
    {
        /// <summary>Library/StickyNotes/Captures (local to this machine, never committed). The default: captures are big.</summary>
        Library,
        /// <summary>A "&lt;Scene&gt;.notes-captures" folder next to the scene's notes file (shared through git).</summary>
        NextToNotesFile,
    }

    [Serializable]
    public sealed class StickyNotesSettings
    {
        /// <summary>The GUID of the toolkit's sticky-note paper image (media/StickyNote.png).</summary>
        public const string DefaultPaperGuid = "2058c76d2bbf14646a01acabc182ba10";
        /// <summary>Looked for (by file name) when the default image's GUID can't be found.</summary>
        public const string WellKnownPaperName = "StickyNote";

        private const string FilePath = "ProjectSettings/StickyNotesSettings.json";

        public StickyNotesBuildInclusion BuildInclusion = StickyNotesBuildInclusion.DevelopmentBuildsOnly;

        /// <summary>Commit the notes file when you leave as the last editor (quit, disconnect, or someone joins after you were alone).</summary>
        public bool CommitWhenLastEditorLeaves = true;

        /// <summary>GUID of a texture to use as the note paper instead of the default image. Empty uses the default.</summary>
        public string PaperTextureGuid = "";

        /// <summary>Warning and Error notes get a performance snapshot automatically (others when the author ticks the box).</summary>
        public bool AutoSnapshotForWarningsAndErrors = true;

        /// <summary>How many recent frames the snapshot's FPS figures cover.</summary>
        public int SnapshotFrames = 120;

        /// <summary>In the editor's Play Mode, notes with a snapshot also record a short Profiler capture (.raw).</summary>
        public bool ProfilerCaptureInPlayMode = true;

        public StickyNotesCaptureLocation ProfilerCaptureLocation = StickyNotesCaptureLocation.Library;

        /// <summary>How many frames a Profiler capture records.</summary>
        public int ProfilerCaptureFrames = 60;

        /// <summary>Tasks (In Progress notes) of someone who leaves the session go back to To Do.</summary>
        public bool ReturnTasksWhenAssigneeLeaves = true;

        /// <summary>
        /// How long someone may be gone from the session before their tasks go back to To Do (seconds). Covers a quick
        /// reconnect or an editor restart.
        /// </summary>
        public float LeaveGraceSeconds = 120f;

        /// <summary>
        /// When an editor loads notes and an assignee isn't around (and wasn't seen leaving), their tasks go back to To Do
        /// once they were taken (or last refreshed by the assignee's own editor) longer ago than this (hours).
        /// </summary>
        public float AbsentGraceHours = 24f;

        /// <summary>Your own In Progress tasks go back to To Do when you quit the editor.</summary>
        public bool ReturnMyTasksOnQuit = true;

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
            Apply();
        }

        #region Paper image and runtime options

        /// <summary>
        /// The note paper texture: the override from these settings, else the toolkit's image by GUID, else any texture
        /// named "StickyNote" (e.g. Features/Sticky Notes/Textures/StickyNote.png). Null when none exists (notes are then
        /// drawn as plain coloured squares).
        /// </summary>
        public static Texture2D LoadPaperTexture()
        {
            var s = Instance;
            var tex = LoadByGuid(s.PaperTextureGuid) ?? LoadByGuid(DefaultPaperGuid);
            if (tex != null) return tex;
            try
            {
                foreach (var guid in AssetDatabase.FindAssets(WellKnownPaperName + " t:Texture2D"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) == WellKnownPaperName) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }
            catch { }
            return null;
        }

        private static Texture2D LoadByGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Pushes these settings into the runtime side (paper image, snapshot options, Profiler capture path).</summary>
        public static void Apply()
        {
            var s = Instance;
            StickyNoteStyle.PaperTexture = LoadPaperTexture();
            StickyNotePerf.AutoForWarningsAndErrors = s.AutoSnapshotForWarningsAndErrors;
            StickyNotePerf.SampleFrames = Mathf.Max(1, s.SnapshotFrames);
            StickyNotePerf.ProfilerCaptureFrames = Mathf.Max(1, s.ProfilerCaptureFrames);
            StickyNotePerf.ProfilerCapturePath = s.ProfilerCaptureInPlayMode ? CapturePathFor : (Func<StickyNote, string>)null;
            SceneView.RepaintAll();
        }

        // Where a note's Play Mode Profiler capture goes (without the .raw extension), relative to the project.
        private static string CapturePathFor(StickyNote note)
        {
            if (note == null) return null;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string id = string.IsNullOrEmpty(note.Id) ? Guid.NewGuid().ToString("N") : note.Id;
            string name = $"{Path.GetFileNameWithoutExtension(note.Scene ?? "Scene")}_{stamp}_{id.Substring(0, Math.Min(8, id.Length))}";
            if (Instance.ProfilerCaptureLocation == StickyNotesCaptureLocation.NextToNotesFile && !string.IsNullOrEmpty(note.Scene))
            {
                string notes = StickyNotesEditorService.NotesPathFor(note.Scene);
                string folder = notes.Substring(0, notes.Length - StickyNotesEditorService.NotesSuffix.Length) + ".notes-captures";
                return folder + "/" + name;
            }
            return "Library/StickyNotes/Captures/" + name;
        }

        #endregion

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

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Look", EditorStyles.boldLabel);
                    var current = LoadByGuid(s.PaperTextureGuid);
                    var picked = (Texture2D)EditorGUILayout.ObjectField(
                        new GUIContent("Paper Image Override", "Leave empty to use the toolkit's sticky-note image (media/StickyNote.png)."), current, typeof(Texture2D), false);
                    if (picked != current)
                        s.PaperTextureGuid = picked != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked)) : "";
                    if (StickyNoteStyle.PaperTexture == null)
                        EditorGUILayout.HelpBox("No sticky-note image found: notes are drawn as coloured squares.", MessageType.None);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Performance snapshots", EditorStyles.boldLabel);
                    s.AutoSnapshotForWarningsAndErrors = EditorGUILayout.Toggle(
                        new GUIContent("Auto For Warnings/Errors", "Warning and Error notes get a performance snapshot automatically; other notes when the author ticks 'Attach performance snapshot'."), s.AutoSnapshotForWarningsAndErrors);
                    s.SnapshotFrames = EditorGUILayout.IntSlider(new GUIContent("FPS Over Frames", "How many recent frames the FPS figures cover."), s.SnapshotFrames, 10, 600);
                    s.ProfilerCaptureInPlayMode = EditorGUILayout.Toggle(
                        new GUIContent("Profiler Capture (Play Mode)", "In the editor's Play Mode, notes with a snapshot also record a short Profiler capture (.raw) and link it."), s.ProfilerCaptureInPlayMode);
                    using (new EditorGUI.DisabledScope(!s.ProfilerCaptureInPlayMode))
                    {
                        s.ProfilerCaptureLocation = (StickyNotesCaptureLocation)EditorGUILayout.EnumPopup(
                            new GUIContent("Capture Location", "Library (local, the default) or next to the scene's notes file (committed with it)."), s.ProfilerCaptureLocation);
                        s.ProfilerCaptureFrames = EditorGUILayout.IntSlider("Capture Frames", s.ProfilerCaptureFrames, 10, 600);
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Kanban", EditorStyles.boldLabel);
                    s.ReturnTasksWhenAssigneeLeaves = EditorGUILayout.Toggle(
                        new GUIContent("Return Tasks When People Leave", "In Progress tasks of someone who leaves the session go back to To Do (the host does it)."), s.ReturnTasksWhenAssigneeLeaves);
                    using (new EditorGUI.DisabledScope(!s.ReturnTasksWhenAssigneeLeaves))
                    {
                        s.LeaveGraceSeconds = EditorGUILayout.FloatField(new GUIContent("Leave Grace (s)", "How long someone may be gone (a reconnect, an editor restart) before their tasks go back."), s.LeaveGraceSeconds);
                        s.AbsentGraceHours = EditorGUILayout.FloatField(new GUIContent("Absent Grace (h)", "On load, tasks of someone who isn't here go back once they were taken (or refreshed by them) longer ago than this."), s.AbsentGraceHours);
                    }
                    s.ReturnMyTasksOnQuit = EditorGUILayout.Toggle(
                        new GUIContent("Return My Tasks On Quit", "Your unfinished tasks go back to To Do when you quit the editor."), s.ReturnMyTasksOnQuit);
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
