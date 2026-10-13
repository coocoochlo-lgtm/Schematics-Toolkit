using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// Keeps <see cref="IntTagRegistry"/> filled in Edit Mode: whenever the open scenes change it marks the registry
    /// stale, and the next lookup rebuilds it from every enabled tag component (<see cref="IIntTagSource"/>). Lazy, so
    /// editing a big scene costs nothing until something actually asks for a tag.
    /// </summary>
    [InitializeOnLoad]
    internal static class IntTagEditModeScanner
    {
        static IntTagEditModeScanner()
        {
            IntTagRegistry.EditModeScanner = Scan;

            EditorApplication.hierarchyChanged += MarkDirty;
            EditorSceneManager.sceneOpened += (_, _) => MarkDirty();
            EditorSceneManager.sceneClosed += _ => MarkDirty();
            PrefabStage.prefabStageOpened += _ => MarkDirty();
            PrefabStage.prefabStageClosing += _ => MarkDirty();
            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    MarkDirty();
            };

            MarkDirty();
        }

        private static void MarkDirty() => IntTagRegistry.RequestEditModeRefresh();

        private static void Scan()
        {
            var entries = new List<(GameObject go, IntTag tag)>();

            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Collect(behaviour, entries);

            // Objects being edited in Prefab Mode live in a preview scene the search above doesn't cover.
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                foreach (var behaviour in stage.prefabContentsRoot.GetComponentsInChildren<MonoBehaviour>(false))
                    Collect(behaviour, entries);

            IntTagRegistry.Rebuild(entries);
        }

        private static void Collect(MonoBehaviour behaviour, List<(GameObject go, IntTag tag)> entries)
        {
            // isActiveAndEnabled isn't reliable in Edit Mode for scripts that don't run there, so check both parts.
            if (behaviour == null || behaviour is not IIntTagSource source || !behaviour.enabled || !behaviour.gameObject.activeInHierarchy)
                return;
            var tags = source.Tags;
            if (tags == null)
                return;
            for (int i = 0; i < tags.Count; i++)
                if (tags[i].IsTagged)
                    entries.Add((behaviour.gameObject, tags[i]));
        }
    }
}
