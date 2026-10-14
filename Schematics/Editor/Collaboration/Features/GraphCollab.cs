using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BlueGraph.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Collaboration inside Schematic Graph windows: see teammates' cursors and selected nodes in their colour, can't
    /// select nodes someone else has selected, and graph edits reach everyone.
    ///
    /// Graph edits (v1): a graph with unsaved changes is saved about 1.5 seconds after the last change (never mid-drag)
    /// and its saved asset file is sent on the "graph" channel; teammates write the same file and reload the graph in
    /// place. The file (Unity's text serialization) is sent rather than EditorJsonUtility JSON because JSON stores object
    /// references (Prefab, Signals, Variables) as local instance ids that mean nothing on another machine.
    /// Limitations: the whole graph is replaced (last writer wins at graph level); if you have unsaved edits when a
    /// teammate's version arrives, yours are kept and sent shortly after, replacing theirs. Works best when people edit
    /// different graphs, or different parts of a graph a few seconds apart (the selection locks help).
    /// </summary>
    [InitializeOnLoad]
    public static class GraphCollab
    {
        private const string OverlayName = "collab-graph-overlay";
        private const double SaveDelay = 1.5;

        private sealed class WindowState
        {
            public SchematicGraphEditorWindow Window;
            public GraphOverlay Overlay;
            public Vector2 Cursor;
            public bool Inside;
            public bool MouseDown;
            public string SelectionKey = "";
            public long SelectionTime;
        }

        private static readonly Dictionary<SchematicGraphEditorWindow, WindowState> _windows = new();
        private static SchematicGraphEditorWindow _active;
        private static double _lastTick, _lastScan;

        private static readonly Dictionary<string, double> _changedAt = new();
        private static readonly Dictionary<string, double> _suppressUntil = new();
        private static readonly Dictionary<string, int> _revisions = new();
        private static readonly Dictionary<string, GraphMsg> _pausedQueue = new();

        static GraphCollab()
        {
            SchematicGraphEditorWindow.CanvasLoaded += (window, canvas) => CollabFeatures.Safe(() => Attach(window));
            EditorApplication.update += Update;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            CollabSession.Subscribe(CollabChannels.Graph, OnMessage);
            CollabSession.Connected += () => CollabFeatures.Safe(CatchUpOpenGraphs);
            CollabFeatures.Resumed += () => CollabFeatures.Safe(OnResumed);
            CollabFeatures.Paused += () => CollabFeatures.Safe(() => { foreach (var s in _windows.Values) s.Overlay?.Clear(); });
        }

        /// <summary>Asset path of the graph in the most recently used Schematic Graph window (null if none is open).</summary>
        public static string ActiveGraphPath
        {
            get
            {
                if (_active == null || _active.SchematicGraph == null) return null;
                return AssetDatabase.GetAssetPath(_active.SchematicGraph);
            }
        }

        #region Windows

        private static void Attach(SchematicGraphEditorWindow window)
        {
            if (window == null || window.Canvas == null) return;
            var canvas = window.Canvas;
            if (_windows.TryGetValue(window, out var existing) && existing.Overlay != null && existing.Overlay.parent == canvas) return;
            canvas.Q(OverlayName)?.RemoveFromHierarchy();

            var state = new WindowState { Window = window, SelectionTime = CollabFeatures.Now };
            state.Overlay = new GraphOverlay(window) { name = OverlayName };
            canvas.Add(state.Overlay);
            _windows[window] = state;

            canvas.RegisterCallback<MouseMoveEvent>(e =>
            {
                state.Cursor = canvas.contentViewContainer.WorldToLocal(e.mousePosition);
                state.Inside = true;
                _active = window;
                ScenePresence.NoteActivity();
            }, TrickleDown.TrickleDown);
            canvas.RegisterCallback<MouseLeaveEvent>(_ => state.Inside = false);
            canvas.RegisterCallback<MouseUpEvent>(_ => state.MouseDown = false, TrickleDown.TrickleDown);
            canvas.RegisterCallback<MouseDownEvent>(e =>
            {
                _active = window;
                state.MouseDown = true;
                if (!CollabFeatures.IsActive || !CollabFeaturesSettings.instance.SelectionLocks) return;
                var view = (e.target as VisualElement)?.GetFirstAncestorOfType<NodeView>() ?? e.target as NodeView;
                if (view?.Target == null) return;
                var holder = LockHolder(window, view.Target.ID);
                if (holder == null) return;
                e.StopImmediatePropagation();
                state.MouseDown = false;
                window.ShowNotification(new GUIContent("Selected by " + holder.Name), 1.2);
            }, TrickleDown.TrickleDown);

            CatchUp(window);
        }

        private static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastScan > 1.0)
            {
                _lastScan = now;
                CollabFeatures.Safe(ScanWindows);
            }
            if (now - _lastTick < 0.1) return;
            _lastTick = now;
            if (EditorWindow.focusedWindow is SchematicGraphEditorWindow focused) _active = focused;
            if (!CollabFeatures.IsActive) return;
            CollabFeatures.Safe(PublishPresence);
            foreach (var state in _windows.Values.ToList())
            {
                CollabFeatures.Safe(() => EnforceLocks(state));
                CollabFeatures.Safe(() => state.Overlay?.Refresh());
            }
            CollabFeatures.Safe(SaveChangedGraphs);
        }

        private static void ScanWindows()
        {
            foreach (var window in _windows.Keys.ToList())
                if (window == null || window.Canvas == null) _windows.Remove(window);
            foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
                if (window != null && window.Canvas != null && window.Canvas.Q(OverlayName) == null)
                    Attach(window);
        }

        private static string GuidOf(SchematicGraphEditorWindow window)
        {
            if (window == null || window.SchematicGraph == null) return null;
            return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(window.SchematicGraph));
        }

        private static void PublishPresence()
        {
            if (_active == null || _active.Canvas == null || _active.SchematicGraph == null || !_windows.TryGetValue(_active, out var state))
            {
                CollabFeatures.PublishPresence(CollabChannels.PresenceGraph, new GraphPresence());
                return;
            }
            var canvas = _active.Canvas;
            var selected = canvas.selection.OfType<NodeView>().Where(v => v.Target != null).Select(v => v.Target.ID).OrderBy(id => id).ToList();
            var key = string.Join(",", selected);
            if (key != state.SelectionKey)
            {
                state.SelectionKey = key;
                state.SelectionTime = CollabFeatures.Now;
            }
            var presence = new GraphPresence
            {
                path = AssetDatabase.GetAssetPath(_active.SchematicGraph),
                guid = GuidOf(_active),
                cursor = new Vector2(Mathf.Round(state.Cursor.x), Mathf.Round(state.Cursor.y)),
                cursorInside = state.Inside,
                selected = selected,
                t = state.SelectionTime,
                viewPosition = canvas.viewTransform.position,
                viewScale = canvas.viewTransform.scale.x,
            };
            CollabFeatures.PublishPresence(CollabChannels.PresenceGraph, presence);
        }

        /// <summary>Remote peers looking at the same graph as the window, with their graph presence.</summary>
        internal static IEnumerable<(CollabPeer peer, GraphPresence presence)> PeersIn(SchematicGraphEditorWindow window)
        {
            var guid = GuidOf(window);
            if (guid == null) yield break;
            foreach (var peer in CollabFeatures.RemotePeers())
            {
                var presence = CollabFeatures.GetPresence<GraphPresence>(peer, CollabChannels.PresenceGraph);
                if (presence != null && presence.guid == guid) yield return (peer, presence);
            }
        }

        private static CollabPeer LockHolder(SchematicGraphEditorWindow window, string nodeId)
        {
            foreach (var (peer, presence) in PeersIn(window))
                if (presence.selected != null && presence.selected.Contains(nodeId))
                    return peer;
            return null;
        }

        /// <summary>Removes nodes other people hold from the selection (box selection, select all, or a later-arriving claim).</summary>
        private static void EnforceLocks(WindowState state)
        {
            if (!CollabFeaturesSettings.instance.SelectionLocks) return;
            var canvas = state.Window != null ? state.Window.Canvas : null;
            if (canvas == null || canvas.selection.Count == 0) return;
            CollabPeer holder = null;
            string localId = CollabFeatures.LocalId;
            foreach (var view in canvas.selection.OfType<NodeView>().ToList())
            {
                if (view.Target == null) continue;
                foreach (var (peer, presence) in PeersIn(state.Window))
                {
                    if (presence.selected == null || !presence.selected.Contains(view.Target.ID)) continue;
                    bool theyWereFirst = presence.t < state.SelectionTime || (presence.t == state.SelectionTime && string.CompareOrdinal(peer.Id, localId) < 0);
                    // A node we just clicked is always ours to give up; one we held first stays ours.
                    if (!theyWereFirst) continue;
                    canvas.RemoveFromSelection(view);
                    holder ??= peer;
                    break;
                }
            }
            if (holder != null) state.Window.ShowNotification(new GUIContent("Selected by " + holder.Name), 1.2);
        }

        #endregion

        #region Graph edits: sending

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!CollabFeatures.IsActive) return;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out var data);
                var guid = data.guid.ToString();
                if (_windows.Keys.Any(w => GuidOf(w) == guid)) _changedAt[guid] = EditorApplication.timeSinceStartup;
            }
        }

        private static void SaveChangedGraphs()
        {
            if (!CollabFeaturesSettings.instance.LiveGraphSync) return;
            double now = EditorApplication.timeSinceStartup;
            foreach (var state in _windows.Values.ToList())
            {
                var graph = state.Window != null ? state.Window.SchematicGraph : null;
                if (graph == null) continue;
                var guid = GuidOf(state.Window);
                if (string.IsNullOrEmpty(guid)) continue;
                if (_suppressUntil.TryGetValue(guid, out var until) && until > now) continue;
                if (!EditorUtility.IsDirty(graph)) { _changedAt.Remove(guid); continue; }
                if (!_changedAt.TryGetValue(guid, out var at)) { _changedAt[guid] = now; continue; }
                if (state.MouseDown || now - at < SaveDelay) continue;
                _changedAt.Remove(guid);
                SaveAndBroadcast(graph, guid);
            }
        }

        private static void SaveAndBroadcast(SchematicGraph graph, string guid)
        {
            AssetDatabase.SaveAssetIfDirty(graph);
            var path = AssetDatabase.GetAssetPath(graph);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var bytes = File.ReadAllBytes(path);
            bool text = bytes.Length > 5 && Encoding.ASCII.GetString(bytes, 0, 5) == "%YAML";
            _revisions.TryGetValue(guid, out var rev);
            var msg = new GraphMsg
            {
                guid = guid,
                path = path,
                from = CollabFeatures.LocalId,
                rev = rev + 1,
                enc = text ? "text" : "base64",
                data = text ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes),
            };
            _revisions[guid] = msg.rev;
            var json = JsonUtility.ToJson(msg);
            CollabSession.Send(CollabChannels.Graph, json);
            if (CollabSession.IsHost) CollabSession.SetShared(CollabChannels.SharedGraphKey(guid), json);
        }

        #endregion

        #region Graph edits: receiving

        private static void OnMessage(CollabPeer peer, string json)
        {
            GraphMsg msg;
            try { msg = JsonUtility.FromJson<GraphMsg>(json); }
            catch (Exception e) { CollabLog.Warning($"Ignored an unreadable graph message from {peer?.Name ?? "a teammate"} ({e.Message})."); return; }
            if (msg == null || string.IsNullOrEmpty(msg.guid) || msg.from == CollabFeatures.LocalId) return;
            if (CollabSession.IsHost) CollabSession.SetShared(CollabChannels.SharedGraphKey(msg.guid), json);
            if (!CollabFeaturesSettings.instance.LiveGraphSync) return;
            if (!CollabFeatures.IsActive) { _pausedQueue[msg.guid] = msg; return; }
            CollabFeatures.Safe(() => Apply(peer, msg));
        }

        private static void OnResumed()
        {
            var queued = _pausedQueue.Values.ToList();
            _pausedQueue.Clear();
            foreach (var msg in queued) Apply(CollabSession.Find(msg.from), msg);
            CatchUpOpenGraphs();
        }

        private static void CatchUpOpenGraphs()
        {
            foreach (var window in _windows.Keys.ToList()) CatchUp(window);
        }

        /// <summary>Applies the host's latest copy of a graph edited this session (late join / window opened).</summary>
        private static void CatchUp(SchematicGraphEditorWindow window)
        {
            if (!CollabFeatures.IsActive || !CollabFeaturesSettings.instance.LiveGraphSync) return;
            var guid = GuidOf(window);
            if (guid == null) return;
            string json = null;
            try { json = CollabSession.GetShared(CollabChannels.SharedGraphKey(guid)); } catch (Exception) { }
            if (string.IsNullOrEmpty(json)) return;
            var msg = JsonUtility.FromJson<GraphMsg>(json);
            if (msg != null && msg.from != CollabFeatures.LocalId) Apply(CollabSession.Find(msg.from), msg);
        }

        private static void Apply(CollabPeer peer, GraphMsg msg)
        {
            string path = AssetDatabase.GUIDToAssetPath(msg.guid);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;   // a new graph: git brings it

            byte[] bytes = msg.enc == "base64" ? Convert.FromBase64String(msg.data ?? "") : Encoding.UTF8.GetBytes(msg.data ?? "");
            if (bytes.Length == 0) return;
            if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;   // already have it

            var graph = AssetDatabase.LoadAssetAtPath<SchematicGraph>(path);
            if (graph != null && EditorUtility.IsDirty(graph))
            {
                // Keep the local unsaved edits; they're sent shortly and replace this version everywhere.
                foreach (var window in _windows.Keys)
                    if (window != null && window.SchematicGraph == graph)
                        window.ShowNotification(new GUIContent($"{peer?.Name ?? "A teammate"} changed this graph; your newer edits win"), 2.0);
                _changedAt[msg.guid] = EditorApplication.timeSinceStartup;
                return;
            }

            _suppressUntil[msg.guid] = EditorApplication.timeSinceStartup + 3.0;
            if (_revisions.TryGetValue(msg.guid, out var rev) && rev >= msg.rev) msg.rev = rev;
            _revisions[msg.guid] = msg.rev;
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            foreach (var window in _windows.Keys.ToList())
            {
                if (window == null || window.Canvas == null || GuidOf(window) != msg.guid) continue;
                window.Canvas.Reload();
                window.ShowNotification(new GUIContent("Updated by " + (peer?.Name ?? "a teammate")), 1.0);
            }
        }

        #endregion

        #region Navigation

        /// <summary>Opens a graph (or focuses its window) and centres the view on a graph-space point.</summary>
        public static void OpenAndCenter(string graphPath, Vector2 graphPoint, float scale)
        {
            var graph = AssetDatabase.LoadAssetAtPath<SchematicGraph>(graphPath);
            if (graph == null)
            {
                CollabFeatures.Notify("That graph isn't in your project yet");
                return;
            }
            AssetDatabase.OpenAsset(graph);
            int attempts = 0;
            void TryCenter()
            {
                var window = Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>().FirstOrDefault(w => w.SchematicGraph == graph && w.Canvas != null);
                var canvas = window != null ? window.Canvas : null;
                if (canvas == null || float.IsNaN(canvas.layout.width) || canvas.layout.width < 1)
                {
                    if (++attempts < 20) EditorApplication.delayCall += TryCenter;
                    return;
                }
                window.Focus();
                float s = scale > 0.01f ? scale : 1f;
                var center = new Vector2(canvas.layout.width, canvas.layout.height) * 0.5f;
                canvas.UpdateViewTransform(new Vector3(center.x - graphPoint.x * s, center.y - graphPoint.y * s, 0), new Vector3(s, s, 1));
            }
            EditorApplication.delayCall += TryCenter;
        }

        #endregion
    }

    /// <summary>Draws teammates' cursors and their selected nodes over a graph canvas.</summary>
    internal sealed class GraphOverlay : VisualElement
    {
        private readonly SchematicGraphEditorWindow _window;

        public GraphOverlay(SchematicGraphEditorWindow window)
        {
            _window = window;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0; style.top = 0; style.right = 0; style.bottom = 0;
        }

        public void Refresh()
        {
            Clear();
            var canvas = _window != null ? _window.Canvas : null;
            if (canvas == null || !CollabFeatures.IsActive) return;
            var peers = GraphCollab.PeersIn(_window).ToList();
            if (peers.Count == 0) return;
            var views = canvas.Query<NodeView>().ToList();

            foreach (var (peer, presence) in peers)
            {
                var color = peer.Color;
                if (presence.selected != null && presence.selected.Count > 0)
                    foreach (var view in views)
                    {
                        if (view.Target == null || !presence.selected.Contains(view.Target.ID)) continue;
                        var rect = this.WorldToLocal(view.worldBound);
                        var outline = new VisualElement { pickingMode = PickingMode.Ignore };
                        outline.style.position = Position.Absolute;
                        outline.style.left = rect.x - 3; outline.style.top = rect.y - 3;
                        outline.style.width = rect.width + 6; outline.style.height = rect.height + 6;
                        outline.style.borderLeftWidth = outline.style.borderRightWidth = outline.style.borderTopWidth = outline.style.borderBottomWidth = 2;
                        outline.style.borderLeftColor = outline.style.borderRightColor = outline.style.borderTopColor = outline.style.borderBottomColor = color;
                        outline.style.borderTopLeftRadius = outline.style.borderTopRightRadius = outline.style.borderBottomLeftRadius = outline.style.borderBottomRightRadius = 6;
                        var tag = NameTag(peer.Name, color);
                        tag.style.top = -18; tag.style.left = 0;
                        outline.Add(tag);
                        Add(outline);
                    }

                if (presence.cursorInside)
                {
                    var world = canvas.contentViewContainer.LocalToWorld(presence.cursor);
                    var local = this.WorldToLocal(world);
                    var cursor = new VisualElement { pickingMode = PickingMode.Ignore };
                    cursor.style.position = Position.Absolute;
                    cursor.style.left = local.x; cursor.style.top = local.y;
                    cursor.style.width = 14; cursor.style.height = 18;
                    cursor.generateVisualContent += ctx =>
                    {
                        var painter = ctx.painter2D;
                        painter.fillColor = color;
                        painter.strokeColor = Color.black;
                        painter.lineWidth = 1f;
                        painter.BeginPath();
                        painter.MoveTo(new Vector2(0, 0));
                        painter.LineTo(new Vector2(0, 16));
                        painter.LineTo(new Vector2(4.5f, 12));
                        painter.LineTo(new Vector2(11, 12));
                        painter.ClosePath();
                        painter.Fill();
                        painter.Stroke();
                    };
                    var tag = NameTag(peer.Name, color);
                    tag.style.left = 12; tag.style.top = 14;
                    cursor.Add(tag);
                    Add(cursor);
                }
            }
        }

        private static Label NameTag(string name, Color color)
        {
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.backgroundColor = color;
            label.style.color = color.grayscale > 0.6f ? Color.black : Color.white;
            label.style.fontSize = 10;
            label.style.paddingLeft = label.style.paddingRight = 4;
            label.style.paddingTop = label.style.paddingBottom = 1;
            label.style.borderTopLeftRadius = label.style.borderTopRightRadius = label.style.borderBottomLeftRadius = label.style.borderBottomRightRadius = 3;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            return label;
        }
    }
}
