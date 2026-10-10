using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes edits made directly on prefab assets (not in Prefab Mode or on scene instances) to disk shortly after the
/// last change, so typing in a field doesn't save on every keystroke.
/// <para>
/// Saving only ever happens from the editor's regular update, when nothing else is going on: never while scripts
/// compile, assets import, Play Mode starts or stops, or a load-edit-save of a prefab's contents is in progress
/// (<see cref="BeginExclusive"/>). Saving during those can leave the editor stuck on "Hold on...".
/// </para>
/// </summary>
[InitializeOnLoad]
public static class PrefabSaveQueue
{
    private const double Delay = 0.4;
    private static readonly HashSet<GameObject> _pending = new();
    private static double _saveAt;
    private static int _exclusive;

    static PrefabSaveQueue()
    {
        EditorApplication.update += Update;
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

    /// <summary>
    /// Holds saving back while a load-edit-save of a prefab's contents runs. Call <see cref="EndExclusive"/> when it
    /// has finished (including its reimport), in a finally block.
    /// </summary>
    public static void BeginExclusive() => _exclusive++;
    public static void EndExclusive() => _exclusive = Mathf.Max(0, _exclusive - 1);

    /// <summary>True while saving would be unsafe.</summary>
    public static bool Busy => _exclusive > 0 || EditorApplication.isCompiling || EditorApplication.isUpdating
                               || EditorApplication.isPlayingOrWillChangePlaymode;

    /// <summary>
    /// Saves everything waiting to be saved, now, if it's safe to. Call before loading a prefab's contents from disk,
    /// so the load sees the latest edits.
    /// </summary>
    public static void Flush()
    {
        if (_pending.Count == 0 || Busy)
            return;
        var roots = new List<GameObject>(_pending);
        _pending.Clear();
        foreach (var root in roots)
        {
            if (root == null || !PrefabUtility.IsPartOfPrefabAsset(root))
                continue;
            try
            {
                PrefabUtility.SavePrefabAsset(root);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, root);
            }
        }
    }

    private static void Update()
    {
        if (_pending.Count > 0 && EditorApplication.timeSinceStartup >= _saveAt)
            Flush();
    }
}
