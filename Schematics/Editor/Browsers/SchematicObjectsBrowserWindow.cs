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
    /// The Schematics section hosted in Project Manager. A sidebar groups the project's Schematics by kind (Objects,
    /// Singletons, Timelines, and any kind marked with <see cref="SchematicTypeAttribute"/>), each with the folders that
    /// hold that kind, like assemblies in a solution explorer. The grid shows the selected kind (and folder). Objects
    /// are Schematic prefabs: double-click opens the prefab, drag a tile into the Scene view to place it. Other kinds
    /// are graph assets: double-click opens the graph, drag a tile into a field to assign it. Everything can be renamed
    /// (double-click its name, or F2), and new ones can be empty or (for Objects) made from a template.
    /// </summary>
    /// <remarks>
    /// Objects come from <see cref="SchematicObjectIndex"/> and graphs from <see cref="SchematicTypes"/>; both follow
    /// imports, so the page stays current without rescanning the project.
    /// </remarks>
    public class SchematicObjectsBrowserWindow : SchematicBrowserWindow
    {
        private const string Section = "Schematics";
        private const string TagSection = "SchematicObjects"; // the tag set's original name, kept so existing tags stay

        private readonly Dictionary<string, SchematicTemplate> _graphEntries = new(StringComparer.Ordinal);
        private ProjectFolderTree _tree;
        private Toggle _levelDesignOnly;
        private Toggle _folderView;
        private string _renameWhenListed;

        /// <summary>Opens Project Manager on the Schematics section.</summary>
        public static SchematicObjectsBrowserWindow Open() => ProjectManagerWindow.Open(ProjectManagerWindow.Section.SchematicObjects).ObjectsPage;

        protected override string PrefsKey => "Remedy.Schematics.ObjectsBrowser";
        protected override string Title => "Schematics";
        protected override string IconName => "PrefabVariant Icon";
        protected override string Hint => "Double-click to open, double-click a name to rename, or drag out.";

        private SchematicTypes.Info Kind => SchematicTypes.Find(_tree?.SelectedGroup?.Key) ?? SchematicTypes.All.FirstOrDefault();

        protected override void OnEnable()
        {
            base.OnEnable();
            SchematicObjectIndex.Changed += OnAssetsChanged;
            SchematicTypes.Changed += OnAssetsChanged;
        }

        protected override void OnDisable()
        {
            SchematicObjectIndex.Changed -= OnAssetsChanged;
            SchematicTypes.Changed -= OnAssetsChanged;
            base.OnDisable();
        }

        private void OnAssetsChanged() => Grid?.schedule.Execute(() =>
        {
            _tree?.Rebuild();
            Grid?.Refresh();
            RenamePending();
        });

        // Folders: the grid shows the open folder's subfolders and the assets right in it. Off: everything in the
        // selected kind (and folder, and the folders below it), without opening folders.
        private bool FolderView => EditorPrefs.GetBool($"{PrefsKey}.FolderView", true);

        protected override void BuildHeader(VisualElement header)
        {
            _folderView = HeaderToggle("Folders", "On: browse folder by folder. Off: show everything in the selection at once.", "FolderView", true, _ => Grid?.Refresh());
            header.Add(_folderView);
            _levelDesignOnly = HeaderToggle("Level Design only", "Only show objects tagged Level Design.", "LevelDesignOnly", false, _ => ApplyFilter());
            header.Add(_levelDesignOnly);

            header.Add(new Button(SchematicObjectIndex.Rescan)
            {
                text = "Rescan",
                tooltip = "Scan the whole project for Schematic prefabs again (it normally keeps up by itself).",
            });
        }

        protected override VisualElement BuildContent()
        {
            var body = new VisualElement();
            body.AddToClassList("schematics-page");

            _tree = new ProjectFolderTree(Section);
            _tree.AddToClassList("schematics-page__tree");
            _tree.ExtraMenu = AddTreeMenu;
            _tree.SelectionChanged += OnTreeSelectionChanged;
            _tree.FoldersChanged += () => Grid?.Refresh();
            body.Add(_tree);

            var grid = base.BuildContent();
            grid.AddToClassList("schematics-page__grid");
            body.Add(grid);

            _tree.SetGroups(SchematicTypes.All.Select(kind => new ProjectFolderTree.Group
            {
                Key = kind.Key,
                Label = kind.Name,
                Icon = kind.Attribute.Icon,
                DefaultFolder = kind.DefaultFolder,
                Folders = () => FoldersOf(kind),
            }));
            OnTreeSelectionChanged();
            return body;
        }

        protected override void ConfigureGrid(SchematicTemplateGrid grid)
        {
            grid.ShowCreateTile = true;
            grid.CreateTileFirst = true;
            grid.CreateTileAction = ShowCreateMenu;
            grid.TileMenu = BuildMenu;
            grid.AdditionalTileMenu = (menu, entry) =>
                ProjectManagerTags.AppendMenu(menu, TagSection, AssetOf(entry), () => Grid?.Refresh());
            grid.Source = Entries;
            grid.CategoriesOf = entry => ProjectManagerTags.Categories(TagSection, AssetOf(entry),
                entry.Prefab != null ? SchematicObjectIndex.TagsOf(entry) : null);
            grid.ThumbnailOf = entry => entry.Prefab != null ? null : ThumbnailOf(entry);
            grid.DragAssetOf = entry => entry.Prefab != null ? null : AssetOf(entry);
            grid.Rename = RenameEntry;
            grid.FoldersSource = () => FolderView && _tree != null ? _tree.VisibleChildren(_tree.SelectedGroup, _tree.SelectedFolder) : null;
            grid.FolderOpened += folder => _tree?.Select(_tree.SelectedGroup?.Key, folder);
            grid.FolderMenu = BuildFolderMenu;
            grid.Moved += _ => OnAssetsChanged();
            // Dropping on the grid itself moves things into the open folder.
            ProjectAssetOps.MakeDropTarget(grid, () => _tree?.TargetFolder, _ => OnAssetsChanged(), "schematic-grid--drop");
            ApplyFilter();
        }

        private void OnTreeSelectionChanged()
        {
            var kind = Kind;
            if (Grid == null || kind == null)
                return;
            Grid.CreateTileLabel = kind.ListsPrefabs ? "New Schematic Object…" : $"{kind.NewAssetName}…";
            Grid.CreateTileTooltip = kind.ListsPrefabs
                ? "A new Schematic Object prefab: empty, or from a template."
                : $"A new {kind.Name} graph in the selected folder.";
            Grid.EmptyMessage = $"No {kind.Name} here yet.\nUse the + tile to make one.";
            if (_levelDesignOnly != null)
                _levelDesignOnly.style.display = kind.ListsPrefabs ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyFilter();
            Grid.Refresh();
        }

        private void ApplyFilter()
        {
            if (Grid == null) return;
            bool levelDesignOnly = EditorPrefs.GetBool($"{PrefsKey}.LevelDesignOnly", false) && Kind?.ListsPrefabs == true;
            Grid.Filter = levelDesignOnly ? new Func<SchematicTemplate, bool>(entry => entry.IsLevelDesign) : null;
        }

        #region Entries

        // The selected kind's Schematics, in the selected folder (and the folders below it).
        private IEnumerable<SchematicTemplate> Entries()
        {
            var kind = Kind;
            if (kind == null)
                return Enumerable.Empty<SchematicTemplate>();
            IEnumerable<SchematicTemplate> entries = kind.ListsPrefabs ? PrefabsOf(kind) : GraphsOf(kind);
            string folder = _tree?.SelectedFolder;
            if (FolderView)
            {
                // Only what's right in the open folder; its subfolders are tiles of their own.
                string open = folder ?? (_tree?.SelectedGroup != null ? ProjectFolderTree.BaseFolder(_tree.SelectedGroup) : "Assets");
                return entries.Where(e => string.Equals(Path.GetDirectoryName(PathOf(e))?.Replace('\\', '/'), open, StringComparison.Ordinal));
            }
            if (string.IsNullOrEmpty(folder))
                return entries;
            return entries.Where(e => PathOf(e).StartsWith(folder + "/", StringComparison.Ordinal));
        }

        // Schematic prefabs listed by this kind: those whose graph's kind lists prefabs and is this one. Every other
        // prefab (no graph yet, or a graph of a kind that lists assets, like a Timeline Graph) is in the first such kind.
        private static IEnumerable<SchematicTemplate> PrefabsOf(SchematicTypes.Info kind)
        {
            var fallback = SchematicTypes.All.FirstOrDefault(k => k.ListsPrefabs);
            return SchematicObjectIndex.All.Where(e =>
            {
                var graph = SchematicObjectFactory.GraphOf(e.Prefab);
                var own = graph != null ? SchematicTypes.Of(graph.GetType()) : null;
                return (own != null && own.ListsPrefabs ? own : fallback) == kind;
            });
        }

        private IEnumerable<SchematicTemplate> GraphsOf(SchematicTypes.Info kind)
        {
            foreach (var path in SchematicTypes.AssetPaths(kind))
            {
                if (!_graphEntries.TryGetValue(path, out var entry))
                    _graphEntries[path] = entry = new SchematicTemplate();
                entry.Name = Path.GetFileNameWithoutExtension(path);
                entry.Description = path;
                entry.Category = Path.GetDirectoryName(path)?.Replace('\\', '/');
                yield return entry;
            }
        }

        private IEnumerable<string> FoldersOf(SchematicTypes.Info kind)
            => (kind.ListsPrefabs ? PrefabsOf(kind).Select(PathOf) : SchematicTypes.AssetPaths(kind))
                .Select(p => Path.GetDirectoryName(p)?.Replace('\\', '/'))
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct();

        private static string PathOf(SchematicTemplate entry)
            => entry.Prefab != null ? AssetDatabase.GetAssetPath(entry.Prefab) : entry.Description ?? "";

        private static UnityEngine.Object AssetOf(SchematicTemplate entry)
            => entry == null ? null : entry.Prefab != null ? entry.Prefab : AssetDatabase.LoadMainAssetAtPath(entry.Description ?? "");

        private static Texture ThumbnailOf(SchematicTemplate entry)
        {
            var asset = AssetOf(entry);
            var kind = asset != null ? SchematicTypes.Of(asset.GetType()) : null;
            var icon = SchematicBrowsers.Icon(kind?.Attribute.Icon);
            return icon != null ? icon : AssetPreview.GetMiniThumbnail(asset);
        }

        #endregion

        #region Making and renaming

        private void ShowCreateMenu()
        {
            var kind = Kind;
            if (kind == null)
                return;
            string folder = _tree?.TargetFolder ?? kind.DefaultFolder;
            if (!kind.ListsPrefabs)
            {
                CreateIn(kind, folder);
                return;
            }
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Empty Schematic Object"), false, () => CreateIn(kind, folder));
            menu.AddItem(new GUIContent("From Template…"), false, () =>
            {
                ProjectFolderTree.CreateFolderPath(folder);
                CreateSchematicObjectWindow.ShowForProject(folder);
            });
            menu.ShowAsContext();
        }

        private void CreateIn(SchematicTypes.Info kind, string folder)
        {
            // A kind's default folder is made for it, so it shows in that kind's tree even before anything is in it.
            bool isDefault = string.IsNullOrEmpty(folder) || folder == kind.DefaultFolder;
            var asset = SchematicTypes.Create(kind, folder);
            if (asset == null)
                return;
            if (isDefault)
                ProjectFolderMarks.Mark(Section, kind.Key, kind.DefaultFolder);
            _renameWhenListed = AssetDatabase.GetAssetPath(asset);
            OnAssetsChanged();
        }

        // Starts renaming a just-made Schematic once the index lists it (prefabs are listed on the next editor tick).
        private void RenamePending()
        {
            if (string.IsNullOrEmpty(_renameWhenListed) || Grid == null)
                return;
            var entry = Grid.Visible.FirstOrDefault(e => PathOf(e) == _renameWhenListed);
            if (entry == null)
                return;
            _renameWhenListed = null;
            Grid.BeginRename(entry);
        }

        private bool RenameEntry(SchematicTemplate entry, string name)
        {
            string path = PathOf(entry);
            if (string.IsNullOrEmpty(path))
                return false;
            string error = AssetDatabase.RenameAsset(path, SafeName(name));
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[Schematics] Couldn't rename '{path}': {error}");
                return false;
            }
            if (entry.Prefab == null)
            {
                // The entry follows the asset to its new path.
                _graphEntries.Remove(path);
                string renamed = Path.GetDirectoryName(path)?.Replace('\\', '/') + "/" + SafeName(name) + Path.GetExtension(path);
                entry.Description = renamed;
                entry.Name = Path.GetFileNameWithoutExtension(renamed);
                _graphEntries[renamed] = entry;
            }
            return true;
        }

        private static string SafeName(string name)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name.Trim();
        }

        private void AddTreeMenu(GenericMenu menu, ProjectFolderTree.Group group, string folder)
        {
            var kind = SchematicTypes.Find(group.Key);
            if (kind == null)
                return;
            string target = folder ?? kind.DefaultFolder;
            menu.AddItem(new GUIContent(kind.ListsPrefabs ? "New Empty Schematic Object" : $"New {kind.NewAssetName.Replace("New ", "")}"), false, () =>
            {
                _tree.Select(group.Key, folder);
                CreateIn(kind, target);
            });
            if (kind.ListsPrefabs)
                menu.AddItem(new GUIContent("New Schematic Object From Template…"), false, () =>
                {
                    ProjectFolderTree.CreateFolderPath(target);
                    CreateSchematicObjectWindow.ShowForProject(target);
                });
        }

        #endregion

        protected override void OnPicked(SchematicTemplate entry)
        {
            if (entry == null) return;
            if (entry.Prefab != null)
                OpenPrefab(entry.Prefab);
            else if (AssetOf(entry) is { } asset)
                AssetDatabase.OpenAsset(asset);
        }

        private void BuildMenu(DropdownMenu menu, SchematicTemplate entry)
        {
            var prefab = entry.Prefab;
            if (prefab == null)
            {
                var asset = AssetOf(entry);
                if (asset == null) return;
                menu.AppendAction("Open Graph", _ => AssetDatabase.OpenAsset(asset));
                menu.AppendAction("Rename", _ => Grid?.BeginRename(entry));
                menu.AppendAction("Show in Folder Tree", _ => Reveal(entry));
                menu.AppendAction("Ping in Project", _ => Ping(asset));
                menu.AppendSeparator();
                menu.AppendAction("Delete", _ => DeleteEntry(entry));
                return;
            }

            menu.AppendAction("Open Prefab", _ => OpenPrefab(prefab));
            menu.AppendAction("Open Schematic Graph", _ => SchematicObjectFactory.OpenGraph(prefab),
                SchematicObjectFactory.GraphOf(prefab) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Rename", _ => Grid?.BeginRename(entry));
            menu.AppendAction("Show in Folder Tree", _ => Reveal(entry));
            menu.AppendAction("Ping in Project", _ => Ping(prefab));
            menu.AppendAction("Place in Scene", _ => SchematicObjectFactory.InstantiateInScene(entry));
            menu.AppendSeparator();

            var template = SchematicTemplates.Find(prefab);
            if (template == null)
                menu.AppendAction("Make Template", _ => MakeTemplate(prefab));
            else
                menu.AppendAction("Show Template in Catalog", _ => Ping(SchematicTemplates.CatalogOf(template)));

            bool levelDesign = template != null ? SchematicTemplates.IsLevelDesign(template) : SchematicTemplates.HasLevelDesignTag(prefab);
            menu.AppendAction("Level Design", _ => SetLevelDesign(prefab, !levelDesign),
                levelDesign ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendAction("Refresh Thumbnail", _ => SchematicTemplateThumbnails.Invalidate(prefab));
            menu.AppendSeparator();
            menu.AppendAction("Delete", _ => DeleteEntry(entry));
        }

        // Selects the entry's folder in the sidebar, so it's easy to see where it lives.
        private void Reveal(SchematicTemplate entry)
        {
            var kind = Kind;
            if (kind == null || _tree == null) return;
            _tree.Reveal(kind.Key, PathOf(entry));
            Grid?.Select(entry);
        }

        private void DeleteEntry(SchematicTemplate entry)
        {
            string path = PathOf(entry);
            if (ProjectAssetOps.Delete(new[] { path }))
            {
                _graphEntries.Remove(path);
                OnAssetsChanged();
            }
        }

        private void BuildFolderMenu(DropdownMenu menu, string folder)
        {
            var group = _tree?.SelectedGroup;
            menu.AppendAction("Open", _ => _tree?.Select(group?.Key, folder));
            menu.AppendAction("Rename", _ => Grid?.BeginRenameFolder(folder));
            menu.AppendAction("Show in Folder Tree", _ => { if (group != null) _tree.Reveal(group.Key, folder); });
            menu.AppendAction("Show in Project", _ => Ping(AssetDatabase.LoadMainAssetAtPath(folder)));
            menu.AppendSeparator();
            menu.AppendAction("Delete", _ =>
            {
                if (ProjectAssetOps.Delete(new[] { folder }))
                    OnAssetsChanged();
            });
        }

        // Lists the prefab in the default catalog (under the default category), so it shows up in the Template Browser.
        private static void MakeTemplate(GameObject prefab)
        {
            var template = SchematicTemplates.Add(prefab, null, SchematicTemplates.DefaultCategory);
            if (template != null)
                Debug.Log($"[Schematics] '{prefab.name}' is now a template (in {AssetDatabase.GetAssetPath(SchematicTemplates.CatalogOf(template))}).", prefab);
        }

        // The Level Design Int Tag on the prefab, and the template's flag too when it is one, kept in step.
        private static void SetLevelDesign(GameObject prefab, bool value)
        {
            var template = SchematicTemplates.Find(prefab);
            if (template != null)
                SchematicTemplates.SetLevelDesign(template, value);
            else
                SchematicTemplates.SetPrefabLevelDesignTag(prefab, value);
            SchematicObjectIndex.Refresh(prefab);
        }
    }
}
