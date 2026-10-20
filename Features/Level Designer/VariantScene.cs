using Remedy.LevelStreaming;
using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>
    /// Marks a scene as a variant of a blockout (an art pass, a lighting setup, a gameplay alternative; formerly
    /// "art design"), and remembers which blockout it was built on, so opening it (from the window or by hand) can
    /// bring the blockout in underneath as an X-ray reference.<br/>
    /// When variants are composed with their blockout into one scene (for a build or "Test Composed Scene"), this
    /// marker is removed from the composed copy: the blockout is already in it.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Designer/Variant Scene")]
    public class VariantScene : MonoBehaviour
    {
        /// <summary>What happens to the blockout when the variant is played on its own.</summary>
        public enum PlayModeBlockout
        {
            /// <summary>The blockout isn't loaded: the variant scene stands on its own (the usual end state).</summary>
            None,
            /// <summary>The blockout is loaded for its collision only: its shapes don't render. Handy while the art
            /// has no collision of its own yet.</summary>
            CollisionOnly,
            /// <summary>The blockout is loaded and visible, for comparing art against it in play.</summary>
            Visible,
        }

        /// <summary>The blockout scene's GUID (editor lookups).</summary>
        public string BlockoutSceneGuid;

        /// <summary>The blockout scene's path, used to load it in Play Mode (it must be in the Build Profile for builds).</summary>
        public string BlockoutScenePath;

        /// <summary>Whether the blockout comes along in Play Mode, and how.</summary>
        public PlayModeBlockout InPlayMode = PlayModeBlockout.None;

        private void Awake()
        {
            if (InPlayMode == PlayModeBlockout.None || string.IsNullOrEmpty(BlockoutScenePath))
                return;

            // Set before the blockout streams in, so its shapes hide their renderers as they wake.
            BlockoutShape.HideRenderersInPlayMode = InPlayMode == PlayModeBlockout.CollisionOnly;
            if (!LevelLoader.IsLoaded(LevelReference.FromScene(BlockoutScenePath)))
                LevelLoader.Load(BlockoutScenePath);
        }

        private void OnDestroy()
        {
            if (InPlayMode != PlayModeBlockout.None)
                BlockoutShape.HideRenderersInPlayMode = false;
        }
    }
}
