using System;
using System.Collections.Generic;
using Remedy.Box3DPhysics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// The in-game sticky note view. Hidden until someone turns it on (<see cref="ToggleKey"/>, F8 by default, or
    /// <see cref="Enabled"/> from code); then every note in the loaded scenes shows as a small coloured square where it was
    /// stuck (yellow Default, orange Warning, red Error, green once resolved). Hover one to read it, click it to edit, and
    /// press <see cref="AddKey"/> (F9) or "Add Note" to stick a new one where the camera is looking.
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
            _host?.BeginEdit(note);
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

        private static StickyNotesView _host;

        private StickyNote _draft;          // the note being edited (a copy), or a new one not saved yet
        private bool _draftIsNew;
        private bool _confirmDelete;
        private string _hoverId;
        private Vector2 _scroll;
        private Rect _panelRect = new Rect(0, 0, 340, 300);
        private bool _panelPlaced;
        private float _scale = 1f;
        private float _nextFlush;
        private string _status;
        private float _statusUntil;

        private CursorLockMode _savedLock;
        private bool _savedVisible;
        private bool _cursorSaved;

        private static Texture2D _white;
        private GUIStyle _glyphStyle, _labelStyle, _boxStyle;

        private readonly List<(StickyNote note, Rect rect, float depth)> _visible = new List<(StickyNote, Rect, float)>();

        private void OnDestroy()
        {
            if (_host == this) _host = null;
            RestoreCursor();
        }

        private void Update()
        {
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
                if (_draft != null) DrawPanel();
            }
            finally { GUI.matrix = oldMatrix; }
        }

        private Camera ViewCamera => TargetCamera != null ? TargetCamera : Camera.main;

        private void DrawToolbar()
        {
            var r = new Rect(10, 10, 430, 30);
            Fill(r, new Color(0f, 0f, 0f, 0.65f));
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 4, r.width - 12, r.height - 8));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Sticky Notes ({ToggleKey})", _labelStyle, GUILayout.Width(150));
            if (GUILayout.Button($"Add Note ({AddKey})", GUILayout.Width(120))) BeginNewNote();
            ShowResolved = GUILayout.Toggle(ShowResolved, "Resolved", GUILayout.Width(80));
            GUILayout.Label(StickyNoteStore.IsConnected ? "● live" : "○ local", _labelStyle);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
            {
                var s = new Rect(10, 44, 430, 22);
                Fill(s, new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(new Rect(s.x + 6, s.y + 2, s.width - 12, s.height - 4), _status, _labelStyle);
            }
        }

        private void DrawIcons()
        {
            var cam = ViewCamera;
            if (cam == null) return;
            var e = Event.current;
            Vector2 mouse = e.mousePosition;

            _visible.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path)) continue;
                foreach (var note in StickyNoteStore.GetNotes(scene.path))
                {
                    if (note.Resolved && !ShowResolved) continue;
                    var world = note.Position + note.Normal * 0.05f;
                    var sp = cam.WorldToScreenPoint(world);
                    if (sp.z <= 0.05f || sp.z > MaxDistance) continue;
                    // Screen pixels (origin bottom-left) to GUI units (origin top-left, scaled).
                    var gp = new Vector2(sp.x, Screen.height - sp.y) / _scale;
                    float size = Mathf.Lerp(26f, 14f, Mathf.InverseLerp(5f, MaxDistance, sp.z));
                    _visible.Add((note, new Rect(gp.x - size * 0.5f, gp.y - size * 0.5f, size, size), sp.z));
                }
            }
            // Far ones first so near ones draw on top; hover picks the nearest under the mouse.
            _visible.Sort((a, b) => b.depth.CompareTo(a.depth));

            string hover = null;
            for (int i = _visible.Count - 1; i >= 0; i--)
                if (_visible[i].rect.Contains(mouse)) { hover = _visible[i].note.Id; break; }
            if (_draft != null && _panelRect.Contains(mouse)) hover = null;
            _hoverId = hover;

            foreach (var (note, rect, _) in _visible)
            {
                bool selected = _draft != null && _draft.Id == note.Id;
                bool hovered = note.Id == _hoverId;
                var outline = selected ? Color.white : new Color(0f, 0f, 0f, 0.8f);
                Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), outline);
                Fill(rect, StickyNoteStyle.ColorOf(note));
                // The author's colour as a thin stripe at the bottom.
                Fill(new Rect(rect.x, rect.yMax - 3, rect.width, 3), StickyNoteStyle.ParseColor(note.AuthorColor, Color.white));
                GUI.Label(rect, StickyNoteStyle.GlyphOf(note), _glyphStyle);
                if (hovered) DrawTooltip(note, rect);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && _hoverId != null)
            {
                var note = StickyNoteStore.Find(_hoverId);
                if (note != null) BeginEdit(note);
                e.Use();
            }
        }

        private void DrawTooltip(StickyNote note, Rect icon)
        {
            string text = $"<b>{note.NoteType}</b>{(note.Resolved ? " (resolved)" : "")} — {note.Author}\n{Truncate(note.Text, 300)}";
            var content = new GUIContent(text);
            float width = 280f;
            float height = _boxStyle.CalcHeight(content, width);
            var r = new Rect(icon.xMax + 6, icon.y, width, height);
            if (r.xMax > Screen.width / _scale) r.x = icon.x - width - 6;
            GUI.Box(r, content, _boxStyle);
        }

        private void DrawPanel()
        {
            if (!_panelPlaced)
            {
                _panelRect.x = Screen.width / _scale - _panelRect.width - 16;
                _panelRect.y = 60;
                _panelPlaced = true;
            }
            _panelRect = GUI.Window(0x57A11E, _panelRect, DrawPanelContents, _draftIsNew ? "New Sticky Note" : "Sticky Note");
        }

        private void DrawPanelContents(int id)
        {
            var note = _draft;
            if (note == null) return;

            int type = GUILayout.Toolbar((int)note.NoteType, new[] { "Default", "Warning", "Error" });
            if (type != (int)note.NoteType) note.NoteType = (StickyNoteType)type;

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(140));
            GUI.SetNextControlName("StickyNoteText");
            note.Text = GUILayout.TextArea(note.Text ?? "", GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            if (!_draftIsNew)
            {
                var created = StickyNote.ParseTime(note.Created);
                GUILayout.Label($"{note.Author}{(created != DateTime.MinValue ? " · " + created.ToString("g") : "")}" +
                                (string.IsNullOrEmpty(note.AttachedName) ? "" : $" · on {note.AttachedName}"));
                if (note.Resolved && !string.IsNullOrEmpty(note.ResolvedBy)) GUILayout.Label($"Resolved by {note.ResolvedBy}");
            }
            else if (!string.IsNullOrEmpty(note.AttachedName)) GUILayout.Label($"On {note.AttachedName}");

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
                if (GUILayout.Button(_confirmDelete ? "Really delete?" : "Delete"))
                    action = () =>
                    {
                        if (_confirmDelete) { StickyNoteStore.Delete(note.Id); _draft = null; }
                        else _confirmDelete = true;
                    };
            }
            if (GUILayout.Button("Cancel")) action = () => _draft = null;
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
            if (action != null) action();
        }

        #endregion

        #region Editing

        private void BeginEdit(StickyNote note)
        {
            _draft = note.Clone();
            _draftIsNew = false;
            _confirmDelete = false;
            _scroll = Vector2.zero;
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
            _draftIsNew = true;
            _confirmDelete = false;
            _scroll = Vector2.zero;
            GUI.FocusControl("StickyNoteText");
        }

        private void SaveDraft(bool keepOpen = false)
        {
            var d = _draft;
            if (d == null) return;
            if (_draftIsNew)
            {
                var added = StickyNoteStore.Add(d.Scene, d.Position, d.Normal, d.NoteType, d.Text, d.AttachedId, d.AttachedName);
                Status(StickyNoteStore.IsConnected ? "Note added and shared." : "Note added (not connected: kept locally).");
                _draft = keepOpen ? added.Clone() : null;
                _draftIsNew = false;
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
            _glyphStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            _glyphStyle.normal.textColor = new Color(0.1f, 0.1f, 0.1f);
            _labelStyle = new GUIStyle(GUI.skin.label) { richText = true };
            _labelStyle.normal.textColor = Color.white;
            _boxStyle = new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
            _boxStyle.normal.textColor = Color.white;
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
