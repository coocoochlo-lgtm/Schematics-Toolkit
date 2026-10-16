using Remedy.Framework;
//using SaintsField;
using UnityEngine;

namespace Remedy.Cameras
{
    [ExecuteAlways]
    public class CameraManager : Singleton<CameraManager>
    {
        /// <summary>The active Camera Operator instance.</summary>
        public static BehaviorInstance CurrentOperator;
        public Transform _cameraTransform;

        private Camera _camera;
        public static Camera Camera => Instance._camera ??= CameraTransform.GetCachedComponent<Camera>();
        public static Transform CameraTransform => Instance._cameraTransform ?? null;

        // Every enabled Camera Operator, kept in step with the active one.
        private readonly System.Collections.Generic.List<BehaviorInstance> _operators = new();

        private float _currentTransitionTime = 0f;
        private float _referenceCamTransitionTime = 0f;
        private float _referenceCamTransitionDuration = 0f;
        private bool _wasUsingReferenceCam = false;
/*
        public DropdownList<CameraOperator> GetOperators()
        {
            var list = new DropdownList<CameraOperator>();
            var ops = FindObjectsByType<CameraOperator>(FindObjectsSortMode.None);

            foreach (var item in ops)
            {
                list.Add(item.name + "_" + item.GetType().Name, item);
            }

            return list;
        }*/

        void Start()
        {
            if(Camera != null)
                CameraSubsystem.SetMainCamera(Camera);
        }

        private void Update()
        {
            if (CurrentOperator == null || !CurrentOperator.IsSpawned) return;

            var op = (CameraOperator)CurrentOperator.Behavior;
            var operatorCamera = op.Camera[CurrentOperator];
            var properties = op.Properties[CurrentOperator];
            if (operatorCamera == null || _cameraTransform == null) return;

            float transitionTime = properties != null ? properties.TransitionTime : 0f;
            if (_currentTransitionTime < transitionTime)
            {
                _currentTransitionTime += Time.deltaTime;

                _cameraTransform.position = Vector3.Lerp(_cameraTransform.position, operatorCamera.position, _currentTransitionTime / transitionTime);
                _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, operatorCamera.rotation, _currentTransitionTime / transitionTime);
            }
            else
            {
                var volumeInstance = op.CurrentVolume[CurrentOperator];
                var volume = volumeInstance != null && volumeInstance.IsSpawned ? (CameraPropertiesVolume)volumeInstance.Behavior : null;
                bool useVolumes = properties != null && properties.UseVolumes;
                var referenceCamera = volume != null ? volume.ReferenceCamera[volumeInstance] : null;

                if (useVolumes && volume != null && volume.UseReferenceCam[volumeInstance] && referenceCamera != null)
                {
                    var refEuler = referenceCamera.eulerAngles;
                    var refPos = referenceCamera.position;
                    var angleMask = volume.ReferenceCamAngleMask[volumeInstance];
                    var positionMask = volume.ReferenceCamPositionMask[volumeInstance];
                    float speed = volume.ReferenceCameraSpeed[volumeInstance];

                    var goalEuler = refEuler * angleMask + _cameraTransform.eulerAngles * !angleMask;
                    var goalPos = refPos * positionMask + _cameraTransform.position * !positionMask;

                    _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, Quaternion.Euler(goalEuler), speed * Time.deltaTime);
                    _cameraTransform.position = Vector3.Lerp(_cameraTransform.position, goalPos, speed * Time.deltaTime);

                    op.ForceAxis(CurrentOperator, _cameraTransform.eulerAngles.y, 0);

                    _wasUsingReferenceCam = true;
                    _referenceCamTransitionTime = 0f;
                    _referenceCamTransitionDuration = speed;
                }
                else if (_wasUsingReferenceCam)
                {
                    if (_referenceCamTransitionTime < _referenceCamTransitionDuration)
                    {
                        _cameraTransform.position = Vector3.Lerp(_cameraTransform.position, operatorCamera.position, _referenceCamTransitionTime / _referenceCamTransitionDuration);
                        _cameraTransform.rotation = Quaternion.Slerp(_cameraTransform.rotation, operatorCamera.rotation, _referenceCamTransitionTime / _referenceCamTransitionDuration);
                        _referenceCamTransitionTime += Time.deltaTime;
                    }
                    else
                    {
                        _wasUsingReferenceCam = false;
                    }
                }
                else
                {
                    _cameraTransform.position = operatorCamera.position;
                    _cameraTransform.rotation = operatorCamera.rotation;
                }
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

        public static void SetCameraOperator(BehaviorInstance op)
        {
            if (Instance._cameraTransform == null || op == null) return;

            var operatorCamera = ((CameraOperator)op.Behavior).Camera[op];
            if (CurrentOperator == null && operatorCamera != null)
            {
                Instance._cameraTransform.position = operatorCamera.position;
                Instance._cameraTransform.rotation = operatorCamera.rotation;
            }

            CurrentOperator = op;
            Instance._currentTransitionTime = 0f;
        }
    }
}