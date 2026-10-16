using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Marks a temporary stand-in for an object a teammate placed whose asset hasn't reached this machine yet (it's
    /// still being pushed/pulled through git). Lives only on never-saved GameObjects (HideFlags.DontSave), and remembers
    /// the placed object's snapshot so <see cref="CollabPlaceholders"/> can build the real thing when the asset arrives.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class CollabPlaceholder : MonoBehaviour
    {
        /// <summary>The missing prefab/model asset's GUID.</summary>
        public string AssetGuid;
        /// <summary>The asset's name, for the label.</summary>
        public string AssetName;
        /// <summary>Who placed it (peer id) and their colour.</summary>
        public string FromPeerId;
        public Color Color = Color.white;
        /// <summary>JSON of a SceneMsg whose nodes are the placed object's snapshot (index 0 = this object).</summary>
        public string SnapshotJson;
    }
}
