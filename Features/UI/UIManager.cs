using Remedy.Framework;
//using SaintsField;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.UI
{
    public class UIManager : Singleton<UIManager>
    {
        [SerializeField]
        private Camera _camera;
        public static Camera Camera => Instance._camera;

        public List<BehaviorInstance> Components = new();
        //[Layer]
        public int UILayer;
        public float BaseDepth = 50f;

        void OnEnable()
        {
            Components = new();
        }

        /// <summary>
        /// Adds a UI Component instance to the list of managed Components and places it on the UI layer.
        /// </summary>
        public static void AddUIComponent(BehaviorInstance component, int layer)
        {
            component.GameObject.layer = Instance.UILayer;
            Vector3 position = component.Transform.position;
            position.z = Instance.BaseDepth + layer;
            component.Transform.position = position;
            Instance.Components.Add(component);
        }

        /// <summary>
        /// Removes a UI Component instance from the list of managed Components.
        /// </summary>
        public static void RemoveUIComponent(BehaviorInstance component, int layer)
        {
            Instance.Components.Remove(component);

            component.GameObject.layer = Instance.UILayer;
            Vector3 position = component.Transform.position;
            position.z = Instance.BaseDepth + layer;
            component.Transform.position = position;
        }
    }
}