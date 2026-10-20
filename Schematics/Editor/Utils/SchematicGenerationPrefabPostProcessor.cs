using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SchematicGenerationPrefabPostProcessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // --- CLEANUP PASS ---
        bool modified = false;

        if (modified)
        {
            EditorUtility.SetDirty(SchematicEditorData.Instance);
            AssetDatabase.SaveAssets();
        }

        // --- PREFAB PROCESSING ---
        foreach (string assetPath in importedAssets)
        {
            if (!assetPath.EndsWith(".prefab")) continue;

            if (!PrefabDragTracker.WasRecentDrag())
                continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null) continue;

            var created = SchematicGenerator.CreateSchematicForPrefab(prefab);

            if (created)
                SchematicEditorData.AddSchematicData(prefab);
        }

        SchematicEditorData.DeleteSchematicPrefabs(deletedAssets);
    }
}

[InitializeOnLoad]
public static class PrefabDragTracker
{
    private static double _lastDragTime;
    private static bool _registered;

    static PrefabDragTracker()
    {
        // Not right away: this runs while Unity is still starting up (in the middle of its first asset refresh), and
        // adding a drop handler then can leave the editor stuck on "Hold On...". Once the editor is idle it's safe.
        EditorApplication.delayCall += Register;
    }

    private static void Register()
    {
        if (_registered)
            return;
        _registered = true;
#if UNITY_6000_2_OR_NEWER
        // Unity 6.2 replaced the int-id drop handler with one that passes an EntityId; only the path is used here.
        DragAndDrop.AddDropHandlerV2((dragged, path, perform) => OnProjectBrowserDrop(perform));
#else
        DragAndDrop.AddDropHandler((int dragged, string path, bool perform) => OnProjectBrowserDrop(perform));
#endif
    }

    public static bool WasRecentDrag()
    {
        return EditorApplication.timeSinceStartup - _lastDragTime < 1.0;
    }

    private static DragAndDropVisualMode OnProjectBrowserDrop(bool perform)
    {
        if (perform)
            _lastDragTime = EditorApplication.timeSinceStartup;

        return DragAndDropVisualMode.None;
    }
}
