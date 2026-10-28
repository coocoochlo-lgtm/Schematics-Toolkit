using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Damagables
{
    /// <summary>
    /// Deals damage, either to Damageables in a radius when Instigate is sent, or on contact depending on its Data.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Damage Instigator"", ""kind"": ""behavior"", ""menu"": ""Damageables/Instigator"", ""description"": ""Deals damage, either to Damageables in a radius when Instigate is sent, or on contact depending on its Data."", ""variables"": [""Data""], ""use_for"": [""hazards like spikes and lava"", ""explosions"", ""melee hits"", ""bullets dealing damage""]}")]
    [SchematicBehaviorPath("Damageables/Instigator")]
    public class DamageInstigator : SchematicBehavior
    {
        [InputSignal] public readonly SignalHandle Instigate;
        [OutputSignal] [NetworkSync(SignalSender.Authority)] public readonly SignalHandle OnInstigation;

        [SchematicDoc(@"{""name"": ""Data"", ""kind"": ""variable"", ""behavior"": ""Damage Instigator"", ""type"": ""DamageInstigationData"", ""default"": ""null"", ""description"": ""Its settings asset: how much damage, over how long, in what radius, on contact or when Instigate is sent.""}")]
        public readonly VariableHandle<DamageInstigationData> Data = (DamageInstigationData)null;

        protected override void OnStart()
        {
            var instance = Self;
            // Fires whatever the Signal carries.
            Instigate.Subscribe(() =>
            {
                var data = Data.Value;
                if (data == null) return;
                DamageManager.InstigateDamage(new(instance.Transform.position, data.ID));
                OnInstigation.Invoke();
            });
        }

        protected override void OnTriggerStay(Collider other)
            => TryDamage(Self, other, DamageInstigationData.InstigationType.OnTriggerStay);

        protected override void OnCollisionEnter(Collision collision)
            => TryDamage(Self, collision.collider, DamageInstigationData.InstigationType.OnCollisionEnter);

        protected override void OnTriggerEnter(Collider other)
            => TryDamage(Self, other, DamageInstigationData.InstigationType.OnCollisionEnter);

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
