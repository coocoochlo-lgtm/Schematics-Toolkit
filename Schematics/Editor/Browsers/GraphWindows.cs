using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Opening graphs from the browsers in a window of their own (Open in New Window), through the Project window's own
    /// Assets > Open in New Graph Window (the graph windows are in another assembly).
    /// </summary>
    internal static class GraphWindows
    {
        private const string MenuPath = "Assets/Open in New Graph Window";

        /// <summary>Opens a graph (or a Schematic prefab's graph) in a new graph window, which stays on it.</summary>
        public static void OpenInNewWindow(Object asset)
        {
            if (asset == null)
                return;
            var selected = Selection.objects;
            Selection.activeObject = asset;
            EditorApplication.ExecuteMenuItem(MenuPath);
            Selection.objects = selected;
        }
    }
}
