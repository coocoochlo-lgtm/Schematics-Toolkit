using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Shows the team inside the running game (a build, or the editor's Play Mode): each editor as the simple avatar
    /// (a sphere with a camera frustum, in their colour) where their Scene view camera is, and other players' ghosts
    /// (a capsule or box where the objects they control are), each with a name. Drawn with GL lines on top of the game
    /// cameras, so nothing is added to the scene. Players can hide it from the collaboration overlay; the choice is
    /// remembered on that machine.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class CollabWorldOverlay : MonoBehaviour
    {
        private const string PrefKey = "SchematicsCollab.ShowTeam";

        private static bool? _visible;

        /// <summary>Show editors and other players in the game (remembered per machine).</summary>
        public static bool Visible
        {
            get
            {
                if (_visible == null)
                {
                    bool fallback = CollabRuntime.BuildInfo == null || CollabRuntime.BuildInfo.ShowTeam;
                    try { _visible = PlayerPrefs.GetInt(PrefKey, fallback ? 1 : 0) == 1; }
                    catch { _visible = fallback; }
                }
                return _visible.Value;
            }
            set
            {
                _visible = value;
                try { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); } catch { }
            }
        }

        // Same field names as the editors' CameraPresence (Features/CollabMessages.cs).
        [Serializable]
        private class CameraShape
        {
            public string scene;
            public List<string> scenes = new List<string>();
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
        }

        private struct Label
        {
            public Vector3 World;
            public string Text;
            public Color Color;
        }

        private readonly List<Label> _labels = new List<Label>();
        private readonly Dictionary<string, (string raw, CameraShape parsed)> _cameras = new Dictionary<string, (string, CameraShape)>();
        private Material _material;
        private bool _hooked;
        private GUIStyle _labelStyle;

        private void OnEnable()
        {
            Hook(true);
        }

        private void OnDisable()
        {
            Hook(false);
        }

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;
            if (on)
            {
                RenderPipelineManager.endCameraRendering += OnEndCamera;
                Camera.onPostRender += OnPostRenderCamera;
            }
            else
            {
                RenderPipelineManager.endCameraRendering -= OnEndCamera;
                Camera.onPostRender -= OnPostRenderCamera;
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        // Scriptable pipelines (URP) call this; the built-in pipeline calls onPostRender instead.
        private void OnEndCamera(ScriptableRenderContext context, Camera camera) => DrawFor(camera);

        private void OnPostRenderCamera(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null) DrawFor(camera);
        }

        private bool ShouldDraw => Visible && CollabRuntime.IsConnected;

        private void DrawFor(Camera camera)
        {
            if (!ShouldDraw || camera == null || camera.cameraType != CameraType.Game) return;
            try
            {
                if (!EnsureMaterial()) return;
                GL.PushMatrix();
                GL.LoadProjectionMatrix(camera.projectionMatrix);
                GL.modelview = camera.worldToCameraMatrix;
                _material.SetPass(0);
                GL.Begin(GL.LINES);
                DrawEditors();
                DrawGhosts();
                GL.End();
                GL.PopMatrix();
            }
            catch (Exception e) { CollabLog.Exception("Drawing teammates in the game failed", e); }
        }

        private bool EnsureMaterial()
        {
            if (_material != null) return true;
            // Unity's built-in line shader, present in every player.
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
            return true;
        }

        #region What to draw

        private HashSet<string> LoadedScenes()
        {
            var set = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) set.Add(scene.path);
            }
            return set;
        }

        private static bool Shares(HashSet<string> loaded, List<string> scenes, string scene)
        {
            if (!string.IsNullOrEmpty(scene) && loaded.Contains(scene)) return true;
            if (scenes != null) foreach (var s in scenes) if (loaded.Contains(s)) return true;
            return false;
        }

        private CameraShape CameraOf(CollabRuntimePeer peer)
        {
            if (peer.Presence == null || !peer.Presence.TryGetValue("camera", out var raw) || string.IsNullOrEmpty(raw)) return null;
            if (_cameras.TryGetValue(peer.Id, out var cached) && cached.raw == raw) return cached.parsed;
            CameraShape parsed = null;
            try
            {
                parsed = JsonUtility.FromJson<CameraShape>(raw);
                if (parsed != null)
                {
                    parsed.position = CollabRuntimeGhosts.SafePosition(parsed.position);
                    parsed.rotation = CollabRuntimeGhosts.SafeRotation(parsed.rotation);
                }
            }
            catch { parsed = null; }
            _cameras[peer.Id] = (raw, parsed);
            return parsed;
        }

        // Editors where their Scene view camera is: the simple avatar (a sphere and a camera frustum).
        private void DrawEditors()
        {
            var loaded = LoadedScenes();
            foreach (var peer in CollabRuntime.Peers)
            {
                if (peer.IsLocal || peer.Role != CollabRole.Editor) continue;
                var cam = CameraOf(peer);
                if (cam == null || !Shares(loaded, cam.scenes, cam.scene)) continue;
                var color = peer.Color;
                color.a = 0.95f;
                GL.Color(color);
                Sphere(cam.position, 0.45f);
                Vector3 forward = cam.rotation * Vector3.forward, right = cam.rotation * Vector3.right, up = cam.rotation * Vector3.up;
                var tip = cam.position + forward * 0.2f;
                var far = cam.position + forward * 0.9f;
                const float w = 0.4f, h = 0.25f;
                var corners = new[] { far + right * w + up * h, far - right * w + up * h, far - right * w - up * h, far + right * w - up * h };
                for (int i = 0; i < 4; i++)
                {
                    Line(tip, corners[i]);
                    Line(corners[i], corners[(i + 1) % 4]);
                }
            }
        }

        private void DrawGhosts()
        {
            var loaded = LoadedScenes();
            foreach (var remote in CollabRuntimeGhosts.RemoteGhosts)
            {
                if (!Shares(loaded, remote.Frame.scenes, remote.Frame.scene)) continue;
                var peer = CollabRuntime.FindPeer(remote.PeerId);
                var color = peer != null ? peer.Color : Color.white;
                color.a = 0.8f;
                GL.Color(color);
                foreach (var ghost in remote.Frame.objects) DrawGhost(ghost);
            }
        }

        public static void DrawGhost(CollabGhostObject ghost)
        {
            var center = ghost.position + ghost.rotation * ghost.center;
            if (ghost.shape == "capsule")
            {
                float radius = Mathf.Clamp(ghost.radius, 0.05f, 50f);
                float half = Mathf.Max(0f, ghost.height * 0.5f - radius);
                var up = ghost.rotation * Vector3.up;
                var top = center + up * half;
                var bottom = center - up * half;
                Circle(top, up, radius);
                Circle(bottom, up, radius);
                Circle(center, up, radius);
                var right = ghost.rotation * Vector3.right * radius;
                var forward = ghost.rotation * Vector3.forward * radius;
                Line(top + right, bottom + right);
                Line(top - right, bottom - right);
                Line(top + forward, bottom + forward);
                Line(top - forward, bottom - forward);
                Arc(top, right, up * radius);
                Arc(top, forward, up * radius);
                Arc(bottom, right, -up * radius);
                Arc(bottom, forward, -up * radius);
                // Which way they face.
                Line(center + up * half, center + up * half + ghost.rotation * Vector3.forward * (radius * 2f));
            }
            else
            {
                var size = new Vector3(Mathf.Clamp(ghost.size.x, 0.05f, 500f), Mathf.Clamp(ghost.size.y, 0.05f, 500f), Mathf.Clamp(ghost.size.z, 0.05f, 500f));
                var min = center - size * 0.5f;
                var max = center + size * 0.5f;
                for (int i = 0; i < 4; i++)
                {
                    float x0 = (i & 1) == 0 ? min.x : max.x, z0 = (i & 2) == 0 ? min.z : max.z;
                    Line(new Vector3(x0, min.y, z0), new Vector3(x0, max.y, z0));
                }
                Line(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z));
                Line(new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z));
                Line(new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z));
                Line(new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z));
                Line(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z));
                Line(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z));
                Line(new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z));
                Line(new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z));
                Line(center, center + ghost.rotation * Vector3.forward * Mathf.Max(size.x, size.z));
            }
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private static void Circle(Vector3 center, Vector3 normal, float radius)
        {
            var u = Vector3.Cross(normal, Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized * radius;
            var v = Vector3.Cross(normal.normalized, u);
            const int segments = 20;
            var previous = center + u;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var point = center + u * Mathf.Cos(a) + v * Mathf.Sin(a);
                Line(previous, point);
                previous = point;
            }
        }

        // A half circle from center + side to center - side through center + bulge.
        private static void Arc(Vector3 center, Vector3 side, Vector3 bulge)
        {
            const int segments = 10;
            var previous = center + side;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI / segments;
                var point = center + side * Mathf.Cos(a) + bulge * Mathf.Sin(a);
                Line(previous, point);
                previous = point;
            }
        }

        private static void Sphere(Vector3 center, float radius)
        {
            Circle(center, Vector3.up, radius);
            Circle(center, Vector3.right, radius);
            Circle(center, Vector3.forward, radius);
        }

        #endregion

        #region Names

        private void OnGUI()
        {
            if (!ShouldDraw || Event.current.type != EventType.Repaint) return;
            try { DrawLabels(); }
            catch (Exception e) { CollabLog.Exception("Drawing teammates' names in the game failed", e); }
        }

        private void DrawLabels()
        {
            var camera = Camera.main;
            if (camera == null) return;
            if (_labelStyle == null)
                _labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            _labels.Clear();
            var loaded = LoadedScenes();
            foreach (var peer in CollabRuntime.Peers)
            {
                if (peer.IsLocal || peer.Role != CollabRole.Editor) continue;
                var cam = CameraOf(peer);
                if (cam != null && Shares(loaded, cam.scenes, cam.scene))
                    _labels.Add(new Label { World = cam.position + Vector3.up * 0.75f, Text = peer.Name + " (editing)", Color = peer.Color });
            }
            foreach (var remote in CollabRuntimeGhosts.RemoteGhosts)
            {
                if (!Shares(loaded, remote.Frame.scenes, remote.Frame.scene)) continue;
                var peer = CollabRuntime.FindPeer(remote.PeerId);
                foreach (var ghost in remote.Frame.objects)
                {
                    float top = ghost.shape == "capsule" ? ghost.height * 0.5f : ghost.size.y * 0.5f;
                    _labels.Add(new Label
                    {
                        World = ghost.position + ghost.rotation * ghost.center + Vector3.up * (top + 0.3f),
                        Text = (peer?.Name ?? "Player") + " (playing)",
                        Color = peer?.Color ?? Color.white,
                    });
                }
            }
            foreach (var label in _labels)
            {
                var screen = camera.WorldToScreenPoint(label.World);
                if (screen.z <= 0f) continue;
                var rect = new Rect(screen.x - 120f, Screen.height - screen.y - 12f, 240f, 24f);
                var shadow = _labelStyle.normal.textColor;
                _labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label.Text, _labelStyle);
                _labelStyle.normal.textColor = label.Color;
                GUI.Label(rect, label.Text, _labelStyle);
                _labelStyle.normal.textColor = shadow;
            }
        }

        #endregion
    }
}
