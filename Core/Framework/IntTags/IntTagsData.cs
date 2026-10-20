using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Framework
{
    /// <summary>
    /// The project's user-added Int Tags: id to name. Lives at Resources/IntTagsData.asset so names are available in
    /// builds too (for logs, debug UI and <see cref="IntTag.ToString"/>); objects themselves only ever store the id.
    /// </summary>
    /// <remarks>
    /// You normally never touch this asset directly: the tag dropdown's "Add Tag..." item creates it on demand, and
    /// Project Settings &gt; Schematics &gt; Int Tags renames tags. Ids are handed out in increasing order from
    /// <see cref="IntTags.FirstUserTag"/> and never reused, so deleting a tag can't make old data point at a new one.
    /// </remarks>
    public sealed class IntTagsData : ScriptableObject
    {
        /// <summary>The name <see cref="Resources.Load(string)"/> finds the asset by.</summary>
        public const string ResourceName = "IntTagsData";

        [Serializable]
        public sealed class Entry
        {
            public int Id;
            public string Name;
        }

        [SerializeField]
        private List<Entry> _tags = new();

        [SerializeField]
        private int _nextId = IntTags.FirstUserTag;

        [NonSerialized]
        private int _version;

        /// <summary>The user tags, in the order they were added.</summary>
        public IReadOnlyList<Entry> Tags => _tags;

        /// <summary>Goes up every time a tag is added, renamed or removed (so name caches know to rebuild).</summary>
        public int Version => _version;

        /// <summary>The id the next added tag will get.</summary>
        public int NextId => Mathf.Max(_nextId, IntTags.FirstUserTag);

        /// <summary>Raised after any tag is added, renamed or removed (and after undo/redo touches the asset).</summary>
        public static event Action Changed;

        private static IntTagsData _instance;
        private static bool _searched;
#if UNITY_EDITOR
        private static double _lastSearchTime;
#endif

        /// <summary>
        /// The project's tag names, loaded from Resources on first use. Null when nobody has added a tag yet (the
        /// built-ins still work without it).
        /// </summary>
        public static IntTagsData Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;
#if UNITY_EDITOR
                // In the editor the asset may appear later (created by the dropdown, or pulled from git), so look
                // again now and then instead of only once.
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                if (_searched && now - _lastSearchTime < 2.0)
                    return null;
                _lastSearchTime = now;
#else
                if (_searched)
                    return null;
#endif
                _searched = true;
                _instance = Resources.Load<IntTagsData>(ResourceName);
                return _instance;
            }
        }

        /// <summary>
        /// Makes this the asset <see cref="Instance"/> returns. The editor calls it right after creating the asset;
        /// tests can use it to swap in their own.
        /// </summary>
        public static void UseInstance(IntTagsData data)
        {
            _instance = data;
            _searched = data != null;
            if (data != null)
                data._version++;
            Changed?.Invoke();
        }

        public bool TryGetName(int id, out string name)
        {
            for (int i = 0; i < _tags.Count; i++)
            {
                var entry = _tags[i];
                if (entry != null && entry.Id == id)
                {
                    name = entry.Name;
                    return true;
                }
            }
            name = null;
            return false;
        }

        /// <summary>
        /// Adds a tag and returns its new id. Names must be non-empty and unique (ignoring case), including among the
        /// built-ins; returns <see cref="IntTags.Untagged"/> and sets <paramref name="error"/> otherwise.
        /// Call <c>Undo.RecordObject</c> / <c>EditorUtility.SetDirty</c> around it from editor code.
        /// </summary>
        public int Add(string name, out string error)
        {
            if (!ValidateName(name, int.MinValue, out error))
                return IntTags.Untagged;

            int id = NextId;
            _nextId = id + 1;
            _tags.Add(new Entry { Id = id, Name = name.Trim() });
            MarkChanged();
            return id;
        }

        /// <summary>Renames a user tag. Built-in tags can't be renamed.</summary>
        public bool Rename(int id, string newName, out string error)
        {
            if (IntTags.IsDefault(id))
            {
                error = "Built-in tags can't be renamed.";
                return false;
            }
            if (!ValidateName(newName, id, out error))
                return false;
            foreach (var entry in _tags)
            {
                if (entry != null && entry.Id == id)
                {
                    entry.Name = newName.Trim();
                    MarkChanged();
                    return true;
                }
            }
            error = $"There is no tag with id {id}.";
            return false;
        }

        /// <summary>
        /// Removes a user tag's name. Objects still holding the id keep it (and show "Missing Tag"); the id is never
        /// handed out again.
        /// </summary>
        public bool Remove(int id)
        {
            if (IntTags.IsDefault(id))
                return false;
            int removed = _tags.RemoveAll(e => e != null && e.Id == id);
            if (removed > 0)
                MarkChanged();
            return removed > 0;
        }

        /// <summary>
        /// Checks a candidate name: not empty, no leading/trailing slash, and not already used by another tag (other than
        /// <paramref name="ignoreId"/>, the tag being renamed; pass int.MinValue for a new tag).
        /// </summary>
        public bool ValidateName(string name, int ignoreId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Tag names can't be empty.";
                return false;
            }
            name = name.Trim();
            if (name.StartsWith("/") || name.EndsWith("/"))
            {
                error = "Tag names can't start or end with '/' (a '/' in the middle groups tags into submenus).";
                return false;
            }
            foreach (var (id, existing) in IntTags.Defaults)
            {
                if (id != ignoreId && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"\"{existing}\" is a built-in tag.";
                    return false;
                }
            }
            foreach (var entry in _tags)
            {
                if (entry != null && entry.Id != ignoreId && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"There is already a tag called \"{entry.Name}\".";
                    return false;
                }
            }
            return true;
        }

        private void MarkChanged()
        {
            _version++;
            Changed?.Invoke();
        }

        private void OnEnable()
        {
            if (_instance == null && name == ResourceName)
                _instance = this;
        }

        // Undo/redo and edits made through the asset's own Inspector come through here.
        private void OnValidate()
        {
            if (_nextId < IntTags.FirstUserTag)
                _nextId = IntTags.FirstUserTag;
            foreach (var entry in _tags)
                if (entry != null && entry.Id >= _nextId)
                    _nextId = entry.Id + 1;
            MarkChanged();
        }
    }
}
