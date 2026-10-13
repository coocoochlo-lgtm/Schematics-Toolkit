using System;
using System.Collections.Generic;

namespace Remedy.Framework
{
    /// <summary>
    /// The built-in Int Tags, and name lookups for every tag (built-in and user-added).
    /// </summary>
    /// <remarks>
    /// The ids below are permanent: scenes, prefabs and graphs save the number, so changing one would silently retag
    /// things. New built-ins may be added (below 100); user tags start at <see cref="FirstUserTag"/> and are never
    /// reused, even after being deleted.
    /// </remarks>
    public static class IntTags
    {
        public const int Untagged = 0;
        public const int Player = 1;
        public const int LevelDesign = 2;
        public const int Enemy = 3;
        public const int Interactable = 4;
        public const int Pickup = 5;
        public const int SpawnPoint = 6;
        public const int Vehicle = 7;
        public const int MainCamera = 8;
        public const int Respawn = 9;
        public const int Finish = 10;

        /// <summary>The first id handed out to a user-added tag. Everything below is reserved for built-ins.</summary>
        public const int FirstUserTag = 100;

        private static readonly (int id, string name)[] _defaults =
        {
            (Untagged, "Untagged"),
            (Player, "Player"),
            (LevelDesign, "LevelDesign"),
            (Enemy, "Enemy"),
            (Interactable, "Interactable"),
            (Pickup, "Pickup"),
            (SpawnPoint, "SpawnPoint"),
            (Vehicle, "Vehicle"),
            (MainCamera, "MainCamera"),
            (Respawn, "Respawn"),
            (Finish, "Finish"),
        };

        /// <summary>The built-in tags only, in id order.</summary>
        public static IReadOnlyList<(int id, string name)> Defaults => _defaults;

        private static List<(int id, string name)> _all;
        private static int _allVersion = -1;
        private static IntTagsData _allSource;

        /// <summary>
        /// Every tag: the built-ins first, then the user-added ones in the order they were made. Rebuilt only when the
        /// IntTagsData asset changes, so it's cheap to call every repaint.
        /// </summary>
        public static IReadOnlyList<(int id, string name)> All
        {
            get
            {
                var data = IntTagsData.Instance;
                int version = data != null ? data.Version : 0;
                if (_all != null && _allVersion == version && ReferenceEquals(_allSource, data))
                    return _all;

                _all ??= new List<(int id, string name)>();
                _all.Clear();
                _all.AddRange(_defaults);
                if (data != null)
                    foreach (var entry in data.Tags)
                        if (entry != null)
                            _all.Add((entry.Id, entry.Name));
                _allVersion = version;
                _allSource = data;
                return _all;
            }
        }

        /// <summary>True if the id is one of the built-in tags (which can't be renamed or removed).</summary>
        public static bool IsDefault(int tag)
        {
            for (int i = 0; i < _defaults.Length; i++)
                if (_defaults[i].id == tag)
                    return true;
            return false;
        }

        /// <summary>
        /// A tag's display name. Unknown ids (a user tag that was deleted, or data from another project) read
        /// "Missing Tag (id)" rather than throwing, so nothing breaks while it's sorted out.
        /// </summary>
        public static string NameOf(int tag)
        {
            for (int i = 0; i < _defaults.Length; i++)
                if (_defaults[i].id == tag)
                    return _defaults[i].name;

            var data = IntTagsData.Instance;
            if (data != null && data.TryGetName(tag, out var name))
                return name;
            return $"Missing Tag ({tag})";
        }

        /// <summary>True if a tag with this id exists (built-in or user-added).</summary>
        public static bool Exists(int tag)
        {
            if (IsDefault(tag))
                return true;
            var data = IntTagsData.Instance;
            return data != null && data.TryGetName(tag, out _);
        }

        /// <summary>Finds a tag by name (case-insensitive). Handy for migrating from Unity's string tags.</summary>
        public static bool TryGetId(string name, out int tag)
        {
            if (!string.IsNullOrEmpty(name))
            {
                foreach (var (id, tagName) in All)
                {
                    if (string.Equals(tagName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        tag = id;
                        return true;
                    }
                }
            }
            tag = Untagged;
            return false;
        }
    }
}
