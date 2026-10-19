using System;

namespace Remedy.Schematics
{
    /// <summary>
    /// Marks a kind of Schematic (a Schematic Graph type) so the Project Manager lists it as its own group in the
    /// Schematics sidebar: Objects, Singletons, Timelines, and any type added later (an audio graph, say). A graph type
    /// without this attribute belongs to the nearest base type that has one.
    /// </summary>
    /// <example>
    /// <code>[SchematicType("Audio Graphs", Order = 30, Icon = "AudioMixerController Icon")]
    /// public class AudioGraph : SchematicGraph { }</code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SchematicTypeAttribute : Attribute
    {
        public SchematicTypeAttribute(string name) => Name = name;

        /// <summary>The group's name in the sidebar ("Singletons").</summary>
        public string Name { get; }

        /// <summary>Groups are listed lowest first.</summary>
        public int Order { get; set; }

        /// <summary>A built-in editor icon name for the group (optional).</summary>
        public string Icon { get; set; }

        /// <summary>
        /// The group lists prefabs with a Schematic Object (whose graph is of this type) rather than graph assets. Used
        /// by Objects, whose graphs live with their prefabs.
        /// </summary>
        public bool ListsPrefabs { get; set; }

        /// <summary>Where new ones go when no folder is selected. Empty: Assets/Content/Schematics/{Name}.</summary>
        public string DefaultFolder { get; set; }

        /// <summary>The file name new ones start with. Empty: "New {Name without a trailing s}".</summary>
        public string NewAssetName { get; set; }
    }

    /// <summary>
    /// Marks an editor method that makes a new Schematic of a type, for types that need more than an empty asset (a
    /// Timeline Graph also makes its Timeline). The method is static, takes the folder ("Assets/...") and returns the
    /// new asset: <c>static UnityEngine.Object Create(string folder)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SchematicTypeCreatorAttribute : Attribute
    {
        public SchematicTypeCreatorAttribute(Type graphType) => GraphType = graphType;

        /// <summary>The graph type the method makes.</summary>
        public Type GraphType { get; }
    }
}
