using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Remedy.Schematics.Collaboration;
using UnityEditor;
using UnityEngine;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// Puts tasks back on the To Do column when the person who took them isn't around any more, so a task can't stay
    /// "In Progress" forever because somebody went home.
    ///
    /// <para><b>Who does it.</b> One editor per room, so it happens once: the session's host, or (when no host is known)
    /// the editor with the smallest collaboration id. Without a session, your own editor. Even if two editors did it at
    /// the same moment nothing bad would happen: notes merge "newest wins", and both would write the same result.</para>
    ///
    /// <para><b>When.</b></para>
    /// <list type="bullet">
    /// <item>Someone who holds In Progress tasks leaves the session (or drops out): once they've been gone for
    /// <see cref="StickyNotesSettings.LeaveGraceSeconds"/> (a reconnect or editor restart within that keeps the tasks).
    /// Someone counts as back when a peer with the same id or the same name is in the room.</item>
    /// <item>On load, for an assignee who isn't here and wasn't seen leaving: once their claim is older than
    /// <see cref="StickyNotesSettings.AbsentGraceHours"/>. The assignee's own editor refreshes the claim
    /// (<see cref="StickyNoteStore.TouchClaim"/>) while they're working, so the claim time works as "last seen".</item>
    /// <item>You quit the editor: your own unfinished tasks go back (<see cref="ReturnMine"/>, called by the editor
    /// service before it writes the files; setting <see cref="StickyNotesSettings.ReturnMyTasksOnQuit"/>).</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    public static class StickyNotesTaskReturn
    {
        private const string GoneKey = "Remedy.StickyNotes.GoneSince";
        // Assignee key ("id:..." or "name:...") -> UTC time they were seen leaving (DateTime ticks).
        private static readonly Dictionary<string, long> _goneSince = new Dictionary<string, long>();
        private static double _nextCheck;

        static StickyNotesTaskReturn()
        {
            Restore();
            CollabSession.PeerLeft += peer => StickyNotesEditorService.Safe(() => OnPeerLeft(peer));
            CollabSession.PeerJoined += peer => StickyNotesEditorService.Safe(() => OnPeerBack(peer));
            CollabSession.PeerChanged += peer => StickyNotesEditorService.Safe(() => OnPeerBack(peer));
            CollabSession.Connected += () => _nextCheck = 0;
            CollabSession.HostChanged += () => _nextCheck = 0;
            StickyNoteStore.Changed += _ => { if (_nextCheck > EditorApplication.timeSinceStartup + 2) _nextCheck = EditorApplication.timeSinceStartup + 2; };
            EditorApplication.update += Tick;
        }

        /// <summary>Gives back every unfinished task assigned to you (used when you quit).</summary>
        public static int ReturnMine()
        {
            int n = 0;
            foreach (var note in StickyNoteStore.AllNotes.ToList())
                if (note.Status == StickyNoteStatus.InProgress && IsMine(note) && StickyNoteStore.ReturnToDo(note.Id, "return")) n++;
            if (n > 0) Debug.Log($"[Sticky Notes] {n} unfinished task{(n == 1 ? "" : "s")} of yours went back to To Do.");
            return n;
        }

        /// <summary>True when a note is assigned to you (your collaboration id or either of your names).</summary>
        public static bool IsMine(StickyNote note)
        {
            if (note == null) return false;
            if (note.IsAssignedTo(StickyNoteStore.LocalPeerId, StickyNoteStore.LocalName)) return true;
            string name = null;
            try { name = CollabSettings.EffectiveName; }
            catch { }
            return note.IsAssignedTo(null, name);
        }

        /// <summary>True when this editor is the one that returns tasks for the room (see the class notes).</summary>
        public static bool IsPerformer
        {
            get
            {
                if (!CollabSession.IsConnected) return true;
                if (CollabSession.IsHost) return true;
                var peers = CollabSession.Peers;
                if (peers == null) return true;
                if (peers.Any(p => p != null && p.IsHost)) return false;
                var editors = peers.Where(p => p != null && !string.IsNullOrEmpty(p.Id) && !CollabSessionTransport.IsPlaytester(p)).ToList();
                if (editors.Count == 0) return true;
                var first = editors.OrderBy(p => p.Id, StringComparer.Ordinal).First();
                return first.IsLocal;
            }
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextCheck || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            _nextCheck = EditorApplication.timeSinceStartup + 5.0;
            StickyNotesEditorService.Safe(Check);
        }

        /// <summary>Runs the checks now (normally every few seconds).</summary>
        public static void Check()
        {
            var settings = StickyNotesSettings.Instance;
            var now = DateTime.UtcNow;
            foreach (var note in StickyNoteStore.AllNotes.ToList())
            {
                if (note.Status != StickyNoteStatus.InProgress) continue;

                if (IsMine(note))
                {
                    // Still here: refresh the claim now and then so others' editors know (see AbsentGraceHours).
                    var claimed = ParseUtc(note.ClaimedAt);
                    if (claimed == DateTime.MinValue || (now - claimed).TotalHours > Math.Max(0.1, settings.AbsentGraceHours * 0.5))
                        StickyNoteStore.TouchClaim(note.Id);
                    continue;
                }

                if (!settings.ReturnTasksWhenAssigneeLeaves || !IsPerformer) continue;
                if (IsPresent(note)) continue;

                bool giveBack;
                if (TryGoneSince(note, out var gone)) giveBack = (now - gone).TotalSeconds >= Math.Max(0f, settings.LeaveGraceSeconds);
                else
                {
                    var claimed = ParseUtc(note.ClaimedAt);
                    giveBack = claimed == DateTime.MinValue || (now - claimed).TotalHours >= Math.Max(0f, settings.AbsentGraceHours);
                }
                if (giveBack && StickyNoteStore.ReturnToDo(note.Id, "return"))
                    Debug.Log($"[Sticky Notes] \"{StickyNoteStyle.Summary(note, 40)}\" went back to To Do: {note.Assignee} isn't in the session any more.");
            }
        }

        // The assignee is in the room right now (same id, or same name since ids change between sessions).
        private static bool IsPresent(StickyNote note)
        {
            if (!CollabSession.IsConnected) return false;
            var peers = CollabSession.Peers;
            if (peers == null) return false;
            foreach (var p in peers)
                if (p != null && note.IsAssignedTo(p.Id, p.Name)) return true;
            return false;
        }

        private static void OnPeerLeft(CollabPeer peer)
        {
            if (peer == null || peer.IsLocal) return;
            long ticks = DateTime.UtcNow.Ticks;
            if (!string.IsNullOrEmpty(peer.Id)) _goneSince["id:" + peer.Id] = ticks;
            if (!string.IsNullOrEmpty(peer.Name)) _goneSince["name:" + peer.Name.ToLowerInvariant()] = ticks;
            Save();
            _nextCheck = 0;
        }

        private static void OnPeerBack(CollabPeer peer)
        {
            if (peer == null) return;
            bool changed = false;
            if (!string.IsNullOrEmpty(peer.Id)) changed |= _goneSince.Remove("id:" + peer.Id);
            if (!string.IsNullOrEmpty(peer.Name)) changed |= _goneSince.Remove("name:" + peer.Name.ToLowerInvariant());
            if (changed) Save();
        }

        private static bool TryGoneSince(StickyNote note, out DateTime gone)
        {
            gone = DateTime.MinValue;
            long ticks = 0;
            if (!string.IsNullOrEmpty(note.AssigneeId) && _goneSince.TryGetValue("id:" + note.AssigneeId, out var a)) ticks = Math.Max(ticks, a);
            if (!string.IsNullOrEmpty(note.Assignee) && _goneSince.TryGetValue("name:" + note.Assignee.ToLowerInvariant(), out var b)) ticks = Math.Max(ticks, b);
            if (ticks == 0) return false;
            gone = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }

        private static DateTime ParseUtc(string iso)
        {
            return !string.IsNullOrEmpty(iso) && DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;
        }

        #region Surviving reloads

        [Serializable]
        private sealed class Memory { public List<string> Keys = new List<string>(); public List<long> Ticks = new List<long>(); }

        private static void Save()
        {
            var m = new Memory();
            foreach (var pair in _goneSince) { m.Keys.Add(pair.Key); m.Ticks.Add(pair.Value); }
            SessionState.SetString(GoneKey, JsonUtility.ToJson(m));
        }

        private static void Restore()
        {
            try
            {
                var m = JsonUtility.FromJson<Memory>(SessionState.GetString(GoneKey, ""));
                if (m == null) return;
                for (int i = 0; i < m.Keys.Count && i < m.Ticks.Count; i++) _goneSince[m.Keys[i]] = m.Ticks[i];
            }
            catch { }
        }

        #endregion
    }
}
