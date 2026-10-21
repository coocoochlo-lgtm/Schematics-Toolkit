using Box3D.Hybrid;
using UnityEngine;

namespace Remedy.Cameras
{
    /// <summary>
    /// The base of a camera rig: a Root that follows a target, with the Camera on a boom beneath it.
    /// The Camera Manager blends the scene camera toward the active Operator.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Camera Operator"", ""kind"": ""behavior"", ""description"": ""The base of a camera rig: a Root that follows a target, with the Camera on a boom beneath it. The Camera Manager blends the scene camera toward the active Operator."", ""variables"": [""Properties"", ""Follow"", ""Root"", ""Camera"", ""CameraCollisionRadius"", ""CurrentVolume"", ""AimDirection"", ""OrientCharacterToCamera"", ""XAxis"", ""YAxis"", ""ActualXAxis"", ""ActualYAxis""], ""signals"": [""SetObjectToFollow"", ""LookInput"", ""AimDirectionOutput"", ""OrientCharacterToCameraOutput""], ""use_for"": [""custom camera rigs""], ""notes"": ""Base of the camera Behaviors; use Third Person or Shoulder instead.""}")]
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
        [SchematicDoc(@"{""name"": ""Follow"", ""kind"": ""variable"", ""behavior"": ""Camera Operator"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Follow (Transform) of Camera Operator.""}")]
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
        }

        protected override void OnEnable()
        {
            var camera = Camera.Value;
            if (camera != null && camera.TryGetComponent(out UnityEngine.Camera cameraComponent))
                cameraComponent.enabled = false; // the Camera Manager's camera renders; this one is a target

            CameraManager.AddOperator(Self);
            CameraManager.SetCameraOperator(Self);
        }

        protected override void OnDisable()
        {
            CameraManager.RemoveOperator(Self);
        }

        /// <summary>Updates the selected Operator's aim direction and applies pending look input.</summary>
        public void UpdateAimDirection()
        {
            var camera = Camera.Value;
            if (camera == null) return;

            AimDirection.Value = Vector3.ProjectOnPlane(camera.forward, Vector3.up).normalized;
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
