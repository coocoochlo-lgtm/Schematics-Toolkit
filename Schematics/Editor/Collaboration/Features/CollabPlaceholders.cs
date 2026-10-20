using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Stand-ins for objects whose asset a teammate just made and that git hasn't delivered here yet. A placeholder is
    /// an empty GameObject carrying the placed object's transform, with a translucent box child the size of the real
    /// object, in the placer's colour, labelled with the asset's name. Nothing about it is ever saved (HideFlags.DontSave).
    /// When <see cref="CollabGit.Pulled"/> brings the asset (or the project otherwise gains it), the real object is built
    /// from the remembered snapshot at the placeholder's current transform, and the placeholder disappears.
    /// </summary>
    [InitializeOnLoad]
    public static class CollabPlaceholders
    {
        private const string VisualName = "__CollabPlaceholderVisual";
        private static readonly Dictionary<string, Bounds> _announcedBounds = new();
        private static readonly Dictionary<Color, Material> _materials = new();
        private static bool _resolveQueued;

        static CollabPlaceholders()
        {
            CollabGit.AssetAnnounced += (guid, bounds) => _announcedBounds[guid] = bounds;
            CollabGit.Pulled += _ => QueueResolve();
            EditorApplication.projectChanged += QueueResolve;
            SceneView.duringSceneGui += DrawLabels;
        }

        /// <summary>True if the GameObject is a placeholder.</summary>
        public static bool IsPlaceholder(GameObject go) => go != null && go.GetComponent<CollabPlaceholder>() != null;

        /// <summary>True if the asset was announced by a teammate but hasn't been pulled yet.</summary>
        public static bool IsAssetPending(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            try { return CollabGit.IsPending(guid); }
            catch { return false; }
        }

        /// <summary>"Name (arriving…)".</summary>
        public static string PlaceholderName(string name) => name + " (arriving…)";

        /// <summary>Creates a placeholder for snapshot node <paramref name="index"/> (a prefab instance root).</summary>
        public static GameObject Create(List<SnapshotNode> nodes, int index, Scene scene, CollabPeer from)
        {
            var node = nodes[index];
            var color = from?.Color ?? Color.cyan;
            string assetPath = AssetDatabase.GUIDToAssetPath(node.prefabGuid);
            string assetName = string.IsNullOrEmpty(assetPath) ? node.name : Path.GetFileNameWithoutExtension(assetPath);

            var root = new GameObject(PlaceholderName(node.name)) { hideFlags = HideFlags.DontSave };
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(root, scene);
            var marker = root.AddComponent<CollabPlaceholder>();
            marker.hideFlags = HideFlags.DontSave;
            marker.AssetGuid = node.prefabGuid;
            marker.AssetName = assetName;
            marker.FromPeerId = from?.Id;
            marker.Color = color;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = VisualName;
            visual.hideFlags = HideFlags.DontSave;
            var collider = visual.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            visual.transform.SetParent(root.transform, false);

            var bounds = new Bounds(node.boundsCenter, node.boundsSize);
            if (bounds.size.sqrMagnitude < 1e-6f && !_announcedBounds.TryGetValue(node.prefabGuid, out bounds))
                bounds = new Bounds(Vector3.zero, Vector3.one);
            visual.transform.localPosition = bounds.center;
            visual.transform.localScale = new Vector3(Mathf.Max(bounds.size.x, 0.05f), Mathf.Max(bounds.size.y, 0.05f), Mathf.Max(bounds.size.z, 0.05f));
            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = MaterialFor(color);

            UpdateSnapshot(root, nodes, index);
            return root;
        }

        /// <summary>Remembers (the latest) snapshot of the placed object.</summary>
        public static void UpdateSnapshot(GameObject placeholder, List<SnapshotNode> nodes, int index)
        {
            var marker = placeholder != null ? placeholder.GetComponent<CollabPlaceholder>() : null;
            if (marker == null) return;
            var msg = new SceneMsg { op = "create", nodes = SceneSnapshot.Subtree(nodes, index) };
            marker.SnapshotJson = JsonUtility.ToJson(msg);
        }

        private static Material MaterialFor(Color color)
        {
            if (_materials.TryGetValue(color, out var cached) && cached != null) return cached;
            var tint = new Color(color.r, color.g, color.b, 0.35f);
            Material material;
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                material = new Material(urp);
                material.SetFloat("_Surface", 1f);       // transparent
                material.SetFloat("_Blend", 0f);         // alpha
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                material.SetColor("_BaseColor", tint);
            }
            else
            {
                material = new Material(Shader.Find("Sprites/Default"));
                material.color = tint;
            }
            material.hideFlags = HideFlags.HideAndDontSave;
            _materials[color] = material;
            return material;
        }

        #region Resolving

        private static void QueueResolve()
        {
            if (_resolveQueued) return;
            _resolveQueued = true;
            EditorApplication.delayCall += () =>
            {
                _resolveQueued = false;
                ResolveAll();
            };
        }

        /// <summary>Replaces every placeholder whose asset is now available. Returns how many were replaced.</summary>
        public static int ResolveAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return 0;
            PropertyCodec.ClearCache();
            int replaced = 0;
            foreach (var marker in Resources.FindObjectsOfTypeAll<CollabPlaceholder>())
            {
                if (marker == null || !marker.gameObject.scene.IsValid()) continue;
                if (IsAssetPending(marker.AssetGuid)) continue;
                string path = AssetDatabase.GUIDToAssetPath(marker.AssetGuid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                if (Replace(marker)) replaced++;
            }
            if (replaced > 0) SceneSync.ReplayDeferred();
            return replaced;
        }

        private static bool Replace(CollabPlaceholder marker)
        {
            var placeholder = marker.gameObject;
            var msg = string.IsNullOrEmpty(marker.SnapshotJson) ? null : JsonUtility.FromJson<SceneMsg>(marker.SnapshotJson);
            if (msg == null || msg.nodes == null || msg.nodes.Count == 0) return false;

            string id = CollabObjectIds.GetId(placeholder, false) ?? msg.nodes[0].id;
            var parent = placeholder.transform.parent;
            int sibling = placeholder.transform.GetSiblingIndex();
            var scene = placeholder.scene;

            // Objects teammates added under it keep their place.
            var added = new List<Transform>();
            foreach (Transform child in placeholder.transform)
                if (child.name != VisualName) added.Add(child);

            CollabObjectIds.Unregister(id);
            SceneSync.BeginApply();
            try
            {
                var from = CollabSession.Find(marker.FromPeerId);
                var result = SceneSnapshot.Apply(msg.nodes, scene, parent != null ? parent.gameObject : null, sibling, from, false);
                var real = result.Root;
                if (real == null || CollabPlaceholders.IsPlaceholder(real))
                {
                    if (real != null && real != placeholder) Object.DestroyImmediate(real);
                    CollabObjectIds.Register(placeholder, id);
                    return false;
                }
                real.transform.localPosition = placeholder.transform.localPosition;
                real.transform.localRotation = placeholder.transform.localRotation;
                real.transform.localScale = placeholder.transform.localScale;
                foreach (var child in added) child.SetParent(real.transform, false);
                foreach (var touched in result.Touched) SceneSync.SuppressEcho(touched);

                // Keep selection if the user had the placeholder selected.
                if (Selection.activeGameObject == placeholder) Selection.activeGameObject = real;
                Object.DestroyImmediate(placeholder);
                return true;
            }
            finally { SceneSync.EndApply(); }
        }

        #endregion

        private static void DrawLabels(SceneView view)
        {
            if (Event.current.type != EventType.Repaint) return;
            foreach (var marker in Resources.FindObjectsOfTypeAll<CollabPlaceholder>())
            {
                if (marker == null || !marker.gameObject.scene.IsValid() || !marker.gameObject.scene.isLoaded) continue;
                var visual = marker.transform.Find(VisualName);
                var top = visual != null ? visual.position + Vector3.up * visual.lossyScale.y * 0.5f : marker.transform.position;
                var style = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = marker.Color } };
                Handles.Label(top + Vector3.up * 0.2f, marker.AssetName + " (arriving…)", style);
            }
        }
    }
}
