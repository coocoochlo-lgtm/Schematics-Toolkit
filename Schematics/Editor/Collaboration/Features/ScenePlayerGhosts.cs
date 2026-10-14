using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Players' ghosts in the Scene view: what each playtester (a build, or an editor in Play Mode) controls, drawn as a
    /// translucent capsule or box in their colour with "Name (playing)", where it is right now (about 12 updates a
    /// second, on the "ghost" channel, see CollabRuntimeGhosts). Shown in Edit Mode and in Play Mode, for scenes you have
    /// open, so you can watch someone play while you change the level around them.
    /// </summary>
    [InitializeOnLoad]
    public static class ScenePlayerGhosts
    {
        private const double ExpirySeconds = 3.0;

        private sealed class Entry
        {
            public CollabGhostFrame Frame;
            public double ReceivedAt;
        }

        private static readonly Dictionary<string, Entry> _ghosts = new();

        static ScenePlayerGhosts()
        {
            CollabSession.Subscribe(CollabRuntime.GhostChannel, OnGhost);
            CollabSession.PeerLeft += peer => { if (peer != null) _ghosts.Remove(peer.Id ?? ""); SceneView.RepaintAll(); };
            CollabSession.Disconnected += () => _ghosts.Clear();
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += RepaintWhileMoving;
            // The game in Play Mode shares its ghosts unless the project switched it off.
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredPlayMode) CollabRuntimeGhosts.Share = CollabFeaturesSettings.instance.SharePlayerGhosts;
            };
        }

        /// <summary>Peers whose ghosts were heard in the last few seconds, with what they control.</summary>
        public static IEnumerable<(CollabPeer peer, CollabGhostFrame frame)> Current
        {
            get
            {
                double now = EditorApplication.timeSinceStartup;
                foreach (var pair in _ghosts)
                    if (now - pair.Value.ReceivedAt < ExpirySeconds)
                        yield return (CollabSession.Find(pair.Key), pair.Value.Frame);
            }
        }

        private static void OnGhost(CollabPeer peer, string json)
        {
            if (peer == null || peer.IsLocal || string.IsNullOrEmpty(peer.Id)) return;
            CollabGhostFrame frame;
            try { frame = JsonUtility.FromJson<CollabGhostFrame>(json); }
            catch (Exception e) { CollabLog.Warning($"Ignored {peer.Name}'s unreadable player ghost ({e.Message})."); return; }
            if (frame?.objects == null || frame.objects.Count == 0) { _ghosts.Remove(peer.Id); return; }
            foreach (var ghost in frame.objects)
            {
                ghost.position = CollabFeatures.SafePosition(ghost.position);
                ghost.center = CollabFeatures.SafePosition(ghost.center);
                ghost.rotation = CollabFeatures.SafeRotation(ghost.rotation);
            }
            _ghosts[peer.Id] = new Entry { Frame = frame, ReceivedAt = EditorApplication.timeSinceStartup };
        }

        private static double _lastRepaint;

        private static void RepaintWhileMoving()
        {
            if (_ghosts.Count == 0) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRepaint < 1.0 / 15.0) return;
            _lastRepaint = now;
            SceneView.RepaintAll();
            foreach (var key in _ghosts.Where(p => now - p.Value.ReceivedAt > ExpirySeconds * 4).Select(p => p.Key).ToList()) _ghosts.Remove(key);
        }

        private static void OnSceneGUI(SceneView view)
        {
            if (!CollabSession.IsConnected || _ghosts.Count == 0 || Event.current.type != EventType.Repaint) return;
            var open = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !string.IsNullOrEmpty(scene.path)) open.Add(scene.path);
            }
            var zTest = Handles.zTest;
            var color = Handles.color;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            try
            {
                foreach (var (peer, frame) in Current)
                {
                    if (peer == null) continue;
                    bool shared = (!string.IsNullOrEmpty(frame.scene) && open.Contains(frame.scene)) || (frame.scenes != null && frame.scenes.Any(open.Contains));
                    if (!shared) continue;
                    foreach (var ghost in frame.objects) CollabFeatures.Safe(() => Draw(peer, ghost));
                }
            }
            finally
            {
                Handles.zTest = zTest;
                Handles.color = color;
            }
        }

        private static void Draw(CollabPeer peer, CollabGhostObject ghost)
        {
            var center = ghost.position + ghost.rotation * ghost.center;
            var solid = new Color(peer.Color.r, peer.Color.g, peer.Color.b, 0.18f);
            var wire = new Color(peer.Color.r, peer.Color.g, peer.Color.b, 0.85f);
            float top;
            if (ghost.shape == "capsule")
            {
                float radius = Mathf.Clamp(ghost.radius, 0.05f, 50f);
                float half = Mathf.Max(0f, ghost.height * 0.5f - radius);
                var up = ghost.rotation * Vector3.up;
                Handles.color = solid;
                Handles.SphereHandleCap(0, center + up * half, ghost.rotation, radius * 2f, EventType.Repaint);
                Handles.SphereHandleCap(0, center - up * half, ghost.rotation, radius * 2f, EventType.Repaint);
                Handles.color = wire;
                Handles.DrawWireDisc(center + up * half, up, radius, 2f);
                Handles.DrawWireDisc(center - up * half, up, radius, 2f);
                var right = ghost.rotation * Vector3.right * radius;
                var forward = ghost.rotation * Vector3.forward * radius;
                foreach (var side in new[] { right, -right, forward, -forward })
                    Handles.DrawLine(center + up * half + side, center - up * half + side, 2f);
                Handles.DrawWireArc(center + up * half, ghost.rotation * Vector3.forward, right, 180f, radius, 2f);
                Handles.DrawWireArc(center - up * half, ghost.rotation * Vector3.forward, -right, 180f, radius, 2f);
                Handles.DrawLine(center + up * half, center + up * half + ghost.rotation * Vector3.forward * radius * 2f, 3f);
                top = half + radius;
            }
            else
            {
                var size = new Vector3(Mathf.Clamp(ghost.size.x, 0.05f, 500f), Mathf.Clamp(ghost.size.y, 0.05f, 500f), Mathf.Clamp(ghost.size.z, 0.05f, 500f));
                var bounds = new Bounds(center, size);
                Handles.color = solid;
                Handles.CubeHandleCap(0, center, Quaternion.identity, Mathf.Min(size.x, Mathf.Min(size.y, size.z)), EventType.Repaint);
                ScenePresence.DrawBox(bounds, wire, 2f);
                Handles.color = wire;
                Handles.DrawLine(center, center + ghost.rotation * Vector3.forward * Mathf.Max(size.x, size.z), 3f);
                top = size.y * 0.5f;
            }
            var style = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = peer.Color } };
            Handles.Label(center + Vector3.up * (top + 0.35f), peer.Name + " (playing)", style);
        }
    }
}
