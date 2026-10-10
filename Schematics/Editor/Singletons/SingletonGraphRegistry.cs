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
        EditorApplication.delayCall += () => Sync();
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                Sync();
        };
    }

    public void OnPreprocessBuild(BuildReport report) => Sync();

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (Touches(imported) || Touches(moved) || deleted.Length > 0)
            EditorApplication.delayCall += () => Sync();
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

        if (SchematicManagerData.SetSingletonGraphs(graphs))
            AssetDatabase.SaveAssets();
    }
}
