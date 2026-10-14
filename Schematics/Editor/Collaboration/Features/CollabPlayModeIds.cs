using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Live edits in the editor's Play Mode. The objects of a scene in Play Mode are copies of the edit-mode ones, so
    /// the editors' ids (kept per edit-mode object in <see cref="CollabObjectIds"/>) don't point at them. Just before
    /// Play Mode starts, every known id is written down with where its object sits in the hierarchy (scene path +
    /// sibling indices); in Play Mode, ids are found by that path. The copies are exact at that moment, so this matches
    /// even objects created during the session (which have no GlobalObjectId yet).
    /// </summary>
    [InitializeOnLoad]
    internal static class CollabPlayModeIds
    {
        private const string SessionKey = "Remedy.Collab.PlayModeIds";

        [Serializable]
        private class Entry
        {
            public string id;
            public string scene;
            public string path;   // sibling indices from the scene root, "3/0/2"
        }

        [Serializable]
        private class Map
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static Dictionary<string, Entry> _byId;
        private static Dictionary<GameObject, string> _byObject;
        private static Dictionary<string, string> _byPath;   // scene + "|" + path -> id

        static CollabPlayModeIds()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode) CollabFeatures.Safe(Record);
                if (change == PlayModeStateChange.EnteredEditMode) { _byId = null; _byObject = null; _byPath = null; }
            };
            CollabRuntime.EditorObjectResolver = Resolve;
            CollabRuntime.EditorObjectIdProvider = IdOf;
        }

        private static void Record()
        {
            var map = new Map();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!SceneSync.IsSyncedScene(scene)) continue;
                CollabObjectIds.RegisterScene(scene);
                var roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++) Walk(roots[r], r.ToString(), scene.path, map.entries);
            }
            SessionState.SetString(SessionKey, JsonUtility.ToJson(map));
        }

        private static void Walk(GameObject go, string path, string scene, List<Entry> entries)
        {
            var id = CollabObjectIds.GetId(go, create: false);
            if (id != null) entries.Add(new Entry { id = id, scene = scene, path = path });
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i).gameObject, path + "/" + i, scene, entries);
        }

        private static void Load()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, Entry>();
            _byObject = new Dictionary<GameObject, string>();
            _byPath = new Dictionary<string, string>();
            try
            {
                var map = JsonUtility.FromJson<Map>(SessionState.GetString(SessionKey, "{}"));
                if (map?.entries != null)
                    foreach (var e in map.entries)
                        if (!string.IsNullOrEmpty(e?.id))
                        {
                            _byId[e.id] = e;
                            _byPath[e.scene + "|" + e.path] = e.id;
                        }
            }
            catch (Exception e) { CollabLog.Warning("Couldn't read the Play Mode id map; live edits won't reach this Play Mode session (" + e.Message + ")."); }
        }

        private static GameObject Resolve(string id)
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(id)) return null;
            Load();
            if (!_byId.TryGetValue(id, out var entry)) return null;
            var scene = SceneManager.GetSceneByPath(entry.scene);
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var parts = entry.path.Split('/');
            var roots = scene.GetRootGameObjects();
            if (!int.TryParse(parts[0], out int rootIndex) || rootIndex < 0 || rootIndex >= roots.Length) return null;
            var t = roots[rootIndex].transform;
            for (int i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int child) || child < 0 || child >= t.childCount) return null;
                t = t.GetChild(child);
            }
            _byObject[t.gameObject] = id;
            return t.gameObject;
        }

        private static string IdOf(GameObject go)
        {
            if (go == null) return null;
            if (!Application.isPlaying) return CollabObjectIds.GetId(go, create: false);
            Load();
            if (_byObject.TryGetValue(go, out var known)) return known;
            // Build the object's path and look it up.
            var path = new StringBuilder();
            for (var t = go.transform; t != null; t = t.parent)
                path.Insert(0, (path.Length > 0 ? "/" : "") ).Insert(0, t.GetSiblingIndex().ToString());
            if (!_byPath.TryGetValue(go.scene.path + "|" + path, out var id)) return null;
            _byObject[go] = id;
            return id;
        }
    }
}
