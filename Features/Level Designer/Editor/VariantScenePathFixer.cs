using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// A variant loads its blockout in Play Mode by path (<see cref="VariantScene.BlockoutScenePath"/>), but the
    /// blockout scene may have been moved or renamed since (for example by "Move Scenes into the Levels Folder").
    /// While a scene is processed for a build or for entering Play Mode, this rewrites that path from the blockout's
    /// GUID, so the right scene is loaded even when the variant scene itself wasn't opened and re-saved after the move.
    /// </summary>
    public class VariantScenePathFixer : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var variant in root.GetComponentsInChildren<VariantScene>(true))
                {
                    if (string.IsNullOrEmpty(variant.BlockoutSceneGuid))
                        continue;
                    string path = AssetDatabase.GUIDToAssetPath(variant.BlockoutSceneGuid);
                    if (!string.IsNullOrEmpty(path))
                        variant.BlockoutScenePath = path;
                }
            }
        }
    }
}
