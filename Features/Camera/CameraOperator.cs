using UnityEngine;

namespace Remedy.Cameras
{
    /// <summary>
    /// The base of a camera rig: a Root that follows a target, with the Camera on a boom beneath it.
    /// The Camera Manager blends the scene camera toward the active Operator.
    /// </summary>
    public abstract class CameraOperator : SchematicBehavior
    {
        [InputSignal, Parameter("Target", typeof(GameObject))]
        public readonly SignalHandle<GameObject> SetObjectToFollow;
        [InputSignal, Parameter("Look", typeof(Vector2))]
        public readonly SignalHandle<Vector2> LookInput;

        [Tooltip("A direction value is passed, which is the horizontal direction the Camera is facing.")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimDirectionOutput;
        [Tooltip("A Boolean that tells the Rig that the Character Should look toward the AimDirectionOutput value.")]
        [OutputSignal, Parameter("Orient", typeof(bool))]
        public readonly SignalHandle<bool> OrientCharacterToCameraOutput;

        public readonly VariableHandle<CameraOperatorProperties> Properties = (CameraOperatorProperties)null;
        public readonly VariableHandle<Transform> Follow = (Transform)null;
        public readonly VariableHandle<Transform> Root = (Transform)null;
        public readonly VariableHandle<Transform> Camera = (Transform)null;

        [Tooltip("Radius of the Sphere used for SphereCasting to set the distance away from the player")]
        public readonly VariableHandle<float> CameraCollisionRadius = 0.2f;

        /// <summary>The Camera Properties Volume this Operator is inside, if any.</summary>
        public readonly VariableHandle<BehaviorInstance> CurrentVolume = (BehaviorInstance)null;
        public readonly VariableHandle<Vector3> AimDirection = Vector3.forward;
        public readonly VariableHandle<bool> OrientCharacterToCamera = false;

        // Rotation state, read by the Camera Manager to keep inactive Operators in step with the active one.
        public readonly VariableHandle<float> XAxis = 0f;
        public readonly VariableHandle<float> YAxis = 0f;
        public readonly VariableHandle<float> ActualXAxis = 0f;
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

            if (!context.GameObject.TryGetComponent(out Rigidbody rigidbody))
                rigidbody = context.GameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            if (!context.GameObject.TryGetComponent(out SphereCollider _))
                context.GameObject.AddComponent<SphereCollider>();

            context.Set(Root, root);
            context.Set(Camera, camera);
        }

        protected override void OnStart(BehaviorInstance instance)
        {
            LookInput.Subscribe(axis => Look(instance, axis));
            SetObjectToFollow.Subscribe(target => Follow.Value = target != null ? target.transform : null);
        }

        protected override void OnEnable(BehaviorInstance instance)
        {
            var camera = Camera.Value;
            if (camera != null && camera.TryGetComponent(out UnityEngine.Camera cameraComponent))
                cameraComponent.enabled = false; // the Camera Manager's camera renders; this one is a target

            CameraManager.AddOperator(instance);
            CameraManager.SetCameraOperator(instance);
        }

        protected override void OnDisable(BehaviorInstance instance)
        {
            CameraManager.RemoveOperator(instance);
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

        // Camera Properties Volumes the Operator moves through.
        protected override void OnTriggerEnter(BehaviorInstance instance, Collider other)
        {
            var volume = ExecutionContext.FindInstance(other.gameObject, typeof(CameraPropertiesVolume));
            if (volume != null)
                CurrentVolume.Value = volume;
        }

        protected override void OnTriggerExit(BehaviorInstance instance, Collider other)
        {
            var volume = ExecutionContext.FindInstance(other.gameObject, typeof(CameraPropertiesVolume));
            if (volume != null && volume == CurrentVolume.Value)
                CurrentVolume.Value = null;
        }

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
