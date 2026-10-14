using System.Collections.Generic;
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
        /// on their cursor. Scenes: your open scenes are matched to theirs. A variant the Level Designer knows is opened
        /// with its blockout by the Level Designer; otherwise scenes you share stay, theirs are added additively and
        /// yours they don't have open (another variant) are closed, after offering to save.
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
            view.LookAt(cam.pivot, CollabFeatures.SafeRotation(cam.rotation), cam.size > 0f ? cam.size : 10f, cam.ortho, false);
            view.Focus();
            view.ShowNotification(new GUIContent("With " + peer.Name), 1.0);
        }

        /// <summary>
        /// Moves to the scene (a variant) a teammate is working on and looks from where they are: the Level Designer
        /// opens a variant with its blockout when it knows it; otherwise your open scenes are matched to theirs (scenes
        /// they don't have open are closed after offering to save, theirs are opened additively, so the blockout you
        /// share stays loaded).
        /// </summary>
        public static void MoveToScene(CollabPeer peer, string scenePath)
        {
            if (peer == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CollabFeatures.Notify("Leave Play Mode to move to another variant");
                return;
            }
            var cam = CollabFeatures.GetPresence<CameraPresence>(peer, CollabChannels.PresenceCamera);
            if (cam == null) return;
            if (!OpenScenes(cam, scenePath)) return;
            var view = SceneView.lastActiveSceneView;
            if (view == null) view = EditorWindow.GetWindow<SceneView>();
            view.in2DMode = cam.in2D;
            view.LookAt(cam.pivot, CollabFeatures.SafeRotation(cam.rotation), cam.size > 0f ? cam.size : 10f, cam.ortho, false);
            view.Focus();
            view.ShowNotification(new GUIContent("With " + peer.Name + " in " + CollabLevelDesignerLink.DisplayName(scenePath)), 1.5);
        }

        private static bool IsOpen(string path) => SceneManager.GetSceneByPath(path).isLoaded;

        private static bool OpenScenes(CameraPresence cam, string preferred = null)
        {
            var wanted = cam.scenes.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            if (!string.IsNullOrEmpty(preferred) && !wanted.Contains(preferred)) wanted.Add(preferred);
            var missing = wanted.Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0)
            {
                CollabFeatures.Notify("Not in your project yet: " + Path.GetFileNameWithoutExtension(missing[0]) + " (pulling…)");
                CollabFeatures.Safe(CollabGit.PullNow);
                wanted.RemoveAll(missing.Contains);
                if (wanted.Count == 0) return false;
            }

            var toOpen = wanted.Where(p => !IsOpen(p)).ToList();
            if (toOpen.Count > 0)
            {
                // A variant the Level Designer knows: it opens the blockout with that variant on top, its way.
                string variant = toOpen.Contains(preferred ?? "") && CollabLevelDesignerLink.IsVariant(preferred) ? preferred
                    : toOpen.FirstOrDefault(CollabLevelDesignerLink.IsVariant);
                if (variant != null)
                {
                    if (!CollabLevelDesignerLink.OpenVariant(variant) || !IsOpen(variant)) return false;   // cancelled (or failed: it said why)
                    foreach (var path in wanted.Where(p => !IsOpen(p))) EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                }
                else
                {
                    // Match their scenes: being where they are means not keeping another variant loaded on top.
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                    bool keepCurrent = wanted.Any(IsOpen);
                    for (int i = 0; i < toOpen.Count; i++)
                    {
                        var mode = !keepCurrent && i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive;
                        EditorSceneManager.OpenScene(toOpen[i], mode);
                    }
                    var first = SceneManager.GetSceneByPath(wanted[0]);
                    if (first.isLoaded) SceneManager.SetActiveScene(first);
                    var extra = new List<Scene>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var scene = SceneManager.GetSceneAt(i);
                        if (scene.isLoaded && !string.IsNullOrEmpty(scene.path) && !wanted.Contains(scene.path)) extra.Add(scene);
                    }
                    foreach (var scene in extra) EditorSceneManager.CloseScene(scene, true);
                }
            }

            var active = SceneManager.GetSceneByPath(string.IsNullOrEmpty(cam.scene) || !IsOpen(cam.scene) ? (preferred ?? wanted[0]) : cam.scene);
            if (active.isLoaded) SceneManager.SetActiveScene(active);
            return true;
        }
    }
}
