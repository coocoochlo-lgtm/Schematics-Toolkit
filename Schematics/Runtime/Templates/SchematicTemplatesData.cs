using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// One premade Schematic Object people can start from: a prefab (with a <see cref="SchematicObject"/> on its root)
    /// plus how it shows up in the "Create New Schematic Object" browser.
    /// </summary>
    [Serializable]
    public class SchematicTemplate
    {
        [Tooltip("The prefab this template creates. Its root should have a Schematic Object.")]
        public GameObject Prefab;

        [Tooltip("The name shown under the thumbnail. Empty: the prefab's name.")]
        public string Name;

        [Tooltip("Groups templates in the browser (its tabs). Use '/' for nothing special: it's just a label.")]
        public string Category = "General";

        [TextArea(1, 4)]
        [Tooltip("A sentence or two shown as the tile's tooltip.")]
        public string Description;

        [Tooltip("A building block for level design. The editor keeps this in step with the Level Design Int Tag on the prefab's Schematic Object, so the Level Designer tools can find these objects in scenes too.")]
        public bool IsLevelDesign;

        /// <summary>What the browser shows: <see cref="Name"/>, or the prefab's name when that's empty.</summary>
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Prefab != null ? Prefab.name : "(missing prefab)";

        /// <summary><see cref="Category"/>, or "General" when it's empty.</summary>
        public string CategoryOrDefault => string.IsNullOrWhiteSpace(Category) ? "General" : Category.Trim();
    }

    /// <summary>
    /// A catalog of <see cref="SchematicTemplate"/>s. Any number of these can live anywhere in the project: the editor
    /// finds them all and merges them into one list (lower <see cref="Order"/> first), so a team, a feature package or
    /// a single level can each ship their own set without touching anyone else's.
    /// </summary>
    /// <remarks>
    /// It's a plain ScriptableObject (in a runtime assembly so it serializes everywhere), but only the editor reads it:
    /// nothing here ends up mattering in a build unless your own code references it.
    /// </remarks>
    [CreateAssetMenu(menuName = "Schematics/Templates Catalog", fileName = "Schematic Templates", order = 200)]
    public class SchematicTemplatesData : ScriptableObject
    {
        [Tooltip("Catalogs are merged lowest Order first. When two catalogs list the same prefab, the first one wins.")]
        public int Order;

        public List<SchematicTemplate> Templates = new();

        /// <summary>The entry for a prefab, or null.</summary>
        public SchematicTemplate Find(GameObject prefab)
        {
            if (prefab == null || Templates == null)
                return null;
            foreach (var template in Templates)
                if (template != null && template.Prefab == prefab)
                    return template;
            return null;
        }

        /// <summary>
        /// Adds a prefab (or returns its existing entry, updated with any non-empty values given). Call
        /// <c>EditorUtility.SetDirty</c> afterwards when used from the editor.
        /// </summary>
        public SchematicTemplate AddOrUpdate(GameObject prefab, string name = null, string category = null, string description = null, bool? isLevelDesign = null)
        {
            if (prefab == null)
                return null;
            Templates ??= new List<SchematicTemplate>();
            var template = Find(prefab);
            if (template == null)
            {
                template = new SchematicTemplate { Prefab = prefab };
                Templates.Add(template);
            }
            if (!string.IsNullOrEmpty(name)) template.Name = name;
            if (!string.IsNullOrEmpty(category)) template.Category = category;
            if (!string.IsNullOrEmpty(description)) template.Description = description;
            if (isLevelDesign.HasValue) template.IsLevelDesign = isLevelDesign.Value;
            return template;
        }

        /// <summary>Removes a prefab's entry. Returns false if it wasn't listed.</summary>
        public bool Remove(GameObject prefab)
        {
            if (prefab == null || Templates == null)
                return false;
            return Templates.RemoveAll(t => t != null && t.Prefab == prefab) > 0;
        }
    }
}
