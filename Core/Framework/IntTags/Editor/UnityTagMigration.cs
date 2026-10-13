using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// Tools/Remedy/Schematics/Int Tags/Convert Unity Tags in Open Scenes: gives every object that has a Unity tag the
    /// matching Int Tag. Player, MainCamera, Respawn and Finish map to the built-ins; any other tag becomes a new user
    /// tag of the same name (or reuses one that already has that name).
    /// </summary>
    /// <remarks>
    /// The Int Tag goes into the object's own tag list (a SchematicObject's Tags, or an existing <see cref="IntTagged"/>),
    /// otherwise an IntTagged component is added. Unity tags are only cleared if you ask, and never MainCamera
    /// (Camera.main depends on it) or EditorOnly (it strips objects from builds). Everything is undoable.
    /// </remarks>
    internal static class UnityTagMigration
    {
        private const string MenuPath = "Tools/Remedy/Schematics/Int Tags/Convert Unity Tags in Open Scenes";

        [MenuItem(MenuPath)]
        private static void ConvertOpenScenes()
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Convert Unity Tags",
                "Give every object in the open scenes that has a Unity tag the matching Int Tag?\n\n" +
                "Player, MainCamera, Respawn and Finish map to the built-in Int Tags; other tags become new Int Tags with the same name.\n\n" +
                "Keep the Unity tags while scripts still use CompareTag. MainCamera and EditorOnly are always kept.",
                "Convert, keep Unity tags", "Cancel", "Convert, clear Unity tags");
            if (choice == 1)
                return;
            bool clearUnityTags = choice == 2;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Convert Unity Tags to Int Tags");
            int group = Undo.GetCurrentGroup();

            var report = new StringBuilder();
            var created = new List<string>();
            int converted = 0;

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;

                bool sceneChanged = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        var go = transform.gameObject;
                        string unityTag = go.tag;
                        if (string.IsNullOrEmpty(unityTag) || unityTag == "Untagged" || unityTag == "EditorOnly")
                            continue;

                        int id = Resolve(unityTag, created);
                        if (id == IntTags.Untagged)
                        {
                            report.AppendLine($"Skipped {go.name}: couldn't make an Int Tag for \"{unityTag}\".");
                            continue;
                        }

                        AddToObject(go, id);
                        converted++;
                        sceneChanged = true;

                        if (clearUnityTags && unityTag != "MainCamera")
                        {
                            Undo.RecordObject(go, "Clear Unity Tag");
                            go.tag = "Untagged";
                        }
                    }
                }

                if (sceneChanged)
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            Undo.CollapseUndoOperations(group);
            IntTagRegistry.RequestEditModeRefresh();

            var summary = new StringBuilder();
            summary.AppendLine($"Tagged {converted} object(s).");
            if (created.Count > 0)
                summary.AppendLine($"New Int Tags: {string.Join(", ", created)}.");
            if (report.Length > 0)
                summary.Append(report);
            summary.AppendLine("Save the scenes to keep the changes. Scripts that use CompareTag still need updating to HasIntTag.");
            Debug.Log("[Int Tags] " + summary);
            EditorUtility.DisplayDialog("Convert Unity Tags", summary.ToString(), "OK");
        }

        // The Unity tags that have a built-in Int Tag of the same meaning.
        private static int Resolve(string unityTag, List<string> created)
        {
            switch (unityTag)
            {
                case "Player": return IntTags.Player;
                case "MainCamera": return IntTags.MainCamera;
                case "Respawn": return IntTags.Respawn;
                case "Finish": return IntTags.Finish;
            }

            if (IntTags.TryGetId(unityTag, out int existing))
                return existing;

            int id = IntTagsEditorData.AddTag(unityTag, out var error);
            if (id == IntTags.Untagged)
                Debug.LogWarning($"[Int Tags] Couldn't add \"{unityTag}\": {error}");
            else
                created.Add(unityTag);
            return id;
        }

        // Prefers a tag list the object already has (SchematicObject.Tags or IntTagged.Tags); adds IntTagged otherwise.
        private static void AddToObject(GameObject go, int id)
        {
            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                if (component is not IIntTagSource)
                    continue;
                var serialized = new SerializedObject(component);
                var list = serialized.FindProperty("Tags");
                if (list == null || !list.isArray)
                    continue;

                for (int i = 0; i < list.arraySize; i++)
                {
                    var value = list.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(IntTag.Value));
                    if (value != null && value.intValue == id)
                        return;
                }

                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                var added = list.GetArrayElementAtIndex(index).FindPropertyRelative(nameof(IntTag.Value));
                if (added == null)
                    continue;
                added.intValue = id;
                serialized.ApplyModifiedProperties();
                return;
            }

            var tagged = Undo.AddComponent<IntTagged>(go);
            Undo.RecordObject(tagged, "Add Int Tag");
            tagged.Tags.Add(id);
            EditorUtility.SetDirty(tagged);
        }
    }
}
