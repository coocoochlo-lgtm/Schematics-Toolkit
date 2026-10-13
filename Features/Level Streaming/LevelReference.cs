using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// Points at an additive level: a scene added on top of the ones already open, or a level prefab placed into the
    /// world. Use it as an Inspector field (scenes get a scene picker) or make one in code with
    /// <see cref="FromScene"/>, <see cref="FromBuildIndex"/> or <see cref="FromPrefab"/>.
    /// </summary>
    /// <remarks>
    /// Scenes are kept as a path (plus the scene asset's GUID, so the editor can follow a renamed or moved scene),
    /// never as a <c>SceneAsset</c>, which only exists in the editor. A bare scene name works too: it's looked up in
    /// the Build Profile's scene list when loading.
    /// </remarks>
    [Serializable]
    public sealed class LevelReference
    {
        public enum LevelKind
        {
            /// <summary>A scene, loaded additively.</summary>
            Scene,
            /// <summary>A prefab, instantiated as a whole level.</summary>
            Prefab,
        }

        [Tooltip("Scene: added on top of the open scenes. Prefab: instantiated into the active scene.")]
        public LevelKind Kind = LevelKind.Scene;

        [Tooltip("The scene's path (Assets/.../Name.unity), or just its name if it's in the Build Profile's scene list.")]
        public string ScenePath = "";

        // Lets the editor follow the scene when it's renamed or moved. Not used at runtime.
        [SerializeField, HideInInspector]
        private string _sceneGuid = "";

        [Tooltip("The level prefab (for the Prefab kind).")]
        public GameObject Prefab;

        /// <summary>A reference to a scene, by its path (Assets/.../Name.unity) or by its name.</summary>
        public static LevelReference FromScene(string nameOrPath) => new LevelReference { Kind = LevelKind.Scene, ScenePath = nameOrPath ?? "" };

        /// <summary>A reference to a scene by its index in the Build Profile's scene list.</summary>
        public static LevelReference FromBuildIndex(int buildIndex) => FromScene(SceneUtility.GetScenePathByBuildIndex(buildIndex));

        /// <summary>A reference to a level prefab.</summary>
        public static LevelReference FromPrefab(GameObject prefab) => new LevelReference { Kind = LevelKind.Prefab, Prefab = prefab };

        /// <summary>Whether it points at something (a scene path or name, or a prefab).</summary>
        public bool IsValid => Kind == LevelKind.Prefab ? Prefab != null : !string.IsNullOrEmpty(ScenePath);

        /// <summary>The level's name: the scene's file name, or the prefab's name. Sent with On Level Start / Exit.</summary>
        public string Name
        {
            get
            {
                if (Kind == LevelKind.Prefab)
                    return Prefab != null ? Prefab.name : "";
                return string.IsNullOrEmpty(ScenePath) ? "" : Path.GetFileNameWithoutExtension(ScenePath);
            }
        }

        /// <summary>
        /// Identifies the level, so asking for the same level twice gives back the same loaded copy. Two references
        /// to the same scene (one by name, one by path) count as the same level when the name can be found in the
        /// Build Profile's scene list.
        /// </summary>
        public string Key
        {
            get
            {
                //if (Kind == LevelKind.Prefab)
                //    return Prefab != null ? "prefab:" + Prefab.GetInstanceID() : "";
                return "scene:" + ResolveScenePath().ToLowerInvariant();
            }
        }

        /// <summary>
        /// The scene's full path when it can be worked out: as written when it's already a path, else the first scene
        /// of that name in the Build Profile's scene list, else the name as written.
        /// </summary>
        public string ResolveScenePath()
        {
            if (string.IsNullOrEmpty(ScenePath))
                return "";
            if (IsPath(ScenePath))
                return ScenePath;
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), ScenePath, StringComparison.OrdinalIgnoreCase))
                    return path;
            }
            return ScenePath;
        }

        /// <summary>Whether a scene is the one this points at (by path, or by name when only a name is known).</summary>
        public bool Matches(Scene scene)
        {
            if (Kind != LevelKind.Scene || !scene.IsValid())
                return false;
            string path = ResolveScenePath();
            if (IsPath(path))
                return string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase);
            return string.Equals(scene.name, path, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a scene string is a path (Assets/.../Name.unity) rather than a bare name.</summary>
        public static bool IsPath(string value) => !string.IsNullOrEmpty(value) && (value.IndexOf('/') >= 0 || value.EndsWith(".unity", StringComparison.OrdinalIgnoreCase));

        public override string ToString() => string.IsNullOrEmpty(Name) ? "(no level)" : Name;
    }
}
