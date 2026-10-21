using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Cameras
{
    /// <summary>
    /// Drives the game's camera: it follows the active Camera Operator's rig camera, blending over the Operator's
    /// Transition Time when the active Operator changes. Inside a Camera Properties Volume that uses a Reference
    /// Camera, it's pulled toward that camera instead (by the volume's Reference Camera Speed; 0 snaps), and blends back
    /// to the Operator on leaving it. Volumes can also change its field of view.
    /// </summary>
    /// <remarks>
    /// The camera is <see cref="_cameraTransform"/> when set, otherwise the scene's Main Camera (or its first enabled
    /// camera that isn't an Operator's rig camera).
    /// </remarks>
    [ExecuteAlways]
    public class CameraManager : Singleton<CameraManager>
    {
        /// <summary>The active Camera Operator instance.</summary>
        public static BehaviorInstance CurrentOperator;

        [Tooltip("The camera the game renders with. Empty: the scene's Main Camera.")]
        public Transform _cameraTransform;

        [Tooltip("Seconds to blend back to the Operator after a Reference Camera, when the Operator has no Properties.")]
        public float ReferenceReturnTime = 1f;
        [Tooltip("How quickly the field of view follows volumes that change it.")]
        public float FovSpeed = 5f;

        private Camera _camera;
        private float _defaultFov = -1f;
        private float _nextCameraSearch;

        /// <summary>The game's camera (null until there is one).</summary>
        public static Camera Camera
        {
            get
            {
                var cameraTransform = CameraTransform;
                if (cameraTransform == null)
                    return null;
                var manager = Instance;
                if (manager._camera == null || manager._camera.transform != cameraTransform)
                    manager._camera = cameraTransform.GetComponent<Camera>();
                return manager._camera;
            }
        }

        /// <summary>The game's camera's Transform: the one assigned, else the scene's main camera.</summary>
        public static Transform CameraTransform
        {
            get
            {
                var manager = Instance;
                // (Not "??": a destroyed or unassigned Unity object isn't C# null.)
                if (manager._cameraTransform == null && Time.unscaledTime >= manager._nextCameraSearch)
                {
                    manager._nextCameraSearch = Time.unscaledTime + 0.5f;
                    var found = manager.FindSceneCamera();
                    if (found != null)
                    {
                        manager._cameraTransform = found.transform;
                        CameraSubsystem.SetMainCamera(found);
                    }
                }
                return manager._cameraTransform != null ? manager._cameraTransform : null;
            }
        }

        // Every enabled Camera Operator, kept in step with the active one.
        private readonly List<BehaviorInstance> _operators = new();

        private float _blendTime, _blendDuration;   // blending toward the Operator's camera
        private bool _followingReference;

        void Start()
        {
            if (Camera != null)
                CameraSubsystem.SetMainCamera(Camera);
        }

        // The Main Camera, else the first enabled camera that isn't an Operator's rig camera (those are disabled).
        private Camera FindSceneCamera()
        {
            var main = UnityEngine.Camera.main;
            if (main != null && !IsOperatorCamera(main.transform))
                return main;
            foreach (var camera in UnityEngine.Camera.allCameras)
                if (!IsOperatorCamera(camera.transform))
                    return camera;
            return null;
        }

        private bool IsOperatorCamera(Transform camera)
        {
            foreach (var op in _operators)
                if (op.IsSpawned && ((CameraOperator)op.Behavior).Camera[op] == camera)
                    return true;
            return false;
        }

        // After the Operators have moved their rigs (their OnUpdate), so the camera is never a frame behind.
        private void LateUpdate()
        {
            if (!Application.isPlaying)
                return;

            UpdateVolumes();

            if (CurrentOperator == null || !CurrentOperator.IsSpawned)
            {
                CurrentOperator = null;
                foreach (var candidate in _operators)
                    if (candidate.IsSpawned) { SetCameraOperator(candidate); break; }
                if (CurrentOperator == null)
                    return;
            }

            var cameraTransform = CameraTransform;
            var op = (CameraOperator)CurrentOperator.Behavior;
            var operatorCamera = op.Camera[CurrentOperator];
            var properties = op.Properties[CurrentOperator];
            if (operatorCamera == null || cameraTransform == null)
                return;

            float dt = Time.deltaTime;
            var volumeInstance = op.CurrentVolume[CurrentOperator];
            var volume = volumeInstance != null && volumeInstance.IsSpawned ? (CameraPropertiesVolume)volumeInstance.Behavior : null;
            bool useVolumes = properties == null || properties.UseVolumes;
            if (!useVolumes)
                volume = null;
            var referenceCamera = volume != null && volume.UseReferenceCam[volumeInstance] ? volume.ReferenceCamera[volumeInstance] : null;

            if (referenceCamera != null)
            {
                // Pulled toward the volume's Reference Camera, on the axes its masks pick.
                var angleMask = volume.ReferenceCamAngleMask[volumeInstance];
                var positionMask = volume.ReferenceCamPositionMask[volumeInstance];
                float speed = volume.ReferenceCameraSpeed[volumeInstance];
                float t = speed > 0f ? 1f - Mathf.Exp(-speed * dt) : 1f;

                var goalEuler = referenceCamera.eulerAngles * angleMask + cameraTransform.eulerAngles * !angleMask;
                var goalPos = referenceCamera.position * positionMask + cameraTransform.position * !positionMask;

                cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, Quaternion.Euler(goalEuler), t);
                cameraTransform.position = Vector3.Lerp(cameraTransform.position, goalPos, t);

                // The Operator turns to face the same way, so leaving the volume doesn't swing the camera around.
                op.ForceAxis(CurrentOperator, cameraTransform.eulerAngles.y, 0);
                _followingReference = true;
            }
            else
            {
                if (_followingReference)
                {
                    _followingReference = false;
                    StartBlend(properties != null ? properties.TransitionTime : ReferenceReturnTime);
                }

                if (_blendTime < _blendDuration)
                {
                    _blendTime += dt;
                    float t = Mathf.Clamp01(_blendTime / _blendDuration);
                    cameraTransform.position = Vector3.Lerp(cameraTransform.position, operatorCamera.position, t);
                    cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, operatorCamera.rotation, t);
                }
                else
                {
                    cameraTransform.position = operatorCamera.position;
                    cameraTransform.rotation = operatorCamera.rotation;
                }
            }

            // Field of view: the volume's, else the camera's own.
            var cameraComponent = Camera;
            if (cameraComponent != null)
            {
                if (_defaultFov < 0f)
                    _defaultFov = cameraComponent.fieldOfView;
                float goalFov = volume != null && volume.ChangeFOV[volumeInstance] ? volume.FOV[volumeInstance] : _defaultFov;
                cameraComponent.fieldOfView = Mathf.Lerp(cameraComponent.fieldOfView, goalFov, 1f - Mathf.Exp(-FovSpeed * dt));
            }

            // Inactive Operators follow the active one so switching is seamless.
            var root = op.Root[CurrentOperator];
            foreach (var other in _operators)
            {
                if (other == CurrentOperator || !other.IsSpawned || root == null) continue;

                var otherOperator = (CameraOperator)other.Behavior;
                var otherRoot = otherOperator.Root[other];
                if (otherRoot == null) continue;

                otherRoot.position = root.position;
                otherRoot.rotation = root.rotation;
                otherOperator.ForceAxis(other, op.ActualXAxis[CurrentOperator], op.ActualYAxis[CurrentOperator]);
            }
        }

        // Which Camera Properties Volume each Operator is in: the one around what it follows (or its rig).
        private void UpdateVolumes()
        {
            foreach (var instance in _operators)
            {
                if (!instance.IsSpawned) continue;
                var op = (CameraOperator)instance.Behavior;
                var follow = op.Follow[instance];
                var point = follow != null ? follow.position : op.Root[instance] != null ? op.Root[instance].position : instance.Transform.position;
                op.CurrentVolume[instance] = CameraPropertiesVolume.At(point);
            }
        }

        private void StartBlend(float duration)
        {
            _blendTime = 0f;
            _blendDuration = Mathf.Max(0f, duration);
        }

        /// <summary>Called by Camera Operators when they become enabled.</summary>
        public static void AddOperator(BehaviorInstance op)
        {
            if (!Instance._operators.Contains(op))
                Instance._operators.Add(op);
        }

        /// <summary>Called by Camera Operators when they become disabled or despawn.</summary>
        public static void RemoveOperator(BehaviorInstance op)
        {
            Instance._operators.Remove(op);
            if (CurrentOperator == op)
                CurrentOperator = null;
        }

        /// <summary>
        /// Makes an Operator the active one: the camera blends to it over its Properties' Transition Time (the first
        /// one is snapped to).
        /// </summary>
        public static void SetCameraOperator(BehaviorInstance op)
        {
            if (op == null) return;
            var manager = Instance;
            var operatorBehavior = (CameraOperator)op.Behavior;
            var operatorCamera = operatorBehavior.Camera[op];
            var cameraTransform = CameraTransform;
            bool first = CurrentOperator == null;

            CurrentOperator = op;
            if (first && operatorCamera != null && cameraTransform != null)
            {
                cameraTransform.position = operatorCamera.position;
                cameraTransform.rotation = operatorCamera.rotation;
                manager.StartBlend(0f);
            }
            else
            {
                var properties = operatorBehavior.Properties[op];
                manager.StartBlend(properties != null ? properties.TransitionTime : 0f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => CurrentOperator = null;
    }
}
