using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Live multiplayer scene editing. Local edits are captured, batched (20 times a second) and sent on the "scene"
    /// channel; teammates' edits are applied as they arrive. Only scenes saved as assets take part (an unsaved scene has
    /// no path for others to match), and prefab editing stages are ignored (prefab assets travel through git).
    ///
    /// Capture:
    /// - Property edits come from Undo.postprocessModifications (Inspector edits, handle drags, anything recorded with
    ///   Undo), sent as "path = value" for just the properties that changed.
    /// - ObjectChangeEvents tells us about creations, deletions, re-parenting, added/removed components, and property
    ///   changes made without Undo detail (those send the whole component instead).
    ///
    /// Conflicts: what someone has selected is locked for everyone else (<see cref="ScenePresence"/>); beyond that the
    /// last write to a property wins.
    ///
    /// Late joiners: the host keeps every scene message since the scene's last commit in the shared store
    /// ("scene:&lt;path&gt;", a <see cref="SceneLog"/>). A joiner opens the committed scene and replays the log; message ids
    /// make replays harmless. The host saves and commits edited scenes every few minutes (Project Settings), when they
    /// close, and when the session ends, with everyone who edited them as co-authors; the log then starts over.
    ///
    /// Undo: remote edits are applied without Undo by default, so Ctrl+Z only undoes your own work (Unity has one
    /// undo stack, so recording them would let you undo a teammate's edit). With "Record Remote Edits In Undo" on, each
    /// remote change gets its own named Undo group; undoing it is then a local edit that's synced like any other.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneSync
    {
        private const double FlushInterval = 0.05;
        private const double SharedWriteInterval = 2.0;
        private const int MaxDeferred = 2000;

        // Outgoing, batched until the next flush.
        private static readonly Dictionary<Object, HashSet<string>> _pendingProps = new();
        private static readonly HashSet<Object> _pendingFull = new();
        private static readonly HashSet<GameObject> _pendingCreates = new();
        private static readonly HashSet<GameObject> _pendingStructure = new();
        private static readonly HashSet<GameObject> _pendingParent = new();
        private static readonly HashSet<string> _pendingDeadScan = new();
        private static readonly Dictionary<Object, double> _undoCovered = new();
        private static double _lastFlush;

        // Incoming.
        private static int _applyDepth;
        private static readonly Dictionary<GameObject, double> _suppress = new();
        private static readonly Dictionary<string, List<(CollabPeer peer, SceneMsg msg)>> _deferred = new();
        private static readonly List<(CollabPeer peer, SceneMsg msg)> _pausedQueue = new();
        private static bool _replaying;

        // Host bookkeeping.
        private static readonly Dictionary<string, SceneLog> _logs = new();
        private static readonly HashSet<string> _dirtyLogs = new();
        private static readonly Dictionary<string, Dictionary<string, CollabPeer>> _editors = new();
        private static readonly Dictionary<string, double> _lastCommit = new();
        private static double _lastSharedWrite;
        private static bool _wasHost;
        private static bool _committingClosed;

        static SceneSync()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            Undo.postprocessModifications += OnPostprocessModifications;
            EditorApplication.update += Update;
            EditorSceneManager.sceneOpened += (scene, _) => CollabFeatures.Safe(() => OnSceneOpened(scene));
            EditorSceneManager.sceneClosing += (scene, _) => CollabFeatures.Safe(() => OnSceneClosing(scene));
            EditorApplication.quitting += () => CollabFeatures.Safe(() => CommitAll(force: true, summary: "Collaborative edits (editor closed)"));
            CollabSession.Subscribe(CollabChannels.Scene, OnMessage);
            CollabSession.Connected += () => CollabFeatures.Safe(OnConnected);
            CollabSession.Disconnected += () => CollabFeatures.Safe(OnDisconnected);
            CollabSession.HostChanged += () => CollabFeatures.Safe(OnHostChanged);
            CollabFeatures.Resumed += () => CollabFeatures.Safe(OnResumed);
            _wasHost = CollabSession.IsConnected && CollabSession.IsHost;
        }

        private static bool Enabled => CollabFeatures.IsActive && CollabFeaturesSettings.instance.LiveSceneSync;
        private static bool RecordUndo => CollabFeaturesSettings.instance.RecordRemoteEditsInUndo;

        /// <summary>True while a remote change is being applied (its effects mustn't be sent back out).</summary>
        public static bool IsApplying => _applyDepth > 0;

        internal static void BeginApply() => _applyDepth++;
        internal static void EndApply() => _applyDepth = Math.Max(0, _applyDepth - 1);

        /// <summary>Ignores change events for the object for a moment (they were caused by applying a remote change).</summary>
        internal static void SuppressEcho(GameObject go)
        {
            if (go != null) _suppress[go] = EditorApplication.timeSinceStartup + 1.0;
        }

        private static bool IsSuppressed(GameObject go) =>
            go != null && _suppress.TryGetValue(go, out var until) && until > EditorApplication.timeSinceStartup;

        /// <summary>True for scenes that take part in syncing: saved assets, loaded, not a prefab stage or preview.</summary>
        public static bool IsSyncedScene(Scene scene) =>
            scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path) && !EditorSceneManager.IsPreviewScene(scene)
            && scene.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);

        private static bool IsEligible(GameObject go) =>
            go != null && (go.hideFlags & HideFlags.DontSaveInEditor) == 0 && IsSyncedScene(go.scene)
            && !CollabPlaceholders.IsPlaceholder(go) && !IsSuppressed(go);

        private static GameObject GameObjectOf(Object obj) => obj as GameObject ?? (obj as Component)?.gameObject;

        #region Capture

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (!Enabled || IsApplying) return modifications;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                foreach (var modification in modifications)
                {
                    var current = modification.currentValue;
                    if (current == null || current.target == null) continue;
                    var go = GameObjectOf(current.target);
                    if (!IsEligible(go) || PropertyCodec.IsSkipped(current.propertyPath)) continue;
                    if (!_pendingProps.TryGetValue(current.target, out var paths))
                        _pendingProps[current.target] = paths = new HashSet<string>();
                    paths.Add(current.propertyPath);
                    _undoCovered[current.target] = now;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            return modifications;
        }

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!Enabled) return;
            for (int i = 0; i < stream.length; i++)
            {
                try { Capture(ref stream, i); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void Capture(ref ObjectChangeEventStream stream, int i)
        {
            switch (stream.GetEventType(i))
            {
                case ObjectChangeKind.CreateGameObjectHierarchy:
                {
                    stream.GetCreateGameObjectHierarchyEvent(i, out var data);
                    var go = ObjectLookup.FromEventData(data) as GameObject;
                    if (IsEligible(go)) _pendingCreates.Add(go);
                    break;
                }
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                {
                    stream.GetChangeGameObjectStructureHierarchyEvent(i, out var data);
                    var go = ObjectLookup.FromEventData(data) as GameObject;
                    if (IsEligible(go)) _pendingStructure.Add(go);
                    break;
                }
                case ObjectChangeKind.ChangeGameObjectStructure:
                {
                    stream.GetChangeGameObjectStructureEvent(i, out var data);
                    var go = ObjectLookup.FromEventData(data) as GameObject;
                    if (IsEligible(go)) _pendingStructure.Add(go);
                    break;
                }
                case ObjectChangeKind.ChangeGameObjectParent:
                {
                    stream.GetChangeGameObjectParentEvent(i, out var data);
                    var go = ObjectLookup.FromEventData(data) as GameObject;
                    if (IsEligible(go)) _pendingParent.Add(go);
                    break;
                }
                case ObjectChangeKind.ChangeChildrenOrder:
                {
                    stream.GetChangeChildrenOrderEvent(i, out var data);
                    var parent = ObjectLookup.FromEventData(data) as GameObject;
                    if (parent != null)
                        foreach (Transform child in parent.transform)
                            if (IsEligible(child.gameObject)) _pendingParent.Add(child.gameObject);
                    break;
                }
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                {
                    stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var data);
                    var target = ObjectLookup.FromEventData(data);
                    var go = GameObjectOf(target);
                    if (!IsEligible(go)) break;
                    // Already sent property by property through Undo? Then nothing more to do.
                    if (_undoCovered.TryGetValue(target, out var at) && EditorApplication.timeSinceStartup - at < 1.0) break;
                    _pendingFull.Add(target);
                    break;
                }
                case ObjectChangeKind.DestroyGameObjectHierarchy:
                {
                    stream.GetDestroyGameObjectHierarchyEvent(i, out var data);
                    if (IsSyncedScene(data.scene)) _pendingDeadScan.Add(data.scene.path);
                    break;
                }
            }
        }

        #endregion

        #region Flush

        private static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastFlush >= FlushInterval)
            {
                _lastFlush = now;
                if (Enabled) CollabFeatures.Safe(Flush);
                else ClearPending();
                if (_suppress.Count > 0)
                    foreach (var key in _suppress.Where(p => p.Key == null || p.Value < now).Select(p => p.Key).ToList())
                        _suppress.Remove(key);
                if (_undoCovered.Count > 256)
                    foreach (var key in _undoCovered.Where(p => p.Key == null || now - p.Value > 2).Select(p => p.Key).ToList())
                        _undoCovered.Remove(key);
            }

            if (!CollabSession.IsConnected) return;
            bool isHost = CollabSession.IsHost;
            if (isHost && _dirtyLogs.Count > 0 && now - _lastSharedWrite > SharedWriteInterval)
            {
                _lastSharedWrite = now;
                CollabFeatures.Safe(WriteDirtyLogs);
            }
            if (isHost && CollabFeatures.IsActive) CollabFeatures.Safe(PeriodicCommit);
            _wasHost = isHost;
        }

        private static void ClearPending()
        {
            _pendingProps.Clear();
            _pendingFull.Clear();
            _pendingCreates.Clear();
            _pendingStructure.Clear();
            _pendingParent.Clear();
            _pendingDeadScan.Clear();
        }

        private static void Flush()
        {
            // Deletions: registered objects that no longer exist.
            foreach (var scenePath in _pendingDeadScan)
                foreach (var id in CollabObjectIds.TakeDeadIds(scenePath))
                    Send(new SceneMsg { op = "destroy", scene = scenePath, id = id });
            _pendingDeadScan.Clear();

            // Creations (a created child of a created object travels inside its parent's snapshot).
            var created = new HashSet<GameObject>();
            foreach (var go in _pendingCreates)
            {
                if (!IsEligible(go) || HasAncestorIn(go, _pendingCreates)) continue;
                SendHierarchy("create", go);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) created.Add(t.gameObject);
            }
            _pendingCreates.Clear();

            foreach (var go in _pendingStructure)
            {
                if (!IsEligible(go) || created.Contains(go) || HasAncestorIn(go, _pendingStructure)) continue;
                SendHierarchy("structure", go);
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) created.Add(t.gameObject);
            }
            _pendingStructure.Clear();

            foreach (var go in _pendingParent)
            {
                if (!IsEligible(go) || created.Contains(go)) continue;
                SendParent(go);
            }
            _pendingParent.Clear();

            foreach (var pair in _pendingProps)
            {
                var target = pair.Key;
                var go = GameObjectOf(target);
                if (!IsEligible(go) || created.Contains(go)) continue;
                var props = PropertyCodec.Read(target, pair.Value);
                if (props.Count == 0) continue;
                Send(new SceneMsg
                {
                    op = "props", scene = go.scene.path, id = CollabObjectIds.GetId(go),
                    comp = target is Component c ? CollabObjectIds.ComponentKey(c) : "", props = props,
                });
            }
            _pendingProps.Clear();

            foreach (var target in _pendingFull)
            {
                var go = GameObjectOf(target);
                if (!IsEligible(go) || created.Contains(go)) continue;
                if (target is Component component)
                {
                    bool overridesOnly = PrefabUtility.IsPartOfPrefabInstance(component) && !PrefabUtility.IsAddedComponentOverride(component);
                    Send(new SceneMsg
                    {
                        op = "comp", scene = go.scene.path, id = CollabObjectIds.GetId(go),
                        comp = CollabObjectIds.ComponentKey(component), data = SceneSnapshot.CaptureComponent(component, overridesOnly),
                    });
                }
                else
                {
                    Send(new SceneMsg { op = "props", scene = go.scene.path, id = CollabObjectIds.GetId(go), comp = "", props = PropertyCodec.Dump(go, false) });
                }
            }
            _pendingFull.Clear();
        }

        private static bool HasAncestorIn(GameObject go, HashSet<GameObject> set)
        {
            for (var t = go.transform.parent; t != null; t = t.parent)
                if (set.Contains(t.gameObject)) return true;
            return false;
        }

        private static void SendHierarchy(string op, GameObject go)
        {
            CollabObjectIds.RegisterScene(go.scene);
            var nodes = SceneSnapshot.Capture(go);
            if (nodes.Count == 0) return;
            var parent = go.transform.parent;
            Send(new SceneMsg
            {
                op = op, scene = go.scene.path, id = nodes[0].id, nodes = nodes,
                parentId = parent != null ? CollabObjectIds.GetId(parent.gameObject) ?? "" : "",
                sibling = go.transform.GetSiblingIndex(),
            });
            CollabObjectIds.UpdateScene(go);

            // A newly made asset placed in the scene: tell teammates so they see a placeholder until git delivers it.
            if (op == "create")
                foreach (var node in nodes)
                    if (!string.IsNullOrEmpty(node.prefabGuid))
                        NewAssetTracker.AnnounceIfNew(node.prefabGuid, new Bounds(node.boundsCenter, node.boundsSize));
        }

        private static void SendParent(GameObject go)
        {
            // Moved to another scene: for everyone else that's a deletion there and a creation here.
            var registeredScene = CollabObjectIds.SceneOf(go) ?? go.scene.path;
            if (registeredScene != null && registeredScene != go.scene.path)
            {
                var id = CollabObjectIds.GetId(go);
                Send(new SceneMsg { op = "destroy", scene = registeredScene, id = id });
                SendHierarchy("create", go);
                return;
            }
            var parent = go.transform.parent;
            Send(new SceneMsg
            {
                op = "parent", scene = go.scene.path, id = CollabObjectIds.GetId(go),
                parentId = parent != null ? CollabObjectIds.GetId(parent.gameObject) ?? "" : "",
                sibling = go.transform.GetSiblingIndex(),
                props = PropertyCodec.Dump(go.transform, false),
            });
        }

        private static void Send(SceneMsg msg)
        {
            if (string.IsNullOrEmpty(msg.id) && msg.op != "committed") return;
            msg.mid = CollabFeatures.NewMessageId();
            msg.from = CollabFeatures.LocalId;
            CollabObjectIds.MarkApplied(msg.mid);
            CollabSession.Send(CollabChannels.Scene, JsonUtility.ToJson(msg));
            if (msg.op != "committed") NoteEditor(msg.scene, CollabSession.Local);
            if (CollabSession.IsHost) AppendToLog(msg);
        }

        #endregion

        #region Receive / apply

        private static void OnMessage(CollabPeer peer, string json)
        {
            SceneMsg msg;
            try { msg = JsonUtility.FromJson<SceneMsg>(json); }
            catch (Exception) { return; }
            if (msg == null || string.IsNullOrEmpty(msg.scene)) return;
            if (!string.IsNullOrEmpty(msg.from) && msg.from == CollabFeatures.LocalId) return;   // our own, echoed

            // The host records everything (even for scenes it doesn't have open, even in Play Mode).
            if (CollabSession.IsHost)
            {
                AppendToLog(msg);
                if (msg.op != "committed") NoteEditor(msg.scene, peer);
            }

            if (!CollabFeaturesSettings.instance.LiveSceneSync) return;
            if (!CollabFeatures.IsActive)
            {
                if (_pausedQueue.Count < 10000) _pausedQueue.Add((peer, msg));
                return;
            }
            CollabFeatures.Safe(() => Apply(peer, msg));
        }

        /// <summary>Applies one scene message if its scene is open here. Returns false if it couldn't be applied yet.</summary>
        private static bool Apply(CollabPeer peer, SceneMsg msg)
        {
            if (msg.op == "committed")
            {
                foreach (var alias in msg.aliases) CollabObjectIds.AddAlias(alias.from, alias.to);
                return true;
            }
            if (CollabObjectIds.WasApplied(msg.mid)) return true;
            var scene = SceneManager.GetSceneByPath(msg.scene);
            if (!IsSyncedScene(scene)) return false;   // not open here: the log replays it when it's opened
            CollabObjectIds.RegisterScene(scene);

            bool record = RecordUndo;
            int group = 0;
            if (record)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Remote edit by " + (peer?.Name ?? "teammate"));
                group = Undo.GetCurrentGroup();
            }

            bool applied;
            BeginApply();
            try { applied = ApplyOp(peer, msg, scene, record); }
            finally
            {
                EndApply();
                if (record)
                {
                    Undo.CollapseUndoOperations(group);
                    Undo.IncrementCurrentGroup();
                }
            }

            if (applied)
            {
                CollabObjectIds.MarkApplied(msg.mid);
                if ((msg.op == "create" || msg.op == "structure") && !_replaying) ReplayDeferred();
            }
            return applied;
        }

        private static bool ApplyOp(CollabPeer peer, SceneMsg msg, Scene scene, bool record)
        {
            switch (msg.op)
            {
                case "props":
                {
                    var go = CollabObjectIds.Resolve(msg.id);
                    if (go == null) return Defer(msg.id, peer, msg);
                    Object target = string.IsNullOrEmpty(msg.comp) ? go : CollabObjectIds.ResolveComponent(go, msg.comp);
                    if (target == null)
                    {
                        // Placeholders only take their transform; everything else waits for the real object.
                        if (CollabPlaceholders.IsPlaceholder(go)) return true;
                        return Defer(msg.id, peer, msg);
                    }
                    if (CollabPlaceholders.IsPlaceholder(go) && !(target is Transform)) return true;
                    PropertyCodec.Apply(target, msg.props, record);
                    SuppressEcho(go);
                    return true;
                }
                case "comp":
                {
                    var go = CollabObjectIds.Resolve(msg.id);
                    if (go == null) return Defer(msg.id, peer, msg);
                    if (CollabPlaceholders.IsPlaceholder(go) || msg.data == null) return true;
                    var component = CollabObjectIds.ResolveComponent(go, msg.comp);
                    if (component == null)
                    {
                        var type = Type.GetType(msg.data.type);
                        if (type == null || !typeof(Component).IsAssignableFrom(type)) return true;
                        component = record ? Undo.AddComponent(go, type) : go.AddComponent(type);
                        if (component == null) return true;
                    }
                    SceneSnapshot.ApplyComponentData(component, msg.data, record);
                    SuppressEcho(go);
                    return true;
                }
                case "create":
                case "structure":
                {
                    GameObject parent = null;
                    if (!string.IsNullOrEmpty(msg.parentId))
                    {
                        parent = CollabObjectIds.Resolve(msg.parentId);
                        if (parent == null) return Defer(msg.parentId, peer, msg);
                    }
                    var result = SceneSnapshot.Apply(msg.nodes, scene, parent, msg.sibling, peer, record);
                    foreach (var go in result.Touched) SuppressEcho(go);
                    return true;
                }
                case "destroy":
                {
                    var go = CollabObjectIds.Resolve(msg.id);
                    if (go == null) return true;   // already gone (or never here)
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        var id = CollabObjectIds.GetId(t.gameObject, false);
                        if (id != null) CollabObjectIds.Unregister(id);
                    }
                    if (record) Undo.DestroyObjectImmediate(go);
                    else Object.DestroyImmediate(go);
                    return true;
                }
                case "parent":
                {
                    var go = CollabObjectIds.Resolve(msg.id);
                    if (go == null) return Defer(msg.id, peer, msg);
                    Transform parent = null;
                    if (!string.IsNullOrEmpty(msg.parentId))
                    {
                        var parentGo = CollabObjectIds.Resolve(msg.parentId);
                        if (parentGo == null) return Defer(msg.parentId, peer, msg);
                        parent = parentGo.transform;
                    }
                    if (go.transform.parent != parent)
                    {
                        if (record) Undo.SetTransformParent(go.transform, parent, "Remote: parent");
                        else go.transform.SetParent(parent, false);
                    }
                    if (parent == null && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
                    if (msg.sibling >= 0) go.transform.SetSiblingIndex(msg.sibling);
                    PropertyCodec.Apply(go.transform, msg.props, record);
                    CollabObjectIds.UpdateScene(go);
                    SuppressEcho(go);
                    return true;
                }
            }
            return true;
        }

        private static bool Defer(string missingId, CollabPeer peer, SceneMsg msg)
        {
            int total = 0;
            foreach (var list in _deferred.Values) total += list.Count;
            if (total >= MaxDeferred) return false;
            if (!_deferred.TryGetValue(missingId ?? "", out var queue)) _deferred[missingId ?? ""] = queue = new();
            queue.Add((peer, msg));
            return false;
        }

        /// <summary>Retries messages that arrived before the object they're about (after creations and placeholder swaps).</summary>
        internal static void ReplayDeferred()
        {
            if (_replaying || _deferred.Count == 0) return;
            _replaying = true;
            try
            {
                for (int pass = 0; pass < 4; pass++)
                {
                    var ready = new List<(CollabPeer peer, SceneMsg msg)>();
                    foreach (var key in _deferred.Keys.ToList())
                        if (CollabObjectIds.Resolve(key) != null)
                        {
                            ready.AddRange(_deferred[key]);
                            _deferred.Remove(key);
                        }
                    if (ready.Count == 0) break;
                    foreach (var (peer, msg) in ready) CollabFeatures.Safe(() => Apply(peer, msg));
                }
            }
            finally { _replaying = false; }
        }

        /// <summary>Replays the host's log of a scene (late join, scene opened, back from Play Mode).</summary>
        public static void ReplaySharedLog(Scene scene)
        {
            if (!Enabled || !IsSyncedScene(scene)) return;
            var log = ReadLog(scene.path);
            if (log == null) return;
            foreach (var alias in log.aliases) CollabObjectIds.AddAlias(alias.from, alias.to);
            int pending = log.entries.Count(e => !CollabObjectIds.WasApplied(e.mid) && e.from != CollabFeatures.LocalId);
            if (pending == 0) return;
            if (scene.isDirty)
                Debug.LogWarning($"[Collaboration] {scene.name} has unsaved local changes from before joining; teammates' edits are applied on top, " +
                                 "but your earlier changes won't reach them unless you redo them while connected.");
            foreach (var entry in log.entries)
            {
                if (entry.from == CollabFeatures.LocalId || CollabObjectIds.WasApplied(entry.mid)) continue;
                var peer = CollabSession.Find(entry.from);
                CollabFeatures.Safe(() => Apply(peer, entry));
            }
        }

        #endregion

        #region Lifecycle

        private static void OnConnected()
        {
            _wasHost = CollabSession.IsHost;
            if (!Enabled) return;
            ForEachSyncedScene(scene =>
            {
                CollabObjectIds.RegisterScene(scene);
                ReplaySharedLog(scene);
            });
        }

        private static void OnResumed()
        {
            var queued = _pausedQueue.ToList();
            _pausedQueue.Clear();
            foreach (var (peer, msg) in queued) CollabFeatures.Safe(() => Apply(peer, msg));
            ForEachSyncedScene(ReplaySharedLog);
        }

        private static void OnSceneOpened(Scene scene)
        {
            if (!IsSyncedScene(scene)) return;
            if (_committingClosed) return;
            CollabObjectIds.RegisterScene(scene, force: true);
            if (Enabled) ReplaySharedLog(scene);
        }

        private static void OnSceneClosing(Scene scene)
        {
            if (!IsSyncedScene(scene) || _committingClosed) return;
            if (CollabSession.IsConnected && CollabSession.IsHost && !EditorApplication.isPlayingOrWillChangePlaymode && HasEdits(scene.path))
                Commit(scene, "Collaborative edits (scene closed)", force: true);
            CollabObjectIds.ForgetScene(scene.path);
        }

        private static void OnDisconnected()
        {
            ClearPending();
            _deferred.Clear();
            if (_wasHost) CommitAll(force: true, summary: "Collaborative edits (session ended)");
            _wasHost = false;
        }

        private static void OnHostChanged()
        {
            _logs.Clear();   // re-read from the shared store, which the new host inherits
            _wasHost = CollabSession.IsHost;
        }

        private static void ForEachSyncedScene(Action<Scene> action)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (IsSyncedScene(scene)) CollabFeatures.Safe(() => action(scene));
            }
        }

        #endregion

        #region Host: log and commits

        private static SceneLog ReadLog(string scenePath)
        {
            if (_logs.TryGetValue(scenePath, out var log)) return log;
            string json = null;
            try { json = CollabSession.GetShared(CollabChannels.SharedSceneKey(scenePath)); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(json)) return null;
            try { log = JsonUtility.FromJson<SceneLog>(json); }
            catch (Exception) { return null; }
            if (CollabSession.IsHost) _logs[scenePath] = log;
            return log;
        }

        private static SceneLog LogFor(string scenePath)
        {
            var log = ReadLog(scenePath);
            if (log == null)
            {
                log = new SceneLog { baseId = Guid.NewGuid().ToString("N"), committedAt = DateTime.UtcNow.ToString("o") };
                _logs[scenePath] = log;
            }
            return log;
        }

        private static void AppendToLog(SceneMsg msg)
        {
            var log = LogFor(msg.scene);
            if (msg.op == "committed")
            {
                foreach (var alias in msg.aliases) log.aliases.Add(alias);
                _dirtyLogs.Add(msg.scene);
                return;
            }
            if (log.entries.Exists(e => e.mid == msg.mid)) return;

            // Compaction: a later value of a property replaces earlier ones (drags send dozens per second).
            if (msg.op == "props" && msg.props.Count > 0)
            {
                var paths = new HashSet<string>(msg.props.Select(p => p.p));
                for (int i = log.entries.Count - 1; i >= 0; i--)
                {
                    var entry = log.entries[i];
                    if (entry.op == "destroy" && entry.id == msg.id) break;
                    if (entry.op != "props" || entry.id != msg.id || entry.comp != msg.comp) continue;
                    entry.props.RemoveAll(p => paths.Contains(p.p));
                    if (entry.props.Count == 0) log.entries.RemoveAt(i);
                }
            }
            log.entries.Add(msg);
            _dirtyLogs.Add(msg.scene);
        }

        private static void WriteDirtyLogs()
        {
            foreach (var scenePath in _dirtyLogs.ToList())
            {
                if (_logs.TryGetValue(scenePath, out var log))
                    CollabSession.SetShared(CollabChannels.SharedSceneKey(scenePath), JsonUtility.ToJson(log));
            }
            _dirtyLogs.Clear();
        }

        private static void NoteEditor(string scenePath, CollabPeer peer)
        {
            if (string.IsNullOrEmpty(scenePath) || peer == null) return;
            if (!_editors.TryGetValue(scenePath, out var editors)) _editors[scenePath] = editors = new();
            editors[peer.Id ?? "?"] = peer;
        }

        private static bool HasEdits(string scenePath)
        {
            if (_editors.TryGetValue(scenePath, out var editors) && editors.Count > 0) return true;
            var log = ReadLog(scenePath);
            return log != null && log.entries.Count > 0;
        }

        private static void PeriodicCommit()
        {
            float minutes = CollabFeaturesSettings.instance.CommitIntervalMinutes;
            if (minutes <= 0) return;
            double now = EditorApplication.timeSinceStartup;
            foreach (var scenePath in _editors.Keys.ToList())
            {
                if (!_lastCommit.TryGetValue(scenePath, out var last)) { _lastCommit[scenePath] = last = now; }
                if (now - last < minutes * 60.0) continue;
                _lastCommit[scenePath] = now;
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (IsSyncedScene(scene)) Commit(scene, "Collaborative edits", force: false);
                else CommitClosedScene(scenePath, "Collaborative edits");
            }
        }

        /// <summary>Host: saves and commits every scene teammates edited (also on the Team window's menu).</summary>
        public static void CommitAll(bool force, string summary)
        {
            if (!CollabSession.IsHost && !_wasHost) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            foreach (var scenePath in _editors.Keys.ToList())
            {
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (IsSyncedScene(scene)) Commit(scene, summary, force);
            }
        }

        /// <summary>
        /// Host: saves the scene, tells everyone the new saved ids of objects created this session, commits it with its
        /// editors as co-authors and starts a fresh log. Waits while placeholders exist (unless forced), because the
        /// objects they stand for would be missing from the commit.
        /// </summary>
        public static bool Commit(Scene scene, string summary, bool force)
        {
            if (!IsSyncedScene(scene)) return false;
            if (!force && Resources.FindObjectsOfTypeAll<CollabPlaceholder>().Any(p => p != null && p.gameObject.scene == scene))
                return false;

            Flush();
            if (!EditorSceneManager.SaveScene(scene)) return false;
            string path = scene.path;
            var aliases = CollabObjectIds.NewlySavedAliases(path);

            _editors.TryGetValue(path, out var editors);
            var coAuthors = editors != null ? editors.Values.Where(p => p != null).ToList() : new List<CollabPeer>();
            if (CollabSession.Local != null && !coAuthors.Contains(CollabSession.Local)) coAuthors.Add(CollabSession.Local);
            var names = string.Join(", ", coAuthors.Select(p => p.Name).Distinct());
            CollabFeatures.Safe(() => CollabGit.CommitScene(path, coAuthors, $"{summary}: {scene.name} ({names})"));

            var old = ReadLog(path);
            var log = new SceneLog { baseId = Guid.NewGuid().ToString("N"), committedAt = DateTime.UtcNow.ToString("o") };
            if (old != null) log.aliases.AddRange(old.aliases);
            log.aliases.AddRange(aliases);
            _logs[path] = log;
            _dirtyLogs.Add(path);
            _editors.Remove(path);
            _lastCommit[path] = EditorApplication.timeSinceStartup;

            if (CollabSession.IsConnected)
            {
                var msg = new SceneMsg { op = "committed", scene = path, aliases = aliases, mid = CollabFeatures.NewMessageId(), from = CollabFeatures.LocalId };
                CollabSession.Send(CollabChannels.Scene, JsonUtility.ToJson(msg));
                WriteDirtyLogs();
            }
            return true;
        }

        /// <summary>
        /// Host: a scene teammates edit that the host doesn't have open is opened additively in the background, brought
        /// up to date from the log, committed, and closed again.
        /// </summary>
        private static void CommitClosedScene(string scenePath, string summary)
        {
            var log = ReadLog(scenePath);
            if (log == null || log.entries.Count == 0) return;
            if (!System.IO.File.Exists(scenePath)) return;
            _committingClosed = true;
            var applied = new List<string>();
            Scene scene = default;
            try
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                CollabObjectIds.RegisterScene(scene, force: true);
                foreach (var entry in log.entries)
                {
                    if (CollabObjectIds.WasApplied(entry.mid)) continue;
                    CollabFeatures.Safe(() => { if (Apply(CollabSession.Find(entry.from), entry)) applied.Add(entry.mid); });
                }
                // Not committed (placeholders still waiting): forget we applied these, so they apply when the scene is next opened.
                if (!Commit(scene, summary, force: false))
                    foreach (var mid in applied) CollabObjectIds.UnmarkApplied(mid);
            }
            finally
            {
                CollabObjectIds.ForgetScene(scenePath);
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                _committingClosed = false;
            }
        }

        #endregion
    }
}
