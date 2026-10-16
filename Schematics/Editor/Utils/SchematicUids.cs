using Remedy.Schematics;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps Schematic ids (<see cref="ISchematicAsset"/>) unique and saved: a new id is written to its asset right away,
/// and an asset that arrives with an id another asset already has (a duplicated graph, a copied Signal) gets a new one.
/// Also finds assets by id.
/// </summary>
[InitializeOnLoad]
public class SchematicUids : AssetPostprocessor
{
    // id -> the asset that has it. Built the first time it's needed.
    private static Dictionary<string, Object> _byUid;

    static SchematicUids()
    {
        SchematicUid.Assigned = asset =>
        {
            // Saved after whatever is running finishes (it may be in the middle of drawing or importing).
            EditorApplication.delayCall += () =>
            {
                if (asset == null || !EditorUtility.IsPersistent(asset))
                    return;
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                if (_byUid != null && asset is ISchematicAsset identified)
                    _byUid[identified.Uid] = asset;
            };
        };
    }

    /// <summary>The Schematic asset with this id, or null.</summary>
    public static T Find<T>(string uid) where T : Object
    {
        if (string.IsNullOrEmpty(uid))
            return null;
        Index();
        return _byUid.TryGetValue(uid, out var asset) && asset != null ? asset as T : null;
    }

    // Everything already in the project, except files being imported right now (those are the newcomers).
    private static void Index(ICollection<string> skip = null)
    {
        if (_byUid != null)
            return;
        _byUid = new Dictionary<string, Object>();
        var paths = new HashSet<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:SchematicGraph t:SignalData t:BehaviorMetadata"))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        foreach (var path in paths)
            if (skip == null || !skip.Contains(path))
                Register(path);
    }

    // Every Schematic asset in a file (graphs hold their Signals, Behavior Metadata holds its Signals).
    private static bool Register(string path)
    {
        bool changed = false;
        if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset"))
            return false;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(asset is ISchematicAsset identified))
                continue;
            string uid = identified.Uid;
            if (_byUid.TryGetValue(uid, out var other) && other != null && other != asset)
            {
                // Two assets with one id: the one arriving now is the copy.
                identified.RegenerateUid();
                EditorUtility.SetDirty(asset);
                Debug.Log($"[Schematics] \"{asset.name}\" ({path}) had the same id as \"{other.name}\" (a copy?); it got its own.", asset);
                uid = identified.Uid;
                changed = true;
            }
            _byUid[uid] = asset;
        }
        return changed;
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool changed = false;
        foreach (var path in imported)
            if (path.EndsWith(".asset"))
            {
                Index(imported);
                changed |= Register(path);
            }
        // Only when a copy got a new id (saving re-imports it, which then finds nothing to change).
        if (changed)
            AssetDatabase.SaveAssets();
    }
}
