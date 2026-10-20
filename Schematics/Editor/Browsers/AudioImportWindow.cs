using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The Audio page's import settings: before files are copied in, each one gets its kind (Music or SFX, which
    /// decides the folder it goes in), its tags, and its creator (read from the file when it says, for credits). The
    /// top row applies a kind, tag or creator to every file at once.
    /// </summary>
    public sealed class AudioImportWindow : EditorWindow
    {
        /// <summary>One file to import, and what it gets.</summary>
        public sealed class Item
        {
            public string Source;
            /// <summary>Folders below the destination to keep (an imported folder's own layout), or "".</summary>
            public string Subfolder = "";
            public bool IsMusic;
            public readonly List<AssetTag> Tags = new();
            public string Creator;
        }

        private const string Section = "Audio";
        private List<Item> _items;
        private Action<List<Item>> _import;
        private ScrollView _rows;

        public static void Open(List<Item> items, Action<List<Item>> import)
        {
            if (items == null || items.Count == 0)
                return;
            var window = CreateInstance<AudioImportWindow>();
            window._items = items;
            window._import = import;
            window.titleContent = new GUIContent(items.Count == 1 ? "Import Audio" : $"Import {items.Count} Audio Files");
            window.minSize = new Vector2(620, 260);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            if (_items == null)
            {
                Close();
                return;
            }
            var root = rootVisualElement;
            var sheet = SchematicBrowserWindow.StyleSheet;
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.AddToClassList("schematic-browser");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "schematic-browser--dark" : "schematic-browser--light");
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            // Everything at once.
            var all = new VisualElement();
            all.AddToClassList("audio-import__all");
            all.Add(new Label("All files") { style = { unityFontStyleAndWeight = FontStyle.Bold, width = 150 } });
            all.Add(KindSwitch(null, isMusic =>
            {
                foreach (var item in _items) item.IsMusic = isMusic;
                RebuildRows();
            }));
            all.Add(TagChips(null, (tag, on) =>
            {
                foreach (var item in _items)
                {
                    item.Tags.Remove(tag);
                    if (on) item.Tags.Add(tag);
                }
                RebuildRows();
            }));
            var creator = new TextField { tooltip = "Creator for every file (for credits)", style = { flexGrow = 1, minWidth = 90 } };
            creator.textEdition.placeholder = "Creator for all…";
            creator.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                foreach (var item in _items) item.Creator = creator.value;
                RebuildRows();
            });
            all.Add(creator);
            all.Add(new Button(() =>
            {
                foreach (var item in _items) item.Creator = creator.value;
                RebuildRows();
            }) { text = "Set", tooltip = "Give every file this creator" });
            root.Add(all);

            _rows = new ScrollView { style = { flexGrow = 1, marginTop = 4 } };
            root.Add(_rows);
            RebuildRows();

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 6 } };
            buttons.Add(new Label("New tags: use + New Tag on the Audio page.") { style = { flexGrow = 1, opacity = 0.6f, unityTextAlign = TextAnchor.MiddleLeft } });
            buttons.Add(new Button(Close) { text = "Cancel" });
            buttons.Add(new Button(() =>
            {
                var items = _items;
                var import = _import;
                Close();
                import?.Invoke(items);
            }) { text = _items.Count == 1 ? "Import" : $"Import {_items.Count} Files" });
            root.Add(buttons);
        }

        private void RebuildRows()
        {
            _rows.Clear();
            foreach (var item in _items)
            {
                var row = new VisualElement();
                row.AddToClassList("audio-import__row");
                var name = new Label(string.IsNullOrEmpty(item.Subfolder) ? Path.GetFileName(item.Source) : item.Subfolder + "/" + Path.GetFileName(item.Source))
                {
                    tooltip = item.Source,
                    style = { width = 150, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis, whiteSpace = WhiteSpace.NoWrap, unityTextAlign = TextAnchor.MiddleLeft },
                };
                row.Add(name);
                row.Add(KindSwitch(item, isMusic => item.IsMusic = isMusic));
                row.Add(TagChips(item, (tag, on) =>
                {
                    item.Tags.Remove(tag);
                    if (on) item.Tags.Add(tag);
                }));
                var creator = new TextField { value = item.Creator ?? "", tooltip = "Who made it (for credits)", style = { flexGrow = 1, minWidth = 90 } };
                creator.textEdition.placeholder = "Creator";
                creator.RegisterValueChangedCallback(evt => item.Creator = evt.newValue);
                row.Add(creator);
                _rows.Add(row);
            }
        }

        // Music | SFX, as two joined buttons. For the "all" row (item null) neither is lit.
        private static VisualElement KindSwitch(Item item, Action<bool> picked)
        {
            var box = new VisualElement { style = { flexDirection = FlexDirection.Row, marginRight = 6 } };
            Button music = null, sfx = null;
            void Sync()
            {
                music?.EnableInClassList("audio-browser__tag-filter--selected", item != null && item.IsMusic);
                sfx?.EnableInClassList("audio-browser__tag-filter--selected", item != null && !item.IsMusic);
            }
            music = new Button(() => { picked(true); Sync(); }) { text = "Music", tooltip = "Goes in the Music folder" };
            sfx = new Button(() => { picked(false); Sync(); }) { text = "SFX", tooltip = "Goes in the SFX folder" };
            foreach (var button in new[] { music, sfx })
            {
                button.AddToClassList("audio-browser__tag-filter");
                button.style.marginLeft = button.style.marginRight = 0;
                box.Add(button);
            }
            Sync();
            return box;
        }

        // The Audio tags as toggles: lit when the file gets it. For the "all" row they set or clear a tag on every file.
        private VisualElement TagChips(Item item, Action<AssetTag, bool> toggled)
        {
            var box = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, flexShrink = 1, marginRight = 6, maxWidth = 260 } };
            foreach (var tag in AssetTags.In(Section))
            {
                var t = tag;
                var chip = new Button { text = t.TagName, tooltip = item == null ? "Click: every file gets it. Right-click: none do." : "Click to give this file the tag (or take it off)" };
                chip.AddToClassList("audio-browser__tag-filter");
                void Paint()
                {
                    bool on = item != null && item.Tags.Contains(t);
                    chip.style.backgroundColor = on || item == null ? t.Color : new Color(t.Color.r, t.Color.g, t.Color.b, 0.25f);
                    chip.style.opacity = on || item == null ? 1f : 0.7f;
                }
                chip.clicked += () =>
                {
                    bool on = item == null || !item.Tags.Contains(t);
                    toggled(t, on);
                    Paint();
                };
                if (item == null)
                    chip.RegisterCallback<ContextClickEvent>(evt => { toggled(t, false); evt.StopPropagation(); });
                Paint();
                box.Add(chip);
            }
            return box;
        }
    }
}
