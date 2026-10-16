using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// Thumbnails for template prefabs: rendered with <see cref="PreviewRenderUtility"/> from a 3/4 angle that frames
    /// the prefab's renderers, like the Prefab view shows it. Rendered one per editor tick (so opening the browser never
    /// stalls), kept in memory, and saved under <c>Library/SchematicTemplateThumbnails</c> keyed by the prefab's
    /// dependency hash, so they survive restarts and re-render by themselves when the prefab (or its meshes and
    /// materials) changes. Prefabs with nothing to render fall back to Unity's own preview or icon.
    /// </summary>
    [InitializeOnLoad]
    public static class SchematicTemplateThumbnails
    {
        /// <summary>The size thumbnails are rendered at (pixels, square).</summary>
        public const int Size = 256;

        private const string CacheFolder = "Library/SchematicTemplateThumbnails";

        // How long to wait for Unity's own preview when there's nothing to render ourselves.
        private const double AssetPreviewTimeout = 4.0;

        private sealed class Entry
        {
            public Texture Texture;
            public string Hash;
            public bool Rendered;  // ours (owned, destroy on replace) rather than Unity's preview or icon
        }

        private static readonly Dictionary<string, Entry> _cache = new();
        private static readonly List<GameObject> _queue = new();
        private static readonly Dictionary<GameObject, double> _waitingForAssetPreview = new();

        /// <summary>Raised when a prefab's thumbnail changed (rendered, loaded or invalidated).</summary>
        public static event Action<GameObject> Updated;

        static SchematicTemplateThumbnails()
        {
            EditorApplication.update += Process;
        }

        /// <summary>
        /// The best thumbnail available right now. When it isn't rendered yet it's queued, and <see cref="Updated"/>
        /// fires once it is.
        /// </summary>
        public static Texture Get(GameObject prefab)
        {
            if (prefab == null)
                return null;
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path))
                return AssetPreview.GetMiniThumbnail(prefab);

            string guid = AssetDatabase.AssetPathToGUID(path);
            string hash = HashOf(path);

            if (_cache.TryGetValue(guid, out var entry) && entry.Hash == hash && entry.Texture != null)
                return entry.Texture;

            // On disk from an earlier session?
            var fromDisk = LoadFromDisk(guid, hash);
            if (fromDisk != null)
            {
                Store(guid, hash, fromDisk, rendered: true);
                return fromDisk;
            }

            if (!_queue.Contains(prefab) && !_waitingForAssetPreview.ContainsKey(prefab))
                _queue.Add(prefab);
            return Placeholder(prefab);
        }

        /// <summary>Throws away a prefab's thumbnail so it's rendered again.</summary>
        public static void Invalidate(GameObject prefab)
        {
            if (prefab == null) return;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            if (_cache.TryGetValue(guid, out var entry))
            {
                if (entry.Rendered && entry.Texture != null)
                    UnityEngine.Object.DestroyImmediate(entry.Texture);
                _cache.Remove(guid);
            }
            try
            {
                if (Directory.Exists(CacheFolder))
                    foreach (var file in Directory.GetFiles(CacheFolder, guid + "_*.png"))
                        File.Delete(file);
            }
            catch (Exception exception) { Debug.LogWarning($"[Schematics] Couldn't clear a cached thumbnail: {exception.Message}"); }
            Updated?.Invoke(prefab);
        }

        /// <summary>Forgets every thumbnail (memory and disk).</summary>
        [MenuItem("Tools/Remedy/Schematics/Templates/Clear Thumbnail Cache")]
        public static void ClearAll()
        {
            foreach (var entry in _cache.Values)
                if (entry.Rendered && entry.Texture != null)
                    UnityEngine.Object.DestroyImmediate(entry.Texture);
            _cache.Clear();
            try
            {
                if (Directory.Exists(CacheFolder))
                    Directory.Delete(CacheFolder, true);
            }
            catch (Exception exception) { Debug.LogWarning($"[Schematics] Couldn't clear the thumbnail cache: {exception.Message}"); }
            SchematicTemplates.Refresh();
        }

        // Unity's preview if it has one already, else the prefab icon.
        private static Texture Placeholder(GameObject prefab)
        {
            var preview = AssetPreview.GetAssetPreview(prefab);
            return preview != null ? preview : AssetPreview.GetMiniThumbnail(prefab);
        }

        private static void Process()
        {
            // Prefabs with nothing to render: wait for Unity's preview, then use it as is.
            if (_waitingForAssetPreview.Count > 0)
            {
                var done = new List<GameObject>();
                foreach (var pair in _waitingForAssetPreview)
                {
                    var prefab = pair.Key;
                    if (prefab == null) { done.Add(prefab); continue; }
                    var preview = AssetPreview.GetAssetPreview(prefab);
                    bool timedOut = EditorApplication.timeSinceStartup - pair.Value > AssetPreviewTimeout;
                    if (preview == null && !timedOut)
                        continue;
                    string path = AssetDatabase.GetAssetPath(prefab);
                    Store(AssetDatabase.AssetPathToGUID(path), HashOf(path), preview != null ? preview : AssetPreview.GetMiniThumbnail(prefab), rendered: false);
                    done.Add(prefab);
                    Updated?.Invoke(prefab);
                }
                foreach (var prefab in done)
                    _waitingForAssetPreview.Remove(prefab);
            }

            if (_queue.Count == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var next = _queue[0];
            _queue.RemoveAt(0);
            if (next == null)
                return;

            string assetPath = AssetDatabase.GetAssetPath(next);
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            string hash = HashOf(assetPath);
            if (_cache.TryGetValue(guid, out var cached) && cached.Hash == hash && cached.Texture != null)
                return;

            Texture2D texture = null;
            try
            {
                texture = Render(next, Size);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Schematics] Couldn't render a thumbnail for {next.name}: {exception.Message}", next);
            }

            if (texture == null)
            {
                // Nothing to render (an empty logic object) or the render failed: Unity's preview it is.
                AssetPreview.GetAssetPreview(next);
                _waitingForAssetPreview[next] = EditorApplication.timeSinceStartup;
                return;
            }

            Store(guid, hash, texture, rendered: true);
            SaveToDisk(guid, hash, texture);
            Updated?.Invoke(next);
        }

        /// <summary>
        /// Renders a prefab into a new square texture (you own it), framed from above-front-right like the Prefab view.
        /// Returns null if the prefab has no renderers.
        /// </summary>
        public static Texture2D Render(GameObject prefab, int size)
        {
            if (prefab == null)
                return null;

            var preview = new PreviewRenderUtility();
            try
            {
                var instance = preview.InstantiatePrefabInScene(prefab);
                if (instance == null || !SchematicObjectFactory.TryGetRendererBounds(instance, out var bounds))
                    return null;

                const float fieldOfView = 30f;
                float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
                float distance = radius / Mathf.Sin(fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;

                // The Scene view's default angle: looking down and across from the front-right.
                var rotation = Quaternion.LookRotation(new Vector3(-1f, -0.7f, -1f));
                var camera = preview.camera;
                preview.cameraFieldOfView = fieldOfView;
                camera.fieldOfView = fieldOfView;
                camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
                camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
                camera.farClipPlane = distance + radius * 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;

                preview.ambientColor = new Color(0.32f, 0.32f, 0.34f, 1f);
                if (preview.lights != null && preview.lights.Length > 0)
                {
                    preview.lights[0].intensity = 1.15f;
                    preview.lights[0].transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                    if (preview.lights.Length > 1)
                    {
                        preview.lights[1].intensity = 0.55f;
                        preview.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
                    }
                }

                preview.BeginStaticPreview(new Rect(0, 0, size, size));
                preview.Render(true);
                var texture = preview.EndStaticPreview();
                if (texture != null)
                {
                    texture.name = prefab.name + " Thumbnail";
                    texture.hideFlags = HideFlags.HideAndDontSave;
                }
                return texture;
            }
            finally
            {
                preview.Cleanup();
            }
        }

        // Close to the Project window's background, so thumbnails sit on it like Unity's own previews.
        private static Color Background => EditorGUIUtility.isProSkin ? new Color(0.235f, 0.235f, 0.235f, 1f) : new Color(0.76f, 0.76f, 0.76f, 1f);

        private static string HashOf(string assetPath)
            => AssetDatabase.GetAssetDependencyHash(assetPath).ToString() + (EditorGUIUtility.isProSkin ? "d" : "l");

        private static void Store(string guid, string hash, Texture texture, bool rendered)
        {
            if (_cache.TryGetValue(guid, out var old) && old.Rendered && old.Texture != null && old.Texture != texture)
                UnityEngine.Object.DestroyImmediate(old.Texture);
            _cache[guid] = new Entry { Texture = texture, Hash = hash, Rendered = rendered };
        }

        private static string FileFor(string guid, string hash) => $"{CacheFolder}/{guid}_{hash}.png";

        private static Texture2D LoadFromDisk(string guid, string hash)
        {
            try
            {
                string file = FileFor(guid, hash);
                if (!File.Exists(file))
                    return null;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (texture.LoadImage(File.ReadAllBytes(file)))
                    return texture;
                UnityEngine.Object.DestroyImmediate(texture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Schematics] Couldn't read a cached thumbnail: {exception.Message}");
            }
            return null;
        }

        private static void SaveToDisk(string guid, string hash, Texture2D texture)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder);
                // Older thumbnails of this prefab are stale now.
                foreach (var file in Directory.GetFiles(CacheFolder, guid + "_*.png"))
                    File.Delete(file);
                File.WriteAllBytes(FileFor(guid, hash), texture.EncodeToPNG());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Schematics] Couldn't cache a thumbnail: {exception.Message}");
            }
        }
    }
}
