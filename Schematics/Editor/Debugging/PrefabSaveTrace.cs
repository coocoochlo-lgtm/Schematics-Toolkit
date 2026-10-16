using Remedy.Schematics;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// TEMPORARY diagnostics for the IO Dock losing the Schematic Object on save. At each step of a dock save it logs
/// whether the Schematic Object is present in: the .prefab file on disk, the prefab asset in memory, the object the
/// dock holds, and the loaded copy being saved. It also reports any other code saving or importing a watched prefab
/// (with a stack trace). Toggle with Tools/Remedy/Schematics/Debug/Trace Prefab Saves (on by default).
/// </summary>
public class PrefabSaveTrace : AssetModificationProcessor
{
    private const string PrefKey = "Remedy.Schematics.TracePrefabSaves";
    private const string MenuPath = "Tools/Remedy/Schematics/Debug/Trace Prefab Saves";

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    [MenuItem(MenuPath)]
    private static void Toggle() => Enabled = !Enabled;

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static int _sequence;
    private static string _scriptGuid;
    private static readonly System.Collections.Generic.HashSet<string> _watched = new();

    /// <summary>Starts a numbered trace for one save; returns its id.</summary>
    public static int Begin(string what, string assetPath)
    {
        if (!Enabled)
            return 0;
        _watched.Add(assetPath);
        int id = ++_sequence;
        Debug.Log($"[PrefabTrace #{id}] ===== {what} on {assetPath} =====");
        return id;
    }

    /// <summary>Logs where the Schematic Object is (and isn't) at this step.</summary>
    public static void Step(int id, string step, string assetPath, GameObject dockObject = null, GameObject contents = null)
    {
        if (!Enabled || string.IsNullOrEmpty(assetPath))
            return;
        _watched.Add(assetPath);

        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            string line = $"[PrefabTrace #{id}] {step,-34} | file: {FileState(assetPath)} | asset: {Describe(asset)}";
            if (asset != null)
                line += EditorUtility.IsDirty(asset) || asset.GetComponents<Component>().Any(c => c != null && EditorUtility.IsDirty(c)) ? " DIRTY" : "";

            if (!ReferenceEquals(dockObject, null))
            {
                line += $" | dock: {Describe(dockObject)}";
                if (dockObject != null && asset != null)
                    line += dockObject == asset ? " (same as asset)" : " (DIFFERENT object from asset)";
            }
            if (!ReferenceEquals(contents, null))
                line += $" | copy: {Describe(contents)}";

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == assetPath)
                line += $" | prefab mode OPEN (root {Describe(stage.prefabContentsRoot)})";

            Debug.Log(line);
        }
        catch (Exception exception)
        {
            Debug.Log($"[PrefabTrace #{id}] {step}: couldn't inspect ({exception.GetType().Name}: {exception.Message})");
        }
    }

    // ✔ / ✘ for the Schematic Object, plus the object's id and missing scripts.
    private static string Describe(GameObject gameObject)
    {
        if (ReferenceEquals(gameObject, null))
            return "null";
        if (gameObject == null)
            return "DESTROYED object";
        var root = gameObject.GetComponent<SchematicObject>();
        int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        string state = root != null ? $"SchematicObject ✔ ({root.Behaviors.Count} behavior lists)" : "SchematicObject ✘";
        return $"{state} id={gameObject.GetHashCode()}{(missing > 0 ? $" MISSING SCRIPTS={missing}" : "")}";
    }

    // Whether the .prefab file's text references the Schematic Object script.
    private static string FileState(string assetPath)
    {
        string guid = ScriptGuid();
        if (guid == null)
            return "? (script guid unknown)";
        string fullPath = Path.GetFullPath(assetPath);
        if (!File.Exists(fullPath))
            return "NO FILE";
        string text = File.ReadAllText(fullPath);
        int count = 0;
        for (int i = text.IndexOf(guid, StringComparison.Ordinal); i >= 0; i = text.IndexOf(guid, i + guid.Length, StringComparison.Ordinal))
            count++;
        return count > 0 ? $"SchematicObject ✔ (refs {count}, {text.Length} chars)" : $"SchematicObject ✘ ({text.Length} chars)";
    }

    private static string ScriptGuid()
    {
        if (_scriptGuid != null)
            return _scriptGuid;
        foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            if (script != null && script.GetClass() == typeof(SchematicObject))
                return _scriptGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
        return null;
    }

    // Anything saving a watched prefab through AssetDatabase.SaveAssets (e.g. a stale in-memory copy).
    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (Enabled)
            foreach (var path in paths)
                if (_watched.Contains(path))
                    Debug.LogWarning($"[PrefabTrace] AssetDatabase is saving the in-memory {path} over the file " +
                                     $"(asset now: {Describe(AssetDatabase.LoadAssetAtPath<GameObject>(path))}).\n{Environment.StackTrace}");
        return paths;
    }

    private class ImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!Enabled)
                return;
            foreach (var path in imported)
                if (_watched.Contains(path))
                    Debug.Log($"[PrefabTrace] imported {path} | file: {FileState(path)} | asset: {Describe(AssetDatabase.LoadAssetAtPath<GameObject>(path))}");
        }
    }
}
