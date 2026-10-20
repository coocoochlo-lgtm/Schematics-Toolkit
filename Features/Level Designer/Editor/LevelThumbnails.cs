using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Thumbnails of blockout and variant scenes for the Level Designer window. Whenever a blockout or variant
    /// is saved while it's the active scene, the Scene view's current camera is rendered into a small picture
    /// (<see cref="Width"/> x <see cref="Height"/> PNG), so the thumbnail always shows the level the way the last
    /// person to save it was looking at it.<br/>
    /// The pictures are kept in <c>&lt;Levels Root&gt;/Thumbnails~/&lt;scene GUID&gt;.png</c>: a folder ending in "~"
    /// is ignored by Unity's importer (no extra assets, no import time) but is still committed to git, so everyone on
    /// the team sees the same thumbnails. Naming them by scene GUID means moving or renaming a scene keeps its
    /// thumbnail.
    /// </summary>
    [InitializeOnLoad]
    public static class LevelThumbnails
    {
        /// <summary>Thumbnail size in pixels (16:9).</summary>
        public const int Width = 256, Height = 144;

        /// <summary>The folder (inside the levels root) holding the thumbnails.</summary>
        public const string FolderName = "Thumbnails~";

        /// <summary>Raised with the scene GUID after a thumbnail was captured (the window redraws).</summary>
        public static event Action<string> Captured;

        private struct Cached
        {
            public Texture2D Texture;
            public DateTime Stamp;
        }

        private static readonly Dictionary<string, Cached> _cache = new();

        static LevelThumbnails()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        /// <summary>The thumbnails folder for the current levels root.</summary>
        public static string Folder => LevelDesignerContext.LevelsRoot + "/" + FolderName;

        /// <summary>Where a scene's thumbnail is (or would be) stored.</summary>
        public static string PathFor(string sceneGuid) => Folder + "/" + sceneGuid + ".png";

        private static void OnSceneSaved(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            // Only the scene being looked at: when a variant and its blockout are saved together, the Scene view
            // shows the art, so only the variant's picture is taken.
            if (scene != SceneManager.GetActiveScene())
                return;
            // Composed scenes (builds, "Test Composed Scene") are generated copies: no thumbnails for them.
            if (LevelComposer.IsGenerated(scene.path))
                return;
            if (LevelDesignerContext.Find<BlockoutScene>(scene) == null && LevelDesignerContext.Find<VariantScene>(scene) == null)
                return;
            // Saving is a busy moment (Unity may be in the middle of a build or a batch save); take the picture right after.
            string path = scene.path;
            EditorApplication.delayCall += () =>
            {
                var active = SceneManager.GetActiveScene();
                if (active.IsValid() && active.path == path)
                    Capture(active);
            };
        }

        /// <summary>
        /// Renders the Scene view's camera into a thumbnail for <paramref name="scene"/> and saves it. For a
        /// variant, its blockout underneath is left out of the picture. Returns false when there's no Scene view or
        /// the scene isn't saved yet.
        /// </summary>
        public static bool Capture(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                return false;
            string guid = AssetDatabase.AssetPathToGUID(scene.path);
            if (string.IsNullOrEmpty(guid))
                return false;
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
                return false;

            // Keep the blockout under a variant out of the variant's picture (Scene visibility only affects the
            // Scene view's own camera, not this one).
            var hidden = new List<Renderer>();
            if (LevelDesignerContext.Mode == LevelDesignerMode.Variant && scene == SceneManager.GetActiveScene())
            {
                var blockout = LevelDesignerContext.BlockoutSceneUnderVariant;
                if (blockout.IsValid() && blockout.isLoaded)
                {
                    foreach (var root in blockout.GetRootGameObjects())
                    {
                        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                        {
                            if (renderer.forceRenderingOff)
                                continue;
                            renderer.forceRenderingOff = true;
                            hidden.Add(renderer);
                        }
                    }
                }
            }

            GameObject cameraObject = null;
            RenderTexture big = null, small = null;
            Texture2D picture = null;
            var previousActive = RenderTexture.active;
            try
            {
                cameraObject = new GameObject("Level Thumbnail Camera") { hideFlags = HideFlags.HideAndDontSave };
                var camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(view.camera);
                camera.cameraType = CameraType.Game;
                camera.enabled = false;

                // Rendered at twice the size and scaled down, for smooth edges without relying on MSAA.
                big = RenderTexture.GetTemporary(Width * 2, Height * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                small = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                camera.aspect = (float)Width / Height;
                camera.targetTexture = big;
                camera.Render();
                camera.targetTexture = null;
                Graphics.Blit(big, small);

                RenderTexture.active = small;
                picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                picture.Apply();

                string path = PathFor(guid);
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(path, picture.EncodeToPNG());
                _cache.Remove(guid);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Level Designer: couldn't capture the scene thumbnail: " + exception.Message);
                return false;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (big != null) RenderTexture.ReleaseTemporary(big);
                if (small != null) RenderTexture.ReleaseTemporary(small);
                if (picture != null) UnityEngine.Object.DestroyImmediate(picture);
                if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
                foreach (var renderer in hidden)
                    if (renderer != null)
                        renderer.forceRenderingOff = false;
            }

            Captured?.Invoke(guid);
            return true;
        }

        /// <summary>Captures the active scene's thumbnail now (when it's a blockout or a variant).</summary>
        public static bool CaptureActive()
        {
            var scene = SceneManager.GetActiveScene();
            if (LevelDesignerContext.Find<BlockoutScene>(scene) == null && LevelDesignerContext.Find<VariantScene>(scene) == null)
                return false;
            return Capture(scene);
        }

        /// <summary>
        /// A scene's thumbnail, or null when none was captured yet. Loaded from disk once and reloaded when the file
        /// changes (a teammate's newer thumbnail pulled from git shows up on its own).
        /// </summary>
        public static Texture2D Get(string sceneGuid)
        {
            if (string.IsNullOrEmpty(sceneGuid))
                return null;
            string path = PathFor(sceneGuid);
            if (!File.Exists(path))
            {
                if (_cache.TryGetValue(sceneGuid, out var gone) && gone.Texture != null)
                    UnityEngine.Object.DestroyImmediate(gone.Texture);
                _cache.Remove(sceneGuid);
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(sceneGuid, out var cached) && cached.Texture != null && cached.Stamp == stamp)
                return cached.Texture;
            if (cached.Texture != null)
                UnityEngine.Object.DestroyImmediate(cached.Texture);

            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, name = "Level Thumbnail" };
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }
            _cache[sceneGuid] = new Cached { Texture = texture, Stamp = stamp };
            return texture;
        }

        /// <summary>Moves the thumbnails folder when the levels root changes (thumbnails follow the scenes' root).</summary>
        public static void MoveFolder(string oldRoot, string newRoot)
        {
            string from = oldRoot + "/" + FolderName;
            string to = newRoot + "/" + FolderName;
            if (!Directory.Exists(from) || from == to)
                return;
            try
            {
                Directory.CreateDirectory(to);
                foreach (var file in Directory.GetFiles(from, "*.png"))
                {
                    string target = Path.Combine(to, Path.GetFileName(file));
                    if (File.Exists(target))
                        File.Delete(target);
                    File.Move(file, target);
                }
                if (Directory.GetFileSystemEntries(from).Length == 0)
                    Directory.Delete(from);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Level Designer: couldn't move the thumbnails: " + exception.Message);
            }
            _cache.Clear();
        }
    }
}
