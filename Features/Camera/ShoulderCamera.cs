using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using UnityEngine;

namespace Remedy.Cameras
{
    [SchematicDoc(@"{""name"": ""Shoulder Camera"", ""kind"": ""behavior"", ""menu"": ""Cameras/Shoulder"", ""description"": ""An over-the-shoulder camera that peers past the character's left or right shoulder, switching sides automatically near walls. Reports its look direction and aim position."", ""variables"": [""Shoulder"", ""ShoulderOffset"", ""ShoulderSwitchSpeed"", ""AutoSwitch"", ""SwitchDistance"", ""FollowOffset"", ""HorizontalFollowSpeed"", ""VerticalFollowSpeed"", ""YDistanceAllowance"", ""MaxDistance"", ""OptimalDistancePush"", ""OptimalDistance"", ""Body"", ""LookaheadOrientationStrength"", ""CollisionLayers"", ""CollisionReactSpeed""], ""signals"": [""ActualLookDirection"", ""GoalLookDirection"", ""AimPosition""], ""use_for"": [""third-person shooters"", ""aiming cameras""]}")]
    [SchematicBehaviorPath("Cameras/Shoulder")]
    public class ShoulderCamera : CameraOperator
    {
        [SchematicDoc(@"{""name"": ""ActualLookDirection"", ""kind"": ""signal"", ""behavior"": ""Shoulder Camera"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Sent every frame with the direction the camera is actually facing now (it eases towards the goal)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> ActualLookDirection;
        [SchematicDoc(@"{""name"": ""GoalLookDirection"", ""kind"": ""signal"", ""behavior"": ""Shoulder Camera"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Sent every frame with the direction the camera is turning towards (where the player is steering it)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> GoalLookDirection;
        [SchematicDoc(@"{""name"": ""AimPosition"", ""kind"": ""signal"", ""behavior"": ""Shoulder Camera"", ""direction"": ""output"", ""params"": [{""name"": ""Position"", ""type"": ""Vector3""}], ""description"": ""Sent every frame with the point in the world the camera is aiming at (where a shot would go)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Position", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimPosition;

        public enum Shoulders
        {
            Left,
            Right
        }

        [SchematicDoc(@"{""name"": ""Shoulder"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""Shoulders"", ""default"": ""Shoulders.Left"", ""description"": ""The shoulder the Camera is 'peering over'""}")]
        [Tooltip("The shoulder the Camera is 'peering over'")]
        public readonly VariableHandle<Shoulders> Shoulder = Shoulders.Left;
        [SchematicDoc(@"{""name"": ""ShoulderOffset"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""Vector3"", ""default"": ""new Vector3(0.5f, 0f, 0f)"", ""description"": ""The distance offset from the character's shoulder.""}")]
        [Tooltip("The distance offset from the character's shoulder.")]
        public readonly VariableHandle<Vector3> ShoulderOffset = new Vector3(0.5f, 0f, 0f);
        [SchematicDoc(@"{""name"": ""ShoulderSwitchSpeed"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""How quickly the camera slides across when it switches shoulders (higher is snappier).""}")]
        public readonly VariableHandle<float> ShoulderSwitchSpeed = 10f;
        [SchematicDoc(@"{""name"": ""AutoSwitch"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Switch to the other shoulder by itself when a wall is too close on this side.""}")]
        public readonly VariableHandle<bool> AutoSwitch = true;
        [SchematicDoc(@"{""name"": ""SwitchDistance"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""2f"", ""description"": ""How close a wall must be (meters) before it switches shoulders by itself.""}")]
        public readonly VariableHandle<float> SwitchDistance = 2f;
        [SchematicDoc(@"{""name"": ""FollowOffset"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""Vector3"", ""default"": ""Vector3.zero"", ""description"": ""Where the camera's pivot sits relative to the character (meters): raise Y to look over the head.""}")]
        public readonly VariableHandle<Vector3> FollowOffset = Vector3.zero;
        [SchematicDoc(@"{""name"": ""HorizontalFollowSpeed"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""How quickly the camera keeps up with the character sideways (higher sticks closer, lower trails behind).""}")]
        public readonly VariableHandle<float> HorizontalFollowSpeed = 10f;
        [SchematicDoc(@"{""name"": ""VerticalFollowSpeed"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""3f"", ""description"": ""How quickly the camera follows the character up and down (lower smooths out jumps and steps).""}")]
        public readonly VariableHandle<float> VerticalFollowSpeed = 3f;
        [SchematicDoc(@"{""name"": ""YDistanceAllowance"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""3f"", ""description"": ""The Camera sticks to the ground beneath the follow target until this distance is reached, then this is used as an offset from the Follow Target""}")]
        [Tooltip("The Camera sticks to the ground beneath the follow target until this distance is reached, then this is used as an offset from the Follow Target")]
        public readonly VariableHandle<float> YDistanceAllowance = 3f;
        [SchematicDoc(@"{""name"": ""MaxDistance"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Max distance from the Followed Object.""}")]
        [Tooltip("Max distance from the Followed Object.")]
        public readonly VariableHandle<float> MaxDistance = 5f;

        [SchematicDoc(@"{""name"": ""OptimalDistancePush"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""1f"", ""description"": ""An angular force applied to distance the Camera Rotation away from walls over time.""}")]
        [Tooltip("An angular force applied to distance the Camera Rotation away from walls over time.")]
        public readonly VariableHandle<float> OptimalDistancePush = 1f;
        [SchematicDoc(@"{""name"": ""OptimalDistance"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""How far behind the character the camera sits when nothing's in the way (meters).""}")]
        public readonly VariableHandle<float> OptimalDistance = 10f;

        [SchematicDoc(@"{""name"": ""Rigidbody"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""Rigidbody"", ""default"": ""null"", ""description"": ""The character's physics body, to read its movement from (found by itself if left empty).""}")]
        [Tooltip("The character's physics body, to read its movement from (found by itself if left empty).")]
        public readonly VariableHandle<Box3DBody> Body = (Box3DBody)null;
        [SchematicDoc(@"{""name"": ""LookaheadOrientationStrength"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""1f"", ""description"": ""How strongly the camera swings round to look the way the character is moving (0 never).""}")]
        public readonly VariableHandle<float> LookaheadOrientationStrength = 1f;

        [SchematicDoc(@"{""name"": ""CollisionLayers"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""LayerMask"", ""default"": ""(-1)"", ""description"": ""What is collided with to move the Camera closer to the Player""}")]
        [Tooltip("What is collided with to move the Camera closer to the Player")]
        public readonly VariableHandle<LayerMask> CollisionLayers = (LayerMask)(-1);
        [SchematicDoc(@"{""name"": ""CollisionReactSpeed"", ""kind"": ""variable"", ""behavior"": ""Shoulder Camera"", ""type"": ""float"", ""default"": ""10f"", ""description"": ""How quickly the camera pulls in when a wall gets between it and the character, and eases back out after.""}")]
        public readonly VariableHandle<float> CollisionReactSpeed = 10f;

        private readonly VariableHandle<float> _targetScale = 1f;
        private readonly VariableHandle<float> _currentScale = 1f;
        private readonly VariableHandle<Vector3> _shoulderPosition = Vector3.zero;

        protected override void OnUpdate()
        {
            var follow = FollowTarget();
            var root = Root.Value;
            var camera = Camera.Value;
            if (follow == null || root == null || camera == null) return;
            var body = BodyOf(follow, Body.Value);
            // Measured in the camera's frame: its eased up (the target's, on slopes, walls and loops).
            var frame = UpFrame(follow, body, CollisionLayers.Value);
            var up = CameraUp;

            float dt = Time.deltaTime;
            var collisionLayers = CollisionLayers.Value;
            ref var shoulder = ref Shoulder.Ref;
            ref var shoulderPosition = ref _shoulderPosition.Ref;
            var followOffset = frame * FollowOffset.Value;

            if (AutoSwitch.Value)
            {
                var side = shoulder == Shoulders.Left ? -root.right : root.right;
                if (CameraCast(root.position + up, 0.25f, side, 1f, collisionLayers, follow, out _))
                    shoulder = shoulder == Shoulders.Left ? Shoulders.Right : Shoulders.Left;
            }

            shoulderPosition.y = ShoulderOffset.Value.y;
            shoulderPosition.z = -1;
            float goalShoulderPosition = ShoulderOffset.Value.x * (((int)shoulder * 2f) - 1f);
            shoulderPosition.x = Mathf.Lerp(shoulderPosition.x, goalShoulderPosition, ShoulderSwitchSpeed.Value * dt);
            camera.localPosition = shoulderPosition;

            KeepObjectAtRoot(root, follow);

            var target = follow.position + followOffset;
            root.position = FollowAcrossAndAlong(root.position, target, Vector3.Dot(target, up), up, HorizontalFollowSpeed.Value, VerticalFollowSpeed.Value, dt);

            // Apply rotation to Root
            _goalRotation.Value = frame * Quaternion.Euler(YAxis.Value, XAxis.Value, 0);

            // Rotation smoothing
            ActualYAxis.Value = Mathf.Lerp(ActualYAxis.Value, YAxis.Value, SmoothSpeed * dt);
            ActualXAxis.Value = Mathf.Lerp(ActualXAxis.Value, XAxis.Value, SmoothSpeed * dt);

            root.rotation = frame * Quaternion.Euler(ActualYAxis.Value, ActualXAxis.Value, 0);

            Vector3 origin = root.position;
            Vector3 direction = -root.forward;
            float maxDistance = MaxDistance.Value;

            if (CameraCast(origin, CameraCollisionRadius.Value, direction, maxDistance, collisionLayers, follow, out RaycastHit hit))
                _targetScale.Value = Mathf.Clamp(hit.distance, 0.5f, maxDistance); // reduce distance based on hit
            else
                _targetScale.Value = maxDistance;

            // Smooth scale interpolation
            _currentScale.Value = Mathf.Lerp(_currentScale.Value, _targetScale.Value, CollisionReactSpeed.Value * dt);
            root.localScale = new Vector3(1, 1, _currentScale.Value);

            UpdateAimDirection();

            GoalLookDirection.Invoke(_goalRotation.Value * Vector3.forward);
            ActualLookDirection.Invoke(root.forward);

            if (CameraCast(camera.position, 0f, camera.forward, AimDistance, ~0, follow, out RaycastHit aimHit))
                AimPosition.Invoke(aimHit.point);
        }

        /// <summary>Swaps the selected Shoulder Camera to the other shoulder.</summary>
        public void SwitchShoulder()
        {
            Shoulder.Value = Shoulder.Value == Shoulders.Left ? Shoulders.Right : Shoulders.Left;
        }
    }
}
