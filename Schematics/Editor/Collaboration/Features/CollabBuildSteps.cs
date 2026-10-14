using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Makes every build a playtest client of the repository's room, with no setup:
    /// - Before the build, writes <see cref="CollabBuildInfo"/> (project key, repository, branch, commit, relay) into a
    ///   temporary Resources folder (<see cref="GeneratedFolder"/>), removed again after the build.
    /// - For each scene that goes into the build, adds one hidden object with <see cref="CollabSceneIds"/>: every saved
    ///   GameObject's editor id ("g:" + GlobalObjectId) with a reference to it, so editors' live edits find their target
    ///   in the game. Only the built copy changes; the scene asset is untouched.
    /// Project Settings > Schematics > Collaboration Features > Playtest builds switches it off (or limits it to
    /// development builds).
    /// </summary>
    internal sealed class CollabBuildSteps : IPreprocessBuildWithReport, IPostprocessBuildWithReport, IProcessSceneWithReport
    {
        /// <summary>Where the build info is written for the duration of a build (deleted afterwards).</summary>
        public const string GeneratedFolder = "Assets/SchematicsCollab.Generated";
        private static string AssetPath => GeneratedFolder + "/Resources/" + CollabBuildInfo.ResourceName + ".asset";

        public int callbackOrder => 0;

        private static bool _active;

        private static bool Wanted(BuildReport report)
        {
            var settings = CollabFeaturesSettings.instance;
            if (!settings.PlaytestBuildsJoinSessions) return false;
            if (report != null && report.summary.platform == BuildTarget.WebGL) return false;   // no sockets in browsers
            bool development = report != null && (report.summary.options & BuildOptions.Development) != 0;
            return !settings.DevelopmentBuildsOnly || development;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            _active = false;
            try
            {
                RemoveGenerated();
                if (!Wanted(report)) return;
                var identity = CollabProjectIdentity.Current();
                var settings = CollabFeaturesSettings.instance;
                var info = ScriptableObject.CreateInstance<CollabBuildInfo>();
                info.Enabled = true;
                info.ProjectKey = identity.Key;
                info.Repository = identity.RemoteUrl;
                info.Branch = identity.Branch;
                info.Commit = identity.Commit;
                info.ProjectFolder = identity.ProjectFolder;
                info.Relay = CollabProjectIdentity.RelayAddress;
                info.BuiltBy = CollabSession.Local?.Name ?? Environment.UserName;
                info.BuiltAt = DateTime.UtcNow.ToString("u");
                info.ShowOverlay = settings.ShowPlaytestOverlay;
                info.ReceiveLiveEditsByDefault = settings.ReceiveLiveEditsByDefault;

                if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(GeneratedFolder));
                if (!AssetDatabase.IsValidFolder(GeneratedFolder + "/Resources")) AssetDatabase.CreateFolder(GeneratedFolder, "Resources");
                AssetDatabase.CreateAsset(info, AssetPath);
                AssetDatabase.SaveAssets();
                _active = true;
                CollabLog.Info($"This build joins the collaboration room of {(identity.HasRepository ? $"{identity.RemoteUrl} ({identity.Branch} @ {identity.Commit})" : "this project")} "
                               + $"as a playtester (project key {identity.Key}).");
            }
            catch (Exception e)
            {
                // Never fail someone's build over collaboration.
                CollabLog.Exception("Preparing the build's collaboration info failed (the build goes on without it)", e);
                RemoveGenerated();
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            _active = false;
            CollabLog.Safe("Cleaning up the build's collaboration info", RemoveGenerated);
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Builds only: in Play Mode (report == null) the editor maps ids itself (CollabPlayModeIds).
            if (report == null || !_active) return;
            try { BakeIds(scene); }
            catch (Exception e) { CollabLog.Exception($"Adding live-edit ids to {scene.path} failed (live edits won't reach that scene)", e); }
        }

        private static void BakeIds(Scene scene)
        {
            var objects = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if ((t.gameObject.hideFlags & HideFlags.DontSaveInBuild) == 0 && !t.gameObject.CompareTag("EditorOnly"))
                        objects.Add(t.gameObject);
            if (objects.Count == 0) return;
            var array = objects.ToArray();
            var gids = new GlobalObjectId[array.Length];
            GlobalObjectId.GetGlobalObjectIdsSlow(array, gids);

            var holder = new GameObject(CollabSceneIds.HolderName) { hideFlags = HideFlags.HideInHierarchy };
            SceneManager.MoveGameObjectToScene(holder, scene);
            var map = holder.AddComponent<CollabSceneIds>();
            map.ScenePath = scene.path;
            for (int i = 0; i < array.Length; i++)
            {
                var gid = gids[i];
                if (gid.identifierType == 0) continue;
                // The same rule the editors use (CollabObjectIds): saved objects, prefab parts by their prefab id.
                bool saved = PrefabUtility.IsPartOfPrefabInstance(array[i]) ? gid.targetPrefabId != 0 : gid.targetObjectId != 0;
                if (!saved) continue;
                map.Ids.Add("g:" + gid);
                map.Objects.Add(array[i]);
            }
        }

        private static void RemoveGenerated()
        {
            if (AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.DeleteAsset(GeneratedFolder);
        }

        /// <summary>A build that failed before its post-process step leaves the folder behind: tidy it up afterwards.</summary>
        [InitializeOnLoadMethod]
        private static void CleanUpLeftovers()
        {
            EditorApplication.delayCall += () =>
            {
                if (!BuildPipeline.isBuildingPlayer && AssetDatabase.IsValidFolder(GeneratedFolder))
                    CollabLog.Safe("Removing a leftover build info folder", RemoveGenerated);
            };
        }
    }
}
