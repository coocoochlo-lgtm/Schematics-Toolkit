using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// Every sticky note this process knows about, per scene, kept in step with the collaboration room.
    ///
    /// The rule that keeps everyone in agreement is simple: each note has an <see cref="StickyNote.Updated"/> stamp and
    /// the newest copy wins, everywhere. Deleting a note keeps a "tombstone" (a copy with Deleted set) so an old copy
    /// can't bring it back. Because of that rule the order things arrive in doesn't matter, so the store can mix:
    /// <list type="bullet">
    /// <item>messages on the <see cref="Channel"/> channel (one note's newest version, sent when someone changes it),</item>
    /// <item>the room's shared store (key <c>notes:&lt;scene path&gt;</c>, the whole set, so late joiners see everything),</item>
    /// <item>the notes file in the repository and the copy embedded in builds.</item>
    /// </list>
    /// It works without any collaboration at all: changes then just stay in this process (and the editor's file).
    /// Everything runs on the main thread.
    /// </summary>
    public static class StickyNoteStore
    {
        /// <summary>The collaboration channel notes travel on.</summary>
        public const string Channel = "sticky-notes";
        /// <summary>Shared store key prefix; the scene path follows.</summary>
        public const string SharedPrefix = "notes:";
        /// <summary>Shared store key listing every scene that has notes in the room (so a window can list them all).</summary>
        public const string SharedIndexKey = "notes-index";
        /// <summary>Tombstones older than this are dropped when the set is published (they've done their job by then).</summary>
        public static double TombstoneDays = 14;

        private static readonly Dictionary<string, Dictionary<string, StickyNote>> _scenes = new Dictionary<string, Dictionary<string, StickyNote>>();
        private static IStickyNotesTransport _transport;

        /// <summary>The notes of a scene changed (added, edited, resolved, deleted, loaded or arrived from someone else).</summary>
        public static event Action<string> Changed;

        /// <summary>
        /// One note changed. The bool is true when the change was made here (by this player or editor), false when it
        /// came from the room. The editor uses it to know which scenes' files need writing.
        /// </summary>
        public static event Action<StickyNote, bool> NoteChanged;

        #region Transport

        /// <summary>
        /// The route to the collaboration room. The editor installs its CollabSession transport when it loads; builds
        /// (and play mode without the editor tools) get a <see cref="CollabRuntimeTransport"/>. Null means local only.
        /// </summary>
        public static IStickyNotesTransport Transport
        {
            get => _transport;
            set
            {
                if (_transport == value) return;
                if (_transport != null)
                {
                    _transport.Unsubscribe(Channel, OnMessage);
                    _transport.SharedChanged -= OnSharedChanged;
                    _transport.Connected -= OnConnected;
                }
                _transport = value;
                if (_transport != null)
                {
                    _transport.Subscribe(Channel, OnMessage);
                    _transport.SharedChanged += OnSharedChanged;
                    _transport.Connected += OnConnected;
                    if (_transport.IsConnected) OnConnected();
                }
            }
        }

        /// <summary>In a collaboration room right now.</summary>
        public static bool IsConnected
        {
            get
            {
                try { return _transport != null && _transport.IsConnected; }
                catch { return false; }
            }
        }

        /// <summary>Your name on new notes: your collaboration name, else the computer's user name.</summary>
        public static string LocalName
        {
            get
            {
                try { if (_transport != null && !string.IsNullOrEmpty(_transport.LocalName)) return _transport.LocalName; }
                catch { }
                return FallbackName;
            }
        }

        public static Color LocalColor
        {
            get
            {
                try { if (_transport != null && IsConnected) return _transport.LocalColor; }
                catch { }
                return Color.white;
            }
        }

        public static string LocalPeerId
        {
            get
            {
                try { return _transport?.LocalPeerId ?? ""; }
                catch { return ""; }
            }
        }

        /// <summary>Used when there's no collaboration name. Can be set by a game (e.g. to the player's profile name).</summary>
        public static string FallbackName
        {
            get
            {
                if (!string.IsNullOrEmpty(_fallbackName)) return _fallbackName;
                try { _fallbackName = Environment.UserName; }
                catch { }
                if (string.IsNullOrEmpty(_fallbackName)) _fallbackName = Application.isEditor ? "Editor" : "Playtester";
                return _fallbackName;
            }
            set => _fallbackName = value;
        }
        private static string _fallbackName;

        #endregion

        #region Reading

        /// <summary>Scenes that have (or had) notes here.</summary>
        public static IEnumerable<string> Scenes => _scenes.Keys;

        /// <summary>
        /// The live notes of a scene, oldest first. Treat them as read-only: change a <see cref="StickyNote.Clone"/> and
        /// pass it to <see cref="Update"/>.
        /// </summary>
        public static List<StickyNote> GetNotes(string scene, bool includeDeleted = false)
        {
            var list = new List<StickyNote>();
            if (scene == null || !_scenes.TryGetValue(scene, out var notes)) return list;
            foreach (var n in notes.Values)
                if (includeDeleted || !n.Deleted) list.Add(n);
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.Created, b.Created);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        /// <summary>Every live note in every scene.</summary>
        public static IEnumerable<StickyNote> AllNotes => _scenes.Values.SelectMany(d => d.Values).Where(n => !n.Deleted);

        public static StickyNote Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var notes in _scenes.Values)
                if (notes.TryGetValue(id, out var n) && !n.Deleted) return n;
            return null;
        }

        /// <summary>The scene's notes as JSON (a <see cref="StickyNoteFile"/>). The file form leaves out tombstones.</summary>
        public static string ToJson(string scene, bool pretty, bool includeDeleted)
        {
            var file = new StickyNoteFile { Scene = scene ?? "" };
            file.Notes.AddRange(GetNotes(scene, includeDeleted));
            return JsonUtility.ToJson(file, pretty);
        }

        #endregion

        #region Changing

        /// <summary>Sticks a new note into a scene and shares it. Returns the note.</summary>
        public static StickyNote Add(string scene, Vector3 position, Vector3 normal, StickyNoteType type, string text,
            string attachedId = null, string attachedName = null)
        {
            string now = StickyNote.Now();
            var note = new StickyNote
            {
                Id = Guid.NewGuid().ToString("N"),
                Scene = scene ?? "",
                Position = position,
                Normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up,
                NoteType = type,
                Text = text ?? "",
                Author = LocalName,
                AuthorId = LocalPeerId,
                AuthorColor = StickyNoteStyle.ToHtml(LocalColor),
                Created = now,
                Updated = now,
                AttachedId = attachedId ?? "",
                AttachedName = attachedName ?? "",
            };
            Commit(note, "add");
            return note;
        }

        /// <summary>
        /// Saves an edited copy of a note (from <see cref="StickyNote.Clone"/>) and shares it. Returns false when nothing
        /// changed or the note is unknown.
        /// </summary>
        public static bool Update(StickyNote edited, string op = "update")
        {
            if (edited == null || string.IsNullOrEmpty(edited.Id)) return false;
            var current = FindAny(edited.Id);
            if (current != null)
            {
                var probe = edited.Clone();
                probe.Updated = current.Updated;
                if (probe.SameAs(current)) return false;
            }
            var copy = edited.Clone();
            copy.Updated = Stamp(current?.Updated);
            if (string.IsNullOrEmpty(copy.Created)) copy.Created = copy.Updated;
            Commit(copy, op);
            return true;
        }

        public static bool SetResolved(string id, bool resolved)
        {
            var note = Find(id);
            if (note == null || note.Resolved == resolved) return false;
            var copy = note.Clone();
            copy.Resolved = resolved;
            copy.ResolvedBy = resolved ? LocalName : "";
            return Update(copy, resolved ? "resolve" : "reopen");
        }

        public static bool Delete(string id)
        {
            var note = Find(id);
            if (note == null) return false;
            var copy = note.Clone();
            copy.Deleted = true;
            return Update(copy, "delete");
        }

        // A stamp newer than both "now" and the previous version, so this edit wins even if our clock is behind.
        private static string Stamp(string previous)
        {
            string now = StickyNote.Now();
            if (string.IsNullOrEmpty(previous) || string.CompareOrdinal(now, previous) > 0) return now;
            if (DateTime.TryParse(previous, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                return t.AddMilliseconds(1).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            return now;
        }

        private static void Commit(StickyNote note, string op)
        {
            Put(note);
            RaiseNoteChanged(note, true);
            RaiseChanged(note.Scene);
            if (!IsConnected) return;
            try
            {
                _transport.Send(Channel, JsonUtility.ToJson(new StickyNoteMessage { Op = op, Note = note }));
                PublishShared(note.Scene);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        #endregion

        #region Loading and merging

        /// <summary>
        /// Mixes notes from a file, an embedded build copy or the shared store into the scene, newest copy winning.
        /// Returns true when anything changed. <paramref name="local"/> marks the changes as made here (the editor then
        /// writes them to the file).
        /// </summary>
        public static bool Merge(StickyNoteFile file, string sceneOverride = null, bool local = false)
        {
            if (file?.Notes == null) return false;
            string scene = !string.IsNullOrEmpty(sceneOverride) ? sceneOverride : file.Scene;
            if (string.IsNullOrEmpty(scene)) return false;
            EnsureScene(scene);
            bool changed = false;
            foreach (var n in file.Notes)
            {
                if (n == null || string.IsNullOrEmpty(n.Id)) continue;
                n.Scene = scene;
                var current = FindAny(n.Id);
                if (current != null && !n.Supersedes(current)) continue;
                if (current != null && n.SameAs(current)) continue;
                Put(n);
                RaiseNoteChanged(n, local);
                changed = true;
            }
            if (changed) RaiseChanged(scene);
            return changed;
        }

        /// <summary>Parses JSON written by <see cref="ToJson"/> and merges it. Bad JSON is ignored (with a warning).</summary>
        public static bool MergeJson(string json, string sceneOverride = null, bool local = false)
        {
            var file = Parse(json);
            return file != null && Merge(file, sceneOverride, local);
        }

        /// <summary>
        /// Replaces a scene's notes with a file's (used after git brought a newer file and nothing here was unsaved).
        /// Notes not in the file are dropped, tombstones included.
        /// </summary>
        public static void Replace(string scene, StickyNoteFile file)
        {
            if (string.IsNullOrEmpty(scene)) return;
            var notes = EnsureScene(scene);
            notes.Clear();
            if (file?.Notes != null)
                foreach (var n in file.Notes)
                    if (n != null && !string.IsNullOrEmpty(n.Id)) { n.Scene = scene; notes[n.Id] = n; }
            RaiseChanged(scene);
        }

        public static StickyNoteFile Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<StickyNoteFile>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sticky Notes] Couldn't read notes JSON: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Remembers a scene (e.g. it just loaded) and, in a room, mixes in the room's notes for it, publishing ours if
        /// the room is missing some.
        /// </summary>
        public static void Track(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return;
            EnsureScene(scene);
            if (IsConnected) SyncShared(scene);
        }

        #endregion

        #region Room

        private static void OnMessage(string peerId, string json)
        {
            StickyNoteMessage msg;
            try { msg = JsonUtility.FromJson<StickyNoteMessage>(json); }
            catch { return; }
            var note = msg?.Note;
            if (note == null || string.IsNullOrEmpty(note.Id) || string.IsNullOrEmpty(note.Scene)) return;
            var current = FindAny(note.Id);
            if (current != null && (!note.Supersedes(current) || note.SameAs(current))) return;
            EnsureScene(note.Scene);
            Put(note);
            RaiseNoteChanged(note, false);
            RaiseChanged(note.Scene);
        }

        private static void OnSharedChanged(string key)
        {
            if (key == null) return;
            try
            {
                if (key == SharedIndexKey) { OnIndexChanged(); return; }
                if (!key.StartsWith(SharedPrefix, StringComparison.Ordinal)) return;
                SyncShared(key.Substring(SharedPrefix.Length));
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void OnConnected()
        {
            try
            {
                OnIndexChanged();
                foreach (var scene in _scenes.Keys.ToList()) SyncShared(scene);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static void OnIndexChanged()
        {
            var index = ReadIndex();
            foreach (var scene in index)
                if (!_scenes.ContainsKey(scene)) SyncShared(scene);
        }

        // Pulls the room's set for a scene into ours; pushes ours back if the room is missing anything we have.
        private static void SyncShared(string scene)
        {
            if (!IsConnected || string.IsNullOrEmpty(scene)) return;
            var shared = Parse(_transport.GetShared(SharedPrefix + scene));
            if (shared != null) Merge(shared, scene, local: false);
            if (!_scenes.TryGetValue(scene, out var mine) || mine.Count == 0) return;

            var theirs = new Dictionary<string, StickyNote>();
            if (shared?.Notes != null)
                foreach (var n in shared.Notes)
                    if (n != null && !string.IsNullOrEmpty(n.Id)) theirs[n.Id] = n;
            bool roomIsBehind = mine.Values.Any(n => !theirs.TryGetValue(n.Id, out var t) || (n.Supersedes(t) && !n.SameAs(t)));
            if (roomIsBehind) PublishShared(scene);
        }

        private static void PublishShared(string scene)
        {
            if (!IsConnected || string.IsNullOrEmpty(scene)) return;
            // Mix in what the room has first, so a note someone else just added isn't overwritten by our older set.
            var shared = Parse(_transport.GetShared(SharedPrefix + scene));
            if (shared != null) Merge(shared, scene, local: false);

            var cutoff = DateTime.UtcNow.AddDays(-TombstoneDays).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var file = new StickyNoteFile { Scene = scene };
            foreach (var n in GetNotes(scene, includeDeleted: true))
                if (!n.Deleted || string.CompareOrdinal(n.Updated, cutoff) > 0) file.Notes.Add(n);
            _transport.SetShared(SharedPrefix + scene, JsonUtility.ToJson(file));

            var index = ReadIndex();
            if (!index.Contains(scene))
            {
                index.Add(scene);
                _transport.SetShared(SharedIndexKey, JsonUtility.ToJson(new SceneIndex { Scenes = index }));
            }
        }

        [Serializable]
        private sealed class SceneIndex { public List<string> Scenes = new List<string>(); }

        private static List<string> ReadIndex()
        {
            if (!IsConnected) return new List<string>();
            string json = _transport.GetShared(SharedIndexKey);
            if (string.IsNullOrEmpty(json)) return new List<string>();
            try { return JsonUtility.FromJson<SceneIndex>(json)?.Scenes ?? new List<string>(); }
            catch { return new List<string>(); }
        }

        #endregion

        #region Internals

        private static Dictionary<string, StickyNote> EnsureScene(string scene)
        {
            if (!_scenes.TryGetValue(scene, out var notes)) _scenes[scene] = notes = new Dictionary<string, StickyNote>();
            return notes;
        }

        // Including tombstones.
        private static StickyNote FindAny(string id)
        {
            foreach (var notes in _scenes.Values)
                if (notes.TryGetValue(id, out var n)) return n;
            return null;
        }

        private static void Put(StickyNote note)
        {
            // A note never changes scene, but be safe: drop any copy filed under another scene.
            foreach (var pair in _scenes)
                if (pair.Key != note.Scene) pair.Value.Remove(note.Id);
            EnsureScene(note.Scene)[note.Id] = note;
        }

        private static void RaiseChanged(string scene)
        {
            if (Changed == null) return;
            foreach (Action<string> h in Changed.GetInvocationList())
            {
                try { h(scene); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void RaiseNoteChanged(StickyNote note, bool local)
        {
            if (NoteChanged == null) return;
            foreach (Action<StickyNote, bool> h in NoteChanged.GetInvocationList())
            {
                try { h(note, local); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        #endregion
    }
}
