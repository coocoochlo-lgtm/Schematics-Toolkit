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
    /// Window &gt; Schematics &gt; Sticky Notes: every note, grouped by scene, filtered by type, resolved state and text.
    /// Double-click a note to frame it in the Scene view (opening its scene if needed); right-click for Edit, Resolve and
    /// Delete. The header shows whether notes are live in a session and whether this editor has notes files to save or
    /// commit.
    /// </summary>
    public sealed class StickyNotesWindow : EditorWindow
    {
        private enum SceneFilter { OpenScenes, AllScenes }

        [SerializeField] private SceneFilter _sceneFilter = SceneFilter.OpenScenes;
        [SerializeField] private bool _showDefault = true, _showWarning = true, _showError = true;
        [SerializeField] private string _search = "";
        [SerializeField] private string _selectedId;

        private Vector2 _scroll;
        private GUIStyle _rowStyle, _headerStyle, _previewStyle;

        [MenuItem("Window/Schematics/Sticky Notes")]
        public static void Open()
        {
            var w = GetWindow<StickyNotesWindow>();
            w.titleContent = new GUIContent("Sticky Notes", EditorGUIUtility.IconContent("console.infoicon.sml").image);
            w.Show();
        }

        private void OnEnable()
        {
            StickyNoteStore.Changed += OnStoreChanged;
            if (_sceneFilter == SceneFilter.AllScenes) StickyNotesEditorService.LoadAllFiles();
        }

        private void OnDisable() => StickyNoteStore.Changed -= OnStoreChanged;

        private void OnStoreChanged(string scene) => Repaint();

        private void OnInspectorUpdate() => Repaint();   // connection/save status

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            DrawStatus();

            var groups = Collect();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (groups.Count == 0)
                EditorGUILayout.HelpBox(_sceneFilter == SceneFilter.OpenScenes
                    ? "No notes in the open scenes. Add one with the Scene view's Sticky Notes overlay, or press F9 in Play Mode (F8 shows notes)."
                    : "No notes match.", MessageType.Info);
            foreach (var group in groups)
            {
                GUILayout.Label($"{Path.GetFileNameWithoutExtension(group.Key)}  ({group.Value.Count})", _headerStyle);
                foreach (var note in group.Value) DrawRow(note);
                GUILayout.Space(4);
            }
            EditorGUILayout.EndScrollView();

            var selected = StickyNoteStore.Find(_selectedId);
            if (selected != null) DrawPreview(selected);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var filter = (SceneFilter)EditorGUILayout.EnumPopup(_sceneFilter, EditorStyles.toolbarDropDown, GUILayout.Width(100));
                if (filter != _sceneFilter)
                {
                    _sceneFilter = filter;
                    if (filter == SceneFilter.AllScenes) StickyNotesEditorService.LoadAllFiles();
                }
                _showDefault = GUILayout.Toggle(_showDefault, "Default", EditorStyles.toolbarButton);
                _showWarning = GUILayout.Toggle(_showWarning, "Warning", EditorStyles.toolbarButton);
                _showError = GUILayout.Toggle(_showError, "Error", EditorStyles.toolbarButton);
                StickyNotesSettings.ShowResolved = GUILayout.Toggle(StickyNotesSettings.ShowResolved, "Resolved", EditorStyles.toolbarButton);
                GUILayout.Space(6);
                _search = GUILayout.TextField(_search ?? "", EditorStyles.toolbarSearchField, GUILayout.MinWidth(80));
                GUILayout.FlexibleSpace();
                bool show = GUILayout.Toggle(StickyNotesSettings.ShowInSceneView, new GUIContent("Visible", "Show notes in the Scene view"), EditorStyles.toolbarButton);
                if (show != StickyNotesSettings.ShowInSceneView) StickyNotesSettings.ShowInSceneView = show;
                if (GUILayout.Button(new GUIContent("Add", "Click in the Scene view where the note goes"), EditorStyles.toolbarButton))
                {
                    StickyNotesSceneGUI.Placing = true;
                    SceneView.lastActiveSceneView?.Focus();
                }
            }
        }

        private void DrawStatus()
        {
            string live = StickyNoteStore.IsConnected
                ? $"Live with {CollabSession.Peers?.Count(p => p != null && !p.IsLocal) ?? 0} others"
                : "Not in a session (local only)";
            string save = StickyNotesEditorService.IsSoleEditor
                ? "you save the notes files"
                : "the room holds the notes (the last editor to leave saves them)";
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"{live} · {save}", EditorStyles.miniLabel);
                int pending = StickyNotesEditorService.UncommittedScenes.Count;
                using (new EditorGUI.DisabledScope(pending == 0 && StickyNotesEditorService.UnsavedScenes.Count == 0 || !StickyNotesEditorService.IsSoleEditor))
                {
                    if (GUILayout.Button(new GUIContent(pending > 0 ? $"Commit ({pending})" : "Commit", "Write and commit your notes files now (Collab git)"),
                            EditorStyles.miniButton, GUILayout.Width(80)))
                        StickyNotesEditorService.CommitNow();
                }
            }
        }

        private SortedDictionary<string, List<StickyNote>> Collect()
        {
            var scenes = new List<string>();
            if (_sceneFilter == SceneFilter.OpenScenes)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!string.IsNullOrEmpty(s.path)) scenes.Add(s.path);
                }
            }
            else scenes.AddRange(StickyNoteStore.Scenes);

            var groups = new SortedDictionary<string, List<StickyNote>>(StringComparer.Ordinal);
            foreach (var scene in scenes.Distinct())
            {
                var notes = StickyNoteStore.GetNotes(scene).Where(Matches).ToList();
                if (notes.Count == 0) continue;
                // Open first, errors before warnings before remarks, newest first.
                notes.Sort((a, b) =>
                {
                    int c = a.Resolved.CompareTo(b.Resolved);
                    if (c != 0) return c;
                    c = b.NoteType.CompareTo(a.NoteType);
                    return c != 0 ? c : string.CompareOrdinal(b.Updated, a.Updated);
                });
                groups[scene] = notes;
            }
            return groups;
        }

        private bool Matches(StickyNote n)
        {
            if (n.Resolved && !StickyNotesSettings.ShowResolved) return false;
            switch (n.NoteType)
            {
                case StickyNoteType.Default when !_showDefault: return false;
                case StickyNoteType.Warning when !_showWarning: return false;
                case StickyNoteType.Error when !_showError: return false;
            }
            if (string.IsNullOrEmpty(_search)) return true;
            return (n.Text ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || (n.Author ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || (n.AttachedName ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawRow(StickyNote note)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20);
            bool selected = note.Id == _selectedId;
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (selected) EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 4, 12, 12), StickyNoteStyle.ColorOf(note));
                EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 14, 12, 2), StickyNoteStyle.ParseColor(note.AuthorColor, Color.white));
            }
            var when = StickyNote.ParseTime(note.Updated);
            string right = $"{note.Author} · {Ago(when)}";
            float rightWidth = Mathf.Min(170, rect.width * 0.4f);
            GUI.Label(new Rect(rect.x + 20, rect.y, rect.width - 24 - rightWidth, rect.height),
                (note.Resolved ? "<color=#888888>" : "") + StickyNoteStyle.Summary(note, 80) + (note.Resolved ? "</color>" : ""), _rowStyle);
            GUI.Label(new Rect(rect.xMax - rightWidth, rect.y, rightWidth, rect.height), right, EditorStyles.miniLabel);

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                _selectedId = note.Id;
                if (e.button == 1) ShowMenu(note);
                else if (e.clickCount == 2) Frame(note);
                e.Use();
                Repaint();
            }
        }

        private void DrawPreview(StickyNote note)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{note.NoteType}{(note.Resolved ? " · resolved" : "")} · {note.Author}", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Frame", EditorStyles.miniButtonLeft)) Frame(note);
                    if (GUILayout.Button("Edit", EditorStyles.miniButtonMid)) StickyNoteEditWindow.Edit(note, StickyNoteEditWindow.AtMouse());
                    if (GUILayout.Button(note.Resolved ? "Reopen" : "Resolve", EditorStyles.miniButtonRight))
                        StickyNoteStore.SetResolved(note.Id, !note.Resolved);
                }
                GUILayout.Label(note.Text, _previewStyle);
                if (!string.IsNullOrEmpty(note.AttachedName)) GUILayout.Label($"On {note.AttachedName}", EditorStyles.miniLabel);
            }
        }

        private void ShowMenu(StickyNote note)
        {
            var screen = StickyNoteEditWindow.AtMouse();
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Frame in Scene View"), false, () => Frame(note));
            menu.AddItem(new GUIContent("Edit…"), false, () => StickyNoteEditWindow.Edit(note, screen));
            menu.AddItem(new GUIContent(note.Resolved ? "Reopen" : "Resolve"), false, () => StickyNoteStore.SetResolved(note.Id, !note.Resolved));
            if (!string.IsNullOrEmpty(note.AttachedId))
                menu.AddItem(new GUIContent("Select Attached Object"), false, () => StickyNoteEditWindow.SelectAttached(note));
            menu.AddItem(new GUIContent("Copy Text"), false, () => EditorGUIUtility.systemCopyBuffer = note.Text);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Delete Sticky Note", $"Delete \"{StickyNoteStyle.Summary(note)}\" for everyone?", "Delete", "Cancel"))
                    StickyNoteStore.Delete(note.Id);
            });
            menu.ShowAsContext();
        }

        /// <summary>Frames a note in the Scene view, opening its scene additively first if it isn't open.</summary>
        public static void Frame(StickyNote note)
        {
            if (note == null) return;
            var scene = SceneManager.GetSceneByPath(note.Scene);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.Log($"[Sticky Notes] {note.Scene} isn't loaded in Play Mode.");
                    return;
                }
                if (!File.Exists(note.Scene)) { Debug.LogWarning($"[Sticky Notes] Scene {note.Scene} doesn't exist."); return; }
                if (!EditorUtility.DisplayDialog("Sticky Notes", $"Open {Path.GetFileNameWithoutExtension(note.Scene)} (additively) to see this note?", "Open", "Cancel"))
                    return;
                EditorSceneManager.OpenScene(note.Scene, OpenSceneMode.Additive);
            }
            var view = SceneView.lastActiveSceneView ?? GetWindow<SceneView>();
            if (view == null) return;
            StickyNotesSettings.ShowInSceneView = true;
            view.Frame(new Bounds(note.Position, Vector3.one * 4f), false);
            view.Focus();
            StickyNoteEditWindow.SelectAttached(note);
        }

        private static string Ago(DateTime local)
        {
            if (local == DateTime.MinValue) return "";
            var span = DateTime.Now - local;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
            return local.ToString("d");
        }

        private void EnsureStyles()
        {
            if (_rowStyle != null) return;
            _rowStyle = new GUIStyle(EditorStyles.label) { richText = true, clipping = TextClipping.Clip };
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { margin = new RectOffset(4, 4, 6, 2) };
            _previewStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = false };
        }
    }
}
