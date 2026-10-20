using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>One object a player controls, as everyone else sees it (a ghost).</summary>
    [Serializable]
    public sealed class CollabGhostObject
    {
        /// <summary>The editors' id of the object ("g:" + GlobalObjectId) when known, else "rt:" + name + a number.</summary>
        public string id;
        public string name;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        /// <summary>"capsule" (radius, height, center local) or "box" (center local, size world-aligned).</summary>
        public string shape = "box";
        public Vector3 center;
        public Vector3 size = Vector3.one;
        public float radius = 0.5f;
        public float height = 2f;
    }

    /// <summary>A player's ghosts, sent on <see cref="CollabRuntime.GhostChannel"/> about 12 times a second while they move.</summary>
    [Serializable]
    public sealed class CollabGhostFrame
    {
        /// <summary>The player's active scene and every loaded scene (asset paths).</summary>
        public string scene;
        public List<string> scenes = new List<string>();
        public List<CollabGhostObject> objects = new List<CollabGhostObject>();
        /// <summary>Sender's unix time in ms.</summary>
        public long t;
    }

    /// <summary>Presence "control": what this player says it is controlling (for the Team window).</summary>
    [Serializable]
    public sealed class CollabControlPresence
    {
        public List<string> ids = new List<string>();
        public List<string> names = new List<string>();
    }

    /// <summary>
    /// Player ghosts. A game (a build, or the editor's Play Mode) finds the objects its player controls, tells the room
    /// (presence "control") and streams their pose and rough shape on the "ghost" channel. Editors draw them in the Scene
    /// view (ScenePlayerGhosts) and other players draw them in their game (<see cref="CollabWorldOverlay"/>), so a
    /// developer can watch someone play, talk to them and change the level while they're in it.
    ///
    /// "Controlled" means a Schematic Object that gets input from Remedy Input: Remedy Input sends each input through a
    /// Signal (its Input Maps' Outputs), and an object listening to one of those Signals (a graph's On Received, or a
    /// Behavior's subscription) is driven by the player. Found by reflection (this runtime assembly references nothing
    /// of Schematics or the gameplay code), re-checked once a second. Camera rigs that only listen for look input
    /// (a Camera and no collider) are left out. Also counted: a Schematic Behavior whose type name contains
    /// "RemedyInput", and anything game code marks with <see cref="CollabRuntime.SetControlled"/>.
    /// </summary>
    internal static class CollabRuntimeGhosts
    {
        public const string ControlPresenceKey = "control";
        private const float SendInterval = 1f / 12f;
        private const float KeepAliveInterval = 1f;
        private const float ScanInterval = 1f;
        private const float RemoteExpirySeconds = 3f;

        /// <summary>Type-name fragments that mark a Schematic Behavior as "player input" (editable by game code).</summary>
        public static readonly List<string> InputBehaviorNames = new List<string> { "RemedyInput" };

        /// <summary>Whether this game shares its ghosts (build info / settings; on by default).</summary>
        public static bool Share = true;

        private static readonly HashSet<GameObject> _manual = new HashSet<GameObject>();
        private static readonly List<GameObject> _controlled = new List<GameObject>();
        private static readonly Dictionary<GameObject, CollabGhostObject> _shapes = new Dictionary<GameObject, CollabGhostObject>();
        private static float _nextScan, _nextSend, _lastSent;
        private static string _lastFrameKey, _lastControlJson;
        private static bool _hadObjects;
        private static bool _subscribed;

        public sealed class Remote
        {
            public string PeerId;
            public CollabGhostFrame Frame;
            public float ReceivedAt;
        }

        private static readonly Dictionary<string, Remote> _remote = new Dictionary<string, Remote>();

        /// <summary>Other players' ghosts heard in the last few seconds.</summary>
        public static IEnumerable<Remote> RemoteGhosts
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                foreach (var remote in _remote.Values)
                    if (now - remote.ReceivedAt < RemoteExpirySeconds) yield return remote;
            }
        }

        public static void SetControlled(GameObject go, bool controlled)
        {
            if (go == null) return;
            if (controlled) _manual.Add(go);
            else _manual.Remove(go);
            _nextScan = 0f;
        }

        /// <summary>Called once a frame by the runner while collaboration is available.</summary>
        public static void Tick()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                CollabRuntime.Subscribe(CollabRuntime.GhostChannel, OnGhost);
                CollabRuntime.PeersChanged += PruneLeft;
            }
            if (!CollabRuntime.IsConnected || !Share) return;
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + ScanInterval;
                CollabLog.Safe("Finding the objects the player controls", Scan);
            }
            if (now >= _nextSend)
            {
                _nextSend = now + SendInterval;
                CollabLog.Safe("Sending the player's ghost", Publish);
            }
        }

        #region Finding controlled objects

        private static Type _schematicObjectType;
        private static PropertyInfo _instancesProperty, _uidProperty, _behaviorProperty, _enabledProperty;
        private static readonly Dictionary<Type, bool> _isInputType = new Dictionary<Type, bool>();
        private static bool _reflectionReady, _reflectionFailed;
        // Remedy Input's Signals (its Input Maps' Outputs), looked up every few seconds.
        private static readonly List<object> _inputSignals = new List<object>();
        private static float _nextSignalLookup;
        private static FieldInfo _listenersField, _subsField;

        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static void PrepareReflection()
        {
            if (_reflectionReady || _reflectionFailed) return;
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = assembly.GetType("Remedy.Schematics.SchematicObject", false);
                    if (type != null) { _schematicObjectType = type; break; }
                }
                if (_schematicObjectType == null) { _reflectionFailed = true; return; }
                _instancesProperty = _schematicObjectType.GetProperty("BehaviorInstances", BindingFlags.Public | BindingFlags.Instance);
                _uidProperty = _schematicObjectType.GetProperty("UID", BindingFlags.Public | BindingFlags.Instance);
                _reflectionReady = true;
            }
            catch (Exception e)
            {
                _reflectionFailed = true;
                CollabLog.Exception("Looking up Schematic Objects for player ghosts failed", e);
            }
        }

        /// <summary>Remedy Input's output Signals: RemedyInput.Instance.InputMaps[].Inputs[].Output.</summary>
        private static void LookUpInputSignals()
        {
            if (Time.unscaledTime < _nextSignalLookup) return;
            _nextSignalLookup = Time.unscaledTime + 5f;
            _inputSignals.Clear();
            Type remedyInput = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                remedyInput = assembly.GetType("RemedyInput", false);
                if (remedyInput != null) break;
            }
            if (remedyInput == null) return;
            var instanceProperty = remedyInput.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            object input;
            try { input = instanceProperty?.GetValue(null); }
            catch { return; }   // not set up in this game
            if (input == null || (input is UnityEngine.Object unityObject && unityObject == null)) return;
            if (!(remedyInput.GetField("InputMaps", Instance)?.GetValue(input) is System.Collections.IEnumerable maps)) return;
            foreach (var map in maps)
            {
                if (map == null) continue;
                if (!(map.GetType().GetField("Inputs", Instance)?.GetValue(map) is System.Collections.IEnumerable inputs)) continue;
                foreach (var entry in inputs)
                {
                    var output = entry?.GetType().GetField("Output", Instance)?.GetValue(entry);
                    if (output is UnityEngine.Object signal && signal != null && !_inputSignals.Contains(output)) _inputSignals.Add(output);
                }
            }
        }

        /// <summary>Who listens to Remedy Input: Schematic Object UIDs, and the objects behind plain subscriptions.</summary>
        private static void InputListeners(HashSet<int> uids, List<GameObject> objects)
        {
            foreach (var signal in _inputSignals)
            {
                var type = signal.GetType();
                if (_listenersField == null || !_listenersField.DeclaringType.IsAssignableFrom(type)) _listenersField = FindField(type, "_listeners");
                if (_subsField == null || !_subsField.DeclaringType.IsAssignableFrom(type)) _subsField = FindField(type, "_subs");
                if (_listenersField?.GetValue(signal) is System.Collections.IDictionary listeners)
                    foreach (var key in listeners.Keys)
                        if (key is int uid && uid != 0 && listeners[key] is System.Collections.ICollection list && list.Count > 0) uids.Add(uid);
                if (_subsField?.GetValue(signal) is System.Collections.IEnumerable subs)
                    foreach (var sub in subs)
                    {
                        var parent = sub?.GetType().GetProperty("Parent", Instance)?.GetValue(sub);
                        var go = parent as GameObject ?? (parent as Component)?.gameObject;
                        if (go != null) objects.Add(go);
                    }
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, Instance | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static bool IsInputBehavior(Type type)
        {
            if (type == null) return false;
            if (_isInputType.TryGetValue(type, out bool known)) return known;
            bool result = false;
            foreach (var fragment in InputBehaviorNames)
                if (!string.IsNullOrEmpty(fragment) && type.Name.IndexOf(fragment, StringComparison.Ordinal) >= 0) result = true;
            _isInputType[type] = result;
            return result;
        }

        private static void Scan()
        {
            _controlled.Clear();
            foreach (var go in _manual)
                if (go != null && go.activeInHierarchy) _controlled.Add(go);
            PrepareReflection();
            if (_reflectionReady)
            {
                var uids = new HashSet<int>();
                var listenerObjects = new List<GameObject>();
                try
                {
                    LookUpInputSignals();
                    InputListeners(uids, listenerObjects);
                }
                catch (Exception e) { CollabLog.Exception("Finding Remedy Input's listeners failed", e); }

                foreach (var obj in UnityEngine.Object.FindObjectsByType(_schematicObjectType, FindObjectsSortMode.None))
                {
                    var component = obj as Component;
                    if (component == null || !component.gameObject.activeInHierarchy || _controlled.Contains(component.gameObject)) continue;
                    bool controlled = false;
                    try
                    {
                        if (_uidProperty != null && _uidProperty.GetValue(component) is int uid && uid != 0 && uids.Contains(uid)) controlled = true;
                        if (!controlled)
                            foreach (var go in listenerObjects)
                                if (go != null && (go == component.gameObject || go.transform.IsChildOf(component.transform))) { controlled = true; break; }
                        if (!controlled && _instancesProperty != null) controlled = HasInputBehavior(component);
                    }
                    catch { controlled = false; }
                    if (controlled && !IsCameraRig(component.gameObject)) _controlled.Add(component.gameObject);
                }
            }
            // Shapes are worked out once a second (they rarely change); poses every send.
            var stale = new List<GameObject>();
            foreach (var go in _shapes.Keys) if (go == null || !_controlled.Contains(go)) stale.Add(go);
            foreach (var go in stale) _shapes.Remove(go);
            foreach (var go in _controlled) _shapes[go] = Describe(go);

            var control = new CollabControlPresence();
            foreach (var go in _controlled)
            {
                control.ids.Add(_shapes[go].id);
                control.names.Add(go.name);
            }
            string json = JsonUtility.ToJson(control);
            if (json != _lastControlJson)
            {
                _lastControlJson = json;
                CollabRuntime.SetPresence(ControlPresenceKey, control.ids.Count > 0 ? json : null);
            }
        }

        // A camera rig listens to the look input but isn't the player's body.
        private static bool IsCameraRig(GameObject go) =>
            go.GetComponentInChildren<Camera>(true) != null && go.GetComponentInChildren<Collider>() == null && go.GetComponentInChildren<CharacterController>() == null;

        private static bool HasInputBehavior(Component schematicObject)
        {
            if (!(_instancesProperty.GetValue(schematicObject) is System.Collections.IEnumerable instances)) return false;
            foreach (var instance in instances)
            {
                if (instance == null) continue;
                var type = instance.GetType();
                if (_behaviorProperty == null || _behaviorProperty.DeclaringType != type)
                {
                    _behaviorProperty = type.GetProperty("Behavior", BindingFlags.Public | BindingFlags.Instance);
                    _enabledProperty = type.GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
                }
                object behavior;
                try { behavior = _behaviorProperty?.GetValue(instance); }
                catch { continue; }   // an instance between spawns
                if (behavior == null || !IsInputBehavior(behavior.GetType())) continue;
                bool enabled = true;
                try { if (_enabledProperty != null) enabled = (bool)_enabledProperty.GetValue(instance); } catch { }
                if (enabled) return true;
            }
            return false;
        }

        private static int _runtimeIds;

        private static CollabGhostObject Describe(GameObject go)
        {
            _shapes.TryGetValue(go, out var previous);
            var ghost = new CollabGhostObject { name = go.name };
            ghost.id = previous?.id ?? CollabRuntime.GetObjectId(go) ?? "rt:" + go.name + ":" + (++_runtimeIds);
            var t = go.transform;
            var capsule = go.GetComponentInChildren<CapsuleCollider>();
            var character = go.GetComponentInChildren<CharacterController>();
            if (character != null)
            {
                ghost.shape = "capsule";
                ghost.radius = character.radius * MaxXZ(character.transform.lossyScale);
                ghost.height = character.height * Mathf.Abs(character.transform.lossyScale.y);
                ghost.center = t.InverseTransformPoint(character.transform.TransformPoint(character.center));
            }
            else if (capsule != null && capsule.direction == 1)
            {
                ghost.shape = "capsule";
                ghost.radius = capsule.radius * MaxXZ(capsule.transform.lossyScale);
                ghost.height = capsule.height * Mathf.Abs(capsule.transform.lossyScale.y);
                ghost.center = t.InverseTransformPoint(capsule.transform.TransformPoint(capsule.center));
            }
            else
            {
                bool any = false;
                Bounds bounds = default;
                foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                {
                    if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                    if (!any) { bounds = renderer.bounds; any = true; } else bounds.Encapsulate(renderer.bounds);
                }
                if (!any)
                    foreach (var collider in go.GetComponentsInChildren<Collider>())
                    {
                        if (!any) { bounds = collider.bounds; any = true; } else bounds.Encapsulate(collider.bounds);
                    }
                if (!any) bounds = new Bounds(t.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
                ghost.shape = "box";
                ghost.center = t.InverseTransformPoint(bounds.center);
                ghost.size = bounds.size;
                ghost.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                ghost.height = bounds.size.y;
            }
            if (!float.IsFinite(ghost.radius) || ghost.radius <= 0f) ghost.radius = 0.4f;
            if (!float.IsFinite(ghost.height) || ghost.height <= 0f) ghost.height = 1.8f;
            return ghost;
        }

        private static float MaxXZ(Vector3 scale) => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

        #endregion

        #region Sending

        private static void Publish()
        {
            var frame = new CollabGhostFrame { t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            var active = SceneManager.GetActiveScene();
            frame.scene = active.path;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) frame.scenes.Add(scene.path);
            }
            var key = new System.Text.StringBuilder();
            foreach (var go in _controlled)
            {
                if (go == null || !_shapes.TryGetValue(go, out var shape)) continue;
                var t = go.transform;
                var ghost = new CollabGhostObject
                {
                    id = shape.id, name = shape.name, shape = shape.shape, center = shape.center, size = shape.size,
                    radius = shape.radius, height = shape.height,
                    position = Round(t.position, 100f),
                    rotation = SafeRotation(t.rotation),
                };
                frame.objects.Add(ghost);
                key.Append(ghost.id).Append(ghost.position).Append(Round(ghost.rotation.eulerAngles, 1f));
            }
            float now = Time.unscaledTime;
            bool hasObjects = frame.objects.Count > 0;
            if (!hasObjects && !_hadObjects) return;
            string frameKey = key.ToString() + frame.scene;
            // Only while something moves, plus a keep-alive so receivers know the ghost is still there.
            if (frameKey == _lastFrameKey && now - _lastSent < KeepAliveInterval) return;
            _lastFrameKey = frameKey;
            _lastSent = now;
            _hadObjects = hasObjects;
            CollabRuntime.Send(CollabRuntime.GhostChannel, JsonUtility.ToJson(frame));
        }

        private static Vector3 Round(Vector3 v, float scale) =>
            new Vector3(Mathf.Round(v.x * scale) / scale, Mathf.Round(v.y * scale) / scale, Mathf.Round(v.z * scale) / scale);

        public static Quaternion SafeRotation(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            CollabLiveEditCodec.Normalize(ref x, ref y, ref z, ref w);
            return new Quaternion(x, y, z, w);
        }

        public static Vector3 SafePosition(Vector3 v) =>
            new Vector3(float.IsFinite(v.x) ? v.x : 0f, float.IsFinite(v.y) ? v.y : 0f, float.IsFinite(v.z) ? v.z : 0f);

        #endregion

        #region Receiving

        private static void OnGhost(string peerId, string json)
        {
            if (string.IsNullOrEmpty(peerId) || peerId == CollabRuntime.LocalPeerId) return;
            CollabGhostFrame frame;
            try { frame = JsonUtility.FromJson<CollabGhostFrame>(json); }
            catch (Exception e) { CollabLog.Warning("Ignored an unreadable player ghost (" + e.Message + ")."); return; }
            if (frame == null) return;
            if (frame.objects == null || frame.objects.Count == 0) { _remote.Remove(peerId); return; }
            foreach (var ghost in frame.objects)
            {
                ghost.position = SafePosition(ghost.position);
                ghost.center = SafePosition(ghost.center);
                ghost.rotation = SafeRotation(ghost.rotation);
            }
            _remote[peerId] = new Remote { PeerId = peerId, Frame = frame, ReceivedAt = Time.realtimeSinceStartup };
        }

        private static void PruneLeft()
        {
            var known = new HashSet<string>();
            foreach (var peer in CollabRuntime.Peers) known.Add(peer.Id);
            var gone = new List<string>();
            foreach (var id in _remote.Keys) if (!known.Contains(id)) gone.Add(id);
            foreach (var id in gone) _remote.Remove(id);
        }

        #endregion
    }
}
