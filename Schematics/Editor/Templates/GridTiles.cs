using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// The "+" tile (New Schematic Object, Import Audio...): a glossy blue square with a plus and a label. Shared by the
    /// Project Manager's grids so every page's action tile looks the same.
    /// </summary>
    public sealed class ActionTile : VisualElement
    {
        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.07f);
        private const float LabelHeight = 30f;
        private readonly Image _image;
        private readonly Label _label;
        private readonly Label _plus;

        public ActionTile(string label, string tooltip, Action click)
        {
            AddToClassList("schematic-grid__tile");
            AddToClassList("schematic-grid__tile--create");
            this.tooltip = tooltip;
            TileStyle.Frame(this);

            _image = new Image
            {
                image = Resources.Load<Texture2D>("Textures/button-gradient"),
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
            };
            _image.AddToClassList("schematic-grid__thumbnail");
            TileStyle.Radius(_image.style, 3);
            _image.style.backgroundColor = new Color(0.18f, 0.36f, 0.55f, 0.55f);
            _image.style.opacity = 0.7f;
            _image.style.borderTopWidth = _image.style.borderBottomWidth = _image.style.borderLeftWidth = _image.style.borderRightWidth = 1;
            var dashed = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            _image.style.borderTopColor = _image.style.borderBottomColor = _image.style.borderLeftColor = _image.style.borderRightColor = dashed;
            Add(_image);

            _plus = new Label("+") { pickingMode = PickingMode.Ignore };
            _plus.style.position = Position.Absolute;
            _plus.style.left = 0;
            _plus.style.right = 0;
            _plus.style.top = 0;
            _plus.style.unityTextAlign = TextAnchor.MiddleCenter;
            _plus.style.opacity = 0.92f;
            _plus.style.color = Color.white;
            _plus.style.unityFontStyleAndWeight = FontStyle.Bold;
            _image.Add(_plus);

            _label = TileStyle.NameLabel(label);
            Add(_label);

            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                evt.StopPropagation();
                click?.Invoke();
            });
            RegisterCallback<PointerEnterEvent>(_ => { style.backgroundColor = HoverColor; _image.style.opacity = 1f; });
            RegisterCallback<PointerLeaveEvent>(_ => { style.backgroundColor = Color.clear; _image.style.opacity = 0.7f; });
        }

        public string Label
        {
            get => _label.text;
            set => _label.text = value;
        }

        public void Resize(float size)
        {
            style.width = size + 6;
            _image.style.width = size;
            _image.style.height = size;
            _plus.style.height = size;
            _plus.style.fontSize = Mathf.Round(size * 0.4f);
            TileStyle.SizeLabel(_label, size, LabelHeight);
        }
    }

    /// <summary>
    /// A folder as a tile: double-click opens it, double-click its name renames it, assets and folders dropped on it
    /// move into it, and it drags onto other folders. Shared by the Project Manager's pages.
    /// </summary>
    public sealed class FolderTile : VisualElement
    {
        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.07f);
        private const float LabelHeight = 30f;

        public readonly string Folder;
        private readonly Image _image;
        private readonly Label _label;
        private Vector2 _pressPosition;
        private bool _pressed, _renaming;

        /// <summary>Double-clicked (open the folder).</summary>
        public Action<string> Opened;
        /// <summary>Fills the right-click menu.</summary>
        public Action<DropdownMenu, string> Menu;
        /// <summary>Things were dropped on it and moved in (their new paths).</summary>
        public Action<List<string>> Moved;
        /// <summary>Renaming started (true) or finished (false); a grid holds its rebuilds meanwhile.</summary>
        public Action<bool> RenamingChanged;
        /// <summary>It was renamed (or the rename was cancelled): time to rebuild.</summary>
        public Action Renamed;

        /// <param name="locked">Not renamed or dragged (a page's fixed folders, like Audio's Music and SFX).</param>
        public FolderTile(string folder, bool locked = false)
        {
            Folder = folder;
            AddToClassList("schematic-grid__tile");
            AddToClassList("schematic-grid__tile--folder");
            TileStyle.Frame(this);
            tooltip = folder;

            _image = new Image
            {
                image = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_Folder Icon" : "Folder Icon")?.image,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            Add(_image);

            _label = TileStyle.NameLabel(Path.GetFileName(folder));
            Add(_label);

            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                _pressed = !locked;
                _pressPosition = evt.position;
                if (evt.clickCount == 2)
                {
                    _pressed = false;
                    if (!locked && _label.ContainsPoint(_label.WorldToLocal(evt.position)))
                        EditorApplication.delayCall += BeginRename;
                    else
                        Opened?.Invoke(Folder);
                }
                evt.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_pressed || (evt.pressedButtons & 1) == 0 || ((Vector2)evt.position - _pressPosition).sqrMagnitude < 36f)
                    return;
                _pressed = false;
                ProjectAssetOps.StartDrag(Path.GetFileName(Folder), Folder);
                evt.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(_ => _pressed = false);
            RegisterCallback<PointerEnterEvent>(_ => style.backgroundColor = HoverColor);
            RegisterCallback<PointerLeaveEvent>(_ => { _pressed = false; style.backgroundColor = Color.clear; });
            this.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                Menu?.Invoke(evt.menu, Folder);
                evt.StopPropagation();
            }));
            ProjectAssetOps.MakeDropTarget(this, () => Folder, moved => Moved?.Invoke(moved), "schematic-grid__tile--drop");
        }

        public void BeginRename()
        {
            if (_renaming || panel == null) return;
            _renaming = true;
            RenamingChanged?.Invoke(true);
            var field = InlineRename.Begin(_label, Path.GetFileName(Folder),
                value => InlineRename.RenameAsset(Folder, value),
                () =>
                {
                    _renaming = false;
                    RenamingChanged?.Invoke(false);
                    Renamed?.Invoke();
                });
            if (field == null)
            {
                _renaming = false;
                RenamingChanged?.Invoke(false);
                return;
            }
            field.style.width = _label.style.width;
        }

        public void Resize(float size)
        {
            style.width = size + 6;
            _image.style.width = size * 0.8f;
            _image.style.height = size * 0.8f;
            _image.style.marginTop = size * 0.1f;
            _image.style.marginBottom = size * 0.1f;
            TileStyle.SizeLabel(_label, size, LabelHeight);
        }
    }

    /// <summary>The look tiles share (frame, padding, name label).</summary>
    internal static class TileStyle
    {
        public const float Margin = 4f;

        public static void Frame(VisualElement tile)
        {
            tile.style.marginLeft = tile.style.marginRight = tile.style.marginTop = tile.style.marginBottom = Margin;
            tile.style.paddingLeft = tile.style.paddingRight = 3;
            tile.style.paddingTop = 3;
            tile.style.paddingBottom = 2;
            tile.style.alignItems = Align.Center;
            Radius(tile.style, 4);
        }

        public static Label NameLabel(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("schematic-grid__label");
            label.style.unityTextAlign = TextAnchor.UpperCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.overflow = Overflow.Hidden;
            label.style.fontSize = 11;
            label.style.marginTop = 2;
            label.style.paddingLeft = 2;
            label.style.paddingRight = 2;
            Radius(label.style, 3);
            return label;
        }

        public static void SizeLabel(Label label, float size, float height)
        {
            label.style.width = size + 4;
            label.style.height = height;
            label.style.fontSize = size < 72 ? 10 : 11;
        }

        public static void Radius(IStyle style, float radius)
        {
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = radius;
        }
    }
}
