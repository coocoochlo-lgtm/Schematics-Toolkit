using Remedy.Schematics;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Remedy.CharacterControllers.Gadgets
{
    /// <summary>
    /// Launches Rigidbodies that touch it along its up axis, after a short delay, squashing while it does.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Pusher"", ""kind"": ""behavior"", ""menu"": ""Level Design/Pusher"", ""description"": ""Launches Rigidbodies that touch it along its up axis, after a short delay, squashing while it does."", ""variables"": [""Force"", ""ScaleOnHit"", ""Delay""], ""signals"": [""OnHit""], ""use_for"": [""jump pads"", ""launchers"", ""bouncers""]}")]
    [SchematicBehaviorPath("Level Design/Pusher")]
    [RequireComponent(typeof(SphereCollider))]
    public class Pusher : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""OnHit"", ""kind"": ""signal"", ""behavior"": ""Pusher"", ""direction"": ""output"", ""params"": [{""name"": ""Hit"", ""type"": ""GameObject""}], ""description"": ""Invoked when a Rigidbody Collides with this."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Invoked when a Rigidbody Collides with this.")]
        [OutputSignal, Parameter("Hit", typeof(GameObject))]
        [NetworkSync(SignalSender.Authority)]
        public readonly SignalHandle<GameObject> OnHit;

        [SchematicDoc(@"{""name"": ""Force"", ""kind"": ""variable"", ""behavior"": ""Pusher"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""Force (float) of Pusher.""}")]
        public readonly VariableHandle<float> Force = 0f;
        [SchematicDoc(@"{""name"": ""ScaleOnHit"", ""kind"": ""variable"", ""behavior"": ""Pusher"", ""type"": ""Vector3"", ""default"": ""Vector3.one"", ""description"": ""Scale On Hit (Vector3) of Pusher.""}")]
        public readonly VariableHandle<Vector3> ScaleOnHit = Vector3.one;
        [SchematicDoc(@"{""name"": ""Delay"", ""kind"": ""variable"", ""behavior"": ""Pusher"", ""type"": ""int"", ""default"": ""30"", ""description"": ""Milliseconds before the push is applied.""}")]
        [Tooltip("Milliseconds before the push is applied.")]
        public readonly VariableHandle<int> Delay = 30;

        private readonly VariableHandle<Vector3> _defaultScale = Vector3.one;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            var collider = context.GameObject.GetComponent<SphereCollider>();
            if (collider != null)
                collider.isTrigger = true;
        }

        protected override void OnEnable()
        {
            _defaultScale.Value = Self.Transform.localScale;
        }

        protected override void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null)
                return;

            ApplyForce(Self, rb, Self.Transform.up * Force.Value, Delay.Value, _defaultScale.Value).Forget();
            Self.Transform.localScale = Vector3.Scale(_defaultScale.Value, ScaleOnHit.Value);

            OnHit.Invoke(Self, rb.gameObject);
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
