using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The Project Manager's sidebar, laid out like a solution explorer: each group (a kind of asset: Objects,
    /// Singletons, Music, SFX...) is a top-level foldout, and inside it the project folders that hold that kind of asset.
    /// A folder only shows in a group when something of that kind is in it (or in a folder below it), or when it was
    /// made inside that group. Folders can be made, renamed (double-click) and shown in the Project window from here.
    /// </summary>
    public sealed class ProjectFolderTree : VisualElement
    {
        /// <summary>One top-level foldout.</summary>
        public sealed class Group
        {
            /// <summary>Stable key (saved selection, folder marks).</summary>
            public string Key;
            public string Label;
            /// <summary>A built-in editor icon name, or null.</summary>
            public string Icon;
            /// <summary>
            /// The folder the group's tree starts at ("Assets/Content/Audio/Music"). Null: the whole project, from Assets.
            /// </summary>
            public string Root;
            /// <summary>Where "New Folder" on the group itself makes the folder (defaults to <see cref="Root"/>).</summary>
            public string DefaultFolder;
            /// <summary>The folders that hold this group's assets (each one's parents are shown too).</summary>
            public Func<IEnumerable<string>> Folders;
            /// <summary>Show every folder under <see cref="Root"/>, empty or not (the group owns its root).</summary>
            public bool ShowAllSubfolders;
        }

        private readonly string _section;
        private readonly string _prefsKey;
        private readonly ScrollView _scroll;
        private readonly List<Group> _groups = new();
        private readonly Dictionary<string, Row> _rows = new();
        private readonly Dictionary<string, HashSet<string>> _visible = new();
        private string _selectedGroup;
        private string _selectedFolder;
        private string _allLabel;

        /// <summary>The selection changed (a group, a folder, or the "all" row).</summary>
        public event Action SelectionChanged;

        /// <summary>Something on disk changed from the tree (a folder made or renamed).</summary>
        public event Action FoldersChanged;

        /// <summary>Adds items to a row's right-click menu: the group, and the folder (null for the group itself).</summary>
        public Action<GenericMenu, Group, string> ExtraMenu { get; set; }

        /// <summary>The selected group, or null when the "all" row (or nothing) is selected.</summary>
        public Group SelectedGroup => _groups.FirstOrDefault(g => g.Key == _selectedGroup);

        /// <summary>The selected folder ("Assets/..."), or null when a whole group (or "all") is selected.</summary>
        public string SelectedFolder => _selectedFolder;

        /// <summary>
        /// The folder new things go in: the selected folder, else the selected group's default folder (made if missing).
        /// </summary>
        public string TargetFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(_selectedFolder) && AssetDatabase.IsValidFolder(_selectedFolder))
                    return _selectedFolder;
                var group = SelectedGroup;
                string folder = group?.DefaultFolder ?? group?.Root;
                return string.IsNullOrEmpty(folder) ? null : folder;
            }
        }

        /// <param name="section">The Project Manager section ("Schematics", "Audio"), for saved state and folder marks.</param>
        public ProjectFolderTree(string section)
        {
            _section = section;
            _prefsKey = "Remedy.ProjectManager.Tree." + section;
            AddToClassList("folder-tree");
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.AddToClassList("folder-tree__scroll");
            Add(_scroll);
            _selectedGroup = EditorPrefs.GetString(_prefsKey + ".Group", null);
            _selectedFolder = EditorPrefs.GetString(_prefsKey + ".Folder", null);
            if (string.IsNullOrEmpty(_selectedFolder)) _selectedFolder = null;
            if (string.IsNullOrEmpty(_selectedGroup)) _selectedGroup = null;
        }

        /// <summary>Adds a row above the groups that selects everything (e.g. "All Audio").</summary>
        public void SetAllRow(string label) => _allLabel = label;

        public void SetGroups(IEnumerable<Group> groups)
        {
            _groups.Clear();
            _groups.AddRange(groups);
            if (_selectedGroup != null && _groups.All(g => g.Key != _selectedGroup))
                _selectedGroup = null;
            if (_selectedGroup == null && _allLabel == null && _groups.Count > 0)
                _selectedGroup = _groups[0].Key;
            Rebuild();
        }

        /// <summary>Selects a group (and a folder in it, or null for the whole group).</summary>
        public void Select(string groupKey, string folder)
        {
            var selectedGroup = _groups.FirstOrDefault(g => g.Key == groupKey);
            if (selectedGroup != null && folder == Base(selectedGroup))
                folder = null;
            _selectedGroup = groupKey;
            _selectedFolder = string.IsNullOrEmpty(folder) ? null : folder;
            EditorPrefs.SetString(_prefsKey + ".Group", _selectedGroup ?? "");
            EditorPrefs.SetString(_prefsKey + ".Folder", _selectedFolder ?? "");
            if (_selectedFolder != null && _selectedGroup != null)
                ExpandTo(_selectedGroup, _selectedFolder);
            foreach (var row in _rows.Values)
                row.SetSelected(row.GroupKey == _selectedGroup && row.Folder == _selectedFolder && !row.IsAll
                    || row.IsAll && _selectedGroup == null);
            SelectionChanged?.Invoke();
        }

        /// <summary>Rebuilds the rows (after assets or folders changed).</summary>
        public void Rebuild()
        {
            _scroll.Clear();
            _rows.Clear();

            if (_allLabel != null)
            {
                var all = new Row(this, null, null, _allLabel, "Folder Icon", 0, false) { IsAll = true };
                _rows["*"] = all;
                _scroll.Add(all);
            }

            _visible.Clear();
            foreach (var group in _groups)
            {
                var folders = VisibleFolders(group);
                _visible[group.Key] = folders;
                var groupRow = new Row(this, group, null, group.Label, group.Icon ?? "Folder Icon", 0, ChildrenOf(group, folders, Base(group)).Any());
                _rows[Key(group.Key, null)] = groupRow;
                _scroll.Add(groupRow);
                AddChildren(groupRow, group, folders, Base(group), 1);
            }

            // A selected folder that's no longer shown (deleted, moved out) falls back to its group.
            if (_selectedFolder != null && !_rows.ContainsKey(Key(_selectedGroup, _selectedFolder)))
                _selectedFolder = null;
            foreach (var row in _rows.Values)
                row.SetSelected(row.IsAll ? _selectedGroup == null : row.GroupKey == _selectedGroup && row.Folder == _selectedFolder);
        }

        /// <summary>
        /// The folders shown directly inside a folder of a group (or inside the group itself when folder is null): what
        /// the folder view shows as folder tiles.
        /// </summary>
        public IEnumerable<string> VisibleChildren(Group group, string folder)
        {
            if (group == null)
                return Enumerable.Empty<string>();
            if (!_visible.TryGetValue(group.Key, out var folders))
                _visible[group.Key] = folders = VisibleFolders(group);
            return ChildrenOf(group, folders, string.IsNullOrEmpty(folder) ? Base(group) : folder).ToList();
        }

        /// <summary>The folder a group's tree starts at (its root, or Assets).</summary>
        public static string BaseFolder(Group group) => Base(group);

        /// <summary>Selects an asset's folder in a group, opening the tree down to it and scrolling it into view.</summary>
        public void Reveal(string groupKey, string assetPath)
        {
            var group = _groups.FirstOrDefault(g => g.Key == groupKey);
            if (group == null)
                return;
            string folder = AssetDatabase.IsValidFolder(assetPath) ? assetPath : ParentOf(assetPath);
            if (folder == Base(group))
                folder = null;
            // Make sure it's listed (it holds the asset, so it shows once the tree has caught up).
            Rebuild();
            if (folder != null && !_rows.ContainsKey(Key(groupKey, folder)))
                folder = null;
            Select(groupKey, folder);
            if (_rows.TryGetValue(Key(groupKey, folder), out var row))
                schedule.Execute(() => _scroll.ScrollTo(row));
        }

        private void AddChildren(Row parent, Group group, HashSet<string> folders, string under, int depth)
        {
            foreach (var folder in ChildrenOf(group, folders, under))
            {
                var row = new Row(this, group, folder, Path.GetFileName(folder), "Folder Icon", depth, ChildrenOf(group, folders, folder).Any());
                _rows[Key(group.Key, folder)] = row;
                parent.Children.Add(row);
                AddChildren(row, group, folders, folder, depth + 1);
            }
        }

        private static IEnumerable<string> ChildrenOf(Group group, HashSet<string> folders, string under)
            => folders.Where(f => string.Equals(ParentOf(f), under, StringComparison.Ordinal))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

        private static string Base(Group group) => string.IsNullOrEmpty(group.Root) ? "Assets" : group.Root.TrimEnd('/');

        private static string ParentOf(string folder) => Path.GetDirectoryName(folder)?.Replace('\\', '/');

        // The folders a group shows: its content folders, its marked folders, (optionally) everything under its root,
        // and every folder between those and the group's base.
        private HashSet<string> VisibleFolders(Group group)
        {
            string root = Base(group);
            var shown = new HashSet<string>(StringComparer.Ordinal);
            IEnumerable<string> sources = group.Folders?.Invoke() ?? Enumerable.Empty<string>();
            sources = sources.Concat(ProjectFolderMarks.Of(_section, group.Key));
            if (group.ShowAllSubfolders && AssetDatabase.IsValidFolder(root))
                sources = sources.Concat(AllSubfolders(root));

            foreach (var source in sources)
            {
                string folder = source?.Replace('\\', '/').TrimEnd('/');
                while (!string.IsNullOrEmpty(folder) && folder.StartsWith(root + "/", StringComparison.Ordinal))
                {
                    if (!shown.Add(folder))
                        break;
                    folder = ParentOf(folder);
                }
            }
            return shown;
        }

        private static IEnumerable<string> AllSubfolders(string folder)
        {
            foreach (var child in AssetDatabase.GetSubFolders(folder))
            {
                yield return child;
                foreach (var grandchild in AllSubfolders(child))
                    yield return grandchild;
            }
        }

        private static string Key(string group, string folder) => (group ?? "") + "|" + (folder ?? "");

        private bool IsExpanded(string group, string folder)
            => EditorPrefs.GetBool($"{_prefsKey}.Open.{Key(group, folder)}", folder == null);

        private void SetExpanded(string group, string folder, bool open)
            => EditorPrefs.SetBool($"{_prefsKey}.Open.{Key(group, folder)}", open);

        private void ExpandTo(string group, string folder)
        {
            SetExpanded(group, null, true);
            for (string parent = ParentOf(folder); !string.IsNullOrEmpty(parent) && parent.Contains("/"); parent = ParentOf(parent))
                SetExpanded(group, parent, true);
            foreach (var row in _rows.Values)
                row.SyncExpanded();
        }

        #region Menus and editing

        private void ShowMenu(Row row)
        {
            var menu = new GenericMenu();
            if (row.IsAll)
                return;

            string parent = row.Folder ?? row.Group.DefaultFolder ?? row.Group.Root ?? "Assets";
            menu.AddItem(new GUIContent("New Folder"), false, () => CreateFolder(row.Group, parent));
            if (row.Folder != null)
            {
                menu.AddItem(new GUIContent("Rename"), false, row.BeginRename);
                menu.AddItem(new GUIContent("Show in Project"), false, () => Ping(row.Folder));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    if (!ProjectAssetOps.Delete(new[] { row.Folder }))
                        return;
                    if (_selectedFolder != null && (_selectedFolder == row.Folder || _selectedFolder.StartsWith(row.Folder + "/", StringComparison.Ordinal)))
                        Select(_selectedGroup, ParentOf(row.Folder));
                    Rebuild();
                    FoldersChanged?.Invoke();
                });
            }
            else if (!string.IsNullOrEmpty(row.Group.Root) && AssetDatabase.IsValidFolder(row.Group.Root))
                menu.AddItem(new GUIContent("Show in Project"), false, () => Ping(row.Group.Root));

            if (ExtraMenu != null)
            {
                menu.AddSeparator("");
                ExtraMenu(menu, row.Group, row.Folder);
            }
            menu.ShowAsContext();
        }

        /// <summary>Makes a folder inside a group (marked as that group's), selects it and starts renaming it.</summary>
        public void CreateFolder(Group group, string parent)
        {
            if (string.IsNullOrEmpty(parent))
                parent = "Assets";
            CreateFolderPath(parent);
            string path = AssetDatabase.GenerateUniqueAssetPath(parent.TrimEnd('/') + "/New Folder");
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"[Project Manager] Couldn't make a folder in '{parent}'.");
                return;
            }
            string created = AssetDatabase.GUIDToAssetPath(guid);
            ProjectFolderMarks.Mark(_section, group.Key, created);
            Rebuild();
            Select(group.Key, created);
            FoldersChanged?.Invoke();
            if (_rows.TryGetValue(Key(group.Key, created), out var row))
                schedule.Execute(row.BeginRename);
        }

        /// <summary>Makes every missing folder along a path ("Assets/A/B").</summary>
        public static void CreateFolderPath(string folder)
        {
            folder = folder?.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;
            string parent = ParentOf(folder);
            if (string.IsNullOrEmpty(parent))
                return;
            CreateFolderPath(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private void Rename(Row row, string newName)
        {
            newName = newName?.Trim();
            string old = row.Folder;
            if (old == null || string.IsNullOrEmpty(newName) || newName == Path.GetFileName(old))
                return;
            if (!InlineRename.RenameAsset(old, newName))
                return;
            string renamed = ParentOf(old) + "/" + InlineRename.SafeFileName(newName);
            if (_selectedFolder != null && (_selectedFolder == old || _selectedFolder.StartsWith(old + "/", StringComparison.Ordinal)))
                _selectedFolder = renamed + _selectedFolder.Substring(old.Length);
            EditorPrefs.SetString(_prefsKey + ".Folder", _selectedFolder ?? "");
            Rebuild();
            FoldersChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        // Something was dropped on a row and moved: the tree and its page catch up.
        private void OnMoved(List<string> moved)
        {
            Rebuild();
            FoldersChanged?.Invoke();
        }

        private static bool InRenameField(IEventHandler target)
        {
            for (var element = target as VisualElement; element != null; element = element.parent)
                if (element.ClassListContains("inline-rename"))
                    return true;
            return false;
        }

        private static void Ping(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        #endregion

        /// <summary>A group or folder: an arrow, an icon and a name, with its children below.</summary>
        private sealed class Row : VisualElement
        {
            public readonly Group Group;
            public readonly string Folder;
            public bool IsAll;
            public readonly VisualElement Children;
            public string GroupKey => Group?.Key;

            private readonly ProjectFolderTree _tree;
            private readonly VisualElement _line;
            private readonly Label _arrow;
            private readonly Label _label;
            private readonly bool _hasChildren;
            private bool _renaming;
            private Vector2 _pressPosition;
            private bool _pressed;

            public Row(ProjectFolderTree tree, Group group, string folder, string text, string icon, int depth, bool hasChildren)
            {
                _tree = tree;
                Group = group;
                Folder = folder;
                _hasChildren = hasChildren;
                AddToClassList("folder-tree__item");

                _line = new VisualElement();
                _line.AddToClassList("folder-tree__row");
                if (folder == null && group != null)
                    _line.AddToClassList("folder-tree__row--group");
                _line.style.paddingLeft = 4 + depth * 14;
                Add(_line);

                _arrow = new Label(hasChildren ? "▸" : "") { pickingMode = hasChildren ? PickingMode.Position : PickingMode.Ignore };
                _arrow.AddToClassList("folder-tree__arrow");
                _arrow.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    Toggle();
                    evt.StopPropagation();
                });
                _line.Add(_arrow);

                var image = SchematicBrowsers.Icon(icon);
                if (image != null)
                {
                    var iconElement = new Image { image = image, pickingMode = PickingMode.Ignore };
                    iconElement.AddToClassList("folder-tree__icon");
                    _line.Add(iconElement);
                }

                _label = new Label(text);
                _label.AddToClassList("folder-tree__label");
                _line.Add(_label);
                if (folder != null)
                    tooltip = folder;

                _line.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == _arrow || InRenameField(evt.target))
                        return; // the arrow toggles itself; clicks in the rename field are the field's
                    if (evt.button == 0)
                    {
                        _pressed = Folder != null;
                        _pressPosition = evt.position;
                        if (evt.clickCount == 2 && Folder != null && evt.target == _label)
                        {
                            // Applied after the click finishes, so the click doesn't steal focus from the field.
                            EditorApplication.delayCall += BeginRename;
                        }
                        else if (evt.clickCount == 2 && _hasChildren)
                            Toggle();
                        _tree.Select(IsAll ? null : GroupKey, Folder);
                        evt.StopPropagation();
                    }
                    else if (evt.button == 1)
                    {
                        _tree.Select(IsAll ? null : GroupKey, Folder);
                        _tree.ShowMenu(this);
                        evt.StopPropagation();
                    }
                }, TrickleDown.TrickleDown);

                // Folder rows drag (onto other rows or folder tiles) to move the folder.
                _line.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!_pressed || (evt.pressedButtons & 1) == 0 || ((Vector2)evt.position - _pressPosition).sqrMagnitude < 36f)
                        return;
                    _pressed = false;
                    ProjectAssetOps.StartDrag(Path.GetFileName(Folder), Folder);
                });
                _line.RegisterCallback<PointerUpEvent>(_ => _pressed = false);
                _line.RegisterCallback<PointerLeaveEvent>(_ => _pressed = false);
                // Any row but "all" takes dropped assets and folders: into its folder, or into its group's root or default folder.
                if (group != null)
                    ProjectAssetOps.MakeDropTarget(_line, () => Folder ?? group.Root ?? group.DefaultFolder, tree.OnMoved, "folder-tree__row--drop");

                Children = new VisualElement();
                Children.AddToClassList("folder-tree__children");
                Add(Children);
                SyncExpanded();
            }

            public void SyncExpanded()
            {
                bool open = _hasChildren && _tree.IsExpanded(GroupKey, Folder);
                Children.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (_hasChildren)
                    _arrow.text = open ? "▾" : "▸";
            }

            private void Toggle()
            {
                if (!_hasChildren) return;
                _tree.SetExpanded(GroupKey, Folder, !_tree.IsExpanded(GroupKey, Folder));
                SyncExpanded();
            }

            public void SetSelected(bool selected) => _line.EnableInClassList("folder-tree__row--selected", selected);

            public void BeginRename()
            {
                if (Folder == null || _renaming || panel == null)
                    return;
                _renaming = true;
                var field = InlineRename.Begin(_label, Path.GetFileName(Folder), value => _tree.Rename(this, value), () => _renaming = false);
                if (field == null)
                    _renaming = false;
                else
                    field.AddToClassList("folder-tree__rename");
            }
        }
    }
}
