using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Editors' scene edits, applied to the running game while someone playtests. When an editor moves an object or
    /// changes a value in the Inspector, the change already goes out on the "scene" channel for the other editors; a
    /// playtester who switched on <see cref="Enabled"/> ("Receive live edits" in the overlay) sees it happen in the game
    /// too, without rebuilding.
    ///
    /// What's mirrored (best effort, documented in Schematics/Editor/Collaboration/Features/README.md):
    /// - GameObject: active state, name, layer.
    /// - Transform: local position, rotation and scale (rotations are validated and normalized first).
    /// - enabled on Behaviours, Renderers and Colliders.
    /// - Serialized fields of components found by reflection (public or [SerializeField], nested structs and arrays/lists
    ///   of them): numbers, bools, strings, enums, vectors, quaternions, colours, rects. Built-in components' "m_Speed"
    ///   style properties are tried as their C# property ("speed").
    /// Not mirrored: creating, deleting or re-parenting objects, object references, and objects created after the build.
    /// Edits apply to the objects as they are now: if the game moved something, an edit to its position moves it again.
    ///
    /// Nothing here ever throws into the game loop: every message and every change is guarded and problems are logged
    /// once (see <see cref="CollabLog"/>).
    /// </summary>
    public static class CollabLiveEdits
    {
        public const string Channel = "scene";
        private const string PrefKey = "SchematicsCollab.ReceiveLiveEdits";

        private static bool _initialized;
        private static bool? _enabled;
        private static readonly HashSet<string> _applied = new HashSet<string>();
        private static readonly Queue<string> _appliedOrder = new Queue<string>();

        /// <summary>Apply editors' live edits to this game. Off by default; remembered per machine.</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled == null)
                {
                    bool fallback = CollabRuntime.BuildInfo != null && CollabRuntime.BuildInfo.ReceiveLiveEditsByDefault;
                    try { _enabled = PlayerPrefs.GetInt(PrefKey, fallback ? 1 : 0) == 1; }
                    catch { _enabled = fallback; }
                }
                return _enabled.Value;
            }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                try { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); } catch { /* not persisted: fine */ }
                try { EnabledChanged?.Invoke(value); }
                catch (Exception e) { CollabLog.Exception("A CollabLiveEdits.EnabledChanged handler threw", e); }
            }
        }

        public static event Action<bool> EnabledChanged;

        /// <summary>Raised after an editor's change was applied: (object, the editor's peer id).</summary>
        public static event Action<GameObject, string> Applied;

        /// <summary>Changes applied, and changes that named something this game doesn't have (or can't set).</summary>
        public static int AppliedCount { get; private set; }
        public static int SkippedCount { get; private set; }

        /// <summary>The last applied change, for the overlay ("Alex moved Door").</summary>
        public static string LastSummary { get; private set; }

        internal static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            CollabRuntime.Subscribe(Channel, OnSceneMessage);
        }

        private static void OnSceneMessage(string fromPeerId, string json)
        {
            if (!Application.isPlaying || !Enabled || string.IsNullOrEmpty(json)) return;
            try
            {
                var msg = JsonUtility.FromJson<LiveEditMsg>(json);
                if (msg == null || (msg.mid != null && !Remember(msg.mid))) return;
                Apply(msg, fromPeerId);
            }
            catch (Exception e) { CollabLog.Exception("Applying a live edit failed", e); }
        }

        // Message ids already applied (a message can arrive twice when the host changes).
        private static bool Remember(string mid)
        {
            if (!_applied.Add(mid)) return false;
            _appliedOrder.Enqueue(mid);
            while (_appliedOrder.Count > 4000) _applied.Remove(_appliedOrder.Dequeue());
            return true;
        }

        private static void Apply(LiveEditMsg msg, string fromPeerId)
        {
            var changes = CollabLiveEditCodec.Changes(msg);
            if (changes.Count == 0) return;
            var go = Resolve(msg.id);
            if (go == null)
            {
                SkippedCount += changes.Count;
                return;
            }

            // Transform parts are gathered so a rotation edited one axis at a time is still normalized as a whole.
            var transform = go.transform;
            Vector3? position = null, scale = null;
            Vector4? rotation = null;   // raw x, y, z, w until all parts are in
            int applied = 0;
            foreach (var change in changes)
            {
                try
                {
                    bool ok;
                    switch (change.Target)
                    {
                        case LiveEditTarget.LocalPosition:
                            ok = Vector(change, position ?? transform.localPosition, out var p);
                            if (ok) position = p;
                            break;
                        case LiveEditTarget.LocalScale:
                            ok = Vector(change, scale ?? transform.localScale, out var s);
                            if (ok) scale = s;
                            break;
                        case LiveEditTarget.LocalRotation:
                            ok = Rotation(change, rotation ?? Raw(transform.localRotation), out var r);
                            if (ok) rotation = r;
                            break;
                        case LiveEditTarget.GameObjectActive:
                            ok = CollabLiveEditCodec.TryBool(change.Value, out bool active);
                            if (ok && go.activeSelf != active) go.SetActive(active);
                            break;
                        case LiveEditTarget.GameObjectName:
                            go.name = change.Value;
                            ok = true;
                            break;
                        case LiveEditTarget.GameObjectLayer:
                            ok = CollabLiveEditCodec.TryLong(change.Value, out long layer) && layer >= 0 && layer < 32;
                            if (ok) go.layer = (int)layer;
                            break;
                        case LiveEditTarget.ComponentEnabled:
                            ok = SetEnabled(ResolveComponent(go, change.Component), change.Value);
                            break;
                        default:
                            ok = FieldSetter.Set(ResolveComponent(go, change.Component), change.Path, change.Value);
                            break;
                    }
                    if (ok) applied++;
                    else SkippedCount++;
                }
                catch (Exception e)
                {
                    SkippedCount++;
                    CollabLog.Exception($"Applying a live edit to {go.name} ({change.Path}) failed", e);
                }
            }
            if (position.HasValue) transform.localPosition = position.Value;
            if (rotation.HasValue)
            {
                // Normalized once, after every part arrived (normalizing per axis would skew the others).
                var q = rotation.Value;
                float x = q.x, y = q.y, z = q.z, w = q.w;
                CollabLiveEditCodec.Normalize(ref x, ref y, ref z, ref w);
                transform.localRotation = new Quaternion(x, y, z, w);
            }
            if (scale.HasValue) transform.localScale = scale.Value;
            if (applied == 0) return;

            AppliedCount += applied;
            var who = CollabRuntime.FindPeer(fromPeerId)?.Name ?? "An editor";
            LastSummary = $"{who} changed {go.name}";
            try { Applied?.Invoke(go, fromPeerId); }
            catch (Exception e) { CollabLog.Exception("A CollabLiveEdits.Applied handler threw", e); }
        }

        /// <summary>The live object an editor id names: the build's baked ids, or (in Play Mode) the editor's lookup.</summary>
        public static GameObject Resolve(string id)
        {
            var go = CollabSceneIds.Find(id);
            if (go != null) return go;
            var resolver = CollabRuntime.EditorObjectResolver;
            if (resolver == null) return null;
            try { return resolver(id); }
            catch (Exception e)
            {
                CollabLog.Exception("Finding the Play Mode object for a live edit failed", e);
                return null;
            }
        }

        /// <summary>"TypeFullName:index" (the editors' component key) on a GameObject, or null.</summary>
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

        private static bool Vector(LiveEditChange change, Vector3 current, out Vector3 result)
        {
            result = current;
            if (change.Axis >= 0)
            {
                if (change.Axis > 2 || !CollabLiveEditCodec.TryDouble(change.Value, out double v)) return false;
                result[change.Axis] = (float)v;
                return true;
            }
            if (!CollabLiveEditCodec.TryFloats(change.Value, 3, out var f)) return false;
            result = new Vector3(f[0], f[1], f[2]);
            return true;
        }

        private static Vector4 Raw(Quaternion q) => new Vector4(q.x, q.y, q.z, q.w);

        private static bool Rotation(LiveEditChange change, Vector4 current, out Vector4 result)
        {
            result = current;
            if (change.Axis >= 0)
            {
                if (!CollabLiveEditCodec.TryDouble(change.Value, out double v)) return false;
                result[Mathf.Min(change.Axis, 3)] = (float)v;
                return true;
            }
            if (!CollabLiveEditCodec.TryFloats(change.Value, 4, out var f)) return false;
            result = new Vector4(f[0], f[1], f[2], f[3]);
            return true;
        }

        private static bool SetEnabled(Component component, string value)
        {
            if (component == null || !CollabLiveEditCodec.TryBool(value, out bool enabled)) return false;
            switch (component)
            {
                case Behaviour behaviour: behaviour.enabled = enabled; return true;
                case Renderer renderer: renderer.enabled = enabled; return true;
                case Collider collider: collider.enabled = enabled; return true;
                default:
                    var property = component.GetType().GetProperty("enabled", BindingFlags.Public | BindingFlags.Instance);
                    if (property == null || !property.CanWrite || property.PropertyType != typeof(bool)) return false;
                    property.SetValue(component, enabled);
                    return true;
            }
        }

        /// <summary>Sets a serialized property path ("speed", "offset.x", "points.Array.data[2].y") on a component by reflection.</summary>
        private static class FieldSetter
        {
            private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            private static readonly Dictionary<(Type, string), MemberInfo> _members = new Dictionary<(Type, string), MemberInfo>();

            public static bool Set(Component component, string path, string value)
            {
                if (component == null || string.IsNullOrEmpty(path)) return false;
                var segments = Segments(path);
                if (segments.Count == 0) return false;
                object boxed = component;
                if (!SetIn(ref boxed, segments, 0, value)) return false;
                return true;
            }

            // "points.Array.data[2].y" -> ["points", "[2]", "y"]
            private static List<string> Segments(string path)
            {
                var list = new List<string>();
                var parts = path.Split('.');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data[", StringComparison.Ordinal))
                    {
                        list.Add(parts[i + 1].Substring(4));   // "[2]"
                        i++;
                        continue;
                    }
                    if (parts[i] == "Array" && i + 1 < parts.Length && parts[i + 1] == "size") return new List<string>();   // resizing: not mirrored
                    list.Add(parts[i]);
                }
                return list;
            }

            // Sets the value at segments[index..] inside container (boxed structs are written back by the caller).
            private static bool SetIn(ref object container, List<string> segments, int index, string value)
            {
                if (container == null) return false;
                string segment = segments[index];
                bool last = index == segments.Count - 1;

                if (segment.StartsWith("[", StringComparison.Ordinal))
                {
                    if (!int.TryParse(segment.Trim('[', ']'), out int element)) return false;
                    if (container is Array array)
                    {
                        if (element < 0 || element >= array.Length) return false;
                        object item = array.GetValue(element);
                        if (!Assign(ref item, array.GetType().GetElementType(), segments, index, last, value)) return false;
                        array.SetValue(item, element);
                        return true;
                    }
                    if (container is System.Collections.IList list)
                    {
                        if (element < 0 || element >= list.Count) return false;
                        object item = list[element];
                        var itemType = container.GetType().IsGenericType ? container.GetType().GetGenericArguments()[0] : typeof(object);
                        if (!Assign(ref item, itemType, segments, index, last, value)) return false;
                        list[element] = item;
                        return true;
                    }
                    return false;
                }

                var member = Member(container.GetType(), segment);
                if (member == null) return false;
                Type type = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
                object current = member is FieldInfo fi ? fi.GetValue(container) : ((PropertyInfo)member).GetValue(container);
                if (!Assign(ref current, type, segments, index, last, value)) return false;
                if (member is FieldInfo field)
                {
                    if (field.IsInitOnly || field.IsLiteral) return false;
                    field.SetValue(container, current);
                }
                else
                {
                    var property = (PropertyInfo)member;
                    if (!property.CanWrite) return false;
                    property.SetValue(container, current);
                }
                return true;
            }

            private static bool Assign(ref object current, Type type, List<string> segments, int index, bool last, string value)
            {
                if (last)
                {
                    if (!Convert(value, type, out var converted)) return false;
                    current = converted;
                    return true;
                }
                // Go deeper (structs are copied out, changed and written back by SetIn's caller).
                if (current == null) return false;
                return SetIn(ref current, segments, index + 1, value);
            }

            private static MemberInfo Member(Type type, string name)
            {
                if (_members.TryGetValue((type, name), out var cached)) return cached;
                MemberInfo found = null;
                for (var t = type; t != null && found == null; t = t.BaseType)
                {
                    var field = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
                    if (field != null && (field.IsPublic || field.IsDefined(typeof(SerializeField), true))) found = field;
                }
                if (found == null)
                {
                    // Built-in components serialize "m_Intensity", "m_Color"...: try the C# property "intensity", "color".
                    string propertyName = name.StartsWith("m_", StringComparison.Ordinal) && name.Length > 2
                        ? char.ToLowerInvariant(name[2]) + name.Substring(3)
                        : name;
                    var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (property != null && property.GetIndexParameters().Length == 0) found = property;
                }
                _members[(type, name)] = found;
                return found;
            }

            private static bool Convert(string v, Type type, out object result)
            {
                result = null;
                if (type == typeof(string)) { result = v ?? ""; return true; }
                if (type == typeof(bool)) { bool ok = CollabLiveEditCodec.TryBool(v, out bool b); result = b; return ok; }
                if (type == typeof(float)) { bool ok = CollabLiveEditCodec.TryDouble(v, out double d); result = (float)d; return ok; }
                if (type == typeof(double)) { bool ok = CollabLiveEditCodec.TryDouble(v, out double d); result = d; return ok; }
                if (type.IsEnum)
                {
                    if (!CollabLiveEditCodec.TryLong(v, out long e)) return false;
                    result = Enum.ToObject(type, e);
                    return true;
                }
                if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                    || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte) || type == typeof(char))
                {
                    if (!CollabLiveEditCodec.TryLong(v, out long n)) return false;
                    try { result = System.Convert.ChangeType(n, type, System.Globalization.CultureInfo.InvariantCulture); }
                    catch { return false; }
                    return true;
                }
                if (type == typeof(Vector2)) { if (!CollabLiveEditCodec.TryFloats(v, 2, out var f)) return false; result = new Vector2(f[0], f[1]); return true; }
                if (type == typeof(Vector3)) { if (!CollabLiveEditCodec.TryFloats(v, 3, out var f)) return false; result = new Vector3(f[0], f[1], f[2]); return true; }
                if (type == typeof(Vector4)) { if (!CollabLiveEditCodec.TryFloats(v, 4, out var f)) return false; result = new Vector4(f[0], f[1], f[2], f[3]); return true; }
                if (type == typeof(Quaternion))
                {
                    if (!CollabLiveEditCodec.TryFloats(v, 4, out var f)) return false;
                    CollabLiveEditCodec.Normalize(ref f[0], ref f[1], ref f[2], ref f[3]);
                    result = new Quaternion(f[0], f[1], f[2], f[3]);
                    return true;
                }
                if (type == typeof(Color)) { if (!CollabLiveEditCodec.TryFloats(v, 4, out var f)) return false; result = new Color(f[0], f[1], f[2], f[3]); return true; }
                if (type == typeof(Color32))
                {
                    if (!CollabLiveEditCodec.TryFloats(v, 4, out var f)) return false;
                    result = (Color32)new Color(f[0], f[1], f[2], f[3]);
                    return true;
                }
                if (type == typeof(Rect)) { if (!CollabLiveEditCodec.TryFloats(v, 4, out var f)) return false; result = new Rect(f[0], f[1], f[2], f[3]); return true; }
                if (type == typeof(Vector2Int)) { if (!Ints(v, 2, out var n)) return false; result = new Vector2Int(n[0], n[1]); return true; }
                if (type == typeof(Vector3Int)) { if (!Ints(v, 3, out var n)) return false; result = new Vector3Int(n[0], n[1], n[2]); return true; }
                return false;   // object references, curves, gradients...: not mirrored
            }

            private static bool Ints(string v, int count, out int[] values)
            {
                values = new int[count];
                var parts = (v ?? "").Split(',');
                for (int i = 0; i < count && i < parts.Length; i++)
                    if (!int.TryParse(parts[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out values[i])) return false;
                return parts.Length > 0;
            }
        }
    }
}
