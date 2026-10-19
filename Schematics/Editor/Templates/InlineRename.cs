using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// Renaming in place: swaps a label for a text field until Enter (or clicking away) applies it or Escape cancels.
    /// Shared by the Project Manager's grids, cards and folder tree so they all rename the same way.
    /// </summary>
    public static class InlineRename
    {
        /// <summary>
        /// Starts renaming. <paramref name="apply"/> gets the trimmed new name (only when it changed and isn't empty);
        /// <paramref name="finished"/> runs either way, after the label is back.
        /// </summary>
        public static TextField Begin(Label label, string current, Action<string> apply, Action finished = null)
        {
            var host = label?.parent;
            if (host == null)
                return null;

            var field = new TextField { value = current ?? "" };
            field.AddToClassList("inline-rename");
            label.style.display = DisplayStyle.None;
            host.Insert(host.IndexOf(label) + 1, field);

            bool done = false;
            void Finish(bool commit)
            {
                if (done) return;
                done = true;
                string value = field.value?.Trim();
                field.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;
                if (commit && !string.IsNullOrEmpty(value) && value != current)
                    apply?.Invoke(value);
                finished?.Invoke();
            }

            // Only Enter and Escape are taken here: every other key has to reach the text field, or nothing can be typed.
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { Finish(true); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Escape) { Finish(false); evt.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            // After the text field has handled a key, it goes no further (arrow keys mustn't move a grid's selection).
            field.RegisterCallback<KeyDownEvent>(evt => evt.StopPropagation());
            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            // Clicks inside the field are the field's (not the tile's or row's, which would select, open or drag).
            field.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            field.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            field.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());

            // Focus once the field is laid out (focusing a field that isn't in a panel yet does nothing).
            field.schedule.Execute(() =>
            {
                if (done) return;
                field.Focus();
                field.SelectAll();
            }).ExecuteLater(1);
            return field;
        }

        /// <summary>A file name with the characters files can't have replaced.</summary>
        public static string SafeFileName(string name)
        {
            name ??= "";
            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Trim();
        }

        /// <summary>Renames an asset or folder; logs and returns false when Unity refuses.</summary>
        public static bool RenameAsset(string path, string newName)
        {
            string error = AssetDatabase.RenameAsset(path, SafeFileName(newName));
            if (string.IsNullOrEmpty(error))
                return true;
            Debug.LogWarning($"[Project Manager] Couldn't rename '{path}': {error}");
            return false;
        }
    }
}
