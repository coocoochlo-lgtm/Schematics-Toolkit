using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PrefabRefreshUtility
{
    // The Schematic Object data a scene copy must always take from its prefab: a stale override of these keeps the old
    // Behaviors / graph link in the scene after the prefab changed.
    private static readonly string[] SchematicProperties = { "Behaviors", "SchematicGraph" };

    /// <summary>
    /// Updates the copies of a prefab in the open scenes after the prefab asset changed: Unity reloads what they don't
    /// override, and their overrides of the Schematic Object's Behaviors and graph link are reverted so they match the
    /// prefab (other overrides, like position, are kept).
    /// </summary>
    public static void RefreshSceneInstances(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            bool changed = false;
            foreach (var sceneRoot in scene.GetRootGameObjects())
            {
                foreach (var schematic in sceneRoot.GetComponentsInChildren<Remedy.Schematics.SchematicObject>(true))
                {
                    if (!PrefabUtility.IsPartOfPrefabInstance(schematic))
                        continue;
                    if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(schematic.gameObject) != prefabPath)
                        continue;

                    var serialized = new SerializedObject(schematic);
                    foreach (var name in SchematicProperties)
                    {
                        var property = serialized.FindProperty(name);
                        if (property != null && property.prefabOverride)
                        {
                            PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    /// <summary>
    /// Reimports the prefab asset and resets the Prefab editor stage if it's open, preserving selection.
    /// </summary>
    public static void ReimportAndResetPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("No prefab provided to reimport/reset.");
            return;
        }

        string path = AssetDatabase.GetAssetPath(prefab);

        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning($"Provided object '{prefab.name}' is not a prefab asset.");
            return;
        }

        // Remember the current selection so we can restore it after reload
        Object previouslySelected = Selection.activeObject;

        // Reimport the prefab asset
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Copies of the prefab in open scenes take the new asset (see RefreshSceneInstances).
        RefreshSceneInstances(path);

        var currentStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (currentStage != null && currentStage.assetPath == path)
        {
            // Store path so we can reopen
            string assetPath = currentStage.assetPath;

            // Close the stage
            EditorSceneManager.CloseScene(currentStage.scene, true);

            // Reopen in Prefab Mode
            var reopenedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            AssetDatabase.OpenAsset(reopenedPrefab);

            // Restore selection
            if (previouslySelected != null)
                Selection.activeObject = previouslySelected;
        }
        else
        {
            // Restore selection
            if (previouslySelected != null)
                Selection.activeObject = previouslySelected;
        }
    }
}
