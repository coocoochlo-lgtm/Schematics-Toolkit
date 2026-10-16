using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Shows teammates in the Scene view and keeps people from editing the same thing at once.
    /// - Publishes your Scene view camera, selection and location (about 10 times a second, only when they change).
    /// - Draws each teammate who has one of your open scenes open: the simple avatar (a sphere with a camera frustum)
    ///   where their camera is, their name, a dotted line to each object they have selected and a box around it, all in
    ///   their colour.
    /// - Variants: a level is a blockout scene with variant scenes loaded on top. Someone who has a scene open that you
    ///   don't (another variant of the same blockout) is drawn faded, as a ghost, labelled "Name — Variant"; their edits
    ///   to that variant aren't applied here (SceneSync only applies changes to scenes you have loaded), while edits to
    ///   the blockout you share are. Click their avatar to move to their variant.
    /// - Selection locks: you can't select what someone else has selected (it's deselected with "Selected by Name" and
    ///   made unpickable in the Scene view until they let go). If two people grab the same object at the same moment,
    ///   whoever selected first keeps it.
    /// </summary>
    [InitializeOnLoad]
    public static class ScenePresence
    {
        private static double _lastPublish;
        private static double _lastLockRefresh;
        private static double _lastActivity;
        private static long _selectionTime;
        private static string _lastSelectionKey = "";
        private static string _lastCameraJson = "";
        private static bool _graphFocusedLast;

        static ScenePresence()
        {
            EditorApplication.update += Update;
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += () => CollabFeatures.Safe(OnSelectionChanged);
            CollabSession.PeerChanged += _ => CollabFeatures.Safe(OnPeersChanged);
            CollabSession.PeerLeft += _ => CollabFeatures.Safe(OnPeersChanged);
            CollabSession.Connected += () => { CollabFeatures.ResetPublishedPresence(); _lastPublish = 0; };
            CollabFeatures.Paused += () => CollabFeatures.Safe(OnPaused);
            // Play Mode publishes the game camera as "camera" (CollabRuntimeRunner): send the Scene view's again after.
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode) { CollabFeatures.ResetPublishedPresence(); _lastPublish = 0; }
            };
            _selectionTime = CollabFeatures.Now;
            _lastActivity = EditorApplication.timeSinceStartup;
        }

        /// <summary>Marks the user as active (for the Team window's Active/Idle status).</summary>
        public static void NoteActivity() => _lastActivity = EditorApplication.timeSinceStartup;

        /// <summary>When the local selection last changed (unix ms): earlier selector wins a contested object.</summary>
        public static long SelectionTime => _selectionTime;

        #region Publishing

        private static void Update()
        {
            if (!CollabSession.IsConnected) return;
            double now = EditorApplication.timeSinceStartup;

            var focused = EditorWindow.focusedWindow;
            if (focused is SceneView) _graphFocusedLast = false;
            else if (focused is SchematicGraphEditorWindow) _graphFocusedLast = true;

            float rate = Mathf.Clamp(CollabFeaturesSettings.instance.PresenceRate, 2f, 20f);
            if (now - _lastPublish >= 1.0 / rate)
            {
                _lastPublish = now;
                CollabFeatures.Safe(Publish);
            }
            if (now - _lastLockRefresh >= 0.25)
            {
                _lastLockRefresh = now;
                CollabFeatures.Safe(RefreshPickingLocks);
            }
        }

        private static void Publish()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CollabFeatures.PublishPresence(CollabChannels.PresenceLocation, new LocationPresence { kind = "none", status = "Play Mode", scenes = OpenScenePaths() });
                CollabFeatures.PublishPresence(CollabChannels.PresenceSelection, new SelectionPresence { t = _selectionTime });
                return;
            }

            // The camera first, on its own: whatever goes wrong with selection or location must not stop it.
            CollabFeatures.Safe(PublishCamera);

            var selection = new SelectionPresence { t = _selectionTime, scene = SceneManager.GetActiveScene().path };
            foreach (var go in Selection.gameObjects)
                if (go != null && SceneSync.IsSyncedScene(go.scene) && !CollabPlaceholders.IsPlaceholder(go))
                {
                    var id = CollabObjectIds.GetId(go);
                    if (id != null) selection.ids.Add(id);
                }
            CollabFeatures.PublishPresence(CollabChannels.PresenceSelection, selection);

            var location = new LocationPresence { scenes = OpenScenePaths() };
            var graph = GraphCollab.ActiveGraphPath;
            if (_graphFocusedLast && !string.IsNullOrEmpty(graph))
            {
                location.kind = "graph";
                location.path = graph;
                location.name = System.IO.Path.GetFileNameWithoutExtension(graph);
            }
            else if (location.scenes.Count > 0)
            {
                var active = SceneManager.GetActiveScene();
                location.kind = "scene";
                location.path = SceneSync.IsSyncedScene(active) ? active.path : location.scenes[0];
                location.name = System.IO.Path.GetFileNameWithoutExtension(location.path);
            }
            location.status = EditorApplication.timeSinceStartup - _lastActivity > 120 ? "Idle" : "Active";
            CollabFeatures.PublishPresence(CollabChannels.PresenceLocation, location);
        }

        private static bool _repairedView;

        private static void PublishCamera()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null) return;

            // A Scene view whose rotation became invalid (for example by jumping to a teammate whose camera presence had
            // a zero quaternion) logs "Quaternion To Matrix conversion failed" on every GUI call and its camera stops
            // updating, so the avatar others see stops following. Repair it once and say so.
            var viewRotation = view.rotation;
            var safeViewRotation = CollabFeatures.SafeRotation(viewRotation);
            if (safeViewRotation.x != viewRotation.x || safeViewRotation.y != viewRotation.y || safeViewRotation.z != viewRotation.z || safeViewRotation.w != viewRotation.w)
            {
                view.rotation = safeViewRotation;
                if (!_repairedView)
                {
                    _repairedView = true;
                    CollabLog.Warning("Your Scene view camera had an invalid rotation; it was reset so teammates see your camera again.");
                }
                return;   // the camera transform updates on the next repaint
            }

            // Where the Scene view camera really is (its transform), so the avatar follows orbiting, flying and zooming.
            var cameraTransform = view.camera.transform;
            var cam = new CameraPresence
            {
                scene = SceneSync.IsSyncedScene(SceneManager.GetActiveScene()) ? SceneManager.GetActiveScene().path : "",
                scenes = OpenScenePaths(),
                pivot = Round(CollabFeatures.SafePosition(view.pivot)),
                rotation = CollabFeatures.SafeRotation(cameraTransform.rotation),
                size = float.IsFinite(view.size) && view.size > 0f ? view.size : 10f,
                ortho = view.orthographic,
                in2D = view.in2DMode,
                position = Round(CollabFeatures.SafePosition(cameraTransform.position)),
            };
            string json = JsonUtility.ToJson(cam);
            if (json != _lastCameraJson) { _lastCameraJson = json; NoteActivity(); }
            CollabFeatures.PublishPresence(CollabChannels.PresenceCamera, cam);
        }

        private static Vector3 Round(Vector3 v) => new(Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f);

        /// <summary>Paths of the open scenes that are saved assets, active scene first.</summary>
        public static List<string> OpenScenePaths()
        {
            var list = new List<string>();
            var active = SceneManager.GetActiveScene();
            if (SceneSync.IsSyncedScene(active)) list.Add(active.path);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (SceneSync.IsSyncedScene(scene) && !list.Contains(scene.path)) list.Add(scene.path);
            }
            return list;
        }

        #endregion

        #region Selection locks

        /// <summary>Scene objects other people have selected, with who has them.</summary>
        public static Dictionary<GameObject, CollabPeer> LockedObjects()
        {
            var locked = new Dictionary<GameObject, CollabPeer>();
            if (!CollabFeatures.IsActive || !CollabFeaturesSettings.instance.SelectionLocks) return locked;
            foreach (var peer in CollabFeatures.RemotePeers())
            {
                var selection = CollabFeatures.GetPresence<SelectionPresence>(peer, CollabChannels.PresenceSelection);
                if (selection?.ids == null) continue;
                foreach (var id in selection.ids)
                {
                    var go = CollabObjectIds.Resolve(id);
                    if (go != null && !locked.ContainsKey(go)) locked[go] = peer;
                }
            }
            return locked;
        }

        private static void OnSelectionChanged()
        {
            var key = string.Join(",", Selection.objects.Where(o => o != null).Select(o => o.GetHashCode()).OrderBy(i => i));
            if (key != _lastSelectionKey)
            {
                _lastSelectionKey = key;
                _selectionTime = CollabFeatures.Now;
                NoteActivity();
            }
            if (!CollabFeatures.IsActive || !CollabFeaturesSettings.instance.SelectionLocks) return;
            var locked = LockedObjects();
            if (locked.Count == 0) return;
            Deselect(locked, _ => true);
        }

        /// <summary>Removes locked objects from the selection (those the filter picks) and says who has them.</summary>
        private static void Deselect(Dictionary<GameObject, CollabPeer> locked, System.Func<CollabPeer, bool> loses)
        {
            var keep = new List<Object>();
            CollabPeer holder = null;
            foreach (var obj in Selection.objects)
            {
                var go = obj as GameObject ?? (obj as Component)?.gameObject;
                if (go != null && locked.TryGetValue(go, out var peer) && loses(peer))
                {
                    holder ??= peer;
                    continue;
                }
                keep.Add(obj);
            }
            if (holder == null) return;
            Selection.objects = keep.ToArray();
            CollabFeatures.Notify("Selected by " + holder.Name);
        }

        private static void OnPeersChanged()
        {
            SceneView.RepaintAll();
            if (!CollabFeatures.IsActive || !CollabFeaturesSettings.instance.SelectionLocks) return;
            // Someone selected what we have: whoever selected first keeps it.
            var locked = LockedObjects();
            if (locked.Count == 0) return;
            string localId = CollabFeatures.LocalId;
            Deselect(locked, peer =>
            {
                var selection = CollabFeatures.GetPresence<SelectionPresence>(peer, CollabChannels.PresenceSelection);
                if (selection == null) return false;
                return selection.t < _selectionTime || (selection.t == _selectionTime && string.CompareOrdinal(peer.Id, localId) < 0);
            });
        }

        private static void RefreshPickingLocks()
        {
            var registry = CollabObjectIds.Instance;
            var locked = LockedObjects();
            var svm = SceneVisibilityManager.instance;
            foreach (var go in locked.Keys)
            {
                if (registry.PickingLocked.Contains(go)) continue;
                if (svm.IsPickingDisabled(go, false)) continue;   // the user disabled it themselves: not ours to restore
                svm.DisablePicking(go, false);
                registry.PickingLocked.Add(go);
            }
            for (int i = registry.PickingLocked.Count - 1; i >= 0; i--)
            {
                var go = registry.PickingLocked[i];
                if (go != null && locked.ContainsKey(go)) continue;
                if (go != null) svm.EnablePicking(go, false);
                registry.PickingLocked.RemoveAt(i);
            }
        }

        private static void OnPaused()
        {
            var registry = CollabObjectIds.Instance;
            foreach (var go in registry.PickingLocked)
                if (go != null) SceneVisibilityManager.instance.EnablePicking(go, false);
            registry.PickingLocked.Clear();
            SceneView.RepaintAll();
        }

        #endregion

        #region Drawing

        private static void OnSceneGUI(SceneView view)
        {
            if (!CollabFeatures.IsActive) return;
            var current = Event.current;
            if (current.type == EventType.MouseDown || current.type == EventType.KeyDown || current.type == EventType.ScrollWheel)
                NoteActivity();
            if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
            {
                CollabFeatures.Safe(() => ClickAvatar(view, current));
                if (current.type == EventType.Used) return;
            }
            if (current.type != EventType.Repaint) return;

            var open = new HashSet<string>(OpenScenePaths());
            foreach (var peer in CollabFeatures.RemotePeers())
            {
                var cam = CollabFeatures.GetPresence<CameraPresence>(peer, CollabChannels.PresenceCamera);
                if (cam == null || cam.scenes == null || !cam.scenes.Any(open.Contains)) continue;
                CollabFeatures.Safe(() => DrawPeer(view, peer, cam, OtherSceneOf(cam, open)));
            }
        }

        /// <summary>
        /// The scene a teammate has open that you don't (their variant), or null when they're in the scenes you have.
        /// Their active scene wins when it's one of those.
        /// </summary>
        public static string OtherSceneOf(CameraPresence cam, HashSet<string> open)
        {
            if (cam?.scenes == null) return null;
            if (!string.IsNullOrEmpty(cam.scene) && !open.Contains(cam.scene)) return cam.scene;
            return cam.scenes.FirstOrDefault(s => !string.IsNullOrEmpty(s) && !open.Contains(s));
        }

        /// <summary>A scene path's display name: the Level Designer's "Level — Variant" when it knows the scene, else the file name.</summary>
        public static string SceneDisplayName(string path) => CollabLevelDesignerLink.DisplayName(path);

        // Clicking a teammate who is in another variant offers to move there.
        private static void ClickAvatar(SceneView view, Event current)
        {
            if (view.camera == null) return;
            var open = new HashSet<string>(OpenScenePaths());
            CollabPeer hit = null;
            string variant = null;
            float best = 28f;
            foreach (var peer in CollabFeatures.RemotePeers())
            {
                var cam = CollabFeatures.GetPresence<CameraPresence>(peer, CollabChannels.PresenceCamera);
                if (cam == null || cam.scenes == null || !cam.scenes.Any(open.Contains)) continue;
                string other = OtherSceneOf(cam, open);
                if (other == null) continue;
                var position = CollabFeatures.SafePosition(cam.position);
                if (view.camera.WorldToViewportPoint(position).z <= 0f) continue;
                float distance = (HandleUtility.WorldToGUIPoint(position) - current.mousePosition).magnitude;
                if (distance < best) { best = distance; hit = peer; variant = other; }
            }
            if (hit == null) return;
            current.Use();
            var peerToJoin = hit;
            string target = variant;
            // Not from inside the Scene view's GUI pass: a modal dialog there breaks the GUI layout.
            EditorApplication.delayCall += () => CollabFeatures.Safe(() =>
            {
                string name = SceneDisplayName(target);
                if (EditorUtility.DisplayDialog("Move to " + name + "?",
                        $"{peerToJoin.Name} is working on {name}.\n\nMove to {name}? Your current variant is closed (you'll be asked to save changes); the blockout stays.",
                        "Move", "Cancel"))
                    CollabNavigation.MoveToScene(peerToJoin, target);
            });
        }

        private static void DrawPeer(SceneView view, CollabPeer peer, CameraPresence cam, string otherScene)
        {
            bool ghost = otherScene != null;
            var color = peer.Color;
            if (ghost) color.a = 0.35f;
            // The remote peer's camera pose, validated (GetPresence already did; this guards older cached values).
            var position = CollabFeatures.SafePosition(cam.position);
            var rotation = CollabFeatures.SafeRotation(cam.rotation);

            var zTest = Handles.zTest;
            var handlesColor = Handles.color;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            try
            {
                CollabFeatures.Safe(() => DrawPlaceholderAvatar(position, rotation, 1f, color));

                var labelColor = peer.Color;
                if (ghost) labelColor.a = 0.7f;
                var labelStyle = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = labelColor } };
                string label = peer.Role == CollabRole.Playtester ? peer.Name + " (playing)" : peer.Name;
                if (ghost) label += " — " + SceneDisplayName(otherScene);
                Handles.Label(position + Vector3.up * 0.75f, label, labelStyle);
                if (ghost)
                {
                    var hint = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = labelColor } };
                    Handles.Label(position + Vector3.up * 0.55f, "click to move there", hint);
                }

                var selection = CollabFeatures.GetPresence<SelectionPresence>(peer, CollabChannels.PresenceSelection);
                if (selection?.ids != null)
                {
                    Handles.color = color;
                    foreach (var id in selection.ids)
                    {
                        var go = CollabObjectIds.Resolve(id);
                        if (go == null) continue;
                        var bounds = WorldBounds(go);
                        Handles.DrawDottedLine(position, bounds.center, 4f);
                        DrawBox(bounds, color);
                    }
                }
            }
            finally
            {
                Handles.zTest = zTest;
                Handles.color = handlesColor;
            }
        }

        /// <summary>The avatar: a sphere where their camera is and a small camera frustum pointing where they look.</summary>
        public static void DrawPlaceholderAvatar(Vector3 position, Quaternion rotation, float scale, Color color)
        {
            Handles.color = color;
            Handles.SphereHandleCap(0, position, rotation, 0.45f * scale, EventType.Repaint);

            // A small camera frustum pointing where they look.
            Vector3 forward = rotation * Vector3.forward, right = rotation * Vector3.right, up = rotation * Vector3.up;
            var tip = position + forward * 0.2f * scale;
            var far = position + forward * 0.9f * scale;
            float w = 0.4f * scale, h = 0.25f * scale;
            var corners = new[] { far + right * w + up * h, far - right * w + up * h, far - right * w - up * h, far + right * w - up * h };
            foreach (var corner in corners) Handles.DrawLine(tip, corner, 2f);
            for (int i = 0; i < 4; i++) Handles.DrawLine(corners[i], corners[(i + 1) % 4], 2f);
            Handles.color = new Color(color.r, color.g, color.b, 0.15f * Mathf.Clamp01(color.a));
            Handles.DrawAAConvexPolygon(corners);
        }

        /// <summary>World bounds of an object's renderers (or colliders, or a half-metre box at its position).</summary>
        public static Bounds WorldBounds(GameObject go)
        {
            bool any = false;
            Bounds bounds = default;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any)
                foreach (var collider in go.GetComponentsInChildren<Collider>())
                {
                    if (!any) { bounds = collider.bounds; any = true; }
                    else bounds.Encapsulate(collider.bounds);
                }
            return any ? bounds : new Bounds(go.transform.position, Vector3.one * 0.5f);
        }

        /// <summary>A thick wire box.</summary>
        public static void DrawBox(Bounds b, Color color, float thickness = 2.5f)
        {
            Handles.color = color;
            Vector3 min = b.min, max = b.max;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
                p[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            for (int e = 0; e < 12; e++) Handles.DrawLine(p[edges[e, 0]], p[edges[e, 1]], thickness);
        }

        #endregion
    }
}
