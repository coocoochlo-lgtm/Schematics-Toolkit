using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Remedy.LevelDesigner
{
    /// <summary>
    /// The list of levels the Level Designer window shows: every <see cref="BlockoutLevel"/> (the grey-box version of a
    /// level) and, under each, its <see cref="Variant"/>s (the dressed-up versions built on top of it), plus the
    /// team's colour <see cref="Palette"/> for blockouts.<br/>
    /// Scenes are remembered by GUID as well as path, so moving or renaming a scene in the Project window doesn't lose
    /// it. One of these lives in the project (made automatically by the window the first time it opens).
    /// </summary>
    [CreateAssetMenu(menuName = "Remedy Toolkit/Level Designer/Level Design Project", fileName = "LevelDesignProject")]
    public class LevelDesignProject : ScriptableObject
    {
        /// <summary>The default <see cref="LevelsRootFolder"/>.</summary>
        public const string DefaultLevelsRootFolder = "Assets/Levels";

        /// <summary>
        /// Where the Level Designer puts every scene it makes, one folder per level:
        /// <c>&lt;Root&gt;/&lt;Level&gt;/&lt;Level&gt;_Blockout.unity</c> for the blockout and
        /// <c>&lt;Root&gt;/&lt;Level&gt;/Variants/&lt;Level&gt;_&lt;Variant&gt;.unity</c> for its variants, plus
        /// <c>&lt;Root&gt;/Thumbnails~/</c> for the scene thumbnails the window shows. Stored in this asset (not per
        /// user) so the whole team's scenes land in the same place.
        /// </summary>
        public string LevelsRootFolder = DefaultLevelsRootFolder;

        /// <summary>
        /// When a build includes a blockout scene whose level has variants flagged "Compose into build", build a
        /// composed scene (blockout + those variants merged) in its place. Off: blockouts build as they are.
        /// </summary>
        public bool ComposeVariantsOnBuild = true;

        /// <summary>Every blockout level, in the order the window lists them.</summary>
        public List<BlockoutLevel> Blockouts = new();

        /// <summary>
        /// The team's named blockout colours ("Climbable", "Enemy Zone", "Secret"…), so everyone paints with the same
        /// colour language. Kept in this asset (not per user) so it's shared through version control. The blockout
        /// toolbar's colour swatch picks from it; the window's Palette tab edits it.
        /// </summary>
        public List<PaletteColor> Palette = DefaultPalette();

        /// <summary>A starting palette: the category colours, so painting and categories agree out of the box.</summary>
        public static List<PaletteColor> DefaultPalette() => new()
        {
            new PaletteColor("Floor", BlockoutShape.ColorOf(BlockoutCategory.Floor)),
            new PaletteColor("Wall", BlockoutShape.ColorOf(BlockoutCategory.Wall)),
            new PaletteColor("Platform", BlockoutShape.ColorOf(BlockoutCategory.Platform)),
            new PaletteColor("Hazard", BlockoutShape.ColorOf(BlockoutCategory.Hazard)),
            new PaletteColor("Interactive", BlockoutShape.ColorOf(BlockoutCategory.Interactive)),
            new PaletteColor("Water", BlockoutShape.ColorOf(BlockoutCategory.Water)),
            new PaletteColor("Decoration", BlockoutShape.ColorOf(BlockoutCategory.Decoration)),
            new PaletteColor("Measure", new Color(1f, 0.85f, 0.2f)),
        };

        /// <summary>Finds the blockout whose scene has this GUID, or null.</summary>
        public BlockoutLevel FindBlockout(string sceneGuid)
        {
            if (string.IsNullOrEmpty(sceneGuid))
                return null;
            foreach (var blockout in Blockouts)
                if (blockout != null && blockout.SceneGuid == sceneGuid)
                    return blockout;
            return null;
        }

        /// <summary>Finds the blockout that owns the variant scene with this GUID (and the variant), or null.</summary>
        public BlockoutLevel FindOwnerOfVariant(string variantSceneGuid, out Variant variant)
        {
            variant = null;
            if (string.IsNullOrEmpty(variantSceneGuid))
                return null;
            foreach (var blockout in Blockouts)
            {
                if (blockout == null)
                    continue;
                foreach (var candidate in blockout.Variants)
                {
                    if (candidate != null && candidate.SceneGuid == variantSceneGuid)
                    {
                        variant = candidate;
                        return blockout;
                    }
                }
            }
            return null;
        }
    }

    /// <summary>One named colour of the project's blockout <see cref="LevelDesignProject.Palette"/>.</summary>
    [Serializable]
    public class PaletteColor
    {
        public string Name;
        public Color Color = Color.white;

        public PaletteColor() { }

        public PaletteColor(string name, Color color)
        {
            Name = name;
            Color = color;
        }
    }

    /// <summary>What happens to a blockout's own shapes when it's composed with its variants into one scene.</summary>
    public enum ComposedBlockoutMode
    {
        /// <summary>The blockout comes along as it is (shapes visible and solid).</summary>
        Visible,
        /// <summary>The blockout's shapes stay solid but don't render: the art is what's seen.</summary>
        CollisionOnly,
        /// <summary>Only the variants are composed; the blockout's objects are left out.</summary>
        LeaveOut,
    }

    /// <summary>
    /// A blockout: the quick grey-box layout of a level, made of <see cref="BlockoutShape"/>s and rulers, where the
    /// gameplay (distances, heights, jumps) gets worked out before any art exists.
    /// </summary>
    [Serializable]
    public class BlockoutLevel
    {
        /// <summary>Display name (defaults to the scene's name).</summary>
        public string Name;

        /// <summary>The blockout scene's asset GUID (survives moves and renames).</summary>
        public string SceneGuid;

        /// <summary>The blockout scene's path when it was last seen (a fallback if the GUID can't be resolved).</summary>
        public string ScenePath;

        /// <summary>Free notes for the team: goals of the level, open questions.</summary>
        [TextArea] public string Notes;

        /// <summary>The variants (art passes, lighting setups, gameplay alternatives) made on top of this blockout.</summary>
        [FormerlySerializedAs("ArtDesigns")]
        public List<Variant> Variants = new();

        /// <summary>
        /// How the blockout itself takes part when it's composed with its variants (for a build, or "Test Composed
        /// Scene").
        /// </summary>
        public ComposedBlockoutMode ComposedBlockout = ComposedBlockoutMode.Visible;

        /// <summary>True when at least one variant is flagged <see cref="Variant.ComposeIntoBuild"/>.</summary>
        public bool HasComposedVariants
        {
            get
            {
                foreach (var variant in Variants)
                    if (variant != null && variant.ComposeIntoBuild)
                        return true;
                return false;
            }
        }
    }

    /// <summary>
    /// A variant of a blockout (what used to be called an "art design"): its own scene, opened with the blockout scene
    /// loaded underneath it so art, lighting or gameplay alternatives can be placed against the blockout's shapes
    /// (shown as an X-ray overlay). Variants flagged <see cref="ComposeIntoBuild"/> are merged with the blockout into
    /// one scene when the game is built.
    /// </summary>
    [Serializable]
    public class Variant
    {
        public string Name;
        public string SceneGuid;
        public string ScenePath;
        [TextArea] public string Notes;

        /// <summary>
        /// Merge this variant into the level's composed scene when the game is built (the blockout plus every flagged
        /// variant become one scene, which replaces the blockout in the build's scene list for that build only).
        /// </summary>
        public bool ComposeIntoBuild;
    }
}
