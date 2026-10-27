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
        private const string LegacyTagSection = "SchematicObjects"; // the old Project Manager tags, moved over on first open
        private const string TagSection = "Schematics";
        private TagBar _tagBar;

        private readonly Dictionary<string, SchematicTemplate> _graphEntries = new(StringComparer.Ordinal);
        private ProjectFolderTree _tree;
        private Toggle _levelDesignOnly;
        private Toggle _folderView;
        // A just-made Schematic to show (open its kind and folder, select, flash and ping it) once it's listed, and
        // whether to start renaming it then.
        private string _revealWhenListed;
        private bool _renameAfterReveal;
        // The just-made Schematic to open once its rename is done (kept as the asset: renaming changes its path).
        private UnityEngine.Object _openAfterRename;
        private int _revealTries;
        // Objects made with the template window opened from this page: the paths there were before, so the new one
        // can be found (and revealed) when it appears.
        private HashSet<string> _knownBeforeTemplate;
        private double _templateOpenedAt;

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
            EditorApplication.projectChanged += OnProjectChanged;
            AssetTags.Changed += OnAssetsChanged;
            EditorApplication.delayCall += () => ProjectManagerTags.MigrateTo(LegacyTagSection, TagSection);
        }

        // Any change in the project (imports, labels, folders, edits): the page catches up once things settle.
        private bool _projectRefreshQueued;
        private void OnProjectChanged()
        {
            if (_projectRefreshQueued) return;
            _projectRefreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                _projectRefreshQueued = false;
                SchematicTypes.Invalidate();
                OnAssetsChanged();
            };
        }

        protected override void OnDisable()
        {
            SchematicObjectIndex.Changed -= OnAssetsChanged;
            SchematicTypes.Changed -= OnAssetsChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            AssetTags.Changed -= OnAssetsChanged;
            base.OnDisable();
        }

        private void OnAssetsChanged() => Grid?.schedule.Execute(() =>
        {
            // Rebuilding would throw away a name being typed: wait for it.
            if (_tree?.Q(className: "inline-rename") != null || Grid?.Q(className: "inline-rename") != null)
            {
                Grid?.schedule.Execute(OnAssetsChanged).ExecuteLater(300);
                return;
            }
            _tree?.Rebuild();
            Grid?.Refresh();
            RenamePending();
        });

        // Folders: the grid shows the open folder's subfolders and the assets right in it. Off: everything in the
        // selected kind (and folder, and the folders below it), without opening folders.
        private bool FolderView => EditorPrefs.GetBool($"{PrefsKey}.FolderView", true);

        private bool FilterByTag => EditorPrefs.GetBool($"{PrefsKey}.FilterByTag", true);

        protected override void BuildHeader(VisualElement header)
        {
            header.Add(HeaderToggle("Filter by Tag", "Clicking a tag shows only what has it.", "FilterByTag", true, on =>
            {
                if (_tagBar != null) _tagBar.FilterEnabled = on;
            }));
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
            _tree = new ProjectFolderTree(Section);
            _tree.AddToClassList("schematics-page__tree");
            _tree.ExtraMenu = AddTreeMenu;
            _tree.SelectionChanged += OnTreeSelectionChanged;
            _tree.FoldersChanged += () => Grid?.Refresh();

            var grid = base.BuildContent();
            grid.AddToClassList("schematics-page__grid");

            // The sidebar beside the grid, at the width all Project Manager pages share (drag the divider to change it).
            var body = ProjectManagerLayout.Split(_tree, grid);
            body.AddToClassList("schematics-page");

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
            grid.AdditionalTileMenu = (menu, entry) => AssetTags.AppendMenu(menu, TagSection, AssetOf(entry));
            grid.Source = Entries;
            // Tags are shown by the tag bar (and as dots on the tiles); the names still count for search.
            grid.ShowCategories = false;
            grid.CategoriesOf = entry => AssetTags.Of(AssetOf(entry), TagSection).Select(t => t.TagName);
            grid.TileBadge = entry => SchematicCardPreviews.Badge(entry, AssetTags.Of(AssetOf(entry), TagSection));
            grid.HoverThumbnailOf = SchematicCardPreviews.Overview;
            grid.HoverZoomOf = SchematicCardPreviews.OverviewZoom;
            grid.HoverInfoOf = SchematicCardPreviews.OverviewInfo;
            grid.DrawHoverOverlayOf = SchematicCardPreviews.DrawOverviewMarks;
            _tagBar = new TagBar(TagSection) { FilterEnabled = FilterByTag };
            _tagBar.FilterChanged += () => { ApplyFilter(); Grid?.Refresh(); };
            grid.InsertBelowToolbar(_tagBar);
            grid.ThumbnailOf = entry => SchematicCardPreviews.Thumbnail(entry)
                ?? (entry.Prefab != null ? null : ThumbnailOf(entry));
            grid.DragAssetOf = entry => entry.Prefab != null ? null : AssetOf(entry);
            grid.Rename = RenameEntry;
            grid.ShareTileSize = true; // the same tile size as the Audio page
            grid.RenameFinished += _ =>
            {
                var asset = _openAfterRename;
                _openAfterRename = null;
                if (asset != null)
                    EditorApplication.delayCall += () => OpenMade(asset);
            };
            grid.BackgroundMenu = BuildBackgroundMenu;
            // Tag chips dragged onto a tile tag it.
            grid.CanDropOnTile = entry => DragAndDrop.GetGenericData(AssetTags.DragKey) is AssetTag && AssetOf(entry) != null;
            grid.DropOnTile = entry =>
            {
                if (DragAndDrop.GetGenericData(AssetTags.DragKey) is AssetTag tag && AssetOf(entry) is { } asset)
                    EditorApplication.delayCall += () => AssetTags.Add(asset, tag);
            };
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
            Grid.CreateTileLabel = kind.ListsPrefabs && kind.Creator == null ? "New Schematic Object…" : $"{kind.NewAssetName}…";
            Grid.CreateTileTooltip = kind.ListsPrefabs && kind.Creator == null
                ? "A new Schematic Object prefab: empty, or from a template."
                : kind.ListsPrefabs
                    ? $"A new {kind.Name} Schematic prefab with its own UI Builder document."
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
            var tagBar = _tagBar;
            Grid.Filter = entry => (!levelDesignOnly || entry.IsLevelDesign) && (tagBar == null || tagBar.Passes(AssetOf(entry)));
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
            if (!kind.ListsPrefabs || kind.Creator != null)
            {
                CreateIn(kind, folder);
                return;
            }
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Empty Schematic Object"), false, () => CreateIn(kind, folder));
            menu.AddItem(new GUIContent("From Template…"), false, () => CreateFromTemplate(folder));
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
            Navigate(AssetDatabase.GetAssetPath(asset), rename: true);
        }

        /// <summary>
        /// Shows a Schematic that was just made: opens its kind and folder in the sidebar, then (once the grid lists it)
        /// selects, scrolls to and flashes its tile and pings it in the Project window; optionally starts renaming it.
        /// </summary>
        private void Navigate(string path, bool rename)
        {
            if (string.IsNullOrEmpty(path)) return;
            _revealWhenListed = path;
            _renameAfterReveal = rename;
            _revealTries = 0;
            var kind = KindOfPath(path);
            if (kind != null)
                _tree?.Reveal(kind.Key, path);
            OnAssetsChanged();
        }

        private static SchematicTypes.Info KindOfPath(string path)
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == typeof(GameObject))
            {
                var graph = SchematicObjectFactory.GraphOf(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var own = graph != null ? SchematicTypes.Of(graph.GetType()) : null;
                return own != null && own.ListsPrefabs ? own : SchematicTypes.All.FirstOrDefault(k => k.ListsPrefabs);
            }
            return type != null ? SchematicTypes.Of(type) : null;
        }

        // Called after every refresh: reveals a just-made Schematic once the grid lists it (prefabs are listed on the
        // next editor tick), and spots one the template window just made.
        private void RenamePending()
        {
            if (Grid == null)
                return;
            if (_knownBeforeTemplate != null && string.IsNullOrEmpty(_revealWhenListed))
            {
                if (EditorApplication.timeSinceStartup - _templateOpenedAt > 600)
                    _knownBeforeTemplate = null;
                else
                {
                    var made = SchematicObjectIndex.All.Select(PathOf).FirstOrDefault(p => !_knownBeforeTemplate.Contains(p));
                    if (made != null)
                    {
                        _knownBeforeTemplate = null;
                        // Named here like any new one (clicking into the name ends the Project window's own rename), then opened.
                        Navigate(made, rename: true);
                        return;
                    }
                }
            }
            if (string.IsNullOrEmpty(_revealWhenListed))
                return;
            var entry = Grid.Visible.FirstOrDefault(e => PathOf(e) == _revealWhenListed);
            if (entry == null)
            {
                // Listed now but in a folder the sidebar didn't know yet: open that folder (a few tries at most).
                if (_revealTries++ < 3)
                {
                    var kind = KindOfPath(_revealWhenListed);
                    if (kind != null)
                    {
                        string path = _revealWhenListed;
                        Grid.schedule.Execute(() =>
                        {
                            _tree?.Reveal(kind.Key, path);
                            OnAssetsChanged();
                        }).ExecuteLater(100);
                    }
                }
                return;
            }
            _revealTries = 0;
            _revealWhenListed = null;
            Grid.Reveal(entry);
            var madeAsset = AssetOf(entry);
            if (madeAsset != null)
                EditorGUIUtility.PingObject(madeAsset);
            if (_renameAfterReveal)
            {
                // Opened once it's named (Prefab Mode would take the focus away from the name being typed).
                _openAfterRename = madeAsset;
                Grid.schedule.Execute(() =>
                {
                    Grid.BeginRename(entry);
                    if (!Grid.IsRenaming && _openAfterRename != null)
                    {
                        var asset = _openAfterRename; // the rename didn't start: open it now
                        _openAfterRename = null;
                        OpenMade(asset);
                    }
                }).ExecuteLater(50);
            }
            else if (madeAsset != null)
                EditorApplication.delayCall += () => OpenMade(madeAsset);
        }

        /// <summary>
        /// Opens a Schematic that was just made: a Schematic Object in Prefab Mode (where its graph opens beside the Scene
        /// view), any other kind in the graph editor.
        /// </summary>
        private static void OpenMade(UnityEngine.Object asset)
        {
            if (asset == null)
                return;
            AssetDatabase.OpenAsset(asset);
        }

        // Opens the template window to make an object in a folder, watching for the object it makes.
        private void CreateFromTemplate(string folder)
        {
            ProjectFolderTree.CreateFolderPath(folder);
            _knownBeforeTemplate = new HashSet<string>(SchematicObjectIndex.All.Select(PathOf));
            _templateOpenedAt = EditorApplication.timeSinceStartup;
            CreateSchematicObjectWindow.ShowForProject(folder);
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
            menu.AddItem(new GUIContent(kind.ListsPrefabs && kind.Creator == null ? "New Empty Schematic Object" : kind.NewAssetName), false, () =>
            {
                _tree.Select(group.Key, folder);
                CreateIn(kind, target);
            });
            if (kind.ListsPrefabs && kind.Creator == null)
                menu.AddItem(new GUIContent("New Schematic Object From Template…"), false, () => CreateFromTemplate(target));
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
                menu.AppendAction("Open Graph in New Window", _ => GraphWindows.OpenInNewWindow(asset));
                menu.AppendAction("Rename", _ => Grid?.BeginRename(entry));
                menu.AppendAction("Show in Folder Tree", _ => Reveal(entry));
                menu.AppendAction("Ping in Project", _ => Ping(asset));
                menu.AppendAction(RevealLabel, _ => EditorUtility.RevealInFinder(PathOf(entry)));
                menu.AppendSeparator();
                menu.AppendAction("Delete", _ => DeleteEntry(entry));
                return;
            }

            menu.AppendAction("Open Prefab", _ => OpenPrefab(prefab));
            menu.AppendAction("Open Schematic Graph", _ => SchematicObjectFactory.OpenGraph(prefab),
                SchematicObjectFactory.GraphOf(prefab) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Open Schematic Graph in New Window", _ => GraphWindows.OpenInNewWindow(prefab),
                SchematicObjectFactory.GraphOf(prefab) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction("Rename", _ => Grid?.BeginRename(entry));
            menu.AppendAction("Show in Folder Tree", _ => Reveal(entry));
            menu.AppendAction("Ping in Project", _ => Ping(prefab));
            menu.AppendAction(RevealLabel, _ => EditorUtility.RevealInFinder(PathOf(entry)));
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

        // Right-clicking the grid's empty space: what the Project window offers there, for the open kind and folder.
        private void BuildBackgroundMenu(DropdownMenu menu)
        {
            var kind = Kind;
            if (kind == null) return;
            string folder = _tree?.TargetFolder ?? kind.DefaultFolder;
            if (kind.ListsPrefabs)
            {
                menu.AppendAction("Create/Empty Schematic Object", _ => CreateIn(kind, folder));
                menu.AppendAction("Create/Schematic Object From Template…", _ => CreateFromTemplate(folder));
            }
            else
                menu.AppendAction($"Create/{kind.NewAssetName.Replace("New ", "")}", _ => CreateIn(kind, folder));
            menu.AppendAction("Create/Folder", _ =>
            {
                var group = _tree?.SelectedGroup;
                if (group != null) _tree.CreateFolder(group, folder);
            });
            menu.AppendAction("Create/Tag…", _ => AssetTagWindow.Open(TagSection, null));
            menu.AppendSeparator();
            bool exists = AssetDatabase.IsValidFolder(folder);
            var status = exists ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            menu.AppendAction("Show in Project", _ => Ping(AssetDatabase.LoadMainAssetAtPath(folder)), status);
            menu.AppendAction(RevealLabel, _ => EditorUtility.RevealInFinder(folder), status);
            menu.AppendSeparator();
            menu.AppendAction("Refresh", _ =>
            {
                AssetDatabase.Refresh();
                SchematicObjectIndex.Rescan();
            });
        }

        // What the OS calls its file browser.
        internal static string RevealLabel => Application.platform switch
        {
            RuntimePlatform.OSXEditor => "Reveal in Finder",
            RuntimePlatform.WindowsEditor => "Show in Explorer",
            _ => "Open in File System",
        };

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
            menu.AppendAction(RevealLabel, _ => EditorUtility.RevealInFinder(folder));
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
