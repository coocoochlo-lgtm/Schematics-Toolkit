using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Turns serialized property values into short text and back, so a property change can travel as "path = value".
    /// Object references become portable: assets as "a:guid:localFileId", scene objects as "s:objectId" and scene
    /// components as "s:objectId#Type:index". (EditorJsonUtility writes references as local instance ids, which mean
    /// nothing on another machine, which is why everything goes through here.)
    /// Unsupported values (AnimationCurve, Gradient, [SerializeReference], references to unsaved non-scene objects
    /// such as a generated Mesh) are skipped; the component's EditorJsonUtility data carries [SerializeReference].
    /// </summary>
    public static class PropertyCodec
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Properties never sent: Unity-internal bookkeeping that differs per machine or can't be set.</summary>
        private static readonly HashSet<string> Skipped = new()
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Script", "m_Father", "m_Children", "m_Component", "m_EditorHideFlags", "m_EditorClassIdentifier",
            "m_PrefabInternal", "m_PrefabParentObject", "m_RootOrder",
        };

        /// <summary>Asset GUIDs that references pointed at but that don't exist locally (filled while decoding).</summary>
        public static readonly HashSet<string> MissingGuids = new();

        /// <summary>True for property paths that must not be synced.</summary>
        public static bool IsSkipped(string path)
        {
            int dot = path.IndexOf('.');
            return Skipped.Contains(dot < 0 ? path : path.Substring(0, dot));
        }

        #region Dump / apply

        /// <summary>
        /// Every supported property of an object (or only prefab overrides). Used for snapshots and as the fallback
        /// when a change didn't come with property detail.
        /// </summary>
        public static List<PropValue> Dump(Object target, bool onlyOverrides, int limit = 20000)
        {
            var list = new List<PropValue>();
            if (target == null) return list;
            using var so = new SerializedObject(target);
            var it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter) && list.Count < limit)
            {
                enter = false;
                if (it.depth == 0 && Skipped.Contains(it.name)) continue;
                if (it.propertyType == SerializedPropertyType.Generic || (it.isArray && it.propertyType != SerializedPropertyType.String))
                {
                    enter = true;
                    continue;
                }
                if (onlyOverrides && !it.prefabOverride) continue;
                if (TryEncode(it, out var value)) list.Add(new PropValue(it.propertyPath, value));
            }
            return list;
        }

        /// <summary>The current values of specific property paths of an object (paths that no longer exist are skipped).</summary>
        public static List<PropValue> Read(Object target, IEnumerable<string> paths)
        {
            var list = new List<PropValue>();
            if (target == null) return list;
            using var so = new SerializedObject(target);
            foreach (var path in paths)
            {
                if (IsSkipped(path)) continue;
                var prop = so.FindProperty(path);
                if (prop != null && TryEncode(prop, out var value)) list.Add(new PropValue(path, value));
            }
            return list;
        }

        /// <summary>Applies property values to an object. Returns true if anything was applied.</summary>
        public static bool Apply(Object target, List<PropValue> props, bool recordUndo)
        {
            if (target == null || props == null || props.Count == 0) return false;
            using var so = new SerializedObject(target);
            so.Update();
            foreach (var pv in props)
            {
                if (pv == null || string.IsNullOrEmpty(pv.p) || IsSkipped(pv.p)) continue;
                var prop = so.FindProperty(pv.p);
                if (prop == null) continue;
                try { Decode(prop, pv.v); }
                catch (Exception e) { CollabLog.Warning($"Skipped {target.GetType().Name}.{pv.p}: its value '{Short(pv.v)}' couldn't be applied ({e.Message})."); }
            }
            bool applied = recordUndo ? so.ApplyModifiedProperties() : so.ApplyModifiedPropertiesWithoutUndo();
            // Rotations often arrive one axis at a time ("m_LocalRotation.x"...): make sure the result is a valid one.
            if (applied && target is Transform transform)
            {
                var rotation = transform.localRotation;
                var safe = CollabFeatures.SafeRotation(rotation);
                if (safe.x != rotation.x || safe.y != rotation.y || safe.z != rotation.z || safe.w != rotation.w) transform.localRotation = safe;
            }
            return applied;
        }

        #endregion

        #region Values

        /// <summary>Encodes a leaf property's value; false for unsupported types.</summary>
        public static bool TryEncode(SerializedProperty p, out string value)
        {
            value = null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: value = p.longValue.ToString(Inv); return true;
                case SerializedPropertyType.Boolean: value = p.boolValue ? "1" : "0"; return true;
                case SerializedPropertyType.Float: value = p.doubleValue.ToString("R", Inv); return true;
                case SerializedPropertyType.String: value = p.stringValue ?? ""; return true;
                case SerializedPropertyType.Color: value = F(p.colorValue.r, p.colorValue.g, p.colorValue.b, p.colorValue.a); return true;
                case SerializedPropertyType.ObjectReference: value = EncodeReference(p.objectReferenceValue); return value != null;
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character: value = p.intValue.ToString(Inv); return true;
                case SerializedPropertyType.Vector2: { var v = p.vector2Value; value = F(v.x, v.y); return true; }
                case SerializedPropertyType.Vector3: { var v = p.vector3Value; value = F(v.x, v.y, v.z); return true; }
                case SerializedPropertyType.Vector4: { var v = p.vector4Value; value = F(v.x, v.y, v.z, v.w); return true; }
                case SerializedPropertyType.Quaternion: { var v = p.quaternionValue; value = F(v.x, v.y, v.z, v.w); return true; }
                case SerializedPropertyType.Rect: { var v = p.rectValue; value = F(v.x, v.y, v.width, v.height); return true; }
                case SerializedPropertyType.Bounds: { var v = p.boundsValue; value = F(v.center.x, v.center.y, v.center.z, v.size.x, v.size.y, v.size.z); return true; }
                case SerializedPropertyType.Vector2Int: { var v = p.vector2IntValue; value = I(v.x, v.y); return true; }
                case SerializedPropertyType.Vector3Int: { var v = p.vector3IntValue; value = I(v.x, v.y, v.z); return true; }
                case SerializedPropertyType.RectInt: { var v = p.rectIntValue; value = I(v.x, v.y, v.width, v.height); return true; }
                case SerializedPropertyType.BoundsInt: { var v = p.boundsIntValue; value = I(v.position.x, v.position.y, v.position.z, v.size.x, v.size.y, v.size.z); return true; }
                case SerializedPropertyType.Hash128: value = p.hash128Value.ToString(); return true;
                default: return false;
            }
        }

        /// <summary>Sets a property from an encoded value.</summary>
        public static void Decode(SerializedProperty p, string v)
        {
            v ??= "";
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.longValue = long.Parse(v, Inv); break;
                case SerializedPropertyType.Boolean: p.boolValue = v == "1"; break;
                case SerializedPropertyType.Float: p.doubleValue = double.Parse(v, Inv); break;
                case SerializedPropertyType.String: p.stringValue = v; break;
                case SerializedPropertyType.Color: { var f = Fs(v, 4); p.colorValue = new Color(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.ObjectReference: p.objectReferenceValue = DecodeReference(v); break;
                case SerializedPropertyType.ArraySize: p.arraySize = int.Parse(v, Inv); break;
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.Character: p.intValue = int.Parse(v, Inv); break;
                case SerializedPropertyType.Vector2: { var f = Fs(v, 2); p.vector2Value = new Vector2(f[0], f[1]); break; }
                case SerializedPropertyType.Vector3: { var f = Fs(v, 3); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
                case SerializedPropertyType.Vector4: { var f = Fs(v, 4); p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.Quaternion: { var f = Fs(v, 4); p.quaternionValue = CollabFeatures.SafeRotation(new Quaternion(f[0], f[1], f[2], f[3])); break; }
                case SerializedPropertyType.Rect: { var f = Fs(v, 4); p.rectValue = new Rect(f[0], f[1], f[2], f[3]); break; }
                case SerializedPropertyType.Bounds: { var f = Fs(v, 6); p.boundsValue = new Bounds(new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5])); break; }
                case SerializedPropertyType.Vector2Int: { var n = Is(v, 2); p.vector2IntValue = new Vector2Int(n[0], n[1]); break; }
                case SerializedPropertyType.Vector3Int: { var n = Is(v, 3); p.vector3IntValue = new Vector3Int(n[0], n[1], n[2]); break; }
                case SerializedPropertyType.RectInt: { var n = Is(v, 4); p.rectIntValue = new RectInt(n[0], n[1], n[2], n[3]); break; }
                case SerializedPropertyType.BoundsInt: { var n = Is(v, 6); p.boundsIntValue = new BoundsInt(n[0], n[1], n[2], n[3], n[4], n[5]); break; }
                case SerializedPropertyType.Hash128: p.hash128Value = Hash128.Parse(v); break;
            }
        }

        private static string Short(string v) => v == null ? "" : v.Length > 40 ? v.Substring(0, 40) + "…" : v;

        private static string F(params float[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = values[i].ToString("R", Inv);
            return string.Join(",", parts);
        }

        private static string I(params int[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = values[i].ToString(Inv);
            return string.Join(",", parts);
        }

        private static float[] Fs(string v, int count)
        {
            var parts = v.Split(',');
            var result = new float[count];
            for (int i = 0; i < count && i < parts.Length; i++) result[i] = float.Parse(parts[i], Inv);
            return result;
        }

        private static int[] Is(string v, int count)
        {
            var parts = v.Split(',');
            var result = new int[count];
            for (int i = 0; i < count && i < parts.Length; i++) result[i] = int.Parse(parts[i], Inv);
            return result;
        }

        #endregion

        #region References

        private static readonly Dictionary<string, Object> _assetCache = new();

        /// <summary>A portable reference: "" for none, null if it can't be made portable (then the property is skipped).</summary>
        public static string EncodeReference(Object obj)
        {
            if (obj == null) return "";
            if (EditorUtility.IsPersistent(obj))
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId))
                    return "a:" + guid + ":" + localId.ToString(Inv);
                return null;
            }
            GameObject go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (go == null) return null;
            var id = CollabObjectIds.GetId(go);
            if (id == null) return null;
            return obj is Component component ? "s:" + id + "#" + CollabObjectIds.ComponentKey(component) : "s:" + id;
        }

        /// <summary>The object a portable reference names, or null (missing assets are added to <see cref="MissingGuids"/>).</summary>
        public static Object DecodeReference(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value.StartsWith("a:"))
            {
                if (_assetCache.TryGetValue(value, out var cached) && cached != null) return cached;
                var parts = value.Split(':');
                if (parts.Length < 3) return null;
                string guid = parts[1];
                long.TryParse(parts[2], NumberStyles.Integer, Inv, out long localId);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    MissingGuids.Add(guid);
                    return null;
                }
                Object found = null;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null) continue;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id) && id == localId)
                    {
                        found = asset;
                        break;
                    }
                }
                found ??= AssetDatabase.LoadMainAssetAtPath(path);
                if (found == null) MissingGuids.Add(guid);
                else _assetCache[value] = found;
                return found;
            }
            if (value.StartsWith("s:"))
            {
                string rest = value.Substring(2);
                int hash = rest.IndexOf('#');
                var go = CollabObjectIds.Resolve(hash < 0 ? rest : rest.Substring(0, hash));
                if (go == null) return null;
                return hash < 0 ? go : CollabObjectIds.ResolveComponent(go, rest.Substring(hash + 1));
            }
            return null;
        }

        /// <summary>Forgets cached asset lookups (after a pull changed assets).</summary>
        public static void ClearCache() => _assetCache.Clear();

        #endregion
    }
}
