using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// The project's git automation, run by the collaboration helper: it finds the repository the project is in, fetches
    /// every few seconds and pulls what's new (with the editor's auto refresh held off meanwhile), commits and pushes
    /// assets people add, and commits the host's scene snapshots. Safety rules: only the given paths are ever staged,
    /// nothing is force-pushed, only its own commits are pushed, local edits are never pulled over, and a scene that's
    /// live in a session is never rewritten on disk. Problems show in the Console.
    /// </summary>
    public static class CollabGit
    {
        public static bool HasRepository => Info.hasRepo;
        public static string Branch => Info.branch ?? "";
        public static string RemoteUrl => Info.remoteUrl ?? "";

        /// <summary>Project-relative paths an automatic pull changed (after the editor imported them).</summary>
        public static event Action<string[]> Pulled;

        /// <summary>A peer placed a new asset (guid, its placement bounds). Until it's pulled, <see cref="IsPending"/> is true.</summary>
        public static event Action<string, Bounds> AssetAnnounced;

        /// <summary>Announced by a peer but not here yet (show a placeholder of the announced bounds).</summary>
        public static bool IsPending(string assetGuid) => assetGuid != null && _pending.ContainsKey(assetGuid);

        /// <summary>
        /// Call when you place a new asset in a live scene: peers immediately hear its guid and bounds (for a placeholder),
        /// and the asset, its .meta and its dependencies are committed ("Add path (collab)") and pushed.
        /// </summary>
        public static void AnnounceAsset(string assetPath, Bounds bounds)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning($"[Collab] {assetPath} isn't an imported asset, so it can't be announced.");
                return;
            }
            // Its dependencies inside the project (a new prefab's new material and texture...), not packages.
            var paths = AssetDatabase.GetDependencies(assetPath, true)
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p != assetPath)
                .ToArray();
            CollabBridge.Post(new CollabFrame
            {
                type = CollabFrameType.Announce,
                key = guid,
                text = assetPath,
                paths = paths,
                bounds = new[] { bounds.center.x, bounds.center.y, bounds.center.z, bounds.size.x, bounds.size.y, bounds.size.z },
            });
        }

        /// <summary>
        /// Commits the saved scene (and its .meta) with <paramref name="summary"/> as the title and a Co-authored-by
        /// trailer for each co-author with a git email, then pushes. Meant for the host's periodic snapshots: save the
        /// scene first. Others in the session keep their live copy of the scene on disk (it's never pulled while live).
        /// </summary>
        public static void CommitScene(string scenePath, IEnumerable<CollabPeer> coAuthors, string summary)
        {
            if (string.IsNullOrEmpty(scenePath)) return;
            var authors = new List<CollabCoAuthor>();
            foreach (var peer in coAuthors ?? Enumerable.Empty<CollabPeer>())
            {
                if (peer?.Presence == null) continue;
                peer.Presence.TryGetValue("git.email", out var email);
                if (string.IsNullOrEmpty(email)) continue;
                peer.Presence.TryGetValue("git.name", out var name);
                authors.Add(new CollabCoAuthor { name = string.IsNullOrEmpty(name) ? peer.Name : name, email = email });
            }
            CollabBridge.Post(new CollabFrame { type = CollabFrameType.CommitScene, text = scenePath, json = summary ?? "", coAuthors = authors.ToArray() });
        }

        /// <summary>Fetch and pull now instead of at the next poll.</summary>
        public static void PullNow() => CollabBridge.Post(new CollabFrame { type = CollabFrameType.PullNow });

        #region Bridge side

        internal static CollabGitInfo Info { get; private set; } = new CollabGitInfo();
        private static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();

        internal static void SetInfo(CollabGitInfo info)
        {
            Info = info ?? new CollabGitInfo();
        }

        internal static void OnAnnounced(string guid, string path, float[] b)
        {
            if (string.IsNullOrEmpty(guid)) return;
            // Already here (e.g. it arrived before the announcement)?
            bool present = !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)) && AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)) != null;
            if (!present) _pending[guid] = path ?? "";
            var bounds = b != null && b.Length >= 6 ? new Bounds(new Vector3(b[0], b[1], b[2]), new Vector3(b[3], b[4], b[5])) : new Bounds();
            try { AssetAnnounced?.Invoke(guid, bounds); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal static void OnResolved(string guid)
        {
            if (guid != null) _pending.Remove(guid);
        }

        // A pull brought these: whatever was announced at those paths is here now (before Pulled is raised).
        internal static void OnPathsArrived(string[] paths)
        {
            var arrived = new HashSet<string>(paths);
            foreach (var pair in _pending.Where(p => arrived.Contains(p.Value)).ToList()) _pending.Remove(pair.Key);
        }

        internal static void SetPending(CollabKV[] entries)
        {
            _pending.Clear();
            if (entries == null) return;
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e?.key)) _pending[e.key] = e.value ?? "";
        }

        internal static void RaisePulled(string[] paths)
        {
            try { Pulled?.Invoke(paths ?? Array.Empty<string>()); }
            catch (Exception e) { Debug.LogException(e); }
        }

        #endregion
    }
}
