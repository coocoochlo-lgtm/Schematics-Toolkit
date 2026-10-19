using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Remembers which folders were made inside a Project Manager sidebar group ("a folder for Singletons"), so the
    /// group keeps showing them while they're still empty. Stored by GUID in the Project Manager database asset.
    /// </summary>
    public static class ProjectFolderMarks
    {
        private static ProjectManagerTagDatabase _database;

        private static ProjectManagerTagDatabase Database => _database != null ? _database : _database = ProjectManagerTagDatabase.LoadOrCreate();

        /// <summary>The folders marked for a group that still exist, as paths ("Assets/...").</summary>
        public static IEnumerable<string> Of(string section, string group)
        {
            var set = Find(section, group, false);
            if (set == null)
                return Array.Empty<string>();
            return set.FolderGuids.Select(AssetDatabase.GUIDToAssetPath).Where(AssetDatabase.IsValidFolder).ToArray();
        }

        /// <summary>Marks a folder as made for a group.</summary>
        public static void Mark(string section, string group, string folder)
        {
            string guid = AssetDatabase.AssetPathToGUID(folder);
            if (string.IsNullOrEmpty(guid))
                return;
            var set = Find(section, group, true);
            if (set.FolderGuids.Contains(guid))
                return;
            Undo.RecordObject(Database, "Mark Folder");
            set.FolderGuids.Add(guid);
            EditorUtility.SetDirty(Database);
            AssetDatabase.SaveAssetIfDirty(Database);
        }

        private static ProjectManagerFolderSet Find(string section, string group, bool create)
        {
            var database = Database;
            if (database == null)
                return null;
            database.Folders ??= new List<ProjectManagerFolderSet>();
            var set = database.Folders.FirstOrDefault(f => f != null
                && string.Equals(f.Section, section, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.Group, group, StringComparison.OrdinalIgnoreCase));
            if (set == null && create)
            {
                set = new ProjectManagerFolderSet { Section = section, Group = group };
                database.Folders.Add(set);
            }
            if (set != null)
                set.FolderGuids ??= new List<string>();
            return set;
        }
    }
}
