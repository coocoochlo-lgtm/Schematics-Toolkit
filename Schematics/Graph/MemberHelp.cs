using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// What the code says about a field or a Behavior's Variable or Signal, for hover help in the editor: its [Tooltip],
    /// else its [SchematicDoc] description (the one the assistant reads).
    /// </summary>
    public static class MemberHelp
    {
        /// <summary>What the code says about a field: its [Tooltip], else its [SchematicDoc] description.</summary>
        public static string Of(MemberInfo member)
        {
            if (member == null)
                return null;
            var tooltip = member.GetCustomAttribute<TooltipAttribute>(true)?.tooltip;
            if (!string.IsNullOrWhiteSpace(tooltip))
                return tooltip.Trim();
            var doc = MemberDoc.Of(member);
            return string.IsNullOrWhiteSpace(doc?.description) ? null : doc.description.Trim();
        }

        /// <summary>A Behavior's Variable or Signal (by its name in the dock), from the field declaring it.</summary>
        public static string OfBehavior(Type behavior, string name)
        {
            for (var type = behavior; type != null && type != typeof(object); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    string clean = Clean(field.Name);
                    if (field.Name != name && clean != name)
                        continue;
                    // An auto-property's help may be on the property rather than its hidden field.
                    return Of(field) ?? (clean != field.Name ? Of(type.GetProperty(clean, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) : null);
                }
            return null;
        }

        // Auto-property backing fields look like "<Speed>k__BackingField" (as SchematicBehavior names them).
        private static string Clean(string name)
        {
            if (name.Length > 0 && name[0] == '<')
            {
                int end = name.IndexOf('>');
                if (end > 1)
                    return name.Substring(1, end - 1);
            }
            return name;
        }

        /// <summary>Joins what something is with how to use it, for a row's tooltip.</summary>
        public static string Row(string about, string use)
            => string.IsNullOrWhiteSpace(about) ? use : about.Trim() + "\n\n" + use;

        /// <summary>A [SchematicDoc]'s description (and pins) parsed, cached per member.</summary>
        [Serializable]
        public sealed class MemberDoc
        {
            [Serializable]
            public sealed class Pin
            {
                public string name, type, description;
            }

            public string name, description, notes;
            public string[] use_for;
            public Pin[] inputs, outputs;

            private static readonly Dictionary<MemberInfo, MemberDoc> Cache = new();

            public static MemberDoc Of(MemberInfo member)
            {
                if (member == null)
                    return null;
                lock (Cache)
                {
                    if (Cache.TryGetValue(member, out var doc))
                        return doc;
                    var attribute = member.GetCustomAttribute<SchematicDocAttribute>(false);
                    if (attribute != null)
                    {
                        try { doc = JsonUtility.FromJson<MemberDoc>(attribute.Json); }
                        catch { doc = null; }
                    }
                    return Cache[member] = doc;
                }
            }
        }
    }
}
