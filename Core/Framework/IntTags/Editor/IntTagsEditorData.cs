using System.IO;
using UnityEditor;
using UnityEngine;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// Finds (or makes) the project's IntTagsData asset and changes it with Undo, so every editor tool that adds or
    /// renames a tag goes through the same place.
    /// </summary>
    public static class IntTagsEditorData
    {
        /// <summary>The existing asset, or null. Doesn't create one.</summary>
        public static IntTagsData Find()
        {
            var data = IntTagsData.Instance;
            if (data != null)
                return data;

            // Not in a Resources folder (or not imported yet): look anywhere, preferring one in Resources.
            IntTagsData fallback = null;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(IntTagsData)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<IntTagsData>(path);
                if (asset == null)
                    continue;
                if (path.Contains("/Resources/") && Path.GetFileNameWithoutExtension(path) == IntTagsData.ResourceName)
                {
                    IntTagsData.UseInstance(asset);
                    return asset;
                }
                fallback ??= asset;
            }
            if (fallback != null)
                Debug.LogWarning($"The IntTagsData asset at {AssetDatabase.GetAssetPath(fallback)} isn't at Resources/{IntTagsData.ResourceName}.asset, so tag names won't be available in builds. Move it into a Resources folder.", fallback);
            return fallback;
        }

        /// <summary>The asset, created next to the toolkit (its Resources folder) the first time it's needed.</summary>
        public static IntTagsData GetOrCreate()
        {
            var data = Find();
            if (data != null)
                return data;

            var path = DefaultAssetPath();
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(folder);

            data = ScriptableObject.CreateInstance<IntTagsData>();
            data.name = IntTagsData.ResourceName;
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            IntTagsData.UseInstance(data);
            Debug.Log($"Created {path} to hold the project's Int Tags.", data);
            return data;
        }

        /// <summary>
        /// Where a new asset goes: the toolkit's own Resources folder (found from where the IntTagsData script lives),
        /// or Assets/Resources if the toolkit is in a read-only package.
        /// </summary>
        public static string DefaultAssetPath()
        {
            const string fallback = "Assets/Resources/" + IntTagsData.ResourceName + ".asset";

            var probe = ScriptableObject.CreateInstance<IntTagsData>();
            try
            {
                var script = MonoScript.FromScriptableObject(probe);
                var scriptPath = script != null ? AssetDatabase.GetAssetPath(script) : null;
                const string suffix = "/Core/Framework/IntTags/IntTagsData.cs";
                if (!string.IsNullOrEmpty(scriptPath) && scriptPath.StartsWith("Assets/") && scriptPath.EndsWith(suffix))
                {
                    var root = scriptPath.Substring(0, scriptPath.Length - suffix.Length);
                    return $"{root}/Resources/{IntTagsData.ResourceName}.asset";
                }
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
            return fallback;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Adds a tag (creating the asset if needed). Returns its id, or Untagged with an error message.</summary>
        public static int AddTag(string name, out string error)
        {
            var data = GetOrCreate();
            if (!data.ValidateName(name, int.MinValue, out error))
                return IntTags.Untagged;
            Undo.RecordObject(data, "Add Int Tag");
            int id = data.Add(name, out error);
            Save(data);
            return id;
        }

        public static bool RenameTag(int id, string newName, out string error)
        {
            var data = Find();
            if (data == null)
            {
                error = "There are no user tags yet.";
                return false;
            }
            if (!data.ValidateName(newName, id, out error))
                return false;
            Undo.RecordObject(data, "Rename Int Tag");
            bool renamed = data.Rename(id, newName, out error);
            Save(data);
            return renamed;
        }

        public static bool RemoveTag(int id)
        {
            var data = Find();
            if (data == null)
                return false;
            Undo.RecordObject(data, "Remove Int Tag");
            bool removed = data.Remove(id);
            Save(data);
            return removed;
        }

        private static void Save(IntTagsData data)
        {
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
        }
    }
}
