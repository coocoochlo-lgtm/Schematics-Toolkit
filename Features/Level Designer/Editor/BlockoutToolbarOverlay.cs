using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// The blockout toolbar in the Scene view, shown while a blockout is the active scene. Its main buttons are
    /// <b>Primitive</b>, <b>Rulers</b> and <b>Schematics</b>: a left click places the last item used from that button,
    /// a right click opens a searchable list of everything it offers (with thumbnails), laid out across the toolbar.
    /// Next to them: the colour swatch (new shapes and rulers take its colour; click it for the project palette),
    /// the <b>Paint</b> bucket, and a snap menu that sets the placement grid.
    /// </summary>
    [Overlay(typeof(SceneView), "Remedy.LevelDesigner.BlockoutToolbar", "Blockout Tools", true)]
    public class BlockoutToolbarOverlay : Overlay, ITransientOverlay, ICreateHorizontalToolbar, ICreateVerticalToolbar
    {
        public bool visible => LevelDesignerContext.Mode == LevelDesignerMode.Blockout;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            Fill(root, false);
            return root;
        }

        public OverlayToolbar CreateHorizontalToolbarContent()
        {
            var toolbar = new OverlayToolbar();
            Fill(toolbar, false);
            return toolbar;
        }

        public OverlayToolbar CreateVerticalToolbarContent()
        {
            var toolbar = new OverlayToolbar();
            Fill(toolbar, true);
            return toolbar;
        }

        private void Fill(VisualElement container, bool vertical)
        {
            container.Add(MainButton("Primitive", BlockoutPalette.Primitives, vertical));
            container.Add(MainButton("Rulers", BlockoutPalette.Rulers, vertical));
            container.Add(MainButton("Schematics", BlockoutPalette.Schematics, vertical));
            container.Add(ColorSwatch());
            container.Add(PaintButton());
            container.Add(CharacterButton());

            var snap = new EditorToolbarDropdown(SnapText(), null);
            snap.tooltip = "Snapping step for placing and sizing blockout shapes";
            snap.clicked += () =>
            {
                var menu = new GenericMenu();
                foreach (float step in LevelDesignerSettings.SnapSteps)
                {
                    float value = step;
                    menu.AddItem(new GUIContent(value <= 0f ? "Off" : BlockoutShape.FormatMeters(value) + " m"),
                        Mathf.Approximately(LevelDesignerSettings.Snap, value), () =>
                        {
                            LevelDesignerSettings.Snap = value;
                            snap.text = SnapText();
                        });
                }
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Ctrl + Drag Ruler"), LevelDesignerSettings.CtrlRuler, () => LevelDesignerSettings.CtrlRuler = !LevelDesignerSettings.CtrlRuler);
                menu.AddItem(new GUIContent("Keep Ctrl + Drag Rulers (else Alt + release)"), LevelDesignerSettings.KeepCtrlRulers, () => LevelDesignerSettings.KeepCtrlRulers = !LevelDesignerSettings.KeepCtrlRulers);
                menu.AddItem(new GUIContent("Paint Selected with Current Colour"), false, BlockoutPainter.PaintSelectionWithCurrent);
                menu.AddItem(new GUIContent("Edit Palette…"), false, LevelDesignerWindow.OpenPalette);
                menu.AddItem(new GUIContent("Open Level Designer"), false, LevelDesignerWindow.Open);
                menu.DropDown(snap.worldBound);
            };
            container.Add(snap);
        }

        // The colour square: shows the current colour ("Auto" when none is picked); click for the palette menu.
        private EditorToolbarDropdown ColorSwatch()
        {
            var swatch = new EditorToolbarDropdown("Auto", null);
            swatch.tooltip = "Colour for new shapes and rulers, and for the paint bucket (click for the palette)";
            void Sync()
            {
                bool has = LevelDesignerSettings.TryGetCurrentColor(out var color);
                swatch.icon = BlockoutPainter.Swatch(color, !has);
                swatch.text = has ? "" : "Auto";
            }
            Sync();
            swatch.clicked += () =>
            {
                var window = containerWindow;
                Vector2 origin = window != null ? window.position.position : Vector2.zero;
                BlockoutColorPopup.Show(new Rect(origin + swatch.worldBound.position, swatch.worldBound.size));
            };
            swatch.RegisterCallback<AttachToPanelEvent>(_ => LevelDesignerSettings.Changed += Sync);
            swatch.RegisterCallback<DetachFromPanelEvent>(_ => LevelDesignerSettings.Changed -= Sync);
            return swatch;
        }

        // The reference character the jump and run rulers are measured from: click for the Character panel, or drop a
        // character prefab on it.
        private EditorToolbarDropdown CharacterButton()
        {
            var button = new EditorToolbarDropdown("Character", EditorGUIUtility.IconContent("d_Avatar Icon")?.image as Texture2D);
            void Sync()
            {
                var prefab = ActiveCharacter.Prefab;
                button.text = prefab != null ? prefab.name : "Character";
                button.tooltip = prefab != null
                    ? $"Rulers are measured from {prefab.name} (click for its numbers, or drop another character prefab here)"
                    : "Pick the reference character the jump and run rulers are measured from (or drop a character prefab here)";
            }
            Sync();
            button.clicked += () =>
            {
                var window = containerWindow;
                Vector2 origin = window != null ? window.position.position : Vector2.zero;
                CharacterPickerWindow.Show(new Rect(origin + button.worldBound.position, button.worldBound.size));
            };
            button.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] is GameObject prefab
                    && EditorUtility.IsPersistent(prefab) && CharacterMetricsAnalyzer.IsCharacter(prefab))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                    evt.StopPropagation();
                }
            });
            button.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] is GameObject prefab
                    && EditorUtility.IsPersistent(prefab) && CharacterMetricsAnalyzer.IsCharacter(prefab))
                {
                    DragAndDrop.AcceptDrag();
                    ActiveCharacter.Prefab = prefab;
                    evt.StopPropagation();
                }
            });
            button.RegisterCallback<AttachToPanelEvent>(_ => ActiveCharacter.Changed += Sync);
            button.RegisterCallback<DetachFromPanelEvent>(_ => ActiveCharacter.Changed -= Sync);
            return button;
        }

        // The paint bucket: on while its tool is active.
        private static EditorToolbarToggle PaintButton()
        {
            var toggle = new EditorToolbarToggle("Paint");
            toggle.tooltip = "Paint bucket: click a shape or ruler to give it the current colour (Esc to stop)";
            void Sync() => toggle.SetValueWithoutNotify(BlockoutPaintTool.IsActive);
            Sync();
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    BlockoutPaintTool.Activate();
                else if (BlockoutPaintTool.IsActive)
                    UnityEditor.EditorTools.ToolManager.RestorePreviousPersistentTool();
            });
            toggle.RegisterCallback<AttachToPanelEvent>(_ => UnityEditor.EditorTools.ToolManager.activeToolChanged += Sync);
            toggle.RegisterCallback<DetachFromPanelEvent>(_ => UnityEditor.EditorTools.ToolManager.activeToolChanged -= Sync);
            return toggle;
        }

        private static string SnapText() => LevelDesignerSettings.Snap <= 0f ? "Snap: Off" : "Snap: " + BlockoutShape.FormatMeters(LevelDesignerSettings.Snap) + " m";

        private EditorToolbarButton MainButton(string title, BlockoutPalette palette, bool vertical)
        {
            var button = new EditorToolbarButton(title, null, null);
            button.tooltip = $"{title}: click to place the last one used, right-click for the list";
            UpdateButton(button, title, palette);
            button.clicked += () =>
            {
                var item = BlockoutCatalog.Last(palette);
                if (item != null)
                    BlockoutPlaceTool.Activate(item);
                else
                    OpenFlyout(button, palette, vertical);
            };
            // Right click: the list. Caught on the way down so the button doesn't treat it as anything else.
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1)
                    return;
                OpenFlyout(button, palette, vertical);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<ContextClickEvent>(evt => evt.StopPropagation());
            System.Action<BlockoutItem> onPicked = _ => UpdateButton(button, title, palette);
            button.RegisterCallback<AttachToPanelEvent>(_ => BlockoutFlyout.Picked += onPicked);
            button.RegisterCallback<DetachFromPanelEvent>(_ => BlockoutFlyout.Picked -= onPicked);
            return button;
        }

        private static void UpdateButton(EditorToolbarButton button, string title, BlockoutPalette palette)
        {
            var last = BlockoutCatalog.Last(palette);
            button.text = last != null ? $"{title}: {last.Name}" : title;
            button.icon = last?.Thumbnail as Texture2D;
        }

        private void OpenFlyout(VisualElement button, BlockoutPalette palette, bool vertical)
        {
            var window = containerWindow;
            Rect bounds = button.worldBound;
            Vector2 origin = window != null ? window.position.position : Vector2.zero;
            Rect screen = new Rect(origin + bounds.position, bounds.size);
            // Perpendicular to the toolbar: below a horizontal one, beside a vertical one.
            Rect anchor = vertical ? new Rect(screen.xMax, screen.y, 1f, 0f) : screen;
            BlockoutFlyout.Show(anchor, palette, vertical);
        }
    }

    /// <summary>
    /// The list a main toolbar button opens on right click: a search field and the items as thumbnails with names.
    /// Picking one starts placing it.
    /// </summary>
    public class BlockoutFlyout : EditorWindow
    {
        /// <summary>Raised when an item is picked (the toolbar updates its buttons).</summary>
        public static event System.Action<BlockoutItem> Picked;

        private BlockoutPalette _palette;
        private bool _horizontal;
        private string _filter = "";
        private VisualElement _items;
        private readonly List<(BlockoutItem item, Image image)> _tiles = new();

        /// <summary>Opens the list next to <paramref name="anchor"/> (screen space).</summary>
        public static void Show(Rect anchor, BlockoutPalette palette, bool besideToolbar)
        {
            var flyout = CreateInstance<BlockoutFlyout>();
            flyout._palette = palette;
            flyout._horizontal = besideToolbar;
            var size = besideToolbar ? new Vector2(520f, 150f) : new Vector2(250f, 380f);
            flyout.ShowAsDropDown(anchor, size);
            flyout.Build();
        }

        private void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 4;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            var search = new ToolbarSearchField();
            search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(evt =>
            {
                _filter = evt.newValue ?? "";
                Populate();
            });
            header.Add(search);
            if (_palette == BlockoutPalette.Schematics)
            {
                var refresh = new Button(() =>
                {
                    BlockoutCatalog.RefreshSchematics();
                    Populate();
                }) { text = "↻", tooltip = "Look for Level Design prefabs and templates again" };
                header.Add(refresh);
            }
            root.Add(header);

            if (_palette == BlockoutPalette.Rulers)
            {
                var character = ActiveCharacter.Prefab;
                var note = new Label(character != null ? "Character: " + character.name : "No character picked (the toolbar's Character button)");
                note.style.unityFontStyleAndWeight = FontStyle.Italic;
                note.style.fontSize = 10;
                root.Add(note);
            }

            var scroll = new ScrollView(_horizontal ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _items = new VisualElement();
            _items.style.flexDirection = _horizontal ? FlexDirection.Row : FlexDirection.Column;
            scroll.Add(_items);
            root.Add(scroll);
            Populate();

            search.Focus();
            EditorApplication.update -= RefreshThumbnails;
            EditorApplication.update += RefreshThumbnails;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RefreshThumbnails;
        }

        private void Populate()
        {
            _items.Clear();
            _tiles.Clear();
            var items = BlockoutCatalog.Items(_palette);
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(_filter) && item.Name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                _items.Add(Tile(item));
            }
            if (_items.childCount == 0)
            {
                string empty = _palette == BlockoutPalette.Schematics
                    ? "No Level Design Schematics. Tag a prefab's Schematic Object with 'Level Design'."
                    : "Nothing matches.";
                var label = new Label(empty) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
                _items.Add(label);
            }
        }

        private VisualElement Tile(BlockoutItem item)
        {
            var tile = new VisualElement { tooltip = item.Tooltip };
            tile.style.flexDirection = _horizontal ? FlexDirection.Column : FlexDirection.Row;
            tile.style.alignItems = Align.Center;
            tile.style.paddingLeft = tile.style.paddingRight = tile.style.paddingTop = tile.style.paddingBottom = 3;
            tile.style.marginRight = _horizontal ? 4 : 0;
            tile.style.marginBottom = _horizontal ? 0 : 2;
            tile.style.borderTopLeftRadius = tile.style.borderTopRightRadius = tile.style.borderBottomLeftRadius = tile.style.borderBottomRightRadius = 4;
            if (_horizontal)
                tile.style.width = 84;

            var image = new Image { image = item.Thumbnail, scaleMode = ScaleMode.ScaleToFit };
            float size = _horizontal ? 64 : 40;
            image.style.width = size;
            image.style.height = size;
            image.style.flexShrink = 0;
            tile.Add(image);

            var label = new Label(item.Name);
            label.style.marginLeft = _horizontal ? 0 : 6;
            label.style.unityTextAlign = _horizontal ? TextAnchor.UpperCenter : TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.Normal;
            tile.Add(label);

            var hover = new Color(1f, 1f, 1f, 0.08f);
            tile.RegisterCallback<MouseEnterEvent>(_ => tile.style.backgroundColor = hover);
            tile.RegisterCallback<MouseLeaveEvent>(_ => tile.style.backgroundColor = Color.clear);
            tile.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                BlockoutPlaceTool.Activate(item);
                Picked?.Invoke(item);
                Close();
            });

            _tiles.Add((item, image));
            return tile;
        }

        // Prefab previews arrive over a few frames.
        private void RefreshThumbnails()
        {
            if (_palette != BlockoutPalette.Schematics)
                return;
            bool loading = AssetPreview.IsLoadingAssetPreviews();
            foreach (var (item, image) in _tiles)
            {
                var thumbnail = item.Thumbnail;
                if (image.image != thumbnail)
                    image.image = thumbnail;
            }
            if (!loading)
                EditorApplication.update -= RefreshThumbnails;
        }
    }
}
