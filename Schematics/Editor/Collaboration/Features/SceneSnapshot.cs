using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Captures a GameObject hierarchy as a flat list of <see cref="SnapshotNode"/>s and rebuilds (or updates) it on
    /// another machine. Prefab instances travel as "prefab GUID + overridden properties", so a placed prefab costs a few
    /// hundred bytes and stays linked to its prefab on every peer. Missing prefabs become placeholders.
    /// </summary>
    public static class SceneSnapshot
    {
        #region Capture

        /// <summary>The hierarchy under (and including) the root, root first, parents before children.</summary>
        public static List<SnapshotNode> Capture(GameObject root)
        {
            var nodes = new List<SnapshotNode>();
            if (root == null) return nodes;
            Capture(root, -1, null, nodes);
            return nodes;
        }

        private static void Capture(GameObject go, int parentIndex, GameObject prefabContext, List<SnapshotNode> nodes)
        {
            if ((go.hideFlags & HideFlags.DontSaveInEditor) != 0) return;   // placeholders and other temporary objects

            var node = new SnapshotNode
            {
                id = CollabObjectIds.GetId(go),
                parent = parentIndex,
                sibling = go.transform.GetSiblingIndex(),
                name = go.name,
                tag = go.tag,
                layer = go.layer,
                active = go.activeSelf,
                staticFlags = (int)GameObjectUtility.GetStaticEditorFlags(go),
            };
            if (node.id == null) return;

            bool isPrefabRoot = PrefabUtility.IsOutermostPrefabInstanceRoot(go)
                || (PrefabUtility.IsAnyPrefabInstanceRoot(go) && PrefabUtility.IsAddedGameObjectOverride(go));
            bool fromPrefab = false;
            if (isPrefabRoot)
            {
                string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                node.prefabGuid = AssetDatabase.AssetPathToGUID(assetPath);
                prefabContext = go;
                fromPrefab = true;
                SetBounds(node, go);
            }
            else if (prefabContext != null && PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsAddedGameObjectOverride(go))
            {
                node.prefabPath = RelativePath(prefabContext.transform, go.transform);
                fromPrefab = true;
            }
            else
            {
                prefabContext = null;
                if (parentIndex < 0) SetBounds(node, go);
            }

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null) continue;   // missing script
                bool overridesOnly = fromPrefab && PrefabUtility.IsPartOfPrefabInstance(component) && !PrefabUtility.IsAddedComponentOverride(component);
                node.comps.Add(CaptureComponent(component, overridesOnly));
            }

            nodes.Add(node);
            int index = nodes.Count - 1;
            foreach (Transform child in go.transform)
                Capture(child.gameObject, index, prefabContext, nodes);
        }

        /// <summary>A component's data (all supported properties, or only prefab overrides).</summary>
        public static ComponentData CaptureComponent(Component component, bool overridesOnly)
        {
            var data = new ComponentData
            {
                type = component.GetType().AssemblyQualifiedName,
                key = CollabObjectIds.ComponentKey(component),
                props = PropertyCodec.Dump(component, overridesOnly),
            };
            if (!overridesOnly && HasManagedReferences(component))
                data.json = EditorJsonUtility.ToJson(component);
            return data;
        }

        private static bool HasManagedReferences(Object target)
        {
            if (!(target is MonoBehaviour)) return false;
            using var so = new SerializedObject(target);
            var it = so.GetIterator();
            while (it.Next(true))
                if (it.propertyType == SerializedPropertyType.ManagedReference)
                    return true;
            return false;
        }

        private static void SetBounds(SnapshotNode node, GameObject go)
        {
            var bounds = LocalBounds(go);
            node.boundsCenter = bounds.center;
            node.boundsSize = bounds.size;
        }

        /// <summary>Renderer bounds of a hierarchy in the root's local space, ignoring the root's own scale (1m cube if nothing renders).</summary>
        public static Bounds LocalBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            Bounds world = default;
            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                if (!any) { world = renderer.bounds; any = true; }
                else world.Encapsulate(renderer.bounds);
            }
            if (!any) return new Bounds(Vector3.zero, Vector3.one);
            var t = go.transform;
            var scale = t.lossyScale;
            Vector3 center = Quaternion.Inverse(t.rotation) * (world.center - t.position);
            Vector3 size = Quaternion.Inverse(t.rotation) * world.size;
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            return new Bounds(Div(center, scale), Div(size, scale));
        }

        private static Vector3 Div(Vector3 v, Vector3 s) => new(
            Mathf.Abs(s.x) > 1e-5f ? v.x / Mathf.Abs(s.x) : v.x,
            Mathf.Abs(s.y) > 1e-5f ? v.y / Mathf.Abs(s.y) : v.y,
            Mathf.Abs(s.z) > 1e-5f ? v.z / Mathf.Abs(s.z) : v.z);

        /// <summary>"Child/Grandchild" path from an ancestor to a descendant.</summary>
        public static string RelativePath(Transform ancestor, Transform descendant)
        {
            var parts = new List<string>();
            for (var t = descendant; t != null && t != ancestor; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        #endregion

        #region Apply

        /// <summary>Everything an apply touched, so the change events it causes aren't sent back out.</summary>
        public sealed class ApplyResult
        {
            public GameObject Root;
            public readonly List<GameObject> Touched = new();
        }

        /// <summary>
        /// Creates the hierarchy, or brings existing objects (matched by id) up to date. Missing prefabs become
        /// placeholders that remember the snapshot and turn into the real thing when the asset arrives.
        /// </summary>
        public static ApplyResult Apply(List<SnapshotNode> nodes, Scene scene, GameObject parent, int sibling, CollabPeer from, bool recordUndo)
        {
            var result = new ApplyResult();
            if (nodes == null || nodes.Count == 0) return result;
            var objects = new GameObject[nodes.Count];
            var prefabRootOf = new int[nodes.Count];
            var skip = new bool[nodes.Count];

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                int parentIndex = node.parent;
                prefabRootOf[i] = !string.IsNullOrEmpty(node.prefabGuid) ? i
                    : (!string.IsNullOrEmpty(node.prefabPath) && parentIndex >= 0 ? prefabRootOf[parentIndex] : -1);
                if (parentIndex >= 0 && skip[parentIndex] && !string.IsNullOrEmpty(node.prefabPath)) { skip[i] = true; continue; }

                // Nearest ancestor that exists here (an object added under a placeholder's not-yet-built child attaches to the placeholder).
                GameObject parentGo = parent;
                for (int a = parentIndex; a >= 0; a = nodes[a].parent)
                    if (objects[a] != null) { parentGo = objects[a]; break; }
                GameObject go = CollabObjectIds.Resolve(node.id);
                bool created = false;

                if (go == null && !string.IsNullOrEmpty(node.prefabPath))
                {
                    // Part of a prefab instance created above: find it inside that instance.
                    int rootIndex = prefabRootOf[i];
                    var root = rootIndex >= 0 ? objects[rootIndex] : null;
                    if (root != null && !CollabPlaceholders.IsPlaceholder(root))
                        go = root.transform.Find(node.prefabPath)?.gameObject;
                    if (go == null) { skip[i] = true; continue; }
                }

                if (go == null && !string.IsNullOrEmpty(node.prefabGuid))
                {
                    var prefab = LoadPrefab(node.prefabGuid);
                    if (prefab != null)
                    {
                        go = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                        created = go != null;
                    }
                    if (go == null)
                    {
                        go = CollabPlaceholders.Create(nodes, i, scene, from);
                        created = true;
                        // Its prefab's own children are part of the stored snapshot; added objects still attach to it.
                    }
                }

                if (go == null)
                {
                    go = new GameObject(node.name);
                    if (parentGo == null && scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
                    created = true;
                }

                if (created && recordUndo) Undo.RegisterCreatedObjectUndo(go, "Remote: create " + node.name);
                CollabObjectIds.Register(go, node.id);
                objects[i] = go;
                result.Touched.Add(go);

                // Hierarchy placement (prefab-internal objects stay where the prefab puts them).
                if (string.IsNullOrEmpty(node.prefabPath))
                {
                    var parentTransform = parentGo != null ? parentGo.transform : null;
                    if (go.transform.parent != parentTransform)
                    {
                        if (recordUndo) Undo.SetTransformParent(go.transform, parentTransform, "Remote: parent");
                        else go.transform.SetParent(parentTransform, false);
                    }
                    if (parentTransform == null && scene.IsValid() && go.scene != scene)
                        SceneManager.MoveGameObjectToScene(go, scene);
                    int index = i == 0 ? sibling : node.sibling;
                    if (index >= 0) go.transform.SetSiblingIndex(index);
                    CollabObjectIds.UpdateScene(go);
                }

                bool placeholder = CollabPlaceholders.IsPlaceholder(go);
                if (!placeholder)
                {
                    ApplyGameObjectFields(go, node);
                    ApplyComponents(go, node, recordUndo);
                }
                else
                {
                    go.name = CollabPlaceholders.PlaceholderName(node.name);
                    var transformData = node.comps.Find(c => c.key.StartsWith("UnityEngine.Transform:") || c.key.StartsWith("UnityEngine.RectTransform:"));
                    if (transformData != null) PropertyCodec.Apply(go.transform, transformData.props, false);
                    CollabPlaceholders.UpdateSnapshot(go, nodes, i);
                }
            }

            result.Root = objects[0];
            if (result.Root == null) result.Root = CollabObjectIds.Resolve(nodes[0].id);
            return result;
        }

        private static GameObject LoadPrefab(string guid)
        {
            if (CollabPlaceholders.IsAssetPending(guid)) return null;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void ApplyGameObjectFields(GameObject go, SnapshotNode node)
        {
            if (go.name != node.name) go.name = node.name;
            if (!string.IsNullOrEmpty(node.tag) && go.tag != node.tag)
            {
                try { go.tag = node.tag; } catch (UnityException) { /* tag not defined here */ }
            }
            if (go.layer != node.layer) go.layer = node.layer;
            if (go.activeSelf != node.active) go.SetActive(node.active);
            var flags = (StaticEditorFlags)node.staticFlags;
            if (GameObjectUtility.GetStaticEditorFlags(go) != flags) GameObjectUtility.SetStaticEditorFlags(go, flags);
        }

        /// <summary>Adds missing components and applies data; removes components the sender no longer has.</summary>
        public static void ApplyComponents(GameObject go, SnapshotNode node, bool recordUndo)
        {
            var keep = new HashSet<Component>();
            foreach (var data in node.comps)
            {
                var component = CollabObjectIds.ResolveComponent(go, data.key);
                if (component == null)
                {
                    var type = Type.GetType(data.type);
                    if (type == null || !typeof(Component).IsAssignableFrom(type)) continue;
                    if (type == typeof(Transform)) component = go.transform;
                    else
                    {
                        try { component = recordUndo ? Undo.AddComponent(go, type) : go.AddComponent(type); }
                        catch (Exception) { component = null; }
                    }
                    if (component == null) continue;
                }
                keep.Add(component);
                ApplyComponentData(component, data, recordUndo);
            }

            foreach (var component in go.GetComponents<Component>())
            {
                if (component == null || component is Transform || keep.Contains(component)) continue;
                // Components that come from a prefab are left alone (removing them is a prefab override we don't sync).
                if (PrefabUtility.IsPartOfPrefabInstance(component) && !PrefabUtility.IsAddedComponentOverride(component)) continue;
                if (recordUndo) Undo.DestroyObjectImmediate(component);
                else Object.DestroyImmediate(component);
            }
        }

        /// <summary>Applies a component's JSON (if any) then its property values.</summary>
        public static void ApplyComponentData(Component component, ComponentData data, bool recordUndo)
        {
            if (!string.IsNullOrEmpty(data.json))
            {
                if (recordUndo) Undo.RecordObject(component, "Remote edit");
                try { EditorJsonUtility.FromJsonOverwrite(data.json, component); }
                catch (Exception e) { CollabLog.Warning("Couldn't apply component data: " + e.Message); }
            }
            PropertyCodec.Apply(component, data.props, recordUndo);
        }

        /// <summary>The part of a snapshot rooted at one node, re-indexed so that node is index 0.</summary>
        public static List<SnapshotNode> Subtree(List<SnapshotNode> nodes, int rootIndex)
        {
            var map = new Dictionary<int, int>();
            var result = new List<SnapshotNode>();
            for (int i = rootIndex; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (i != rootIndex && !map.ContainsKey(node.parent)) continue;
                var copy = JsonUtility.FromJson<SnapshotNode>(JsonUtility.ToJson(node));
                copy.parent = i == rootIndex ? -1 : map[node.parent];
                map[i] = result.Count;
                result.Add(copy);
            }
            return result;
        }

        #endregion
    }
}
