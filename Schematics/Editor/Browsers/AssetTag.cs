using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// A Project Manager tag: a name and a colour, for one page's kind of asset (its <see cref="Section"/>: Audio,
    /// Schematics...), and the list of assets that have it: the tag is the key, its <see cref="AssetGuids"/> the
    /// value. An asset can be in any number of tags; drag a tag chip onto an asset to tag it. Editor-only
    /// organization: nothing at runtime reads these.
    /// </summary>
    public sealed class AssetTag : ScriptableObject
    {
        public string TagName = "New Tag";
        public Color Color = new(0.35f, 0.65f, 0.9f, 1f);

        [Tooltip("Which Project Manager page the tag belongs to (Audio, Schematics...).")]
        public string Section;

        [Tooltip("The assets with this tag, by GUID (so moving or renaming them keeps their tag).")]
        public System.Collections.Generic.List<string> AssetGuids = new();
    }
}
