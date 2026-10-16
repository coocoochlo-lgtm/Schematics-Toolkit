using System;
using System.Collections.Generic;
using System.Linq;
using Remedy.Framework;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Every prefab under Assets/ with a <see cref="SchematicObject"/> on its root, for the Schematic Objects browser.
    /// Scanned once (the first time something asks), then kept current by watching imports, moves and deletes, so a
    /// big project isn't rescanned every time something changes.
    /// </summary>
    /// <remarks>
    /// Each prefab is handed out as a <see cref="SchematicTemplate"/> (Name: the prefab's name, Category: its folder,
    /// Description: its path) so the same <see cref="SchematicTemplateGrid"/> can show it. These entries aren't in any
    /// catalog; the same object is reused for the same path, so a browser's selection survives updates.
    /// </remarks>
    public static class SchematicObjectIndex
    {
        private static readonly Dictionary<string, SchematicTemplate> _byPath = new(StringComparer.Ordinal);
        private static readonly List<SchematicTemplate> _sorted = new();
        private static readonly HashSet<string> _pending = new(StringComparer.Ordinal);
        private static bool _scanned;
        private static bool _flushQueued;

        /// <summary>Raised after the list changed (a Schematic prefab was added, removed, moved, renamed or edited).</summary>
        public static event Action Changed;

        /// <summary>Every Schematic prefab in the project, sorted by name.</summary>
        public static IReadOnlyList<SchematicTemplate> All
        {
            get
            {
                EnsureScanned();
                return _sorted;
            }
        }

        /// <summary>The entry for a prefab, or null if it isn't a Schematic prefab under Assets/.</summary>
        public static SchematicTemplate Find(GameObject prefab)
        {
            if (prefab == null) return null;
            EnsureScanned();
            return _byPath.TryGetValue(AssetDatabase.GetAssetPath(prefab), out var entry) ? entry : null;
        }

        /// <summary>Throws the list away and scans the whole project again.</summary>
        public static void Rescan()
        {
            _scanned = false;
            EnsureScanned();
            Changed?.Invoke();
        }

        /// <summary>The folder an entry is in, relative to Assets ("Content/Prefabs"), or "Assets" for the root.</summary>
        public static string FolderOf(SchematicTemplate entry) => entry?.Category ?? "Assets";

        /// <summary>The names of the Int Tags on an entry's Schematic Object ("Untagged" when it has none).</summary>
        public static IEnumerable<string> TagsOf(SchematicTemplate entry)
        {
            if (entry?.Prefab == null || !entry.Prefab.TryGetComponent(out SchematicObject schematic) || schematic.Tags == null || schematic.Tags.Count == 0)
                return new[] { "Untagged" };
            return schematic.Tags.Select(t => IntTags.NameOf(t.Value)).Distinct();
        }

        /// <summary>Re-reads one entry (after its prefab or tags were changed from code).</summary>
        public static void Refresh(GameObject prefab)
        {
            if (prefab == null) return;
            Queue(AssetDatabase.GetAssetPath(prefab));
        }

        private static void EnsureScanned()
        {
            if (_scanned)
                return;
            _scanned = true;
            _byPath.Clear();
            _pending.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
                Update(AssetDatabase.GUIDToAssetPath(guid));
            Sort();
        }

        // Adds, updates or removes the entry for one path. True when something changed.
        private static bool Update(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var prefab = IsPrefabPath(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            bool isSchematic = prefab != null && prefab.TryGetComponent(out SchematicObject _);
            if (!isSchematic)
                return _byPath.Remove(path);

            if (!_byPath.TryGetValue(path, out var entry))
            {
                entry = new SchematicTemplate();
                _byPath[path] = entry;
            }
            entry.Prefab = prefab;
            entry.Name = prefab.name;
            entry.Category = FolderFor(path);
            entry.Description = path;
            entry.IsLevelDesign = SchematicTemplates.HasLevelDesignTag(prefab);
            return true;
        }

        private static void Sort()
        {
            _sorted.Clear();
            _sorted.AddRange(_byPath.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Description, StringComparer.Ordinal));
        }

        private static bool IsPrefabPath(string path)
            => path.StartsWith("Assets/", StringComparison.Ordinal) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

        private static string FolderFor(string path)
        {
            string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            return folder == "Assets" ? "Assets" : folder.StartsWith("Assets/", StringComparison.Ordinal) ? folder.Substring("Assets/".Length) : folder;
        }

        // Imports arrive mid-import, where loading assets isn't safe; they're handled on the next editor tick, together.
        private static void Queue(string path)
        {
            if (!_scanned || string.IsNullOrEmpty(path))
                return;
            _pending.Add(path);
            if (_flushQueued) return;
            _flushQueued = true;
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            _flushQueued = false;
            if (!_scanned || _pending.Count == 0)
                return;
            var paths = _pending.ToList();
            _pending.Clear();
            bool changed = false;
            foreach (var path in paths)
                changed |= Update(path);
            if (!changed)
                return;
            Sort();
            Changed?.Invoke();
        }

        internal sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (!_scanned)
                    return;
                foreach (var path in imported) if (IsPrefabPath(path)) Queue(path);
                foreach (var path in deleted) if (IsPrefabPath(path)) Queue(path);
                foreach (var path in moved) if (IsPrefabPath(path)) Queue(path);
                foreach (var path in movedFrom) if (IsPrefabPath(path)) Queue(path);
            }
        }
    }
}
