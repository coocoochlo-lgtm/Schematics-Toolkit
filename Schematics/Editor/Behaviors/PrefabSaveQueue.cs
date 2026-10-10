using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes edits made directly on prefab assets (not in Prefab Mode or on scene instances) to disk shortly after the
/// last change, so typing in a field doesn't save on every keystroke. Anything that loads a prefab's contents from
/// disk should call <see cref="Flush"/> first, or it would load (and then save over) the older file.
/// </summary>
[InitializeOnLoad]
public static class PrefabSaveQueue
{
    private const double Delay = 0.4;
    private static readonly HashSet<GameObject> _pending = new();
    private static double _saveAt;

    static PrefabSaveQueue()
    {
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += Flush;
        EditorApplication.quitting += Flush;
        EditorApplication.playModeStateChanged += _ => Flush();
    }

    /// <summary>Saves the prefab asset this object belongs to, soon. Does nothing for scene objects.</summary>
    public static void Request(Object assetObject)
    {
        if (assetObject == null || !PrefabUtility.IsPartOfPrefabAsset(assetObject))
            return;
        var root = assetObject is Component component ? component.transform.root.gameObject
                 : assetObject is GameObject gameObject ? gameObject.transform.root.gameObject : null;
        if (root == null)
            return;
        _pending.Add(root);
        _saveAt = EditorApplication.timeSinceStartup + Delay;
    }

    /// <summary>Saves everything waiting to be saved, now.</summary>
    public static void Flush()
    {
        if (_pending.Count == 0)
            return;
        var roots = new List<GameObject>(_pending);
        _pending.Clear();
        foreach (var root in roots)
            if (root != null)
                PrefabUtility.SavePrefabAsset(root);
    }

    private static void Update()
    {
        if (_pending.Count > 0 && EditorApplication.timeSinceStartup >= _saveAt)
            Flush();
    }
}
