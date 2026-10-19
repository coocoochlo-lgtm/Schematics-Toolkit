using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The Project Manager's tags, shared by every page: <see cref="AssetTag"/> assets (one set per section) put on
    /// assets as labels, so an asset can have any number of them. Tags live in Assets/Project Manager/Tags/&lt;Section&gt;/.
    /// </summary>
    public static class AssetTags
    {
        public const string Folder = "Assets/Project Manager/Tags";

        /// <summary>The DragAndDrop generic-data key a dragged tag chip carries (the <see cref="AssetTag"/>).</summary>
        public const string DragKey = "Remedy.AssetTag";

        private static List<AssetTag> _all;

        /// <summary>Raised when tags were made, edited, deleted, or put on or taken off assets.</summary>
        public static event Action Changed;

        private static List<AssetTag> AllTags
        {
            get
            {
                if (_all != null)
                    return _all;
                _all = AssetDatabase.FindAssets("t:" + nameof(AssetTag))
                    .Select(guid => AssetDatabase.LoadAssetAtPath<AssetTag>(AssetDatabase.GUIDToAssetPath(guid)))
                    .Where(tag => tag != null)
                    .ToList();
                return _all;
            }
        }

        /// <summary>A section's tags, A to Z.</summary>
        public static IReadOnlyList<AssetTag> In(string section)
            => AllTags.Where(tag => tag != null && string.Equals(tag.Section, section, StringComparison.OrdinalIgnoreCase))
                .OrderBy(tag => tag.TagName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>The tag's label (its asset GUID).</summary>
        public static string LabelOf(AssetTag tag) => tag == null ? null : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(tag));

        /// <summary>The section's tags an asset has.</summary>
        public static List<AssetTag> Of(UnityEngine.Object asset, string section)
        {
            if (asset == null)
                return new List<AssetTag>();
            var labels = new HashSet<string>(AssetDatabase.GetLabels(asset), StringComparer.OrdinalIgnoreCase);
            return In(section).Where(tag => labels.Contains(LabelOf(tag))).ToList();
        }

        public static bool Has(UnityEngine.Object asset, AssetTag tag)
            => asset != null && tag != null && AssetDatabase.GetLabels(asset).Contains(LabelOf(tag), StringComparer.OrdinalIgnoreCase);

        public static void Add(UnityEngine.Object asset, AssetTag tag) => Set(asset, tag, true);
        public static void Remove(UnityEngine.Object asset, AssetTag tag) => Set(asset, tag, false);
        public static void Toggle(UnityEngine.Object asset, AssetTag tag) => Set(asset, tag, !Has(asset, tag));

        private static void Set(UnityEngine.Object asset, AssetTag tag, bool on)
        {
            if (asset == null || tag == null)
                return;
            string label = LabelOf(tag);
            var labels = AssetDatabase.GetLabels(asset).ToList();
            bool has = labels.Contains(label, StringComparer.OrdinalIgnoreCase);
            if (has == on)
                return;
            if (on) labels.Add(label);
            else labels.RemoveAll(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase));
            AssetDatabase.SetLabels(asset, labels.ToArray());
            Changed?.Invoke();
        }

        /// <summary>Makes a tag in a section (or returns the one with that name).</summary>
        public static AssetTag Create(string section, string name, Color? color = null)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                return null;
            var existing = In(section).FirstOrDefault(tag => string.Equals(tag.TagName, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            string folder = $"{Folder}/{section}";
            ProjectFolderTree.CreateFolderPath(folder);
            var created = ScriptableObject.CreateInstance<AssetTag>();
            created.TagName = name;
            created.Section = section;
            created.Color = color ?? Color.HSVToRGB(Mathf.Repeat(In(section).Count * 0.618034f, 1f), 0.58f, 0.92f);
            AssetDatabase.CreateAsset(created, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{Remedy.Schematics.Templates.InlineRename.SafeFileName(name)}.asset"));
            AssetDatabase.SaveAssets();
            Invalidate();
            Changed?.Invoke();
            return created;
        }

        /// <summary>Asks, then deletes a tag (taking it off every asset that has it).</summary>
        public static bool Delete(AssetTag tag)
        {
            if (tag == null)
                return false;
            string label = LabelOf(tag);
            var tagged = AssetDatabase.FindAssets("l:" + label).Select(AssetDatabase.GUIDToAssetPath).ToList();
            if (!EditorUtility.DisplayDialog("Delete Tag",
                    $"Delete the tag '{tag.TagName}'?" + (tagged.Count > 0 ? $" It comes off the {tagged.Count} asset(s) that have it." : ""),
                    "Delete", "Cancel"))
                return false;
            foreach (var path in tagged)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null)
                    AssetDatabase.SetLabels(asset, AssetDatabase.GetLabels(asset)
                        .Where(l => !string.Equals(l, label, StringComparison.OrdinalIgnoreCase)).ToArray());
            }
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(tag));
            Invalidate();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Forgets the tag list (it's found again when next asked for).</summary>
        public static void Invalidate() => _all = null;

        internal static void RaiseChanged() => Changed?.Invoke();

        /// <summary>
        /// Moves an older tag over: makes (or finds) the tag, and every asset with the old label gets the new one
        /// instead. Used to bring Audio's and Schematics' earlier tags into this system.
        /// </summary>
        public static AssetTag Migrate(string section, string name, Color color, string oldLabel)
        {
            var tag = Create(section, name, color);
            if (tag == null || string.IsNullOrEmpty(oldLabel))
                return tag;
            string label = LabelOf(tag);
            foreach (var guid in AssetDatabase.FindAssets("l:" + oldLabel))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var labels = AssetDatabase.GetLabels(asset)
                    .Where(l => !string.Equals(l, oldLabel, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    labels.Add(label);
                AssetDatabase.SetLabels(asset, labels.ToArray());
            }
            return tag;
        }

        /// <summary>The "Tags/..." items of an asset's right-click menu: each of the section's tags, ticked when it has it.</summary>
        public static void AppendMenu(DropdownMenu menu, string section, UnityEngine.Object asset)
        {
            if (asset == null) return;
            var tags = In(section);
            if (tags.Count == 0)
                menu.AppendAction("Tags/(no tags yet: use + New Tag)", _ => { }, DropdownMenuAction.Status.Disabled);
            foreach (var tag in tags)
            {
                var t = tag;
                menu.AppendAction("Tags/" + t.TagName, _ => Toggle(asset, t),
                    Has(asset, t) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
        }

        /// <summary>Makes an element take dropped tag chips, tagging the asset it stands for.</summary>
        public static void MakeDropTarget(VisualElement element, Func<UnityEngine.Object> asset, string hoverClass = "schematic-grid__tile--drop")
        {
            element.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!(DragAndDrop.GetGenericData(DragKey) is AssetTag) || asset() == null) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                element.AddToClassList(hoverClass);
                evt.StopPropagation();
            });
            element.RegisterCallback<DragLeaveEvent>(_ => element.RemoveFromClassList(hoverClass));
            element.RegisterCallback<DragExitedEvent>(_ => element.RemoveFromClassList(hoverClass));
            element.RegisterCallback<DragPerformEvent>(evt =>
            {
                element.RemoveFromClassList(hoverClass);
                if (!(DragAndDrop.GetGenericData(DragKey) is AssetTag tag)) return;
                var target = asset();
                if (target == null) return;
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();
                EditorApplication.delayCall += () => Add(target, tag);
            });
        }

        /// <summary>Small coloured dots for an asset's tags (named in the tooltip), for a thumbnail's corner.</summary>
        public static VisualElement Dots(IReadOnlyList<AssetTag> tags)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("asset-tag-dots");
            row.style.flexDirection = FlexDirection.Row;
            foreach (var tag in tags.Take(6))
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.style.width = dot.style.height = 9;
                dot.style.marginRight = 2;
                dot.style.borderTopLeftRadius = dot.style.borderTopRightRadius = dot.style.borderBottomLeftRadius = dot.style.borderBottomRightRadius = 5;
                dot.style.backgroundColor = tag.Color;
                dot.style.borderTopWidth = dot.style.borderBottomWidth = dot.style.borderLeftWidth = dot.style.borderRightWidth = 1;
                dot.style.borderTopColor = dot.style.borderBottomColor = dot.style.borderLeftColor = dot.style.borderRightColor = new Color(0f, 0f, 0f, 0.55f);
                row.Add(dot);
            }
            return row;
        }

        internal sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Concat(deleted).Concat(moved).Any(p => p.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)))
                {
                    Invalidate();
                    EditorApplication.delayCall -= RaiseChanged;
                    EditorApplication.delayCall += RaiseChanged;
                }
            }
        }
    }

    /// <summary>Makes or edits a tag: its name and colour.</summary>
    public sealed class AssetTagWindow : EditorWindow
    {
        private AssetTag _tag;
        private string _section;
        private Action<AssetTag> _done;

        /// <summary>Edits a tag (null: makes a new one in the section).</summary>
        public static void Open(string section, AssetTag tag, Action<AssetTag> done = null)
        {
            var window = CreateInstance<AssetTagWindow>();
            window._tag = tag;
            window._section = section;
            window._done = done;
            window.titleContent = new GUIContent(tag == null ? $"New {section} Tag" : "Edit Tag");
            window.minSize = window.maxSize = new Vector2(300, 96);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 8;
            var name = new TextField("Name") { value = _tag != null ? _tag.TagName : "" };
            root.Add(name);
            var color = new ColorField("Colour")
            {
                value = _tag != null ? _tag.Color : Color.HSVToRGB(Mathf.Repeat(AssetTags.In(_section).Count * 0.618034f, 1f), 0.58f, 0.92f),
                showAlpha = false,
            };
            root.Add(color);
            void Save()
            {
                string value = name.value?.Trim();
                if (string.IsNullOrEmpty(value))
                    return;
                var tag = _tag;
                if (tag == null)
                    tag = AssetTags.Create(_section, value, color.value);
                else
                {
                    Undo.RecordObject(tag, "Edit Tag");
                    tag.TagName = value;
                    tag.Color = color.value;
                    EditorUtility.SetDirty(tag);
                    AssetDatabase.SaveAssetIfDirty(tag);
                    AssetTags.RaiseChanged();
                }
                _done?.Invoke(tag);
                Close();
            }
            name.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) Save();
                else if (evt.keyCode == KeyCode.Escape) Close();
            });
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 6 } };
            buttons.Add(new Button(Close) { text = "Cancel" });
            buttons.Add(new Button(Save) { text = _tag == null ? "Create" : "Save" });
            root.Add(buttons);
            name.schedule.Execute(() => name.Focus()).ExecuteLater(1);
        }
    }

    /// <summary>
    /// A page's tag chips: every tag of its section (shown whether or not anything has it yet), "+ New Tag", and
    /// (when filtering is on) a click on a chip shows only assets with that tag. Chips drag onto assets to tag them;
    /// right-click a chip to edit or delete it.
    /// </summary>
    public sealed class TagBar : VisualElement
    {
        private readonly string _section;
        private bool _filterEnabled;

        /// <summary>The tag being filtered by, or null.</summary>
        public AssetTag Selected { get; private set; }

        /// <summary>The filter changed (or tags changed): the page shows its assets again.</summary>
        public event Action FilterChanged;

        /// <summary>Whether clicking a chip filters (a page's "Filter by Tag" toggle). Off clears the filter.</summary>
        public bool FilterEnabled
        {
            get => _filterEnabled;
            set
            {
                _filterEnabled = value;
                if (!value) Selected = null;
                Rebuild();
                FilterChanged?.Invoke();
            }
        }

        public TagBar(string section)
        {
            _section = section;
            AddToClassList("audio-browser__tags");
            RegisterCallback<AttachToPanelEvent>(_ => { AssetTags.Changed += OnTagsChanged; Rebuild(); });
            RegisterCallback<DetachFromPanelEvent>(_ => AssetTags.Changed -= OnTagsChanged);
        }

        /// <summary>Whether an asset passes the filter.</summary>
        public bool Passes(UnityEngine.Object asset) => Selected == null || AssetTags.Has(asset, Selected);

        private void OnTagsChanged() => schedule.Execute(() =>
        {
            if (Selected == null) { Rebuild(); FilterChanged?.Invoke(); return; }
            if (!AssetTags.In(_section).Contains(Selected))
                Selected = null;
            Rebuild();
            FilterChanged?.Invoke();
        });

        public void Rebuild()
        {
            Clear();
            if (_filterEnabled)
            {
                var all = Chip("All", null, () => Select(null));
                all.EnableInClassList("audio-browser__tag-filter--selected", Selected == null);
                Add(all);
            }
            foreach (var tag in AssetTags.In(_section))
            {
                var t = tag;
                var chip = Chip(t.TagName, t, () => { if (_filterEnabled) Select(Selected == t ? null : t); });
                chip.style.backgroundColor = t.Color;
                chip.style.color = ReadableOn(t.Color);
                chip.EnableInClassList("audio-browser__tag-filter--selected", Selected == t);
                chip.tooltip = _filterEnabled ? "Click to filter; drag onto an asset to tag it." : "Drag onto an asset to tag it.";
                chip.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction("Edit Tag…", _ => AssetTagWindow.Open(_section, t));
                    evt.menu.AppendAction("Delete Tag…", _ => AssetTags.Delete(t));
                    evt.StopPropagation();
                }));
                MakeDraggable(chip, t);
                Add(chip);
            }
            var add = Chip("+ New Tag", null, () => AssetTagWindow.Open(_section, null));
            add.AddToClassList("audio-browser__tag-filter--new");
            add.tooltip = "Make a new tag, then drag it onto assets.";
            Add(add);
        }

        private void Select(AssetTag tag)
        {
            Selected = tag;
            Rebuild();
            FilterChanged?.Invoke();
        }

        private static Button Chip(string text, AssetTag tag, Action click)
        {
            var chip = new Button(click) { text = text };
            chip.AddToClassList("audio-browser__tag-filter");
            return chip;
        }

        private static Color ReadableOn(Color background)
            => background.r * 0.299f + background.g * 0.587f + background.b * 0.114f > 0.6f ? Color.black : Color.white;

        // A chip dragged a few pixels carries its tag (a click still filters).
        private static void MakeDraggable(VisualElement chip, AssetTag tag)
        {
            Vector2 press = default;
            bool pressed = false;
            chip.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pressed = true;
                press = evt.position;
            }, TrickleDown.TrickleDown);
            chip.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!pressed || (evt.pressedButtons & 1) == 0 || ((Vector2)evt.position - press).sqrMagnitude < 36f)
                    return;
                pressed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.paths = Array.Empty<string>();
                DragAndDrop.SetGenericData(AssetTags.DragKey, tag);
                DragAndDrop.StartDrag(tag.TagName);
            }, TrickleDown.TrickleDown);
            chip.RegisterCallback<PointerUpEvent>(_ => pressed = false, TrickleDown.TrickleDown);
        }
    }
}
