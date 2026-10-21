using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using UnityEngine;

namespace Remedy.Cameras
{
    [SchematicDoc(@"{""name"": ""Third Person Camera"", ""kind"": ""behavior"", ""menu"": ""Cameras/Third Person"", ""description"": ""A third-person camera that follows its target from behind at a set distance, staying near the ground beneath it, levelling itself out over time and pushing away from walls. Reports where it's aiming."", ""variables"": [""FollowOffset"", ""HorizontalFollowSpeed"", ""VerticalFollowSpeed"", ""YDistanceAllowance"", ""GroundedOffset"", ""MaxDistance"", ""LevelAngle"", ""AutoLevelDelay"", ""AutoLevelSpeed"", ""OptimalDistancePush"", ""OptimalDistance"", ""Body"", ""LookaheadOrientationStrength"", ""LookdownOrientationMaxVelocity"", ""LookdownOrientationMaxAngle"", ""LookupOrientationMaxVelocity"", ""LookupOrientationMaxAngle"", ""LookdownOrientationSpeed"", ""CollisionLayers"", ""CollisionReactSpeed"", ""YOffsetTransitionSpeed""], ""signals"": [""AimPosition""], ""use_for"": [""third-person action and adventure cameras""]}")]
    [SchematicBehaviorPath("Cameras/Third Person")]
    public class ThirdPersonCamera : CameraOperator
    {
        [SchematicDoc(@"{""name"": ""AimPosition"", ""kind"": ""signal"", ""behavior"": ""Third Person Camera"", ""direction"": ""output"", ""params"": [{""name"": ""Position"", ""type"": ""Vector3""}], ""description"": ""Sent by Third Person Camera when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Position", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimPosition;

        [SchematicDoc(@"{""name"": ""FollowOffset"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""Vector3"", ""default"": ""Vector3.zero"", ""description"": ""The Object to Follow.""}")]
        [Tooltip("The Object to Follow.")]
        public readonly VariableHandle<Vector3> FollowOffset = Vector3.zero;
        [SchematicDoc(@"{""name"": ""HorizontalFollowSpeed"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Horizontal Follow Speed (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> HorizontalFollowSpeed = 10f;
        [SchematicDoc(@"{""name"": ""VerticalFollowSpeed"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""3f"", ""description"": ""Vertical Follow Speed (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> VerticalFollowSpeed = 3f;
        [SchematicDoc(@"{""name"": ""YDistanceAllowance"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""3f"", ""description"": ""The Camera sticks to the ground beneath the follow target until this distance is reached, then this is used as an offset from the Follow Target""}")]
        [Tooltip("The Camera sticks to the ground beneath the follow target until this distance is reached, then this is used as an offset from the Follow Target")]
        public readonly VariableHandle<float> YDistanceAllowance = 3f;
        [SchematicDoc(@"{""name"": ""GroundedOffset"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""Vector3"", ""default"": ""new Vector3(0, 1, 0)"", ""description"": ""Grounded Offset (Vector3) of Third Person Camera.""}")]
        public readonly VariableHandle<Vector3> GroundedOffset = new Vector3(0, 1, 0);
        [SchematicDoc(@"{""name"": ""MaxDistance"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Max distance from the Followed Object.""}")]
        [Tooltip("Max distance from the Followed Object.")]
        public readonly VariableHandle<float> MaxDistance = 5f;

        [SchematicDoc(@"{""name"": ""LevelAngle"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Level Angle (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> LevelAngle = 5f;
        [SchematicDoc(@"{""name"": ""AutoLevelDelay"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""1f"", ""description"": ""Auto Level Delay (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> AutoLevelDelay = 1f;
        [SchematicDoc(@"{""name"": ""AutoLevelSpeed"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Auto Level Speed (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> AutoLevelSpeed = 5f;

        [SchematicDoc(@"{""name"": ""OptimalDistancePush"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""1f"", ""description"": ""An angular force applied to distance the Camera Rotation away from walls over time.""}")]
        [Tooltip("An angular force applied to distance the Camera Rotation away from walls over time.")]
        public readonly VariableHandle<float> OptimalDistancePush = 1f;
        [SchematicDoc(@"{""name"": ""OptimalDistance"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Optimal Distance (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> OptimalDistance = 10f;

        [SchematicDoc(@"{""name"": ""Body"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""Box3DBody"", ""default"": ""null"", ""description"": ""The followed character's physics body, for looking ahead and tilting when falling; empty uses the one on what it follows.""}")]
        [Tooltip("The followed character's physics body (look-ahead, tilting when falling). Empty: the one on what it follows.")]
        public readonly VariableHandle<Box3DBody> Body = (Box3DBody)null;
        [SchematicDoc(@"{""name"": ""LookaheadOrientationStrength"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""1f"", ""description"": ""Lookahead Orientation Strength (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> LookaheadOrientationStrength = 1f;
        [SchematicDoc(@"{""name"": ""LookdownOrientationMaxVelocity"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""The Velocity (downward) at which the character must be moving to fully forcefully orient the camera down.""}")]
        [Tooltip("The Velocity (downward) at which the character must be moving to fully forcefully orient the camera down.")]
        public readonly VariableHandle<float> LookdownOrientationMaxVelocity = 10f;
        [SchematicDoc(@"{""name"": ""LookdownOrientationMaxAngle"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""40f"", ""description"": ""The maximum angle downward the to orient the camera when falling""}")]
        [Tooltip("The maximum angle downward the to orient the camera when falling")]
        public readonly VariableHandle<float> LookdownOrientationMaxAngle = 40f;
        [SchematicDoc(@"{""name"": ""LookupOrientationMaxVelocity"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""The Velocity (upward) at which the character must be moving to fully forcefully orient the camera up.""}")]
        [Tooltip("The Velocity (upward) at which the character must be moving to fully forcefully orient the camera up.")]
        public readonly VariableHandle<float> LookupOrientationMaxVelocity = 10f;
        [SchematicDoc(@"{""name"": ""LookupOrientationMaxAngle"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""30f"", ""description"": ""The maximum angle upward to orient the camera when rising""}")]
        [Tooltip("The maximum angle upward to orient the camera when rising")]
        public readonly VariableHandle<float> LookupOrientationMaxAngle = 30f;
        [SchematicDoc(@"{""name"": ""LookdownOrientationSpeed"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Lookdown Orientation Speed (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> LookdownOrientationSpeed = 10f;

        [SchematicDoc(@"{""name"": ""CollisionLayers"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""LayerMask"", ""default"": ""(-1)"", ""description"": ""What is collided with to move the Camera closer to the Player""}")]
        [Tooltip("What is collided with to move the Camera closer to the Player")]
        public readonly VariableHandle<LayerMask> CollisionLayers = (LayerMask)(-1);
        [SchematicDoc(@"{""name"": ""CollisionReactSpeed"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Collision React Speed (float) of Third Person Camera.""}")]
        public readonly VariableHandle<float> CollisionReactSpeed = 10f;
        [SchematicDoc(@"{""name"": ""YOffsetTransitionSpeed"", ""kind"": ""variable"", ""behavior"": ""Third Person Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""Y Offset Transition Speed (float) of Third Person Camera.""}")]
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
            var follow = FollowTarget();
            var root = Root.Value;
            var camera = Camera.Value;
            if (follow == null || root == null || camera == null) return;

            float dt = Time.deltaTime;
            var collisionLayers = CollisionLayers.Value;
            var body = BodyOf(follow, Body.Value);
            var velocity = body.GetVelocity();
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

            if (SchematicPhysics.Raycast(camera.position, camera.forward, out RaycastHit aimHit, AimDistance, ~0, body))
                AimPosition.Invoke(aimHit.point);

            KeepObjectAtRoot(root, follow);

            horPos.x = Mathf.Lerp(root.position.x, follow.position.x + followOffset.x, HorizontalFollowSpeed.Value * dt);
            horPos.z = Mathf.Lerp(root.position.z, follow.position.z + followOffset.z, HorizontalFollowSpeed.Value * dt);

            // TODO: Apply an Offset Grace Period (Transition between the two different offsets so the camera doesn't jump up and down)
            if (SchematicPhysics.SphereCast(follow.position + Vector3.up, 0.5f, Vector3.down, out RaycastHit floorHit, out _, yDistanceAllowance, collisionLayers, body))
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

                // Lookahead in the direction the character is moving
                if (body != null && (Mathf.Abs(velocity.x) > 1 || Mathf.Abs(velocity.z) > 1))
                {
                    actualXAxis = Mathf.LerpAngle(actualXAxis, body.transform.eulerAngles.y, LookaheadOrientationStrength.Value * dt);
                    xAxis = Mathf.LerpAngle(xAxis, body.transform.eulerAngles.y, LookaheadOrientationStrength.Value * dt);
                }

                // Orient relative to walls
                if (SchematicPhysics.SphereCast(follow.position, 0.25f, follow.right, out RaycastHit wallHit, out _, OptimalDistance.Value, collisionLayers, body))
                {
                    Vector3 projectedForward = Vector3.ProjectOnPlane(follow.right, wallHit.normal).normalized;
                    var euler = Quaternion.LookRotation(projectedForward, Vector3.up).eulerAngles.y;

                    if (Mathf.Abs(Mathf.DeltaAngle(euler, root.eulerAngles.y)) < 60)
                        xAxis = Mathf.LerpAngle(xAxis, euler, OptimalDistancePush.Value * dt);
                }
                else if (SchematicPhysics.SphereCast(follow.position, 0.25f, -follow.right, out wallHit, out _, OptimalDistance.Value, collisionLayers, body))
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
            if (!SchematicPhysics.SphereCast(follow.position + Vector3.up, 0.5f, Vector3.down, out floorHit, out _, yDistanceAllowance, collisionLayers, body))
            {
                _lookingDown.Value = true;

                if (body != null && velocity.y <= 0)
                {
                    float t = Mathf.Clamp(Mathf.Abs(velocity.y) / LookdownOrientationMaxVelocity.Value, 0.2f, 1f);
                    float angle = Mathf.Lerp(0, LookdownOrientationMaxAngle.Value, t);
                    yAxis = Mathf.Lerp(yAxis, angle, LookdownOrientationSpeed.Value * dt);
                    actualYAxis = Mathf.Lerp(actualYAxis, angle, LookdownOrientationSpeed.Value * dt);
                }
                else if (body != null && velocity.y > 0)
                {
                    float t = Mathf.Clamp(Mathf.Abs(velocity.y) / LookupOrientationMaxVelocity.Value, 0f, 1f);
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

            if (SchematicPhysics.SphereCast(origin, CameraCollisionRadius.Value, direction, out RaycastHit hit, out _, targetDistance, collisionLayers, body))
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
