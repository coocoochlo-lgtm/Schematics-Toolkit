using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remedy.Schematics.Collaboration;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// Window &gt; Schematics &gt; Sticky Notes Board: every sticky note as a card on a Kanban board with three columns,
    /// <b>To Do</b>, <b>In Progress</b> and <b>Done</b>. A note lands in To Do the moment it's made (in the editor or by a
    /// playtester in a build), because the board just shows the notes; it never keeps its own copy.
    ///
    /// <para>The column follows from the note (<see cref="StickyNote.Status"/>): resolved means Done, an assignee means In
    /// Progress, otherwise To Do. <b>Take</b> assigns a note to you and moves it to In Progress, <b>Done</b> resolves it,
    /// <b>Give back</b> puts it back in To Do. You can also drag cards between columns. Assignments travel through the
    /// normal note sync (newest version wins), so everyone's board agrees.</para>
    ///
    /// <para>Cards are sorted automatically: To Do by type (Error, Warning, Default) and then oldest first; In Progress
    /// grouped by assignee (you first); Done newest first. Double-click a card to frame its note in the Scene view
    /// (opening its scene if needed). When someone leaves the session with unfinished tasks, they go back to To Do
    /// (<see cref="StickyNotesTaskReturn"/>).</para>
    /// </summary>
    public sealed class StickyNotesKanbanWindow : EditorWindow
    {
        [SerializeField] private bool _allScenes = true;
        [SerializeField] private bool _onlyMine;
        [SerializeField] private string _search = "";
        [SerializeField] private string _focusId;

        private readonly Column[] _columns = new Column[3];
        private Label _status;
        private bool _rebuildQueued;

        // Dragging a card between columns.
        private VisualElement _pressedCard;
        private string _pressedId;
        private Vector2 _pressStart;
        private bool _dragging;
        private Label _ghost;

        private sealed class Column
        {
            public StickyNoteStatus Status;
            public VisualElement Root;
            public Label Count;
            public ScrollView List;
        }

        [MenuItem("Window/Schematics/Sticky Notes Board")]
        private static void OpenFromMenu() => Open();

        /// <summary>Opens the board (docked as a tab next to the Scene view when possible), optionally showing one note.</summary>
        public static void Open(string focusNoteId = null)
        {
            var w = GetWindow<StickyNotesKanbanWindow>("Notes Board", true, typeof(SceneView));
            w.titleContent = new GUIContent("Notes Board", EditorGUIUtility.IconContent("console.infoicon.sml").image);
            w._focusId = focusNoteId;
            w.Show();
            w.QueueRebuild();
        }

        private void OnEnable()
        {
            StickyNoteStore.Changed += OnStoreChanged;
            CollabSession.PeerJoined += OnPeersChanged;
            CollabSession.PeerLeft += OnPeersChanged;
            if (_allScenes) StickyNotesEditorService.LoadAllFiles();
        }

        private void OnDisable()
        {
            StickyNoteStore.Changed -= OnStoreChanged;
            CollabSession.PeerJoined -= OnPeersChanged;
            CollabSession.PeerLeft -= OnPeersChanged;
        }

        private void OnStoreChanged(string scene) => QueueRebuild();
        private void OnPeersChanged(CollabPeer peer) => QueueRebuild();

        private void QueueRebuild()
        {
            if (_rebuildQueued || rootVisualElement == null) return;
            _rebuildQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _rebuildQueued = false;
                Rebuild();
            }).StartingIn(50);
        }

        #region Layout

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;

            var toolbar = new Toolbar();
            var all = new ToolbarToggle { text = "All scenes", value = _allScenes, tooltip = "Every scene's notes (otherwise only the open scenes')" };
            all.RegisterValueChangedCallback(e =>
            {
                _allScenes = e.newValue;
                if (_allScenes) StickyNotesEditorService.LoadAllFiles();
                Rebuild();
            });
            toolbar.Add(all);
            var mine = new ToolbarToggle { text = "Only mine", value = _onlyMine, tooltip = "Only tasks assigned to you, plus To Do" };
            mine.RegisterValueChangedCallback(e => { _onlyMine = e.newValue; Rebuild(); });
            toolbar.Add(mine);
            var search = new ToolbarSearchField { value = _search ?? "" };
            search.style.flexGrow = 1;
            search.style.maxWidth = 260;
            search.RegisterValueChangedCallback(e => { _search = e.newValue; Rebuild(); });
            toolbar.Add(search);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            toolbar.Add(spacer);
            _status = new Label();
            _status.style.unityTextAlign = TextAnchor.MiddleRight;
            _status.style.marginRight = 6;
            _status.style.fontSize = 10;
            toolbar.Add(_status);
            toolbar.Add(new ToolbarButton(() => { StickyNotesSceneGUI.Placing = true; SceneView.lastActiveSceneView?.Focus(); }) { text = "Add Note", tooltip = "Click in the Scene view where the note goes" });
            toolbar.Add(new ToolbarButton(StickyNotesWindow.Open) { text = "List" });
            root.Add(toolbar);

            var board = new VisualElement();
            board.style.flexDirection = FlexDirection.Row;
            board.style.flexGrow = 1;
            board.style.paddingLeft = board.style.paddingRight = board.style.paddingTop = board.style.paddingBottom = 4;
            root.Add(board);

            _columns[0] = MakeColumn(board, StickyNoteStatus.ToDo, "To Do");
            _columns[1] = MakeColumn(board, StickyNoteStatus.InProgress, "In Progress");
            _columns[2] = MakeColumn(board, StickyNoteStatus.Done, "Done");

            // Ages ("5 min ago") and the session line go stale; refresh now and then.
            root.schedule.Execute(Rebuild).Every(30000);
            Rebuild();
        }

        private static Column MakeColumn(VisualElement board, StickyNoteStatus status, string title)
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexBasis = 0;
            col.style.marginLeft = col.style.marginRight = 3;
            col.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.80f, 0.80f, 0.80f);
            SetRadius(col, 4);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.paddingLeft = header.style.paddingRight = 8;
            header.style.paddingTop = header.style.paddingBottom = 5;
            var name = new Label(title);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.flexGrow = 1;
            header.Add(name);
            var count = new Label("0");
            count.style.opacity = 0.7f;
            header.Add(count);
            col.Add(header);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1;
            list.contentContainer.style.paddingLeft = list.contentContainer.style.paddingRight = 4;
            list.contentContainer.style.paddingBottom = 6;
            col.Add(list);

            board.Add(col);
            return new Column { Status = status, Root = col, Count = count, List = list };
        }

        #endregion

        #region Cards

        private void Rebuild()
        {
            if (_columns[0] == null) return;

            bool live = StickyNoteStore.IsConnected;
            int others = live ? CollabSession.Peers?.Count(p => p != null && !p.IsLocal) ?? 0 : 0;
            _status.text = live ? $"Live with {others} other{(others == 1 ? "" : "s")} · you are {StickyNoteStore.LocalName}" : $"Local only · you are {StickyNoteStore.LocalName}";

            var notes = Collect();
            var todo = notes.Where(n => n.Status == StickyNoteStatus.ToDo).ToList();
            todo.Sort(StickyNoteStyle.CompareByPriority);
            var doing = notes.Where(n => n.Status == StickyNoteStatus.InProgress).ToList();
            var done = notes.Where(n => n.Status == StickyNoteStatus.Done)
                .OrderByDescending(n => n.Updated, StringComparer.Ordinal).ToList();

            Fill(_columns[0], todo, groupByAssignee: false);
            Fill(_columns[1], doing, groupByAssignee: true);
            Fill(_columns[2], done, groupByAssignee: false);

            if (!string.IsNullOrEmpty(_focusId))
            {
                string id = _focusId;
                _focusId = null;
                rootVisualElement.schedule.Execute(() =>
                {
                    foreach (var col in _columns)
                    foreach (var card in col.List.Query<VisualElement>(className: "sticky-card").ToList())
                        if ((card.userData as string) == id) { col.List.ScrollTo(card); Flash(card); }
                }).StartingIn(60);
            }
        }

        private List<StickyNote> Collect()
        {
            IEnumerable<string> scenes;
            if (_allScenes) scenes = StickyNoteStore.Scenes.ToList();
            else
            {
                var open = new List<string>();
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!string.IsNullOrEmpty(s.path)) open.Add(s.path);
                }
                scenes = open;
            }
            var list = new List<StickyNote>();
            foreach (var scene in scenes.Distinct())
                foreach (var n in StickyNoteStore.GetNotes(scene))
                    if (Matches(n)) list.Add(n);
            return list;
        }

        private bool Matches(StickyNote n)
        {
            if (_onlyMine && n.Status != StickyNoteStatus.ToDo && !StickyNotesTaskReturn.IsMine(n)) return false;
            if (string.IsNullOrEmpty(_search)) return true;
            return (n.Text ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || (n.Author ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || (n.Assignee ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || (n.AttachedName ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || (n.Scene ?? "").IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Fill(Column column, List<StickyNote> notes, bool groupByAssignee)
        {
            column.List.Clear();
            column.Count.text = notes.Count.ToString();
            if (notes.Count == 0)
            {
                var empty = new Label(column.Status == StickyNoteStatus.ToDo ? "Nothing to do." : column.Status == StickyNoteStatus.InProgress ? "Nobody's on anything. Take a task from To Do." : "Nothing done yet.");
                empty.style.opacity = 0.6f;
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.marginTop = 6;
                empty.style.marginLeft = 4;
                column.List.Add(empty);
                return;
            }
            if (!groupByAssignee)
            {
                foreach (var n in notes) column.List.Add(MakeCard(n));
                return;
            }

            // In Progress: one group per assignee, yours first, then by name; inside a group by priority, oldest first.
            var groups = notes.GroupBy(n => string.IsNullOrEmpty(n.Assignee) ? "(someone)" : n.Assignee, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Any(StickyNotesTaskReturn.IsMine) ? 0 : 1)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                bool mine = g.Any(StickyNotesTaskReturn.IsMine);
                bool here = !StickyNoteStore.IsConnected || mine || (CollabSession.Peers?.Any(p => p != null && g.First().IsAssignedTo(p.Id, p.Name)) ?? false);
                var header = new Label($"{g.Key}{(mine ? " (you)" : "")}{(here ? "" : " · away")}  {g.Count()}");
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.style.fontSize = 11;
                header.style.marginTop = 6;
                header.style.marginBottom = 2;
                header.style.marginLeft = 2;
                header.style.opacity = here ? 0.9f : 0.55f;
                column.List.Add(header);
                var list = g.ToList();
                list.Sort(StickyNoteStyle.CompareByPriority);
                foreach (var n in list) column.List.Add(MakeCard(n));
            }
        }

        private VisualElement MakeCard(StickyNote note)
        {
            bool pro = EditorGUIUtility.isProSkin;
            var typeColor = StickyNoteStyle.ColorOf(note.NoteType);
            var card = new VisualElement { userData = note.Id, tooltip = note.Text };
            card.AddToClassList("sticky-card");
            card.style.marginTop = 4;
            card.style.paddingLeft = 8;
            card.style.paddingRight = 6;
            card.style.paddingTop = 5;
            card.style.paddingBottom = 5;
            card.style.backgroundColor = pro ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.93f, 0.93f, 0.93f);
            card.style.borderLeftWidth = 5;
            card.style.borderLeftColor = note.Resolved ? StickyNoteStyle.ResolvedColor : typeColor;
            SetRadius(card, 3);
            if (note.Resolved) card.style.opacity = 0.8f;

            // Type badge and excerpt.
            var top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            var badge = new Label(note.NoteType.ToString());
            badge.style.fontSize = 9;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = new Color(0.1f, 0.1f, 0.1f);
            badge.style.backgroundColor = typeColor;
            badge.style.paddingLeft = badge.style.paddingRight = 4;
            badge.style.marginRight = 6;
            badge.style.alignSelf = Align.FlexStart;
            SetRadius(badge, 2);
            top.Add(badge);
            var text = new Label(Excerpt(note.Text, 140));
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.flexShrink = 1;
            text.style.flexGrow = 1;
            top.Add(text);
            card.Add(top);

            // Scene · author · age.
            string scene = string.IsNullOrEmpty(note.Scene) ? "" : Path.GetFileNameWithoutExtension(note.Scene);
            AddMeta(card, $"{scene}{(string.IsNullOrEmpty(note.AttachedName) ? "" : " · on " + note.AttachedName)} · by {note.Author} · {StickyNoteStyle.Ago(note.Created)}");
            if (note.Status == StickyNoteStatus.InProgress)
                AddMeta(card, $"Taken by {note.Assignee}{(string.IsNullOrEmpty(note.ClaimedAt) ? "" : " " + StickyNoteStyle.Ago(note.ClaimedAt))}");
            else if (note.Status == StickyNoteStatus.Done)
                AddMeta(card, $"Done{(string.IsNullOrEmpty(note.ResolvedBy) ? "" : " by " + note.ResolvedBy)} · {StickyNoteStyle.Ago(note.Updated)}");
            if (note.HasPerf)
            {
                var perf = AddMeta(card, "Perf: " + StickyNotePerf.Summary(note.Perf));
                perf.tooltip = StickyNotePerf.Details(note.Perf);
            }

            // Buttons.
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 3;
            string id = note.Id;
            switch (note.Status)
            {
                case StickyNoteStatus.ToDo:
                    buttons.Add(SmallButton("Take", "Assign it to you and move it to In Progress", () => StickyNoteStore.Claim(id)));
                    buttons.Add(SmallButton("Done", "Resolve it", () => StickyNoteStore.SetResolved(id, true)));
                    break;
                case StickyNoteStatus.InProgress:
                    if (StickyNotesTaskReturn.IsMine(note))
                        buttons.Add(SmallButton("Give back", "Put it back in To Do", () => StickyNoteStore.GiveBack(id)));
                    else
                    {
                        buttons.Add(SmallButton("Take over", $"Assign it to you instead of {note.Assignee}", () => StickyNoteStore.Claim(id)));
                        buttons.Add(SmallButton("Give back", $"Put it back in To Do (takes it away from {note.Assignee})", () => StickyNoteStore.GiveBack(id)));
                    }
                    buttons.Add(SmallButton("Done", "Resolve it", () => StickyNoteStore.SetResolved(id, true)));
                    break;
                default:
                    buttons.Add(SmallButton("Reopen", "Back to To Do", () => StickyNoteStore.SetResolved(id, false)));
                    break;
            }
            buttons.Add(SmallButton("…", "More", () => ShowMenu(id)));
            card.Add(buttons);

            card.RegisterCallback<PointerDownEvent>(e => OnCardDown(card, id, e));
            card.RegisterCallback<PointerMoveEvent>(e => OnCardMove(e));
            card.RegisterCallback<PointerUpEvent>(e => OnCardUp(card, e));
            card.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            return card;
        }

        private static Label AddMeta(VisualElement card, string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10;
            l.style.opacity = 0.7f;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginTop = 2;
            card.Add(l);
            return l;
        }

        private static Button SmallButton(string text, string tooltip, Action onClick)
        {
            var b = new Button(() => StickyNotesEditorService.Safe(onClick)) { text = text, tooltip = tooltip };
            b.style.fontSize = 10;
            b.style.height = 18;
            b.style.marginLeft = 2;
            b.style.marginRight = 0;
            b.style.paddingLeft = b.style.paddingRight = 5;
            return b;
        }

        private void ShowMenu(string id)
        {
            var note = StickyNoteStore.Find(id);
            if (note == null) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Frame in Scene View"), false, () => StickyNotesWindow.Frame(note));
            menu.AddItem(new GUIContent("Edit…"), false, () => StickyNoteEditWindow.Edit(note, MouseScreenRect()));
            if (!string.IsNullOrEmpty(note.AttachedId))
                menu.AddItem(new GUIContent("Select Attached Object"), false, () => StickyNoteEditWindow.SelectAttached(note));
            if (!string.IsNullOrEmpty(note.PerfCapture) && File.Exists(note.PerfCapture))
                menu.AddItem(new GUIContent("Open Profiler Capture"), false, () => StickyNoteEditWindow.OpenCapture(note.PerfCapture));
            menu.AddItem(new GUIContent("Copy Text"), false, () => EditorGUIUtility.systemCopyBuffer = note.Text);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Delete Sticky Note", $"Delete \"{StickyNoteStyle.Summary(note)}\" for everyone?", "Delete", "Cancel"))
                    StickyNoteStore.Delete(note.Id);
            });
            menu.ShowAsContext();
        }

        private Rect MouseScreenRect()
        {
            var p = position.position + position.size * 0.5f;
            return new Rect(p.x, p.y, 1, 1);
        }

        private static string Excerpt(string text, int max)
        {
            text = (text ?? "").Trim().Replace("\r", "");
            if (text.Length == 0) return "(empty note)";
            return text.Length > max ? text.Substring(0, max - 1) + "…" : text;
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        private static void Flash(VisualElement card)
        {
            var old = card.style.backgroundColor;
            card.style.backgroundColor = new Color(0.30f, 0.45f, 0.75f);
            card.schedule.Execute(() => card.style.backgroundColor = old).StartingIn(900);
        }

        #endregion

        #region Clicking and dragging

        private void OnCardDown(VisualElement card, string id, PointerDownEvent e)
        {
            if (e.button != 0) return;
            if (e.target is Button) return;   // the card's own buttons
            if (e.clickCount == 2)
            {
                var note = StickyNoteStore.Find(id);
                if (note != null) EditorApplication.delayCall += () => StickyNotesWindow.Frame(note);
                e.StopPropagation();
                return;
            }
            _pressedCard = card;
            _pressedId = id;
            _pressStart = e.position;
            _dragging = false;
            card.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnCardMove(PointerMoveEvent e)
        {
            if (_pressedCard == null || !_pressedCard.HasPointerCapture(e.pointerId)) return;
            if (!_dragging && ((Vector2)e.position - _pressStart).magnitude < 6f) return;
            if (!_dragging)
            {
                _dragging = true;
                var note = StickyNoteStore.Find(_pressedId);
                _ghost = new Label(note != null ? StickyNoteStyle.Summary(note, 40) : "Note");
                _ghost.pickingMode = PickingMode.Ignore;
                _ghost.style.position = Position.Absolute;
                _ghost.style.paddingLeft = _ghost.style.paddingRight = 6;
                _ghost.style.paddingTop = _ghost.style.paddingBottom = 3;
                _ghost.style.backgroundColor = note != null ? StickyNoteStyle.ColorOf(note) : StickyNoteStyle.DefaultColor;
                _ghost.style.color = new Color(0.1f, 0.1f, 0.1f);
                _ghost.style.opacity = 0.9f;
                SetRadius(_ghost, 3);
                rootVisualElement.Add(_ghost);
                _pressedCard.style.opacity = 0.4f;
            }
            var local = rootVisualElement.WorldToLocal(e.position);
            _ghost.style.left = local.x + 8;
            _ghost.style.top = local.y + 4;
            var over = ColumnAt(e.position);
            foreach (var c in _columns)
            {
                if (c == null) continue;
                bool hot = c == over;
                c.Root.style.borderTopWidth = c.Root.style.borderBottomWidth = c.Root.style.borderLeftWidth = c.Root.style.borderRightWidth = hot ? 2 : 0;
                var accent = new Color(0.35f, 0.6f, 1f);
                c.Root.style.borderTopColor = c.Root.style.borderBottomColor = c.Root.style.borderLeftColor = c.Root.style.borderRightColor = accent;
            }
        }

        private void OnCardUp(VisualElement card, PointerUpEvent e)
        {
            if (_pressedCard == null || !card.HasPointerCapture(e.pointerId)) return;
            string id = _pressedId;
            bool dragged = _dragging;
            var target = dragged ? ColumnAt(e.position) : null;
            card.ReleasePointer(e.pointerId);
            EndDrag();
            if (target != null)
            {
                var note = StickyNoteStore.Find(id);
                if (note != null && note.Status != target.Status) StickyNotesEditorService.Safe(() => StickyNoteStore.MoveTo(id, target.Status));
            }
            e.StopPropagation();
        }

        private void EndDrag()
        {
            if (_pressedCard != null) _pressedCard.style.opacity = StyleKeyword.Null;
            _pressedCard = null;
            _dragging = false;
            if (_ghost != null) { _ghost.RemoveFromHierarchy(); _ghost = null; }
            foreach (var c in _columns)
                if (c != null) c.Root.style.borderTopWidth = c.Root.style.borderBottomWidth = c.Root.style.borderLeftWidth = c.Root.style.borderRightWidth = 0;
        }

        private Column ColumnAt(Vector2 panelPosition)
        {
            foreach (var c in _columns)
                if (c != null && c.Root.worldBound.Contains(panelPosition)) return c;
            return null;
        }

        #endregion
    }
}
