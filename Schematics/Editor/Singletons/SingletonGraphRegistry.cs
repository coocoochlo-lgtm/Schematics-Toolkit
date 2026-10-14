using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Keeps the Schematic Manager Data's list of Singleton Graphs matching the Singleton Graph assets in the project:
/// on editor load, whenever one is created, deleted or moved, before entering Play Mode, and before every build.
/// The Manager Data lives in Resources, so the graphs it lists are included in builds without being in Resources.
/// </summary>
public class SingletonGraphRegistry : AssetPostprocessor, IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    [InitializeOnLoadMethod]
    private static void OnEditorLoad()
    {
        // After the asset database is ready.
        RequestSync();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                Sync();
        };
    }

    public void OnPreprocessBuild(BuildReport report) => Sync();

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (Touches(imported) || Touches(moved) || TouchesDeleted(deleted))
            RequestSync();
    }

    private static bool _syncRequested;

    // At most one Sync queued at a time, run once the editor is idle.
    private static void RequestSync()
    {
        if (_syncRequested)
            return;
        _syncRequested = true;
        EditorApplication.delayCall += RunRequestedSync;
    }

    private static void RunRequestedSync()
    {
        // In Play Mode there's nothing to do; the next edit-mode change or entering Play Mode syncs again.
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _syncRequested = false;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunRequestedSync;
            return;
        }
        _syncRequested = false;
        Sync();
    }

    // A deleted asset can't be inspected any more; only care if it was one of the registered graphs.
    private static bool TouchesDeleted(string[] paths)
    {
        if (paths.Length == 0)
            return false;
        var registered = new HashSet<string>();
        foreach (var graph in RegisteredGraphsWithoutCreating())
            if (graph != null)
                registered.Add(AssetDatabase.GetAssetPath(graph));
        foreach (var path in paths)
            if (registered.Contains(path) || path.EndsWith(".asset") && registered.Count == 0)
                return true;
        return false;
    }

    // The registered list, without creating the Manager Data asset if it doesn't exist yet.
    private static IEnumerable<SingletonGraph> RegisteredGraphsWithoutCreating()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SchematicManagerData)))
        {
            var data = AssetDatabase.LoadAssetAtPath<SchematicManagerData>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null)
                return SchematicManagerData.SingletonGraphs;
        }
        return System.Array.Empty<SingletonGraph>();
    }

    private static bool Touches(string[] paths)
    {
        foreach (var path in paths)
            if (path.EndsWith(".asset") && AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(SingletonGraph))
                return true;
        return false;
    }

    [MenuItem("Tools/Remedy/Schematics/Refresh Singleton Graphs")]
    public static void Sync()
    {
        var graphs = new List<SingletonGraph>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SingletonGraph)))
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                if (asset is SingletonGraph graph && !graphs.Contains(graph))
                    graphs.Add(graph);
        }
        graphs.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.name, b.name));

        // Don't make the Manager Data asset just to record that there's nothing to record.
        if (graphs.Count == 0 && !RegisteredGraphsWithoutCreating().GetEnumerator().MoveNext()
            && AssetDatabase.FindAssets("t:" + nameof(SchematicManagerData)).Length == 0)
            return;

        if (SchematicManagerData.SetSingletonGraphs(graphs))
            AssetDatabase.SaveAssets();
    }
}
