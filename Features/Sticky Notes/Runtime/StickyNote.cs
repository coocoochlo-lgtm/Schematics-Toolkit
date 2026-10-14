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

        /// <summary><see cref="Type"/> as an enum (unknown text reads as Default).</summary>
        public StickyNoteType NoteType
        {
            get => Enum.TryParse(Type, true, out StickyNoteType t) ? t : StickyNoteType.Default;
            set => Type = value.ToString();
        }

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
    }
}
