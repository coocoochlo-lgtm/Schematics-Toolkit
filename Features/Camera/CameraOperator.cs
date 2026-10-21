using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Cameras
{
    /// <summary>
    /// The base of a camera rig: a Root that follows a target, with the Camera on a boom beneath it.
    /// The Camera Manager blends the scene camera toward the active Operator.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Camera Operator"", ""kind"": ""behavior"", ""description"": ""The base of a camera rig: a Root that follows a target, with the Camera on a boom beneath it. The Camera Manager blends the scene camera toward the active Operator."", ""variables"": [""Properties"", ""Follow"", ""Root"", ""Camera"", ""CameraCollisionRadius"", ""CurrentVolume"", ""AimDirection"", ""OrientCharacterToCamera"", ""XAxis"", ""YAxis"", ""ActualXAxis"", ""ActualYAxis"", ""AlignToTargetUp"", ""UpAlignSpeed"", ""GroundProbeDistance"", ""HeadingRecovery""], ""signals"": [""SetObjectToFollow"", ""LookInput"", ""AimDirectionOutput"", ""OrientCharacterToCameraOutput""], ""use_for"": [""custom camera rigs""], ""notes"": ""Base of the camera Behaviors; use Third Person or Shoulder instead.""}")]
    public abstract class CameraOperator : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""SetObjectToFollow"", ""kind"": ""signal"", ""behavior"": ""Camera Operator"", ""direction"": ""input"", ""params"": [{""name"": ""Target"", ""type"": ""GameObject""}], ""description"": ""Tells Camera Operator to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Target", typeof(GameObject))]
        public readonly SignalHandle<GameObject> SetObjectToFollow;
        [SchematicDoc(@"{""name"": ""LookInput"", ""kind"": ""signal"", ""behavior"": ""Camera Operator"", ""direction"": ""input"", ""params"": [{""name"": ""Look"", ""type"": ""Vector2""}], ""description"": ""Tells Camera Operator to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Look", typeof(Vector2))]
        public readonly SignalHandle<Vector2> LookInput;

        [SchematicDoc(@"{""name"": ""AimDirectionOutput"", ""kind"": ""signal"", ""behavior"": ""Camera Operator"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""A direction value is passed, which is the horizontal direction the Camera is facing."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("A direction value is passed, which is the horizontal direction the Camera is facing.")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimDirectionOutput;
        [SchematicDoc(@"{""name"": ""OrientCharacterToCameraOutput"", ""kind"": ""signal"", ""behavior"": ""Camera Operator"", ""direction"": ""output"", ""params"": [{""name"": ""Orient"", ""type"": ""bool""}], ""description"": ""A Boolean that tells the Rig that the Character Should look toward the AimDirectionOutput value."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("A Boolean that tells the Rig that the Character Should look toward the AimDirectionOutput value.")]
        [OutputSignal, Parameter("Orient", typeof(bool))]
        public readonly SignalHandle<bool> OrientCharacterToCameraOutput;

        [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""CameraOperatorProperties"", ""default"": ""null"", ""description"": ""Properties (CameraOperatorProperties) of Camera Operator.""}")]
        public readonly VariableHandle<CameraOperatorProperties> Properties = (CameraOperatorProperties)null;
        [SchematicDoc(@"{""name"": ""Follow"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""What the camera follows. Empty: in Play Mode, the root of the Schematic Object the camera is on.""}")]
        [Tooltip("What the camera follows. Empty: in Play Mode, the root of the Schematic Object the camera is on.")]
        public readonly VariableHandle<Transform> Follow = (Transform)null;
        [SchematicDoc(@"{""name"": ""Root"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Root (Transform) of Camera Operator.""}")]
        public readonly VariableHandle<Transform> Root = (Transform)null;
        [SchematicDoc(@"{""name"": ""Camera"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Camera (Transform) of Camera Operator.""}")]
        public readonly VariableHandle<Transform> Camera = (Transform)null;

        [SchematicDoc(@"{""name"": ""CameraCollisionRadius"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0.2f"", ""description"": ""Radius of the Sphere used for SphereCasting to set the distance away from the player""}")]
        [Tooltip("Radius of the Sphere used for SphereCasting to set the distance away from the player")]
        public readonly VariableHandle<float> CameraCollisionRadius = 0.2f;

        /// <summary>The Camera Properties Volume this Operator is inside, if any.</summary>
        [SchematicDoc(@"{""name"": ""CurrentVolume"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""BehaviorInstance"", ""default"": ""null"", ""description"": ""The Camera Properties Volume this Operator is inside, if any.""}")]
        public readonly VariableHandle<BehaviorInstance> CurrentVolume = (BehaviorInstance)null;
        [SchematicDoc(@"{""name"": ""AimDirection"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""Vector3"", ""default"": ""Vector3.forward"", ""description"": ""Aim Direction (Vector3) of Camera Operator.""}")]
        public readonly VariableHandle<Vector3> AimDirection = Vector3.forward;
        [SchematicDoc(@"{""name"": ""OrientCharacterToCamera"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Orient Character To Camera (bool) of Camera Operator.""}")]
        public readonly VariableHandle<bool> OrientCharacterToCamera = false;

        // Rotation state, read by the Camera Manager to keep inactive Operators in step with the active one.
        [SchematicDoc(@"{""name"": ""XAxis"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""X Axis (float) of Camera Operator.""}")]
        public readonly VariableHandle<float> XAxis = 0f;
        [SchematicDoc(@"{""name"": ""YAxis"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""Y Axis (float) of Camera Operator.""}")]
        public readonly VariableHandle<float> YAxis = 0f;
        [SchematicDoc(@"{""name"": ""ActualXAxis"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""Actual X Axis (float) of Camera Operator.""}")]
        public readonly VariableHandle<float> ActualXAxis = 0f;
        [SchematicDoc(@"{""name"": ""ActualYAxis"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""Actual Y Axis (float) of Camera Operator.""}")]
        public readonly VariableHandle<float> ActualYAxis = 0f;

        protected readonly VariableHandle<float> _xAxisInput = 0f;
        protected readonly VariableHandle<float> _yAxisInput = 0f;
        protected readonly VariableHandle<float> _timeSinceInput = 0f;
        protected readonly VariableHandle<Quaternion> _goalRotation = Quaternion.identity;

        [SchematicDoc(@"{""name"": ""AlignToTargetUp"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Tilt the camera with what it follows: its up turns toward the target's up over time, so on slopes, ramps and walls the camera leans with it.""}")]
        [Tooltip("Tilt the camera with what it follows: its up turns toward the target's up over time, so on slopes, ramps and walls the camera leans with it.")]
        public readonly VariableHandle<bool> AlignToTargetUp = true;
        [SchematicDoc(@"{""name"": ""UpAlignSpeed"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""3f"", ""description"": ""How quickly the camera's up follows the target's (higher is snappier).""}")]
        [Tooltip("How quickly the camera's up follows the target's (higher is snappier).")]
        public readonly VariableHandle<float> UpAlignSpeed = 3f;
        [SchematicDoc(@"{""name"": ""GroundProbeDistance"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0.5f"", ""description"": ""How far below the bottom of the target's bounds (down its up) ground is looked for. With none there (jumping, falling), the camera keeps the up of the last ground instead of tilting with the target.""}")]
        [Tooltip("How far below the bottom of the target's bounds (down its up) ground is looked for. With none there (jumping, falling), the camera keeps the up of the last ground instead of tilting with the target.")]
        public readonly VariableHandle<float> GroundProbeDistance = 0.5f;
        [SchematicDoc(@"{""name"": ""HeadingRecovery"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""float"", ""default"": ""0.5f"", ""description"": ""After trips over walls and loops, how quickly the camera's idea of straight ahead settles back to the world's once it's on level-ish ground (the view doesn't move; 0: never).""}")]
        [Tooltip("After trips over walls and loops, how quickly the camera's idea of straight ahead settles back to the world's once it's on level-ish ground (the view doesn't move; 0: never).")]
        public readonly VariableHandle<float> HeadingRecovery = 0.5f;

        /// <summary>Within this angle of world up, ground counts as level for <see cref="HeadingRecovery"/>.</summary>
        protected const float LevelGroundAngle = 30f;

        // The camera's up, easing toward the target's, and the frame it carries: rolled along with the up a little
        // each frame (never rebuilt from world up), so headings stay continuous on walls, ceilings and loops.
        protected readonly VariableHandle<Vector3> _up = Vector3.up;
        protected readonly VariableHandle<Quaternion> _frame = Quaternion.identity;
        // The target's up the last time there was ground under it: what the camera eases toward.
        protected readonly VariableHandle<Vector3> _groundUp = Vector3.up;

        /// <summary>
        /// Eases the camera's up toward the target's (world up with Align To Target Up off) and returns the frame
        /// the rig's yaw and pitch are built in: <c>frame * Quaternion.Euler(pitch, yaw, 0)</c>. The frame's up is
        /// <see cref="CameraUp"/>; "height", "falling" and "level" are all measured along it.
        /// </summary>
        protected Quaternion UpFrame(Transform follow, Box3DBody body, LayerMask groundLayers)
        {
            ref var up = ref _up.Ref;
            ref var frame = ref _frame.Ref;
            if (up.sqrMagnitude < 0.5f)
            {
                up = Vector3.up;
                frame = Quaternion.identity;
            }
            var goal = Vector3.up;
            if (AlignToTargetUp.Value && follow != null)
            {
                // Only ground the target stands on tilts the camera: in the air it keeps the last ground's up.
                ref var groundUp = ref _groundUp.Ref;
                if (groundUp.sqrMagnitude < 0.5f)
                    groundUp = Vector3.up;
                if (HasGroundBelow(follow, body, groundLayers))
                    groundUp = follow.up;
                goal = groundUp;
            }
            var next = Vector3.Slerp(up, goal, 1f - Mathf.Exp(-UpAlignSpeed.Value * Time.deltaTime)).normalized;
            frame = (Quaternion.FromToRotation(up, next) * frame).normalized;
            up = next;

            // Back on level-ish ground, the frame's "ahead" drifts back to the world's. It turns about the frame's own
            // up and the heading turns the other way by the same amount, so frame * Euler(pitch, heading) -- the view --
            // stays exactly where it is.
            float recovery = HeadingRecovery.Value;
            if (recovery > 0f && Vector3.Angle(up, Vector3.up) < LevelGroundAngle)
            {
                var level = Quaternion.FromToRotation(Vector3.up, up);
                float twist = Vector3.SignedAngle(level * Vector3.forward, frame * Vector3.forward, up);
                if (Mathf.Abs(twist) > 0.01f)
                {
                    float turn = twist * (1f - Mathf.Exp(-recovery * Time.deltaTime));
                    frame = (frame * Quaternion.Euler(0f, -turn, 0f)).normalized;
                    XAxis.Value += turn;
                    ActualXAxis.Value += turn;
                }
            }
            return frame;
        }

        /// <summary>
        /// Whether there's ground just below the bottom of a target's <see cref="SchematicBounds"/>, looking down its up
        /// (within <see cref="GroundProbeDistance"/>); the target's own body isn't counted.
        /// </summary>
        protected bool HasGroundBelow(Transform target, Box3DBody body, LayerMask layers)
        {
            var up = target.up;
            var bottom = BoundsBottom(target, up);
            const float skin = 0.1f, radius = 0.15f;
            return CameraCast(bottom + up * (skin + radius), radius, -up, skin + Mathf.Max(0f, GroundProbeDistance.Value), layers, target, out _);
        }

        // The bottom of the target along its up: from its Schematic Bounds (its physics shapes and what its parts push,
        // like a car's tyres), or its position when it has none.
        private static Vector3 BoundsBottom(Transform target, Vector3 up)
        {
            var bounds = SchematicBounds.Of(target.gameObject);
            return bounds != null && bounds.HasBox ? bounds.Lowest(up) : target.position;
        }

        /// <summary>The camera's current (eased) up.</summary>
        protected Vector3 CameraUp => _up.Value.sqrMagnitude > 0.5f ? _up.Value : Vector3.up;

        /// <summary>A direction's heading (degrees) in a frame: what XAxis would be to face it.</summary>
        protected static float YawIn(Quaternion frame, Vector3 direction)
        {
            var local = Quaternion.Inverse(frame) * direction;
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// A direction's heading (degrees) in an Operator's current frame: what its XAxis would be to face it (the
        /// Camera Manager hands a Reference Camera's heading back with it).
        /// </summary>
        public float FrameYaw(BehaviorInstance instance, Vector3 direction)
        {
            var frame = _frame[instance];
            return YawIn(frame.w == 0f && frame.x == 0f && frame.y == 0f && frame.z == 0f ? Quaternion.identity : frame, direction);
        }

        /// <summary>
        /// Moves a position toward a target, across the camera's up at one speed and along it (toward
        /// <paramref name="goalHeight"/>, measured along the up) at another.
        /// </summary>
        protected static Vector3 FollowAcrossAndAlong(Vector3 position, Vector3 target, float goalHeight, Vector3 up,
            float acrossSpeed, float alongSpeed, float dt)
        {
            var across = Vector3.Lerp(Vector3.ProjectOnPlane(position, up), Vector3.ProjectOnPlane(target, up), acrossSpeed * dt);
            float height = Mathf.Lerp(Vector3.Dot(position, up), goalHeight, alongSpeed * dt);
            return across + up * height;
        }

        public float XAxisSpeed => Properties.Value != null ? Properties.Value.XAxisSpeed : 3f;
        public float YAxisSpeed => Properties.Value != null ? Properties.Value.YAxisSpeed : 2f;
        public float SmoothSpeed => Properties.Value != null ? Properties.Value.SmoothSpeed : 10f;
        /// <summary>If True, the Camera Manager refers to this Operator's Current Volume for Camera Properties.</summary>
        public bool UseVolumes => Properties.Value != null && Properties.Value.UseVolumes;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            // The rig: Root at the object, Camera on a boom behind it.
            var root = new GameObject("Root").transform;
            root.SetParent(context.Transform, false);

            var camera = new GameObject("Camera").transform;
            camera.gameObject.AddComponent<UnityEngine.Camera>();
            camera.SetParent(root, false);
            camera.localPosition = new Vector3(0, 0, -1);
            camera.rotation = Quaternion.LookRotation(root.position - camera.position, Vector3.up);

            context.Set(Root, root);
            context.Set(Camera, camera);
        }

        protected override void OnStart()
        {
            var instance = Self;
            LookInput.Subscribe(axis => Look(instance, axis));
            SetObjectToFollow.Subscribe(target => Follow.Value = target != null ? target.transform : null);
            DetachRig();
        }

        /// <summary>
        /// At play time the rig (Root, and the Camera under it) moves out to the top of the hierarchy, so it isn't
        /// carried around (or scaled, or turned) by the object it's on. Only its place in the hierarchy changes: Root
        /// and Camera still refer to it, it's switched on and off with the Operator, and destroyed with it.
        /// </summary>
        private void DetachRig()
        {
            var root = Root.Value;
            if (root == null || !Application.isPlaying || root.parent == null)
                return;
            root.SetParent(null, worldPositionStays: true);
            root.name = $"{Self.GameObject.name} (Camera Rig)";
        }

        protected override void OnEnable()
        {
            var root = Root.Value;
            if (root != null && root.parent == null)
                root.gameObject.SetActive(true);

            var camera = Camera.Value;
            if (camera != null && camera.TryGetComponent(out UnityEngine.Camera cameraComponent))
                cameraComponent.enabled = false; // the Camera Manager's camera renders; this one is a target

            CameraManager.AddOperator(Self);
            CameraManager.SetCameraOperator(Self);
        }

        protected override void OnDisable()
        {
            CameraManager.RemoveOperator(Self);
            // A detached rig goes with its Operator (it's no longer under it in the hierarchy).
            var root = Root.Value;
            if (root != null && root.parent == null)
                root.gameObject.SetActive(false);
        }

        protected override void OnDestroy()
        {
            var root = Root.Value;
            if (root != null && root.parent == null && Application.isPlaying)
                Object.Destroy(root.gameObject);
        }

        /// <summary>
        /// Updates the selected Operator's aim direction (level with the camera's up, so it runs along walls and slopes)
        /// and applies pending look input.
        /// </summary>
        public void UpdateAimDirection()
        {
            var camera = Camera.Value;
            if (camera == null) return;

            AimDirection.Value = Vector3.ProjectOnPlane(camera.forward, CameraUp).normalized;
            AimDirectionOutput.Invoke(AimDirection.Value);

            XAxis.Value += _xAxisInput.Value * (XAxisSpeed * Time.deltaTime);
            YAxis.Value = Mathf.Clamp(YAxis.Value - _yAxisInput.Value * (YAxisSpeed * Time.deltaTime), -80f, 80f); // clamp pitch

            _xAxisInput.Value = 0;
            _yAxisInput.Value = 0;
        }

        /// <summary>Snaps an Operator's rotation.</summary>
        public void ForceAxis(BehaviorInstance instance, float xAxis, float yAxis)
        {
            XAxis[instance] = xAxis;
            ActualXAxis[instance] = xAxis;
            YAxis[instance] = yAxis;
            ActualYAxis[instance] = yAxis;
        }

        public void Look(BehaviorInstance instance, Vector2 axis)
        {
            _xAxisInput[instance] = axis.x;
            _yAxisInput[instance] = axis.y;

            if (axis != Vector2.zero)
                _timeSinceInput[instance] = 0;
        }

        // Which Camera Properties Volume the Operator is in is set by the Camera Manager each frame (from the box
        // around what it follows), so it works without physics triggers.

        /// <summary>
        /// The Box3D body of what the Operator follows (the character), or <paramref name="assigned"/> when set: its
        /// casts skip it (so the camera never hits the character), and its velocity drives look-ahead.
        /// </summary>
        /// <summary>
        /// What the Operator follows: <see cref="Follow"/>, else (in Play Mode) the root of the Schematic Object it's
        /// on, which then becomes its Follow.
        /// </summary>
        protected Transform FollowTarget()
        {
            var follow = Follow.Value;
            if (follow == null && Application.isPlaying && Self.Owner != null)
                Follow.Value = follow = Self.Owner.transform;
            return follow;
        }

        /// <summary>
        /// Moves the Operator's own object to the rig's Root (without moving the Root), so the object is where the
        /// camera pivots. Not when the Operator is on what it follows, on something holding it, or inside it (a part of
        /// the character): that would drag the character, or a piece of it, along with the camera.
        /// </summary>
        protected void KeepObjectAtRoot(Transform root, Transform follow)
        {
            var self = Self.Transform;
            if (follow == self || follow.IsChildOf(self) || self.IsChildOf(follow))
                return;
            var rootPosition = root.position;
            self.position = rootPosition;
            root.position = rootPosition;
        }

        private static readonly RaycastHit[] _castHits = new RaycastHit[64];
        private static readonly GameObject[] _castObjects = new GameObject[64];

        /// <summary>
        /// The camera's sweeps (radius 0: a ray): the nearest hit, skipping everything that's part of what it follows
        /// (every body and child of its Schematic Object, wherever the physics body sits), trigger-only shapes, and
        /// anything the sweep starts inside (there's no getting away from those, and counting them kept the camera
        /// pulled in for good).
        /// </summary>
        protected static bool CameraCast(Vector3 origin, float radius, Vector3 direction, float distance, LayerMask mask,
            Transform follow, out RaycastHit hit)
        {
            hit = default;
            if (distance <= 0f || direction.sqrMagnitude < 1e-8f)
                return false;
            Transform own = null;
            if (follow != null)
            {
                var schematic = follow.GetComponentInParent<SchematicObject>();
                own = schematic != null ? schematic.transform : follow;
            }
            int count = SchematicPhysics.SphereCastAll(origin, radius, direction, distance, mask, _castHits, _castObjects, null, includeSensors: false);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_castHits[i].distance <= 0.001f)
                    continue;
                var hitObject = _castObjects[i];
                if (own != null && hitObject != null && hitObject.transform.IsChildOf(own))
                    continue;
                if (_castHits[i].distance < best)
                {
                    best = _castHits[i].distance;
                    hit = _castHits[i];
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>How far the aim ray looks for what the camera's pointing at, in meters.</summary>
        protected const float AimDistance = 1000f;

        protected static Box3DBody BodyOf(Transform follow, Box3DBody assigned)
            => assigned != null ? assigned : follow != null ? follow.GetComponentInParent<Box3DBody>() : null;

        /// <summary>The selected Operator's current volume settings, if it is inside one.</summary>
        protected bool TryGetVolume(out CameraPropertiesVolume volume, out BehaviorInstance volumeInstance)
        {
            volumeInstance = CurrentVolume.Value;
            volume = volumeInstance != null && volumeInstance.IsSpawned ? (CameraPropertiesVolume)volumeInstance.Behavior : null;
            return volume != null;
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var root = context.Get(Root);
            var camera = context.Get(Camera);
            if (root == null || camera == null)
                return;

            // Boom arm
            Gizmos.color = Color.cyan;
            DrawWireCylinder(root.position, camera.position, context.Get(CameraCollisionRadius));

            // Camera
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(camera.position, 0.2f);
        }

        private static void DrawWireCylinder(Vector3 start, Vector3 end, float radius)
        {
            const int segments = 20;
            if (start == end) return;
            Vector3 up = (end - start).normalized;

            float height = Vector3.Distance(start, end);
            Quaternion rotation = Quaternion.LookRotation(end - start);

            Vector3[] points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * Mathf.PI * i / segments;
                Vector3 circlePoint = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
                points[i] = start + rotation * circlePoint;
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                Gizmos.DrawLine(points[i], points[next]);
                Gizmos.DrawLine(points[i], points[i] + up * height);
                Gizmos.DrawLine(points[i] + up * height, points[next] + up * height);
            }
        }
    }
}
