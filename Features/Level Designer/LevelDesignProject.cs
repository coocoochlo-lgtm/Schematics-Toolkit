using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>
    /// The list of levels the Level Designer window shows: every <see cref="BlockoutLevel"/> (the grey-box version of a
    /// level) and, under each, its <see cref="ArtDesign"/>s (the dressed-up versions built on top of it).<br/>
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
        /// <c>&lt;Root&gt;/&lt;Level&gt;/Art/&lt;Level&gt;_&lt;Art Design&gt;.unity</c> for its art designs, plus
        /// <c>&lt;Root&gt;/Thumbnails~/</c> for the scene thumbnails the window shows. Stored in this asset (not per
        /// user) so the whole team's scenes land in the same place.
        /// </summary>
        public string LevelsRootFolder = DefaultLevelsRootFolder;

        /// <summary>Every blockout level, in the order the window lists them.</summary>
        public List<BlockoutLevel> Blockouts = new();

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

        /// <summary>Finds the blockout that owns the art design scene with this GUID (and the art design), or null.</summary>
        public BlockoutLevel FindOwnerOfArt(string artSceneGuid, out ArtDesign art)
        {
            art = null;
            if (string.IsNullOrEmpty(artSceneGuid))
                return null;
            foreach (var blockout in Blockouts)
            {
                if (blockout == null)
                    continue;
                foreach (var design in blockout.ArtDesigns)
                {
                    if (design != null && design.SceneGuid == artSceneGuid)
                    {
                        art = design;
                        return blockout;
                    }
                }
            }
            return null;
        }
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

        /// <summary>The art versions made on top of this blockout.</summary>
        public List<ArtDesign> ArtDesigns = new();
    }

    /// <summary>
    /// An art design of a blockout: its own scene, opened with the blockout scene loaded underneath it so art can be
    /// placed against the blockout's shapes (shown as an X-ray overlay).
    /// </summary>
    [Serializable]
    public class ArtDesign
    {
        public string Name;
        public string SceneGuid;
        public string ScenePath;
        [TextArea] public string Notes;
    }
}
