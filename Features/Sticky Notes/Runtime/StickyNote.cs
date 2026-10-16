using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Remedy.StickyNotes
{
    /// <summary>How loud a note is. The colour of its icon follows it (see <see cref="StickyNoteStyle"/>).</summary>
    public enum StickyNoteType
    {
        /// <summary>A remark or idea ("this corridor feels long").</summary>
        Default = 0,
        /// <summary>Something that's off but not broken ("the jump here is barely makeable").</summary>
        Warning = 1,
        /// <summary>Something broken ("fell through the floor here").</summary>
        Error = 2,
    }

    /// <summary>
    /// Which Kanban column a note is in. It isn't stored: it follows from <see cref="StickyNote.Resolved"/> and
    /// <see cref="StickyNote.Assignee"/>, so the board can never disagree with the notes themselves.
    /// </summary>
    public enum StickyNoteStatus
    {
        /// <summary>Open and nobody has taken it.</summary>
        ToDo = 0,
        /// <summary>Open and someone took it (it has an assignee).</summary>
        InProgress = 1,
        /// <summary>Resolved.</summary>
        Done = 2,
    }

    /// <summary>
    /// One note stuck to a point in the world. Plain data (JsonUtility friendly) so the same object travels over the
    /// collaboration room, sits in the per-scene JSON file in the repository and is embedded in builds.
    ///
    /// Notes are never edited in place by the UI: take a <see cref="Clone"/>, change it and hand it to
    /// <see cref="StickyNoteStore.Update"/>, which stamps <see cref="Updated"/> so everyone agrees on the newest version.
    /// </summary>
    [Serializable]
    public sealed class StickyNote
    {
        /// <summary>A GUID ("N" format). Never changes, so every copy of the note can be matched up.</summary>
        public string Id;
        /// <summary>Project path of the scene the note belongs to ("Assets/Levels/Forest.unity").</summary>
        public string Scene;
        public Vector3 Position;
        /// <summary>The surface normal where it was stuck, so the icon can sit a little off the wall it's on.</summary>
        public Vector3 Normal = Vector3.up;
        /// <summary>
        /// "Default", "Warning" or "Error". Kept as text rather than a number so the notes file reads well in a diff;
        /// use <see cref="NoteType"/> from code.
        /// </summary>
        public string Type = nameof(StickyNoteType.Default);
        public string Text = "";
        public string Author = "";
        /// <summary>The author's collaboration id when they were in a room (lets UIs tell "mine" from "theirs").</summary>
        public string AuthorId = "";
        /// <summary>"#RRGGBB": the author's collaboration colour, for the little author stripe.</summary>
        public string AuthorColor = "#FFFFFF";
        /// <summary>UTC, ISO 8601 with milliseconds ("2026-10-14T09:30:00.000Z"), so plain text comparison orders them.</summary>
        public string Created = "";
        /// <summary>Same format as <see cref="Created"/>. The newest version of a note wins when copies disagree.</summary>
        public string Updated = "";
        public bool Resolved;
        /// <summary>Who resolved it (empty while open).</summary>
        public string ResolvedBy = "";
        /// <summary>
        /// Optional object the note is about: a GlobalObjectId string when it was made in the editor, or a runtime
        /// collaboration id when it was made in a build. Informational: the note stays where <see cref="Position"/> says.
        /// </summary>
        public string AttachedId = "";
        /// <summary>The attached object's name when the note was made (readable even when the id can't be resolved).</summary>
        public string AttachedName = "";
        /// <summary>
        /// A deleted note is kept as a "tombstone" while the room is alive so a stale copy can't bring it back. Tombstones
        /// are never written to the notes file.
        /// </summary>
        public bool Deleted;

        /// <summary>
        /// Who took the note on the Kanban board (their name), empty while it's in To Do. Kept together with
        /// <see cref="AssigneeId"/> because collaboration ids change between sessions while names usually don't.
        /// </summary>
        public string Assignee = "";
        /// <summary>The assignee's collaboration id when they took it (empty without a session).</summary>
        public string AssigneeId = "";
        /// <summary>When it was taken (same format as <see cref="Created"/>). Used to return tasks of people who are long gone.</summary>
        public string ClaimedAt = "";
        /// <summary>
        /// Optional performance snapshot taken when the note was made, as compact "key=value;..." text (see
        /// <see cref="StickyNotePerf"/>). Empty when no snapshot was attached. Text rather than a nested object because
        /// JsonUtility always writes nested objects, and most notes have no snapshot.
        /// </summary>
        public string Perf = "";
        /// <summary>
        /// Project-relative path of a short Profiler capture (.raw) recorded when the note was made in the editor's Play
        /// Mode. Only meaningful on the machine (or repository) that has the file.
        /// </summary>
        public string PerfCapture = "";

        /// <summary><see cref="Type"/> as an enum (unknown text reads as Default).</summary>
        public StickyNoteType NoteType
        {
            get => Enum.TryParse(Type, true, out StickyNoteType t) ? t : StickyNoteType.Default;
            set => Type = value.ToString();
        }

        /// <summary>The Kanban column this note is in.</summary>
        public StickyNoteStatus Status => Resolved ? StickyNoteStatus.Done
            : !string.IsNullOrEmpty(Assignee) || !string.IsNullOrEmpty(AssigneeId) ? StickyNoteStatus.InProgress
            : StickyNoteStatus.ToDo;

        /// <summary>
        /// True when the note is assigned to this person: the same collaboration id, or (because ids change between
        /// sessions) the same name.
        /// </summary>
        public bool IsAssignedTo(string peerId, string name)
        {
            if (!string.IsNullOrEmpty(peerId) && AssigneeId == peerId) return true;
            return !string.IsNullOrEmpty(name) && string.Equals(Assignee, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The note has a performance snapshot attached.</summary>
        public bool HasPerf => !string.IsNullOrEmpty(Perf);

        public StickyNote Clone() => (StickyNote)MemberwiseClone();

        /// <summary>The current time in the format notes use.</summary>
        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        /// <summary>Parses <see cref="Created"/>/<see cref="Updated"/> for display (local time); MinValue when unreadable.</summary>
        public static DateTime ParseTime(string iso)
        {
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? t.ToLocalTime() : DateTime.MinValue;
        }

        /// <summary>
        /// True when this copy should replace <paramref name="other"/>: it's newer, or equally new and wins a fixed
        /// tie-break (so two editors that changed a note in the same millisecond still agree on one version).
        /// </summary>
        public bool Supersedes(StickyNote other)
        {
            if (other == null) return true;
            int c = string.CompareOrdinal(Updated ?? "", other.Updated ?? "");
            if (c != 0) return c > 0;
            if (Deleted != other.Deleted) return Deleted;   // a delete beats an edit made at the same instant
            return string.CompareOrdinal(JsonUtility.ToJson(this), JsonUtility.ToJson(other)) > 0;
        }

        /// <summary>Same content (used to skip pointless republishing and file writes).</summary>
        public bool SameAs(StickyNote other) => other != null && JsonUtility.ToJson(this) == JsonUtility.ToJson(other);

        public override string ToString() => $"[{Type}] {Text} ({Author})";
    }

    /// <summary>The notes of one scene, as stored in &lt;Scene&gt;.notes.json, in the room's shared store and in builds.</summary>
    [Serializable]
    public sealed class StickyNoteFile
    {
        public int Version = 1;
        public string Scene = "";
        public List<StickyNote> Notes = new List<StickyNote>();
    }

    /// <summary>What travels on the "sticky-notes" channel: one note's newest version.</summary>
    [Serializable]
    public sealed class StickyNoteMessage
    {
        /// <summary>"add", "update", "resolve", "reopen" or "delete". Informational: receivers just keep the newest version.</summary>
        public string Op = "update";
        public StickyNote Note;
    }

    /// <summary>Colours and labels shared by the in-game view and the editor tools.</summary>
    public static class StickyNoteStyle
    {
        public static readonly Color DefaultColor = new Color(1f, 0.86f, 0.30f);   // sticky-note yellow
        public static readonly Color WarningColor = new Color(1f, 0.55f, 0.10f);   // orange
        public static readonly Color ErrorColor = new Color(0.92f, 0.22f, 0.22f);  // red
        public static readonly Color ResolvedColor = new Color(0.55f, 0.75f, 0.55f, 0.8f);

        public static Color ColorOf(StickyNoteType type)
        {
            switch (type)
            {
                case StickyNoteType.Warning: return WarningColor;
                case StickyNoteType.Error: return ErrorColor;
                default: return DefaultColor;
            }
        }

        /// <summary>The icon colour of a note: its type's colour, or a muted green once resolved.</summary>
        public static Color ColorOf(StickyNote note) => note == null ? DefaultColor : note.Resolved ? ResolvedColor : ColorOf(note.NoteType);

        /// <summary>A one-character glyph drawn on the icon.</summary>
        public static string GlyphOf(StickyNote note)
        {
            if (note == null) return "";
            if (note.Resolved) return "✓";
            switch (note.NoteType)
            {
                case StickyNoteType.Warning: return "!";
                case StickyNoteType.Error: return "×";
                default: return "…";
            }
        }

        #region Paper (the sticky note image)

        /// <summary>
        /// The sticky-note paper image (media/StickyNote.png in the toolkit). The editor sets it when it loads (Project
        /// Settings can point it at another texture); builds get it from the scene data object the build step adds.
        /// Null means "draw the plain coloured square instead".
        /// </summary>
        public static Texture2D PaperTexture;

        /// <summary>
        /// Where text and buttons may go on the paper, as fractions of its size (left, top, right, bottom). The bottom
        /// margin is bigger because of the curled corner in the default image. Change it if you use your own image.
        /// </summary>
        public static Vector4 PaperInsets = new Vector4(0.075f, 0.065f, 0.075f, 0.15f);

        /// <summary>Width / height of the paper (the image's own aspect, or roughly square without one).</summary>
        public static float PaperAspect => PaperTexture != null && PaperTexture.height > 0 ? (float)PaperTexture.width / PaperTexture.height : 0.95f;

        /// <summary>
        /// The tint multiplied onto the (yellow) paper: none for Default, orange for Warning, red for Error, a muted green
        /// once resolved.
        /// </summary>
        public static Color PaperTint(StickyNote note)
        {
            if (note == null) return Color.white;
            if (note.Resolved) return new Color(0.70f, 1f, 0.80f, 0.9f);
            return PaperTint(note.NoteType);
        }

        public static Color PaperTint(StickyNoteType type)
        {
            switch (type)
            {
                case StickyNoteType.Warning: return new Color(1f, 0.64f, 0.36f);
                case StickyNoteType.Error: return new Color(1f, 0.36f, 0.32f);
                default: return Color.white;
            }
        }

        /// <summary>The part of a paper rect that text and buttons may use (inside <see cref="PaperInsets"/>).</summary>
        public static Rect PaperContentRect(Rect paper)
        {
            var i = PaperInsets;
            return new Rect(paper.x + paper.width * i.x, paper.y + paper.height * i.y,
                paper.width * (1f - i.x - i.z), paper.height * (1f - i.y - i.w));
        }

        /// <summary>The biggest rect with the paper's aspect that fits in <paramref name="area"/>, centred (scale-to-fit).</summary>
        public static Rect FitPaper(Rect area)
        {
            float aspect = PaperAspect;
            float w = area.width, h = area.width / aspect;
            if (h > area.height) { h = area.height; w = h * aspect; }
            return new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
        }

        /// <summary>
        /// Draws the paper image tinted for the note (IMGUI; works in the editor and in game). Returns false when there's
        /// no image, so the caller draws its fallback.
        /// </summary>
        public static bool DrawPaper(Rect rect, Color tint)
        {
            var tex = PaperTexture;
            if (tex == null || Event.current == null || Event.current.type != EventType.Repaint) return tex != null;
            var old = GUI.color;
            GUI.color = new Color(old.r * tint.r, old.g * tint.g, old.b * tint.b, old.a * tint.a);
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, true);
            GUI.color = old;
            return true;
        }

        /// <summary>Smooth 0..1 ease for the open animation.</summary>
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        /// <summary>How long the note takes to scale up into its editor panel (seconds).</summary>
        public static float OpenAnimationSeconds = 0.15f;

        #endregion

        public static Color ParseColor(string html, Color fallback) => ColorUtility.TryParseHtmlString(html, out var c) ? c : fallback;

        public static string ToHtml(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>The first line of a note, shortened for labels and lists.</summary>
        public static string Summary(StickyNote note, int max = 60)
        {
            string text = note?.Text ?? "";
            int nl = text.IndexOf('\n');
            if (nl >= 0) text = text.Substring(0, nl);
            text = text.Trim();
            if (text.Length == 0) text = "(empty note)";
            return text.Length > max ? text.Substring(0, max - 1) + "…" : text;
        }

        /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago" or a date.</summary>
        public static string Ago(string iso)
        {
            var local = StickyNote.ParseTime(iso);
            if (local == DateTime.MinValue) return "";
            var span = DateTime.Now - local;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
            return local.ToString("d");
        }

        /// <summary>Sort order of the board's To Do column: errors, then warnings, then remarks; oldest first within each.</summary>
        public static int CompareByPriority(StickyNote a, StickyNote b)
        {
            int c = b.NoteType.CompareTo(a.NoteType);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Created, b.Created);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }
    }
}
