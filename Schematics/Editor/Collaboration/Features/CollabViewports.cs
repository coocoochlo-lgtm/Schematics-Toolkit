using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Viewports: little live pictures of what each teammate is looking at in their Scene view.
    ///
    /// Nothing visual ever crosses the network. Teammates already share where their Scene view camera is (the "camera"
    /// presence that also places their avatar), so each of us renders the picture ourselves, from our own copy of the
    /// scene, with one hidden camera moved to their pose. That keeps the network cost at zero, but rendering costs us GPU
    /// time, so:
    /// - Only viewports whose panel is actually on screen are rendered.
    /// - There is a project-wide frame rate (Project Settings > Schematics > Collaboration Viewports, 10 per second by
    ///   default), and the renders are <b>staggered</b>: with 3 viewports at 10 fps, one viewport renders every 1/30 s
    ///   instead of all three together every 1/10 s, so the editor never hitches on a burst of renders.
    /// - At most one viewport renders per editor update, and none render while Unity isn't the focused application
    ///   (optional) or while in Play Mode.
    ///
    /// Each editor also shares its Scene view lens (field of view, aspect, clip planes) under its own presence key, so
    /// teammates' pictures have the right shape and zoom. Teammates on an older version that don't share it fall back
    /// to this editor's own Scene view field of view and a 16:9 picture.
    /// </summary>
    [InitializeOnLoad]
    public static class CollabViewports
    {
        /// <summary>Presence key for <see cref="CollabViewportLens"/> (our own key, so the shared CameraPresence format is untouched).</summary>
        public const string LensPresenceKey = "viewportLens";

        private const string CameraName = "Schematics Collaboration Viewport Camera";
        private const string OpenSessionKey = "Remedy.Schematics.CollabViewports.Open";
        private const float DefaultAspect = 16f / 9f;

        /// <summary>Raised when a viewport opens or closes (the panels rebuild their tiles).</summary>
        public static event Action OpenChanged;
        /// <summary>Raised after a viewport's picture changed (peer id): its tiles repaint.</summary>
        public static event Action<string> Rendered;

        /// <summary>What we keep per open viewport.</summary>
        public sealed class Viewport
        {
            public string PeerId;
            /// <summary>The picture (owned here; released when the viewport closes or the teammate leaves).</summary>
            public RenderTexture Texture;
            /// <summary>Width / height of the teammate's Scene view (16:9 until they tell us).</summary>
            public float Aspect = DefaultAspect;
            /// <summary>Why there's no fresh picture, or null when it's rendering normally.</summary>
            public string Status = "Waiting for the first frame…";
            /// <summary>The pixel size the panels want (largest visible tile); 0 = not measured yet.</summary>
            public int WantedWidth, WantedHeight;
            public double LastRender;
        }

        private static readonly List<Viewport> _open = new();
        private static readonly List<IViewportTile> _tiles = new();
        private static Camera _camera;
        private static double _nextRender;
        private static int _cursor;
        private static double _lastLensPublish;
        private static bool _warnedRender;
        private static bool _renderBroken;
        private static readonly double _loadTime = EditorApplication.timeSinceStartup;

        static CollabViewports()
        {
            EditorApplication.update += () => CollabFeatures.Safe(Update);
            CollabSession.PeerLeft += peer => CollabFeatures.Safe(() => { if (peer != null) Close(peer.Id); });
            CollabSession.Disconnected += () => CollabFeatures.Safe(CloseAll);
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;

            // Re-open what was open before the domain reload (the session reconnects by itself).
            var saved = SessionState.GetString(OpenSessionKey, "");
            foreach (var id in saved.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (_open.All(v => v.PeerId != id)) _open.Add(new Viewport { PeerId = id });
        }

        #region Public API (used by the overlays)

        /// <summary>The open viewports, in the order they were opened.</summary>
        public static IReadOnlyList<Viewport> Open => _open;

        /// <summary>True when a viewport onto this teammate is open.</summary>
        public static bool IsOpen(string peerId) => Get(peerId) != null;

        /// <summary>The open viewport onto a teammate, or null.</summary>
        public static Viewport Get(string peerId)
        {
            foreach (var v in _open) if (v.PeerId == peerId) return v;
            return null;
        }

        /// <summary>Opens or closes the viewport onto a teammate.</summary>
        public static void Toggle(string peerId)
        {
            if (string.IsNullOrEmpty(peerId)) return;
            if (IsOpen(peerId)) Close(peerId);
            else
            {
                _open.Add(new Viewport { PeerId = peerId });
                _nextRender = 0;   // show something straight away
                Changed();
            }
        }

        /// <summary>Closes a viewport and releases its picture.</summary>
        public static void Close(string peerId)
        {
            var viewport = Get(peerId);
            if (viewport == null) return;
            Release(viewport);
            _open.Remove(viewport);
            Changed();
        }

        /// <summary>Closes every viewport.</summary>
        public static void CloseAll()
        {
            if (_open.Count == 0) return;
            foreach (var v in _open) Release(v);
            _open.Clear();
            Changed();
        }

        /// <summary>
        /// A teammate's camera presence and whether we can show it: null reason = renderable. Used for button tooltips and
        /// the message drawn over a tile that can't render.
        /// </summary>
        public static string Unavailable(CollabPeer peer, out CameraPresence camera)
        {
            camera = null;
            if (peer == null) return "Left the session";
            if (!CollabSession.IsConnected) return "Not connected";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Paused in Play Mode";
            camera = CollabFeatures.GetPresence<CameraPresence>(peer, CollabChannels.PresenceCamera);
            if (camera == null) return peer.Role == CollabRole.Playtester ? "Playtesting (no Scene view camera)" : "Hasn't shared a Scene view camera yet";
            var open = new HashSet<string>(ScenePresence.OpenScenePaths());
            if (camera.scenes == null || !camera.scenes.Any(open.Contains))
            {
                string theirs = !string.IsNullOrEmpty(camera.scene) ? camera.scene : camera.scenes?.FirstOrDefault();
                return string.IsNullOrEmpty(theirs) ? "Not looking at a saved scene" : "Viewing " + System.IO.Path.GetFileNameWithoutExtension(theirs) + ", which you don't have open";
            }
            return null;
        }

        /// <summary>The scene a teammate is working in, for tooltips (or null).</summary>
        public static string SceneOf(CollabPeer peer)
        {
            var camera = CollabFeatures.GetPresence<CameraPresence>(peer, CollabChannels.PresenceCamera);
            if (!string.IsNullOrEmpty(camera?.scene)) return camera.scene;
            var location = CollabFeatures.GetPresence<LocationPresence>(peer, CollabChannels.PresenceLocation);
            if (location != null && !string.IsNullOrEmpty(location.path)) return location.path;
            return camera?.scenes?.FirstOrDefault();
        }

        /// <summary>The aspect (width / height) a teammate's Scene view reported, or 16:9.</summary>
        public static float AspectOf(CollabPeer peer)
        {
            var lens = Lens(peer);
            return lens != null ? lens.aspect : DefaultAspect;
        }

        /// <summary>A tile showing a viewport (the panel overlay's tiles implement this so only visible ones render).</summary>
        public interface IViewportTile
        {
            string PeerId { get; }
            /// <summary>True while the tile is really on screen (attached, its overlay shown and not collapsed).</summary>
            bool IsShowing { get; }
            /// <summary>The picture's size on screen in pixels (0 when not laid out yet).</summary>
            Vector2Int PixelSize { get; }
            void OnRendered();
        }

        /// <summary>Tiles register while attached to a panel.</summary>
        public static void Register(IViewportTile tile) { if (!_tiles.Contains(tile)) _tiles.Add(tile); }
        public static void Unregister(IViewportTile tile) => _tiles.Remove(tile);

        #endregion

        #region Scheduling

        private static void Changed()
        {
            SessionState.SetString(OpenSessionKey, string.Join("\n", _open.Select(v => v.PeerId)));
            OpenChanged?.Invoke();
        }

        private static readonly List<Viewport> _due = new();

        private static void Update()
        {
            if (!CollabSession.IsConnected)
            {
                // Not connected (yet, after a domain reload): keep the list, release the pictures meanwhile.
                foreach (var v in _open) Release(v);
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (CollabFeatures.IsActive && now - _lastLensPublish > 0.5)
            {
                _lastLensPublish = now;
                PublishLocalLens();
            }
            if (_open.Count == 0) return;

            // Teammates who left while we weren't listening (domain reload): close their viewports, after giving the
            // session a few seconds to list everyone again.
            if (now - _loadTime > 10)
                for (int i = _open.Count - 1; i >= 0; i--)
                    if (CollabSession.Find(_open[i].PeerId) == null)
                        Close(_open[i].PeerId);

            if (!CollabFeatures.IsActive) return;   // Play Mode: the scene is a throwaway copy
            var settings = CollabViewportsSettings.instance;
            if (settings.PauseWhenEditorUnfocused && !InternalEditorUtility.isApplicationActive) return;

            _due.Clear();
            foreach (var v in _open)
                if (MeasureVisible(v)) _due.Add(v);
                else if (v.Texture != null && now - v.LastRender > 10) Release(v);   // hidden for a while: give the memory back
            if (_due.Count == 0) return;

            // Stagger: each viewport gets FPS renders a second, spread evenly, one render per slot of 1/(FPS * N) s.
            double interval = 1.0 / Mathf.Clamp(settings.ViewportFps, 1f, 60f);
            double step = interval / _due.Count;
            if (now < _nextRender) return;
            _nextRender = now - _nextRender > interval ? now + step : _nextRender + step;   // don't burst to catch up

            _cursor %= _due.Count;
            var viewport = _due[_cursor];
            _cursor = (_cursor + 1) % _due.Count;
            Render(viewport, now);
        }

        /// <summary>True when some tile shows this viewport; also records the largest pixel size wanted.</summary>
        private static bool MeasureVisible(Viewport viewport)
        {
            bool visible = false;
            int w = 0, h = 0;
            for (int i = _tiles.Count - 1; i >= 0; i--)
            {
                var tile = _tiles[i];
                if (tile == null) { _tiles.RemoveAt(i); continue; }
                if (tile.PeerId != viewport.PeerId || !tile.IsShowing) continue;
                visible = true;
                var size = tile.PixelSize;
                w = Mathf.Max(w, size.x);
                h = Mathf.Max(h, size.y);
            }
            viewport.WantedWidth = w;
            viewport.WantedHeight = h;
            return visible;
        }

        #endregion

        #region Rendering

        private static void Render(Viewport viewport, double now)
        {
            var peer = CollabSession.Find(viewport.PeerId);
            string reason = Unavailable(peer, out var pose);
            if (_renderBroken) reason = "Viewports can't render with this render pipeline (see the Console)";
            if (reason != null || pose == null)
            {
                reason ??= "Hasn't shared a Scene view camera yet";
                if (viewport.Status != reason) { viewport.Status = reason; Notify(viewport); }
                return;
            }

            var lens = Lens(peer);
            viewport.Aspect = lens != null ? lens.aspect : DefaultAspect;
            if (!EnsureTexture(viewport)) return;

            var camera = GetCamera();
            var sceneView = SceneView.lastActiveSceneView;
            var sceneCamera = sceneView != null ? sceneView.camera : null;

            // Pose from presence (validated again: a zero or NaN quaternion would break the camera's matrices).
            camera.transform.SetPositionAndRotation(CollabFeatures.SafePosition(pose.position), CollabFeatures.SafeRotation(pose.rotation));

            // Lens: theirs when shared, else our Scene view's defaults.
            camera.orthographic = pose.ortho;
            float size = float.IsFinite(pose.size) && pose.size > 0f ? pose.size : 10f;
            camera.fieldOfView = lens != null ? lens.fov : sceneCamera != null ? sceneCamera.fieldOfView : 60f;
            camera.orthographicSize = lens != null && lens.orthoSize > 0f ? lens.orthoSize : size;
            if (lens != null && lens.far > lens.near && lens.near > 0f)
            {
                camera.nearClipPlane = lens.near;
                camera.farClipPlane = lens.far;
            }
            else
            {
                // Roughly what the Scene view does with dynamic clipping: the far plane grows with the zoom distance.
                float far = Mathf.Max(1000f, size * 2000f);
                camera.farClipPlane = far;
                camera.nearClipPlane = Mathf.Max(0.01f, far * 0.00001f);
            }
            camera.aspect = viewport.Aspect;

            // Look like our Scene view does: its skybox/background choice, its visible layers.
            if (sceneCamera != null)
            {
                camera.clearFlags = sceneCamera.clearFlags;
                camera.backgroundColor = sceneCamera.backgroundColor;
            }
            else
            {
                camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.19f, 0.19f, 0.19f, 1f);
            }
            if (camera.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox == null) camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = Tools.visibleLayers;
            ApplyPostProcessing(camera, sceneView);

            camera.targetTexture = viewport.Texture;
            try
            {
                if (!RenderCamera(camera, viewport.Texture)) return;
            }
            finally
            {
                camera.targetTexture = null;
            }
            viewport.LastRender = now;
            viewport.Status = null;
            Notify(viewport);
        }

        private static void Notify(Viewport viewport)
        {
            Rendered?.Invoke(viewport.PeerId);
            foreach (var tile in _tiles.ToArray())
                if (tile != null && tile.PeerId == viewport.PeerId)
                    CollabFeatures.Safe(tile.OnRendered);
        }

        private static bool EnsureTexture(Viewport viewport)
        {
            int width = viewport.WantedWidth > 0 ? viewport.WantedWidth : 320;
            // The texture matches the teammate's aspect; the tile letterboxes it if the shapes differ.
            int height = Mathf.RoundToInt(width / Mathf.Max(0.1f, viewport.Aspect));
            int max = Mathf.Clamp(CollabViewportsSettings.instance.MaxTextureSize, 128, 4096);
            if (width > max) { height = Mathf.RoundToInt(height * (float)max / width); width = max; }
            if (height > max) { width = Mathf.RoundToInt(width * (float)max / height); height = max; }
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);

            var texture = viewport.Texture;
            if (texture != null && texture.width == width && texture.height == height && texture.IsCreated()) return true;
            Release(viewport);
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                name = "Collaboration Viewport (" + viewport.PeerId + ")",
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = Mathf.Clamp(CollabViewportsSettings.instance.AntiAliasing, 1, 8),
            };
            if (!texture.Create())
            {
                Object.DestroyImmediate(texture);
                viewport.Status = "Couldn't create the viewport's texture";
                return false;
            }
            viewport.Texture = texture;
            return true;
        }

        private static void Release(Viewport viewport)
        {
            if (viewport.Texture == null) return;
            var texture = viewport.Texture;
            viewport.Texture = null;
            viewport.Status = "Waiting for the first frame…";
            texture.Release();
            Object.DestroyImmediate(texture);
            Notify(viewport);
        }

        private static Camera GetCamera()
        {
            if (_camera != null) return _camera;
            // A camera left behind by a domain reload that skipped Cleanup (static reference lost): remove it.
            foreach (var old in Resources.FindObjectsOfTypeAll<Camera>())
                if (old != null && old.name == CameraName && (old.gameObject.hideFlags & HideFlags.DontSave) != 0)
                    Object.DestroyImmediate(old.gameObject);

            // Never saved, never in the Hierarchy; Scene Sync ignores DontSave objects, so teammates never get it.
            var go = EditorUtility.CreateGameObjectWithHideFlags(CameraName, HideFlags.HideAndDontSave, typeof(Camera));
            _camera = go.GetComponent<Camera>();
            _camera.enabled = false;   // only renders when asked
            _camera.cameraType = CameraType.Game;
            _camera.allowHDR = true;
            _camera.allowMSAA = true;
            _camera.useOcclusionCulling = true;
            return _camera;
        }

        private static void Cleanup()
        {
            foreach (var v in _open) Release(v);
            if (_camera != null) Object.DestroyImmediate(_camera.gameObject);
            _camera = null;
        }

        // RenderPipeline.StandardRequest / SubmitRenderRequest (Unity 2023.2+), found by reflection so this compiles and
        // degrades on any version: the SRP way to render one camera into a texture outside the normal loop.
        private static bool _requestLookedUp;
        private static Type _requestType;
        private static FieldInfo _requestDestination;
        private static MethodInfo _supportsRequest, _submitRequest;

        private static bool RenderCamera(Camera camera, RenderTexture target)
        {
            try
            {
                if (GraphicsSettings.currentRenderPipeline != null && TrySubmitRequest(camera, target)) return true;
                camera.Render();
                return true;
            }
            catch (Exception e)
            {
                if (!_warnedRender)
                {
                    _warnedRender = true;
                    CollabLog.Warning("Collaboration viewports can't render a camera with this render pipeline, so they stay blank: " + e.Message);
                }
                _renderBroken = true;
                return false;
            }
        }

        private static bool TrySubmitRequest(Camera camera, RenderTexture target)
        {
            if (!_requestLookedUp)
            {
                _requestLookedUp = true;
                _requestType = typeof(RenderPipeline).GetNestedType("StandardRequest", BindingFlags.Public);
                if (_requestType != null)
                {
                    _requestDestination = _requestType.GetField("destination", BindingFlags.Public | BindingFlags.Instance);
                    foreach (var method in typeof(RenderPipeline).GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (!method.IsGenericMethodDefinition || method.GetParameters().Length != 2) continue;
                        if (method.Name == "SupportsRenderRequest") _supportsRequest = method.MakeGenericMethod(_requestType);
                        else if (method.Name == "SubmitRenderRequest") _submitRequest = method.MakeGenericMethod(_requestType);
                    }
                }
            }
            if (_requestType == null || _requestDestination == null || _submitRequest == null) return false;

            var request = Activator.CreateInstance(_requestType);
            _requestDestination.SetValue(request, target);
            if (_supportsRequest != null && !(bool)_supportsRequest.Invoke(null, new[] { camera, request })) return false;
            try { _submitRequest.Invoke(null, new[] { camera, request }); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
            return true;
        }

        // URP keeps post-processing on a separate component; match the Scene view's "Post Processing" toggle when we can.
        private static Type _urpCameraDataType;
        private static PropertyInfo _urpPostProcessing;

        private static void ApplyPostProcessing(Camera camera, SceneView view)
        {
            if (GraphicsSettings.currentRenderPipeline == null) return;
            _urpCameraDataType ??= Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (_urpCameraDataType == null) return;
            _urpPostProcessing ??= _urpCameraDataType.GetProperty("renderPostProcessing", BindingFlags.Public | BindingFlags.Instance);
            if (_urpPostProcessing == null || !_urpPostProcessing.CanWrite) return;
            var data = camera.GetComponent(_urpCameraDataType);
            if (data == null) data = camera.gameObject.AddComponent(_urpCameraDataType);
            if (data == null) return;
            bool wanted = view == null || ReadBool(view.sceneViewState, "showImageEffects", "imageEffectsEnabled", true);
            if ((bool)_urpPostProcessing.GetValue(data) != wanted) _urpPostProcessing.SetValue(data, wanted);
        }

        private static bool ReadBool(object target, string name, string legacyName, bool fallback)
        {
            if (target == null) return fallback;
            var type = target.GetType();
            foreach (var n in new[] { name, legacyName })
            {
                var property = type.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.PropertyType == typeof(bool)) return (bool)property.GetValue(target);
                var field = type.GetField(n, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(bool)) return (bool)field.GetValue(target);
            }
            return fallback;
        }

        #endregion

        #region Lens presence

        /// <summary>A teammate's validated lens presence, or null when they don't share one.</summary>
        private static CollabViewportLens Lens(CollabPeer peer)
        {
            var lens = peer != null ? CollabFeatures.GetPresence<CollabViewportLens>(peer, LensPresenceKey) : null;
            if (lens == null) return null;
            // From another editor: never trust it blindly (GetPresence only sanitizes the built-in camera presence).
            if (!float.IsFinite(lens.fov) || lens.fov < 1f || lens.fov > 179f) return null;
            if (!float.IsFinite(lens.aspect) || lens.aspect < 0.1f || lens.aspect > 10f) return null;
            if (!float.IsFinite(lens.near) || !float.IsFinite(lens.far)) { lens.near = lens.far = 0f; }
            if (!float.IsFinite(lens.orthoSize)) lens.orthoSize = 0f;
            return lens;
        }

        private static void PublishLocalLens()
        {
            var view = SceneView.lastActiveSceneView;
            var camera = view != null ? view.camera : null;
            if (camera == null || camera.pixelHeight <= 0) return;
            float R(float v, float step) => Mathf.Round(v / step) * step;   // rounded so tiny jitters aren't re-sent
            CollabFeatures.PublishPresence(LensPresenceKey, new CollabViewportLens
            {
                fov = R(camera.fieldOfView, 0.1f),
                aspect = R((float)camera.pixelWidth / camera.pixelHeight, 0.01f),
                near = R(camera.nearClipPlane, 0.001f),
                far = R(camera.farClipPlane, 1f),
                orthoSize = R(camera.orthographicSize, 0.01f),
            });
        }

        #endregion
    }

    /// <summary>
    /// Presence "viewportLens": the Scene view camera's lens, so teammates' viewports of us have the right shape and zoom.
    /// (The shared <see cref="CameraPresence"/> carries only the pose.)
    /// </summary>
    [Serializable]
    public class CollabViewportLens
    {
        /// <summary>Vertical field of view in degrees.</summary>
        public float fov = 60f;
        /// <summary>Width / height of the Scene view.</summary>
        public float aspect = 16f / 9f;
        public float near;
        public float far;
        public float orthoSize;
    }

    /// <summary>
    /// Project-wide viewport settings (Project Settings > Schematics > Collaboration Viewports). Stored in ProjectSettings/
    /// so the whole team gets the same performance budget from git.
    /// </summary>
    [FilePath("ProjectSettings/SchematicsCollaborationViewports.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class CollabViewportsSettings : ScriptableSingleton<CollabViewportsSettings>
    {
        [Tooltip("How many times per second each open viewport is redrawn. Renders are spread evenly over that time " +
                 "(3 viewports at 10 fps = one render every 1/30 s) rather than all at once, so lower this if viewports slow the editor down.")]
        [UnityEngine.Range(1, 60)] public float ViewportFps = 10f;

        [Tooltip("Don't render viewports while another application has focus.")]
        public bool PauseWhenEditorUnfocused = true;

        [Tooltip("Largest width or height of a viewport's texture in pixels (it otherwise matches the panel's size on screen).")]
        [UnityEngine.Range(128, 4096)] public int MaxTextureSize = 1024;

        [Tooltip("MSAA samples for viewport textures (1 = off). Higher looks smoother and costs more.")]
        [UnityEngine.Range(1, 8)] public int AntiAliasing = 1;

        /// <summary>Saves the settings file after an edit.</summary>
        public void SaveSettings() => Save(true);
    }

    /// <summary>Draws <see cref="CollabViewportsSettings"/> in Project Settings.</summary>
    internal static class CollabViewportsSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Schematics/Collaboration Viewports", SettingsScope.Project)
            {
                label = "Collaboration Viewports",
                guiHandler = _ =>
                {
                    var settings = CollabViewportsSettings.instance;
                    settings.hideFlags &= ~HideFlags.NotEditable;
                    var so = new SerializedObject(settings);
                    so.Update();
                    EditorGUILayout.HelpBox("Viewports show what teammates see in their Scene view. Each picture is rendered on this computer " +
                                            "from their shared camera position; no images are sent over the network.", MessageType.None);
                    EditorGUI.BeginChangeCheck();
                    var it = so.GetIterator();
                    bool enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = false;
                        if (it.propertyPath == "m_Script") continue;
                        EditorGUILayout.PropertyField(it, true);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        settings.SaveSettings();
                    }
                },
                keywords = new[] { "collaboration", "viewport", "framerate", "fps", "camera", "team" },
            };
        }
    }
}
