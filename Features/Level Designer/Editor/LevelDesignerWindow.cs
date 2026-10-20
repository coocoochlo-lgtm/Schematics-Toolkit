using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// The Level Designer window. <b>Levels</b>: blockouts on the left (+ makes a new blockout scene, - removes one),
    /// and on the right the variants built on the selected blockout, as cards (open, duplicate, rename, remove,
    /// "Compose into build"), with + to start a new variant over the blockout and <b>Test Composed Scene</b> to play
    /// the blockout merged with picked variants. Blockouts can be grouped into folders in the list (right-click, or
    /// drag a blockout onto a folder); scenes stay where they are on disk. The reference character the jump and run
    /// rulers use is picked in the Scene view, on the blockout toolbar's Character button. <b>Palette</b>: the team's
    /// named blockout colours. The header holds the blockout X-ray toggle and opacity, the snapping step, and the
    /// Levels menu (the folder every scene goes in, moving existing scenes there, capturing a thumbnail).<br/>
    /// The Level Designer panel hosted in Project Manager (or a standalone window when Project Manager is unavailable),
    /// built with UI Toolkit.
    /// </summary>
    public class LevelDesignerWindow : EditorWindow
    {
        [MenuItem("Window/Remedy/Level Designer")]
        [MenuItem("Tools/Remedy/Level Designer")]
        public static void Open()
        {
            if (OpenProjectManager())
                return;
            // GetWindow makes (or focuses) a regular dockable window with a tab; it isn't a utility or popup window.
            var window = GetWindow<LevelDesignerWindow>(false, "Level Designer", true);
            window.Show();
        }

        private static bool OpenProjectManager()
        {
            var manager = Type.GetType("Remedy.Schematics.Browsers.ProjectManagerWindow, Remedy.Schematics.Browsers.Editor", false);
            var open = manager?.GetMethod("OpenLevelDesigner", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (open == null)
                return false;
            open.Invoke(null, null);
            return true;
        }

        private static GUIContent Title()
        {
            var icon = EditorGUIUtility.IconContent("SceneAsset Icon")?.image;
            return new GUIContent("Level Designer", icon, "Blockouts, variants and the blockout palette");
        }

        /// <summary>Opens Project Manager on the Level Designer's Palette tab.</summary>
        public static void OpenPalette()
        {
            if (OpenProjectManager())
            {
                var manager = Type.GetType("Remedy.Schematics.Browsers.ProjectManagerWindow, Remedy.Schematics.Browsers.Editor", false);
                manager?.GetMethod("OpenLevelDesignerPalette", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.Invoke(null, null);
                return;
            }
            Open();
            var window = GetWindow<LevelDesignerWindow>(false, "Level Designer", true);
            window._tab = Tab.Palette;
            window.CreateGUI();
        }

        // Values kept from when there was a Characters tab (1), so a saved window still opens the right tab.
        private enum Tab { Levels = 0, Palette = 2 }

        [SerializeField] private Tab _tab;
        [SerializeField] private int _selectedBlockout;

        private VisualElement _body;
        private ScrollView _blockoutTree;
        [SerializeField] private string _selectedFolder = "";
        private VisualElement _artPane;
        private Toggle _xrayToggle;
        private Slider _opacitySlider;

        private LevelDesignProject Project => LevelDesignerContext.Project;

        private void OnEnable()
        {
            // Set here (not only in Open) so the tab keeps its title and icon after a domain reload or when Unity
            // restores the window from the saved layout.
            titleContent = Title();
            // Small enough to dock in a narrow column next to other windows.
            minSize = new Vector2(300, 200);
            LevelDesignerContext.ModeChanged += OnModeChanged;
            LevelDesignerSettings.Changed += SyncHeader;
            LevelThumbnails.Captured += OnThumbnailCaptured;
            Undo.undoRedoPerformed += Rebuild;
            EditorApplication.focusChanged += OnEditorFocusChanged;
        }

        private void OnDisable()
        {
            LevelDesignerContext.ModeChanged -= OnModeChanged;
            LevelDesignerSettings.Changed -= SyncHeader;
            LevelThumbnails.Captured -= OnThumbnailCaptured;
            Undo.undoRedoPerformed -= Rebuild;
            EditorApplication.focusChanged -= OnEditorFocusChanged;
        }

        private void OnThumbnailCaptured(string sceneGuid)
        {
            RebuildBlockoutTree();
            if (_tab == Tab.Levels)
                BuildArtPane();
        }

        // Coming back to Unity (after a git pull, say): teammates' new thumbnails may have arrived.
        private void OnEditorFocusChanged(bool focused)
        {
            if (focused)
                OnThumbnailCaptured(null);
        }

        private void OnModeChanged()
        {
            RebuildBlockoutTree();
            if (_tab == Tab.Levels)
                BuildArtPane();
        }

        public void CreateGUI()
        {
            CreateHostedGUI(rootVisualElement);
        }

        public void CreateHostedGUI(VisualElement root, bool showPalette = false)
        {
            if (showPalette)
                _tab = Tab.Palette;
            if (_tab != Tab.Levels && _tab != Tab.Palette)
                _tab = Tab.Levels;
            root.Clear();
            root.Add(BuildHeader());
            _body = new VisualElement { style = { flexGrow = 1 } };
            root.Add(_body);
            Rebuild();
        }

        private void Rebuild()
        {
            if (_body == null)
                return;
            _body.Clear();
            if (_tab == Tab.Levels)
                BuildLevels();
            else
                BuildPalette();
            SyncHeader();
        }

        // ---------------------------------------------------------------- header

        private VisualElement BuildHeader()
        {
            var toolbar = new Toolbar();

            var levels = new ToolbarToggle { text = "Levels", value = _tab == Tab.Levels };
            var palette = new ToolbarToggle { text = "Palette", value = _tab == Tab.Palette, tooltip = "The team's named blockout colours" };
            _tabToggles = new[] { (Tab.Levels, levels), (Tab.Palette, palette) };
            levels.RegisterValueChangedCallback(evt => SwitchTab(Tab.Levels));
            palette.RegisterValueChangedCallback(evt => SwitchTab(Tab.Palette));
            toolbar.Add(levels);
            toolbar.Add(palette);
            toolbar.Add(new ToolbarSpacer { flex = true });

            _xrayToggle = new ToolbarToggle { text = "Blockout Overlay", tooltip = "While editing a variant, show its blockout as an X-ray through it" };
            _xrayToggle.RegisterValueChangedCallback(evt => LevelDesignerSettings.XRayEnabled = evt.newValue);
            toolbar.Add(_xrayToggle);

            _opacitySlider = new Slider(0.05f, 1f) { tooltip = "X-ray opacity" };
            _opacitySlider.style.width = 110;
            _opacitySlider.RegisterValueChangedCallback(evt => LevelDesignerSettings.XRayOpacity = evt.newValue);
            toolbar.Add(_opacitySlider);

            var snap = new ToolbarMenu { tooltip = "Snapping step for blockout placement and sizing" };
            void UpdateSnapText() => snap.text = LevelDesignerSettings.Snap <= 0f ? "Snap Off" : "Snap " + BlockoutShape.FormatMeters(LevelDesignerSettings.Snap) + " m";
            UpdateSnapText();
            foreach (float step in LevelDesignerSettings.SnapSteps)
            {
                float value = step;
                snap.menu.AppendAction(value <= 0f ? "Off" : BlockoutShape.FormatMeters(value) + " m",
                    _ => { LevelDesignerSettings.Snap = value; UpdateSnapText(); },
                    _ => Mathf.Approximately(LevelDesignerSettings.Snap, value) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            toolbar.Add(snap);

            var levelsMenu = new ToolbarMenu { text = "Levels", tooltip = "Where the Level Designer keeps its scenes, and thumbnails" };
            levelsMenu.menu.AppendAction("Levels Folder: " + LevelDesignerContext.LevelsRoot.Replace("/", "\u2215"), _ => { }, _ => DropdownMenuAction.Status.Disabled);
            levelsMenu.menu.AppendAction("Change Levels Folder…", _ => ChooseLevelsRoot());
            levelsMenu.menu.AppendAction("Move Existing Scenes into the Levels Folder…", _ =>
            {
                if (LevelDesignerContext.MoveScenesIntoLayout() > 0)
                    Rebuild();
            });
            levelsMenu.menu.AppendAction("Compose Flagged Variants into Builds", _ =>
            {
                LevelDesignerContext.RecordProject("Compose Variants into Builds");
                Project.ComposeVariantsOnBuild = !Project.ComposeVariantsOnBuild;
                AssetDatabase.SaveAssets();
            }, _ => Project.ComposeVariantsOnBuild ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            levelsMenu.menu.AppendSeparator();
            levelsMenu.menu.AppendAction("Capture Thumbnail of the Active Scene", _ =>
            {
                if (!LevelThumbnails.CaptureActive())
                    EditorUtility.DisplayDialog("Level Designer", "Open a blockout or a variant (and a Scene view) first.", "OK");
            });
            levelsMenu.RegisterCallback<PointerDownEvent>(_ => RefreshLevelsMenuTitle(levelsMenu), TrickleDown.TrickleDown);
            toolbar.Add(levelsMenu);
            return toolbar;
        }

        // The first (disabled) item shows the current folder; it's rebuilt before the menu opens so it's current.
        private static void RefreshLevelsMenuTitle(ToolbarMenu levelsMenu)
        {
            if (levelsMenu.menu.MenuItems().Count > 0)
                levelsMenu.menu.RemoveItemAt(0);
            levelsMenu.menu.InsertAction(0, "Levels Folder: " + LevelDesignerContext.LevelsRoot.Replace("/", "\u2215"), _ => { }, _ => DropdownMenuAction.Status.Disabled);
        }

        private void ChooseLevelsRoot()
        {
            string current = LevelDesignerContext.LevelsRoot;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
            string start = Directory.Exists(projectRoot + current) ? projectRoot + current : Application.dataPath;
            string absolute = EditorUtility.OpenFolderPanel("Levels Folder (where every blockout and variant scene goes)", start, "");
            if (string.IsNullOrEmpty(absolute))
                return;
            absolute = absolute.Replace('\\', '/');
            if (!absolute.StartsWith(projectRoot + "Assets"))
            {
                EditorUtility.DisplayDialog("Level Designer", "The levels folder must be inside this project's Assets folder.", "OK");
                return;
            }
            string folder = absolute.Substring(projectRoot.Length).TrimEnd('/');
            if (folder == current)
                return;
            LevelDesignerContext.SetLevelsRoot(folder);
            if (LevelDesignerContext.PlanLayoutMoves().Count > 0 &&
                EditorUtility.DisplayDialog("Level Designer", $"New scenes will go in {folder}. Move the existing scenes there too?", "Review Move…", "Not Now"))
                LevelDesignerContext.MoveScenesIntoLayout();
            Rebuild();
        }

        private (Tab tab, ToolbarToggle toggle)[] _tabToggles;

        private void SwitchTab(Tab tab)
        {
            _tab = tab;
            if (_tabToggles != null)
                foreach (var (t, toggle) in _tabToggles)
                    toggle.SetValueWithoutNotify(t == tab);
            Rebuild();
        }

        private void SyncHeader()
        {
            _xrayToggle?.SetValueWithoutNotify(LevelDesignerSettings.XRayEnabled);
            _opacitySlider?.SetValueWithoutNotify(LevelDesignerSettings.XRayOpacity);
            _opacitySlider?.SetEnabled(LevelDesignerSettings.XRayEnabled);
        }

        // ---------------------------------------------------------------- levels

        private BlockoutLevel SelectedBlockout =>
            _selectedBlockout >= 0 && _selectedBlockout < Project.Blockouts.Count ? Project.Blockouts[_selectedBlockout] : null;

        private void BuildLevels()
        {
            var split = new TwoPaneSplitView(0, 230, TwoPaneSplitViewOrientation.Horizontal);
            _body.Add(split);

            // Left: blockouts.
            var left = new VisualElement { style = { minWidth = 160 } };
            var leftHeader = Header("Blockouts");
            var levelName = new TextField { style = { flexGrow = 1, minWidth = 60 }, tooltip = "Name of the new level (its folder and scene names)" };
            levelName.textEdition.placeholder = "New level name";
            void CreateLevel()
            {
                var created = LevelDesignerContext.CreateBlockout(levelName.value);
                if (created != null)
                {
                    PutInFolder(created, _selectedFolder);
                    _selectedBlockout = Project.Blockouts.IndexOf(created);
                    Rebuild();
                }
            }
            leftHeader.Add(SmallButton("+", $"Make a new blockout scene in {LevelDesignerContext.LevelsRoot}/<Level>/", CreateLevel));
            leftHeader.Add(SmallButton("Add…", "Add an existing scene as a blockout", AddExistingBlockout));
            leftHeader.Add(SmallButton("−", "Remove the selected blockout from the list", RemoveSelectedBlockout));
            leftHeader.Add(SmallButton("New Folder", "A folder in the list to group blockouts in (drag blockouts onto it)", () => CreateListFolder(_selectedFolder)));
            left.Add(leftHeader);
            var nameRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4, marginBottom = 2 } };
            levelName.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CreateLevel();
            });
            nameRow.Add(levelName);
            left.Add(nameRow);

            _blockoutTree = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            // Dropping a blockout or folder on the list's empty space moves it to the top level.
            MakeListDropTarget(_blockoutTree, () => "");
            _blockoutTree.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("New Folder", _ => CreateListFolder(""));
                evt.menu.AppendAction("Add Existing Blockout…", _ => AddExistingBlockout());
            }));
            left.Add(_blockoutTree);
            if (Project.Blockouts.Count == 0)
                left.Add(Hint("No blockouts yet. Press + to make one: a new scene with a floor, ready for the blockout tools in the Scene view."));
            split.Add(left);

            // Right: variants of the selected blockout.
            _artPane = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            split.Add(_artPane);

            if (SelectedBlockout == null && Project.Blockouts.Count > 0)
                _selectedBlockout = Mathf.Max(0, Project.Blockouts.IndexOf(LevelDesignerContext.CurrentBlockoutEntry));
            RebuildBlockoutTree();
            BuildArtPane();
        }

        // A blockout row: its thumbnail, then its name (● when it's the one open) and variant count.
        private static VisualElement MakeBlockoutItem()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4 } };
            AddCardFrame(row);
            var image = new Image { name = "thumbnail", scaleMode = ScaleMode.ScaleAndCrop };
            image.style.width = 64;
            image.style.height = 36;
            image.style.flexShrink = 0;
            image.style.marginRight = 6;
            image.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            row.Add(image);
            row.Add(new Label { name = "name", style = { unityTextAlign = TextAnchor.MiddleLeft, flexGrow = 1, overflow = Overflow.Hidden } });
            var background = new Color(0f, 0f, 0f, 0f);
            row.RegisterCallback<PointerEnterEvent>(_ => row.style.backgroundColor = new Color(0.3f, 0.55f, 0.8f, 0.18f));
            row.RegisterCallback<PointerLeaveEvent>(_ => row.style.backgroundColor = background);
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                row.style.backgroundColor = new Color(0.25f, 0.55f, 0.9f, 0.35f);
                row.schedule.Execute(() =>
                {
                    if (row != null)
                        row.style.backgroundColor = background;
                }).ExecuteLater(220);
            });
            return row;
        }

        private void BindBlockoutItem(VisualElement element, int index)
        {
            var blockout = Project.Blockouts[index];
            var label = element.Q<Label>("name");
            bool current = LevelDesignerContext.CurrentBlockoutEntry == blockout;
            label.text = (current ? "● " : "") + (string.IsNullOrEmpty(blockout.Name) ? "(unnamed)" : blockout.Name) +
                         (blockout.Variants.Count > 0 ? $"  ({blockout.Variants.Count})" : "");
            label.style.unityFontStyleAndWeight = current ? FontStyle.Bold : FontStyle.Normal;
            element.tooltip = LevelDesignerContext.PathOf(blockout);
            element.Q<Image>("thumbnail").image = LevelThumbnails.Get(SceneGuidOf(blockout.SceneGuid, LevelDesignerContext.PathOf(blockout)));
        }

        // The GUID a thumbnail is stored under (the remembered GUID, or the scene's current one).
        private static string SceneGuidOf(string guid, string path)
        {
            if (!string.IsNullOrEmpty(guid))
                return guid;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.AssetPathToGUID(path);
        }

        // A 16:9 thumbnail for a variant card (a dark placeholder until the scene is first saved while open).
        private static VisualElement CardThumbnail(string sceneGuid, string scenePath)
        {
            var image = new Image { scaleMode = ScaleMode.ScaleAndCrop, image = LevelThumbnails.Get(SceneGuidOf(sceneGuid, scenePath)) };
            image.style.height = 111;
            image.style.marginBottom = 4;
            image.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            image.style.borderTopLeftRadius = image.style.borderTopRightRadius = image.style.borderBottomLeftRadius = image.style.borderBottomRightRadius = 4;
            if (image.image == null)
            {
                image.tooltip = "No thumbnail yet: it's captured from the Scene view when the scene is saved while open";
                image.Add(new Label("No thumbnail yet") { style = { color = new Color(0.6f, 0.6f, 0.6f), fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter, flexGrow = 1 } });
            }
            return image;
        }

        // ---------------------------------------------------------------- blockout folders

        private const string BlockoutDragKey = "Remedy.LevelDesigner.Blockout";
        private const string FolderDragKey = "Remedy.LevelDesigner.ListFolder";
        private const string OpenFoldersPref = "Remedy.LevelDesigner.OpenFolder.";

        private static string ParentFolder(string folder)
        {
            int slash = folder?.LastIndexOf('/') ?? -1;
            return slash < 0 ? "" : folder.Substring(0, slash);
        }

        private static string FolderName(string folder)
        {
            int slash = folder.LastIndexOf('/');
            return slash < 0 ? folder : folder.Substring(slash + 1);
        }

        // Every list folder: the ones kept in the project, those blockouts are in, and all their parents.
        private SortedSet<string> AllListFolders()
        {
            var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddWithParents(string folder)
            {
                for (; !string.IsNullOrEmpty(folder); folder = ParentFolder(folder))
                    folders.Add(folder);
            }
            foreach (var folder in Project.Folders)
                AddWithParents(folder);
            foreach (var blockout in Project.Blockouts)
                AddWithParents(blockout.Folder);
            return folders;
        }

        private readonly Dictionary<string, Label> _folderLabels = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsOpen(string folder) => EditorPrefs.GetBool(OpenFoldersPref + folder, true);

        // The list: folders (with their folders and blockouts inside), then the blockouts at the top level.
        private void RebuildBlockoutTree()
        {
            if (_blockoutTree == null || Project == null)
                return;
            _blockoutTree.Clear();
            _folderLabels.Clear();
            var folders = AllListFolders();
            AddListChildren(folders, "", 0);
        }

        private void AddListChildren(SortedSet<string> folders, string parent, int depth)
        {
            foreach (var folder in folders)
            {
                if (!string.Equals(ParentFolder(folder), parent, StringComparison.OrdinalIgnoreCase))
                    continue;
                _blockoutTree.Add(FolderRow(folder, depth));
                if (IsOpen(folder))
                    AddListChildren(folders, folder, depth + 1);
            }
            for (int i = 0; i < Project.Blockouts.Count; i++)
                if (string.Equals(Project.Blockouts[i].Folder ?? "", parent, StringComparison.OrdinalIgnoreCase))
                    _blockoutTree.Add(BlockoutRow(i, depth));
        }

        private VisualElement FolderRow(string folder, int depth)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 22, paddingLeft = 4 + depth * 14 } };
            bool selected = _selectedBlockout < 0 && string.Equals(_selectedFolder, folder, StringComparison.OrdinalIgnoreCase);
            if (selected)
                row.style.backgroundColor = new Color(0.25f, 0.5f, 0.85f, 0.35f);

            var arrow = new Label(IsOpen(folder) ? "▾" : "▸") { style = { width = 14, unityTextAlign = TextAnchor.MiddleCenter } };
            arrow.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                EditorPrefs.SetBool(OpenFoldersPref + folder, !IsOpen(folder));
                RebuildBlockoutTree();
                evt.StopPropagation();
            });
            row.Add(arrow);
            row.Add(new Image { image = EditorGUIUtility.IconContent("Folder Icon")?.image, style = { width = 16, height = 16, marginRight = 4 } });
            var label = new Label(FolderName(folder)) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } };
            row.Add(label);
            _folderLabels[folder] = label;

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                if (evt.clickCount == 2 && evt.target == label)
                {
                    EditorApplication.delayCall += () => RenameListFolder(label, folder);
                    return;
                }
                if (evt.clickCount == 2)
                {
                    EditorPrefs.SetBool(OpenFoldersPref + folder, !IsOpen(folder));
                    RebuildBlockoutTree();
                    return;
                }
                _selectedFolder = folder;
                _selectedBlockout = -1;
                RebuildBlockoutTree();
                BuildArtPane();
            });
            Vector2 press = default;
            bool pressed = false;
            row.RegisterCallback<PointerDownEvent>(evt => { pressed = evt.button == 0; press = evt.position; }, TrickleDown.TrickleDown);
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!pressed || (evt.pressedButtons & 1) == 0 || ((Vector2)evt.position - press).sqrMagnitude < 36f) return;
                pressed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.paths = Array.Empty<string>();
                DragAndDrop.SetGenericData(FolderDragKey, folder);
                DragAndDrop.StartDrag(FolderName(folder));
            });
            row.RegisterCallback<PointerUpEvent>(_ => pressed = false);
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("New Folder Inside", _ => CreateListFolder(folder));
                evt.menu.AppendAction("Rename", _ => RenameListFolder(label, folder));
                evt.menu.AppendAction("Delete Folder (keeps its blockouts)", _ => DeleteListFolder(folder));
                evt.StopPropagation();
            }));
            MakeListDropTarget(row, () => folder);
            return row;
        }

        private VisualElement BlockoutRow(int index, int depth)
        {
            var row = MakeBlockoutItem();
            row.style.height = 44;
            row.style.marginLeft = depth * 14;
            BindBlockoutItem(row, index);
            var blockout = Project.Blockouts[index];
            if (index == _selectedBlockout)
                row.Q<Label>("name").style.color = new Color(0.55f, 0.8f, 1f);

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _selectedBlockout = index;
                _selectedFolder = blockout.Folder ?? "";
                if (evt.clickCount == 2)
                {
                    LevelDesignerContext.OpenBlockout(blockout);
                    return;
                }
                RebuildBlockoutTree();
                BuildArtPane();
            });
            Vector2 press = default;
            bool pressed = false;
            row.RegisterCallback<PointerDownEvent>(evt => { pressed = evt.button == 0; press = evt.position; }, TrickleDown.TrickleDown);
            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!pressed || (evt.pressedButtons & 1) == 0 || ((Vector2)evt.position - press).sqrMagnitude < 36f) return;
                pressed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.paths = Array.Empty<string>();
                DragAndDrop.SetGenericData(BlockoutDragKey, blockout);
                DragAndDrop.StartDrag(blockout.Name);
            });
            row.RegisterCallback<PointerUpEvent>(_ => pressed = false);
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Open", _ => LevelDesignerContext.OpenBlockout(blockout));
                evt.menu.AppendAction("Move to Top Level", _ => PutInFolder(blockout, ""),
                    string.IsNullOrEmpty(blockout.Folder) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                evt.menu.AppendAction("Remove from List…", _ =>
                {
                    _selectedBlockout = index;
                    RemoveSelectedBlockout();
                });
                evt.StopPropagation();
            }));
            // Dropping onto a blockout puts the dragged one in the same folder.
            MakeListDropTarget(row, () => blockout.Folder ?? "");
            return row;
        }

        // Takes a dragged blockout (into the folder) or list folder (moved inside it).
        private void MakeListDropTarget(VisualElement element, Func<string> folder)
        {
            element.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool blockout = DragAndDrop.GetGenericData(BlockoutDragKey) is BlockoutLevel;
                bool listFolder = DragAndDrop.GetGenericData(FolderDragKey) is string dragged && CanMoveFolder(dragged, folder());
                if (!blockout && !listFolder) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                evt.StopPropagation();
            });
            element.RegisterCallback<DragPerformEvent>(evt =>
            {
                string target = folder();
                if (DragAndDrop.GetGenericData(BlockoutDragKey) is BlockoutLevel blockout)
                {
                    DragAndDrop.AcceptDrag();
                    evt.StopPropagation();
                    EditorApplication.delayCall += () => PutInFolder(blockout, target);
                }
                else if (DragAndDrop.GetGenericData(FolderDragKey) is string dragged && CanMoveFolder(dragged, target))
                {
                    DragAndDrop.AcceptDrag();
                    evt.StopPropagation();
                    EditorApplication.delayCall += () => RenameFolderPath(dragged, string.IsNullOrEmpty(target) ? FolderName(dragged) : target + "/" + FolderName(dragged));
                }
            });
        }

        private static bool CanMoveFolder(string folder, string into)
            => !string.Equals(folder, into, StringComparison.OrdinalIgnoreCase)
               && !into.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(ParentFolder(folder), into, StringComparison.OrdinalIgnoreCase);

        private void PutInFolder(BlockoutLevel blockout, string folder)
        {
            if (blockout == null || string.Equals(blockout.Folder ?? "", folder ?? "", StringComparison.Ordinal))
                return;
            LevelDesignerContext.RecordProject("Move Blockout to Folder");
            blockout.Folder = folder ?? "";
            if (!string.IsNullOrEmpty(folder))
                EditorPrefs.SetBool(OpenFoldersPref + folder, true);
            RebuildBlockoutTree();
        }

        private void CreateListFolder(string parent)
        {
            var existing = AllListFolders();
            string prefix = string.IsNullOrEmpty(parent) ? "" : parent + "/";
            string name = "New Folder";
            for (int n = 2; existing.Contains(prefix + name); n++)
                name = "New Folder " + n;
            LevelDesignerContext.RecordProject("New Level Folder");
            Project.Folders.Add(prefix + name);
            if (!string.IsNullOrEmpty(parent))
                EditorPrefs.SetBool(OpenFoldersPref + parent, true);
            _selectedFolder = prefix + name;
            _selectedBlockout = -1;
            RebuildBlockoutTree();
            BuildArtPane();
            // Start renaming the new folder.
            _blockoutTree.schedule.Execute(() =>
            {
                if (_folderLabels.TryGetValue(prefix + name, out var label))
                    RenameListFolder(label, prefix + name);
            });
        }

        // Swaps the folder's name for a text field: Enter (or clicking away) renames it, Escape cancels.
        private void RenameListFolder(Label label, string folder)
        {
            var host = label?.parent;
            if (host == null) return;
            var field = new TextField { value = FolderName(folder), style = { flexGrow = 1, marginLeft = 0, marginRight = 0 } };
            label.style.display = DisplayStyle.None;
            host.Insert(host.IndexOf(label) + 1, field);
            bool done = false;
            void Finish(bool apply)
            {
                if (done) return;
                done = true;
                string value = field.value?.Replace("/", " ").Trim();
                field.RemoveFromHierarchy();
                label.style.display = DisplayStyle.Flex;
                if (apply && !string.IsNullOrEmpty(value) && value != FolderName(folder))
                {
                    string parent = ParentFolder(folder);
                    RenameFolderPath(folder, string.IsNullOrEmpty(parent) ? value : parent + "/" + value);
                }
            }
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { Finish(true); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Escape) { Finish(false); evt.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            field.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectAll();
            }).ExecuteLater(1);
        }

        // Renames (or moves) a list folder, with everything inside it.
        private void RenameFolderPath(string from, string to)
        {
            if (string.IsNullOrEmpty(to) || string.Equals(from, to, StringComparison.Ordinal))
                return;
            string Map(string folder) => folder == null ? null
                : string.Equals(folder, from, StringComparison.OrdinalIgnoreCase) ? to
                : folder.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase) ? to + folder.Substring(from.Length)
                : folder;
            LevelDesignerContext.RecordProject("Rename Level Folder");
            for (int i = 0; i < Project.Folders.Count; i++)
                Project.Folders[i] = Map(Project.Folders[i]);
            if (!Project.Folders.Contains(to))
                Project.Folders.Add(to);
            foreach (var blockout in Project.Blockouts)
                blockout.Folder = Map(blockout.Folder ?? "") ?? "";
            _selectedFolder = Map(_selectedFolder ?? "") ?? "";
            EditorPrefs.SetBool(OpenFoldersPref + to, IsOpen(from));
            RebuildBlockoutTree();
        }

        // Removes a list folder; its blockouts and folders move up into its parent.
        private void DeleteListFolder(string folder)
        {
            string parent = ParentFolder(folder);
            string Map(string f) => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase) ? parent
                : f.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)
                    ? (string.IsNullOrEmpty(parent) ? "" : parent + "/") + f.Substring(folder.Length + 1)
                    : f;
            LevelDesignerContext.RecordProject("Delete Level Folder");
            Project.Folders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < Project.Folders.Count; i++)
                Project.Folders[i] = Map(Project.Folders[i]);
            Project.Folders.RemoveAll(string.IsNullOrEmpty);
            foreach (var blockout in Project.Blockouts)
                blockout.Folder = Map(blockout.Folder ?? "");
            if (string.Equals(_selectedFolder, folder, StringComparison.OrdinalIgnoreCase))
                _selectedFolder = parent;
            RebuildBlockoutTree();
        }

        private void AddExistingBlockout()
        {
            string absolute = EditorUtility.OpenFilePanel("Add Blockout Scene", Application.dataPath, "unity");
            if (string.IsNullOrEmpty(absolute))
                return;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/') + "/";
            absolute = absolute.Replace('\\', '/');
            if (!absolute.StartsWith(projectRoot))
            {
                EditorUtility.DisplayDialog("Level Designer", "The scene must be inside this project.", "OK");
                return;
            }
            var entry = LevelDesignerContext.AddExistingBlockout(absolute.Substring(projectRoot.Length));
            _selectedBlockout = Project.Blockouts.IndexOf(entry);
            Rebuild();
        }

        private void RemoveSelectedBlockout()
        {
            var blockout = SelectedBlockout;
            if (blockout == null)
                return;
            int choice = EditorUtility.DisplayDialogComplex("Remove Blockout",
                $"Remove \"{blockout.Name}\" and its {blockout.Variants.Count} variant(s) from the Level Designer?",
                "Remove from List", "Cancel", "Remove and Delete Scenes");
            if (choice == 1)
                return;
            LevelDesignerContext.RemoveBlockout(blockout, choice == 2);
            _selectedBlockout = Mathf.Clamp(_selectedBlockout, 0, Project.Blockouts.Count - 1);
            Rebuild();
        }

        private void BuildArtPane()
        {
            if (_artPane == null)
                return;
            _artPane.Clear();
            var blockout = SelectedBlockout;
            if (blockout == null)
            {
                _artPane.Add(Hint("Select a blockout to see its variants."));
                return;
            }

            // The blockout itself.
            var header = Header("Blockout");
            var nameField = new TextField { value = blockout.Name, isDelayed = true, style = { flexGrow = 1 } };
            nameField.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Rename Blockout");
                blockout.Name = evt.newValue;
                RebuildBlockoutTree();
            });
            header.Add(nameField);
            header.Add(SmallButton("Open", "Open the blockout scene (blockout tools appear in the Scene view)", () => LevelDesignerContext.OpenBlockout(blockout)));
            header.Add(SmallButton("Ping", "Show the scene in the Project window", () => Ping(LevelDesignerContext.PathOf(blockout))));
            _artPane.Add(header);

            var path = new Label(LevelDesignerContext.PathOf(blockout)) { style = { fontSize = 10, color = new Color(0.6f, 0.6f, 0.6f), marginLeft = 6 } };
            _artPane.Add(path);
            AddLevelTags(_artPane, LevelDesignerContext.PathOf(blockout));
            var blockoutThumb = CardThumbnail(blockout.SceneGuid, LevelDesignerContext.PathOf(blockout));
            blockoutThumb.style.width = 197;
            blockoutThumb.style.marginLeft = 6;
            blockoutThumb.style.marginTop = 4;
            _artPane.Add(blockoutThumb);
            _artPane.Add(NotesField(blockout.Notes, value => blockout.Notes = value, "Notes about this blockout"));

            // Variants.
            var variantHeader = Header("Variants");
            var newName = new TextField { value = "", style = { flexGrow = 1 }, tooltip = "Name for the new variant" };
            newName.textEdition.placeholder = "New variant name";
            variantHeader.Add(newName);
            variantHeader.Add(SmallButton("+ Add Variant", "Make a new variant scene over this blockout and open it", () =>
            {
                var variant = LevelDesignerContext.CreateVariant(blockout, newName.value);
                if (variant != null)
                    BuildArtPane();
            }));
            Button testButton = null;
            testButton = SmallButton("▶ Test Composed Scene…", "Merge the blockout with the picked variants (the ones flagged for the build by default) and play it", () =>
            {
                var bounds = testButton.worldBound;
                UnityEditor.PopupWindow.Show(bounds, new ComposeTestPopup(blockout));
            });
            variantHeader.Add(testButton);
            _artPane.Add(variantHeader);

            var composeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 6, marginBottom = 4 } };
            var blockoutMode = new EnumField("Blockout when composed", blockout.ComposedBlockout)
            {
                tooltip = "In the composed scene (builds and Test Composed Scene): keep the blockout visible, keep only its collision, or leave it out",
            };
            blockoutMode.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Composed Blockout");
                blockout.ComposedBlockout = (ComposedBlockoutMode)evt.newValue;
                AssetDatabase.SaveAssets();
            });
            composeRow.Add(blockoutMode);
            _artPane.Add(composeRow);
            if (!Project.ComposeVariantsOnBuild)
                _artPane.Add(Hint("Composing variants into builds is switched off (Levels menu)."));

            var oldArt = LevelDesignerContext.ScenesInOldArtFolders();
            if (oldArt.Count > 0 && !EditorPrefs.GetBool(DismissArtMigrationKey, false))
                _artPane.Add(ArtMigrationBanner(oldArt.Count));

            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 4, paddingRight = 4 } };
            foreach (var variant in blockout.Variants)
                grid.Add(VariantCard(blockout, variant));
            grid.Add(PlusCard(blockout, newName));
            _artPane.Add(grid);
        }

        private VisualElement VariantCard(BlockoutLevel blockout, Variant variant)
        {
            var card = Card();
            string variantPath = LevelDesignerContext.PathOf(variant);
            bool isOpen = SceneManager.GetActiveScene().path == variantPath;
            if (isOpen)
                card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor = new Color(0.35f, 0.7f, 1f);

            card.Add(CardThumbnail(variant.SceneGuid, variantPath));
            var name = new TextField { value = variant.Name, isDelayed = true };
            name.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Rename Variant");
                variant.Name = evt.newValue;
            });
            card.Add(name);

            bool exists = !string.IsNullOrEmpty(variantPath) && File.Exists(variantPath);
            var pathLabel = new Label(exists ? Path.GetFileName(variantPath) : "Scene missing!") { tooltip = variantPath };
            pathLabel.style.fontSize = 10;
            pathLabel.style.color = exists ? new Color(0.6f, 0.6f, 0.6f) : new Color(1f, 0.4f, 0.3f);
            card.Add(pathLabel);
            AddLevelTags(card, variantPath);

            var compose = new Toggle("Compose into build")
            {
                value = variant.ComposeIntoBuild,
                tooltip = "When the game is built, merge this variant with the blockout (and the level's other flagged variants) into one scene that replaces the blockout in the build",
            };
            compose.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Compose Variant into Build");
                variant.ComposeIntoBuild = evt.newValue;
                AssetDatabase.SaveAssets();
            });
            card.Add(compose);
            card.Add(NotesField(variant.Notes, value => variant.Notes = value, "Notes"));

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            buttons.Add(SmallButton(isOpen ? "Opened" : "Open", "Open this variant with the blockout underneath", () => LevelDesignerContext.OpenVariant(blockout, variant)));
            buttons.Add(SmallButton("Duplicate", "Copy this variant's scene into a new variant", () =>
            {
                LevelDesignerContext.DuplicateVariant(blockout, variant);
                BuildArtPane();
            }));
            buttons.Add(SmallButton("✕", "Remove this variant", () =>
            {
                int choice = EditorUtility.DisplayDialogComplex("Remove Variant", $"Remove \"{variant.Name}\"?", "Remove from List", "Cancel", "Remove and Delete Scene");
                if (choice == 1)
                    return;
                LevelDesignerContext.RemoveVariant(blockout, variant, choice == 2);
                BuildArtPane();
            }));
            card.Add(buttons);
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                    LevelDesignerContext.OpenVariant(blockout, variant);
            });
            return card;
        }

        private void AddLevelTags(VisualElement host, string scenePath)
        {
            var scene = string.IsNullOrEmpty(scenePath) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (scene == null)
                return;
            var tagType = Type.GetType("Remedy.Schematics.Browsers.ProjectManagerTags, Remedy.Schematics.Browsers.Editor", false);
            var buildEditor = tagType?.GetMethod("BuildEditor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (buildEditor == null)
                return;
            var editor = buildEditor.Invoke(null, new object[] { "Levels", scene, (Action)(() =>
            {
                RebuildBlockoutTree();
                if (_tab == Tab.Levels)
                    BuildArtPane();
            }) }) as VisualElement;
            if (editor != null)
                host.Add(editor);
        }

        private const string DismissArtMigrationKey = "Remedy.LevelDesigner.DismissArtFolderMigration";

        // Variants used to be "art designs" in an "Art" folder. Offered, never automatic: moving scenes touches
        // version control, so the user reviews the moves first (the same dialog as "Move Existing Scenes").
        private VisualElement ArtMigrationBanner(int count)
        {
            var banner = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginLeft = 6, marginRight = 6, marginBottom = 6, paddingLeft = 6, paddingRight = 4, paddingTop = 4, paddingBottom = 4 } };
            banner.style.backgroundColor = new Color(0.35f, 0.6f, 1f, 0.15f);
            banner.style.borderTopLeftRadius = banner.style.borderTopRightRadius = banner.style.borderBottomLeftRadius = banner.style.borderBottomRightRadius = 4;
            banner.Add(new Label($"Art designs are now called variants and live in a \"{LevelDesignerContext.VariantsFolderName}\" folder. {count} scene(s) are still in old \"{LevelDesignerContext.OldArtFolderName}\" folders.")
            {
                style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1, flexShrink = 1 },
            });
            banner.Add(SmallButton("Review Move…", "Show what would move (GUIDs and references are kept) and move on confirmation", () =>
            {
                if (LevelDesignerContext.MoveScenesIntoLayout() > 0)
                    Rebuild();
            }));
            banner.Add(SmallButton("Dismiss", "Keep them where they are (Levels → Move Existing Scenes does it later)", () =>
            {
                EditorPrefs.SetBool(DismissArtMigrationKey, true);
                BuildArtPane();
            }));
            return banner;
        }

        private VisualElement PlusCard(BlockoutLevel blockout, TextField nameField)
        {
            var card = Card();
            card.style.justifyContent = Justify.Center;
            card.style.alignItems = Align.Center;
            var plus = new Button(() =>
            {
                if (LevelDesignerContext.CreateVariant(blockout, nameField.value) != null)
                    BuildArtPane();
            })
            {
                text = "+",
                tooltip = "New variant over this blockout",
            };
            plus.style.fontSize = 32;
            plus.style.width = 64;
            plus.style.height = 64;
            card.Add(plus);
            card.Add(new Label("New Variant") { style = { marginTop = 4 } });
            return card;
        }

        private static VisualElement Card()
        {
            var card = new VisualElement();
            card.AddToClassList("level-designer__card");
            card.style.width = 210;
            card.style.minHeight = 130;
            card.style.marginRight = card.style.marginBottom = 6;
            card.style.paddingLeft = card.style.paddingRight = card.style.paddingTop = card.style.paddingBottom = 6;
            card.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.20f, 0.23f, 0.92f)
                : new Color(0.82f, 0.85f, 0.89f, 0.96f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            card.style.borderLeftWidth = card.style.borderRightWidth = card.style.borderTopWidth = card.style.borderBottomWidth = 1;
            card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor =
                EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.22f) : new Color(0f, 0f, 0f, 0.22f);
            AddCardFrame(card);
            var background = card.style.backgroundColor.value;
            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor =
                    EditorGUIUtility.isProSkin ? new Color(0.55f, 0.75f, 1f, 0.75f) : new Color(0.12f, 0.38f, 0.7f, 0.75f);
                card.style.scale = new Scale(new Vector2(1.015f, 1.015f));
            });
            card.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                card.style.backgroundColor = background;
                card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);
                card.style.scale = new Scale(Vector2.one);
            });
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                card.style.backgroundColor = new Color(0.25f, 0.55f, 0.9f, 0.3f);
                card.schedule.Execute(() =>
                {
                    if (card != null)
                        card.style.backgroundColor = background;
                }).ExecuteLater(220);
            });
            return card;
        }

        private static void AddCardFrame(VisualElement card)
        {
            card.AddToClassList("level-designer__card-frame");
            card.style.position = Position.Relative;
            card.style.overflow = Overflow.Hidden;
            card.style.borderTopWidth = card.style.borderRightWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = 1;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 5;
            var border = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.22f) : new Color(0f, 0f, 0f, 0.22f);
            card.style.borderTopColor = card.style.borderRightColor = card.style.borderBottomColor = card.style.borderLeftColor = border;
            var gloss = new Image
            {
                image = Resources.Load<Texture2D>("Textures/button-gradient"),
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore
            };
            gloss.style.position = Position.Absolute;
            gloss.style.left = gloss.style.top = gloss.style.right = gloss.style.bottom = 0;
            gloss.style.opacity = 0.24f;
            gloss.AddToClassList("level-designer__card-gloss");
            card.Add(gloss);
        }

        private static TextField NotesField(string value, System.Action<string> set, string placeholder)
        {
            var notes = new TextField { value = value ?? "", multiline = true, isDelayed = true };
            notes.textEdition.placeholder = placeholder;
            notes.style.minHeight = 36;
            notes.style.whiteSpace = WhiteSpace.Normal;
            notes.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Edit Notes");
                set(evt.newValue);
            });
            return notes;
        }

        private static void Ping(string path)
        {
            var asset = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (asset != null)
                EditorGUIUtility.PingObject(asset);
        }

        // ---------------------------------------------------------------- palette

        // The project's palette: rename, recolour, reorder, remove, add. Changes are undoable and saved in the
        // LevelDesignProject asset, so the whole team shares the colour language.
        private void BuildPalette()
        {
            var header = Header("Palette");
            header.Add(SmallButton("+ Add Colour", "Add a colour to the palette", () =>
            {
                LevelDesignerContext.RecordProject("Add Palette Colour");
                var color = LevelDesignerSettings.HasCurrentColor ? LevelDesignerSettings.CurrentColor : Color.white;
                Project.Palette.Add(new PaletteColor("Colour " + (Project.Palette.Count + 1), color));
                AssetDatabase.SaveAssets();
                Rebuild();
            }));
            header.Add(SmallButton("Reset to Defaults", "Replace the palette with the category colours", () =>
            {
                if (!EditorUtility.DisplayDialog("Level Designer", "Replace the whole palette with the default category colours?", "Reset", "Cancel"))
                    return;
                LevelDesignerContext.RecordProject("Reset Palette");
                Project.Palette = LevelDesignProject.DefaultPalette();
                AssetDatabase.SaveAssets();
                Rebuild();
            }));
            _body.Add(header);
            _body.Add(Hint("Named colours for the blockout toolbar's swatch and paint bucket. Give each a meaning (\"Climbable\", \"Enemy zone\") so blockouts read the same for everyone. Stored in the LevelDesignProject asset, so commit it to share."));

            var scroll = new ScrollView { style = { flexGrow = 1, marginTop = 6 } };
            _body.Add(scroll);
            var palette = Project.Palette;
            for (int i = 0; i < palette.Count; i++)
            {
                var entry = palette[i];
                if (entry == null)
                    continue;
                int index = i;
                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 6, paddingRight = 6, marginBottom = 2 } };

                var color = new ColorField { value = entry.Color, showAlpha = false, style = { width = 70 } };
                color.RegisterValueChangedCallback(evt =>
                {
                    LevelDesignerContext.RecordProject("Change Palette Colour");
                    entry.Color = evt.newValue;
                });
                row.Add(color);

                var name = new TextField { value = entry.Name, isDelayed = true, style = { flexGrow = 1 } };
                name.RegisterValueChangedCallback(evt =>
                {
                    LevelDesignerContext.RecordProject("Rename Palette Colour");
                    entry.Name = evt.newValue;
                    AssetDatabase.SaveAssets();
                });
                row.Add(name);

                row.Add(SmallButton("Use", "Make this the blockout toolbar's colour", () => LevelDesignerSettings.CurrentColor = entry.Color));
                row.Add(SmallButton("Paint Selected", "Paint the selected blockout shapes and rulers with this colour", () =>
                {
                    LevelDesignerSettings.CurrentColor = entry.Color;
                    BlockoutPainter.PaintSelectionWithCurrent();
                }));
                var up = SmallButton("▲", "Move up", () => MovePaletteEntry(index, -1));
                up.SetEnabled(index > 0);
                row.Add(up);
                var down = SmallButton("▼", "Move down", () => MovePaletteEntry(index, 1));
                down.SetEnabled(index < palette.Count - 1);
                row.Add(down);
                row.Add(SmallButton("✕", "Remove from the palette (painted objects keep their colour)", () =>
                {
                    LevelDesignerContext.RecordProject("Remove Palette Colour");
                    Project.Palette.RemoveAt(index);
                    AssetDatabase.SaveAssets();
                    Rebuild();
                }));
                scroll.Add(row);
            }
            // Colour fields change on every drag step; save once the pointer is let go.
            scroll.RegisterCallback<PointerUpEvent>(_ => AssetDatabase.SaveAssets());
        }

        private void MovePaletteEntry(int index, int direction)
        {
            var palette = Project.Palette;
            int target = index + direction;
            if (target < 0 || target >= palette.Count)
                return;
            LevelDesignerContext.RecordProject("Reorder Palette");
            (palette[index], palette[target]) = (palette[target], palette[index]);
            AssetDatabase.SaveAssets();
            Rebuild();
        }

        // ---------------------------------------------------------------- small helpers

        private static VisualElement Header(string title)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingLeft = 6;
            header.style.paddingRight = 4;
            header.style.paddingTop = 4;
            header.style.paddingBottom = 4;
            header.style.marginBottom = 2;
            header.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);
            header.Add(new Label(title) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 8 } });
            return header;
        }

        private static Button SmallButton(string text, string tooltip, System.Action action)
        {
            return new Button(action) { text = text, tooltip = tooltip };
        }

        private static Label Hint(string text)
        {
            return new Label(text)
            {
                style = { whiteSpace = WhiteSpace.Normal, color = new Color(0.65f, 0.65f, 0.65f), marginLeft = 8, marginRight = 8, marginTop = 8 },
            };
        }
    }

    /// <summary>
    /// The "Test Composed Scene" picker: which variants to merge with the blockout (the ones flagged "Compose into
    /// build" are ticked to start with; all of them when none is flagged), how the blockout takes part, then Play.
    /// </summary>
    public class ComposeTestPopup : PopupWindowContent
    {
        private readonly BlockoutLevel _blockout;
        private readonly bool[] _picked;
        private ComposedBlockoutMode _mode;
        private Vector2 _scroll;

        public ComposeTestPopup(BlockoutLevel blockout)
        {
            _blockout = blockout;
            _mode = blockout.ComposedBlockout;
            _picked = new bool[blockout.Variants.Count];
            bool anyFlagged = blockout.HasComposedVariants;
            for (int i = 0; i < _picked.Length; i++)
                _picked[i] = blockout.Variants[i] != null && (!anyFlagged || blockout.Variants[i].ComposeIntoBuild);
        }

        public override Vector2 GetWindowSize() => new Vector2(300f, Mathf.Min(420f, 110f + 20f * _picked.Length));

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField("Test Composed Scene", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Merge " + _blockout.Name + " with:", EditorStyles.miniLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _picked.Length; i++)
            {
                var variant = _blockout.Variants[i];
                if (variant == null)
                    continue;
                _picked[i] = EditorGUILayout.ToggleLeft(new GUIContent(variant.Name + (variant.ComposeIntoBuild ? "  (in build)" : "")), _picked[i]);
            }
            if (_picked.Length == 0)
                EditorGUILayout.HelpBox("This blockout has no variants yet: only the blockout will play.", MessageType.None);
            EditorGUILayout.EndScrollView();
            _mode = (ComposedBlockoutMode)EditorGUILayout.EnumPopup(new GUIContent("Blockout", "Visible, collision only, or left out"), _mode);

            if (GUILayout.Button("Compose and Play"))
            {
                var variants = new List<Variant>();
                for (int i = 0; i < _picked.Length; i++)
                    if (_picked[i] && _blockout.Variants[i] != null)
                        variants.Add(_blockout.Variants[i]);
                var blockout = _blockout;
                var mode = _mode;
                editorWindow.Close();
                // Scenes are opened and Play Mode entered after this GUI pass is over.
                EditorApplication.delayCall += () => LevelComposer.TestComposed(blockout, variants, mode);
            }
        }
    }
}
