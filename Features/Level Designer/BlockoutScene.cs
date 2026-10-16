using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>
    /// Marks a scene as a blockout. The Level Designer looks for it to know when to show the blockout tools
    /// (the Primitive / Rulers / Schematics toolbar and the Ctrl ruler) in the Scene view.<br/>
    /// One per scene; the window adds it when it creates a blockout.<br/>
    /// (It has its own file, named after the class, because Unity can only save a MonoBehaviour whose script file
    /// carries its name; it used to share LevelDesignerScenes.cs, and the window repairs markers lost that way.)
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Designer/Blockout Scene")]
    public class BlockoutScene : MonoBehaviour
    {
        /// <summary>Notes shown in the Level Designer window.</summary>
        [TextArea] public string Notes;
    }
}
