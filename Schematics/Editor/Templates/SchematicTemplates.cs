using System;
using System.Collections.Generic;
using System.Linq;
using Remedy.Framework;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Templates
{
    /// <summary>
    /// Every Schematic template in the project, in one list: all <see cref="SchematicTemplatesData"/> catalogs merged
    /// (lowest Order first, then by path; the first catalog listing a prefab wins). Reloaded lazily whenever the project
    /// changes, so adding a catalog or a template anywhere shows up in every open browser.
    /// </summary>
    /// <remarks>
    /// This is the API the template browser (<see cref="SchematicTemplateGrid"/>) and other tools (the Level Designer)
    /// read. Templates are editor-only data: nothing here runs in a build.
    /// </remarks>
    [InitializeOnLoad]
    public static class SchematicTemplates
    {
        /// <summary>The catalog made on demand when nothing else is there to add to.</summary>
        public const string DefaultCatalogPath = SchematicObjectFactory.TemplatesFolder + "/Default Templates.asset";

        /// <summary>The category templates get when none is given.</summary>
        public const string DefaultCategory = "General";

        private static readonly List<SchematicTemplate> _all = new();
        private static readonly List<string> _categories = new();
        private static readonly List<SchematicTemplatesData> _catalogs = new();
        private static readonly Dictionary<SchematicTemplate, SchematicTemplatesData> _owners = new();
        private static bool _dirty = true;

        /// <summary>Raised after the merged list changed (a catalog or template was added, removed or edited).</summary>
        public static event Action Changed;

        static SchematicTemplates()
        {
            EditorApplication.projectChanged += MarkDirty;
            Undo.undoRedoPerformed += MarkDirty;
        }

        /// <summary>Every template with a prefab, from every catalog, merged.</summary>
        public static IReadOnlyList<SchematicTemplate> All { get { EnsureLoaded(); return _all; } }

        /// <summary>The categories used by <see cref="All"/>, sorted (the default one first).</summary>
        public static IReadOnlyList<string> Categories { get { EnsureLoaded(); return _categories; } }

        /// <summary>Every catalog in the project, in merge order.</summary>
        public static IReadOnlyList<SchematicTemplatesData> Catalogs { get { EnsureLoaded(); return _catalogs; } }

        /// <summary>Templates in a category ("All" or null for every template).</summary>
        public static IEnumerable<SchematicTemplate> InCategory(string category)
            => string.IsNullOrEmpty(category) || category == "All" ? All : All.Where(t => t.CategoryOrDefault == category);

        /// <summary>The catalog a template came from.</summary>
        public static SchematicTemplatesData CatalogOf(SchematicTemplate template)
        {
            EnsureLoaded();
            return template != null && _owners.TryGetValue(template, out var catalog) ? catalog : null;
        }

        /// <summary>The template for a prefab, or null if it isn't one.</summary>
        public static SchematicTemplate Find(GameObject prefab)
        {
            if (prefab == null) return null;
            foreach (var template in All)
                if (template.Prefab == prefab)
                    return template;
            return null;
        }

        /// <summary>
        /// A thumbnail for a prefab, rendered from a 3/4 angle like the Prefab view (cached in memory and in
        /// Library/). Returns Unity's preview while the render is pending; <see cref="SchematicTemplateThumbnails.Updated"/>
        /// says when the real one is ready.
        /// </summary>
        public static Texture Thumbnail(GameObject prefab) => SchematicTemplateThumbnails.Get(prefab);

        /// <summary>Forgets the merged list; it's rebuilt the next time it's read, and <see cref="Changed"/> is raised.</summary>
        public static void Refresh()
        {
            _dirty = true;
            Changed?.Invoke();
        }

        private static void MarkDirty()
        {
            if (_dirty) return;
            Refresh();
        }

        #region Catalogs

        /// <summary>
        /// The catalog new templates go into when no other is named: the one at <see cref="DefaultCatalogPath"/>, made the
        /// first time it's needed.
        /// </summary>
        public static SchematicTemplatesData DefaultCatalog
        {
            get
            {
                var catalog = AssetDatabase.LoadAssetAtPath<SchematicTemplatesData>(DefaultCatalogPath);
                if (catalog != null)
                    return catalog;
                SchematicObjectFactory.EnsureFolder(SchematicObjectFactory.TemplatesFolder);
                catalog = ScriptableObject.CreateInstance<SchematicTemplatesData>();
                AssetDatabase.CreateAsset(catalog, DefaultCatalogPath);
                AssetDatabase.SaveAssets();
                Refresh();
                return catalog;
            }
        }

        /// <summary>A new, empty catalog at a path (".asset").</summary>
        public static SchematicTemplatesData CreateCatalog(string assetPath)
        {
            SchematicObjectFactory.EnsureFolder(System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));
            var catalog = ScriptableObject.CreateInstance<SchematicTemplatesData>();
            AssetDatabase.CreateAsset(catalog, AssetDatabase.GenerateUniqueAssetPath(assetPath));
            AssetDatabase.SaveAssets();
            Refresh();
            return catalog;
        }

        /// <summary>
        /// Lists a prefab as a template (or updates its entry) in <paramref name="catalog"/> (the default one when null).
        /// The prefab should have a Schematic Object on its root; one without still works, it just has no graph.
        /// </summary>
        public static SchematicTemplate Add(GameObject prefab, string name = null, string category = null, string description = null,
            bool? isLevelDesign = null, SchematicTemplatesData catalog = null)
        {
            if (prefab == null || !EditorUtility.IsPersistent(prefab))
                return null;
            catalog ??= CatalogOf(Find(prefab)) ?? DefaultCatalog;
            Undo.RecordObject(catalog, "Add Schematic Template");
            var template = catalog.AddOrUpdate(prefab, name, category, description, isLevelDesign);
            if (isLevelDesign.HasValue)
                SetPrefabLevelDesignTag(prefab, isLevelDesign.Value);
            else if (HasLevelDesignTag(prefab))
                template.IsLevelDesign = true;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Refresh();
            return template;
        }

        /// <summary>Takes a template out of its catalog (the prefab itself is kept).</summary>
        public static bool Remove(SchematicTemplate template)
        {
            var catalog = CatalogOf(template);
            if (catalog == null || template == null)
                return false;
            Undo.RecordObject(catalog, "Remove Schematic Template");
            bool removed = catalog.Templates.Remove(template);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Refresh();
            return removed;
        }

        #endregion

        #region Level Design tag

        /// <summary>Whether a template is a level design building block: its flag, or the Level Design tag on its prefab.</summary>
        public static bool IsLevelDesign(SchematicTemplate template)
            => template != null && (template.IsLevelDesign || HasLevelDesignTag(template.Prefab));

        /// <summary>Sets a template's Level Design flag and the matching Int Tag on its prefab's Schematic Object.</summary>
        public static void SetLevelDesign(SchematicTemplate template, bool value)
        {
            if (template == null) return;
            var catalog = CatalogOf(template);
            if (catalog != null)
            {
                Undo.RecordObject(catalog, "Set Level Design");
                template.IsLevelDesign = value;
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
            else
                template.IsLevelDesign = value;
            SetPrefabLevelDesignTag(template.Prefab, value);
            Refresh();
        }

        /// <summary>
        /// Brings every prefab's Level Design tag in line with its template's flag (a flag set in the catalog's Inspector
        /// tags the prefab; a tagged prefab sets the flag). Run by the catalog Inspector when it changes.
        /// </summary>
        public static void SyncLevelDesignTags(SchematicTemplatesData catalog)
        {
            if (catalog == null || catalog.Templates == null) return;
            bool changed = false;
            foreach (var template in catalog.Templates)
            {
                if (template?.Prefab == null) continue;
                if (template.IsLevelDesign)
                    SetPrefabLevelDesignTag(template.Prefab, true);
                else if (HasLevelDesignTag(template.Prefab))
                {
                    template.IsLevelDesign = true;
                    changed = true;
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
            }
        }

        /// <summary>Whether a prefab's root Schematic Object carries the Level Design Int Tag.</summary>
        public static bool HasLevelDesignTag(GameObject prefab)
        {
            if (prefab == null || !prefab.TryGetComponent(out SchematicObject schematic) || schematic.Tags == null)
                return false;
            return schematic.Tags.Contains(new IntTag(IntTags.LevelDesign));
        }

        /// <summary>Adds or removes the Level Design Int Tag on a prefab asset's root Schematic Object (saved right away).</summary>
        public static void SetPrefabLevelDesignTag(GameObject prefab, bool value)
        {
            if (prefab == null || !prefab.TryGetComponent(out SchematicObject schematic))
                return;
            var tag = new IntTag(IntTags.LevelDesign);
            schematic.Tags ??= new List<IntTag>();
            bool has = schematic.Tags.Contains(tag);
            if (has == value)
                return;
            if (value) schematic.Tags.Add(tag);
            else schematic.Tags.RemoveAll(t => t == tag);
            EditorUtility.SetDirty(schematic);
            EditorUtility.SetDirty(prefab);
            if (EditorUtility.IsPersistent(prefab))
                AssetDatabase.SaveAssetIfDirty(prefab);
        }

        #endregion

        private static void EnsureLoaded()
        {
            if (!_dirty)
                return;
            _dirty = false;

            _all.Clear();
            _categories.Clear();
            _catalogs.Clear();
            _owners.Clear();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SchematicTemplatesData)))
            {
                var catalog = AssetDatabase.LoadAssetAtPath<SchematicTemplatesData>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog != null)
                    _catalogs.Add(catalog);
            }
            _catalogs.Sort((a, b) =>
            {
                int order = a.Order.CompareTo(b.Order);
                return order != 0 ? order : string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b));
            });

            var seen = new HashSet<GameObject>();
            var categories = new HashSet<string>();
            foreach (var catalog in _catalogs)
            {
                if (catalog.Templates == null) continue;
                foreach (var template in catalog.Templates)
                {
                    if (template?.Prefab == null || !seen.Add(template.Prefab))
                        continue;
                    _all.Add(template);
                    _owners[template] = catalog;
                    categories.Add(template.CategoryOrDefault);
                }
            }

            _categories.AddRange(categories.OrderBy(c => c == DefaultCategory ? 0 : 1).ThenBy(c => c, StringComparer.OrdinalIgnoreCase));
        }
    }
}
