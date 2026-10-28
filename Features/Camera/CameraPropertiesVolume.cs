using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Cameras
{
    /// <summary>
    /// A box (its Box Collider's) that changes camera settings for Camera Operators whose target is inside it.
    /// Operators read the values with the volume's instance, e.g. <c>volume.Zoom[volumeInstance]</c>.
    /// </summary>
    /// <remarks>
    /// Being inside is worked out from the box itself each frame (<see cref="At"/>), not from physics triggers, so
    /// volumes work whichever physics engine the scene uses (Box3D). Overlapping volumes: the smallest wins.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Camera Properties Volume"", ""kind"": ""behavior"", ""menu"": ""Cameras/Camera Properties Volume"", ""description"": ""A trigger volume that changes camera settings for Camera Operators inside it. Operators read the values with the volume's instance, e.g. volume.Zoom[volumeInstance]."", ""variables"": [""ChangeZoom"", ""Zoom"", ""ChangeFOV"", ""FOV"", ""ChangeRestingAngle"", ""RestingAngle"", ""UseReferenceCam"", ""ReferenceCamera"", ""ReferenceCamPositionMask"", ""ReferenceCamAngleMask"", ""ReferenceCameraSpeed""], ""use_for"": [""changing camera zoom or angle in specific areas""]}")]
    [SchematicBehaviorPath("Cameras/Camera Properties Volume")]
    [RequireComponent(typeof(BoxCollider))]
    public class CameraPropertiesVolume : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""ChangeZoom"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Change how far away the camera sits while inside the volume.""}")]
        public readonly VariableHandle<bool> ChangeZoom = false;
        [SchematicDoc(@"{""name"": ""Zoom"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""float"", ""default"": ""15f"", ""description"": ""How far away the camera sits inside the volume (meters), when Change Zoom is on.""}")]
        public readonly VariableHandle<float> Zoom = 15f;
        [SchematicDoc(@"{""name"": ""ChangeFOV"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Change the camera's field of view while inside the volume.""}")]
        public readonly VariableHandle<bool> ChangeFOV = false;
        [SchematicDoc(@"{""name"": ""FOV"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""float"", ""default"": ""60f"", ""description"": ""The field of view inside the volume (degrees: lower zooms in, higher is wider), when Change FOV is on.""}")]
        public readonly VariableHandle<float> FOV = 60f;
        [SchematicDoc(@"{""name"": ""ChangeRestingAngle"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Change the angle the camera settles back to while inside the volume.""}")]
        public readonly VariableHandle<bool> ChangeRestingAngle = false;
        [SchematicDoc(@"{""name"": ""RestingAngle"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""float"", ""default"": ""60f"", ""description"": ""The angle the camera settles to inside the volume (degrees, looking down), when Change Resting Angle is on.""}")]
        public readonly VariableHandle<float> RestingAngle = 60f;
        [SchematicDoc(@"{""name"": ""UseReferenceCam"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Line the camera up with a placed reference camera while inside the volume (for a fixed, framed shot).""}")]
        public readonly VariableHandle<bool> UseReferenceCam = false;
        [SchematicDoc(@"{""name"": ""ReferenceCamera"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""The placed camera to match while Use Reference Cam is on.""}")]
        public readonly VariableHandle<Transform> ReferenceCamera = (Transform)null;
        [SchematicDoc(@"{""name"": ""ReferenceCamPositionMask"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""Vector3Bool"", ""default"": ""Vector3Bool.False"", ""description"": ""A mask to applied to copying the position of the Reference Camera""}")]
        [Tooltip("A mask to applied to copying the position of the Reference Camera")]
        public readonly VariableHandle<Vector3Bool> ReferenceCamPositionMask = Vector3Bool.False;
        [SchematicDoc(@"{""name"": ""ReferenceCamAngleMask"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""Vector3Bool"", ""default"": ""Vector3Bool.False"", ""description"": ""A mask applied to euler angle copying of the reference Camera""}")]
        [Tooltip("A mask applied to euler angle copying of the reference Camera")]
        public readonly VariableHandle<Vector3Bool> ReferenceCamAngleMask = Vector3Bool.False;
        [SchematicDoc(@"{""name"": ""ReferenceCameraSpeed"", ""kind"": ""variable"", ""behavior"": ""Camera Properties Volume"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""The Speed at which the Player's Camera is pulled to the Reference Camera""}")]
        [Tooltip("The Speed at which the Player's Camera is pulled to the Reference Camera")]
        public readonly VariableHandle<float> ReferenceCameraSpeed = 0f;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            if (context.GameObject.TryGetComponent(out BoxCollider collider))
                collider.isTrigger = true;
        }

        private static readonly List<BehaviorInstance> _active = new();

        protected override void OnEnable()
        {
            if (!_active.Contains(Self))
                _active.Add(Self);
        }

        protected override void OnDisable() => _active.Remove(Self);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _active.Clear();

        /// <summary>The volume a world position is inside (the smallest, where they overlap), or null.</summary>
        public static BehaviorInstance At(Vector3 position)
        {
            BehaviorInstance best = null;
            float bestSize = float.MaxValue;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var instance = _active[i];
                if (instance == null || !instance.IsSpawned)
                {
                    _active.RemoveAt(i);
                    continue;
                }
                if (!instance.GameObject.TryGetComponent(out BoxCollider box) || !box.enabled)
                    continue;
                var local = box.transform.InverseTransformPoint(position) - box.center;
                var half = box.size * 0.5f;
                if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
                    continue;
                var scale = box.transform.lossyScale;
                float size = Mathf.Abs(box.size.x * scale.x * box.size.y * scale.y * box.size.z * scale.z);
                if (size < bestSize)
                {
                    best = instance;
                    bestSize = size;
                }
            }
            return best;
        }

        protected override void OnCreate()
        {
            var collider = Self.GetComponent<BoxCollider>();
            if (collider != null)
                collider.isTrigger = true;
        }
    }
}
