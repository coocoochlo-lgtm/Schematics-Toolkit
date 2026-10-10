using System;
using System.Collections.Generic;

namespace Remedy.Schematics
{
    /// <summary>
    /// The Behaviors attached to one GameObject of a Schematic Object (the root or one of its children).
    /// </summary>
    [Serializable]
    public class BehaviorAttachmentList
    {
        public List<BehaviorAttachment> Items = new();
    }

    /// <summary>
    /// A Behavior attached to a GameObject through the Inspector, with the default values for that object.
    /// </summary>
    [Serializable]
    public class BehaviorAttachment
    {
        public BehaviorMetadata Metadata;

        /// <summary>
        /// The instance's Enabled flag when it spawns.
        /// </summary>
        public bool EnabledByDefault = true;

        /// <summary>
        /// Variable defaults for this object only. Anything not listed falls back to the Metadata, then to the code default.
        /// </summary>
        public List<VariableOverride> Overrides = new();

        /// <summary>
        /// Sets this object's value for a Variable, adding the override if needed.
        /// </summary>
        public void SetOverride(string name, object value, System.Type type = null)
        {
            var entry = FindOverride(name);
            if (entry == null)
            {
                entry = new VariableOverride { Name = name };
                Overrides.Add(entry);
            }
            entry.Value.Set(value, type);
        }

        public VariableOverride FindOverride(string name)
        {
            foreach (var entry in Overrides)
                if (entry.Name == name)
                    return entry;
            return null;
        }
    }

    [Serializable]
    public class VariableOverride
    {
        public string Name;
        public SchematicValue Value = new();
    }
}
