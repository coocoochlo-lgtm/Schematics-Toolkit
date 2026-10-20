using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Who made an audio clip, for credits: a creator set by hand (on import or with Edit Creator…) is kept in the
    /// clip's import settings (its .meta file, so it moves, renames and commits with the clip); otherwise it's whatever
    /// the file's own tags say (<see cref="AudioMetadata"/>).
    /// </summary>
    public static class AudioCredits
    {
        private const string Prefix = "remedy.creator:";

        /// <summary>The clip's creator: the one set by hand, else the file's own, else null.</summary>
        public static string CreatorOf(string assetPath)
            => Stored(assetPath) ?? AudioMetadata.ArtistOf(assetPath);

        private static string Stored(string assetPath)
        {
            var importer = string.IsNullOrEmpty(assetPath) ? null : AssetImporter.GetAtPath(assetPath);
            string data = importer != null ? importer.userData : null;
            if (string.IsNullOrEmpty(data) || !data.StartsWith(Prefix, StringComparison.Ordinal))
                return null;
            string creator = data.Substring(Prefix.Length).Trim();
            return creator.Length > 0 ? creator : null;
        }

        /// <summary>
        /// Sets (or, with an empty name, clears) the clip's creator. Leaves an import setting another tool already uses
        /// alone (and says so).
        /// </summary>
        public static bool SetCreator(string assetPath, string creator)
        {
            var importer = string.IsNullOrEmpty(assetPath) ? null : AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                return false;
            string data = importer.userData ?? "";
            if (data.Length > 0 && !data.StartsWith(Prefix, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Project Manager] '{assetPath}' keeps other data in its import settings, so its creator wasn't saved there.");
                return false;
            }
            creator = (creator ?? "").Trim().Replace("\n", " ");
            importer.userData = creator.Length > 0 ? Prefix + creator : "";
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>A small window to type a clip's creator.</summary>
        public static void Edit(string assetPath, Action changed) => CreatorWindow.Open(assetPath, changed);

        private sealed class CreatorWindow : EditorWindow
        {
            private string _path;
            private Action _changed;

            public static void Open(string path, Action changed)
            {
                var window = CreateInstance<CreatorWindow>();
                window._path = path;
                window._changed = changed;
                window.titleContent = new GUIContent("Creator");
                window.minSize = window.maxSize = new Vector2(320, 80);
                window.ShowUtility();
            }

            private void CreateGUI()
            {
                var root = rootVisualElement;
                root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 8;
                string fromFile = AudioMetadata.ArtistOf(_path);
                var field = new TextField("Creator") { value = CreatorOf(_path) ?? "" };
                field.tooltip = string.IsNullOrEmpty(fromFile) ? "The file doesn't name one." : $"The file says: {fromFile}. Leave empty to use that.";
                root.Add(field);
                void Save()
                {
                    string value = field.value?.Trim() ?? "";
                    SetCreator(_path, value == fromFile ? "" : value);
                    _changed?.Invoke();
                    Close();
                }
                field.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) Save();
                    else if (evt.keyCode == KeyCode.Escape) Close();
                });
                var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 6 } };
                buttons.Add(new Button(Close) { text = "Cancel" });
                buttons.Add(new Button(Save) { text = "Save" });
                root.Add(buttons);
                field.schedule.Execute(() => field.Focus()).ExecuteLater(1);
            }
        }
    }
}
