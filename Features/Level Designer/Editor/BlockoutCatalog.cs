using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Remedy.Framework;
using UnityEditor;
using UnityEngine;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>The blockout toolbar's main buttons.</summary>
    public enum BlockoutPalette
    {
        Primitives,
        Rulers,
        Schematics,
    }

    /// <summary>One thing the blockout toolbar can place: a primitive shape, a ruler, or a Level Design Schematic.</summary>
    public sealed class BlockoutItem
    {
        public string Id;
        public string Name;
        public string Tooltip;
        public BlockoutPalette Palette;

        // Primitives
        public BlockoutShapeKind ShapeKind;
        public Vector3 DefaultSize = Vector3.one;
        public BlockoutCategory Category;

        // Rulers
        public BlockoutRulerKind RulerKind;
        public float Seconds = 1f;

        // Schematics (prefabs tagged Level Design, or Schematic Templates marked for level design)
        public GameObject Prefab;
        public Texture TemplateIcon;

        /// <summary>A picture for the flyout (generated, or the prefab's preview).</summary>
        public Texture Thumbnail => BlockoutCatalog.ThumbnailOf(this);
    }

    /// <summary>
    /// What the blockout toolbar offers, per main button, and their thumbnails. Primitives and rulers are built in;
    /// Schematics are every prefab tagged <see cref="IntTags.LevelDesign"/> plus any Schematic Template marked as a
    /// level design template (found by name, so this works whether or not the templates feature is installed).
    /// </summary>
    [InitializeOnLoad]
    public static class BlockoutCatalog
    {
        private static readonly List<BlockoutItem> _primitives = new();
        private static readonly List<BlockoutItem> _rulers = new();
        private static readonly List<BlockoutItem> _schematics = new();
        private static bool _schematicsDirty = true;
        private static readonly Dictionary<string, Texture2D> _thumbnails = new();

        static BlockoutCatalog()
        {
            AddPrimitive("box", "Box", BlockoutShapeKind.Box, new Vector3(2, 1, 2), BlockoutCategory.Floor);
            AddPrimitive("floor", "Floor", BlockoutShapeKind.Box, new Vector3(8, 0.5f, 8), BlockoutCategory.Floor);
            AddPrimitive("wall", "Wall", BlockoutShapeKind.Box, new Vector3(4, 3, 0.5f), BlockoutCategory.Wall);
            AddPrimitive("platform", "Platform", BlockoutShapeKind.Box, new Vector3(2, 0.5f, 2), BlockoutCategory.Platform);
            AddPrimitive("ramp", "Ramp", BlockoutShapeKind.Ramp, new Vector3(2, 1, 4), BlockoutCategory.Floor);
            AddPrimitive("stairs", "Stairs", BlockoutShapeKind.Stairs, new Vector3(2, 2, 4), BlockoutCategory.Floor);
            AddPrimitive("cylinder", "Pillar", BlockoutShapeKind.Cylinder, new Vector3(1, 3, 1), BlockoutCategory.Wall);
            AddPrimitive("sphere", "Sphere", BlockoutShapeKind.Sphere, new Vector3(2, 2, 2), BlockoutCategory.Decoration);
            AddPrimitive("wedge", "Wedge", BlockoutShapeKind.Wedge, new Vector3(4, 2, 4), BlockoutCategory.Wall);
            AddPrimitive("arch", "Arch", BlockoutShapeKind.Arch, new Vector3(4, 4, 1), BlockoutCategory.Wall);
            AddPrimitive("quarterpipe", "Quarter Pipe", BlockoutShapeKind.QuarterPipe, new Vector3(4, 3, 3), BlockoutCategory.Floor);
            AddPrimitive("loop", "Loop", BlockoutShapeKind.Loop, new Vector3(4, 8, 8), BlockoutCategory.Floor);
            AddPrimitive("hazard", "Hazard", BlockoutShapeKind.Box, new Vector3(2, 0.25f, 2), BlockoutCategory.Hazard);
            AddPrimitive("water", "Water", BlockoutShapeKind.Box, new Vector3(6, 0.5f, 6), BlockoutCategory.Water);

            AddRuler("ruler.distance", "Distance", BlockoutRulerKind.Distance, 0f, "Drag from one point to another: total, horizontal and vertical distance. Hold Shift to measure straight out of the surface (heights, clearances).");
            AddRuler("ruler.jump", "Jump Arc", BlockoutRulerKind.JumpArc, 0f, "Click a take-off point and drag the direction: the active character's running jump.");
            AddRuler("ruler.run1", "Run 1 s", BlockoutRulerKind.RunDistance, 1f, "How far the active character runs in 1 second.");
            AddRuler("ruler.run3", "Run 3 s", BlockoutRulerKind.RunDistance, 3f, "How far the active character runs in 3 seconds.");

            EditorApplication.projectChanged += () => _schematicsDirty = true;
        }

        private static void AddPrimitive(string id, string name, BlockoutShapeKind kind, Vector3 size, BlockoutCategory category)
        {
            _primitives.Add(new BlockoutItem
            {
                Id = "primitive." + id, Name = name, Palette = BlockoutPalette.Primitives, ShapeKind = kind, DefaultSize = size, Category = category,
                Tooltip = $"{name} ({BlockoutShape.FormatMeters(size.x)} x {BlockoutShape.FormatMeters(size.y)} x {BlockoutShape.FormatMeters(size.z)} m). Click to place, or drag out its footprint.",
            });
        }

        private static void AddRuler(string id, string name, BlockoutRulerKind kind, float seconds, string tooltip)
        {
            _rulers.Add(new BlockoutItem { Id = id, Name = name, Palette = BlockoutPalette.Rulers, RulerKind = kind, Seconds = seconds, Tooltip = tooltip });
        }

        /// <summary>The items behind a main button.</summary>
        public static IReadOnlyList<BlockoutItem> Items(BlockoutPalette palette)
        {
            switch (palette)
            {
                case BlockoutPalette.Primitives: return _primitives;
                case BlockoutPalette.Rulers: return _rulers;
                default:
                    if (_schematicsDirty)
                        RefreshSchematics();
                    return _schematics;
            }
        }

        /// <summary>The item a main button uses on a left click: the last one picked from it.</summary>
        public static BlockoutItem Last(BlockoutPalette palette)
        {
            string id = EditorPrefs.GetString("Remedy.LevelDesigner.Last." + palette, "");
            var items = Items(palette);
            foreach (var item in items)
                if (item.Id == id)
                    return item;
            return items.Count > 0 ? items[0] : null;
        }

        /// <summary>Remembers the item picked from a main button.</summary>
        public static void SetLast(BlockoutItem item)
        {
            if (item != null)
                EditorPrefs.SetString("Remedy.LevelDesigner.Last." + item.Palette, item.Id);
        }

        // ---------------------------------------------------------------- schematics

        /// <summary>Looks for Level Design prefabs and templates again.</summary>
        public static void RefreshSchematics()
        {
            _schematicsDirty = false;
            _schematics.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !prefab.HasIntTag(IntTags.LevelDesign))
                    continue;
                _schematics.Add(new BlockoutItem
                {
                    Id = "prefab." + guid, Name = prefab.name, Palette = BlockoutPalette.Schematics, Prefab = prefab,
                    Tooltip = path,
                });
            }

            AddTemplates();
            _schematics.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        // Schematic Templates are another feature; they're found by type name and read by reflection so this
        // assembly doesn't depend on it. An entry counts when it has a true "IsLevelDesign" field or property
        // and a GameObject (prefab) to place.
        private static void AddTemplates()
        {
            Type templatesType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in SafeTypes(assembly))
                {
                    if (type.Name == "SchematicTemplatesData" && typeof(ScriptableObject).IsAssignableFrom(type))
                    {
                        templatesType = type;
                        break;
                    }
                }
                if (templatesType != null)
                    break;
            }
            if (templatesType == null)
                return;

            foreach (var guid in AssetDatabase.FindAssets("t:" + templatesType.Name))
            {
                var asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), templatesType);
                if (asset == null)
                    continue;
                foreach (var entry in EnumerateEntries(asset))
                {
                    if (!(ReadMember(entry, "IsLevelDesign") is bool isLevelDesign) || !isLevelDesign)
                        continue;
                    var prefab = FirstMemberOfType<GameObject>(entry);
                    if (prefab == null)
                        continue;
                    string name = ReadMember(entry, "Name") as string ?? ReadMember(entry, "DisplayName") as string ?? prefab.name;
                    Texture icon = FirstMemberOfType<Texture2D>(entry);
                    if (icon == null && FirstMemberOfType<Sprite>(entry) is Sprite sprite)
                        icon = sprite.texture;
                    string id = "template." + guid + "." + name;
                    if (_schematics.Exists(i => i.Id == id))
                        continue;
                    _schematics.Add(new BlockoutItem
                    {
                        Id = id, Name = name, Palette = BlockoutPalette.Schematics, Prefab = prefab, TemplateIcon = icon,
                        Tooltip = "Schematic Template: " + name,
                    });
                }
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return Array.FindAll(e.Types, t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static IEnumerable<object> EnumerateEntries(object asset)
        {
            foreach (var field in asset.GetType().GetFields(Members))
            {
                if (field.FieldType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(field.FieldType))
                    continue;
                if (field.GetValue(asset) is IEnumerable list)
                    foreach (var entry in list)
                        if (entry != null)
                            yield return entry;
            }
        }

        private static object ReadMember(object target, string name)
        {
            var type = target.GetType();
            var field = type.GetField(name, Members);
            if (field != null)
                return field.GetValue(target);
            var property = type.GetProperty(name, Members);
            return property != null && property.GetIndexParameters().Length == 0 ? property.GetValue(target) : null;
        }

        private static T FirstMemberOfType<T>(object target) where T : UnityEngine.Object
        {
            foreach (var field in target.GetType().GetFields(Members))
                if (typeof(T).IsAssignableFrom(field.FieldType) && field.GetValue(target) is T value && value != null)
                    return value;
            return null;
        }

        // ---------------------------------------------------------------- thumbnails

        /// <summary>The thumbnail for an item (generated once and cached; prefab previews come from Unity).</summary>
        public static Texture ThumbnailOf(BlockoutItem item)
        {
            if (item == null)
                return null;
            switch (item.Palette)
            {
                case BlockoutPalette.Schematics:
                    if (item.TemplateIcon != null)
                        return item.TemplateIcon;
                    var preview = AssetPreview.GetAssetPreview(item.Prefab);
                    return preview != null ? preview : AssetPreview.GetMiniThumbnail(item.Prefab);
                case BlockoutPalette.Rulers:
                    return Cached(item.Id, () => RulerThumbnail(item));
                default:
                    return Cached(item.Id, () => ShapeThumbnail(item));
            }
        }

        private static Texture2D Cached(string id, Func<Texture2D> make)
        {
            if (_thumbnails.TryGetValue(id, out var texture) && texture != null)
                return texture;
            texture = make();
            if (texture != null)
            {
                texture.hideFlags = HideFlags.HideAndDontSave;
                _thumbnails[id] = texture;
            }
            return texture;
        }

        // Renders the shape's mesh with the blockout material (labels off, so the shape reads at thumbnail size).
        private static Texture2D ShapeThumbnail(BlockoutItem item)
        {
            var material = LevelDesignerContext.BlockoutMaterial;
            if (material == null)
                return null;

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            BlockoutMeshBuilder.Build(mesh, item.ShapeKind, item.DefaultSize);
            var preview = new PreviewRenderUtility();
            try
            {
                var bounds = mesh.bounds;
                float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
                var camera = preview.camera;
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = radius * 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.16f, 0.17f, 0f);
                var rotation = Quaternion.Euler(28f, -38f, 0f);
                camera.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * (radius / Mathf.Sin(15f * Mathf.Deg2Rad)), rotation);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45f, 30f, 0f);
                preview.ambientColor = new Color(0.35f, 0.35f, 0.38f);

                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", BlockoutShape.ColorOf(item.Category));
                block.SetFloat("_CellLabels", 0f);

                preview.BeginStaticPreview(new Rect(0, 0, 128, 128));
                preview.DrawMesh(mesh, Matrix4x4.identity, material, 0, block);
                preview.Render(true);
                return preview.EndStaticPreview();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Level Designer: couldn't render a thumbnail for " + item.Name + ": " + e.Message);
                return null;
            }
            finally
            {
                preview.Cleanup();
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        // A small drawing of the ruler kind (lines on a transparent texture).
        private static Texture2D RulerThumbnail(BlockoutItem item)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            var color = new Color32(255, 214, 64, 255);
            void Plot(float x, float y)
            {
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int px = Mathf.RoundToInt(x) + dx, py = Mathf.RoundToInt(y) + dy;
                    if (px >= 0 && py >= 0 && px < size && py < size)
                        pixels[py * size + px] = color;
                }
            }
            void Line(Vector2 a, Vector2 b)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b)) + 1;
                for (int i = 0; i <= steps; i++)
                {
                    var p = Vector2.Lerp(a, b, (float)i / steps);
                    Plot(p.x, p.y);
                }
            }

            switch (item.RulerKind)
            {
                case BlockoutRulerKind.Distance:
                    Line(new Vector2(8, 32), new Vector2(56, 32));
                    Line(new Vector2(8, 24), new Vector2(8, 40));
                    Line(new Vector2(56, 24), new Vector2(56, 40));
                    for (int x = 16; x < 56; x += 8) Line(new Vector2(x, 32), new Vector2(x, 36));
                    break;
                case BlockoutRulerKind.JumpArc:
                {
                    color = new Color32(90, 220, 110, 255);
                    Vector2 previous = new Vector2(6, 10);
                    for (int i = 1; i <= 24; i++)
                    {
                        float t = i / 24f;
                        var p = new Vector2(6 + 52 * t, 10 + 44 * 4f * t * (1f - t));
                        Line(previous, p);
                        previous = p;
                    }
                    break;
                }
                default:
                    color = new Color32(110, 190, 255, 255);
                    Line(new Vector2(6, 24), new Vector2(58, 24));
                    for (int x = 6; x <= 58; x += 13) Line(new Vector2(x, 18), new Vector2(x, 30));
                    Line(new Vector2(46, 40), new Vector2(58, 34));
                    Line(new Vector2(46, 28), new Vector2(58, 34));
                    break;
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
