using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TEMPORARY diagnostics for the Remedy Input asset reverting edits after Play Mode. Logs (with a stack trace where
/// there is one): every change made to it through a SerializedObject (an Inspector or the IO Dock: what changed, from
/// what, to what), every change made some other way (found by comparing it twice a second), and every reimport of
/// its file. A change that's put back right after it's made shows who put it back. Toggle with
/// Tools/Remedy/Schematics/Debug/Trace Input Asset Writes.
/// </summary>
[InitializeOnLoad]
public static class InputAssetWriteTrace
{
    private const string PrefKey = "Remedy.Schematics.TraceInputAssetWrites";
    private const string MenuPath = "Tools/Remedy/Schematics/Debug/Trace Input Asset Writes";
    private const string WatchedType = "RemedyInput";

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

    private static readonly Dictionary<int, string> _snapshots = new();
    private static int _seenThroughSerializedObject;
    private static double _nextPoll;

    static InputAssetWriteTrace()
    {
        Undo.postprocessModifications += OnModifications;
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += change =>
        {
            if (Enabled)
                Debug.Log($"[InputTrace] Play Mode: {change}");
            _snapshots.Clear();
        };
    }

    private static bool Watched(UnityEngine.Object target) => target != null && target.GetType().Name == WatchedType;

    private static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
    {
        if (!Enabled)
            return modifications;
        foreach (var modification in modifications)
        {
            var current = modification.currentValue;
            if (current == null || !Watched(current.target))
                continue;
            _seenThroughSerializedObject++;
            Debug.Log($"[InputTrace] SerializedObject change ({(EditorApplication.isPlaying ? "Play" : "Edit")} Mode) on {current.target.name}: "
                + $"{current.propertyPath}: \"{modification.previousValue?.value}\" -> \"{current.value}\"\n{Stack()}");
        }
        return modifications;
    }

    // Twice a second: has the asset changed since last time, and did that go through a SerializedObject?
    private static void Poll()
    {
        if (!Enabled || EditorApplication.timeSinceStartup < _nextPoll)
            return;
        _nextPoll = EditorApplication.timeSinceStartup + 0.5;
        foreach (var asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
        {
            if (!Watched(asset))
                continue;
            string json;
            try { json = EditorJsonUtility.ToJson(asset); }
            catch { continue; }
            int key = asset.GetHashCode();
            if (_snapshots.TryGetValue(key, out var before) && before != json)
            {
                Debug.Log($"[InputTrace] {asset.name} changed ({(EditorApplication.isPlaying ? "Play" : "Edit")} Mode)"
                    + (_seenThroughSerializedObject == 0 ? " WITHOUT a SerializedObject change: written straight from code (or reloaded)" : "")
                    + $":\n{Difference(before, json)}");
            }
            _snapshots[key] = json;
        }
        _seenThroughSerializedObject = 0;
    }

    // The first stretch where two versions differ, with a little either side.
    private static string Difference(string before, string after)
    {
        int start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start])
            start++;
        int endBefore = before.Length, endAfter = after.Length;
        while (endBefore > start && endAfter > start && before[endBefore - 1] == after[endAfter - 1])
        {
            endBefore--;
            endAfter--;
        }
        int from = Math.Max(0, start - 80);
        string Cut(string text, int end) => text.Substring(from, Math.Min(text.Length, end + 80) - from);
        return $"  before: …{Cut(before, endBefore)}…\n  after:  …{Cut(after, endAfter)}…";
    }

    private static string Stack()
        => string.Join("\n", Environment.StackTrace.Split('\n').Skip(3).Take(25));

    // Its file reimported (which reloads it from disk, dropping unsaved changes).
    private class Imports : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!Enabled)
                return;
            foreach (var path in imported)
            {
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type != null && type.Name == WatchedType)
                    Debug.Log($"[InputTrace] {path} was reimported (reloaded from disk)\n{Stack()}");
            }
        }
    }
}
