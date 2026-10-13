using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>"Jump to teammate": opens what they're working on and shows it from where they're looking.</summary>
    public static class CollabNavigation
    {
        /// <summary>
        /// Opens the teammate's scene(s) and frames the Scene view on their camera, or opens their Schematic Graph centred
        /// on their cursor. Scenes: if you already have any of their scenes open, the others are added alongside
        /// (additively, like a blockout scene with its art scenes); otherwise their main scene replaces yours (after
        /// offering to save) and the rest are added.
        /// </summary>
        public static void JumpTo(CollabPeer peer)
        {
            if (peer == null || !CollabFeatures.IsRemote(peer)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CollabFeatures.Notify("Leave Play Mode to jump to a teammate");
                return;
            }
            var location = CollabFeatures.GetPresence<LocationPresence>(peer, CollabChannels.PresenceLocation);
            if (location != null && location.kind == "graph" && !string.IsNullOrEmpty(location.path))
            {
                var graph = CollabFeatures.GetPresence<GraphPresence>(peer, CollabChannels.PresenceGraph);
                float scale = graph != null && graph.viewScale > 0.01f ? graph.viewScale : 1f;
                Vector2 point = Vector2.zero;
                if (graph != null)
                    point = graph.cursorInside ? graph.cursor : (-(Vector2)graph.viewPosition + new Vector2(400, 300)) / scale;
                GraphCollab.OpenAndCenter(location.path, point, scale);
                return;
            }

            var cam = CollabFeatures.GetPresence<CameraPresence>(peer, CollabChannels.PresenceCamera);
            if (cam == null || cam.scenes == null || cam.scenes.Count == 0)
            {
                CollabFeatures.Notify(peer.Name + " has no saved scene open");
                return;
            }
            if (!OpenScenes(cam)) return;

            var view = SceneView.lastActiveSceneView;
            if (view == null) view = EditorWindow.GetWindow<SceneView>();
            view.in2DMode = cam.in2D;
            view.LookAt(cam.pivot, cam.rotation, cam.size, cam.ortho, false);
            view.Focus();
            view.ShowNotification(new GUIContent("With " + peer.Name), 1.0);
        }

        private static bool OpenScenes(CameraPresence cam)
        {
            var wanted = cam.scenes.Where(p => !string.IsNullOrEmpty(p)).ToList();
            var missing = wanted.Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0)
            {
                CollabFeatures.Notify("Not in your project yet: " + Path.GetFileNameWithoutExtension(missing[0]) + " (pulling…)");
                CollabFeatures.Safe(CollabGit.PullNow);
                wanted.RemoveAll(missing.Contains);
                if (wanted.Count == 0) return false;
            }

            bool IsOpen(string path) => SceneManager.GetSceneByPath(path).isLoaded;
            var toOpen = wanted.Where(p => !IsOpen(p)).ToList();
            if (toOpen.Count > 0)
            {
                bool keepCurrent = wanted.Any(IsOpen);
                if (!keepCurrent && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                for (int i = 0; i < toOpen.Count; i++)
                {
                    var mode = !keepCurrent && i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive;
                    EditorSceneManager.OpenScene(toOpen[i], mode);
                }
            }

            var active = SceneManager.GetSceneByPath(string.IsNullOrEmpty(cam.scene) ? wanted[0] : cam.scene);
            if (active.isLoaded) SceneManager.SetActiveScene(active);
            return true;
        }
    }
}
