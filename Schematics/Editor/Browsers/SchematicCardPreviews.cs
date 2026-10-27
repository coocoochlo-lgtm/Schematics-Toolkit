using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>Small graph-kind previews and lower-right graph badges for the Schematic browser tiles.</summary>
    internal static class SchematicCardPreviews
    {
        private const int Size = 128;
        private const int OverviewSize = 256;
        private sealed class CachedPreview
        {
            public Hash128 DependencyHash;
            public Texture Texture;
        }

        private static readonly Dictionary<string, CachedPreview> Previews = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> Overviews = new(StringComparer.Ordinal);

        public static Texture Thumbnail(SchematicTemplate entry)
        {
            var graph = GraphOf(entry);
            var kind = graph != null ? SchematicTypes.Of(graph.GetType()) : null;
            if (kind?.Name != "UI" && kind?.Name != "Audio")
                return null;

            var assetPath = AssetDatabase.GetAssetPath(graph);
            var previewAsset = kind.Name == "UI" ? LayoutOf(graph) : null;
            var dependencyPath = previewAsset != null ? AssetDatabase.GetAssetPath(previewAsset) : assetPath;
            var cacheKey = kind.Name + ":" + (string.IsNullOrEmpty(dependencyPath) ? assetPath : dependencyPath);
            var hash = !string.IsNullOrEmpty(dependencyPath) ? AssetDatabase.GetAssetDependencyHash(dependencyPath) : default;
            if (Previews.TryGetValue(cacheKey, out var cached) && cached.DependencyHash == hash && cached.Texture != null)
                return cached.Texture;

            var texture = kind.Name == "UI"
                ? RenderUILayoutPreview(previewAsset as VisualTreeAsset)
                : DrawAudioWaveform(graph as Remedy.Audio.SoundCueGraph);
            if (cached?.Texture != null)
                UnityEngine.Object.DestroyImmediate(cached.Texture);
            Previews[cacheKey] = new CachedPreview { DependencyHash = hash, Texture = texture };
            return texture;
        }

        private static Texture RenderUILayoutPreview(VisualTreeAsset layout)
        {
            if (layout != null)
            {
                // Unity's UI Toolkit asset preview is rendered from the UXML document, including its USS sheets.
                var preview = AssetPreview.GetAssetPreview(layout);
                if (preview != null) return preview;
            }
            return DrawUIUnavailablePreview();
        }

        private static Texture2D DrawUIUnavailablePreview()
        {
            var pixels = NewCanvas(EditorGUIUtility.isProSkin ? new Color32(29, 32, 37, 255) : new Color32(238, 240, 243, 255));
            var frame = EditorGUIUtility.isProSkin ? new Color32(100, 115, 132, 255) : new Color32(100, 112, 126, 255);
            Outline(pixels, 12, 12, 104, 104, frame);
            return MakeTexture("UI Toolkit Preview", pixels);
        }

        public static VisualElement Badge(SchematicTemplate entry, IReadOnlyList<AssetTag> tags)
        {
            var graph = GraphOf(entry);
            var kind = graph != null ? SchematicTypes.Of(graph.GetType()) : null;
            bool showGraphIcon = graph != null && (entry.Prefab != null || kind?.Name == "Audio");
            var graphIcon = showGraphIcon ? SchematicTabIcons.ForGraph(graph.GetType()) : null;
            if ((tags == null || tags.Count == 0) && graphIcon == null)
                return null;

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("schematic-grid__thumbnail-badges");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.style.width = Length.Percent(100);

            if (tags != null && tags.Count > 0)
            {
                var tagDots = AssetTags.Dots(tags);
                tagDots.style.flexGrow = 1;
                tagDots.style.minWidth = 0;
                tagDots.style.alignSelf = Align.FlexEnd;
                row.Add(tagDots);
            }

            if (graphIcon != null)
            {
                var badge = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = kind.Name + " Schematic" };
                badge.style.width = badge.style.height = 22;
                badge.style.flexShrink = 0;
                badge.style.marginLeft = 3;
                badge.style.paddingLeft = badge.style.paddingRight = 2;
                badge.style.paddingTop = badge.style.paddingBottom = 2;
                badge.style.backgroundColor = EditorGUIUtility.isProSkin
                    ? new Color(0.08f, 0.09f, 0.11f, 0.92f)
                    : new Color(0.96f, 0.96f, 0.96f, 0.94f);
                badge.style.borderTopLeftRadius = badge.style.borderTopRightRadius = 3;
                badge.style.borderBottomLeftRadius = badge.style.borderBottomRightRadius = 3;
                var image = new Image { image = graphIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.width = image.style.height = 18;
                badge.Add(image);
                row.Add(badge);
            }
            return row;
        }

        public static Texture Overview(SchematicTemplate entry)
        {
            var graph = GraphOf(entry);
            if (graph == null) return null;
            string key = AssetDatabase.GetAssetPath(graph);
            if (string.IsNullOrEmpty(key)) key = graph.GetEntityId().ToString();
            key += ":overview";
            if (Overviews.TryGetValue(key, out var cached) && cached != null) return cached;
            var texture = SchematicMinimap.RenderOverview(graph, OverviewSize);
            Overviews[key] = texture;
            return texture;
        }

        public static float OverviewZoom(SchematicTemplate entry)
            => SchematicMinimap.RecommendedPreviewZoom(GraphOf(entry), OverviewSize);

        public static void DrawOverviewMarks(SchematicTemplate entry, Rect viewport, Vector2 zoomCenter, float zoom)
            => SchematicMinimap.DrawOverviewMarks(GraphOf(entry), viewport, zoomCenter, zoom, OverviewSize);

        public static SchematicTemplateGrid.HoverInfo? OverviewInfo(SchematicTemplate entry, Vector2 point)
        {
            var graph = GraphOf(entry);
            float scale = OverviewSize / (float)Size;
            var hit = SchematicMinimap.OverviewHitAt(graph, point * scale, OverviewSize);
            if (hit == null) return null;
            var bounds = hit.Value.Bounds;
            bounds.position /= scale;
            bounds.size /= scale;
            return new SchematicTemplateGrid.HoverInfo(hit.Value.Label, bounds);
        }

        private static SchematicGraph GraphOf(SchematicTemplate entry)
        {
            if (entry == null)
                return null;
            if (entry.Prefab != null)
                return SchematicObjectFactory.GraphOf(entry.Prefab);
            return AssetDatabase.LoadMainAssetAtPath(entry.Description ?? "") as SchematicGraph;
        }

        private static VisualTreeAsset LayoutOf(SchematicGraph graph)
        {
            if (graph == null)
                return null;
            var field = graph.GetType().GetField("Layout", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(graph) as VisualTreeAsset;
        }

        private sealed class PreviewElement
        {
            public int Depth;
            public string Name;
            public string TypeName;
        }

        private static Texture2D DrawUITreePreview(VisualTreeAsset layout)
        {
            var pixels = NewCanvas(new Color32(29, 32, 37, 255));
            Fill(pixels, 4, 4, 120, 120, new Color32(36, 40, 46, 255));
            Outline(pixels, 4, 4, 120, 120, new Color32(92, 100, 112, 255));
            Fill(pixels, 5, 109, 118, 14, new Color32(52, 58, 67, 255));
            for (int i = 0; i < 3; i++)
                Disk(pixels, 11 + i * 7, 116, 2, new Color32(150, 160, 173, 255));

            var elements = new List<PreviewElement>();
            if (layout != null)
            {
                try
                {
                    var root = layout.CloneTree();
                    CollectPreviewElements(root, 0, elements);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Couldn't make a UI Tree thumbnail for '{layout.name}': {exception.Message}");
                }
            }

            const int rows = 7;
            var count = Math.Min(rows, elements.Count);
            var lineColor = new Color32(105, 115, 128, 255);
            var labelColor = new Color32(193, 202, 213, 255);
            var accentColor = new Color32(126, 177, 219, 255);
            for (int i = 0; i < count; i++)
            {
                var element = elements[i];
                int y = 96 - i * 13;
                int depth = Mathf.Min(element.Depth, 4);
                int x = 13 + depth * 12;
                if (depth > 0)
                    Line(pixels, x - 6, y + 4, x - 2, y + 4, lineColor);
                DrawElementGlyph(pixels, element.TypeName, x, y, accentColor, labelColor);
                int nameWidth = Mathf.Clamp((element.Name?.Length ?? element.TypeName.Length) * 2 + 14, 23, 65);
                Fill(pixels, x + 11, y + 2, nameWidth, 3, labelColor);
                Fill(pixels, x + 11, y - 3, Math.Max(12, nameWidth * 2 / 3), 2, new Color32(112, 122, 135, 255));
            }

            if (elements.Count > rows)
                for (int i = 0; i < 3; i++)
                    Disk(pixels, 103 + i * 5, 8, 1, lineColor);
            if (elements.Count == 0)
            {
                Fill(pixels, 25, 61, 78, 4, labelColor);
                Fill(pixels, 36, 51, 56, 3, lineColor);
                Outline(pixels, 30, 43, 68, 30, accentColor);
            }
            return MakeTexture("UI Tree Preview", pixels);
        }

        private static void CollectPreviewElements(VisualElement parent, int depth, List<PreviewElement> results)
        {
            if (parent.visualTreeAssetSource != null)
                results.Add(new PreviewElement
                {
                    Depth = depth,
                    Name = parent.name,
                    TypeName = parent.GetType().Name,
                });
            for (int i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy.ElementAt(i);
                CollectPreviewElements(child, child.visualTreeAssetSource != null ? depth + 1 : depth, results);
            }
        }

        private static void DrawElementGlyph(Color32[] pixels, string typeName, int x, int y, Color32 accent, Color32 line)
        {
            if (typeName.Contains("Button"))
            {
                Fill(pixels, x, y, 8, 7, accent);
                Fill(pixels, x + 2, y + 3, 4, 1, new Color32(36, 40, 46, 255));
            }
            else if (typeName.Contains("Image"))
            {
                Outline(pixels, x, y, 8, 7, accent);
                Line(pixels, x + 1, y + 1, x + 4, y + 5, accent);
                Line(pixels, x + 4, y + 5, x + 7, y + 2, accent);
            }
            else if (typeName.Contains("Scroll") || typeName.Contains("ListView"))
            {
                Outline(pixels, x, y, 8, 7, accent);
                Fill(pixels, x + 1, y + 4, 5, 1, line);
                Fill(pixels, x + 1, y + 2, 4, 1, line);
                Fill(pixels, x + 6, y + 1, 1, 4, accent);
            }
            else if (typeName.Contains("Toggle"))
            {
                Outline(pixels, x, y, 7, 7, accent);
                Line(pixels, x + 1, y + 3, x + 3, y + 1, accent);
                Line(pixels, x + 3, y + 1, x + 6, y + 5, accent);
            }
            else if (typeName.Contains("Text") || typeName.Contains("Label"))
            {
                Fill(pixels, x, y + 5, 8, 1, accent);
                Fill(pixels, x, y + 2, 6, 1, line);
                Fill(pixels, x, y, 7, 1, line);
            }
            else
            {
                Outline(pixels, x, y, 8, 7, accent);
                Fill(pixels, x + 2, y + 3, 4, 1, line);
            }
        }

        private static Texture2D DrawAudioWaveform(Remedy.Audio.SoundCueGraph cue)
        {
            const int width = 512, height = 128, rate = 11025;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Sound Cue Waveform Preview", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var amplitudes = new float[width];
            try
            {
                if (cue != null)
                {
                    var voice = Remedy.Audio.SoundCueVoice.Create(cue, rate, 1);
                    int total = Mathf.Max(1, (int)((voice.LengthSamples > 0 ? Mathf.Min(voice.LengthSamples / (float)rate, 20f) : 4f) * rate));
                    var block = new float[Remedy.Audio.SoundCueBuilder.BlockSize];
                    for (int done = 0; done < total; done += block.Length)
                    {
                        int count = Mathf.Min(block.Length, total - done);
                        voice.Render(block, 0, count);
                        for (int i = 0; i < count; i++)
                        {
                            int bucket = (int)((long)(done + i) * width / total);
                            amplitudes[bucket] = Mathf.Max(amplitudes[bucket], Mathf.Abs(block[i]));
                        }
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"Couldn't render Sound Cue waveform preview: {e.Message}", cue); }
            var pixels = new Color[width * height];
            for (int x = 0; x < width; x++)
            {
                int half = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(1f, amplitudes[x]) * (height * 0.46f)), 1, height / 2);
                for (int y = height / 2 - half; y <= height / 2 + half; y++) pixels[y * width + x] = new Color(1, 1, 1, 0.9f);
            }
            texture.SetPixels(pixels); texture.Apply(false, false);
            return texture;
        }

        private static Color32[] NewCanvas(Color32 color)
        {
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            return pixels;
        }

        private static Texture2D MakeTexture(string name, Color32[] pixels)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static void Fill(Color32[] pixels, int x, int y, int width, int height, Color32 color)
        {
            for (int py = y; py < y + height; py++)
                for (int px = x; px < x + width; px++)
                    Put(pixels, px, py, color);
        }

        private static void Outline(Color32[] pixels, int x, int y, int width, int height, Color32 color)
        {
            Line(pixels, x, y, x + width - 1, y, color);
            Line(pixels, x, y + height - 1, x + width - 1, y + height - 1, color);
            Line(pixels, x, y, x, y + height - 1, color);
            Line(pixels, x + width - 1, y, x + width - 1, y + height - 1, color);
        }

        private static void Line(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
        {
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                Put(pixels, x0, y0, color);
                if (x0 == x1 && y0 == y1)
                    break;
                int twice = 2 * error;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
            }
        }

        private static void Disk(Color32[] pixels, int cx, int cy, int radius, Color32 color)
        {
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                    if (x * x + y * y <= radius * radius)
                        Put(pixels, cx + x, cy + y, color);
        }

        private static void Put(Color32[] pixels, int x, int y, Color32 color)
        {
            if ((uint)x < Size && (uint)y < Size)
                pixels[y * Size + x] = color;
        }
    }
}
