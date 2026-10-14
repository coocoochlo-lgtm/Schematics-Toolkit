using UnityEngine;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// The scene's notes, embedded into the scene when a build is made (the editor's build step adds a hidden object
    /// with this component), so builds see the notes that were in the repository without any file access. You never
    /// add it yourself, and it's never saved into a scene asset.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class StickyNotesSceneData : MonoBehaviour
    {
        [SerializeField] private string _scenePath = "";
        [SerializeField, TextArea] private string _json = "";

        /// <summary>True once any scene in this build carried notes data, meaning the project opted this build in.</summary>
        public static bool AnyEmbedded { get; private set; }

        /// <summary>Called by the editor's build step.</summary>
        public void Set(string scenePath, string json)
        {
            _scenePath = scenePath ?? "";
            _json = json ?? "";
        }

        private void Awake()
        {
            AnyEmbedded = true;
            string scene = !string.IsNullOrEmpty(gameObject.scene.path) ? gameObject.scene.path : _scenePath;
            if (!string.IsNullOrEmpty(_json)) StickyNoteStore.MergeJson(_json, scene);
            StickyNoteStore.Track(scene);
        }
    }
}
