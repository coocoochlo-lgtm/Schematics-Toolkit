using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Notices assets that are new in this editor session (created in Unity or dropped into the project folder), so
    /// that when one is placed in a scene it's announced to teammates (<see cref="CollabGit.AnnounceAsset"/>): they show
    /// a placeholder of the right size until git delivers it.
    /// "New" means: created through Unity (OnWillCreateAsset), or imported with a .meta file younger than this editor
    /// session. Re-imports of existing assets keep their old .meta and aren't counted.
    /// </summary>
    [InitializeOnLoad]
    public sealed class NewAssetTracker : AssetPostprocessor
    {
        static NewAssetTracker()
        {
            // Assets that arrived from teammates through git are theirs, not new here.
            CollabGit.Pulled += paths =>
            {
                if (paths == null) return;
                bool changed = false;
                foreach (var path in paths)
                {
                    _pulled.Add(path);
                    changed |= New.Remove(AssetDatabase.AssetPathToGUID(path));
                }
                if (changed) Save(SessionKey, New);
            };
        }

        private static readonly HashSet<string> _pulled = new();

        private const string SessionKey = "Remedy.Collab.NewAssets";
        private const string AnnouncedKey = "Remedy.Collab.AnnouncedAssets";
        private static HashSet<string> _new;
        private static HashSet<string> _announced;
        private static readonly DateTime SessionStart = DateTime.UtcNow.AddSeconds(-EditorApplication.timeSinceStartup);

        private static HashSet<string> New => _new ??= Load(SessionKey);
        private static HashSet<string> Announced => _announced ??= Load(AnnouncedKey);

        private static HashSet<string> Load(string key) =>
            new(SessionState.GetString(key, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));

        private static void Save(string key, HashSet<string> set) => SessionState.SetString(key, string.Join("\n", set));

        /// <summary>True if the asset (by GUID) was created or added in this editor session.</summary>
        public static bool IsNew(string guid) => !string.IsNullOrEmpty(guid) && New.Contains(guid);

        /// <summary>Marks an asset as new (also used by <see cref="NewAssetCreationWatcher"/>).</summary>
        internal static void MarkNew(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".meta") || _pulled.Contains(assetPath)) return;
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return;
            if (New.Add(guid)) Save(SessionKey, New);
        }

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/")) continue;
                try
                {
                    var meta = path + ".meta";
                    if (File.Exists(meta) && File.GetCreationTimeUtc(meta) >= SessionStart && File.GetLastWriteTimeUtc(meta) >= SessionStart)
                        MarkNew(path);
                }
                catch (IOException) { }
            }
        }

        /// <summary>
        /// Announces a placed object's prefab/model asset (and its new dependencies) if it's new, once per session.
        /// Bounds are the object's renderer bounds in its own local space, unscaled (what placeholders use).
        /// </summary>
        public static void AnnounceIfNew(string prefabGuid, Bounds localBounds)
        {
            if (string.IsNullOrEmpty(prefabGuid) || !CollabFeatures.IsActive) return;
            string path = AssetDatabase.GUIDToAssetPath(prefabGuid);
            if (string.IsNullOrEmpty(path)) return;

            var toAnnounce = new List<string>();
            if (IsNew(prefabGuid)) toAnnounce.Add(path);
            foreach (var dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (dependency == path || dependency.EndsWith(".cs")) continue;
                if (IsNew(AssetDatabase.AssetPathToGUID(dependency))) toAnnounce.Add(dependency);
            }
            if (toAnnounce.Count == 0) return;
            // The placed asset itself goes last, so by the time teammates see it announced, its dependencies are on the way.
            toAnnounce.Reverse();
            bool changed = false;
            foreach (var assetPath in toAnnounce)
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (!Announced.Add(guid)) continue;
                changed = true;
                CollabFeatures.Safe(() => CollabGit.AnnounceAsset(assetPath, localBounds));
            }
            if (changed) Save(AnnouncedKey, Announced);
        }
    }

    /// <summary>Catches assets created from inside Unity (Create menu, Save As, prefab creation).</summary>
    public sealed class NewAssetCreationWatcher : AssetModificationProcessor
    {
        private static void OnWillCreateAsset(string assetName)
        {
            string path = assetName.EndsWith(".meta") ? assetName.Substring(0, assetName.Length - 5) : assetName;
            // The GUID exists only once the asset is imported.
            EditorApplication.delayCall += () => NewAssetTracker.MarkNew(path);
        }
    }
}
