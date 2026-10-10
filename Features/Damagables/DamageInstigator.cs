using UnityEngine;

namespace Remedy.Damagables
{
    /// <summary>
    /// Deals damage, either to Damageables in a radius when Instigate is sent, or on contact depending on its Data.
    /// </summary>
    [SchematicBehaviorPath("Damageables/Instigator")]
    public class DamageInstigator : SchematicBehavior
    {
        [InputSignal] public readonly SignalHandle Instigate;
        [OutputSignal] public readonly SignalHandle OnInstigation;

        public readonly VariableHandle<DamageInstigationData> Data = (DamageInstigationData)null;

        protected override void OnStart(BehaviorInstance instance)
        {
            // Fires whatever the Signal carries.
            Instigate.Subscribe(() =>
            {
                var data = Data.Value;
                if (data == null) return;
                DamageManager.InstigateDamage(new(instance.Transform.position, data.ID));
                OnInstigation.Invoke();
            });
        }

        protected override void OnTriggerStay(BehaviorInstance instance, Collider other)
            => TryDamage(instance, other, DamageInstigationData.InstigationType.OnTriggerStay);

        protected override void OnCollisionEnter(BehaviorInstance instance, Collision collision)
            => TryDamage(instance, collision.collider, DamageInstigationData.InstigationType.OnCollisionEnter);

        protected override void OnTriggerEnter(BehaviorInstance instance, Collider other)
            => TryDamage(instance, other, DamageInstigationData.InstigationType.OnCollisionEnter);

        private void TryDamage(BehaviorInstance instance, Collider other, DamageInstigationData.InstigationType when)
        {
            var data = Data.Value;
            if (data == null || data.Instigation != when)
                return;

            var damageable = FindDamageable(other);
            if (damageable != null && damageable.DamageLayer.Value == data.DamageLayer)
            {
                damageable.Damage(new(instance.Transform.position, data.ID));
                OnInstigation.Invoke(instance);
            }
        }

        // The collider's own object, or the object its Rigidbody belongs to. Returned with that object selected.
        private static Damageable FindDamageable(Collider other)
        {
            var damageable = other.gameObject.GetSchematicBehavior<Damageable>();
            if (damageable == null && other.attachedRigidbody != null)
                damageable = other.attachedRigidbody.gameObject.GetSchematicBehavior<Damageable>();
            return damageable;
        }
    }
}
