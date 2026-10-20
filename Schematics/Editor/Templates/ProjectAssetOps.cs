using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// Moving and deleting assets and folders from the Project Manager, and dragging them between its grids, cards and
    /// folder tree. Drags carry asset paths, so the Project window, Scene view and Inspector fields work with them too.
    /// </summary>
    public static class ProjectAssetOps
    {
        /// <summary>The paths being dragged (assets, folders, or a Schematic tile's prefab), or none.</summary>
        public static string[] DraggedPaths()
        {
            var paths = DragAndDrop.paths ?? Array.Empty<string>();
            if (paths.Length == 0 && DragAndDrop.GetGenericData(SchematicTemplateGrid.DragDataKey) is SchematicTemplate template && template.Prefab != null)
                paths = new[] { AssetDatabase.GetAssetPath(template.Prefab) };
            return paths.Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Assets", StringComparison.Ordinal)).ToArray();
        }

        /// <summary>Whether the paths can go into a folder (not into themselves, and not where they already are).</summary>
        public static bool CanMoveInto(string folder, IEnumerable<string> paths)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return false;
            return paths.Any(p => CanMove(p, folder));
        }

        private static bool CanMove(string path, string folder)
            => !string.Equals(Parent(path), folder, StringComparison.Ordinal)
               && !string.Equals(path, folder, StringComparison.Ordinal)
               && !folder.StartsWith(path + "/", StringComparison.Ordinal);

        /// <summary>Moves assets and folders into a folder (renaming on a clash). Returns the new paths.</summary>
        public static List<string> MoveInto(string folder, IEnumerable<string> paths)
        {
            var moved = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return moved;
            foreach (var path in paths.Distinct().ToList())
            {
                if (!CanMove(path, folder))
                    continue;
                string destination = AssetDatabase.GenerateUniqueAssetPath(folder.TrimEnd('/') + "/" + Path.GetFileName(path));
                string error = AssetDatabase.MoveAsset(path, destination);
                if (string.IsNullOrEmpty(error))
                    moved.Add(destination);
                else
                    Debug.LogWarning($"[Project Manager] Couldn't move '{path}' to '{folder}': {error}");
            }
            return moved;
        }

        /// <summary>
        /// Asks, then moves assets or folders to the OS trash (so they can be restored from there). A Schematic prefab's
        /// own graph goes with it. Returns true when something was deleted.
        /// </summary>
        public static bool Delete(IEnumerable<string> paths)
        {
            var list = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            if (list.Count == 0)
                return false;

            // A Schematic prefab's graph belongs to it alone; leaving it behind would leave an orphan graph.
            var extras = new List<string>();
            foreach (var path in list)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var graph = prefab != null ? SchematicObjectFactory.GraphOf(prefab) : null;
                string graphPath = graph != null ? AssetDatabase.GetAssetPath(graph) : null;
                if (graph != null && graph.Prefab == prefab && !string.IsNullOrEmpty(graphPath) && graphPath != path && !list.Contains(graphPath))
                    extras.Add(graphPath);
            }

            bool folders = list.Any(AssetDatabase.IsValidFolder);
            string what = list.Count == 1 ? $"'{Path.GetFileName(list[0])}'" : $"these {list.Count} items";
            string message = $"Delete {what}?" + (folders ? " Everything inside a folder goes with it." : "")
                + (extras.Count > 0 ? $"\n\nTheir own Schematic Graph{(extras.Count == 1 ? "" : "s")} will be deleted too." : "")
                + "\n\nThey go to your system's trash, so they can be restored from there.";
            if (!EditorUtility.DisplayDialog("Delete", message, "Delete", "Cancel"))
                return false;

            bool any = false;
            foreach (var path in list.Concat(extras))
            {
                if (AssetDatabase.MoveAssetToTrash(path))
                    any = true;
                else
                    Debug.LogWarning($"[Project Manager] Couldn't delete '{path}'.");
            }
            return any;
        }

        /// <summary>Starts dragging assets or folders by path (a drag Unity's own windows understand too).</summary>
        public static void StartDrag(string title, params string[] paths)
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.paths = paths;
            DragAndDrop.objectReferences = paths.Select(AssetDatabase.LoadMainAssetAtPath).Where(o => o != null).ToArray();
            DragAndDrop.StartDrag(title);
        }

        /// <summary>
        /// Makes an element accept dragged assets and folders, moving them into the folder it stands for.
        /// <paramref name="folder"/> is read when the drag arrives (null: not a target right now).
        /// </summary>
        public static void MakeDropTarget(VisualElement element, Func<string> folder, Action<List<string>> moved, string hoverClass = "drop-target--hover")
        {
            element.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool ok = CanMoveInto(folder(), DraggedPaths());
                element.EnableInClassList(hoverClass, ok);
                if (!ok)
                    return; // an outer target (the open folder) may still take it
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                evt.StopPropagation();
            });
            element.RegisterCallback<DragLeaveEvent>(_ => element.RemoveFromClassList(hoverClass));
            element.RegisterCallback<DragExitedEvent>(_ => element.RemoveFromClassList(hoverClass));
            element.RegisterCallback<DragPerformEvent>(evt =>
            {
                element.RemoveFromClassList(hoverClass);
                string target = folder();
                var paths = DraggedPaths();
                if (!CanMoveInto(target, paths))
                    return;
                DragAndDrop.AcceptDrag();
                var result = MoveInto(target, paths);
                evt.StopPropagation();
                if (result.Count > 0)
                    moved?.Invoke(result);
            });
        }

        private static string Parent(string path) => Path.GetDirectoryName(path)?.Replace('\\', '/');
    }
}
