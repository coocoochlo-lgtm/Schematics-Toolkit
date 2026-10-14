using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Painting blockout shapes and rulers with a colour after they're placed (undoable). A painted shape becomes
    /// <see cref="BlockoutCategory.Custom"/> with that colour (the shader reads it through the shape's property block,
    /// so nothing else changes); a painted ruler gets that <see cref="BlockoutRuler.Color"/>.
    /// </summary>
    public static class BlockoutPainter
    {
        /// <summary>Paints one shape.</summary>
        public static void Paint(BlockoutShape shape, Color color)
        {
            if (shape == null)
                return;
            Undo.RecordObject(shape, "Paint Blockout");
            shape.Category = BlockoutCategory.Custom;
            shape.CustomColor = color;
            shape.ApplyRendering();
            EditorUtility.SetDirty(shape);
        }

        /// <summary>Paints one ruler.</summary>
        public static void Paint(BlockoutRuler ruler, Color color)
        {
            if (ruler == null)
                return;
            Undo.RecordObject(ruler, "Paint Ruler");
            ruler.Color = color;
            EditorUtility.SetDirty(ruler);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Paints every shape and ruler in the selection (children of selected objects included), as one undo step.
        /// Returns how many were painted.
        /// </summary>
        public static int PaintSelection(Color color)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paint Selected");
            var shapes = new HashSet<BlockoutShape>();
            var rulers = new HashSet<BlockoutRuler>();
            foreach (var go in Selection.gameObjects)
            {
                if (go == null)
                    continue;
                shapes.UnionWith(go.GetComponentsInChildren<BlockoutShape>(true));
                rulers.UnionWith(go.GetComponentsInChildren<BlockoutRuler>(true));
            }
            foreach (var shape in shapes)
                Paint(shape, color);
            foreach (var ruler in rulers)
                Paint(ruler, color);
            Undo.CollapseUndoOperations(group);
            return shapes.Count + rulers.Count;
        }

        /// <summary>Paint Selected with the toolbar's colour, with a notification when there's nothing to do.</summary>
        public static void PaintSelectionWithCurrent()
        {
            if (!LevelDesignerSettings.TryGetCurrentColor(out var color))
            {
                Notify("Pick a colour on the blockout toolbar first");
                return;
            }
            int painted = PaintSelection(color);
            Notify(painted == 0 ? "Nothing selected to paint (blockout shapes or rulers)" : $"Painted {painted} object(s)");
        }

        internal static void Notify(string message)
        {
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent(message), 1.5f);
        }

        private static readonly Dictionary<Color, Texture2D> _swatches = new();

        /// <summary>A small square of a colour for buttons and menus (cached).</summary>
        public static Texture2D Swatch(Color color, bool auto = false)
        {
            var key = auto ? new Color(-1f, -1f, -1f, -1f) : color;
            if (_swatches.TryGetValue(key, out var texture) && texture != null)
                return texture;
            const int size = 16;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            var pixels = new Color[size * size];
            var border = new Color(0f, 0f, 0f, 0.8f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                Color fill;
                if (auto)
                    // "Auto": a diagonal split of two category colours.
                    fill = x > y ? BlockoutShape.ColorOf(BlockoutCategory.Platform) : BlockoutShape.ColorOf(BlockoutCategory.Floor);
                else
                    fill = new Color(color.r, color.g, color.b, 1f);
                pixels[y * size + x] = edge ? border : fill;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            _swatches[key] = texture;
            return texture;
        }
    }

    /// <summary>
    /// The paint bucket: click a blockout shape or a ruler in the Scene view to give it the colour picked on the
    /// blockout toolbar. Undoable; Esc or another tool ends it. For many objects at once, select them and use
    /// <b>Paint Selected</b> in the colour menu.
    /// </summary>
    [EditorTool("Blockout Paint")]
    public class BlockoutPaintTool : EditorTool
    {
        private static GUIContent _icon;

        public override GUIContent toolbarIcon => _icon ??= new GUIContent("Paint", "Blockout: click a shape or ruler to paint it with the toolbar's colour");

        public override bool IsAvailable() => LevelDesignerContext.Mode == LevelDesignerMode.Blockout;

        /// <summary>True while the paint bucket is the active Scene view tool.</summary>
        public static bool IsActive => ToolManager.activeToolType == typeof(BlockoutPaintTool);

        /// <summary>Switches to the paint bucket.</summary>
        public static void Activate()
        {
            ToolManager.SetActiveTool<BlockoutPaintTool>();
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || LevelDesignerContext.Mode != LevelDesignerMode.Blockout)
                return;
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(control);
            if (BlockoutRulerDrawer.IsMeasuring)
                return;

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                ToolManager.RestorePreviousPersistentTool();
                e.Use();
                return;
            }

            bool hasColor = LevelDesignerSettings.TryGetCurrentColor(out var color);
            var ruler = BlockoutRulerDrawer.PickRuler(e.mousePosition);
            BlockoutShape shape = null;
            if (ruler == null && BlockoutShape.RaycastAll(HandleUtility.GUIPointToWorldRay(e.mousePosition), 10000f, out var hit))
                shape = hit.Shape;

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    if (!hasColor)
                        BlockoutPainter.Notify("Pick a colour on the blockout toolbar first");
                    else if (ruler != null)
                        BlockoutPainter.Paint(ruler, color);
                    else if (shape != null)
                        BlockoutPainter.Paint(shape, color);
                    e.Use();
                    break;

                case EventType.MouseMove:
                    window.Repaint();
                    break;

                case EventType.Repaint:
                {
                    var labelColor = hasColor ? color : Color.white;
                    string text = !hasColor ? "Paint: pick a colour on the blockout toolbar" : ruler != null ? "Paint ruler" : shape != null ? "Paint " + shape.name : "Paint: click a shape or ruler";
                    var at = HandleUtility.GUIPointToWorldRay(e.mousePosition + new Vector2(0f, 24f)).GetPoint(3f);
                    LevelDesignerSceneGUI.Label(at, text, labelColor);
                    if (shape != null && hasColor)
                    {
                        using (new Handles.DrawingScope(color, shape.transform.localToWorldMatrix))
                        {
                            var bounds = shape.Mesh != null ? shape.Mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                            Handles.DrawWireCube(bounds.center, bounds.size);
                        }
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The colour menu of the blockout toolbar's swatch: "Auto" (category colours), the project's palette as
    /// swatches, any colour, adding it to the palette, Paint Selected and the palette editor.
    /// </summary>
    public class BlockoutColorPopup : EditorWindow
    {
        /// <summary>Opens the menu under <paramref name="anchor"/> (screen space).</summary>
        public static void Show(Rect anchor)
        {
            var popup = CreateInstance<BlockoutColorPopup>();
            popup.ShowAsDropDown(anchor, new Vector2(236f, 250f));
            popup.Build();
        }

        private void Build()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;

            var auto = new Button(() =>
            {
                LevelDesignerSettings.HasCurrentColor = false;
                Close();
            })
            {
                text = (LevelDesignerSettings.HasCurrentColor ? "" : "✓ ") + "Auto (category colours)",
                tooltip = "New shapes use their category's colour, rulers their usual one",
            };
            root.Add(auto);

            root.Add(new Label("Palette") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            var project = LevelDesignerContext.Project;
            foreach (var entry in project.Palette)
            {
                if (entry == null)
                    continue;
                var swatch = new VisualElement { tooltip = entry.Name };
                swatch.style.width = swatch.style.height = 24;
                swatch.style.marginRight = swatch.style.marginBottom = 3;
                swatch.style.backgroundColor = new Color(entry.Color.r, entry.Color.g, entry.Color.b, 1f);
                swatch.style.borderTopLeftRadius = swatch.style.borderTopRightRadius = swatch.style.borderBottomLeftRadius = swatch.style.borderBottomRightRadius = 3;
                bool current = LevelDesignerSettings.HasCurrentColor && LevelDesignerSettings.CurrentColor == entry.Color;
                swatch.style.borderLeftWidth = swatch.style.borderRightWidth = swatch.style.borderTopWidth = swatch.style.borderBottomWidth = current ? 2 : 1;
                swatch.style.borderLeftColor = swatch.style.borderRightColor = swatch.style.borderTopColor = swatch.style.borderBottomColor = current ? Color.white : new Color(0f, 0f, 0f, 0.6f);
                var color = entry.Color;
                swatch.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0)
                        return;
                    LevelDesignerSettings.CurrentColor = color;
                    Close();
                });
                grid.Add(swatch);
            }
            root.Add(grid);

            var custom = new ColorField("Colour") { value = LevelDesignerSettings.CurrentColor, showAlpha = false, tooltip = "Any colour" };
            custom.RegisterValueChangedCallback(evt => LevelDesignerSettings.CurrentColor = evt.newValue);
            root.Add(custom);

            var nameField = new TextField { tooltip = "Name for the palette entry" };
            nameField.textEdition.placeholder = "Name (e.g. Climbable)";
            var addRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            nameField.style.flexGrow = 1;
            addRow.Add(nameField);
            addRow.Add(new Button(() =>
            {
                LevelDesignerContext.RecordProject("Add Palette Colour");
                project.Palette.Add(new PaletteColor(string.IsNullOrWhiteSpace(nameField.value) ? "Colour " + (project.Palette.Count + 1) : nameField.value.Trim(), custom.value));
                AssetDatabase.SaveAssets();
                LevelDesignerSettings.CurrentColor = custom.value;
                Build();
            }) { text = "+ Palette", tooltip = "Add this colour to the project's palette (shared with the team)" });
            root.Add(addRow);

            var paint = new Button(() =>
            {
                BlockoutPainter.PaintSelectionWithCurrent();
                Close();
            }) { text = "Paint Selected", tooltip = "Give every selected blockout shape and ruler this colour" };
            paint.style.marginTop = 6;
            root.Add(paint);
            root.Add(new Button(() =>
            {
                BlockoutPaintTool.Activate();
                Close();
            }) { text = "Paint Bucket Tool", tooltip = "Click shapes and rulers to paint them" });
            root.Add(new Button(() =>
            {
                Close();
                LevelDesignerWindow.OpenPalette();
            }) { text = "Edit Palette…" });
        }
    }
}
