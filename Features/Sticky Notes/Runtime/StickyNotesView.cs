using System;
using System.Collections.Generic;
using Remedy.Box3DPhysics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// The in-game sticky note view. Hidden until someone turns it on (<see cref="ToggleKey"/>, F8 by default, or
    /// <see cref="Enabled"/> from code); then every note in the loaded scenes shows as a small piece of sticky-note paper
    /// where it was stuck (the paper image, tinted: yellow Default, orange Warning, red Error, green once resolved, with a
    /// type badge in the corner; a coloured square when there's no image). Hover one to read it; click it and it scales
    /// up (about 0.15 s) into the editor panel, which is laid out on the paper itself. Press <see cref="AddKey"/> (F9) or
    /// "Add Note" to stick a new one where the camera is looking, and "Tasks" for a small list of open notes and who's on
    /// them.
    ///
    /// It draws with IMGUI on purpose: it needs no assets, theme or package, works with either input backend and in any
    /// build, and the panel is small. A hidden object (never saved, survives scene loads) hosts it.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class StickyNotesView : MonoBehaviour
    {
        #region Public API

        /// <summary>Turns the view on and off with a key. KeyCode.None disables the key.</summary>
        public static KeyCode ToggleKey = KeyCode.F8;

        /// <summary>Sticks a new note where the camera looks (while the view is on). KeyCode.None disables the key.</summary>
        public static KeyCode AddKey = KeyCode.F9;

        /// <summary>
        /// Whether the view can be turned on at all. By default: in the editor, in development builds, and in builds the
        /// project opted in (Project Settings &gt; Schematics &gt; Sticky Notes). Set it from game code to override.
        /// </summary>
        public static bool Available
        {
            get => _available ?? (Application.isEditor || Debug.isDebugBuild || StickyNotesSceneData.AnyEmbedded);
            set => _available = value;
        }
        private static bool? _available;

        /// <summary>Raised with the new state when the view is turned on or off.</summary>
        public static event Action<bool> EnabledChanged;

        /// <summary>The view is on (notes are visible).</summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (value && !Available) value = false;
                if (_enabled == value) return;
                _enabled = value;
                if (value) EnsureHost();
                _host?.OnEnabledChanged(value);
                try { EnabledChanged?.Invoke(value); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
        private static bool _enabled;

        public static void Toggle() => Enabled = !Enabled;

        /// <summary>The camera notes are drawn for and placed from. Null uses Camera.main.</summary>
        public static Camera TargetCamera;

        /// <summary>Show resolved notes too.</summary>
        public static bool ShowResolved = true;

        /// <summary>Notes further away than this (metres) aren't drawn.</summary>
        public static float MaxDistance = 250f;

        /// <summary>Frees the mouse cursor while the view is on (and puts it back after), so notes can be clicked.</summary>
        public static bool UnlockCursor = true;

        /// <summary>
        /// True while the note editor panel is open, so game code can ignore gameplay input (typing "w" in a note
        /// shouldn't walk the player forward).
        /// </summary>
        public static bool IsEditing => _host != null && _host._draft != null;

        /// <summary>Finds where a note goes for a ray from the camera. Replace it to use your own physics or ignore the player.</summary>
        public delegate bool PlacementRaycast(Ray ray, out Vector3 point, out Vector3 normal, out GameObject hitObject);

        /// <summary>The placement ray. Defaults to <see cref="SchematicPhysics"/> (Box3D), ignoring hits closer than half a metre.</summary>
        public static PlacementRaycast Raycaster = DefaultRaycast;

        /// <summary>
        /// Gives a stable id for the object a new note is stuck on (e.g. the runtime collaboration id), stored in
        /// <see cref="StickyNote.AttachedId"/>. Defaults to the collaboration id ("g:<GlobalObjectId>", the same id
        /// editor notes store); null stores only the object's name.
        /// </summary>
        public static Func<GameObject, string> ObjectIdProvider = Remedy.Schematics.Collaboration.CollabRuntime.GetObjectId;

        /// <summary>Starts a new note where the camera looks and opens it in the editor panel (not saved until Save).</summary>
        public static void AddNoteAtView()
        {
            if (!Available) return;
            Enabled = true;
            _host?.BeginNewNote();
        }

        /// <summary>Opens a note in the editor panel.</summary>
        public static void Open(StickyNote note)
        {
            if (note == null || !Available) return;
            Enabled = true;
            _host?.BeginEdit(note, default);
        }

        /// <summary>Creates the hidden host object if it isn't there yet (done automatically when the game starts).</summary>
        public static void EnsureHost()
        {
            if (_host != null || !Application.isPlaying) return;
            var go = new GameObject("Sticky Notes View") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _host = go.AddComponent<StickyNotesView>();
        }

        #endregion

        /// <summary>Shows the small list of open notes (To Do and In Progress) under the toolbar.</summary>
        public static bool ShowTaskList;

        /// <summary>Default for the "Attach performance snapshot" box on new notes (warnings and errors may get one anyway).</summary>
        public static bool AttachSnapshotByDefault;

        private static StickyNotesView _host;

        private const float PanelWidth = 380f;

        private StickyNote _draft;          // the note being edited (a copy), or a new one not saved yet
        private bool _draftIsNew;
        private bool _confirmDelete;
        private bool _attachPerf;
        private string _draftPerf;          // snapshot taken when a new note was started
        private bool _showPerfDetails;
        private string _hoverId;
        private Vector2 _scroll, _taskScroll;
        private Rect _panelRect = new Rect(0, 0, PanelWidth, PanelWidth);
        private bool _panelPlaced;
        private Rect _openFrom;             // where the open animation starts (the note's icon)
        private float _openStart = -10f;
        private bool _draggingPanel;
        private Vector2 _dragOffset;
        private float _scale = 1f;
        private float _nextFlush;
        private string _status;
        private float _statusUntil;

        private CursorLockMode _savedLock;
        private bool _savedVisible;
        private bool _cursorSaved;

        private static Texture2D _white;
        private GUIStyle _glyphStyle, _labelStyle, _boxStyle, _paperLabel, _paperSmall, _paperToggle, _paperTitle;

        private readonly List<(StickyNote note, Rect rect, float depth)> _visible = new List<(StickyNote, Rect, float)>();

        private void OnDestroy()
        {
            if (_host == this) _host = null;
            RestoreCursor();
        }

        private void Update()
        {
            StickyNotePerf.Tick();
            if (!Application.isEditor && Time.unscaledTime > _nextFlush)
            {
                _nextFlush = Time.unscaledTime + 5f;
                StickyNotesRuntime.FlushLocalCopies();
            }
            if (_enabled && UnlockCursor && _cursorSaved && Cursor.lockState != CursorLockMode.None)
            {
                // The game locked it again; free it while the view is on.
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnEnabledChanged(bool on)
        {
            if (on)
            {
                if (UnlockCursor && !_cursorSaved)
                {
                    _savedLock = Cursor.lockState;
                    _savedVisible = Cursor.visible;
                    _cursorSaved = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
            else
            {
                _draft = null;
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            if (!_cursorSaved) return;
            _cursorSaved = false;
            Cursor.lockState = _savedLock;
            Cursor.visible = _savedVisible;
        }

        #region Drawing

        private void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode == ToggleKey) { Toggle(); e.Use(); return; }
                if (_enabled && e.keyCode == AddKey) { BeginNewNote(); e.Use(); return; }
                if (_enabled && _draft != null && e.keyCode == KeyCode.Escape) { _draft = null; e.Use(); return; }
            }
            if (!_enabled) return;

            EnsureStyles();
            _scale = Mathf.Max(1f, Screen.height / 1080f);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            try
            {
                DrawToolbar();
                DrawIcons();
                if (ShowTaskList) DrawTaskList();
                if (_draft != null) DrawPanel();
            }
            finally { GUI.matrix = oldMatrix; }
        }

        private Camera ViewCamera => TargetCamera != null ? TargetCamera : Camera.main;

        private void DrawToolbar()
        {
            var r = new Rect(10, 10, 520, 30);
            Fill(r, new Color(0f, 0f, 0f, 0.65f));
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 4, r.width - 12, r.height - 8));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Sticky Notes ({ToggleKey})", _labelStyle, GUILayout.Width(150));
            if (GUILayout.Button($"Add Note ({AddKey})", GUILayout.Width(120))) BeginNewNote();
            int open = 0;
            foreach (var n in LoadedNotes()) if (!n.Resolved) open++;
            ShowTaskList = GUILayout.Toggle(ShowTaskList, $"Tasks ({open})", GUI.skin.button, GUILayout.Width(80));
            ShowResolved = GUILayout.Toggle(ShowResolved, "Resolved", GUILayout.Width(80));
            GUILayout.Label(StickyNoteStore.IsConnected ? "● live" : "○ local", _labelStyle);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
            {
                var s = new Rect(10, 44, 520, 22);
                Fill(s, new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(new Rect(s.x + 6, s.y + 2, s.width - 12, s.height - 4), _status, _labelStyle);
            }
        }

        private static IEnumerable<StickyNote> LoadedNotes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                foreach (var note in StickyNoteStore.GetNotes(scene.path)) yield return note;
            }
        }

        private void DrawIcons()
        {
            var cam = ViewCamera;
            if (cam == null) return;
            var e = Event.current;
            Vector2 mouse = e.mousePosition;
            float aspect = StickyNoteStyle.PaperAspect;

            _visible.Clear();
            foreach (var note in LoadedNotes())
            {
                if (note.Resolved && !ShowResolved) continue;
                var world = note.Position + note.Normal * 0.05f;
                var sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0.05f || sp.z > MaxDistance) continue;
                // Screen pixels (origin bottom-left) to GUI units (origin top-left, scaled).
                var gp = new Vector2(sp.x, Screen.height - sp.y) / _scale;
                float size = Mathf.Lerp(30f, 16f, Mathf.InverseLerp(5f, MaxDistance, sp.z));
                float w = StickyNoteStyle.PaperTexture != null ? size * aspect : size;
                _visible.Add((note, new Rect(gp.x - w * 0.5f, gp.y - size * 0.5f, w, size), sp.z));
            }
            // Far ones first so near ones draw on top; hover picks the nearest under the mouse.
            _visible.Sort((a, b) => b.depth.CompareTo(a.depth));

            string hover = null;
            for (int i = _visible.Count - 1; i >= 0; i--)
                if (_visible[i].rect.Contains(mouse)) { hover = _visible[i].note.Id; break; }
            if (_draft != null && _panelRect.Contains(mouse)) hover = null;
            if (ShowTaskList && TaskListRect().Contains(mouse)) hover = null;
            _hoverId = hover;

            foreach (var (note, rect, _) in _visible)
            {
                if (_draft != null && _draft.Id == note.Id && OpenProgress < 1f) continue;   // it's flying into the panel
                bool selected = _draft != null && _draft.Id == note.Id;
                DrawNoteIcon(note, rect, selected || note.Id == _hoverId);
                if (note.Id == _hoverId) DrawTooltip(note, rect);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && _hoverId != null)
            {
                var note = StickyNoteStore.Find(_hoverId);
                if (note != null)
                {
                    Rect from = default;
                    foreach (var v in _visible) if (v.note.Id == note.Id) from = v.rect;
                    BeginEdit(note, from);
                }
                e.Use();
            }
        }

        /// <summary>One note as a small piece of paper, tinted for its type, with a type badge and the author's stripe.</summary>
        private void DrawNoteIcon(StickyNote note, Rect rect, bool highlighted)
        {
            if (StickyNoteStyle.PaperTexture != null)
            {
                if (highlighted) Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(1f, 1f, 1f, 0.55f));
                StickyNoteStyle.DrawPaper(rect, StickyNoteStyle.PaperTint(note));
                // Type badge in the corner, and the author's colour as a thin stripe along the bottom.
                float b = Mathf.Max(7f, rect.height * 0.36f);
                var badge = new Rect(rect.xMax - b * 0.8f, rect.y - b * 0.2f, b, b);
                Fill(new Rect(badge.x - 1, badge.y - 1, badge.width + 2, badge.height + 2), new Color(0f, 0f, 0f, 0.8f));
                Fill(badge, StickyNoteStyle.ColorOf(note));
                var old = _glyphStyle.fontSize;
                _glyphStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(b * 0.8f, 7f, 12f));
                GUI.Label(badge, StickyNoteStyle.GlyphOf(note), _glyphStyle);
                _glyphStyle.fontSize = old;
                Fill(new Rect(rect.x + rect.width * 0.08f, rect.yMax - rect.height * 0.12f, rect.width * 0.6f, 2f), StickyNoteStyle.ParseColor(note.AuthorColor, Color.white));
                return;
            }
            // No image: the plain coloured square.
            Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), highlighted ? Color.white : new Color(0f, 0f, 0f, 0.8f));
            Fill(rect, StickyNoteStyle.ColorOf(note));
            Fill(new Rect(rect.x, rect.yMax - 3, rect.width, 3), StickyNoteStyle.ParseColor(note.AuthorColor, Color.white));
            GUI.Label(rect, StickyNoteStyle.GlyphOf(note), _glyphStyle);
        }

        private void DrawTooltip(StickyNote note, Rect icon)
        {
            string extra = "";
            if (note.Status == StickyNoteStatus.InProgress) extra += $"\n<i>In progress: {note.Assignee}</i>";
            if (note.HasPerf) extra += $"\n<size=10>{StickyNotePerf.Summary(note.Perf)}</size>";
            string text = $"<b>{note.NoteType}</b>{(note.Resolved ? " (resolved)" : "")} — {note.Author}\n{Truncate(note.Text, 300)}{extra}";
            var content = new GUIContent(text);
            float width = 280f;
            float height = _boxStyle.CalcHeight(content, width);
            var r = new Rect(icon.xMax + 6, icon.y, width, height);
            if (r.xMax > Screen.width / _scale) r.x = icon.x - width - 6;
            GUI.Box(r, content, _boxStyle);
        }

        private Rect TaskListRect() => new Rect(10, 70, 340, 260);

        // A small read-only list of open notes for playtesters: what's still to do, and who's on what.
        private void DrawTaskList()
        {
            var r = TaskListRect();
            Fill(r, new Color(0f, 0f, 0f, 0.72f));
            var open = new List<StickyNote>();
            foreach (var n in LoadedNotes()) if (!n.Resolved) open.Add(n);
            open.Sort((a, b) =>
            {
                int c = a.Status.CompareTo(b.Status);   // To Do first, then In Progress
                return c != 0 ? c : StickyNoteStyle.CompareByPriority(a, b);
            });

            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 4, r.width - 12, r.height - 8));
            GUILayout.Label($"<b>Open notes</b> ({open.Count})", _labelStyle);
            _taskScroll = GUILayout.BeginScrollView(_taskScroll);
            StickyNote clicked = null;
            if (open.Count == 0) GUILayout.Label("Nothing open in this level.", _labelStyle);
            foreach (var n in open)
            {
                var row = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
                Fill(new Rect(row.x, row.y + 4, 12, 12), StickyNoteStyle.ColorOf(n));
                string who = n.Status == StickyNoteStatus.InProgress ? $"<color=#9fd0ff>{n.Assignee}</color>" : "<color=#bbbbbb>to do</color>";
                GUI.Label(new Rect(row.x + 18, row.y, row.width - 110, row.height), StickyNoteStyle.Summary(n, 40), _labelStyle);
                GUI.Label(new Rect(row.xMax - 90, row.y, 90, row.height), who, _labelStyle);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) { clicked = n; Event.current.Use(); }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (clicked != null) BeginEdit(clicked, new Rect(r.x + 18, r.y + 30, 20, 20));
        }

        private float OpenProgress => StickyNoteStyle.OpenAnimationSeconds <= 0f ? 1f
            : Mathf.Clamp01((Time.unscaledTime - _openStart) / StickyNoteStyle.OpenAnimationSeconds);

        private void DrawPanel()
        {
            float height = StickyNoteStyle.PaperTexture != null ? PanelWidth / StickyNoteStyle.PaperAspect : 330f;
            if (!_panelPlaced)
            {
                _panelRect = new Rect(Screen.width / _scale - PanelWidth - 16, 60, PanelWidth, height);
                _panelPlaced = true;
            }
            _panelRect.height = height;

            // The scale-up from the note's icon into the panel.
            float t = StickyNoteStyle.EaseOut(OpenProgress);
            if (t < 1f)
            {
                var from = _openFrom.width > 0 ? _openFrom : new Rect(_panelRect.center.x - 10, _panelRect.center.y - 10, 20, 20);
                var r = new Rect(Vector2.Lerp(from.position, _panelRect.position, t), Vector2.Lerp(from.size, _panelRect.size, t));
                if (!StickyNoteStyle.DrawPaper(r, StickyNoteStyle.PaperTint(_draft))) Fill(r, StickyNoteStyle.ColorOf(_draft));
                return;
            }

            HandlePanelDrag();
            bool paper = StickyNoteStyle.DrawPaper(_panelRect, StickyNoteStyle.PaperTint(_draft));
            Rect content;
            if (paper) content = StickyNoteStyle.PaperContentRect(_panelRect);
            else
            {
                Fill(_panelRect, new Color(0.13f, 0.13f, 0.13f, 0.95f));
                Fill(new Rect(_panelRect.x, _panelRect.y, _panelRect.width, 5), StickyNoteStyle.ColorOf(_draft));
                content = new Rect(_panelRect.x + 10, _panelRect.y + 12, _panelRect.width - 20, _panelRect.height - 20);
            }
            // Clicks on the panel never reach the game or the icons behind it.
            GUILayout.BeginArea(content);
            DrawPanelContents(paper);
            GUILayout.EndArea();
            if (Event.current.type == EventType.MouseDown && _panelRect.Contains(Event.current.mousePosition)) Event.current.Use();
        }

        // Drag the panel by its top margin (the strip above the content).
        private void HandlePanelDrag()
        {
            var e = Event.current;
            var grip = new Rect(_panelRect.x, _panelRect.y, _panelRect.width, Mathf.Max(14f, _panelRect.height * StickyNoteStyle.PaperInsets.y));
            if (e.type == EventType.MouseDown && e.button == 0 && grip.Contains(e.mousePosition))
            {
                _draggingPanel = true;
                _dragOffset = e.mousePosition - _panelRect.position;
                e.Use();
            }
            else if (_draggingPanel && e.type == EventType.MouseDrag)
            {
                _panelRect.position = e.mousePosition - _dragOffset;
                e.Use();
            }
            else if (_draggingPanel && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)) _draggingPanel = false;
        }

        private void DrawPanelContents(bool onPaper)
        {
            var note = _draft;
            if (note == null) return;
            var label = onPaper ? _paperLabel : _labelStyle;
            var small = onPaper ? _paperSmall : _labelStyle;

            GUILayout.BeginHorizontal();
            string title = _draftIsNew ? "New note" : note.Status == StickyNoteStatus.Done ? "Done" : note.Status == StickyNoteStatus.InProgress ? $"In progress · {note.Assignee}" : "To do";
            GUILayout.Label(title, onPaper ? _paperTitle : label, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            int type = GUILayout.Toolbar((int)note.NoteType, new[] { "Default", "Warning", "Error" });
            if (type != (int)note.NoteType) note.NoteType = (StickyNoteType)type;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUI.SetNextControlName("StickyNoteText");
            note.Text = GUILayout.TextArea(note.Text ?? "", GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            if (!_draftIsNew)
            {
                var created = StickyNote.ParseTime(note.Created);
                GUILayout.Label($"{note.Author}{(created != DateTime.MinValue ? " · " + created.ToString("g") : "")}" +
                                (string.IsNullOrEmpty(note.AttachedName) ? "" : $" · on {note.AttachedName}"), small);
                if (note.Resolved && !string.IsNullOrEmpty(note.ResolvedBy)) GUILayout.Label($"Resolved by {note.ResolvedBy}", small);
                if (note.HasPerf)
                {
                    _showPerfDetails = GUILayout.Toggle(_showPerfDetails, "Snapshot: " + StickyNotePerf.Summary(note.Perf), onPaper ? _paperToggle : GUI.skin.toggle);
                    if (_showPerfDetails) GUILayout.Label(StickyNotePerf.Details(note.Perf), small);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(note.AttachedName)) GUILayout.Label($"On {note.AttachedName}", small);
                bool auto = StickyNotePerf.ShouldAttach(note.NoteType, false);
                using (new GUIEnabledScope(!auto))
                {
                    bool v = GUILayout.Toggle(_attachPerf || auto, auto ? "Attach performance snapshot (automatic)" : "Attach performance snapshot",
                        onPaper ? _paperToggle : GUI.skin.toggle);
                    if (!auto) _attachPerf = v;
                }
            }

            // Buttons only record what to do; it's done after the layout group closes so IMGUI's layout stays balanced.
            Action action = null;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_draftIsNew ? "Add" : "Save")) action = () => SaveDraft();
            if (!_draftIsNew)
            {
                if (GUILayout.Button(note.Resolved ? "Reopen" : "Resolve"))
                    action = () =>
                    {
                        SaveDraft(keepOpen: true);
                        StickyNoteStore.SetResolved(note.Id, !note.Resolved);
                        _draft = StickyNoteStore.Find(note.Id)?.Clone();
                    };
                if (GUILayout.Button(_confirmDelete ? "Really?" : "Delete"))
                    action = () =>
                    {
                        if (_confirmDelete) { StickyNoteStore.Delete(note.Id); _draft = null; }
                        else _confirmDelete = true;
                    };
            }
            if (GUILayout.Button("Cancel")) action = () => _draft = null;
            GUILayout.EndHorizontal();

            if (action != null) action();
        }

        private readonly struct GUIEnabledScope : IDisposable
        {
            private readonly bool _old;
            public GUIEnabledScope(bool enabled) { _old = GUI.enabled; GUI.enabled = _old && enabled; }
            public void Dispose() => GUI.enabled = _old;
        }

        #endregion

        #region Editing

        private void BeginEdit(StickyNote note, Rect from = default)
        {
            _draft = note.Clone();
            _draftIsNew = false;
            _confirmDelete = false;
            _showPerfDetails = false;
            _scroll = Vector2.zero;
            StartOpenAnimation(from);
        }

        private void StartOpenAnimation(Rect from)
        {
            _openFrom = from;
            _openStart = Time.unscaledTime;
        }

        private void BeginNewNote()
        {
            var cam = ViewCamera;
            if (cam == null) { Status("No camera to place the note from."); return; }

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            Vector3 point = default, normal = default;
            GameObject hitObject = null;
            bool hit = false;
            try { hit = Raycaster != null && Raycaster(ray, out point, out normal, out hitObject); }
            catch (Exception ex) { Debug.LogException(ex); }
            if (!hit)
            {
                point = ray.GetPoint(3f);
                normal = -ray.direction;
            }

            var scene = hitObject != null && !string.IsNullOrEmpty(hitObject.scene.path) ? hitObject.scene : SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path)) { Status("This scene isn't saved, so notes can't be stuck in it."); return; }

            string attachedId = "";
            if (hitObject != null && ObjectIdProvider != null)
            {
                try { attachedId = ObjectIdProvider(hitObject) ?? ""; }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            _draft = new StickyNote
            {
                Id = Guid.NewGuid().ToString("N"),
                Scene = scene.path,
                Position = point,
                Normal = normal,
                AttachedId = attachedId,
                AttachedName = hitObject != null ? hitObject.name : "",
            };
            // The snapshot is taken now, before the tester spends a while typing (and the panel itself costs frames).
            try { _draftPerf = StickyNotePerf.Capture(scene.path, point, cam); }
            catch (Exception ex) { Debug.LogException(ex); _draftPerf = null; }
            _attachPerf = AttachSnapshotByDefault;
            _draftIsNew = true;
            _confirmDelete = false;
            _scroll = Vector2.zero;
            // Grow from where the note goes (the middle of the screen when it's off screen).
            var sp = cam.WorldToScreenPoint(point);
            var gp = sp.z > 0 ? new Vector2(sp.x, Screen.height - sp.y) / _scale : new Vector2(Screen.width, Screen.height) * 0.5f / _scale;
            StartOpenAnimation(new Rect(gp.x - 10, gp.y - 10, 20, 20));
            GUI.FocusControl("StickyNoteText");
        }

        private void SaveDraft(bool keepOpen = false)
        {
            var d = _draft;
            if (d == null) return;
            if (_draftIsNew)
            {
                StickyNotePerf.AttachIfWanted(d, _attachPerf, _draftPerf, ViewCamera);
                var added = StickyNoteStore.Add(d.Scene, d.Position, d.Normal, d.NoteType, d.Text, d.AttachedId, d.AttachedName, d.Perf, d.PerfCapture, d.Id);
                Status(StickyNoteStore.IsConnected ? "Note added and shared." : "Note added (not connected: kept locally).");
                _draft = keepOpen ? added.Clone() : null;
                _draftIsNew = false;
                _draftPerf = null;
                return;
            }
            var current = StickyNoteStore.Find(d.Id);
            if (current == null) { _draft = null; Status("That note was deleted."); return; }
            var copy = current.Clone();
            copy.Text = d.Text;
            copy.Type = d.Type;
            StickyNoteStore.Update(copy);
            if (!keepOpen) _draft = null;
        }

        private void Status(string text)
        {
            _status = text;
            _statusUntil = Time.unscaledTime + 3f;
        }

        private static bool DefaultRaycast(Ray ray, out Vector3 point, out Vector3 normal, out GameObject hitObject)
        {
            point = default;
            normal = default;
            hitObject = null;
            var origin = ray.origin;
            float remaining = 500f;
            // Skip hits right at the camera (usually the player's own body in third person), at most twice.
            for (int attempt = 0; attempt < 3 && remaining > 0f; attempt++)
            {
                if (!SchematicPhysics.SphereCast(origin, 0f, ray.direction, out var hit, out var go, remaining, ~0))
                    return false;
                float fromCamera = Vector3.Distance(ray.origin, hit.point);
                if (fromCamera >= 0.5f)
                {
                    point = hit.point;
                    normal = hit.normal;
                    hitObject = go;
                    return true;
                }
                origin = hit.point + ray.direction * 0.05f;
                remaining = 500f - fromCamera - 0.05f;
            }
            return false;
        }

        #endregion

        #region Helpers

        private void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (_glyphStyle != null) return;
            _glyphStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13, padding = new RectOffset(0, 0, 0, 0) };
            _glyphStyle.normal.textColor = new Color(0.1f, 0.1f, 0.1f);
            _labelStyle = new GUIStyle(GUI.skin.label) { richText = true };
            _labelStyle.normal.textColor = Color.white;
            _boxStyle = new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
            _boxStyle.normal.textColor = Color.white;

            // Dark "ink" for text written on the paper.
            var ink = new Color(0.12f, 0.10f, 0.05f);
            _paperLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            _paperLabel.normal.textColor = ink;
            _paperTitle = new GUIStyle(_paperLabel) { fontStyle = FontStyle.Bold, fontSize = 14 };
            _paperSmall = new GUIStyle(_paperLabel) { fontSize = 11 };
            _paperToggle = new GUIStyle(GUI.skin.toggle) { wordWrap = true, fontSize = 11 };
            _paperToggle.normal.textColor = _paperToggle.onNormal.textColor = _paperToggle.hover.textColor =
                _paperToggle.onHover.textColor = _paperToggle.active.textColor = _paperToggle.onActive.textColor = ink;
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white != null ? _white : Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static string Truncate(string s, int max)
        {
            s = s ?? "";
            return s.Length > max ? s.Substring(0, max - 1) + "…" : s;
        }

        #endregion
    }
}
