using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Stable ids for scene GameObjects, shared by every peer.
    ///
    /// How ids work, and why:
    /// - A GameObject that is saved in the scene file gets "g:" + its <see cref="GlobalObjectId"/>. Everyone opened the
    ///   same committed scene, so everyone computes the same id for it without talking.
    /// - A GameObject created during the session has no file id yet (and a fresh prefab instance's GlobalObjectId isn't
    ///   unique until saved), so its creator assigns "n:" + creatorPeerId + ":" + a random suffix and sends it with the
    ///   snapshot. Receivers register their copy under the same id.
    /// - Ids are remembered per object in a hidden, never-saved registry (this ScriptableObject) the first time they're
    ///   seen, and never recomputed: if someone saves the scene locally and the object's GlobalObjectId appears, the
    ///   object keeps the id everyone already knows. The registry survives script reloads (DontSave ScriptableObjects
    ///   keep their references to scene objects across domain reloads).
    /// - When the host saves and commits a scene, its "n:" objects get real GlobalObjectIds. The host broadcasts
    ///   aliases ("n:..." = "g:...") and keeps them in the scene's shared log, so late joiners (who compute "g:" ids
    ///   from the committed file) and old peers (who still know "n:" ids) understand each other.
    /// - Components aren't given ids: they're addressed as GameObject id + "TypeFullName:index".
    /// Nothing is ever written into the scene, so no extra component ends up in builds or diffs.
    /// </summary>
    public sealed class CollabObjectIds : ScriptableObject
    {
        [Serializable]
        private class Entry
        {
            public GameObject target;
            public string id;
            public string scene;
        }

        [SerializeField] private List<Entry> _entries = new();
        [SerializeField] private List<IdAlias> _aliases = new();
        [SerializeField] private List<string> _appliedMessages = new();
        [SerializeField] private List<string> _registeredScenes = new();

        /// <summary>Objects whose Scene view picking the collaboration features disabled (restored when unlocked).</summary>
        [SerializeField] internal List<GameObject> PickingLocked = new();

        [NonSerialized] private Dictionary<GameObject, Entry> _byObject;
        [NonSerialized] private Dictionary<string, Entry> _byId;
        [NonSerialized] private Dictionary<string, string> _aliasMap;
        [NonSerialized] private HashSet<string> _applied;

        private static CollabObjectIds _instance;

        /// <summary>The registry (found again after a script reload, or created).</summary>
        public static CollabObjectIds Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var found = Resources.FindObjectsOfTypeAll<CollabObjectIds>();
                _instance = found.Length > 0 ? found[0] : CreateInstance<CollabObjectIds>();
                _instance.hideFlags = HideFlags.HideAndDontSave;
                _instance.Rebuild();
                return _instance;
            }
        }

        private void Rebuild()
        {
            _byObject = new Dictionary<GameObject, Entry>();
            _byId = new Dictionary<string, Entry>();
            _aliasMap = new Dictionary<string, string>();
            _applied = new HashSet<string>(_appliedMessages);
            _entries.RemoveAll(e => e == null || string.IsNullOrEmpty(e.id));
            foreach (var entry in _entries)
            {
                if (entry.target != null) _byObject[entry.target] = entry;
                _byId[entry.id] = entry;
            }
            foreach (var alias in _aliases)
            {
                _aliasMap[alias.from] = alias.to;
                _aliasMap[alias.to] = alias.from;
            }
        }

        #region Ids

        /// <summary>The object's id, assigning one if it has none yet (null for objects not in a saved, loaded scene).</summary>
        public static string GetId(GameObject go, bool create = true)
        {
            if (go == null) return null;
            var registry = Instance;
            if (registry._byObject.TryGetValue(go, out var entry) && entry.target == go)
                return entry.id;
            if (!create) return null;
            if (!go.scene.IsValid() || string.IsNullOrEmpty(go.scene.path)) return null;
            if ((go.hideFlags & HideFlags.DontSaveInEditor) != 0) return null;
            string id = SavedId(go) ?? "n:" + CollabFeatures.LocalId + ":" + Guid.NewGuid().ToString("N").Substring(0, 12);
            Register(go, id);
            return id;
        }

        /// <summary>"g:" + GlobalObjectId when the object is saved in its scene file, else null.</summary>
        private static string SavedId(GameObject go)
        {
            var gid = GlobalObjectId.GetGlobalObjectIdSlow(go);
            return IsSaved(go, gid) ? "g:" + gid : null;
        }

        private static bool IsSaved(GameObject go, GlobalObjectId gid)
        {
            if (gid.identifierType == 0) return false;
            return PrefabUtility.IsPartOfPrefabInstance(go) ? gid.targetPrefabId != 0 : gid.targetObjectId != 0;
        }

        /// <summary>Remembers that the object has the id (replacing whatever either had before).</summary>
        public static void Register(GameObject go, string id)
        {
            if (go == null || string.IsNullOrEmpty(id)) return;
            var registry = Instance;
            if (registry._byObject.TryGetValue(go, out var old))
            {
                registry._byId.Remove(old.id);
                registry._entries.Remove(old);
            }
            if (registry._byId.TryGetValue(id, out var other))
            {
                if (other.target != null) registry._byObject.Remove(other.target);
                registry._entries.Remove(other);
            }
            var entry = new Entry { target = go, id = id, scene = go.scene.path };
            registry._entries.Add(entry);
            registry._byObject[go] = entry;
            registry._byId[id] = entry;
        }

        /// <summary>Forgets an id (after a remote destroy, so it isn't reported as a local deletion).</summary>
        public static void Unregister(string id)
        {
            var registry = Instance;
            if (!registry._byId.TryGetValue(id, out var entry)) return;
            registry._byId.Remove(id);
            if (entry.target != null) registry._byObject.Remove(entry.target);
            registry._entries.Remove(entry);
        }

        /// <summary>The GameObject with the id (directly, via an alias, or by GlobalObjectId), or null.</summary>
        public static GameObject Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var registry = Instance;
            if (registry._byId.TryGetValue(id, out var entry) && entry.target != null) return entry.target;
            if (registry._aliasMap.TryGetValue(id, out var alias) && registry._byId.TryGetValue(alias, out entry) && entry.target != null)
                return entry.target;
            foreach (var candidate in new[] { id, alias })
            {
                if (candidate == null || !candidate.StartsWith("g:")) continue;
                if (!GlobalObjectId.TryParse(candidate.Substring(2), out var gid)) continue;
                if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) is GameObject go)
                {
                    // Already known under another id? Keep that one (it's what others use for it).
                    if (registry._byObject.TryGetValue(go, out var known) && known.target == go)
                    {
                        AddAlias(id, known.id);
                        return go;
                    }
                    Register(go, candidate);
                    return go;
                }
            }
            return null;
        }

        /// <summary>Records that two ids name the same object.</summary>
        public static void AddAlias(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return;
            var registry = Instance;
            if (registry._aliasMap.TryGetValue(from, out var existing) && existing == to) return;
            registry._aliases.Add(new IdAlias { from = from, to = to });
            registry._aliasMap[from] = to;
            registry._aliasMap[to] = from;
        }

        /// <summary>
        /// Registers every saved GameObject of a scene, so deleting one we never touched is still noticed and everyone
        /// uses the same "g:" ids for them. Cheap enough for blockout scenes; done once per scene per editor session.
        /// </summary>
        public static void RegisterScene(Scene scene, bool force = false)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path)) return;
            var registry = Instance;
            if (!force && registry._registeredScenes.Contains(scene.path)) return;
            if (!registry._registeredScenes.Contains(scene.path)) registry._registeredScenes.Add(scene.path);

            var all = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if ((t.gameObject.hideFlags & HideFlags.DontSaveInEditor) == 0)
                        all.Add(t.gameObject);
            var objects = all.ToArray();
            var ids = new GlobalObjectId[objects.Length];
            GlobalObjectId.GetGlobalObjectIdsSlow(objects, ids);
            for (int i = 0; i < objects.Length; i++)
            {
                if (registry._byObject.ContainsKey(objects[i])) continue;
                if (IsSaved(objects[i], ids[i])) Register(objects[i], "g:" + ids[i]);
            }
        }

        /// <summary>Forgets a scene (closed): its objects are gone, and reopening it should re-register.</summary>
        public static void ForgetScene(string scenePath)
        {
            var registry = Instance;
            registry._registeredScenes.Remove(scenePath);
            foreach (var entry in registry._entries.ToArray())
                if (entry.scene == scenePath)
                    Unregister(entry.id);
        }

        /// <summary>Ids of registered objects of the scene that no longer exist (deleted locally). They're forgotten.</summary>
        public static List<string> TakeDeadIds(string scenePath)
        {
            var dead = new List<string>();
            var registry = Instance;
            foreach (var entry in registry._entries.ToArray())
                if (entry.scene == scenePath && entry.target == null)
                {
                    dead.Add(entry.id);
                    Unregister(entry.id);
                }
            return dead;
        }

        /// <summary>The registered "n:" objects of a scene that now have a saved GlobalObjectId (after a save), as aliases.</summary>
        public static List<IdAlias> NewlySavedAliases(string scenePath)
        {
            var list = new List<IdAlias>();
            foreach (var entry in Instance._entries)
            {
                if (entry.scene != scenePath || entry.target == null || !entry.id.StartsWith("n:")) continue;
                var saved = SavedId(entry.target);
                if (saved != null) list.Add(new IdAlias { from = entry.id, to = saved });
            }
            foreach (var alias in list) AddAlias(alias.from, alias.to);
            return list;
        }

        /// <summary>The scene path the object was registered in (null if unknown).</summary>
        public static string SceneOf(GameObject go) =>
            go != null && Instance._byObject.TryGetValue(go, out var entry) && entry.target == go ? entry.scene : null;

        /// <summary>Updates the scene path of an object's entry (moved between scenes).</summary>
        public static void UpdateScene(GameObject go)
        {
            if (go != null && Instance._byObject.TryGetValue(go, out var entry)) entry.scene = go.scene.path;
        }

        #endregion

        #region Components

        /// <summary>"TypeFullName:index" for a component (index among components of exactly that type on its GameObject).</summary>
        public static string ComponentKey(Component component)
        {
            if (component == null) return "";
            var type = component.GetType();
            int index = 0;
            foreach (var other in component.GetComponents(type))
            {
                if (other == component) break;
                if (other.GetType() == type) index++;
            }
            return type.FullName + ":" + index;
        }

        /// <summary>The component a key names on the GameObject, or null.</summary>
        public static Component ResolveComponent(GameObject go, string key)
        {
            if (go == null || string.IsNullOrEmpty(key)) return null;
            int colon = key.LastIndexOf(':');
            string typeName = colon >= 0 ? key.Substring(0, colon) : key;
            int index = colon >= 0 && int.TryParse(key.Substring(colon + 1), out var i) ? i : 0;
            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null || component.GetType().FullName != typeName) continue;
                if (index-- == 0) return component;
            }
            return null;
        }

        #endregion

        #region Applied messages

        /// <summary>True if the message was applied before (log replay after a reload or rejoin).</summary>
        public static bool WasApplied(string mid) => !string.IsNullOrEmpty(mid) && Instance._applied.Contains(mid);

        /// <summary>Remembers an applied message id (the last few thousand are kept).</summary>
        public static void MarkApplied(string mid)
        {
            if (string.IsNullOrEmpty(mid)) return;
            var registry = Instance;
            if (!registry._applied.Add(mid)) return;
            registry._appliedMessages.Add(mid);
            if (registry._appliedMessages.Count > 8000)
            {
                foreach (var old in registry._appliedMessages.GetRange(0, 2000)) registry._applied.Remove(old);
                registry._appliedMessages.RemoveRange(0, 2000);
            }
        }

        /// <summary>Forgets that a message was applied (it'll be applied again on the next replay).</summary>
        public static void UnmarkApplied(string mid)
        {
            var registry = Instance;
            if (registry._applied.Remove(mid)) registry._appliedMessages.Remove(mid);
        }

        #endregion
    }

    /// <summary>
    /// Turns the instance ids in ObjectChangeEvents data into objects. EditorUtility.InstanceIDToObject became a compile
    /// error in newer Unity 6 versions (replaced by EntityId), and the event data's "instanceId" with it, so both are
    /// reached by reflection: this compiles in every version and uses whichever exists.
    /// </summary>
    internal static class ObjectLookup
    {
        private static readonly Dictionary<Type, MethodInfo> _converters = new();

        /// <summary>The object an ObjectChangeEvents data struct refers to (its instanceId/entityId), or null.</summary>
        public static Object FromEventData(object data, string member = "instanceId")
        {
            // Newer Unity 6 versions expose entityId (and may drop InstanceIDToObject): try that first, then instanceId.
            foreach (var name in new[] { member.Replace("instanceId", "entityId"), member })
            {
                var value = ReadMember(data, name);
                if (value == null) continue;
                var obj = ToObject(value);
                if (obj != null) return obj;
            }
            return null;
        }

        private static object ReadMember(object data, string name)
        {
            if (data == null) return null;
            var type = data.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var property = type.GetProperty(name, flags);
            if (property != null) try { return property.GetValue(data); } catch { return null; }
            var field = type.GetField(name, flags);
            if (field != null) try { return field.GetValue(data); } catch { return null; }
            return null;
        }

        private static Object ToObject(object id)
        {
            var type = id.GetType();
            if (!_converters.TryGetValue(type, out var method))
            {
                foreach (var candidate in typeof(EditorUtility).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (candidate.Name != "InstanceIDToObject" && candidate.Name != "EntityIdToObject") continue;
                    var parameters = candidate.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == type) { method = candidate; break; }
                }
                _converters[type] = method;
            }
            if (method == null) return null;
            try { return method.Invoke(null, new[] { id }) as Object; }
            catch { return null; }
        }
    }
}
