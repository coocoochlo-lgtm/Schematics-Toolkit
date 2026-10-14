using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// Sticky notes in the Scene view: each note is a small square in its type's colour (a ring marks the exact spot),
    /// hovering shows the text, clicking opens the note editor, right-clicking a note gives Edit/Resolve/Delete.
    /// Add notes with the overlay's "Add Note" (then click where it goes), Shift+right-click "Add Sticky Note here",
    /// or GameObject &gt; Sticky Note &gt; Add Sticky Note on Selection.
    /// </summary>
    [InitializeOnLoad]
    public static class StickyNotesSceneGUI
    {
        private static readonly int ControlHash = "Remedy.StickyNotes.SceneGUI".GetHashCode();
        private static readonly List<(StickyNote note, Rect rect, float depth)> _visible = new List<(StickyNote, Rect, float)>();
        private static string _hoverId;
        private static GUIStyle _glyph, _tooltip;

        /// <summary>Placement mode: the next left click in a Scene view sticks a new note there (Esc cancels).</summary>
        public static bool Placing
        {
            get => _placing;
            set { _placing = value; SceneView.RepaintAll(); }
        }
        private static bool _placing;

        static StickyNotesSceneGUI()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            StickyNoteStore.Changed += _ => SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            int id = GUIUtility.GetControlID(ControlHash, FocusType.Passive);

            if (_placing)
            {
                HandlePlacement(view, e, id);
                return;
            }
            if (!StickyNotesSettings.ShowInSceneView) return;

            CollectVisible();
            Vector2 mouse = e.mousePosition;
            string hover = null;
            for (int i = _visible.Count - 1; i >= 0; i--)
                if (_visible[i].rect.Contains(mouse)) { hover = _visible[i].note.Id; break; }
            if (hover != _hoverId && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)) view.Repaint();
            _hoverId = hover;

            switch (e.type)
            {
                case EventType.Layout:
                    // Claim the click when the mouse is over a note, so it doesn't select what's behind it.
                    if (_hoverId != null) HandleUtility.AddControl(id, 0f);
                    break;

                case EventType.MouseDown when e.button == 0 && _hoverId != null && HandleUtility.nearestControl == id && !e.alt:
                {
                    var note = StickyNoteStore.Find(_hoverId);
                    if (note != null) BeginOpen(view, note);
                    e.Use();
                    break;
                }

                case EventType.ContextClick when _hoverId != null:
                    ShowNoteMenu(StickyNoteStore.Find(_hoverId));
                    e.Use();
                    break;

                case EventType.ContextClick when e.shift:
                {
                    var guiPoint = e.mousePosition;
                    var screen = GUIUtility.GUIToScreenPoint(guiPoint);
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Add Sticky Note here"), false, () => AddAt(guiPoint, new Rect(screen.x, screen.y, 1, 1), view));
                    menu.ShowAsContext();
                    e.Use();
                    break;
                }

                case EventType.Repaint:
                    Draw();
                    break;
            }
        }

        #region Drawing

        private static void CollectVisible()
        {
            _visible.Clear();
            bool showResolved = StickyNotesSettings.ShowResolved;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                foreach (var note in StickyNoteStore.GetNotes(scene.path))
                {
                    if (note.Resolved && !showResolved) continue;
                    var p = HandleUtility.WorldToGUIPointWithDepth(note.Position + note.Normal * 0.05f);
                    if (p.z <= 0f) continue;
                    float size = Mathf.Lerp(26f, 14f, Mathf.InverseLerp(5f, 300f, p.z));
                    float w = StickyNoteStyle.PaperTexture != null ? size * StickyNoteStyle.PaperAspect : size;
                    _visible.Add((note, new Rect(p.x - w * 0.5f, p.y - size * 0.5f, w, size), p.z));
                }
            }
            _visible.Sort((a, b) => b.depth.CompareTo(a.depth));   // far first, so near ones draw on top
        }

        private static void Draw()
        {
            // The exact spot: a ring on the surface, in the note's colour.
            foreach (var (note, _, depth) in _visible)
            {
                if (depth > 80f) continue;
                using (new Handles.DrawingScope(StickyNoteStyle.ColorOf(note)))
                    Handles.DrawWireDisc(note.Position, note.Normal, HandleUtility.GetHandleSize(note.Position) * 0.08f);
            }

            EnsureStyles();
            Handles.BeginGUI();
            foreach (var (note, rect, _) in _visible)
            {
                if (_opening != null && _opening.NoteId == note.Id) continue;   // it's flying into the panel
                DrawIcon(note, rect, note.Id == _hoverId);
            }
            if (_hoverId != null)
            {
                foreach (var (note, rect, _) in _visible)
                {
                    if (note.Id != _hoverId) continue;
                    string extra = "";
                    if (note.Status == StickyNoteStatus.InProgress) extra += $"\n<i>In progress: {note.Assignee}</i>";
                    if (note.HasPerf) extra += $"\n<size=10>{StickyNotePerf.Summary(note.Perf)}</size>";
                    var content = new GUIContent($"<b>{note.NoteType}</b>{(note.Resolved ? " (resolved)" : "")} — {note.Author}\n{note.Text}{extra}");
                    float width = 260f;
                    float height = Mathf.Min(_tooltip.CalcHeight(content, width), 220f);
                    var r = new Rect(rect.xMax + 6, rect.y, width, height);
                    GUI.Box(r, content, _tooltip);
                    break;
                }
            }
            DrawOpening();
            Handles.EndGUI();
        }

        /// <summary>One note as a small piece of paper tinted for its type, with a type badge and the author's stripe.</summary>
        private static void DrawIcon(StickyNote note, Rect rect, bool hovered)
        {
            if (StickyNoteStyle.PaperTexture != null)
            {
                if (hovered) EditorGUI.DrawRect(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), new Color(1f, 1f, 1f, 0.5f));
                StickyNoteStyle.DrawPaper(rect, StickyNoteStyle.PaperTint(note));
                float b = Mathf.Max(7f, rect.height * 0.38f);
                var badge = new Rect(rect.xMax - b * 0.8f, rect.y - b * 0.2f, b, b);
                EditorGUI.DrawRect(new Rect(badge.x - 1f, badge.y - 1f, badge.width + 2f, badge.height + 2f), new Color(0, 0, 0, 0.8f));
                EditorGUI.DrawRect(badge, StickyNoteStyle.ColorOf(note));
                int old = _glyph.fontSize;
                _glyph.fontSize = Mathf.RoundToInt(Mathf.Clamp(b * 0.8f, 7f, 11f));
                GUI.Label(badge, StickyNoteStyle.GlyphOf(note), _glyph);
                _glyph.fontSize = old;
                EditorGUI.DrawRect(new Rect(rect.x + rect.width * 0.08f, rect.yMax - rect.height * 0.13f, rect.width * 0.6f, 2f),
                    StickyNoteStyle.ParseColor(note.AuthorColor, Color.white));
                return;
            }
            // No image: the plain coloured square.
            EditorGUI.DrawRect(new Rect(rect.x - 1.5f, rect.y - 1.5f, rect.width + 3f, rect.height + 3f), hovered ? Color.white : new Color(0, 0, 0, 0.8f));
            EditorGUI.DrawRect(rect, StickyNoteStyle.ColorOf(note));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2.5f, rect.width, 2.5f), StickyNoteStyle.ParseColor(note.AuthorColor, Color.white));
            GUI.Label(rect, StickyNoteStyle.GlyphOf(note), _glyph);
        }

        #region Opening animation

        // A note scaling up from its icon to where the editor panel will be, in the Scene view's GUI space.
        private sealed class Opening
        {
            public string NoteId;
            public SceneView View;
            public Rect From, To;
            public Rect ScreenTo;
            public double Start;
            public Color Tint;
        }
        private static Opening _opening;

        private static void BeginOpen(SceneView view, StickyNote note)
        {
            Rect from = default;
            foreach (var v in _visible) if (v.note.Id == note.Id) from = v.rect;
            if (from.width <= 0f) from = new Rect(Event.current.mousePosition, new Vector2(20, 20));
            var size = StickyNoteEditWindow.PanelSize;

            // Next to the icon, kept inside the Scene view.
            var area = new Rect(0, 0, view.position.width, view.position.height);
            var to = new Rect(from.xMax + 8f, from.center.y - size.y * 0.5f, size.x, size.y);
            if (to.xMax > area.xMax - 4f) to.x = from.x - size.x - 8f;
            to.x = Mathf.Clamp(to.x, 4f, Mathf.Max(4f, area.xMax - size.x - 4f));
            to.y = Mathf.Clamp(to.y, 4f, Mathf.Max(4f, area.yMax - size.y - 24f));

            var screenTo = GUIUtility.GUIToScreenRect(to);
            if (StickyNoteStyle.OpenAnimationSeconds <= 0f)
            {
                var n = note;
                EditorApplication.delayCall += () => StickyNoteEditWindow.Edit(n, new Rect(screenTo.x, screenTo.y - 1f, 1f, 1f), animate: false);
                return;
            }
            _opening = new Opening
            {
                NoteId = note.Id, View = view, From = from, To = to, ScreenTo = screenTo,
                Start = EditorApplication.timeSinceStartup, Tint = StickyNoteStyle.PaperTint(note),
            };
            EditorApplication.update -= TickOpening;
            EditorApplication.update += TickOpening;
            view.Repaint();
        }

        private static void TickOpening()
        {
            var o = _opening;
            if (o == null) { EditorApplication.update -= TickOpening; return; }
            if (o.View != null) o.View.Repaint();
            if (EditorApplication.timeSinceStartup - o.Start < StickyNoteStyle.OpenAnimationSeconds) return;

            EditorApplication.update -= TickOpening;
            _opening = null;
            var note = StickyNoteStore.Find(o.NoteId);
            // The window opens exactly where the paper ended up, so it looks like the same piece of paper.
            if (note != null) StickyNoteEditWindow.Edit(note, new Rect(o.ScreenTo.x, o.ScreenTo.y - 1f, 1f, 1f), animate: false);
            if (o.View != null) o.View.Repaint();
        }

        private static void DrawOpening()
        {
            var o = _opening;
            if (o == null || o.View != SceneView.currentDrawingSceneView) return;
            float t = StickyNoteStyle.EaseOut((float)((EditorApplication.timeSinceStartup - o.Start) / StickyNoteStyle.OpenAnimationSeconds));
            var r = new Rect(Vector2.Lerp(o.From.position, o.To.position, t), Vector2.Lerp(o.From.size, o.To.size, t));
            if (!StickyNoteStyle.DrawPaper(r, o.Tint))
            {
                var note = StickyNoteStore.Find(o.NoteId);
                EditorGUI.DrawRect(r, note != null ? StickyNoteStyle.ColorOf(note) : StickyNoteStyle.DefaultColor);
            }
        }

        #endregion

        private static void EnsureStyles()
        {
            if (_glyph != null) return;
            _glyph = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
            _glyph.normal.textColor = new Color(0.1f, 0.1f, 0.1f);
            _tooltip = new GUIStyle(EditorStyles.helpBox) { richText = true, wordWrap = true, fontSize = 11, padding = new RectOffset(8, 8, 6, 6) };
        }

        private static void ShowNoteMenu(StickyNote note)
        {
            if (note == null) return;
            var screen = StickyNoteEditWindow.AtMouse();
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Edit…"), false, () => StickyNoteEditWindow.Edit(note, screen));
            menu.AddItem(new GUIContent(note.Resolved ? "Reopen" : "Resolve"), false, () => StickyNoteStore.SetResolved(note.Id, !note.Resolved));
            if (!note.Resolved)
            {
                if (note.Status == StickyNoteStatus.InProgress && StickyNotesTaskReturn.IsMine(note))
                    menu.AddItem(new GUIContent("Give Back"), false, () => StickyNoteStore.GiveBack(note.Id));
                else
                    menu.AddItem(new GUIContent("Take"), false, () => StickyNoteStore.Claim(note.Id));
            }
            menu.AddItem(new GUIContent("Show on Kanban Board"), false, () => StickyNotesKanbanWindow.Open(note.Id));
            foreach (StickyNoteType type in System.Enum.GetValues(typeof(StickyNoteType)))
            {
                var t = type;
                menu.AddItem(new GUIContent("Type/" + t), note.NoteType == t, () =>
                {
                    var current = StickyNoteStore.Find(note.Id);
                    if (current == null) return;
                    var copy = current.Clone();
                    copy.NoteType = t;
                    StickyNoteStore.Update(copy);
                });
            }
            if (!string.IsNullOrEmpty(note.AttachedId))
                menu.AddItem(new GUIContent("Select Attached Object"), false, () => StickyNoteEditWindow.SelectAttached(note));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () => StickyNoteStore.Delete(note.Id));
            menu.ShowAsContext();
        }

        #endregion

        #region Adding

        private static void HandlePlacement(SceneView view, Event e, int id)
        {
            switch (e.type)
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;
                case EventType.MouseMove:
                    view.Repaint();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape:
                    Placing = false;
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt:
                {
                    var screen = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    AddAt(e.mousePosition, new Rect(screen.x, screen.y, 1, 1), view);
                    Placing = false;
                    e.Use();
                    break;
                }
                case EventType.Repaint:
                    if (Pick(e.mousePosition, out var point, out var normal, out _))
                    {
                        using (new Handles.DrawingScope(StickyNoteStyle.DefaultColor))
                        {
                            float size = HandleUtility.GetHandleSize(point) * 0.12f;
                            Handles.DrawWireDisc(point, normal, size);
                            Handles.DrawLine(point, point + normal * size * 2f);
                        }
                    }
                    Handles.BeginGUI();
                    GUI.Label(new Rect(e.mousePosition.x + 14, e.mousePosition.y + 10, 220, 20), "Click to stick a note (Esc cancels)", EditorStyles.whiteLabel);
                    Handles.EndGUI();
                    break;
            }
        }

        /// <summary>The surface under a Scene view GUI point (or the ground plane, or a point ahead), and the object there.</summary>
        public static bool Pick(Vector2 guiPoint, out Vector3 point, out Vector3 normal, out GameObject hitObject)
        {
            hitObject = HandleUtility.PickGameObject(guiPoint, false);
            if (HandleUtility.PlaceObject(guiPoint, out point, out normal)) return true;
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d) && d < 1000f)
            {
                point = ray.GetPoint(d);
                normal = Vector3.up;
                return true;
            }
            point = ray.GetPoint(10f);
            normal = -ray.direction;
            return true;
        }

        private static void AddAt(Vector2 guiPoint, Rect screenRect, SceneView view)
        {
            Pick(guiPoint, out var point, out var normal, out var hitObject);
            OpenNew(point, normal, hitObject, screenRect);
        }

        /// <summary>Starts a new note at a point (in the hit object's scene, else the active scene).</summary>
        public static void OpenNew(Vector3 point, Vector3 normal, GameObject attachTo, Rect screenRect)
        {
            var scene = attachTo != null && !string.IsNullOrEmpty(attachTo.scene.path) ? attachTo.scene : SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Sticky Notes", "Save the scene first: notes are stored next to the scene file.", "OK");
                return;
            }
            string attachedId = "";
            if (attachTo != null)
            {
                try { attachedId = GlobalObjectId.GetGlobalObjectIdSlow(attachTo).ToString(); }
                catch { }
            }
            StickyNotesEditorService.LoadScene(scene.path);
            EditorApplication.delayCall += () =>
                StickyNoteEditWindow.CreateNew(scene.path, point, normal, attachedId, attachTo != null ? attachTo.name : "", screenRect);
        }

        [MenuItem("GameObject/Sticky Note/Add Sticky Note on Selection", false, 49)]
        private static void AddOnSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            var point = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            var screen = EditorWindow.focusedWindow != null
                ? new Rect(EditorWindow.focusedWindow.position.center, Vector2.one)
                : new Rect(200, 200, 1, 1);
            OpenNew(point, Vector3.up, go, screen);
        }

        [MenuItem("GameObject/Sticky Note/Add Sticky Note on Selection", true)]
        private static bool AddOnSelectionValidate() => Selection.activeGameObject != null;

        [MenuItem("Tools/Schematics/Sticky Notes/Add Note at Scene View Center")]
        private static void AddAtViewCenter()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            var center = new Rect(view.position.center, Vector2.one);
            OpenNew(view.pivot, -view.camera.transform.forward, null, center);
        }

        [MenuItem("Tools/Schematics/Sticky Notes/Show Notes in Scene View")]
        private static void ToggleShow() => StickyNotesSettings.ShowInSceneView = !StickyNotesSettings.ShowInSceneView;

        [MenuItem("Tools/Schematics/Sticky Notes/Show Notes in Scene View", true)]
        private static bool ToggleShowValidate()
        {
            Menu.SetChecked("Tools/Schematics/Sticky Notes/Show Notes in Scene View", StickyNotesSettings.ShowInSceneView);
            return true;
        }

        #endregion
    }

    /// <summary>Scene view overlay: show/hide notes, show resolved, add a note, open the list.</summary>
    [Overlay(typeof(SceneView), "remedy-sticky-notes", "Sticky Notes", true)]
    public sealed class StickyNotesOverlay : Overlay
    {
        private Label _count;
        private Toggle _show, _resolved;
        private Button _add;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.minWidth = 160;

            _show = new Toggle("Show notes") { value = StickyNotesSettings.ShowInSceneView };
            _show.RegisterValueChangedCallback(e => StickyNotesSettings.ShowInSceneView = e.newValue);
            root.Add(_show);

            _resolved = new Toggle("Show resolved") { value = StickyNotesSettings.ShowResolved };
            _resolved.RegisterValueChangedCallback(e => StickyNotesSettings.ShowResolved = e.newValue);
            root.Add(_resolved);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            _add = new Button(() => StickyNotesSceneGUI.Placing = !StickyNotesSceneGUI.Placing) { text = "Add Note", tooltip = "Then click in the Scene view where the note goes (Esc cancels)." };
            _add.style.flexGrow = 1;
            row.Add(_add);
            var list = new Button(StickyNotesWindow.Open) { text = "List", tooltip = "Window > Schematics > Sticky Notes" };
            row.Add(list);
            var board = new Button(() => StickyNotesKanbanWindow.Open()) { text = "Board", tooltip = "The Kanban board: To Do, In Progress, Done" };
            row.Add(board);
            root.Add(row);

            _count = new Label();
            _count.style.unityFontStyleAndWeight = FontStyle.Italic;
            _count.style.marginTop = 2;
            root.Add(_count);

            root.schedule.Execute(Refresh).Every(500);
            Refresh();
            return root;
        }

        private void Refresh()
        {
            if (_count == null) return;
            int open = 0, warnings = 0, errors = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(scene.path)) continue;
                foreach (var n in StickyNoteStore.GetNotes(scene.path))
                {
                    if (n.Resolved) continue;
                    open++;
                    if (n.NoteType == StickyNoteType.Warning) warnings++;
                    if (n.NoteType == StickyNoteType.Error) errors++;
                }
            }
            _count.text = $"{open} open · {warnings} warnings · {errors} errors" + (StickyNoteStore.IsConnected ? " · live" : "");
            _show.SetValueWithoutNotify(StickyNotesSettings.ShowInSceneView);
            _resolved.SetValueWithoutNotify(StickyNotesSettings.ShowResolved);
            _add.text = StickyNotesSceneGUI.Placing ? "Cancel" : "Add Note";
        }
    }
}
