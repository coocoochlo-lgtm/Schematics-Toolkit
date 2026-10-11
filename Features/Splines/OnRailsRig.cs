using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>
    /// Star Fox-style on-rails movement. The object is a rig that travels along a spline (its Spline Agent) carrying a
    /// camera; the ship inside it moves freely on the plane in front of the camera, clamped so it never leaves the
    /// screen, banking and pitching as it goes. The camera drifts a little toward the ship, as in the games.
    /// </summary>
    [SchematicDoc(@"{""name"": ""On Rails Rig"", ""kind"": ""behavior"", ""menu"": ""Splines/On Rails Rig"", ""description"": ""Star Fox-style on-rails movement. The object is a rig that travels along a spline (its Spline Agent) carrying a camera; the ship inside it moves freely on the plane in front of the camera, clamped so it never leaves the screen, banking and pitching as it goes. The camera drifts a little toward the ship, as in the games."", ""variables"": [""Camera"", ""Ship"", ""ShipDepth"", ""ShipSpeed"", ""ScreenMargin"", ""MaxBank"", ""MaxPitch"", ""TiltResponse"", ""CameraFollow"", ""InvertY"", ""ShipOffset""], ""signals"": [""MoveInput""], ""use_for"": [""Star Fox-style rail shooters"", ""on-rails sequences""]}")]
    [SchematicBehaviorPath("Splines/On Rails Rig")]
    [RequireBehavior(typeof(SplineAgent))]
    public class OnRailsRig : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Camera"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""Camera"", ""default"": ""null"", ""description"": ""Camera (Camera) of On Rails Rig.""}")]
        public readonly VariableHandle<Camera> Camera = (Camera)null;
        [SchematicDoc(@"{""name"": ""Ship"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Ship (Transform) of On Rails Rig.""}")]
        public readonly VariableHandle<Transform> Ship = (Transform)null;

        [SchematicDoc(@"{""name"": ""ShipDepth"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""float"", ""default"": ""12f"", ""description"": ""How far in front of the camera the ship flies.""}")]
        [Tooltip("How far in front of the camera the ship flies.")]
        public readonly VariableHandle<float> ShipDepth = 12f;
        [SchematicDoc(@"{""name"": ""ShipSpeed"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""float"", ""default"": ""14f"", ""description"": ""How fast the ship moves across the screen, in units per second.""}")]
        [Tooltip("How fast the ship moves across the screen, in units per second.")]
        public readonly VariableHandle<float> ShipSpeed = 14f;
        [SchematicDoc(@"{""name"": ""ScreenMargin"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""Vector2"", ""default"": ""new Vector2(0.08f, 0.1f)"", ""description"": ""Space kept between the ship and the screen edges, as a fraction of the screen (0-0.5).""}")]
        [Tooltip("Space kept between the ship and the screen edges, as a fraction of the screen (0-0.5).")]
        public readonly VariableHandle<Vector2> ScreenMargin = new Vector2(0.08f, 0.1f);
        [SchematicDoc(@"{""name"": ""MaxBank"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""float"", ""default"": ""40f"", ""description"": ""Degrees the ship banks at full sideways input.""}")]
        [Tooltip("Degrees the ship banks at full sideways input.")]
        public readonly VariableHandle<float> MaxBank = 40f;
        [SchematicDoc(@"{""name"": ""MaxPitch"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""float"", ""default"": ""20f"", ""description"": ""Degrees the ship pitches at full vertical input.""}")]
        [Tooltip("Degrees the ship pitches at full vertical input.")]
        public readonly VariableHandle<float> MaxPitch = 20f;
        [SchematicDoc(@"{""name"": ""TiltResponse"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""float"", ""default"": ""8f"", ""description"": ""How quickly the ship's tilt follows the input.""}")]
        [Tooltip("How quickly the ship's tilt follows the input.")]
        public readonly VariableHandle<float> TiltResponse = 8f;
        [SchematicDoc(@"{""name"": ""CameraFollow"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""float"", ""default"": ""0.3f"", ""description"": ""How much the camera drifts toward the ship (0 stays centred, 1 keeps the ship centred).""}")]
        [Tooltip("How much the camera drifts toward the ship (0 stays centred, 1 keeps the ship centred).")]
        public readonly VariableHandle<float> CameraFollow = 0.3f;
        [SchematicDoc(@"{""name"": ""InvertY"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Invert vertical input (flight-style controls).""}")]
        [Tooltip("Invert vertical input (flight-style controls).")]
        public readonly VariableHandle<bool> InvertY = false;

        /// <summary>The ship's position on the screen plane, relative to the camera's centre.</summary>
        [SchematicDoc(@"{""name"": ""ShipOffset"", ""kind"": ""variable"", ""behavior"": ""On Rails Rig"", ""type"": ""Vector2"", ""default"": ""Vector2.zero"", ""description"": ""The ship's position on the screen plane, relative to the camera's centre.""}")]
        public readonly VariableHandle<Vector2> ShipOffset = Vector2.zero;
        private readonly VariableHandle<Vector2> _input = Vector2.zero;
        private readonly VariableHandle<Vector2> _tilt = Vector2.zero;
        private readonly VariableHandle<Vector3> _cameraHome = Vector3.zero;

        [SchematicDoc(@"{""name"": ""MoveInput"", ""kind"": ""signal"", ""behavior"": ""On Rails Rig"", ""direction"": ""input"", ""params"": [{""name"": ""Value"", ""type"": ""Vector2""}], ""description"": ""Tells On Rails Rig to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Value", typeof(Vector2))]
        public readonly SignalHandle<Vector2> MoveInput;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            var cameraObject = new GameObject("Rail Camera", typeof(Camera));
            cameraObject.transform.SetParent(context.Transform, false);
            var ship = new GameObject("Ship").transform;
            ship.SetParent(context.Transform, false);
            ship.localPosition = new Vector3(0f, 0f, 12f);
            context.Set(Camera, cameraObject.GetComponent<Camera>());
            context.Set(Ship, ship);
        }

        protected override void OnStart()
        {
            if (Camera.Value != null)
                _cameraHome.Value = Camera.Value.transform.localPosition;
            ShipOffset.Value = Vector2.zero;
            MoveInput.Subscribe(value => _input.Value = Vector2.ClampMagnitude(value, 1f));
        }

        // LateUpdate: after the Spline Agent has moved the rig.
        protected override void OnLateUpdate()
        {
            var camera = Camera.Value;
            var ship = Ship.Value;
            if (camera == null || ship == null)
                return;

            float dt = Time.deltaTime;
            Vector2 input = _input.Value;
            if (InvertY.Value)
                input.y = -input.y;

            // Move on the screen plane, then keep inside the visible area at the ship's depth. The camera drifts by
            // CameraFollow of the offset, so the ship's offset on screen is (1 - CameraFollow) of it.
            Vector2 offset = ShipOffset.Value + input * (ShipSpeed.Value * dt);
            float follow = Mathf.Clamp(CameraFollow.Value, 0f, 0.95f);
            Vector2 half = VisibleHalfExtents(camera, ShipDepth.Value, ScreenMargin.Value) / (1f - follow);
            offset.x = Mathf.Clamp(offset.x, -half.x, half.x);
            offset.y = Mathf.Clamp(offset.y, -half.y, half.y);
            ShipOffset.Value = offset;

            // The camera drifts toward the ship; the ship is placed relative to the camera, so it stays clamped on screen.
            var cameraTransform = camera.transform;
            cameraTransform.localPosition = _cameraHome.Value + new Vector3(offset.x, offset.y, 0f) * follow;
            Vector2 onScreen = offset * (1f - follow);
            ship.position = cameraTransform.TransformPoint(new Vector3(onScreen.x, onScreen.y, ShipDepth.Value));

            // Bank into turns, pitch with vertical movement.
            Vector2 targetTilt = new Vector2(-input.y * MaxPitch.Value, -input.x * MaxBank.Value);
            Vector2 tilt = Vector2.Lerp(_tilt.Value, targetTilt, 1f - Mathf.Exp(-TiltResponse.Value * dt));
            _tilt.Value = tilt;
            ship.rotation = Self.Transform.rotation * Quaternion.Euler(tilt.x, input.x * MaxBank.Value * 0.25f, tilt.y);
        }

        /// <summary>Half the width and height the ship can use at a depth in front of the camera.</summary>
        public static Vector2 VisibleHalfExtents(Camera camera, float depth, Vector2 margin)
        {
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * camera.aspect;
            return new Vector2(halfWidth * (1f - 2f * Mathf.Clamp(margin.x, 0f, 0.49f)),
                               halfHeight * (1f - 2f * Mathf.Clamp(margin.y, 0f, 0.49f)));
        }

        // The box the ship is kept in, at its depth in front of the camera.
        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var camera = context.Get(Camera);
            if (camera == null)
                return;
            float depth = context.Get(ShipDepth);
            Vector2 half = VisibleHalfExtents(camera, depth, context.Get(ScreenMargin));
            var t = camera.transform;
            Vector3 a = t.TransformPoint(new Vector3(-half.x, -half.y, depth));
            Vector3 b = t.TransformPoint(new Vector3(half.x, -half.y, depth));
            Vector3 c = t.TransformPoint(new Vector3(half.x, half.y, depth));
            Vector3 d = t.TransformPoint(new Vector3(-half.x, half.y, depth));
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
            Gizmos.DrawLine(t.position, a); Gizmos.DrawLine(t.position, b); Gizmos.DrawLine(t.position, c); Gizmos.DrawLine(t.position, d);
        }
    }
}
