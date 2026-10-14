using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// Project Settings &gt; Schematics &gt; Int Tags: see every tag and its id, add tags, and rename or remove your own.
    /// Built-in tags are shown for reference and can't be changed.
    /// </summary>
    internal sealed class IntTagsSettingsProvider : SettingsProvider
    {
        private string _newName = "";
        private string _message;
        private MessageType _messageType;
        private Vector2 _scroll;

        private IntTagsSettingsProvider() : base(IntTagMenu.SettingsPath, SettingsScope.Project,
            new HashSet<string> { "tag", "tags", "int tag", "IntTag", "LevelDesign", "Player" })
        { }

        [SettingsProvider]
        private static SettingsProvider Create() => new IntTagsSettingsProvider();

        public override void OnGUI(string searchContext)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Int Tags are saved as numbers, so renaming a tag never breaks the objects and graphs that use it. " +
                "Ids are never reused: a removed tag's objects show \"Missing Tag\" instead of silently getting a new tag. " +
                "Use '/' in a name to group tags into submenus (e.g. Enemies/Boss).",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Built-in", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                foreach (var (id, name) in IntTags.Defaults)
                    Row(id, name);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Your tags", EditorStyles.boldLabel);

            var data = IntTagsEditorData.Find();
            if (data == null || data.Tags.Count == 0)
            {
                EditorGUILayout.LabelField("None yet. Add one below, or from any Int Tag dropdown.", EditorStyles.miniLabel);
            }
            else
            {
                // Copied first: renaming or removing changes the list.
                var entries = new List<(int id, string name)>();
                foreach (var entry in data.Tags)
                    if (entry != null)
                        entries.Add((entry.Id, entry.Name));

                foreach (var (id, name) in entries)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(id.ToString(), GUILayout.Width(40));
                        EditorGUI.BeginChangeCheck();
                        var renamed = EditorGUILayout.DelayedTextField(name);
                        if (EditorGUI.EndChangeCheck() && renamed != name)
                        {
                            if (IntTagsEditorData.RenameTag(id, renamed, out var error))
                                SetMessage($"Renamed \"{name}\" to \"{renamed.Trim()}\".", MessageType.None);
                            else
                                SetMessage(error, MessageType.Warning);
                        }

                        CountLabel(id);

                        if (GUILayout.Button("Remove", GUILayout.Width(64)) &&
                            EditorUtility.DisplayDialog("Remove Int Tag",
                                $"Remove \"{name}\" (id {id})?\n\nObjects and graphs that use it keep the id and will show \"Missing Tag ({id})\". The id won't be reused.",
                                "Remove", "Cancel"))
                        {
                            IntTagsEditorData.RemoveTag(id);
                            SetMessage($"Removed \"{name}\".", MessageType.None);
                            GUIUtility.ExitGUI();
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("New", GUILayout.Width(40));
                _newName = EditorGUILayout.TextField(_newName);
                if (GUILayout.Button("Add Tag", GUILayout.Width(80)))
                {
                    int id = IntTagsEditorData.AddTag(_newName, out var error);
                    if (id == IntTags.Untagged)
                        SetMessage(error, MessageType.Warning);
                    else
                    {
                        SetMessage($"Added \"{_newName.Trim()}\" with id {id}.", MessageType.None);
                        _newName = "";
                        GUI.FocusControl(null);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_message))
                EditorGUILayout.HelpBox(_message, _messageType);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                var next = data != null ? data.NextId : IntTags.FirstUserTag;
                EditorGUILayout.LabelField($"Next id: {next}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(data == null))
                {
                    if (GUILayout.Button("Select Asset", GUILayout.Width(100)))
                    {
                        Selection.activeObject = data;
                        EditorGUIUtility.PingObject(data);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private static void Row(int id, string name)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(id.ToString(), GUILayout.Width(40));
                EditorGUILayout.TextField(name);
                CountLabel(id);
                GUILayout.Space(68);
            }
        }

        // How many objects in the open scenes have the tag right now: a quick "is this used?" check.
        private static void CountLabel(int id)
        {
            int count = id == IntTags.Untagged ? 0 : IntTagRegistry.Count(id);
            EditorGUILayout.LabelField(count > 0 ? $"{count} in scene" : "", EditorStyles.miniLabel, GUILayout.Width(80));
        }

        private void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }
    }
}
