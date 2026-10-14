using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// A small public entry point for other tools (collaboration, browsers) that want to work with Level Designer
    /// scenes without knowing its internals. Everything here is static and safe to call by reflection:
    /// <c>Type.GetType("Remedy.LevelDesigner.Editor.LevelDesigner, Remedy.LevelDesigner.Editor")</c>.<br/>
    /// (From C# code inside another namespace under <c>Remedy</c>, write the full name
    /// <c>Remedy.LevelDesigner.Editor.LevelDesigner</c>: a bare <c>LevelDesigner</c> there means the namespace.)
    /// </summary>
    public static class LevelDesigner
    {
        /// <summary>
        /// Opens a variant scene the way the Level Designer window does: its blockout loaded first, the variant on top
        /// of it (additively) as the active scene, so the X-ray and variant tools come up. Asks to save modified scenes
        /// first, like the window. Works for variants listed in the project, and for any scene carrying a
        /// <see cref="VariantScene"/> marker. Returns false when the scene isn't a variant, can't be found, or the user
        /// cancelled saving.
        /// </summary>
        public static bool OpenVariant(string scenePath)
        {
            scenePath = Normalize(scenePath);
            if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
                return false;

            var project = LevelDesignerContext.ExistingProject;
            if (project != null)
            {
                var owner = project.FindOwnerOfVariant(AssetDatabase.AssetPathToGUID(scenePath), out var variant);
                if (owner != null)
                    return LevelDesignerContext.OpenVariant(owner, variant);
            }

            // Not listed: open it and look for its marker.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var marker = LevelDesignerContext.Find<VariantScene>(scene);
            if (marker == null)
            {
                LevelDesignerContext.Refresh();
                return false;
            }
            string blockoutPath = LevelDesignerContext.ResolvePath(marker.BlockoutSceneGuid, marker.BlockoutScenePath);
            if (!string.IsNullOrEmpty(blockoutPath) && File.Exists(blockoutPath))
            {
                EditorSceneManager.OpenScene(blockoutPath, OpenSceneMode.Single);
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
            }
            LevelDesignerContext.Refresh();
            return true;
        }

        /// <summary>
        /// Which level a scene belongs to, from the project's level list (no scene is opened, no asset is made).
        /// <paramref name="levelName"/> is the blockout's display name in the window (or its folder/file level name
        /// when it has none); <paramref name="isBlockout"/> is true for the blockout scene itself and false for one of
        /// its variants. Returns false for scenes that aren't part of any level.
        /// </summary>
        public static bool TryGetLevelOf(string scenePath, out string levelName, out bool isBlockout)
        {
            levelName = null;
            isBlockout = false;
            scenePath = Normalize(scenePath);
            var project = LevelDesignerContext.ExistingProject;
            if (string.IsNullOrEmpty(scenePath) || project == null)
                return false;
            string guid = AssetDatabase.AssetPathToGUID(scenePath);
            if (string.IsNullOrEmpty(guid))
                return false;

            var blockout = project.FindBlockout(guid);
            if (blockout != null)
                isBlockout = true;
            else
                blockout = project.FindOwnerOfVariant(guid, out _);
            if (blockout == null)
                return false;
            levelName = !string.IsNullOrWhiteSpace(blockout.Name) ? blockout.Name : LevelDesignerContext.LevelNameOf(blockout);
            return true;
        }

        private static string Normalize(string path) => string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
    }
}
