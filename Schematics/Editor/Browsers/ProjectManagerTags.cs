using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    public static class ProjectManagerTags
    {
        private const string LabelPrefix = "Remedy.ProjectManager.Tag.";
        private static readonly string[] KnownSections = { "Templates", "SchematicObjects", "Audio", "Levels" };
        private static ProjectManagerTagDatabase _database;

        public static IReadOnlyList<string> Tags(string section)
            => (GetTagSet(GetDatabase(section), section, false)?.Names ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static IEnumerable<string> Of(string section, UnityEngine.Object asset)
        {
            if (asset == null)
                return Array.Empty<string>();
            var labels = new HashSet<string>(AssetDatabase.GetLabels(asset));
            return Tags(section).Where(tag => labels.Contains(Label(section, tag)));
        }

        public static IEnumerable<string> Categories(string section, UnityEngine.Object asset, IEnumerable<string> existing)
            => (existing ?? Array.Empty<string>())
                .Concat(Of(section, asset).Select(tag => "Tag: " + tag))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        public static void AppendMenu(DropdownMenu menu, string section, UnityEngine.Object asset, Action changed)
        {
            if (menu == null || asset == null)
                return;
            var labels = new HashSet<string>(AssetDatabase.GetLabels(asset));
            menu.AppendSeparator("Project Manager Tags");
            foreach (var tag in Tags(section))
            {
                string label = Label(section, tag);
                bool selected = labels.Contains(label);
                menu.AppendAction($"Project Manager Tags/{tag}", _ =>
                {
                    var current = AssetDatabase.GetLabels(asset).ToList();
                    if (current.Contains(label))
                        current.Remove(label);
                    else
                        current.Add(label);
                    AssetDatabase.SetLabels(asset, current.ToArray());
                    changed?.Invoke();
                }, selected ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            menu.AppendAction("Project Manager Tags/Add Tag…", _ =>
                TagNamePrompt.Open(section, tag => SetTag(asset, section, tag, changed)));
            bool hasSectionTags = labels.Any(value => Tags(section).Any(tag => value == Label(section, tag)));
            menu.AppendAction("Project Manager Tags/Clear Tags", _ =>
            {
                var current = AssetDatabase.GetLabels(asset).Where(value =>
                    !Tags(section).Any(tag => value == Label(section, tag))).ToArray();
                AssetDatabase.SetLabels(asset, current);
                changed?.Invoke();
            }, hasSectionTags ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        public static VisualElement BuildEditor(string section, UnityEngine.Object asset, Action changed)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.AddToClassList("project-manager__tag-row");
            foreach (var tag in Of(section, asset))
            {
                string value = tag;
                var button = new Button(() =>
                {
                    var labels = AssetDatabase.GetLabels(asset).Where(label => label != Label(section, value)).ToArray();
                    AssetDatabase.SetLabels(asset, labels);
                    changed?.Invoke();
                }) { text = value + " ×", tooltip = "Remove this tag" };
                button.AddToClassList("project-manager__tag");
                row.Add(button);
            }
            var add = new Button(() => TagNamePrompt.Open(section, tag => SetTag(asset, section, tag, changed)))
            {
                text = "+ Tag",
                tooltip = $"Add a {section} tag. Tags are separate for each Project Manager section."
            };
            add.AddToClassList("project-manager__tag");
            row.Add(add);
            return row;
        }

        private static void SetTag(UnityEngine.Object asset, string section, string tag, Action changed)
        {
            tag = (tag ?? "").Trim().Replace("|", " ");
            if (asset == null || string.IsNullOrEmpty(tag))
                return;
            var database = GetDatabase(section);
            var tagSet = GetTagSet(database, section, false);
            string existingTag = tagSet?.Names.FirstOrDefault(value =>
                string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
            if (existingTag == null)
            {
                Undo.RecordObject(database, "Add Project Manager Tag");
                tagSet ??= GetTagSet(database, section, true);
                tagSet.Names.Add(tag);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
            }
            else
                tag = existingTag;
            var labels = AssetDatabase.GetLabels(asset).ToList();
            if (!labels.Contains(Label(section, tag)))
                labels.Add(Label(section, tag));
            AssetDatabase.SetLabels(asset, labels.ToArray());
            changed?.Invoke();
        }

        private static string Label(string section, string tag)
        {
            var bytes = Encoding.UTF8.GetBytes(tag ?? "");
            return LabelPrefix + section + "." + BitConverter.ToString(bytes).Replace("-", "");
        }

        private static ProjectManagerTagDatabase GetDatabase(string section)
        {
            if (_database == null)
                _database = ProjectManagerTagDatabase.LoadOrCreate();
            foreach (string knownSection in KnownSections)
                MigrateLegacyPreferences(_database, knownSection);
            MigrateLegacyPreferences(_database, section);
            return _database;
        }

        private static ProjectManagerTagSet GetTagSet(ProjectManagerTagDatabase database, string section, bool create)
        {
            if (database == null)
                return null;
            database.Sections ??= new List<ProjectManagerTagSet>();
            var tagSet = database.Sections.FirstOrDefault(value =>
                value != null && string.Equals(value.Section, section, StringComparison.OrdinalIgnoreCase));
            if (tagSet == null && create)
            {
                tagSet = new ProjectManagerTagSet { Section = section };
                database.Sections.Add(tagSet);
            }
            if (tagSet != null)
                tagSet.Names ??= new List<string>();
            return tagSet;
        }

        private static void MigrateLegacyPreferences(ProjectManagerTagDatabase database, string section)
        {
            string key = "Remedy.ProjectManager.Tags." + Application.dataPath + "." + section;
            if (!EditorPrefs.HasKey(key))
                return;

            var tagSet = GetTagSet(database, section, true);
            bool changed = false;
            foreach (string tag in EditorPrefs.GetString(key, "")
                         .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(value => value.Trim())
                         .Where(value => !string.IsNullOrEmpty(value)))
            {
                if (tagSet.Names.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    continue;
                tagSet.Names.Add(tag);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssets();
            }
            EditorPrefs.DeleteKey(key);
        }

        private sealed class TagNamePrompt : EditorWindow
        {
            private Action<string> _accept;

            public static void Open(string section, Action<string> accept)
            {
                var window = CreateInstance<TagNamePrompt>();
                window._accept = accept;
                window.titleContent = new GUIContent("Add " + section + " Tag");
                window.minSize = new Vector2(260, 80);
                window.ShowUtility();
            }

            private void CreateGUI()
            {
                var root = rootVisualElement;
                root.style.paddingLeft = root.style.paddingRight = 8;
                root.style.paddingTop = root.style.paddingBottom = 8;
                var field = new TextField("Tag name");
                root.Add(field);
                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd } };
                buttons.Add(new Button(Close) { text = "Cancel" });
                buttons.Add(new Button(() =>
                {
                    _accept?.Invoke(field.value);
                    Close();
                }) { text = "Add" });
                root.Add(buttons);
                field.Focus();
            }
        }
    }
}
