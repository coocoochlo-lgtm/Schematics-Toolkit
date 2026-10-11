using UnityEngine;

namespace Remedy.Cameras
{
    [SchematicBehaviorPath("Cameras/Third Person")]
    public class ThirdPersonCamera : CameraOperator
    {
        [OutputSignal, Parameter("Position", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimPosition;

        [Tooltip("The Object to Follow.")]
        public readonly VariableHandle<Vector3> FollowOffset = Vector3.zero;
        public readonly VariableHandle<float> HorizontalFollowSpeed = 10f;
        public readonly VariableHandle<float> VerticalFollowSpeed = 3f;
        [Tooltip("The Camera sticks to the ground beneath the follow target until this distance is reached, then this is used as an offset from the Follow Target")]
        public readonly VariableHandle<float> YDistanceAllowance = 3f;
        public readonly VariableHandle<Vector3> GroundedOffset = new Vector3(0, 1, 0);
        [Tooltip("Max distance from the Followed Object.")]
        public readonly VariableHandle<float> MaxDistance = 5f;

        public readonly VariableHandle<float> LevelAngle = 5f;
        public readonly VariableHandle<float> AutoLevelDelay = 1f;
        public readonly VariableHandle<float> AutoLevelSpeed = 5f;

        [Tooltip("An angular force applied to distance the Camera Rotation away from walls over time.")]
        public readonly VariableHandle<float> OptimalDistancePush = 1f;
        public readonly VariableHandle<float> OptimalDistance = 10f;

        public readonly VariableHandle<Rigidbody> Rigidbody = (Rigidbody)null;
        public readonly VariableHandle<float> LookaheadOrientationStrength = 1f;
        [Tooltip("The Velocity (downward) at which the Rigidbody must be moving to fully forcefully orient the camera down.")]
        public readonly VariableHandle<float> LookdownOrientationMaxVelocity = 10f;
        [Tooltip("The maximum angle downward the to orient the camera when falling")]
        public readonly VariableHandle<float> LookdownOrientationMaxAngle = 40f;
        [Tooltip("The Velocity (upward) at which the Rigidbody must be moving to fully forcefully orient the camera up.")]
        public readonly VariableHandle<float> LookupOrientationMaxVelocity = 10f;
        [Tooltip("The maximum angle upward to orient the camera when rising")]
        public readonly VariableHandle<float> LookupOrientationMaxAngle = 30f;
        public readonly VariableHandle<float> LookdownOrientationSpeed = 10f;

        [Tooltip("What is collided with to move the Camera closer to the Player")]
        public readonly VariableHandle<LayerMask> CollisionLayers = (LayerMask)(-1);
        public readonly VariableHandle<float> CollisionReactSpeed = 10f;
        public readonly VariableHandle<float> YOffsetTransitionSpeed = 10f;

        private readonly VariableHandle<float> _targetScale = 1f;
        private readonly VariableHandle<float> _currentScale = 1f;
        private readonly VariableHandle<Vector3> _horPos = Vector3.zero;
        private readonly VariableHandle<Vector3> _vertPos = Vector3.zero;
        private readonly VariableHandle<bool> _lookingDown = false;
        private readonly VariableHandle<float> _currentYOffset = 0f;
        private readonly VariableHandle<float> _lastFloorHit = 0f;

        protected override void OnUpdate()
        {
            var follow = Follow.Value;
            var root = Root.Value;
            var camera = Camera.Value;
            if (follow == null || root == null || camera == null) return;

            float dt = Time.deltaTime;
            var collisionLayers = CollisionLayers.Value;
            var rigidbody = Rigidbody.Value;
            var followOffset = FollowOffset.Value;
            float yDistanceAllowance = YDistanceAllowance.Value;

            ref float xAxis = ref XAxis.Ref;
            ref float yAxis = ref YAxis.Ref;
            ref float actualXAxis = ref ActualXAxis.Ref;
            ref float actualYAxis = ref ActualYAxis.Ref;
            ref float timeSinceInput = ref _timeSinceInput.Ref;
            ref var horPos = ref _horPos.Ref;
            ref var vertPos = ref _vertPos.Ref;
            ref float currentYOffset = ref _currentYOffset.Ref;
            ref float lastFloorHit = ref _lastFloorHit.Ref;

            if (Physics.Raycast(camera.position, camera.forward, out RaycastHit aimHit, Mathf.Infinity))
                AimPosition.Invoke(aimHit.point);

            // Keep the object at the Root without moving the Root.
            var prevRootPos = root.position;
            Self.Transform.position = root.position;
            root.position = prevRootPos;

            horPos.x = Mathf.Lerp(root.position.x, follow.position.x + followOffset.x, HorizontalFollowSpeed.Value * dt);
            horPos.z = Mathf.Lerp(root.position.z, follow.position.z + followOffset.z, HorizontalFollowSpeed.Value * dt);

            // TODO: Apply an Offset Grace Period (Transition between the two different offsets so the camera doesn't jump up and down)
            if (Physics.SphereCast(follow.position + Vector3.up, 0.5f, Vector3.down, out RaycastHit floorHit, yDistanceAllowance, collisionLayers))
            {
                currentYOffset = Mathf.Lerp(currentYOffset, 0f, YOffsetTransitionSpeed.Value * dt);
                lastFloorHit = floorHit.point.y + GroundedOffset.Value.y;
            }
            else
            {
                currentYOffset = Mathf.Lerp(currentYOffset, 1f, YOffsetTransitionSpeed.Value * dt);
            }

            float goalY = Mathf.Lerp(lastFloorHit, follow.position.y + followOffset.y, currentYOffset);
            vertPos.y = Mathf.Lerp(root.position.y, goalY, VerticalFollowSpeed.Value * dt);

            root.position = horPos + vertPos;

            // Rotation smoothing
            actualXAxis = Mathf.Lerp(actualXAxis, xAxis, SmoothSpeed * dt);

            TryGetVolume(out var volume, out var volumeInstance);

            // Level look axis angle
            timeSinceInput += dt;
            if (timeSinceInput > AutoLevelDelay.Value)
            {
                if (!_lookingDown.Value)
                {
                    // Reset camera to resting angle
                    float restingAngle = volume != null && volume.ChangeRestingAngle[volumeInstance] ? volume.RestingAngle[volumeInstance] : LevelAngle.Value;
                    actualYAxis = Mathf.Lerp(actualYAxis, restingAngle, AutoLevelSpeed.Value * dt);
                    yAxis = Mathf.Lerp(yAxis, restingAngle, AutoLevelSpeed.Value * dt);
                }

                // Lookahead in the direction of the Rigidbody Horizontal Velocity
                if (rigidbody != null && (Mathf.Abs(rigidbody.linearVelocity.x) > 1 || Mathf.Abs(rigidbody.linearVelocity.z) > 1))
                {
                    actualXAxis = Mathf.LerpAngle(actualXAxis, rigidbody.transform.eulerAngles.y, LookaheadOrientationStrength.Value * dt);
                    xAxis = Mathf.LerpAngle(xAxis, rigidbody.transform.eulerAngles.y, LookaheadOrientationStrength.Value * dt);
                }

                // Orient relative to walls
                if (Physics.SphereCast(follow.position, 0.25f, follow.right, out RaycastHit wallHit, OptimalDistance.Value, collisionLayers))
                {
                    Vector3 projectedForward = Vector3.ProjectOnPlane(follow.right, wallHit.normal).normalized;
                    var euler = Quaternion.LookRotation(projectedForward, Vector3.up).eulerAngles.y;

                    if (Mathf.Abs(Mathf.DeltaAngle(euler, root.eulerAngles.y)) < 60)
                        xAxis = Mathf.LerpAngle(xAxis, euler, OptimalDistancePush.Value * dt);
                }
                else if (Physics.SphereCast(follow.position, 0.25f, -follow.right, out wallHit, OptimalDistance.Value, collisionLayers))
                {
                    Vector3 projectedForward = Vector3.ProjectOnPlane(-follow.right, wallHit.normal).normalized;
                    var euler = Quaternion.LookRotation(projectedForward, Vector3.up).eulerAngles.y;

                    if (Mathf.Abs(Mathf.DeltaAngle(euler, root.eulerAngles.y)) < 100)
                        xAxis = Mathf.LerpAngle(xAxis, euler, OptimalDistancePush.Value * dt);
                }
            }
            else
            {
                actualYAxis = Mathf.Lerp(actualYAxis, yAxis, SmoothSpeed * dt);
            }

            // Aim down when falling
            if (!Physics.SphereCast(follow.position + Vector3.up, 0.5f, Vector3.down, out floorHit, yDistanceAllowance, collisionLayers))
            {
                _lookingDown.Value = true;

                if (rigidbody != null && rigidbody.linearVelocity.y <= 0)
                {
                    float t = Mathf.Clamp(Mathf.Abs(rigidbody.linearVelocity.y) / LookdownOrientationMaxVelocity.Value, 0.2f, 1f);
                    float angle = Mathf.Lerp(0, LookdownOrientationMaxAngle.Value, t);
                    yAxis = Mathf.Lerp(yAxis, angle, LookdownOrientationSpeed.Value * dt);
                    actualYAxis = Mathf.Lerp(actualYAxis, angle, LookdownOrientationSpeed.Value * dt);
                }
                else if (rigidbody != null && rigidbody.linearVelocity.y > 0)
                {
                    float t = Mathf.Clamp(Mathf.Abs(rigidbody.linearVelocity.y) / LookupOrientationMaxVelocity.Value, 0f, 1f);
                    float angle = Mathf.Lerp(0, -LookupOrientationMaxAngle.Value, t);
                    yAxis = Mathf.Lerp(yAxis, angle, LookdownOrientationSpeed.Value * dt);
                    actualYAxis = Mathf.Lerp(actualYAxis, angle, LookdownOrientationSpeed.Value * dt);
                }
            }
            else
                _lookingDown.Value = false;

            // Apply rotation to Root
            root.rotation = Quaternion.Euler(actualYAxis, actualXAxis, 0f);

            // SphereCast from Root to max range in the local -Z direction
            Vector3 origin = root.position;
            Vector3 direction = -root.forward;

            float targetDistance = volume != null && volume.ChangeZoom[volumeInstance] ? volume.Zoom[volumeInstance] : MaxDistance.Value;

            if (Physics.SphereCast(origin, CameraCollisionRadius.Value, direction, out RaycastHit hit, targetDistance, collisionLayers, QueryTriggerInteraction.Ignore))
                _targetScale.Value = Mathf.Clamp(hit.distance, 0.5f, targetDistance); // reduce distance based on hit
            else
                _targetScale.Value = targetDistance;

            // Smooth scale interpolation
            _currentScale.Value = Mathf.Lerp(_currentScale.Value, _targetScale.Value, CollisionReactSpeed.Value * dt);
            root.localScale = new Vector3(1, 1, _currentScale.Value);

            var dir = root.position - camera.position;
            camera.rotation = Quaternion.Slerp(camera.rotation, Quaternion.LookRotation(dir, Vector3.up), 20f * dt);
            camera.localPosition = Vector3.Lerp(camera.localPosition, new Vector3(0, 0, -1), 20f * dt);

            UpdateAimDirection();
        }
    }
}
