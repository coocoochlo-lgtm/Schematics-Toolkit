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
    /// and on the right the art designs built on the selected blockout, as cards (open, duplicate, rename, remove),
    /// with + to start a new art design over the blockout. <b>Characters</b>: the movement numbers of character
    /// prefabs (jump height, air time, run speed…), exported to JSON or CSV, and which character the blockout's
    /// jump and run rulers use. The header holds the blockout X-ray toggle and opacity, the snapping step, and the
    /// Levels menu (the folder every scene goes in, moving existing scenes there, capturing a thumbnail).<br/>
    /// A normal dockable editor window built with UI Toolkit: drag its tab next to the Inspector, the Scene view or
    /// anywhere else, and it stays there across sessions like any built-in window.
    /// </summary>
    public class LevelDesignerWindow : EditorWindow
    {
        [MenuItem("Window/Remedy/Level Designer")]
        [MenuItem("Tools/Remedy/Level Designer")]
        public static void Open()
        {
            // GetWindow makes (or focuses) a regular dockable window with a tab; it isn't a utility or popup window.
            var window = GetWindow<LevelDesignerWindow>(false, "Level Designer", true);
            window.Show();
        }

        private static GUIContent Title()
        {
            var icon = EditorGUIUtility.IconContent("SceneAsset Icon")?.image;
            return new GUIContent("Level Designer", icon, "Blockouts, art designs and character metrics");
        }

        private enum Tab { Levels, Characters }

        [SerializeField] private Tab _tab;
        [SerializeField] private int _selectedBlockout;

        private VisualElement _body;
        private ListView _blockoutList;
        private VisualElement _artPane;
        private Toggle _xrayToggle;
        private Slider _opacitySlider;
        private readonly List<GameObject> _characters = new();
        private bool _charactersScanned;

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
            _blockoutList?.RefreshItems();
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
            _blockoutList?.RefreshItems();
            if (_tab == Tab.Levels)
                BuildArtPane();
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
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
                BuildCharacters();
            SyncHeader();
        }

        // ---------------------------------------------------------------- header

        private VisualElement BuildHeader()
        {
            var toolbar = new Toolbar();

            var levels = new ToolbarToggle { text = "Levels", value = _tab == Tab.Levels };
            var characters = new ToolbarToggle { text = "Characters", value = _tab == Tab.Characters };
            levels.RegisterValueChangedCallback(evt => SwitchTab(Tab.Levels, levels, characters));
            characters.RegisterValueChangedCallback(evt => SwitchTab(Tab.Characters, levels, characters));
            toolbar.Add(levels);
            toolbar.Add(characters);
            toolbar.Add(new ToolbarSpacer { flex = true });

            _xrayToggle = new ToolbarToggle { text = "Blockout Overlay", tooltip = "While editing an art design, show its blockout as an X-ray through the art" };
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
            levelsMenu.menu.AppendSeparator();
            levelsMenu.menu.AppendAction("Capture Thumbnail of the Active Scene", _ =>
            {
                if (!LevelThumbnails.CaptureActive())
                    EditorUtility.DisplayDialog("Level Designer", "Open a blockout or an art design (and a Scene view) first.", "OK");
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
            string absolute = EditorUtility.OpenFolderPanel("Levels Folder (where every blockout and art design scene goes)", start, "");
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

        private void SwitchTab(Tab tab, ToolbarToggle levels, ToolbarToggle characters)
        {
            _tab = tab;
            levels.SetValueWithoutNotify(tab == Tab.Levels);
            characters.SetValueWithoutNotify(tab == Tab.Characters);
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
                    _selectedBlockout = Project.Blockouts.IndexOf(created);
                    Rebuild();
                }
            }
            leftHeader.Add(SmallButton("+", $"Make a new blockout scene in {LevelDesignerContext.LevelsRoot}/<Level>/", CreateLevel));
            leftHeader.Add(SmallButton("Add…", "Add an existing scene as a blockout", AddExistingBlockout));
            leftHeader.Add(SmallButton("−", "Remove the selected blockout from the list", RemoveSelectedBlockout));
            left.Add(leftHeader);
            var nameRow = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 4, paddingRight = 4, marginBottom = 2 } };
            levelName.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    CreateLevel();
            });
            nameRow.Add(levelName);
            left.Add(nameRow);

            _blockoutList = new ListView(Project.Blockouts, 44, MakeBlockoutItem, BindBlockoutItem)
            {
                selectionType = SelectionType.Single,
                style = { flexGrow = 1 },
            };
            _blockoutList.selectionChanged += _ =>
            {
                _selectedBlockout = _blockoutList.selectedIndex;
                BuildArtPane();
            };
            _blockoutList.itemsChosen += _ =>
            {
                var blockout = SelectedBlockout;
                if (blockout != null)
                    LevelDesignerContext.OpenBlockout(blockout);
            };
            left.Add(_blockoutList);
            if (Project.Blockouts.Count == 0)
                left.Add(Hint("No blockouts yet. Press + to make one: a new scene with a floor, ready for the blockout tools in the Scene view."));
            split.Add(left);

            // Right: art designs of the selected blockout.
            _artPane = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            split.Add(_artPane);

            if (SelectedBlockout == null && Project.Blockouts.Count > 0)
                _selectedBlockout = Mathf.Max(0, Project.Blockouts.IndexOf(LevelDesignerContext.CurrentBlockoutEntry));
            if (SelectedBlockout != null)
                _blockoutList.SetSelectionWithoutNotify(new[] { _selectedBlockout });
            BuildArtPane();
        }

        // A blockout row: its thumbnail, then its name (● when it's the one open) and art design count.
        private static VisualElement MakeBlockoutItem()
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingLeft = 4 } };
            var image = new Image { name = "thumbnail", scaleMode = ScaleMode.ScaleAndCrop };
            image.style.width = 64;
            image.style.height = 36;
            image.style.flexShrink = 0;
            image.style.marginRight = 6;
            image.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            row.Add(image);
            row.Add(new Label { name = "name", style = { unityTextAlign = TextAnchor.MiddleLeft, flexGrow = 1, overflow = Overflow.Hidden } });
            return row;
        }

        private void BindBlockoutItem(VisualElement element, int index)
        {
            var blockout = Project.Blockouts[index];
            var label = element.Q<Label>("name");
            bool current = LevelDesignerContext.CurrentBlockoutEntry == blockout;
            label.text = (current ? "● " : "") + (string.IsNullOrEmpty(blockout.Name) ? "(unnamed)" : blockout.Name) +
                         (blockout.ArtDesigns.Count > 0 ? $"  ({blockout.ArtDesigns.Count})" : "");
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

        // A 16:9 thumbnail for an art card (a dark placeholder until the scene is first saved while open).
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
                $"Remove \"{blockout.Name}\" and its {blockout.ArtDesigns.Count} art design(s) from the Level Designer?",
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
                _artPane.Add(Hint("Select a blockout to see its art designs."));
                return;
            }

            // The blockout itself.
            var header = Header("Blockout");
            var nameField = new TextField { value = blockout.Name, isDelayed = true, style = { flexGrow = 1 } };
            nameField.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Rename Blockout");
                blockout.Name = evt.newValue;
                _blockoutList?.RefreshItems();
            });
            header.Add(nameField);
            header.Add(SmallButton("Open", "Open the blockout scene (blockout tools appear in the Scene view)", () => LevelDesignerContext.OpenBlockout(blockout)));
            header.Add(SmallButton("Ping", "Show the scene in the Project window", () => Ping(LevelDesignerContext.PathOf(blockout))));
            _artPane.Add(header);

            var path = new Label(LevelDesignerContext.PathOf(blockout)) { style = { fontSize = 10, color = new Color(0.6f, 0.6f, 0.6f), marginLeft = 6 } };
            _artPane.Add(path);
            var blockoutThumb = CardThumbnail(blockout.SceneGuid, LevelDesignerContext.PathOf(blockout));
            blockoutThumb.style.width = 197;
            blockoutThumb.style.marginLeft = 6;
            blockoutThumb.style.marginTop = 4;
            _artPane.Add(blockoutThumb);
            _artPane.Add(NotesField(blockout.Notes, value => blockout.Notes = value, "Notes about this blockout"));

            // Art designs.
            var artHeader = Header("Art Designs");
            var newName = new TextField { value = "", style = { flexGrow = 1 }, tooltip = "Name for the new art design" };
            newName.textEdition.placeholder = "New art design name";
            artHeader.Add(newName);
            artHeader.Add(SmallButton("+ Add Art Design", "Make a new art design scene over this blockout and open it", () =>
            {
                var art = LevelDesignerContext.CreateArtDesign(blockout, newName.value);
                if (art != null)
                    BuildArtPane();
            }));
            _artPane.Add(artHeader);

            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, paddingLeft = 4, paddingRight = 4 } };
            foreach (var art in blockout.ArtDesigns)
                grid.Add(ArtCard(blockout, art));
            grid.Add(PlusCard(blockout, newName));
            _artPane.Add(grid);
        }

        private VisualElement ArtCard(BlockoutLevel blockout, ArtDesign art)
        {
            var card = Card();
            string artPath = LevelDesignerContext.PathOf(art);
            bool isOpen = SceneManager.GetActiveScene().path == artPath;
            if (isOpen)
                card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor = new Color(0.35f, 0.7f, 1f);

            card.Add(CardThumbnail(art.SceneGuid, artPath));
            var name = new TextField { value = art.Name, isDelayed = true };
            name.RegisterValueChangedCallback(evt =>
            {
                LevelDesignerContext.RecordProject("Rename Art Design");
                art.Name = evt.newValue;
            });
            card.Add(name);

            bool exists = !string.IsNullOrEmpty(artPath) && File.Exists(artPath);
            var pathLabel = new Label(exists ? Path.GetFileName(artPath) : "Scene missing!") { tooltip = artPath };
            pathLabel.style.fontSize = 10;
            pathLabel.style.color = exists ? new Color(0.6f, 0.6f, 0.6f) : new Color(1f, 0.4f, 0.3f);
            card.Add(pathLabel);
            card.Add(NotesField(art.Notes, value => art.Notes = value, "Notes"));

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            buttons.Add(SmallButton(isOpen ? "Opened" : "Open", "Open this art design with the blockout underneath", () => LevelDesignerContext.OpenArtDesign(blockout, art)));
            buttons.Add(SmallButton("Duplicate", "Copy this art design's scene into a new art design", () =>
            {
                LevelDesignerContext.DuplicateArtDesign(blockout, art);
                BuildArtPane();
            }));
            buttons.Add(SmallButton("✕", "Remove this art design", () =>
            {
                int choice = EditorUtility.DisplayDialogComplex("Remove Art Design", $"Remove \"{art.Name}\"?", "Remove from List", "Cancel", "Remove and Delete Scene");
                if (choice == 1)
                    return;
                LevelDesignerContext.RemoveArtDesign(blockout, art, choice == 2);
                BuildArtPane();
            }));
            card.Add(buttons);
            card.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                    LevelDesignerContext.OpenArtDesign(blockout, art);
            });
            return card;
        }

        private VisualElement PlusCard(BlockoutLevel blockout, TextField nameField)
        {
            var card = Card();
            card.style.justifyContent = Justify.Center;
            card.style.alignItems = Align.Center;
            var plus = new Button(() =>
            {
                if (LevelDesignerContext.CreateArtDesign(blockout, nameField.value) != null)
                    BuildArtPane();
            })
            {
                text = "+",
                tooltip = "New art design over this blockout",
            };
            plus.style.fontSize = 32;
            plus.style.width = 64;
            plus.style.height = 64;
            card.Add(plus);
            card.Add(new Label("New Art Design") { style = { marginTop = 4 } });
            return card;
        }

        private static VisualElement Card()
        {
            var card = new VisualElement();
            card.style.width = 210;
            card.style.minHeight = 130;
            card.style.marginRight = card.style.marginBottom = 6;
            card.style.paddingLeft = card.style.paddingRight = card.style.paddingTop = card.style.paddingBottom = 6;
            card.style.backgroundColor = new Color(0f, 0f, 0f, 0.15f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            card.style.borderLeftWidth = card.style.borderRightWidth = card.style.borderTopWidth = card.style.borderBottomWidth = 1;
            card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor = new Color(1f, 1f, 1f, 0.08f);
            return card;
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

        // ---------------------------------------------------------------- characters

        private void BuildCharacters()
        {
            var header = Header("Characters");
            header.Add(SmallButton("Find in Project", "Look for prefabs with the character controller", () =>
            {
                _characters.Clear();
                _characters.AddRange(CharacterMetricsAnalyzer.FindCharacterPrefabs());
                _charactersScanned = true;
                Rebuild();
            }));
            var add = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = false, tooltip = "Add a character prefab", style = { width = 180 } };
            add.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is GameObject prefab && !_characters.Contains(prefab))
                {
                    if (CharacterMetricsAnalyzer.IsCharacter(prefab))
                    {
                        _characters.Add(prefab);
                        Rebuild();
                    }
                    else
                        EditorUtility.DisplayDialog("Level Designer", $"{prefab.name} doesn't have the Physics Based Character Controller.", "OK");
                }
            });
            header.Add(add);
            header.Add(SmallButton("Export JSON", "Save every listed character's numbers as JSON", () => CharacterMetricsAnalyzer.Export(CollectMetrics(), false)));
            header.Add(SmallButton("Export CSV", "Save every listed character's numbers as CSV", () => CharacterMetricsAnalyzer.Export(CollectMetrics(), true)));
            _body.Add(header);

            if (ActiveCharacter.Prefab != null && !_characters.Contains(ActiveCharacter.Prefab))
                _characters.Insert(0, ActiveCharacter.Prefab);

            var scroll = new ScrollView { style = { flexGrow = 1 } };
            _body.Add(scroll);
            if (_characters.Count == 0)
            {
                scroll.Add(Hint(_charactersScanned
                    ? "No prefabs with the Physics Based Character Controller were found."
                    : "Press Find in Project, or drop a character prefab in the field above."));
                return;
            }

            foreach (var prefab in _characters)
            {
                if (prefab == null)
                    continue;
                scroll.Add(CharacterCard(prefab));
            }
        }

        private List<CharacterMetrics> CollectMetrics()
        {
            var list = new List<CharacterMetrics>();
            foreach (var prefab in _characters)
                if (prefab != null && CharacterMetricsAnalyzer.TryAnalyze(prefab, out var metrics))
                    list.Add(metrics);
            return list;
        }

        private VisualElement CharacterCard(GameObject prefab)
        {
            var card = Card();
            card.style.width = StyleKeyword.Auto;
            card.style.minHeight = StyleKeyword.Auto;
            card.style.marginLeft = 6;

            var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var title = new Label(prefab.name) { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } };
            top.Add(title);
            bool active = ActiveCharacter.Prefab == prefab;
            var useToggle = new Toggle("Use for Rulers") { value = active, tooltip = "The blockout's Jump Arc and Run rulers use this character" };
            useToggle.RegisterValueChangedCallback(evt =>
            {
                ActiveCharacter.Prefab = evt.newValue ? prefab : null;
                Rebuild();
            });
            top.Add(useToggle);
            top.Add(SmallButton("Ping", "Show the prefab", () => EditorGUIUtility.PingObject(prefab)));
            top.Add(SmallButton("Jump Ruler Here", "Place a jump arc ruler at the Scene view's pivot", () => PlaceRulerAtPivot(prefab, BlockoutRulerKind.JumpArc)));
            top.Add(SmallButton("Run Ruler Here", "Place a 1 second run ruler at the Scene view's pivot", () => PlaceRulerAtPivot(prefab, BlockoutRulerKind.RunDistance)));
            card.Add(top);

            if (!CharacterMetricsAnalyzer.TryAnalyze(prefab, out var m))
            {
                card.Add(Hint("The controller has no Properties assigned, so there's nothing to measure."));
                return card;
            }

            var table = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 4 } };
            void Row(string label, string value, string tooltip)
            {
                var cell = new VisualElement { tooltip = tooltip, style = { width = 175, flexDirection = FlexDirection.Row, marginBottom = 2 } };
                cell.Add(new Label(label) { style = { width = 105, color = new Color(0.7f, 0.7f, 0.7f) } });
                cell.Add(new Label(value) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                table.Add(cell);
            }
            string M(float v) => BlockoutShape.FormatMeters(v) + " m";
            string S(float v) => v.ToString("0.##") + " s";
            Row("Run speed", BlockoutShape.FormatMeters(m.MoveSpeed) + " m/s", "Top speed on the ground and in the air");
            Row("Run 1 s / 3 s", M(m.RunDistance(1f)) + " / " + M(m.RunDistance(3f)), "Distance from a standstill");
            Row("To top speed", S(m.TimeToTopSpeed) + ", " + M(m.DistanceToTopSpeed), "Time and distance to reach top speed");
            Row("Stopping", M(m.StopDistance), "Slide after letting go at top speed");
            Row("Jump height", M(m.JumpHeight), "How far the feet rise (highest ledge that can be jumped onto)");
            Row("Rise / hang", S(m.RiseTime) + " / " + S(m.HangTime), "Time going up, time held at the top");
            Row("Air time", S(m.AirTime), "A jump landing at the height it started");
            Row("Jump distance", M(m.JumpDistance), "A running jump landing at the starting height (widest gap)");
            Row("Fall", BlockoutShape.FormatMeters(m.FallAcceleration) + " m/s²", "Downward acceleration while falling, gravity included");
            Row("Jump again after", S(m.JumpCooldown), "Delay after landing before the next jump");
            if (m.CanHover)
            {
                Row("Hover", S(m.HoverTime) + " at " + BlockoutShape.FormatMeters(m.HoverSpeed) + " m/s", "A full hover");
                Row("Hover distance", M(m.HoverDistance), "Extra distance a full hover adds");
            }
            card.Add(table);
            card.Add(new Label($"Gravity {BlockoutShape.FormatMeters(m.Gravity)} m/s², physics step {m.FixedTimeStep:0.###} s. Predictions from the tuning: springs and collisions make the real motion a little softer.")
            {
                style = { fontSize = 10, color = new Color(0.6f, 0.6f, 0.6f), whiteSpace = WhiteSpace.Normal, marginTop = 2 },
            });
            return card;
        }

        private static void PlaceRulerAtPivot(GameObject prefab, BlockoutRulerKind kind)
        {
            if (LevelDesignerContext.Mode != LevelDesignerMode.Blockout)
            {
                EditorUtility.DisplayDialog("Level Designer", "Open a blockout first: rulers live in blockout scenes.", "OK");
                return;
            }
            if (!CharacterMetricsAnalyzer.TryAnalyze(prefab, out var metrics))
                return;
            var view = SceneView.lastActiveSceneView;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            var forward = view != null ? view.rotation * Vector3.forward : Vector3.forward;
            forward.y = 0f;
            var rotation = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized) : Quaternion.identity;
            var ruler = LevelDesignerSceneGUI.CreateRuler(kind, LevelDesignerSettings.SnapPoint(at), rotation,
                $"{metrics.CharacterName} {(kind == BlockoutRulerKind.JumpArc ? "Jump" : "Run")}");
            ruler.Character = prefab;
            ruler.Metrics = metrics;
            if (kind == BlockoutRulerKind.RunDistance)
                ruler.Color = new Color(0.45f, 0.75f, 1f);
            Selection.activeGameObject = ruler.gameObject;
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
}
