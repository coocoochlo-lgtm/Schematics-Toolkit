using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Closes a Schematic Graph's editor window when the graph asset is deleted (or the folder it's in), so no window is
    /// left editing a graph that's gone. Graphs that disappear another way (deleted outside Unity, a git checkout) have
    /// their windows closed once Unity notices.
    /// </summary>
    internal sealed class GraphWindowCloser : AssetModificationProcessor
    {
        private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            if (string.IsNullOrEmpty(path))
                return AssetDeleteResult.DidNotDelete;
            bool folder = AssetDatabase.IsValidFolder(path);
            foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
            {
                if (window == null || window.SchematicGraph == null)
                    continue;
                string graphPath = AssetDatabase.GetAssetPath(window.SchematicGraph);
                bool goes = string.Equals(graphPath, path, StringComparison.OrdinalIgnoreCase)
                            || (folder && graphPath.StartsWith(path.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
                if (goes)
                    window.Close();
            }
            // Unity still deletes the asset itself.
            return AssetDeleteResult.DidNotDelete;
        }

        private sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (deleted.Length == 0)
                    return;
                EditorApplication.delayCall += () =>
                {
                    foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
                        // A graph that was open but is now destroyed (the reference reads as null).
                        if (window != null && window.SchematicGraph == null && !ReferenceEquals(window.SchematicGraph, null))
                            window.Close();
                };
            }
        }
    }
}
