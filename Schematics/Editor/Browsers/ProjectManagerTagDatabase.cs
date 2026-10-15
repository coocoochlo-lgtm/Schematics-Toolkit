using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    [Serializable]
    public sealed class ProjectManagerTagSet
    {
        public string Section;
        public List<string> Names = new();
    }

    public sealed class ProjectManagerTagDatabase : ScriptableObject
    {
        private const string AssetFolder = "Assets/Project Manager";
        private const string AssetPath = AssetFolder + "/ProjectManagerTags.asset";

        public List<ProjectManagerTagSet> Sections = new();

        internal static ProjectManagerTagDatabase LoadOrCreate()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ProjectManagerTagDatabase));
            var existing = guids
                .Select(guid => AssetDatabase.LoadAssetAtPath<ProjectManagerTagDatabase>(AssetDatabase.GUIDToAssetPath(guid)))
                .FirstOrDefault(asset => asset != null);
            if (existing != null)
                return existing;

            if (!AssetDatabase.IsValidFolder(AssetFolder))
                AssetDatabase.CreateFolder("Assets", "Project Manager");
            var database = CreateInstance<ProjectManagerTagDatabase>();
            AssetDatabase.CreateAsset(database, AssetPath);
            AssetDatabase.SaveAssets();
            return database;
        }
    }
}
