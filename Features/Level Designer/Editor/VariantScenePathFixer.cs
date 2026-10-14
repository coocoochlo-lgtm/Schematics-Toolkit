using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// An art design loads its blockout in Play Mode by path (<see cref="ArtDesignScene.BlockoutScenePath"/>), but the
    /// blockout scene may have been moved or renamed since (for example by "Move Scenes into the Levels Folder").
    /// While a scene is processed for a build or for entering Play Mode, this rewrites that path from the blockout's
    /// GUID, so the right scene is loaded even when the art scene itself wasn't opened and re-saved after the move.
    /// </summary>
    public class ArtDesignScenePathFixer : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var art in root.GetComponentsInChildren<ArtDesignScene>(true))
                {
                    if (string.IsNullOrEmpty(art.BlockoutSceneGuid))
                        continue;
                    string path = AssetDatabase.GUIDToAssetPath(art.BlockoutSceneGuid);
                    if (!string.IsNullOrEmpty(path))
                        art.BlockoutScenePath = path;
                }
            }
        }
    }
}
