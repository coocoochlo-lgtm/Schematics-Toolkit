/*using SaintsField;
using SaintsField.Playa;*/
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Weapons.Projectiles
{
    [SchematicBehaviorPath("Weapons/Projectile")]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class Projectile : SchematicBehavior
    {
        [Tooltip("On hit anything collidable, passes the PhysicsMaterial of the collider that was hit.")]
        [OutputSignal, Parameter("Material", typeof(PhysicsMaterial))]
        public readonly SignalHandle<PhysicsMaterial> OnHit;
        [Tooltip("On hit the Target Object, passes the reference of the Target Object that was hit.")]
        [OutputSignal, Parameter("Target", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnHitTarget;
        [Tooltip("On Hit of anything collidable, and the projectile bounced off, passes the PhysicsMaterial of the hit Collider")]
        [OutputSignal, Parameter("Material", typeof(PhysicsMaterial))]
        public readonly SignalHandle<PhysicsMaterial> OnRicochet;
        [Tooltip("Called when this is destroyed")]
        [OutputSignal]
        public readonly SignalHandle OnDestroyed;

        public readonly VariableHandle<ProjectileData> Data = (ProjectileData)null;

        [Tooltip("Used for magnetization or orientation to the Target object.")]
        public readonly VariableHandle<Transform> Target = (Transform)null;
        [Tooltip("The position to interpolate toward until it has been reached.")]
        public readonly VariableHandle<Vector3> TargetPosition = Vector3.zero;
        [Tooltip("Speed of motion")]
        public readonly VariableHandle<Vector3> Velocity = Vector3.zero;
        // This should probably be deleted, Collisions should be handled by collision layer only
        [Tooltip("The current number of collisions")]
        public readonly VariableHandle<int> CollisionCount = 0;

        /// <summary>The Fire Mode that shot this Projectile. Set by the Weapon.</summary>
        public readonly VariableHandle<FireMode> ParentFireMode = (FireMode)null;
        public readonly VariableHandle<Vector3> AimTargetPosition = Vector3.zero;

        private readonly VariableHandle<float> _timeAlive = 0f;
        private readonly VariableHandle<float> _distanceTraveled = 0f;
        // Launched on the first update, after the Weapon has finished orienting the spawned Projectile.
        private readonly VariableHandle<bool> _launched = false;

        /// <summary>
        /// The Projectile Data a projectile prefab is set up with, for reading it before one is spawned.
        /// </summary>
        public static ProjectileData DataOf(SchematicObject prefab)
        {
            var behavior = ExecutionContext.GetRuntime<Projectile>().Behavior as Projectile;
            return behavior != null ? behavior.Data.DefaultFor(prefab) : null;
        }

        protected override void OnEnable()
        {
            var data = Data.Value;
            var rigidbody = Self.GetComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            Self.GetComponent<SphereCollider>().isTrigger = true;
            if (data != null)
                Self.GameObject.layer = data.Layer;
        }

        protected override void OnDisable()
        {
            // Pooled projectiles are reused, so drop the joint a Fixed projectile was attached with.
            var joint = Self.GetComponent<FixedJoint>();
            if (joint != null)
                Object.Destroy(joint);
        }

        protected override void OnUpdate()
        {
            var data = Data.Value;
            if (data == null)
                return;

            var transform = Self.Transform;
            ref var velocity = ref Velocity.Ref;

            if (!_launched.Value)
            {
                _launched.Value = true;
                velocity = data.InitialVelocity * transform.forward;
            }

            ref var distanceTraveled = ref _distanceTraveled.Ref;
            distanceTraveled += (velocity * Time.deltaTime).magnitude;

            if (distanceTraveled < data.LineUpDistance)
            {
                var aimTarget = AimTargetPosition.Value;
                transform.position = Vector3.Lerp(transform.position, aimTarget, distanceTraveled / data.LineUpDistance);

                if (Vector3.Distance(transform.position, aimTarget) < 1)
                    distanceTraveled = data.LineUpDistance;
            }

            var radius = Self.GetComponent<SphereCollider>().radius;
            if (Physics.SphereCast(transform.position, radius, velocity.normalized, out RaycastHit hit, velocity.magnitude * Time.deltaTime))
                Hit(Self, hit.collider);

            transform.position += velocity * Time.deltaTime;

            ref var timeAlive = ref _timeAlive.Ref;
            timeAlive += Time.deltaTime;

            if (timeAlive > data.LifeSpan)
                Despawn(Self);
        }

        protected override void OnTriggerEnter(Collider other) => Hit(Self, other);

        private void Hit(BehaviorInstance instance, Collider other)
        {
            var data = Data.Value;
            if (data == null)
                return;

            bool isTarget = data.TargetingMode == ProjectileData.TargetMode.MatchingTag
                ? other.gameObject.CompareTag(data.TargetTag)
                : Target.Value != null && other.gameObject == Target.Value.gameObject;

            if (isTarget)
                OnHitTarget.Invoke(instance, other.gameObject);

            OnHit.Invoke(instance, other.sharedMaterial);
        }

        /// <summary>
        /// Sends On Destroyed and returns the Projectile to its pool.
        /// </summary>
        public void Despawn(BehaviorInstance instance)
        {
            OnDestroyed.Invoke(instance);
            SchematicsManager.Destroy(instance.Owner);
        }

        // Draws a Gizmo displaying the path to the next frame's position
        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            if (!Application.isPlaying || context.Instance == null)
                return;

            var collider = context.GetComponent<SphereCollider>();
            float radius = collider ? collider.radius : 0.1f;
            var velocity = context.Get(Velocity);
            var position = context.Transform.position;

            Gizmos.color = Color.red;

            Vector3 nextPosition = position + velocity * Time.deltaTime;
            float castDistance = velocity.magnitude * Time.deltaTime;

            Gizmos.DrawWireSphere(position, radius);
            Gizmos.DrawLine(position, nextPosition);

            if (Physics.SphereCast(position, radius, velocity.normalized, out RaycastHit hit, castDistance))
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(hit.point, radius);
            }
            else
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(nextPosition, radius);
            }
        }
    }
}
