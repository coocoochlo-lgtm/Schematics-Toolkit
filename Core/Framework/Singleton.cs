using System;
using UnityEngine;

namespace Remedy.Framework
{
    public class SingletonBase : MonoBehaviour
    { }

    /// <summary>
    /// A simple Singleton MonoBehaviour which can be accessed statically via the Instance property.
    /// </summary>
    /// <typeparam name="TSingleton">This Singleton Class (for accessing the Singleton statically).</typeparam>
    public class Singleton<TSingleton> : SingletonBase where TSingleton : Singleton<TSingleton>
    {
        private static GameObject _instanceObject;
        /// <summary>
        /// A static property that returns the game object that contains the singleton component.
        /// If the game object does not exist, it will try to load it from the resources or create a new one.
        /// The game object will not be destroyed when loading a new scene.
        /// </summary>
        public static GameObject InstanceObject
        {
            get
            {
                if(_instanceObject == null)
                {
                    //Attempts to get the Singleton from the Scene first, as the user might want to modify the properties of the Singleton in the Inspector
                    var _inScene = UnityEngine.Object.FindFirstObjectByType<TSingleton>();

                    var hasKeepInSceneAttribute = Attribute.IsDefined(typeof(TSingleton), typeof(KeepInSceneAttribute));

                    if (_inScene == null)
                        _instanceObject = new GameObject(typeof(TSingleton).Name);
                    else
                        _instanceObject = _inScene.gameObject;

                    // Only prevent destruction if the attribute is NOT present
                    if (Application.isPlaying && !hasKeepInSceneAttribute)
                        DontDestroyOnLoad(_instanceObject);

                    if (!_instanceObject.scene.IsValid() || _instanceObject.hideFlags != HideFlags.None)
                        Destroy(_instanceObject);

                    _instanceObject.hideFlags = HideFlags.None;
                }

                return _instanceObject;
            }
        }

        private static TSingleton _instance;
        /// <summary>
        /// A static property that returns the singleton instance.
        /// If the instance does not exist, it will try to get it from the game object or add it as a component.
        /// </summary>
        public static TSingleton Instance
        {
            get
            {
                var instanceObject = InstanceObject;
                if (instanceObject.hideFlags != HideFlags.None)
                {
                    Destroy(_instanceObject);
                }

                if(_instance == null)
                {
                    var inSceneInstance = UnityEngine.Object.FindFirstObjectByType<TSingleton>();
                    if (inSceneInstance != null && inSceneInstance != _instance)
                    {
                        _instance = inSceneInstance;
                        // Copy values from the scene instance to the persistent singleton
                        //_instance = inSceneInstance.CopyComponent(InstanceObject);

                        // Destroy the scene instance as it's no longer needed
                        if (inSceneInstance.gameObject != _instanceObject)
                        {
                            Destroy(inSceneInstance.gameObject);
                        }
                    }
                }

                _instance ??= instanceObject.GetComponent<TSingleton>()
                                            ?? instanceObject.AddComponent<TSingleton>();

                return _instance;
            }
        }

        public static Transform Transform => Instance.transform;

        public bool EnableLogging = false;

        public static void Initialize()
        {
            var _ = Instance;
        }

        public static void Enable()
        {
            Instance.enabled = true;
        }

        public static void Disable()
        {
            Instance.enabled = false;
        }
        protected virtual void Awake()
        {
            if(_instance == null)
                _instance = (TSingleton)this;
        }

        protected virtual void OnDestroy()
        {
            if (this == _instance)
                _instance = null;
            if (gameObject == _instanceObject)
                _instanceObject = null;
            Debug.LogWarning("Singleton Destroyed: " + this.GetType());
        }
    }

    [ExecuteAlways]
    public class EditorSingleton<T> : Singleton<T> where T : Singleton<T>
    {
    }
}