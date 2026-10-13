using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// The Int Tag dropdown: every tag by name, then "Add Tag..." and "Manage Tags...". Shared by the Inspector drawer
    /// and the graph node field, so both behave the same. A '/' in a tag name groups it into a submenu.
    /// </summary>
    public static class IntTagMenu
    {
        public const string SettingsPath = "Project/Schematics/Int Tags";

        /// <summary>
        /// Shows the menu under <paramref name="rect"/>. <paramref name="onPick"/> gets the chosen (or newly made) id.
        /// Pass int.MinValue as <paramref name="current"/> for "mixed values" (nothing checked).
        /// </summary>
        public static void Show(Rect rect, int current, Action<int> onPick)
        {
            var menu = new GenericMenu();
            bool userSeparator = false;

            foreach (var (id, name) in IntTags.All)
            {
                if (!userSeparator && !IntTags.IsDefault(id))
                {
                    menu.AddSeparator("");
                    userSeparator = true;
                }
                int picked = id;
                menu.AddItem(new GUIContent(name), id == current, () => onPick(picked));
            }

            if (current != int.MinValue && !IntTags.Exists(current))
            {
                menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent(IntTags.NameOf(current)), true);
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Add Tag..."), false, () => AddIntTagWindow.Open(onPick));
            menu.AddItem(new GUIContent("Manage Tags..."), false, OpenSettings);
            menu.DropDown(rect);
        }

        /// <summary>
        /// A multi-select version for tag lists: checked items are in <paramref name="selected"/>; picking one toggles it.
        /// </summary>
        public static void ShowToggles(Rect rect, IReadOnlyCollection<int> selected, Action<int, bool> onToggle)
        {
            var menu = new GenericMenu();
            bool userSeparator = false;
            var set = new HashSet<int>(selected);

            foreach (var (id, name) in IntTags.All)
            {
                if (id == IntTags.Untagged)
                    continue;
                if (!userSeparator && !IntTags.IsDefault(id))
                {
                    menu.AddSeparator("");
                    userSeparator = true;
                }
                int picked = id;
                bool on = set.Contains(id);
                menu.AddItem(new GUIContent(name), on, () => onToggle(picked, !on));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Add Tag..."), false, () => AddIntTagWindow.Open(id => onToggle(id, true)));
            menu.AddItem(new GUIContent("Manage Tags..."), false, OpenSettings);
            menu.DropDown(rect);
        }

        public static void OpenSettings() => SettingsService.OpenProjectSettings(SettingsPath);
    }
}
