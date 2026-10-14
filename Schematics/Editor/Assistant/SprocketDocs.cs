using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// What Sprocket knows about the toolkit's pieces, for "What is this?": an entry per node, Behavior, Variable,
    /// Signal and editor window. Generated from the code's own summaries and tooltips (Tools > Remedy > Schematics >
    /// Assistant > Regenerate Documentation); write a Description on any entry to replace the generated text, and it's
    /// kept when the rest is regenerated.
    /// </summary>
    public sealed class SprocketDocs : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("node:Type, behavior:Type, variable:Type.Name, signal:Type.Name, component:Type, window:Type")]
            public string Key;
            public string Title;
            [Tooltip("Generated from the code's summaries and tooltips.")]
            [TextArea(2, 8)] public string Summary;
            [Tooltip("Your own words: replaces the generated summary, and survives regeneration.")]
            [TextArea(2, 12)] public string Description;

            public string Text => string.IsNullOrWhiteSpace(Description) ? Summary : Description;
        }

        public List<Entry> Entries = new();

        [NonSerialized] private Dictionary<string, Entry> _byKey;

        public Entry Find(string key)
        {
            if (_byKey == null || _byKey.Count != Entries.Count)
            {
                _byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
                foreach (var entry in Entries)
                    if (entry != null && !string.IsNullOrEmpty(entry.Key))
                        _byKey[entry.Key] = entry;
            }
            return key != null && _byKey.TryGetValue(key, out var found) ? found : null;
        }

        internal void Invalidate() => _byKey = null;
    }
}
