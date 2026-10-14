using System;
using UnityEditor;
using UnityEngine;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// The small note editor that pops up from the Scene view or the Sticky Notes window: type, text, resolve, delete.
    /// It edits a copy and saves when it closes (click away or Save); Cancel throws the changes away. A new note isn't
    /// stuck anywhere until it has some text and is saved.
    /// </summary>
    public sealed class StickyNoteEditWindow : EditorWindow
    {
        private static readonly Vector2 Size = new Vector2(320, 250);

        private StickyNote _draft;
        private bool _isNew;
        private bool _discard;
        private bool _confirmDelete;
        private Vector2 _scroll;
        private GUIStyle _textStyle;
        private bool _focusText = true;

        /// <summary>Opens an existing note under <paramref name="screenRect"/> (screen coordinates).</summary>
        public static void Edit(StickyNote note, Rect screenRect)
        {
            if (note == null) return;
            Open(note.Clone(), false, screenRect);
        }

        /// <summary>Starts a new note at a point; it's added when saved with some text.</summary>
        public static void CreateNew(string scene, Vector3 position, Vector3 normal, string attachedId, string attachedName, Rect screenRect)
        {
            var draft = new StickyNote
            {
                Id = Guid.NewGuid().ToString("N"),
                Scene = scene,
                Position = position,
                Normal = normal,
                AttachedId = attachedId ?? "",
                AttachedName = attachedName ?? "",
                Author = StickyNoteStore.LocalName,
            };
            Open(draft, true, screenRect);
        }

        /// <summary>A screen rect at the mouse, for opening from an OnGUI.</summary>
        public static Rect AtMouse()
        {
            var p = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            return new Rect(p.x, p.y, 1, 1);
        }

        private static void Open(StickyNote draft, bool isNew, Rect screenRect)
        {
            foreach (var other in Resources.FindObjectsOfTypeAll<StickyNoteEditWindow>()) other.Close();
            var w = CreateInstance<StickyNoteEditWindow>();
            w._draft = draft;
            w._isNew = isNew;
            w.titleContent = new GUIContent(isNew ? "New Sticky Note" : "Sticky Note");
            w.ShowAsDropDown(screenRect, Size);
        }

        private void OnDestroy()
        {
            if (!_discard) StickyNotesEditorService.Safe(Save);
        }

        private void Save()
        {
            var d = _draft;
            if (d == null) return;
            _draft = null;
            if (_isNew)
            {
                if (string.IsNullOrWhiteSpace(d.Text)) return;   // nothing written: don't stick an empty note
                StickyNoteStore.Add(d.Scene, d.Position, d.Normal, d.NoteType, d.Text, d.AttachedId, d.AttachedName);
                return;
            }
            var current = StickyNoteStore.Find(d.Id);
            if (current == null) return;
            var copy = current.Clone();
            copy.Text = d.Text;
            copy.Type = d.Type;
            StickyNoteStore.Update(copy);
        }

        private void OnGUI()
        {
            var note = _draft;
            if (note == null) { Close(); return; }
            _textStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

            // A coloured band in the note's colour.
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 4), StickyNoteStyle.ColorOf(note));
            GUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(_isNew ? "New note" : note.Resolved ? "Resolved note" : "Note", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                note.NoteType = (StickyNoteType)EditorGUILayout.EnumPopup(note.NoteType, GUILayout.Width(90));
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUI.SetNextControlName("StickyNoteText");
            note.Text = EditorGUILayout.TextArea(note.Text ?? "", _textStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (_focusText && Event.current.type == EventType.Repaint)
            {
                _focusText = false;
                EditorGUI.FocusTextInControl("StickyNoteText");
                Repaint();
            }

            if (!_isNew)
            {
                var created = StickyNote.ParseTime(note.Created);
                string info = $"{note.Author}" + (created != DateTime.MinValue ? $" · {created:g}" : "");
                if (note.Resolved && !string.IsNullOrEmpty(note.ResolvedBy)) info += $" · resolved by {note.ResolvedBy}";
                EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            }
            if (!string.IsNullOrEmpty(note.AttachedName) || !string.IsNullOrEmpty(note.AttachedId))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"On {(string.IsNullOrEmpty(note.AttachedName) ? "an object" : note.AttachedName)}", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(note.AttachedId)))
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50))) SelectAttached(note);
                }
            }

            Action action = null;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_isNew ? "Add" : "Save")) action = Close;   // OnDestroy saves
                if (!_isNew)
                {
                    if (GUILayout.Button(note.Resolved ? "Reopen" : "Resolve"))
                        action = () =>
                        {
                            bool resolve = !note.Resolved;
                            Save();
                            StickyNoteStore.SetResolved(note.Id, resolve);
                            _discard = true;
                            Close();
                        };
                    if (GUILayout.Button(_confirmDelete ? "Really?" : "Delete"))
                        action = () =>
                        {
                            if (!_confirmDelete) { _confirmDelete = true; Repaint(); return; }
                            _discard = true;
                            StickyNoteStore.Delete(note.Id);
                            Close();
                        };
                }
                if (GUILayout.Button("Cancel")) action = () => { _discard = true; Close(); };
            }

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { _discard = true; e.Use(); action = Close; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && (e.control || e.command))
            {
                e.Use();
                action = Close;
            }
            if (action != null) EditorApplication.delayCall += () => { if (this != null) action(); };
        }

        /// <summary>Selects and pings the object a note is attached to (editor GlobalObjectId), if it's loaded.</summary>
        public static bool SelectAttached(StickyNote note)
        {
            if (note == null || string.IsNullOrEmpty(note.AttachedId)) return false;
            if (!GlobalObjectId.TryParse(note.AttachedId, out var id)) return false;
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            if (obj == null) return false;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
            return true;
        }
    }
}
