using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// A Project Manager tag: a name and a colour, for one page's kind of asset (its <see cref="Section"/>: Audio,
    /// Schematics...). Assets get any number of tags (stored as asset labels); drag a tag chip onto an asset to tag
    /// it. Editor-only organization: nothing at runtime reads these.
    /// </summary>
    public sealed class AssetTag : ScriptableObject
    {
        public string TagName = "New Tag";
        public Color Color = new(0.35f, 0.65f, 0.9f, 1f);

        [Tooltip("Which Project Manager page the tag belongs to (Audio, Schematics...).")]
        public string Section;
    }
}
