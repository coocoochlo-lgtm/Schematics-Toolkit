using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Maps the editors' object ids to the objects of a built scene. Editors name scene objects "g:" + GlobalObjectId,
    /// which a build can't compute (GlobalObjectId is editor-only), so at build time the editor adds one hidden
    /// GameObject with this component to every scene it builds, listing each saved GameObject's id and a reference to
    /// it. In the game, live edits look objects up here (<see cref="Find"/>). Nothing is added to the scene files
    /// themselves: only the copies that go into the build carry it.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    public sealed class CollabSceneIds : MonoBehaviour
    {
        /// <summary>Name of the hidden GameObject that carries the map.</summary>
        public const string HolderName = "__SchematicsCollabSceneIds";

        public string ScenePath;

        /// <summary>
        /// The build's <see cref="CollabBuildInfo"/> as JSON (written at build time). Normally the build reads it from
        /// Resources before the first scene loads; this copy is the fallback if that asset is missing from the build.
        /// </summary>
        public string BuildInfoJson;
        public List<string> Ids = new List<string>();
        public List<GameObject> Objects = new List<GameObject>();

        private static readonly Dictionary<string, GameObject> _byId = new Dictionary<string, GameObject>();
        private static readonly Dictionary<GameObject, string> _byObject = new Dictionary<GameObject, string>();
        private static readonly List<CollabSceneIds> _maps = new List<CollabSceneIds>();

        private void Awake()
        {
            if (!Application.isEditor && !CollabRuntime.IsAvailable && !string.IsNullOrEmpty(BuildInfoJson))
                CollabRuntime.BootFromScene(BuildInfoJson);
            _maps.Add(this);
            int count = Mathf.Min(Ids.Count, Objects.Count);
            for (int i = 0; i < count; i++)
                if (!string.IsNullOrEmpty(Ids[i]) && Objects[i] != null)
                {
                    _byId[Ids[i]] = Objects[i];
                    _byObject[Objects[i]] = Ids[i];
                }
        }

        private void OnDestroy()
        {
            _maps.Remove(this);
            int count = Mathf.Min(Ids.Count, Objects.Count);
            for (int i = 0; i < count; i++)
            {
                if (!string.IsNullOrEmpty(Ids[i]) && _byId.TryGetValue(Ids[i], out var go) && (go == null || go == Objects[i]))
                    _byId.Remove(Ids[i]);
                if (Objects[i] != null) _byObject.Remove(Objects[i]);
            }
        }

        /// <summary>The live object an editor id names, in any loaded built scene, or null.</summary>
        public static GameObject Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var go) && go != null ? go : null;
        }

        /// <summary>The editor id ("g:" + GlobalObjectId) of a live object of a built scene, or null.</summary>
        public static string IdOf(GameObject go)
        {
            if (go == null) return null;
            return _byObject.TryGetValue(go, out var id) ? id : null;
        }

        /// <summary>How many objects of loaded scenes can receive live edits (for the overlay).</summary>
        public static int Count => _byId.Count;

        /// <summary>Paths of the loaded scenes that carry a map.</summary>
        public static IEnumerable<string> ScenePaths
        {
            get
            {
                foreach (var map in _maps)
                    if (map != null) yield return map.ScenePath;
            }
        }
    }
}
