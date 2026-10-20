#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

/// <summary>
/// Loads a scene additively, in Edit Mode as well as at runtime, for splitting levels into sub-scenes.
/// Stays a MonoBehaviour: it is a level-authoring tool that has to run in Edit Mode, where Schematic Behaviors don't.
/// </summary>
[ExecuteAlways]
public class SceneReference : MonoBehaviour
{
#if UNITY_EDITOR
    // SceneAsset only exists in the editor; builds load by the path saved below.
    public SceneAsset sceneAsset;
#endif
    public bool loadOnStart = true;

    [HideInInspector]
    public string scenePath;

    private Scene loadedScene;

    void Awake()
    {
        if (Application.isPlaying && loadOnStart)
            LoadScene();
#if UNITY_EDITOR
        else if (!Application.isPlaying && loadOnStart)
            EditorApplication.delayCall += () => LoadSceneInEditor();
#endif
    }

    public void LoadScene()
    {
#if UNITY_EDITOR
        if (sceneAsset != null)
            scenePath = AssetDatabase.GetAssetPath(sceneAsset);
#endif
        if (string.IsNullOrEmpty(scenePath)) return;

        if (!SceneManager.GetSceneByPath(scenePath).isLoaded)
        {
            SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive).completed += (op) =>
            {
                loadedScene = SceneManager.GetSceneByPath(scenePath);
            };
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keep the path current so builds, which can't read the SceneAsset, still know what to load.
        if (sceneAsset != null)
            scenePath = AssetDatabase.GetAssetPath(sceneAsset);
    }

    private void LoadSceneInEditor()
    {
        if (sceneAsset == null) return;

        scenePath = AssetDatabase.GetAssetPath(sceneAsset);
        if (string.IsNullOrEmpty(scenePath)) return;

        if (!EditorSceneManager.GetSceneByPath(scenePath).isLoaded)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }
    }

    void OnDisable()
    {
        if (!Application.isPlaying && sceneAsset != null && !string.IsNullOrEmpty(scenePath))
        {
            var scene = EditorSceneManager.GetSceneByPath(scenePath);
            if (scene.IsValid() && scene.isDirty)
                EditorSceneManager.SaveScene(scene);
        }
    }
#endif
}