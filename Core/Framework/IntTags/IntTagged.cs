using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Framework
{
    /// <summary>
    /// Gives a GameObject Int Tags. Add it to anything that should be findable by tag; Schematic Objects don't need it
    /// (they have their own Tags list).
    /// </summary>
    /// <remarks>
    /// The tags are registered with <see cref="IntTagRegistry"/> while the component is enabled, so disabled,
    /// deactivated or pooled-away objects drop out of lookups automatically. Runs in Edit Mode as well, so editor
    /// tools and graph previews can find tagged objects without entering Play Mode.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Remedy/Int Tagged")]
    public sealed class IntTagged : MonoBehaviour, IIntTagSource
    {
        [Tooltip("The tags this object can be found by. Pick from the dropdown, or choose \"Add Tag...\" to make a new one.")]
        public List<IntTag> Tags = new();

        // What this component actually added to the registry, so changes to Tags can be applied as a difference.
        private readonly List<IntTag> _registered = new();

        IReadOnlyList<IntTag> IIntTagSource.Tags => Tags;

        private void OnEnable() => IntTagRegistry.SyncSource(this, Tags, _registered, true);

        private void OnDisable() => IntTagRegistry.SyncSource(this, Tags, _registered, false);

        // Picks up tags edited in the Inspector, in either mode.
        private void OnValidate()
        {
            if (!Application.isPlaying)
                IntTagRegistry.RequestEditModeRefresh();
            else if (isActiveAndEnabled)
                IntTagRegistry.SyncSource(this, Tags, _registered, true);
        }

        /// <summary>Adds a tag (and registers it right away if the component is enabled).</summary>
        public void AddTag(IntTag tag)
        {
            if (!tag.IsTagged || Tags.Contains(tag))
                return;
            Tags.Add(tag);
            if (isActiveAndEnabled)
                IntTagRegistry.SyncSource(this, Tags, _registered, true);
        }

        /// <summary>Removes a tag (and unregisters it right away).</summary>
        public void RemoveTag(IntTag tag)
        {
            if (!Tags.Remove(tag))
                return;
            IntTagRegistry.SyncSource(this, Tags, _registered, isActiveAndEnabled);
        }
    }
}
