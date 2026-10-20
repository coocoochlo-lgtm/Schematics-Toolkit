using System.Collections.Generic;
using UnityEngine;

namespace Remedy.LevelDesigner
{
    /// <summary>
    /// Marks a scene as a blockout. The Level Designer looks for it to know when to show the blockout tools
    /// (the Primitive / Rulers / Schematics toolbar and the Ctrl ruler) in the Scene view, and removes lights and
    /// cameras from the blockout scene in Edit Mode.<br/>
    /// One per scene; the window adds it when it creates a blockout.<br/>
    /// (It has its own file, named after the class, because Unity can only save a MonoBehaviour whose script file
    /// carries its name; it used to share LevelDesignerScenes.cs, and the window repairs markers lost that way.)
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Designer/Blockout Scene")]
    public class BlockoutScene : MonoBehaviour
    {
        /// <summary>Notes shown in the Level Designer window.</summary>
        [TextArea] public string Notes;

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
                || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded
                || UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null)
                return;

            var scene = gameObject.scene;
            var objects = new HashSet<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                    objects.Add(camera.gameObject);
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    objects.Add(light.gameObject);
            }

            bool changed = false;
            foreach (var go in objects)
            {
                if (go == null || go.scene != scene)
                    continue;

                var components = go.GetComponents<Component>();
                bool onlyLightingComponents = go.transform.childCount == 0;
                foreach (var component in components)
                    if (component is not Transform && component is not Camera && component is not Light)
                    {
                        onlyLightingComponents = false;
                        break;
                    }

                if (onlyLightingComponents)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(go);
                    changed = true;
                    continue;
                }

                foreach (var camera in go.GetComponents<Camera>())
                {
                    UnityEditor.Undo.DestroyObjectImmediate(camera);
                    changed = true;
                }
                foreach (var light in go.GetComponents<Light>())
                {
                    UnityEditor.Undo.DestroyObjectImmediate(light);
                    changed = true;
                }
            }

            if (changed)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
#endif
        }
    }
}
