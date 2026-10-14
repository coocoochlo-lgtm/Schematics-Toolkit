using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Framework
{
    /// <summary>
    /// Every tagged GameObject, filed under its Int Tags: a <c>Dictionary&lt;int, List&lt;GameObject&gt;&gt;</c>, so
    /// "all the Enemies" is one dictionary lookup instead of a scene search.
    /// </summary>
    /// <remarks>
    /// <para>Objects get here on their own: <see cref="IntTagged"/> and SchematicObject add their tags when they're
    /// enabled (or spawned from a pool) and remove them when they're disabled (or despawned). Code can also tag things
    /// directly with <see cref="Add"/> / <see cref="Remove"/>; those tags last until the object is deactivated.</para>
    /// <para>Only active objects are listed. Destroyed objects are dropped lazily, the next time their tag is looked up,
    /// so destroying something never costs a search.</para>
    /// <para>Edit Mode works too: the editor rebuilds the registry from the open scenes whenever the hierarchy changes
    /// (lazily, on the next lookup). Tags added from code in Edit Mode don't survive that rebuild; put them on an
    /// <see cref="IntTagged"/> component instead.</para>
    /// <para>The lists returned by <see cref="Find"/> are the registry's own: read them, don't keep them, and don't add
    /// or remove tags while looping over one (use <see cref="FindAll"/> to get a copy when you need to).</para>
    /// </remarks>
    public static class IntTagRegistry
    {
        // One per tag: the objects, plus where each one sits in the list so removing is a swap instead of a search.
        private sealed class Bucket
        {
            public readonly List<GameObject> Items = new();
            public readonly Dictionary<GameObject, int> Index = new();

            public bool Add(GameObject go)
            {
                if (Index.ContainsKey(go))
                    return false;
                Index[go] = Items.Count;
                Items.Add(go);
                return true;
            }

            public bool Remove(GameObject go)
            {
                if (!Index.TryGetValue(go, out int index))
                    return false;
                RemoveAt(index);
                return true;
            }

            public void RemoveAt(int index)
            {
                int last = Items.Count - 1;
                var removed = Items[index];
                if (index != last)
                {
                    var moved = Items[last];
                    Items[index] = moved;
                    Index[moved] = index;
                }
                Items.RemoveAt(last);
                Index.Remove(removed);
            }
        }

        private static readonly Dictionary<int, Bucket> _byTag = new();
        private static readonly Dictionary<GameObject, List<int>> _byObject = new();
        private static readonly List<Component> _componentBuffer = new();
        private static bool _suppressEvents;

        /// <summary>Raised when an object gains a tag (not for the Edit Mode rebuild; see <see cref="Rebuilt"/>).</summary>
        public static event Action<GameObject, IntTag> Added;

        /// <summary>Raised when an object loses a tag (not when a destroyed object is dropped lazily).</summary>
        public static event Action<GameObject, IntTag> Removed;

        /// <summary>Raised after the editor rebuilt the whole registry from the open scenes (Edit Mode only).</summary>
        public static event Action Rebuilt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _byTag.Clear();
            _byObject.Clear();
            Added = null;
            Removed = null;
            Rebuilt = null;
            _suppressEvents = false;
        }

        #region Queries

        /// <summary>
        /// Every active object with the tag. Never null; an empty list when there are none. The list is the
        /// registry's own (no allocation): don't modify it or keep it.
        /// </summary>
        public static IReadOnlyList<GameObject> Find(IntTag tag)
        {
            EnsureFresh();
            if (!_byTag.TryGetValue(tag.Value, out var bucket))
                return Array.Empty<GameObject>();
            Prune(tag.Value, bucket);
            return bucket.Items;
        }

        /// <summary>The first active object with the tag, or null. Handy for one-of-a-kind tags like Player.</summary>
        public static GameObject FindFirst(IntTag tag)
        {
            var found = Find(tag);
            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>Copies every active object with the tag into <paramref name="results"/> (cleared first). Returns the count.</summary>
        public static int FindAll(IntTag tag, List<GameObject> results)
        {
            results.Clear();
            results.AddRange(Find(tag));
            return results.Count;
        }

        /// <summary>How many active objects have the tag.</summary>
        public static int Count(IntTag tag) => Find(tag).Count;

        /// <summary>True if the object is registered under the tag.</summary>
        public static bool Has(GameObject go, IntTag tag)
        {
            if (go == null)
                return false;
            EnsureFresh();
            return _byTag.TryGetValue(tag.Value, out var bucket) && bucket.Index.ContainsKey(go);
        }

        /// <summary>Copies the tags an object is registered under into <paramref name="results"/> (cleared first).</summary>
        public static int GetTags(GameObject go, List<IntTag> results)
        {
            results.Clear();
            if (go == null)
                return 0;
            EnsureFresh();
            if (_byObject.TryGetValue(go, out var tags))
                foreach (var tag in tags)
                    results.Add(tag);
            return results.Count;
        }

        /// <summary>Every tag that currently has at least one object (ids). Allocates; meant for debug views.</summary>
        public static List<int> ActiveTags()
        {
            EnsureFresh();
            var result = new List<int>();
            foreach (var pair in _byTag)
            {
                Prune(pair.Key, pair.Value);
                if (pair.Value.Items.Count > 0)
                    result.Add(pair.Key);
            }
            return result;
        }

        #endregion

        #region Changes

        /// <summary>
        /// Files an object under a tag. Adding a tag it already has does nothing (no duplicates, no event).
        /// Untagged (0) is never stored.
        /// </summary>
        public static void Add(GameObject go, IntTag tag)
        {
            if (go == null || !tag.IsTagged)
                return;
            EnsureFresh();

            if (!_byTag.TryGetValue(tag.Value, out var bucket))
            {
                bucket = new Bucket();
                _byTag[tag.Value] = bucket;
            }
            if (!bucket.Add(go))
                return;

            if (!_byObject.TryGetValue(go, out var tags))
            {
                tags = new List<int>(2);
                _byObject[go] = tags;
            }
            tags.Add(tag.Value);

            if (!_suppressEvents)
                Added?.Invoke(go, tag);
        }

        /// <summary>Takes an object out of a tag. Does nothing if it didn't have it.</summary>
        public static void Remove(GameObject go, IntTag tag)
        {
            if (ReferenceEquals(go, null))
                return;
            EnsureFresh();

            if (!_byTag.TryGetValue(tag.Value, out var bucket) || !bucket.Remove(go))
                return;

            if (_byObject.TryGetValue(go, out var tags))
            {
                tags.Remove(tag.Value);
                if (tags.Count == 0)
                    _byObject.Remove(go);
            }

            if (!_suppressEvents)
                Removed?.Invoke(go, tag);
        }

        /// <summary>Takes an object out of every tag it's under.</summary>
        public static void RemoveAll(GameObject go)
        {
            if (ReferenceEquals(go, null))
                return;
            EnsureFresh();
            if (!_byObject.TryGetValue(go, out var tags))
                return;
            var copy = tags.ToArray();
            foreach (var tag in copy)
                Remove(go, tag);
        }

        /// <summary>Empties the registry without raising events.</summary>
        public static void Clear()
        {
            _byTag.Clear();
            _byObject.Clear();
        }

        // Destroyed objects compare equal to null but still hash by instance id, so they can be found and dropped here.
        private static void Prune(int tag, Bucket bucket)
        {
            var items = bucket.Items;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var go = items[i];
                if (go != null)
                    continue;
                bucket.RemoveAt(i);
                if (_byObject.TryGetValue(go, out var tags))
                {
                    tags.Remove(tag);
                    if (tags.Count == 0)
                        _byObject.Remove(go);
                }
            }
        }

        #endregion

        #region Sources

        /// <summary>
        /// Keeps the registry in step with a tag-giving component (<see cref="IntTagged"/>, SchematicObject): adds the
        /// tags in <paramref name="tags"/> it hasn't added yet, and removes the ones it added that are gone (or all of
        /// them when <paramref name="active"/> is false). <paramref name="registered"/> is the component's own record
        /// of what it added. A tag another enabled source on the same object still gives is kept. When the object
        /// itself is being deactivated, it leaves every tag, including ones added from code.
        /// </summary>
        /// <remarks>In Edit Mode this just asks the editor to rebuild the registry, which reads every source.</remarks>
        public static void SyncSource(Component source, IReadOnlyList<IntTag> tags, List<IntTag> registered, bool active)
        {
            if (source == null || registered == null)
                return;

            if (!Application.isPlaying)
            {
                registered.Clear();
                RequestEditModeRefresh();
                return;
            }

            var go = source.gameObject;

            for (int i = registered.Count - 1; i >= 0; i--)
            {
                var tag = registered[i];
                if (active && Contains(tags, tag))
                    continue;
                registered.RemoveAt(i);
                if (!HeldByOtherSource(go, source, tag))
                    Remove(go, tag);
            }

            if (!active)
            {
                if (!go.activeInHierarchy)
                    RemoveAll(go);
                return;
            }

            if (tags == null)
                return;
            for (int i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (!tag.IsTagged || registered.Contains(tag))
                    continue;
                registered.Add(tag);
                Add(go, tag);
            }
        }

        private static bool Contains(IReadOnlyList<IntTag> tags, IntTag tag)
        {
            if (tags == null)
                return false;
            for (int i = 0; i < tags.Count; i++)
                if (tags[i].Value == tag.Value)
                    return true;
            return false;
        }

        private static bool HeldByOtherSource(GameObject go, Component except, IntTag tag)
        {
            _componentBuffer.Clear();
            go.GetComponents(_componentBuffer);
            try
            {
                foreach (var component in _componentBuffer)
                {
                    if (component == null || component == except || component is not IIntTagSource source)
                        continue;
                    if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                        continue;
                    if (Contains(source.Tags, tag))
                        return true;
                }
                return false;
            }
            finally
            {
                _componentBuffer.Clear();
            }
        }

        #endregion

        #region Edit Mode

        private static bool _editModeDirty = true;
        private static bool _rebuilding;

        /// <summary>
        /// Tells the registry the scene's tags changed in Edit Mode, so it rebuilds before the next lookup.
        /// Does nothing in Play Mode or in builds.
        /// </summary>
        public static void RequestEditModeRefresh()
        {
            if (!Application.isPlaying)
                _editModeDirty = true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor only: the function that refills the registry from the open scenes (set by the IntTags editor code).
        /// </summary>
        public static Action EditModeScanner { get; set; }

        /// <summary>
        /// Editor only: replaces the registry's contents with these objects and tags, without per-tag events, then
        /// raises <see cref="Rebuilt"/>. Used by the Edit Mode scanner.
        /// </summary>
        public static void Rebuild(IEnumerable<(GameObject go, IntTag tag)> entries)
        {
            bool wasRebuilding = _rebuilding;
            _rebuilding = true;
            _suppressEvents = true;
            try
            {
                Clear();
                foreach (var (go, tag) in entries)
                    Add(go, tag);
            }
            finally
            {
                _suppressEvents = false;
                _rebuilding = wasRebuilding;
                _editModeDirty = false;
            }
            Rebuilt?.Invoke();
        }
#endif

        private static void EnsureFresh()
        {
#if UNITY_EDITOR
            if (!_editModeDirty || _rebuilding || Application.isPlaying || EditModeScanner == null)
                return;
            _editModeDirty = false;
            _rebuilding = true;
            try
            {
                EditModeScanner();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _rebuilding = false;
            }
#endif
        }

        #endregion
    }
}
