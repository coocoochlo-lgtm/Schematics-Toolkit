using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// What a build needs to find its room, written by the editor at build time (CollabBuildSteps, in the editor's
    /// collaboration features) into Resources/<see cref="ResourceName"/> and removed again after the build. The project
    /// key is the same one editors use (repository + branch + project folder), so a build made from a repo joins the
    /// editors working on that repo and branch with no setup at all.
    /// </summary>
    public sealed class CollabBuildInfo : ScriptableObject
    {
        public const string ResourceName = "SchematicsCollabBuildInfo";

        [Tooltip("Builds without this asset, or with this off, never open a network connection for collaboration.")]
        public bool Enabled = true;

        [Tooltip("The room: a hash of the repository URL, branch and project folder (see CollabProtocol.ProjectKey).")]
        public string ProjectKey;

        [Tooltip("The repository's remote URL, for display.")]
        public string Repository;
        public string Branch;
        [Tooltip("The commit the build was made from (short hash).")]
        public string Commit;
        [Tooltip("The Unity project's folder inside the repository.")]
        public string ProjectFolder;

        [Tooltip("host:port of a relay (copied from the builder's Preferences). Empty finds editors on the local network.")]
        public string Relay;

        [Tooltip("Who made the build and when (shown in the overlay).")]
        public string BuiltBy;
        public string BuiltAt;

        [Tooltip("Show the small collaboration overlay in the corner of the game (with the Receive Live Edits toggle).")]
        public bool ShowOverlay = true;

        [Tooltip("Apply editors' live edits from the start (each playtester can still switch it off in the overlay).")]
        public bool ReceiveLiveEditsByDefault;

        /// <summary>The build's info, or null in builds made without it (and in the editor).</summary>
        public static CollabBuildInfo Load()
        {
            try { return Resources.Load<CollabBuildInfo>(ResourceName); }
            catch (System.Exception e)
            {
                CollabLog.Exception("Reading the collaboration build info failed", e);
                return null;
            }
        }
    }
}
