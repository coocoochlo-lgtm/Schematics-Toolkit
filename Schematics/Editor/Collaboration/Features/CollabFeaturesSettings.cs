using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Project-wide settings for the collaboration features (Project Settings > Schematics > Collaboration Features).
    /// They live in ProjectSettings/ so every teammate gets the same avatar and the same sync behaviour from git.
    /// </summary>
    [FilePath("ProjectSettings/SchematicsCollaborationFeatures.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class CollabFeaturesSettings : ScriptableSingleton<CollabFeaturesSettings>
    {
        [Tooltip("The 3D model drawn where each teammate's Scene view camera is. Leave empty for the built-in placeholder (a head and a camera frustum). " +
                 "It's tinted with the teammate's colour where its shader has a _BaseColor or _Color property.")]
        public GameObject AvatarPrefab;

        [Tooltip("Uniform scale applied to the avatar model.")]
        public float AvatarScale = 1f;

        [Tooltip("Send scene edits to teammates as they happen.")]
        public bool LiveSceneSync = true;

        [Tooltip("Save and broadcast Schematic Graph edits shortly after they happen.")]
        public bool LiveGraphSync = true;

        [Tooltip("Show other people's selections and lock what they have selected.")]
        public bool SelectionLocks = true;

        [Tooltip("Remote edits go into your Undo history (in their own Undo group). Off by default, so Ctrl+Z only ever undoes your own work.")]
        public bool RecordRemoteEditsInUndo = false;

        [Tooltip("How often the host saves and commits scenes that teammates have edited (minutes). 0 = only when the scene closes or the session ends.")]
        public float CommitIntervalMinutes = 10f;

        [Tooltip("How many times per second your camera, cursor and selection are shared.")]
        [Range(2, 20)] public float PresenceRate = 10f;

        [Tooltip("Play a short sound when a chat message arrives while the Team window isn't focused.")]
        public bool ChatSound = false;

        /// <summary>Saves the settings file after an edit.</summary>
        public void SaveSettings() => Save(true);
    }

    /// <summary>Draws <see cref="CollabFeaturesSettings"/> in Project Settings.</summary>
    internal static class CollabFeaturesSettingsProvider
    {
        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Schematics/Collaboration Features", SettingsScope.Project)
            {
                label = "Collaboration Features",
                guiHandler = _ =>
                {
                    var settings = CollabFeaturesSettings.instance;
                    settings.hideFlags &= ~HideFlags.NotEditable;
                    var so = new SerializedObject(settings);
                    so.Update();
                    EditorGUI.BeginChangeCheck();
                    var it = so.GetIterator();
                    bool enter = true;
                    while (it.NextVisible(enter))
                    {
                        enter = false;
                        if (it.propertyPath == "m_Script") continue;
                        EditorGUILayout.PropertyField(it, true);
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        settings.SaveSettings();
                    }
                },
                keywords = new[] { "collaboration", "avatar", "team", "chat", "multiplayer" },
            };
        }
    }
}
