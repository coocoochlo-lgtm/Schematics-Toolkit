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
        [Tooltip("The sticky-note paper image. Referencing it here is what puts it into the build.")]
        [SerializeField] private Texture2D _paper;
        [SerializeField] private bool _autoSnapshot = true;

        /// <summary>True once any scene in this build carried notes data, meaning the project opted this build in.</summary>
        public static bool AnyEmbedded { get; private set; }

        /// <summary>Called by the editor's build step.</summary>
        public void Set(string scenePath, string json)
        {
            _scenePath = scenePath ?? "";
            _json = json ?? "";
        }

        /// <summary>Called by the editor's build step: the paper image and the project's snapshot setting.</summary>
        public void SetOptions(Texture2D paper, bool autoSnapshotForWarningsAndErrors)
        {
            _paper = paper;
            _autoSnapshot = autoSnapshotForWarningsAndErrors;
        }

        private void Awake()
        {
            AnyEmbedded = true;
            if (_paper != null && StickyNoteStyle.PaperTexture == null) StickyNoteStyle.PaperTexture = _paper;
            if (!Application.isEditor) StickyNotePerf.AutoForWarningsAndErrors = _autoSnapshot;
            string scene = !string.IsNullOrEmpty(gameObject.scene.path) ? gameObject.scene.path : _scenePath;
            if (!string.IsNullOrEmpty(_json)) StickyNoteStore.MergeJson(_json, scene);
            StickyNoteStore.Track(scene);
        }
    }
}
