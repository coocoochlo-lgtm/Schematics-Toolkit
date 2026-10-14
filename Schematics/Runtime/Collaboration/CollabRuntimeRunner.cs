using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// A hidden object that keeps collaboration ticking while the game runs: it hands network events to
    /// <see cref="CollabRuntime"/> on the main thread, tells editors where a build's player is (its camera, so editors
    /// see the playtester in their Scene view, and its scene, for the Team window), and draws the small overlay with the
    /// "Receive live edits" toggle. Created on startup, survives scene loads, never throws into the game loop.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-31000)]
    internal sealed class CollabRuntimeRunner : MonoBehaviour
    {
        private static CollabRuntimeRunner _instance;

        /// <summary>Show the overlay (builds: from the build info; editor Play Mode: off, the Team window has the toggle).</summary>
        public static bool ShowOverlay = true;

        private float _nextPresence;
        private string _lastCamera, _lastLocation;
        private bool _collapsed;
        private GUIStyle _box, _label;

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("[Schematics Collaboration]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<CollabRuntimeRunner>();
            var info = CollabRuntime.BuildInfo;
            ShowOverlay = !Application.isEditor && (info == null || info.ShowOverlay);
        }

        private void Update()
        {
            var transport = CollabRuntime.Transport;
            if (transport == null) return;
            // The editor's transport is pumped by the editor itself; a build's client is pumped here.
            if (transport.IsBuild)
            {
                try { transport.Update(); }
                catch (Exception e) { CollabLog.Exception("Collaboration update failed", e); }
                if (Time.unscaledTime >= _nextPresence)
                {
                    _nextPresence = Time.unscaledTime + 0.2f;
                    CollabLog.Safe("Publishing the playtester's presence", PublishPresence);
                }
            }
        }

        private void OnApplicationQuit()
        {
            var transport = CollabRuntime.Transport;
            if (transport != null && transport.IsBuild) CollabLog.Safe("Leaving the collaboration room", transport.Shutdown);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        #region Presence (builds)

        // Same field names as the editor's CameraPresence / LocationPresence (Features/CollabMessages.cs), so editors show
        // a playtester like a teammate: an avatar where the game camera is, and "Scene: X · Play Mode" in the Team window.
        [Serializable]
        private class CameraShape
        {
            public string scene;
            public List<string> scenes = new List<string>();
            public Vector3 pivot;
            public Quaternion rotation = Quaternion.identity;
            public float size = 5f;
            public bool ortho;
            public bool in2D;
            public Vector3 position;
        }

        [Serializable]
        private class LocationShape
        {
            public string kind = "scene";
            public string path;
            public string name;
            public List<string> scenes = new List<string>();
            public string status = "Play Mode";
        }

        private void PublishPresence()
        {
            if (!CollabRuntime.IsConnected) { _lastCamera = _lastLocation = null; return; }
            var scenes = new List<string>();
            var active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path)) scenes.Add(active.path);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path) && !scenes.Contains(scene.path)) scenes.Add(scene.path);
            }

            var location = new LocationShape { path = active.path, name = active.name, scenes = scenes };
            string locationJson = JsonUtility.ToJson(location);
            if (locationJson != _lastLocation)
            {
                _lastLocation = locationJson;
                CollabRuntime.SetPresence("location", locationJson);
            }

            var cam = Camera.main;
            if (cam == null) return;
            var t = cam.transform;
            var rotation = t.rotation;
            float x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;
            CollabLiveEditCodec.Normalize(ref x, ref y, ref z, ref w);
            rotation = new Quaternion(x, y, z, w);
            var camera = new CameraShape
            {
                scene = active.path,
                scenes = scenes,
                position = Round(t.position),
                rotation = rotation,
                pivot = Round(t.position + t.forward * 5f),
                size = 5f,
                ortho = cam.orthographic,
            };
            string cameraJson = JsonUtility.ToJson(camera);
            if (cameraJson == _lastCamera) return;
            _lastCamera = cameraJson;
            CollabRuntime.SetPresence("camera", cameraJson);
        }

        private static Vector3 Round(Vector3 v) => new Vector3(Mathf.Round(v.x * 100f) / 100f, Mathf.Round(v.y * 100f) / 100f, Mathf.Round(v.z * 100f) / 100f);

        #endregion

        #region Overlay

        private void OnGUI()
        {
            if (!ShowOverlay || CollabRuntime.Transport == null) return;
            try { DrawOverlay(); }
            catch (Exception e) { CollabLog.Exception("Drawing the collaboration overlay failed", e); }
        }

        private void DrawOverlay()
        {
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
                _label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = false };
            }
            bool connected = CollabRuntime.IsConnected;
            int editors = 0, playtesters = 0;
            foreach (var peer in CollabRuntime.Peers)
            {
                if (peer.IsLocal) continue;
                if (peer.Role == CollabRole.Editor) editors++;
                else playtesters++;
            }
            string dot = connected ? "<color=#6BC46D>●</color>" : "<color=#E8B931>●</color>";
            string title = connected
                ? $"{dot} Collab: {editors} editor{(editors == 1 ? "" : "s")}" + (playtesters > 0 ? $", {playtesters} other playtester{(playtesters == 1 ? "" : "s")}" : "")
                : $"{dot} Collab: looking for editors…";

            float width = 290f;
            var area = new Rect(Screen.width - width - 10f, 10f, width, _collapsed ? 30f : 82f);
            GUI.Box(area, GUIContent.none, _box);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 5f, area.width - 16f, area.height - 8f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, _label);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_collapsed ? "+" : "–", GUILayout.Width(22))) _collapsed = !_collapsed;
            GUILayout.EndHorizontal();
            if (!_collapsed)
            {
                bool receive = GUILayout.Toggle(CollabLiveEdits.Enabled, " Receive live edits from editors");
                if (receive != CollabLiveEdits.Enabled) CollabLiveEdits.Enabled = receive;
                string status = CollabLiveEdits.AppliedCount > 0
                    ? $"{CollabLiveEdits.AppliedCount} applied · {CollabLiveEdits.LastSummary}"
                    : (CollabLiveEdits.Enabled ? "Waiting for edits…" : "Off: the game runs as built");
                GUILayout.Label($"<size=10>{status}</size>", _label);
            }
            GUILayout.EndArea();
        }

        #endregion
    }
}
