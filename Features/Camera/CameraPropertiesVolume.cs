using UnityEngine;

namespace Remedy.Cameras
{
    /// <summary>
    /// A trigger volume that changes camera settings for Camera Operators inside it.
    /// Operators read the values with the volume's instance, e.g. <c>volume.Zoom[volumeInstance]</c>.
    /// </summary>
    [SchematicBehaviorPath("Cameras/Camera Properties Volume")]
    [RequireComponent(typeof(BoxCollider))]
    public class CameraPropertiesVolume : SchematicBehavior
    {
        public readonly VariableHandle<bool> ChangeZoom = false;
        public readonly VariableHandle<float> Zoom = 15f;
        public readonly VariableHandle<bool> ChangeFOV = false;
        public readonly VariableHandle<float> FOV = 60f;
        public readonly VariableHandle<bool> ChangeRestingAngle = false;
        public readonly VariableHandle<float> RestingAngle = 60f;
        public readonly VariableHandle<bool> UseReferenceCam = false;
        public readonly VariableHandle<Transform> ReferenceCamera = (Transform)null;
        [Tooltip("A mask to applied to copying the position of the Reference Camera")]
        public readonly VariableHandle<Vector3Bool> ReferenceCamPositionMask = Vector3Bool.False;
        [Tooltip("A mask applied to euler angle copying of the reference Camera")]
        public readonly VariableHandle<Vector3Bool> ReferenceCamAngleMask = Vector3Bool.False;
        [Tooltip("The Speed at which the Player's Camera is pulled to the Reference Camera")]
        public readonly VariableHandle<float> ReferenceCameraSpeed = 0f;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            if (context.GameObject.TryGetComponent(out BoxCollider collider))
                collider.isTrigger = true;
        }

        protected override void OnCreate(BehaviorInstance instance)
        {
            var collider = instance.GetComponent<BoxCollider>();
            if (collider != null)
                collider.isTrigger = true;
        }
    }
}
