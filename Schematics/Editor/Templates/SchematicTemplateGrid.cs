using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// A Project-window-like browser for Schematic templates: a search field, category chips, a size slider and a
    /// wrapping grid of square thumbnails with the name underneath. Click selects, double-click or Enter picks
    /// (<see cref="Picked"/>), arrow keys move, right-click offers Open Prefab / Ping / Remove, and the "+" tile makes
    /// new templates. Tiles can be dragged into the Scene view to place them.
    /// </summary>
    /// <remarks>
    /// Self-contained and reusable: drop it into any EditorWindow or panel (the "Create New Schematic Object" window
    /// and the Level Designer both do). It listens to <see cref="SchematicTemplates.Changed"/> and the thumbnail
    /// renderer while it's attached to a panel, so it stays current by itself. Use <see cref="Filter"/> to show only
    /// some templates (e.g. <c>SchematicTemplates.IsLevelDesign</c>).
    /// </remarks>
    public class SchematicTemplateGrid : VisualElement
    {
        /// <summary>The DragAndDrop generic-data key a dragged tile carries (its <see cref="SchematicTemplate"/>).</summary>
        public const string DragDataKey = "Remedy.SchematicTemplate";

        private const string AllCategory = "All";
        private const float MinTileSize = 48f, MaxTileSize = 192f;
        private const float LabelHeight = 30f, TileMargin = 4f;

        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.07f);
        private static Color SelectedColor => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f, 1f) : new Color(0.23f, 0.45f, 0.69f, 1f);
        private static Color SelectedTextColor => Color.white;

        private readonly string _prefsKey;
        private readonly ToolbarSearchField _search;
        private readonly Slider _sizeSlider;
        private readonly VisualElement _categoryBar;
        private readonly ScrollView _scroll;
        private readonly VisualElement _content;
        private readonly Label _empty;
        private readonly Label _details;
        private readonly List<Tile> _tiles = new();

        private Func<SchematicTemplate, bool> _filter;
        private string _category = AllCategory;
        private float _tileSize;
        private bool _showCreateTile = true;

        /// <summary>A template was chosen (double-click, Enter, or <see cref="PickSelected"/>).</summary>
        public event Action<SchematicTemplate> Picked;

        /// <summary>The selected tile changed (null when nothing is selected).</summary>
        public event Action<SchematicTemplate> SelectionChanged;

        /// <summary>The "+" tile made a new template.</summary>
        public event Action<SchematicTemplate> TemplateCreated;

        /// <summary>The selected template, or null.</summary>
        public SchematicTemplate Selected { get; private set; }

        /// <summary>The templates currently shown (after search, category and <see cref="Filter"/>).</summary>
        public IReadOnlyList<SchematicTemplate> Visible => _tiles.Where(t => t.Template != null).Select(t => t.Template).ToList();

        /// <summary>Only templates this returns true for are shown. Null shows all.</summary>
        public Func<SchematicTemplate, bool> Filter
        {
            get => _filter;
            set { _filter = value; Refresh(); }
        }

        /// <summary>The category shown ("All" or null for every category).</summary>
        public string Category
        {
            get => _category;
            set { _category = string.IsNullOrEmpty(value) ? AllCategory : value; Save("Category", _category); Refresh(); }
        }

        /// <summary>The search text (matches name, category and description).</summary>
        public string SearchText
        {
            get => _search.value;
            set => _search.value = value ?? "";
        }

        /// <summary>Thumbnail size in points (48 to 192).</summary>
        public float TileSize
        {
            get => _tileSize;
            set
            {
                _tileSize = Mathf.Clamp(value, MinTileSize, MaxTileSize);
                _sizeSlider.SetValueWithoutNotify(_tileSize);
                Save("TileSize", _tileSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var tile in _tiles) tile.Resize(_tileSize);
            }
        }

        /// <summary>Show the "+" tile that makes new templates.</summary>
        public bool ShowCreateTile
        {
            get => _showCreateTile;
            set { _showCreateTile = value; Refresh(); }
        }

        /// <summary>Show the category chips.</summary>
        public bool ShowCategories
        {
            get => _categoryBar.style.display != DisplayStyle.None;
            set => _categoryBar.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Show the line under the grid describing the selected template.</summary>
        public bool ShowDetails
        {
            get => _details.style.display != DisplayStyle.None;
            set => _details.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The catalog new templates from the "+" tile go into. Null: the default catalog.</summary>
        public SchematicTemplatesData TargetCatalog { get; set; }

        /// <summary>The category new templates get. Null: the selected category, or "Custom" under All.</summary>
        public string NewTemplateCategory { get; set; }

        /// <summary>Whether new templates from the "+" tile are flagged as Level Design.</summary>
        public bool NewTemplatesAreLevelDesign { get; set; }

        /// <param name="prefsKey">Remembers the tile size and category per use (EditorPrefs key prefix).</param>
        public SchematicTemplateGrid(string prefsKey = "Remedy.Schematics.TemplateGrid")
        {
            _prefsKey = prefsKey;
            focusable = true;
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Column;

            // Toolbar: search + size.
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;
            _search = new ToolbarSearchField();
            _search.style.flexGrow = 1;
            _search.style.width = StyleKeyword.Auto;
            _search.RegisterValueChangedCallback(_ => Refresh());
            _search.RegisterCallback<KeyDownEvent>(OnSearchKeyDown, TrickleDown.TrickleDown);
            toolbar.Add(_search);

            _sizeSlider = new Slider(MinTileSize, MaxTileSize) { tooltip = "Thumbnail size" };
            _sizeSlider.style.width = 90;
            _sizeSlider.style.marginLeft = 6;
            _sizeSlider.style.marginRight = 4;
            _sizeSlider.RegisterValueChangedCallback(evt => TileSize = evt.newValue);
            toolbar.Add(_sizeSlider);
            Add(toolbar);

            // Category chips.
            _categoryBar = new VisualElement();
            _categoryBar.style.flexDirection = FlexDirection.Row;
            _categoryBar.style.flexWrap = Wrap.Wrap;
            _categoryBar.style.flexShrink = 0;
            _categoryBar.style.paddingLeft = 4;
            _categoryBar.style.paddingRight = 4;
            _categoryBar.style.paddingTop = 3;
            _categoryBar.style.paddingBottom = 3;
            Add(_categoryBar);

            // The grid.
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _content = new VisualElement();
            _content.style.flexDirection = FlexDirection.Row;
            _content.style.flexWrap = Wrap.Wrap;
            _content.style.paddingLeft = 4;
            _content.style.paddingTop = 4;
            _content.style.paddingRight = 4;
            _content.style.paddingBottom = 4;
            _scroll.Add(_content);
            Add(_scroll);

            _empty = new Label("No templates yet.\nClick + to make one, or run Tools > Remedy > Schematics > Templates > Generate Level Kit Templates.");
            _empty.style.whiteSpace = WhiteSpace.Normal;
            _empty.style.unityTextAlign = TextAnchor.MiddleCenter;
            _empty.style.opacity = 0.6f;
            _empty.style.marginTop = 24;
            _empty.style.marginLeft = 12;
            _empty.style.marginRight = 12;

            _details = new Label();
            _details.style.flexShrink = 0;
            _details.style.whiteSpace = WhiteSpace.Normal;
            _details.style.paddingLeft = 6;
            _details.style.paddingRight = 6;
            _details.style.paddingTop = 3;
            _details.style.paddingBottom = 3;
            _details.style.minHeight = 20;
            _details.style.borderTopWidth = 1;
            _details.style.borderTopColor = new Color(0f, 0f, 0f, 0.25f);
            _details.style.opacity = 0.85f;
            Add(_details);

            _category = Load("Category", AllCategory);
            float.TryParse(Load("TileSize", "96"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size);
            _tileSize = Mathf.Clamp(size <= 0 ? 96f : size, MinTileSize, MaxTileSize);
            _sizeSlider.SetValueWithoutNotify(_tileSize);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                SchematicTemplates.Changed += OnTemplatesChanged;
                SchematicTemplateThumbnails.Updated += OnThumbnailUpdated;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                SchematicTemplates.Changed -= OnTemplatesChanged;
                SchematicTemplateThumbnails.Updated -= OnThumbnailUpdated;
            });
        }

        /// <summary>Focuses the search field (open the browser and start typing).</summary>
        public void FocusSearch() => _search.Q<TextField>()?.Focus();

        /// <summary>Picks the selected template, as double-click or Enter do.</summary>
        public void PickSelected()
        {
            if (Selected != null)
                Picked?.Invoke(Selected);
        }

        /// <summary>Selects a template (if it's visible).</summary>
        public void Select(SchematicTemplate template)
        {
            if (Selected == template) return;
            Selected = template;
            foreach (var tile in _tiles)
                tile.SetSelected(tile.Template != null && tile.Template == template);
            UpdateDetails();
            var selectedTile = _tiles.FirstOrDefault(t => t.Template == template && t.Template != null);
            if (selectedTile != null)
                _scroll.ScrollTo(selectedTile);
            SelectionChanged?.Invoke(template);
        }

        /// <summary>Rebuilds the chips and tiles from <see cref="SchematicTemplates.All"/>.</summary>
        public void Refresh()
        {
            BuildCategories();

            _content.Clear();
            _tiles.Clear();

            string search = _search.value?.Trim() ?? "";
            foreach (var template in SchematicTemplates.InCategory(_category))
            {
                if (_filter != null && !_filter(template))
                    continue;
                if (search.Length > 0 && !Matches(template, search))
                    continue;
                var tile = new Tile(this, template);
                _tiles.Add(tile);
                _content.Add(tile);
            }

            if (_showCreateTile)
            {
                var plus = new Tile(this, null);
                _tiles.Add(plus);
                _content.Add(plus);
            }

            bool anyTemplates = _tiles.Any(t => t.Template != null);
            if (!anyTemplates)
            {
                _empty.text = search.Length > 0 || _category != AllCategory || _filter != null
                    ? "Nothing matches."
                    : "No templates yet.\nClick + to make one, or run Tools > Remedy > Schematics > Templates > Generate Level Kit Templates.";
                _content.Add(_empty);
            }

            foreach (var tile in _tiles)
                tile.Resize(_tileSize);

            // Keep the selection if it's still shown; otherwise select the first.
            var keep = Selected != null && _tiles.Any(t => t.Template == Selected) ? Selected : _tiles.FirstOrDefault(t => t.Template != null)?.Template;
            Selected = null;
            Select(keep);
            if (keep == null)
                UpdateDetails();
        }

        private static bool Matches(SchematicTemplate template, string search)
        {
            foreach (var word in search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool found = template.DisplayName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                    || template.CategoryOrDefault.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                    || (template.Description ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!found) return false;
            }
            return true;
        }

        private void BuildCategories()
        {
            _categoryBar.Clear();
            var categories = new List<string> { AllCategory };
            foreach (var category in SchematicTemplates.Categories)
                if (_filter == null || SchematicTemplates.InCategory(category).Any(_filter))
                    categories.Add(category);
            if (!categories.Contains(_category))
                _category = AllCategory;

            foreach (var category in categories)
            {
                var chip = new Button(() => Category = category) { text = category };
                bool on = category == _category;
                chip.style.marginLeft = 0;
                chip.style.marginRight = 4;
                chip.style.marginTop = 1;
                chip.style.marginBottom = 1;
                chip.style.paddingLeft = 8;
                chip.style.paddingRight = 8;
                chip.style.height = 20;
                SetRadius(chip.style, 10);
                if (on)
                {
                    chip.style.backgroundColor = SelectedColor;
                    chip.style.color = SelectedTextColor;
                }
                _categoryBar.Add(chip);
            }
        }

        private void UpdateDetails()
        {
            if (Selected == null)
            {
                _details.text = _tiles.Count > 0 && _tiles.All(t => t.Template == null) ? "Click + to make a template." : "";
                return;
            }
            string text = $"<b>{Selected.DisplayName}</b>  <color=#888888>{Selected.CategoryOrDefault}</color>";
            if (!string.IsNullOrEmpty(Selected.Description))
                text += "  " + Selected.Description;
            _details.text = text;
        }

        private void OnTemplatesChanged() => schedule.Execute(Refresh);

        private void OnThumbnailUpdated(GameObject prefab)
        {
            foreach (var tile in _tiles)
                if (tile.Template != null && tile.Template.Prefab == prefab)
                    tile.UpdateThumbnail();
        }

        #region Keyboard

        private void OnSearchKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    PickSelected();
                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                    Focus();
                    Move(0, 1);
                    evt.StopPropagation();
                    break;
            }
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    PickSelected();
                    break;
                case KeyCode.LeftArrow: Move(-1, 0); break;
                case KeyCode.RightArrow: Move(1, 0); break;
                case KeyCode.UpArrow: Move(0, -1); break;
                case KeyCode.DownArrow: Move(0, 1); break;
                default: return;
            }
            evt.StopPropagation();
        }

        private void Move(int dx, int dy)
        {
            var templates = _tiles.Where(t => t.Template != null).Select(t => t.Template).ToList();
            if (templates.Count == 0) return;
            int index = Mathf.Max(0, templates.IndexOf(Selected));
            float width = _content.contentRect.width > 0 ? _content.contentRect.width : _scroll.contentViewport.layout.width;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / (_tileSize + TileMargin * 2f)));
            index = Mathf.Clamp(index + dx + dy * columns, 0, templates.Count - 1);
            Select(templates[index]);
        }

        #endregion

        #region New templates

        private void ShowCreateMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Empty Template…"), false, CreateEmpty);

            var selected = Selection.activeGameObject;
            bool sceneObject = selected != null && !EditorUtility.IsPersistent(selected);
            if (sceneObject)
                menu.AddItem(new GUIContent($"New Template From Selection ({selected.name})…"), false, () => CreateFromSelection(selected));
            else
                menu.AddDisabledItem(new GUIContent("New Template From Selection (select a scene object)"));

            menu.AddItem(new GUIContent("Add Existing Prefab…"), false, AddExistingPrefab);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New Catalog…"), false, CreateCatalog);
            menu.AddItem(new GUIContent("Show Default Catalog"), false, () =>
            {
                var catalog = SchematicTemplates.DefaultCatalog;
                Selection.activeObject = catalog;
                EditorGUIUtility.PingObject(catalog);
            });
            menu.ShowAsContext();
        }

        private string CategoryForNew => !string.IsNullOrEmpty(NewTemplateCategory) ? NewTemplateCategory : _category != AllCategory ? _category : "Custom";

        private void CreateEmpty()
        {
            SchematicObjectFactory.EnsureFolder(SchematicObjectFactory.CustomTemplatesFolder);
            string path = EditorUtility.SaveFilePanelInProject("New Schematic Template", "New Template", "prefab",
                "Where should the template prefab go?", SchematicObjectFactory.CustomTemplatesFolder);
            if (string.IsNullOrEmpty(path)) return;
            Created(SchematicObjectFactory.CreateEmptyTemplate(path, CategoryForNew, TargetCatalog));
        }

        private void CreateFromSelection(GameObject sceneObject)
        {
            SchematicObjectFactory.EnsureFolder(SchematicObjectFactory.CustomTemplatesFolder);
            string path = EditorUtility.SaveFilePanelInProject("New Schematic Template From Selection", sceneObject.name, "prefab",
                $"Save '{sceneObject.name}' as a template prefab (the scene object becomes an instance of it).", SchematicObjectFactory.CustomTemplatesFolder);
            if (string.IsNullOrEmpty(path)) return;
            Created(SchematicObjectFactory.CreateTemplateFromSceneObject(sceneObject, path, CategoryForNew, TargetCatalog));
        }

        private void AddExistingPrefab()
        {
            string absolute = EditorUtility.OpenFilePanelWithFilters("Add Prefab as Template", Application.dataPath, new[] { "Prefab", "prefab" });
            if (string.IsNullOrEmpty(absolute)) return;
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            absolute = Path.GetFullPath(absolute).Replace('\\', '/');
            if (!absolute.StartsWith(projectRoot + "/"))
            {
                EditorUtility.DisplayDialog("Add Prefab as Template", "Pick a prefab inside this project.", "OK");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(absolute.Substring(projectRoot.Length + 1));
            if (prefab == null) return;
            if (!prefab.TryGetComponent(out SchematicObject _) &&
                EditorUtility.DisplayDialog("Add Prefab as Template", $"'{prefab.name}' has no Schematic Object. Add one (with its own graph)?", "Add", "Keep as is"))
            {
                using (var scope = new PrefabUtility.EditPrefabContentsScope(AssetDatabase.GetAssetPath(prefab)))
                    scope.prefabContentsRoot.AddComponent<SchematicObject>();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(prefab));
                SchematicObjectFactory.EnsureOwnGraph(prefab);
            }
            Created(SchematicTemplates.Add(prefab, null, CategoryForNew, null, NewTemplatesAreLevelDesign ? true : (bool?)null, TargetCatalog));
        }

        private void CreateCatalog()
        {
            SchematicObjectFactory.EnsureFolder(SchematicObjectFactory.TemplatesFolder);
            string path = EditorUtility.SaveFilePanelInProject("New Templates Catalog", "Schematic Templates", "asset",
                "Catalogs anywhere in the project are merged into the browser.", SchematicObjectFactory.TemplatesFolder);
            if (string.IsNullOrEmpty(path)) return;
            var catalog = SchematicTemplates.CreateCatalog(path);
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
        }

        private void Created(SchematicTemplate template)
        {
            if (template == null) return;
            if (NewTemplatesAreLevelDesign)
                SchematicTemplates.SetLevelDesign(template, true);
            Refresh();
            Select(template);
            TemplateCreated?.Invoke(template);
        }

        #endregion

        #region Context menu

        private void BuildTileMenu(ContextualMenuPopulateEvent evt, SchematicTemplate template)
        {
            Select(template);
            var prefab = template.Prefab;
            evt.menu.AppendAction("Create", _ => Picked?.Invoke(template));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Open Prefab", _ => AssetDatabase.OpenAsset(prefab));
            evt.menu.AppendAction("Open Schematic Graph", _ => SchematicObjectFactory.OpenGraph(prefab),
                SchematicObjectFactory.GraphOf(prefab) != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction("Ping in Project", _ => { EditorGUIUtility.PingObject(prefab); Selection.activeObject = prefab; });
            evt.menu.AppendAction("Show Catalog", _ =>
            {
                var catalog = SchematicTemplates.CatalogOf(template);
                Selection.activeObject = catalog;
                EditorGUIUtility.PingObject(catalog);
            });
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Level Design", _ => SchematicTemplates.SetLevelDesign(template, !SchematicTemplates.IsLevelDesign(template)),
                SchematicTemplates.IsLevelDesign(template) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction("Refresh Thumbnail", _ => SchematicTemplateThumbnails.Invalidate(prefab));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Remove From Templates", _ =>
            {
                if (EditorUtility.DisplayDialog("Remove Template", $"Remove '{template.DisplayName}' from the templates? The prefab itself is kept.", "Remove", "Cancel"))
                    SchematicTemplates.Remove(template);
            });
            evt.StopPropagation();
        }

        #endregion

        #region Prefs

        private void Save(string key, string value) => EditorPrefs.SetString($"{_prefsKey}.{key}", value);
        private string Load(string key, string fallback) => EditorPrefs.GetString($"{_prefsKey}.{key}", fallback);

        private static void SetRadius(IStyle style, float radius)
        {
            style.borderTopLeftRadius = radius;
            style.borderTopRightRadius = radius;
            style.borderBottomLeftRadius = radius;
            style.borderBottomRightRadius = radius;
        }

        #endregion

        /// <summary>One square in the grid: a template, or (with no template) the "+" tile.</summary>
        private sealed class Tile : VisualElement
        {
            public readonly SchematicTemplate Template;
            private readonly SchematicTemplateGrid _grid;
            private readonly Image _image;
            private readonly Label _label;
            private readonly Label _plus;
            private bool _selected, _hovered;
            private Vector2 _pressPosition;
            private bool _pressed;

            public Tile(SchematicTemplateGrid grid, SchematicTemplate template)
            {
                _grid = grid;
                Template = template;
                style.marginLeft = TileMargin;
                style.marginRight = TileMargin;
                style.marginTop = TileMargin;
                style.marginBottom = TileMargin;
                style.paddingLeft = 3;
                style.paddingRight = 3;
                style.paddingTop = 3;
                style.paddingBottom = 2;
                style.alignItems = Align.Center;
                SetRadius(style, 4);

                _image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                SetRadius(_image.style, 3);
                Add(_image);

                _label = new Label { pickingMode = PickingMode.Ignore };
                _label.style.unityTextAlign = TextAnchor.UpperCenter;
                _label.style.whiteSpace = WhiteSpace.Normal;
                _label.style.overflow = Overflow.Hidden;
                _label.style.fontSize = 11;
                _label.style.marginTop = 2;
                _label.style.paddingLeft = 2;
                _label.style.paddingRight = 2;
                SetRadius(_label.style, 3);
                Add(_label);

                if (template == null)
                {
                    _label.text = "New Template…";
                    tooltip = "Make a new template: empty, from the selected scene object, or from an existing prefab.";
                    _plus = new Label("+") { pickingMode = PickingMode.Ignore };
                    _plus.style.position = Position.Absolute;
                    _plus.style.left = 0;
                    _plus.style.right = 0;
                    _plus.style.top = 0;
                    _plus.style.unityTextAlign = TextAnchor.MiddleCenter;
                    _plus.style.opacity = 0.6f;
                    _image.Add(_plus);
                    _image.style.borderTopWidth = _image.style.borderBottomWidth = _image.style.borderLeftWidth = _image.style.borderRightWidth = 1;
                    var dashed = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                    _image.style.borderTopColor = _image.style.borderBottomColor = _image.style.borderLeftColor = _image.style.borderRightColor = dashed;
                    RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        _grid.ShowCreateMenu();
                        evt.StopPropagation();
                    });
                }
                else
                {
                    _label.text = template.DisplayName;
                    string path = AssetDatabase.GetAssetPath(template.Prefab);
                    tooltip = string.IsNullOrEmpty(template.Description)
                        ? $"{template.DisplayName}\n{template.CategoryOrDefault}\n{path}"
                        : $"{template.DisplayName}\n{template.Description}\n{template.CategoryOrDefault} · {path}";
                    UpdateThumbnail();

                    RegisterCallback<PointerDownEvent>(OnPointerDown);
                    RegisterCallback<PointerMoveEvent>(OnPointerMove);
                    RegisterCallback<PointerUpEvent>(_ => _pressed = false);
                    this.AddManipulator(new ContextualMenuManipulator(evt => _grid.BuildTileMenu(evt, Template)));
                }

                RegisterCallback<PointerEnterEvent>(_ => { _hovered = true; Paint(); });
                RegisterCallback<PointerLeaveEvent>(_ => { _hovered = false; _pressed = false; Paint(); });
            }

            public void UpdateThumbnail()
            {
                if (Template != null)
                    _image.image = SchematicTemplates.Thumbnail(Template.Prefab);
            }

            public void Resize(float size)
            {
                style.width = size + 6;
                _image.style.width = size;
                _image.style.height = size;
                _label.style.width = size + 4;
                _label.style.height = LabelHeight;
                _label.style.fontSize = size < 72 ? 10 : 11;
                if (_plus != null)
                {
                    _plus.style.height = size;
                    _plus.style.fontSize = Mathf.Round(size * 0.4f);
                }
            }

            public void SetSelected(bool selected)
            {
                _selected = selected;
                Paint();
            }

            private void Paint()
            {
                style.backgroundColor = _hovered && !_selected ? HoverColor : Color.clear;
                _label.style.backgroundColor = _selected ? SelectedColor : Color.clear;
                _label.style.color = _selected ? new StyleColor(SelectedTextColor) : new StyleColor(StyleKeyword.Null);
                _image.style.opacity = Template == null && !_hovered ? 0.7f : 1f;
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0) return;
                _grid.Focus();
                _grid.Select(Template);
                _pressed = true;
                _pressPosition = evt.position;
                if (evt.clickCount == 2)
                {
                    _pressed = false;
                    _grid.Picked?.Invoke(Template);
                }
                evt.StopPropagation();
            }

            // Dragging a tile into the Scene view places it there (handled by CreateSchematicObjectWindow's Scene hook).
            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (!_pressed || (evt.pressedButtons & 1) == 0)
                    return;
                if (((Vector2)evt.position - _pressPosition).sqrMagnitude < 36f)
                    return;
                _pressed = false;
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.paths = Array.Empty<string>();
                DragAndDrop.SetGenericData(DragDataKey, Template);
                DragAndDrop.StartDrag(Template.DisplayName);
                evt.StopPropagation();
            }
        }
    }
}
