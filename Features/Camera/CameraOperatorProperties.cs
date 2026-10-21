using UnityEngine;

namespace Remedy.Cameras
{
    [CreateAssetMenu(menuName = "Remedy Toolkit/Camera/Operator Properties")]
    public class CameraOperatorProperties : ScriptableObject
    {
        [Tooltip("Time it takes to transition to the Operator from the last one")]
        public float TransitionTime = 2f;
        public float XAxisSpeed = 3f;
        public float YAxisSpeed = 2f;
        public float SmoothSpeed = 10f;
        [Tooltip("Whether Camera Properties Volumes (zoom, resting angle, FOV, Reference Cameras) affect this Operator. Operators without Properties always use them.")]
        public bool UseVolumes = true;
    }
}