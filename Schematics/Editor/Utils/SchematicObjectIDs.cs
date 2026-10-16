using Remedy.Schematics;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps every Schematic prefab's <see cref="SchematicObject.ObjectID"/> right: derived from the prefab asset's GUID,
/// so it's stable across sessions and unique per prefab. A duplicated prefab (or a variant) carries its source's ID
/// until this notices the mismatch and gives it its own; prefabs made before ObjectIDs existed get theirs on load.
/// Fixes are written straight to the prefab asset's component, without opening the prefab.
/// </summary>
[InitializeOnLoad]
public static class SchematicObjectIDs
{
    /// <summary>The ObjectID for the prefab at a path.</summary>
    public static int For(string prefabPath) => SchematicObject.ObjectIDFor(AssetDatabase.AssetPathToGUID(prefabPath));

    static SchematicObjectIDs()
    {
        // Once per editor session, after startup: every prefab that has a Schematic Graph.
        EditorApplication.delayCall += () =>
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:SchematicGraph"))
            {
                var graph = AssetDatabase.LoadAssetAtPath<SchematicGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph != null && graph.Prefab != null)
                    paths.Add(AssetDatabase.GetAssetPath(graph.Prefab));
            }
            Fix(paths);
        };
    }

    /// <summary>Gives each prefab with a Schematic Object the ID its GUID calls for. Returns how many changed.</summary>
    public static int Fix(IEnumerable<string> prefabPaths)
    {
        int changed = 0;
        foreach (var path in prefabPaths)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var schematic = prefab != null ? prefab.GetComponent<SchematicObject>() : null;
            if (schematic == null)
                continue;
            int id = For(path);
            if (schematic.ObjectID == id)
                continue;

            var serialized = new SerializedObject(schematic);
            serialized.FindProperty("_objectId").intValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
            changed++;
        }
        return changed;
    }

    [MenuItem("Tools/Remedy/Schematics/Refresh Object IDs")]
    private static void FixAll()
    {
        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        Debug.Log($"[Schematics] Object IDs refreshed: {Fix(paths)} prefab(s) updated.");
    }

    // New, duplicated or moved prefabs: checked once the import is done (not during it).
    private sealed class Watcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var prefabs = new List<string>();
            foreach (var path in imported) if (path.EndsWith(".prefab")) prefabs.Add(path);
            foreach (var path in moved) if (path.EndsWith(".prefab")) prefabs.Add(path);
            if (prefabs.Count > 0)
                EditorApplication.delayCall += () => Fix(prefabs);
        }
    }
}
