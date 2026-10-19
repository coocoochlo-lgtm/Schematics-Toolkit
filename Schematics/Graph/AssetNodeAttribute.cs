using System;

namespace Remedy.Schematics
{
    /// <summary>
    /// Says a node is what an asset becomes when it's dropped into a Schematic Graph: drop an Audio Clip and get a Play
    /// Sound node with the clip filled in, say. Put it on the node class, once per asset type (or tag set) it handles.
    /// When several nodes fit a dropped asset, the editor asks which.
    /// </summary>
    /// <example>
    /// <code>[AssetNode(typeof(AudioClip), Tags = new[] { "SFX" }, Title = "Play SFX: {0}")]
    /// [AssetNode(typeof(AudioClip), Tags = new[] { "Music" }, Title = "Play Music: {0}")]
    /// public class PlaySound : SchematicActionNode { [Input("Clip")] public AudioClip Clip; ... }</code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class AssetNodeAttribute : Attribute
    {
        public AssetNodeAttribute(Type assetType) => AssetType = assetType;

        /// <summary>The kind of asset (it or anything derived from it).</summary>
        public Type AssetType { get; }

        /// <summary>
        /// Only for assets with one of these tags (all of them with <see cref="RequireAllTags"/>). Editor tags (like
        /// Audio's Music and SFX, which the editor gives assets itself) and tags users make in the Project Manager both
        /// count. Empty: every asset of the type.
        /// </summary>
        public string[] Tags { get; set; }

        /// <summary>The asset needs every one of <see cref="Tags"/>, not just one.</summary>
        public bool RequireAllTags { get; set; }

        /// <summary>The node field the dropped asset goes in. Empty: the first field that can hold it.</summary>
        public string Field { get; set; }

        /// <summary>The new node's title; {0} is the asset's name. Empty: the node's own name.</summary>
        public string Title { get; set; }

        /// <summary>Where it comes in the menu when several nodes fit (lowest first).</summary>
        public int Order { get; set; }
    }
}
