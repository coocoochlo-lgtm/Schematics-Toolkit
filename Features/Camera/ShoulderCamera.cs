using UnityEngine;

namespace Remedy.Cameras
{
    [SchematicBehaviorPath("Cameras/Shoulder")]
    public class ShoulderCamera : CameraOperator
    {
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> ActualLookDirection;
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> GoalLookDirection;
        [OutputSignal, Parameter("Position", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimPosition;

        public enum Shoulders
        {
            Left,
            Right
        }

        [Tooltip("The shoulder the Camera is 'peering over'")]
        public readonly VariableHandle<Shoulders> Shoulder = Shoulders.Left;
        [Tooltip("The distance offset from the character's shoulder.")]
        public readonly VariableHandle<Vector3> ShoulderOffset = new Vector3(0.5f, 0f, 0f);
        public readonly VariableHandle<float> ShoulderSwitchSpeed = 10f;
        public readonly VariableHandle<bool> AutoSwitch = true;
        public readonly VariableHandle<float> SwitchDistance = 2f;
        public readonly VariableHandle<Vector3> FollowOffset = Vector3.zero;
        public readonly VariableHandle<float> HorizontalFollowSpeed = 10f;
        public readonly VariableHandle<float> VerticalFollowSpeed = 3f;
        [Tooltip("The Camera sticks to the ground beneath the follow target until this distance is reached, then this is used as an offset from the Follow Target")]
        public readonly VariableHandle<float> YDistanceAllowance = 3f;
        [Tooltip("Max distance from the Followed Object.")]
        public readonly VariableHandle<float> MaxDistance = 5f;

        [Tooltip("An angular force applied to distance the Camera Rotation away from walls over time.")]
        public readonly VariableHandle<float> OptimalDistancePush = 1f;
        public readonly VariableHandle<float> OptimalDistance = 10f;

        public readonly VariableHandle<Rigidbody> Rigidbody = (Rigidbody)null;
        public readonly VariableHandle<float> LookaheadOrientationStrength = 1f;

        [Tooltip("What is collided with to move the Camera closer to the Player")]
        public readonly VariableHandle<LayerMask> CollisionLayers = (LayerMask)(-1);
        public readonly VariableHandle<float> CollisionReactSpeed = 10f;

        private readonly VariableHandle<float> _targetScale = 1f;
        private readonly VariableHandle<float> _currentScale = 1f;
        private readonly VariableHandle<Vector3> _horPos = Vector3.zero;
        private readonly VariableHandle<Vector3> _vertPos = Vector3.zero;
        private readonly VariableHandle<Vector3> _shoulderPosition = Vector3.zero;

        protected override void OnUpdate()
        {
            var follow = Follow.Value;
            var root = Root.Value;
            var camera = Camera.Value;
            if (follow == null || root == null || camera == null) return;

            float dt = Time.deltaTime;
            var collisionLayers = CollisionLayers.Value;
            ref var shoulder = ref Shoulder.Ref;
            ref var shoulderPosition = ref _shoulderPosition.Ref;
            ref var horPos = ref _horPos.Ref;
            ref var vertPos = ref _vertPos.Ref;
            var followOffset = FollowOffset.Value;

            if (AutoSwitch.Value)
            {
                var side = shoulder == Shoulders.Left ? -root.right : root.right;
                if (Physics.SphereCast(root.position + Vector3.up, 0.25f, side, out _, 1, collisionLayers))
                    shoulder = shoulder == Shoulders.Left ? Shoulders.Right : Shoulders.Left;
            }

            shoulderPosition.y = ShoulderOffset.Value.y;
            shoulderPosition.z = -1;
            float goalShoulderPosition = ShoulderOffset.Value.x * (((int)shoulder * 2f) - 1f);
            shoulderPosition.x = Mathf.Lerp(shoulderPosition.x, goalShoulderPosition, ShoulderSwitchSpeed.Value * dt);
            camera.localPosition = shoulderPosition;

            // Keep the object at the Root without moving the Root.
            var prevRootPos = root.position;
            Self.Transform.position = root.position;
            root.position = prevRootPos;

            horPos.x = Mathf.Lerp(root.position.x, follow.position.x + followOffset.x, HorizontalFollowSpeed.Value * dt);
            horPos.z = Mathf.Lerp(root.position.z, follow.position.z + followOffset.z, HorizontalFollowSpeed.Value * dt);
            vertPos.y = Mathf.Lerp(root.position.y, follow.position.y + followOffset.y, VerticalFollowSpeed.Value * dt);

            root.position = horPos + vertPos;

            // Apply rotation to Root
            _goalRotation.Value = Quaternion.Euler(YAxis.Value, XAxis.Value, 0);

            // Rotation smoothing
            ActualYAxis.Value = Mathf.Lerp(ActualYAxis.Value, YAxis.Value, SmoothSpeed * dt);
            ActualXAxis.Value = Mathf.Lerp(ActualXAxis.Value, XAxis.Value, SmoothSpeed * dt);

            root.rotation = Quaternion.Euler(ActualYAxis.Value, ActualXAxis.Value, 0);

            Vector3 origin = root.position;
            Vector3 direction = -root.forward;
            float maxDistance = MaxDistance.Value;

            if (Physics.SphereCast(origin, CameraCollisionRadius.Value, direction, out RaycastHit hit, maxDistance, collisionLayers, QueryTriggerInteraction.Ignore))
                _targetScale.Value = Mathf.Clamp(hit.distance, 0.5f, maxDistance); // reduce distance based on hit
            else
                _targetScale.Value = maxDistance;

            // Smooth scale interpolation
            _currentScale.Value = Mathf.Lerp(_currentScale.Value, _targetScale.Value, CollisionReactSpeed.Value * dt);
            root.localScale = new Vector3(1, 1, _currentScale.Value);

            UpdateAimDirection();

            GoalLookDirection.Invoke(_goalRotation.Value * Vector3.forward);
            ActualLookDirection.Invoke(root.forward);

            if (Physics.Raycast(camera.position, camera.forward, out RaycastHit aimHit, Mathf.Infinity))
                AimPosition.Invoke(aimHit.point);
        }

        /// <summary>Swaps the selected Shoulder Camera to the other shoulder.</summary>
        public void SwitchShoulder()
        {
            Shoulder.Value = Shoulder.Value == Shoulders.Left ? Shoulders.Right : Shoulders.Left;
        }
    }
}
