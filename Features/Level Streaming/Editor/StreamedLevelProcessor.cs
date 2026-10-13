using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelStreaming.Editor
{
    /// <summary>
    /// Makes level scenes arrive asleep. When a scene with a <see cref="StreamedLevel"/> is built into the game, or
    /// loaded in Play Mode in the editor, this switches its content off (and the Streamed Level remembers what it
    /// switched off), so Unity doesn't wake it all in the frame the scene is added. The Streamed Level then wakes it
    /// over the next frames.
    /// </summary>
    /// <remarks>
    /// Only ever touches a copy: the scene being built, or the one Play Mode loads. The scene asset you edit is never
    /// changed. If this doesn't run (e.g. Play Mode entered without reloading the scene) the content simply wakes the
    /// normal way, all at once.
    /// </remarks>
    public class StreamedLevelProcessor : IProcessSceneWithReport
    {
        // Late, so processors that add or remove objects run first and their objects stream in too.
        public int callbackOrder => 1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Other editor work can process scenes too (e.g. lighting bakes): only a build or Play Mode should switch
            // content off, since anything else might be a scene that's open for editing.
            bool building = report != null || BuildPipeline.isBuildingPlayer;
            if (!building && !EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var found = new List<StreamedLevel>();
            foreach (var root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<StreamedLevel>(true));
            if (found.Count == 0)
                return;

            StreamedLevel level = null;
            foreach (var candidate in found)
            {
                // One that never wakes would never wake its content either.
                if (candidate.enabled && candidate.gameObject.activeInHierarchy)
                {
                    level = candidate;
                    break;
                }
            }
            if (level == null)
            {
                Debug.LogWarning($"[Level Streaming] The Streamed Level in '{scene.name}' is switched off, so its content " +
                                 "wasn't put to sleep and will wake all at once.", found[0]);
                return;
            }
            if (found.Count > 1)
                Debug.LogWarning($"[Level Streaming] '{scene.name}' has {found.Count} Streamed Levels; only " +
                                 $"'{level.name}' is used. One per scene is enough.", level);

            level.PrepareForStreaming();
            WarnIfNetworked(scene, level);
        }

        private static void WarnIfNetworked(Scene scene, StreamedLevel level)
        {
            var type = LevelLoader.NetworkObjectType;
            if (type == null)
                return;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren(type, true) == null)
                    continue;
                Debug.LogWarning($"[Level Streaming] '{scene.name}' streams in but contains NetworkObjects. In-scene " +
                                 "NetworkObjects need Netcode's scene management (NetworkSceneManager) to load the scene " +
                                 "on every peer; load networked levels that way rather than through the Level Loader.", level);
                return;
            }
        }
    }
}
