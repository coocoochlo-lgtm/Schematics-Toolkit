using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.StickyNotes.Editor
{
    /// <summary>
    /// Embeds each scene's notes into the scene as it goes into a build (a <see cref="StickyNotesSceneData"/> object), so
    /// builds show the notes from the repository (plus any the room holds right now) without reading files. Which builds
    /// get them is a project setting (development builds only by default). Every scene of an opted-in build gets the
    /// object, even without notes, because its presence is also what makes the in-game view available in release builds.
    /// Nothing is written to the scene assets: this only changes the copy that goes into the build.
    /// </summary>
    internal sealed class StickyNotesBuildProcessor : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // report is null when entering Play Mode: the editor service already has the notes then.
            if (report == null || string.IsNullOrEmpty(scene.path)) return;
            if (!Included(report)) return;

            string json = "";
            StickyNotesEditorService.LoadScene(scene.path);
            if (StickyNoteStore.GetNotes(scene.path).Count > 0)
                json = StickyNoteStore.ToJson(scene.path, pretty: false, includeDeleted: false);
            else
            {
                string path = StickyNotesEditorService.NotesPathFor(scene.path);
                if (File.Exists(path)) json = File.ReadAllText(path);
            }

            var go = new GameObject("Sticky Notes (build data)");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<StickyNotesSceneData>().Set(scene.path, json);
        }

        private static bool Included(BuildReport report)
        {
            switch (StickyNotesSettings.Instance.BuildInclusion)
            {
                case StickyNotesBuildInclusion.AlwaysInclude: return true;
                case StickyNotesBuildInclusion.Never: return false;
                default: return (report.summary.options & BuildOptions.Development) != 0;
            }
        }
    }
}
