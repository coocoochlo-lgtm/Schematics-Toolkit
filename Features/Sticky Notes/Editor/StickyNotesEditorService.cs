using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remedy.Schematics.Collaboration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// The editor's side of sticky notes: loads each scene's notes file, connects the store to the collaboration session,
    /// writes the files and commits them.
    ///
    /// <para><b>Where notes live.</b> Next to the scene, as <c>&lt;Scene&gt;.notes.json</c> (Forest.unity →
    /// Forest.notes.json): easy to find, reviewed in the same pull request as the scene, and moved along when the scene
    /// is moved. Builds get the notes embedded into the scene by <see cref="StickyNotesBuildProcessor"/>.</para>
    ///
    /// <para><b>Who writes the file.</b> While other editors are in the session, nobody writes it: the room's shared
    /// store holds the notes, so working trees stay clean and git pulls never trip over a locally modified notes file.
    /// The moment you're the only editor left (or you're working without a session), your editor writes the files, and
    /// when you leave (quit, the session ends, or another editor joins and takes over) it commits them through
    /// <see cref="CollabGit"/>, with everyone who touched those notes as co-authors. Playtesters (builds) never count as
    /// editors: their notes are saved by whichever editor is in the room. Without a session the files are written but not
    /// committed (commit them like any file).</para>
    ///
    /// <para>The in-memory notes survive script reloads and entering Play Mode (kept in SessionState).</para>
    /// </summary>
    [InitializeOnLoad]
    public static class StickyNotesEditorService
    {
        public const string NotesSuffix = ".notes.json";
        private const string MemoryKey = "Remedy.StickyNotes.Memory";

        private static readonly HashSet<string> _unsaved = new HashSet<string>();      // memory may differ from the file
        private static readonly HashSet<string> _uncommitted = new HashSet<string>();  // we wrote the file since the last commit
        private static readonly HashSet<string> _loaded = new HashSet<string>();
        private static readonly Dictionary<string, Dictionary<string, CollabPeer>> _contributors = new Dictionary<string, Dictionary<string, CollabPeer>>();

        private static bool _loading;
        private static bool _heldAfterDisconnect;   // we dropped out of a room with other editors: they'll save, we don't write
        private static bool _wasSole = true;
        private static bool _memoryDirty;
        private static double _writeAt;
        private static bool _warnedNoRepo;

        /// <summary>Raised after a notes file was written (scene path).</summary>
        public static event Action<string> FileWritten;

        static StickyNotesEditorService()
        {
            Safe(StickyNotesSettings.Apply);
            // Use your collaboration name even outside a session, so tasks you take are recognised as yours either way.
            Safe(() => { var name = CollabSettings.EffectiveName; if (!string.IsNullOrEmpty(name)) StickyNoteStore.FallbackName = name; });
            // The asset database may not be ready this early on the first import; try again once it is.
            EditorApplication.delayCall += () => Safe(StickyNotesSettings.Apply);
            RestoreMemory();

            _loading = true;
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++) LoadScene(SceneManager.GetSceneAt(i).path);
            }
            finally { _loading = false; }

            StickyNoteStore.NoteChanged += OnNoteChanged;
            StickyNoteStore.Changed += _ => _memoryDirty = true;
            StickyNoteStore.Transport = new CollabSessionTransport();

            EditorSceneManager.sceneOpened += (scene, mode) => Safe(() => LoadScene(scene.path));
            SceneManager.sceneLoaded += (scene, mode) => Safe(() => LoadScene(scene.path));
            CollabGit.Pulled += paths => Safe(() => OnPulled(paths));
            CollabSession.PeerJoined += peer => Safe(() => OnPeerJoined(peer));
            CollabSession.PeerLeft += peer => Safe(OnPeerLeft);
            CollabSession.Connected += () => Safe(OnConnected);
            CollabSession.Disconnected += () => Safe(OnDisconnected);
            CollabSession.Subscribe(StickyNoteStore.Channel, (peer, json) => Safe(() => RememberContributor(peer, json)));

            EditorApplication.update += Tick;
            // wantsToQuit runs before the collaboration bridge closes on "quitting", so the commit still reaches it.
            EditorApplication.wantsToQuit += OnWantsToQuit;
            AssemblyReloadEvents.beforeAssemblyReload += SaveMemory;
            EditorApplication.playModeStateChanged += _ => SaveMemory();
        }

        #region Public API

        /// <summary>The notes file of a scene: "Assets/Levels/Forest.unity" → "Assets/Levels/Forest.notes.json".</summary>
        public static string NotesPathFor(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return "";
            string dir = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(scenePath) + NotesSuffix;
            return string.IsNullOrEmpty(dir) ? name : dir + "/" + name;
        }

        /// <summary>The scene a notes file belongs to (the reverse of <see cref="NotesPathFor"/>).</summary>
        public static string ScenePathFor(string notesPath)
        {
            if (string.IsNullOrEmpty(notesPath) || !notesPath.EndsWith(NotesSuffix, StringComparison.OrdinalIgnoreCase)) return "";
            return notesPath.Substring(0, notesPath.Length - NotesSuffix.Length) + ".unity";
        }

        /// <summary>True when this editor writes the notes files right now (no other editor in the session).</summary>
        public static bool IsSoleEditor
        {
            get
            {
                if (!CollabSession.IsConnected) return !_heldAfterDisconnect;
                var peers = CollabSession.Peers;
                if (peers == null) return true;
                foreach (var p in peers)
                    if (p != null && !p.IsLocal && !CollabSessionTransport.IsPlaytester(p)) return false;
                return true;
            }
        }

        /// <summary>Scenes whose notes changed but aren't written to their file yet (because other editors are here).</summary>
        public static IReadOnlyCollection<string> UnsavedScenes => _unsaved;

        /// <summary>Scenes whose notes files this editor wrote and hasn't committed yet.</summary>
        public static IReadOnlyCollection<string> UncommittedScenes => _uncommitted;

        /// <summary>Loads a scene's notes file (once) and joins it to the room's notes.</summary>
        public static void LoadScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return;
            if (_loaded.Add(scenePath))
            {
                string path = NotesPathFor(scenePath);
                if (File.Exists(path))
                {
                    bool wasLoading = _loading;
                    _loading = true;
                    try { StickyNoteStore.MergeJson(File.ReadAllText(path), scenePath); }
                    catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't read {path}: {e.Message}"); }
                    finally { _loading = wasLoading; }
                }
            }
            StickyNoteStore.Track(scenePath);
        }

        /// <summary>Loads every notes file in the project (for the Sticky Notes window's "all scenes" list).</summary>
        public static void LoadAllFiles()
        {
            try
            {
                foreach (var full in Directory.GetFiles("Assets", "*" + NotesSuffix, SearchOption.AllDirectories))
                    LoadScene(ScenePathFor(full.Replace('\\', '/')));
            }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't look for notes files: {e.Message}"); }
        }

        /// <summary>Writes the notes files that changed now (only when you're the sole editor, unless forced).</summary>
        public static void WriteNow(bool force = false)
        {
            if (!force && !IsSoleEditor) return;
            foreach (var scene in _unsaved.ToList())
            {
                if (WriteFile(scene)) _uncommitted.Add(scene);
                _unsaved.Remove(scene);
            }
            _memoryDirty = true;
        }

        /// <summary>Writes and commits this editor's notes files now (Collab git; same as leaving as the last editor).</summary>
        public static void CommitNow(string reason = "Sticky notes")
        {
            if (IsSoleEditor) WriteNow();
            if (_uncommitted.Count == 0) return;
            if (!CollabGit.HasRepository)
            {
                if (!_warnedNoRepo) Debug.Log("[Sticky Notes] Notes files are saved; they'll be committed through Collab when a session with git is running (or commit them yourself).");
                _warnedNoRepo = true;
                return;
            }
            foreach (var scene in _uncommitted.ToList())
            {
                string path = NotesPathFor(scene);
                if (!File.Exists(path)) continue;
                var notes = StickyNoteStore.GetNotes(scene);
                int open = notes.Count(n => !n.Resolved);
                var coAuthors = new List<CollabPeer>();
                if (_contributors.TryGetValue(scene, out var peers)) coAuthors.AddRange(peers.Values);
                if (CollabSession.Local != null && !coAuthors.Contains(CollabSession.Local)) coAuthors.Add(CollabSession.Local);
                string summary = $"{reason}: {Path.GetFileNameWithoutExtension(scene)} ({open} open, {notes.Count - open} resolved)";
                // CommitScene commits whatever path it's given (plus its .meta), so it commits the notes file here.
                Safe(() => CollabGit.CommitScene(path, coAuthors, summary));
                _contributors.Remove(scene);
            }
            _uncommitted.Clear();
            _memoryDirty = true;
        }

        #endregion

        #region Events

        private static void OnNoteChanged(StickyNote note, bool local)
        {
            if (_loading || note == null || string.IsNullOrEmpty(note.Scene)) return;
            _unsaved.Add(note.Scene);
            _writeAt = EditorApplication.timeSinceStartup + 1.0;
            _memoryDirty = true;
            if (local && CollabSession.Local != null) AddContributor(note.Scene, CollabSession.Local);
        }

        private static void Tick()
        {
            if (CollabSession.IsConnected) _wasSole = IsSoleEditor;
            if (_unsaved.Count > 0 && EditorApplication.timeSinceStartup >= _writeAt && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                _writeAt = EditorApplication.timeSinceStartup + 1.0;
                if (IsSoleEditor) Safe(() => WriteNow());
            }
            if (_memoryDirty) SaveMemory();
        }

        private static void OnPeerJoined(CollabPeer peer)
        {
            if (peer == null || peer.IsLocal || CollabSessionTransport.IsPlaytester(peer)) return;
            // Someone else is here now: hand over by committing what we wrote while alone, so neither of us later finds
            // a modified notes file in the way of a pull.
            if (StickyNotesSettings.Instance.CommitWhenLastEditorLeaves && (_uncommitted.Count > 0 || _unsaved.Count > 0))
            {
                WriteNow(force: true);
                CommitNow("Sticky notes (handed over)");
            }
        }

        private static void OnPeerLeft()
        {
            // Last editor standing: start writing the files (committed when we leave).
            if (IsSoleEditor) _writeAt = 0;
        }

        private static void OnConnected()
        {
            _heldAfterDisconnect = false;
            if (!IsSoleEditor && (_uncommitted.Count > 0 || _unsaved.Count > 0) && StickyNotesSettings.Instance.CommitWhenLastEditorLeaves)
            {
                WriteNow(force: true);
                CommitNow("Sticky notes (handed over)");
            }
        }

        private static void OnDisconnected()
        {
            if (_wasSole)
            {
                // The session ended with us as the last editor: save and commit.
                WriteNow(force: true);
                if (StickyNotesSettings.Instance.CommitWhenLastEditorLeaves) CommitNow("Sticky notes (session ended)");
            }
            else
            {
                // Others are probably still in the room and will save; keep ours in memory until we're back.
                _heldAfterDisconnect = true;
            }
        }

        private static bool OnWantsToQuit()
        {
            Safe(() =>
            {
                // Unfinished tasks of ours go back to To Do first, so the saved file and the room both have them there.
                if (StickyNotesSettings.Instance.ReturnMyTasksOnQuit) StickyNotesTaskReturn.ReturnMine();
                // Never lose notes on quit: write them even if we were held (worst case a teammate's commit and ours agree).
                if (IsSoleEditor || _heldAfterDisconnect) WriteNow(force: true);
                if (IsSoleEditor && StickyNotesSettings.Instance.CommitWhenLastEditorLeaves && CollabSession.IsConnected)
                    CommitNow("Sticky notes (editor closed)");
                SaveMemory();
            });
            return true;
        }

        private static void OnPulled(string[] paths)
        {
            if (paths == null) return;
            foreach (var p in paths)
            {
                if (p == null || !p.EndsWith(NotesSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                string scene = ScenePathFor(p);
                StickyNoteFile file = null;
                try { if (File.Exists(p)) file = StickyNoteStore.Parse(File.ReadAllText(p)); }
                catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't read pulled {p}: {e.Message}"); }
                if (file == null) continue;
                _loaded.Add(scene);
                _loading = true;
                try
                {
                    // Nothing pending here and nobody else holding newer notes: the pulled file is the truth (so notes
                    // deleted elsewhere go away). Otherwise mix it in, newest copy winning.
                    if (IsSoleEditor && !_unsaved.Contains(scene) && !_uncommitted.Contains(scene)) StickyNoteStore.Replace(scene, file);
                    else StickyNoteStore.Merge(file, scene);
                }
                finally { _loading = false; }
            }
        }

        private static void RememberContributor(CollabPeer peer, string json)
        {
            if (peer == null) return;
            var msg = JsonUtility.FromJson<StickyNoteMessage>(json);
            if (msg?.Note != null && !string.IsNullOrEmpty(msg.Note.Scene)) AddContributor(msg.Note.Scene, peer);
        }

        private static void AddContributor(string scene, CollabPeer peer)
        {
            if (!_contributors.TryGetValue(scene, out var map)) _contributors[scene] = map = new Dictionary<string, CollabPeer>();
            map[peer.Id ?? peer.Name ?? ""] = peer;
        }

        #endregion

        #region Files

        // Writes the scene's notes file if its content changed. Returns true when it wrote.
        private static bool WriteFile(string scene)
        {
            if (string.IsNullOrEmpty(scene) || !File.Exists(scene)) return false;   // unsaved or deleted scene
            string path = NotesPathFor(scene);
            var notes = StickyNoteStore.GetNotes(scene);
            if (notes.Count == 0 && !File.Exists(path)) return false;
            string json = StickyNoteStore.ToJson(scene, pretty: true, includeDeleted: false);
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == json) return false;
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sticky Notes] Couldn't write {path}: {e.Message}");
                return false;
            }
            AssetDatabase.ImportAsset(path);   // gives it a .meta so it's committed with one
            _loaded.Add(scene);
            try { FileWritten?.Invoke(scene); }
            catch (Exception e) { Debug.LogException(e); }
            return true;
        }

        #endregion

        #region Surviving reloads

        [Serializable]
        private sealed class Memory
        {
            public List<StickyNoteFile> Scenes = new List<StickyNoteFile>();
            public List<string> Unsaved = new List<string>();
            public List<string> Uncommitted = new List<string>();
            public List<string> Loaded = new List<string>();
            public bool Held;
        }

        private static void SaveMemory()
        {
            _memoryDirty = false;
            try
            {
                var m = new Memory
                {
                    Unsaved = _unsaved.ToList(),
                    Uncommitted = _uncommitted.ToList(),
                    Loaded = _loaded.ToList(),
                    Held = _heldAfterDisconnect,
                };
                foreach (var scene in StickyNoteStore.Scenes.ToList())
                {
                    var file = new StickyNoteFile { Scene = scene };
                    file.Notes.AddRange(StickyNoteStore.GetNotes(scene, includeDeleted: true));
                    m.Scenes.Add(file);
                }
                SessionState.SetString(MemoryKey, JsonUtility.ToJson(m));
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void RestoreMemory()
        {
            string json = SessionState.GetString(MemoryKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var m = JsonUtility.FromJson<Memory>(json);
                if (m == null) return;
                _loading = true;
                foreach (var file in m.Scenes) StickyNoteStore.Merge(file);
                foreach (var s in m.Unsaved) _unsaved.Add(s);
                foreach (var s in m.Uncommitted) _uncommitted.Add(s);
                foreach (var s in m.Loaded) _loaded.Add(s);
                _heldAfterDisconnect = m.Held;
            }
            catch (Exception e) { Debug.LogException(e); }
            finally { _loading = false; }
        }

        #endregion

        internal static void Safe(Action action)
        {
            try { action(); }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Moves a scene's notes file along when the scene is moved or renamed.</summary>
    internal sealed class StickyNotesAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            for (int i = 0; i < moved.Length && i < movedFrom.Length; i++)
            {
                if (!moved[i].EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                string from = StickyNotesEditorService.NotesPathFor(movedFrom[i]);
                string to = StickyNotesEditorService.NotesPathFor(moved[i]);
                if (!File.Exists(from) || File.Exists(to)) continue;
                string error = AssetDatabase.MoveAsset(from, to);
                if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[Sticky Notes] Couldn't move {from} with its scene: {error}");
                else
                {
                    string sceneTo = moved[i];
                    EditorApplication.delayCall += () => StickyNotesEditorService.LoadScene(sceneTo);
                }
            }
        }
    }
}
