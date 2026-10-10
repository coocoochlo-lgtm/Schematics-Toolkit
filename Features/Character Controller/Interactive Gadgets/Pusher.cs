using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Remedy.CharacterControllers.Gadgets
{
    /// <summary>
    /// Launches Rigidbodies that touch it along its up axis, after a short delay, squashing while it does.
    /// </summary>
    [SchematicBehaviorPath("Level Design/Pusher")]
    [RequireComponent(typeof(SphereCollider))]
    public class Pusher : SchematicBehavior
    {
        [Tooltip("Invoked when a Rigidbody Collides with this.")]
        [OutputSignal, Parameter("Hit", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnHit;

        public readonly VariableHandle<float> Force = 0f;
        public readonly VariableHandle<Vector3> ScaleOnHit = Vector3.one;
        [Tooltip("Milliseconds before the push is applied.")]
        public readonly VariableHandle<int> Delay = 30;

        private readonly VariableHandle<Vector3> _defaultScale = Vector3.one;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            var collider = context.GameObject.GetComponent<SphereCollider>();
            if (collider != null)
                collider.isTrigger = true;
        }

        protected override void OnEnable(BehaviorInstance instance)
        {
            _defaultScale.Value = instance.Transform.localScale;
        }

        protected override void OnTriggerEnter(BehaviorInstance instance, Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null)
                return;

            ApplyForce(instance, rb, instance.Transform.up * Force.Value, Delay.Value, _defaultScale.Value).Forget();
            instance.Transform.localScale = Vector3.Scale(_defaultScale.Value, ScaleOnHit.Value);

            OnHit.Invoke(instance, rb.gameObject);
        }

        private static async UniTaskVoid ApplyForce(BehaviorInstance instance, Rigidbody rb, Vector3 velocity, int delay, Vector3 defaultScale)
        {
            int uid = instance.UID;
            await UniTask.Delay(delay);

            if (rb != null)
                rb.linearVelocity = velocity;
            if (instance.IsSpawnedAs(uid))
                instance.Transform.localScale = defaultScale;
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
#if UNITY_EDITOR
            if (UnityEditor.Selection.activeGameObject != context.GameObject) return;
            Gizmos.color = Color.cyan;

            Vector3 initialVelocity = context.Transform.up * context.Get(Force);

            Vector3[] trajectoryPoints = CharacterTrajectoryCalculator.CalculateTrajectory(
                context.Transform.position,
                initialVelocity,
                timeStep: Time.fixedDeltaTime,
                maxTime: 3f,
                maxPoints: 200,
                true
            );

            for (int i = 0; i < trajectoryPoints.Length - 1; i++)
            {
                Gizmos.DrawLine(trajectoryPoints[i], trajectoryPoints[i + 1]);
            }

            if (trajectoryPoints.Length > 1)
            {
                Vector3 end = trajectoryPoints[^1];
                Vector3 prev = trajectoryPoints[^2];
                Vector3 direction = (end - prev).normalized;

                float headSize = 0.5f;
                Vector3 right = Quaternion.LookRotation(direction) * Quaternion.Euler(0, 150, 0) * Vector3.forward;
                Vector3 left = Quaternion.LookRotation(direction) * Quaternion.Euler(0, -150, 0) * Vector3.forward;

                Gizmos.DrawLine(end, end + right * headSize);
                Gizmos.DrawLine(end, end + left * headSize);
            }
#endif
        }
    }
}
