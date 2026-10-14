using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// Starts sticky notes when the game starts (play mode or a build): connects the store to the runtime collaboration
    /// client unless the editor already installed its own route, follows scene loads, creates the in-game view, and in
    /// builds keeps a local copy of each scene's notes so a playtester who isn't connected doesn't lose theirs (they're
    /// shared with the room the next time they connect).
    /// </summary>
    public static class StickyNotesRuntime
    {
        private static readonly HashSet<string> _dirtyLocalCopies = new HashSet<string>();

        /// <summary>Where builds keep their local copies (one JSON file per scene).</summary>
        public static string LocalCopyFolder => Path.Combine(Application.persistentDataPath, "StickyNotes");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            // The editor's assembly installs a CollabSession route when it loads (before this runs), so in the editor's
            // play mode notes keep going through the editor's session and aren't sent twice.
            if (StickyNoteStore.Transport == null)
            {
                try { StickyNoteStore.Transport = new CollabRuntimeTransport(); }
                catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Collaboration isn't available; notes stay local. {e.Message}"); }
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (!Application.isEditor)
            {
                StickyNoteStore.Changed -= OnChanged;
                StickyNoteStore.Changed += OnChanged;
                Application.quitting -= FlushLocalCopies;
                Application.quitting += FlushLocalCopies;
                Application.focusChanged -= OnFocusChanged;
                Application.focusChanged += OnFocusChanged;
            }

            StickyNotesView.EnsureHost();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrEmpty(scene.path)) return;
            if (!Application.isEditor) LoadLocalCopy(scene.path);
            StickyNoteStore.Track(scene.path);
        }

        private static void OnChanged(string scene)
        {
            if (!string.IsNullOrEmpty(scene)) _dirtyLocalCopies.Add(scene);
        }

        private static void OnFocusChanged(bool focused)
        {
            if (!focused) FlushLocalCopies();
        }

        /// <summary>Writes builds' local copies now (also done every few seconds by the view, on focus loss and on quit).</summary>
        public static void FlushLocalCopies()
        {
            if (_dirtyLocalCopies.Count == 0) return;
            try
            {
                Directory.CreateDirectory(LocalCopyFolder);
                foreach (var scene in _dirtyLocalCopies)
                    File.WriteAllText(LocalCopyPath(scene), StickyNoteStore.ToJson(scene, pretty: false, includeDeleted: true));
            }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't save notes locally: {e.Message}"); }
            _dirtyLocalCopies.Clear();
        }

        private static void LoadLocalCopy(string scene)
        {
            try
            {
                string path = LocalCopyPath(scene);
                if (File.Exists(path)) StickyNoteStore.MergeJson(File.ReadAllText(path), scene);
            }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't read the local notes copy: {e.Message}"); }
        }

        private static string LocalCopyPath(string scene)
        {
            var sb = new StringBuilder();
            foreach (char c in scene) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '.' ? c : '_');
            return Path.Combine(LocalCopyFolder, sb + ".json");
        }
    }
}
