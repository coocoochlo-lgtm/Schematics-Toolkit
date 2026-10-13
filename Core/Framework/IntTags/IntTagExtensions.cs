using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Framework
{
    /// <summary>
    /// Int Tag shortcuts on GameObject: <c>go.HasIntTag(IntTags.Player)</c>, the replacement for <c>CompareTag</c>.
    /// </summary>
    public static class IntTagExtensions
    {
        private static readonly List<IntTag> _tagBuffer = new();
        private static readonly List<Component> _componentBuffer = new();

        /// <summary>
        /// True if the object has the tag. For active objects in Play Mode that's what <see cref="IntTagRegistry"/> says
        /// (including tags added or removed from code); inactive objects, prefabs and Edit Mode objects also answer
        /// from their tag components' lists.
        /// </summary>
        public static bool HasIntTag(this GameObject go, IntTag tag)
        {
            if (go == null || !tag.IsTagged)
                return false;
            if (IntTagRegistry.Has(go, tag))
                return true;
            if (IsRegistryAuthoritative(go))
                return false;

            _componentBuffer.Clear();
            go.GetComponents(_componentBuffer);
            try
            {
                foreach (var component in _componentBuffer)
                {
                    if (component is IIntTagSource source && source.Tags != null)
                        for (int i = 0; i < source.Tags.Count; i++)
                            if (source.Tags[i].Value == tag.Value)
                                return true;
                }
                return false;
            }
            finally
            {
                _componentBuffer.Clear();
            }
        }

        /// <summary>
        /// Every tag the object has (registered or listed by its tag components), without duplicates. Allocates an array.
        /// </summary>
        public static IntTag[] GetIntTags(this GameObject go)
        {
            if (go == null)
                return System.Array.Empty<IntTag>();

            IntTagRegistry.GetTags(go, _tagBuffer);
            if (IsRegistryAuthoritative(go))
            {
                var registered = _tagBuffer.ToArray();
                _tagBuffer.Clear();
                return registered;
            }
            _componentBuffer.Clear();
            go.GetComponents(_componentBuffer);
            try
            {
                foreach (var component in _componentBuffer)
                {
                    if (component is not IIntTagSource source || source.Tags == null)
                        continue;
                    foreach (var tag in source.Tags)
                        if (tag.IsTagged && !_tagBuffer.Contains(tag))
                            _tagBuffer.Add(tag);
                }
                return _tagBuffer.ToArray();
            }
            finally
            {
                _componentBuffer.Clear();
                _tagBuffer.Clear();
            }
        }

        // In Play Mode an active scene object's tags are exactly what's registered (tags can be added and removed at
        // runtime). Inactive objects and prefab assets aren't registered, so their tag components are asked instead.
        private static bool IsRegistryAuthoritative(GameObject go)
            => Application.isPlaying && go.activeInHierarchy && go.scene.IsValid();

        /// <summary>True if the component's GameObject has the tag.</summary>
        public static bool HasIntTag(this Component component, IntTag tag)
            => component != null && component.gameObject.HasIntTag(tag);
    }
}
