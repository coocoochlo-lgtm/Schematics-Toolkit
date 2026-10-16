using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// The note editor that pops up from the Scene view, the Sticky Notes window or the Kanban board. It's drawn as the
    /// sticky-note paper itself (the image scaled to fit, tinted for the note's type) with the type, text, task and
    /// snapshot info and the buttons laid out inside the paper's writable area (<see cref="StickyNoteStyle.PaperInsets"/>).
    /// When it opens it scales up from a small note in about 0.15 s (the Scene view does that part itself, from the
    /// note's icon, and then opens this window without its own animation).
    ///
    /// It edits a copy and saves when it closes (click away or Save); Cancel or Esc throws the changes away. A new note
    /// isn't stuck anywhere until it has some text and is saved.
    /// </summary>
    public sealed class StickyNoteEditWindow : EditorWindow
    {
        /// <summary>The paper's width on screen; the height follows the image's aspect.</summary>
        public const float PanelWidth = 380f;

        /// <summary>The window's size: the paper's aspect at <see cref="PanelWidth"/> (taller without an image, for the band).</summary>
        public static Vector2 PanelSize =>
            new Vector2(PanelWidth, StickyNoteStyle.PaperTexture != null ? Mathf.Round(PanelWidth / StickyNoteStyle.PaperAspect) : 300f);

        /// <summary>The id of the note open in the editor right now (null when none).</summary>
        public static string OpenNoteId { get; private set; }

        private StickyNote _draft;
        private bool _isNew;
        private bool _discard;
        private bool _confirmDelete;
        private bool _attachPerf;
        private string _draftPerf;
        private bool _showPerf;
        private Vector2 _scroll;
        private bool _focusText = true;
        private double _openedAt;
        private bool _animate;
        private GUIStyle _textStyle, _ink, _inkSmall, _inkTitle, _inkToggle, _inkFoldout;

        /// <summary>Opens an existing note under <paramref name="screenRect"/> (screen coordinates).</summary>
        public static void Edit(StickyNote note, Rect screenRect, bool animate = true)
        {
            if (note == null) return;
            Open(note.Clone(), false, screenRect, animate, null);
        }

        /// <summary>Starts a new note at a point; it's added when saved with some text.</summary>
        public static void CreateNew(string scene, Vector3 position, Vector3 normal, string attachedId, string attachedName, Rect screenRect, bool animate = true)
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
            // Taken now, when the note is started (in Play Mode this includes the frame rate; in Edit Mode memory and device).
            string perf = null;
            StickyNotesEditorService.Safe(() => perf = StickyNotePerf.Capture(scene, position, SceneView.lastActiveSceneView != null && !Application.isPlaying ? SceneView.lastActiveSceneView.camera : null));
            Open(draft, true, screenRect, animate, perf);
        }

        /// <summary>A screen rect at the mouse, for opening from an OnGUI.</summary>
        public static Rect AtMouse()
        {
            var p = GUIUtility.GUIToScreenPoint(Event.current != null ? Event.current.mousePosition : Vector2.zero);
            return new Rect(p.x, p.y, 1, 1);
        }

        private static void Open(StickyNote draft, bool isNew, Rect screenRect, bool animate, string perf)
        {
            foreach (var other in Resources.FindObjectsOfTypeAll<StickyNoteEditWindow>()) other.Close();
            var w = CreateInstance<StickyNoteEditWindow>();
            w._draft = draft;
            w._isNew = isNew;
            w._draftPerf = perf;
            w._animate = animate;
            w._openedAt = EditorApplication.timeSinceStartup;
            w.titleContent = new GUIContent(isNew ? "New Sticky Note" : "Sticky Note");
            OpenNoteId = draft.Id;
            w.ShowAsDropDown(screenRect, PanelSize);
        }

        private void OnDestroy()
        {
            if (_draft != null && OpenNoteId == _draft.Id) OpenNoteId = null;
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
                StickyNotePerf.AttachIfWanted(d, _attachPerf, _draftPerf);
                StickyNoteStore.Add(d.Scene, d.Position, d.Normal, d.NoteType, d.Text, d.AttachedId, d.AttachedName, d.Perf, d.PerfCapture, d.Id);
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
            EnsureStyles();

            var full = new Rect(0, 0, position.width, position.height);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(full, new Color(0.16f, 0.16f, 0.16f));

            // Scale up from a small note in the middle.
            float t = _animate && StickyNoteStyle.OpenAnimationSeconds > 0f
                ? StickyNoteStyle.EaseOut((float)((EditorApplication.timeSinceStartup - _openedAt) / StickyNoteStyle.OpenAnimationSeconds))
                : 1f;
            var paperRect = StickyNoteStyle.PaperTexture != null ? StickyNoteStyle.FitPaper(full) : full;
            if (t < 1f)
            {
                var small = new Rect(paperRect.center.x - 12, paperRect.center.y - 12, 24, 24);
                var r = new Rect(Vector2.Lerp(small.position, paperRect.position, t), Vector2.Lerp(small.size, paperRect.size, t));
                if (!StickyNoteStyle.DrawPaper(r, StickyNoteStyle.PaperTint(note)) && Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(r, StickyNoteStyle.ColorOf(note));
                Repaint();
                return;
            }

            Rect content;
            bool paper = StickyNoteStyle.DrawPaper(paperRect, StickyNoteStyle.PaperTint(note));
            if (paper) content = StickyNoteStyle.PaperContentRect(paperRect);
            else
            {
                // No image: the old look, a band in the note's colour.
                EditorGUI.DrawRect(new Rect(0, 0, position.width, 4), StickyNoteStyle.ColorOf(note));
                content = new Rect(6, 10, position.width - 12, position.height - 16);
            }

            Action action = null;
            GUILayout.BeginArea(content);
            DrawContents(note, paper, ref action);
            GUILayout.EndArea();

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { _discard = true; e.Use(); action = Close; }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && (e.control || e.command))
            {
                e.Use();
                action = Close;
            }
            if (action != null) EditorApplication.delayCall += () => { if (this != null) action(); };
        }

        private void DrawContents(StickyNote note, bool paper, ref Action action)
        {
            var ink = paper ? _ink : EditorStyles.label;
            var inkSmall = paper ? _inkSmall : EditorStyles.miniLabel;

            // Title row: what it is, and the task button.
            using (new EditorGUILayout.HorizontalScope())
            {
                string title = _isNew ? "New note"
                    : note.Status == StickyNoteStatus.Done ? "Done"
                    : note.Status == StickyNoteStatus.InProgress ? $"In progress · {note.Assignee}"
                    : "To do";
                GUILayout.Label(title, paper ? _inkTitle : EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (!_isNew && !note.Resolved)
                {
                    if (note.Status == StickyNoteStatus.InProgress && StickyNotesTaskReturn.IsMine(note))
                    {
                        if (GUILayout.Button(new GUIContent("Give back", "Put it back in To Do for someone else"), EditorStyles.miniButton, GUILayout.Width(70)))
                            action = () => { StickyNoteStore.GiveBack(note.Id); Refresh(note.Id); };
                    }
                    else if (GUILayout.Button(new GUIContent("Take", "Assign it to you (moves it to In Progress on the Kanban board)"), EditorStyles.miniButton, GUILayout.Width(50)))
                        action = () => { StickyNoteStore.Claim(note.Id); Refresh(note.Id); };
                }
            }

            // Type: three buttons (a popup menu would take focus and close this window).
            int type = GUILayout.Toolbar((int)note.NoteType, new[] { "Default", "Warning", "Error" }, EditorStyles.miniButton);
            if (type != (int)note.NoteType) note.NoteType = (StickyNoteType)type;
            GUILayout.Space(2);

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
                GUILayout.Label(info, inkSmall);
            }
            if (!string.IsNullOrEmpty(note.AttachedName) || !string.IsNullOrEmpty(note.AttachedId))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"On {(string.IsNullOrEmpty(note.AttachedName) ? "an object" : note.AttachedName)}", inkSmall);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(note.AttachedId)))
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50))) SelectAttached(note);
                }
            }

            DrawSnapshot(note, paper, inkSmall);

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
        }

        private void DrawSnapshot(StickyNote note, bool paper, GUIStyle inkSmall)
        {
            if (_isNew)
            {
                bool auto = StickyNotePerf.ShouldAttach(note.NoteType, false);
                using (new EditorGUI.DisabledScope(auto))
                {
                    var label = new GUIContent(auto ? "Attach performance snapshot (automatic)" : "Attach performance snapshot",
                        "FPS, frame times, memory, draw calls and device info from when you started the note." +
                        (Application.isPlaying && StickyNotePerf.ProfilerCapturePath != null ? " In Play Mode a short Profiler capture is saved too." : ""));
                    bool v = GUILayout.Toggle(_attachPerf || auto, label, paper ? _inkToggle : EditorStyles.toggle);
                    if (!auto) _attachPerf = v;
                }
                return;
            }
            if (!note.HasPerf) return;

            _showPerf = EditorGUILayout.Foldout(_showPerf, "Snapshot: " + StickyNotePerf.Summary(note.Perf), true, paper ? _inkFoldout : EditorStyles.foldout);
            if (!_showPerf) return;
            GUILayout.Label(StickyNotePerf.Details(note.Perf), inkSmall);
            if (!string.IsNullOrEmpty(note.PerfCapture))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool exists = File.Exists(note.PerfCapture);
                    GUILayout.Label(exists ? Path.GetFileName(note.PerfCapture) : "Profiler capture (not on this machine)", inkSmall);
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!exists))
                    {
                        if (GUILayout.Button("Open in Profiler", EditorStyles.miniButtonLeft)) OpenCapture(note.PerfCapture);
                        if (GUILayout.Button("Reveal", EditorStyles.miniButtonRight)) EditorUtility.RevealInFinder(note.PerfCapture);
                    }
                }
            }
        }

        // After Take/Give back: show the stored version (with its new assignee) but keep the text being edited.
        private void Refresh(string id)
        {
            var current = StickyNoteStore.Find(id);
            if (current == null || _draft == null) return;
            var edited = _draft;
            _draft = current.Clone();
            _draft.Text = edited.Text;
            _draft.Type = edited.Type;
            Repaint();
        }

        /// <summary>Opens a note's Profiler capture (.raw) in the Profiler window.</summary>
        public static void OpenCapture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            EditorApplication.ExecuteMenuItem("Window/Analysis/Profiler");
            try { UnityEditorInternal.ProfilerDriver.LoadProfile(Path.GetFullPath(path), false); }
            catch (Exception e) { Debug.LogWarning($"[Sticky Notes] Couldn't load {path} into the Profiler: {e.Message}"); }
        }

        private void EnsureStyles()
        {
            if (_textStyle != null) return;
            _textStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            var ink = new Color(0.12f, 0.10f, 0.05f);
            _ink = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
            _ink.normal.textColor = ink;
            _inkTitle = new GUIStyle(_ink) { fontStyle = FontStyle.Bold, fontSize = 13, wordWrap = false };
            _inkSmall = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _inkSmall.normal.textColor = ink;
            _inkToggle = new GUIStyle(EditorStyles.toggle);
            _inkToggle.normal.textColor = _inkToggle.onNormal.textColor = _inkToggle.hover.textColor = _inkToggle.onHover.textColor =
                _inkToggle.focused.textColor = _inkToggle.onFocused.textColor = ink;
            _inkFoldout = new GUIStyle(EditorStyles.foldout);
            _inkFoldout.normal.textColor = _inkFoldout.onNormal.textColor = _inkFoldout.hover.textColor = _inkFoldout.onHover.textColor =
                _inkFoldout.focused.textColor = _inkFoldout.onFocused.textColor = _inkFoldout.active.textColor = _inkFoldout.onActive.textColor = ink;
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
