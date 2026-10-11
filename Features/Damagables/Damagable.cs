using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Remedy.Damagables
{
    /// <summary>
    /// Health, damage and healing for an object. The methods act on the selected instance, e.g.
    /// <c>target.GetSchematicBehavior&lt;Damageable&gt;()?.Damage(instigation)</c>, or take the instance explicitly.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Damageable"", ""kind"": ""behavior"", ""menu"": ""Damageables/Damageable"", ""description"": ""Health, damage and healing for an object. The methods act on the selected instance, e.g. target.GetSchematicBehavior<Damageable>()?.Damage(instigation), or take the instance explicitly."", ""variables"": [""DamageLayer"", ""Health"", ""MaxHealth"", ""IsAlive""], ""signals"": [""OnKnockback""], ""use_for"": [""health for players, enemies and breakable objects"", ""death and healing""]}")]
    [SchematicBehaviorPath("Damageables/Damageable")]
    public class Damageable : SchematicBehavior
    {
        [OutputSignal] public readonly SignalHandle OnDamageBegin;
        [OutputSignal] public readonly SignalHandle OnHealBegin;
        [OutputSignal] public readonly SignalHandle OnHealInstigation;
        [OutputSignal] public readonly SignalHandle OnDamageContinued;
        [OutputSignal] public readonly SignalHandle OnHealContinued;
        [OutputSignal] public readonly SignalHandle OnDamageFinish;
        [OutputSignal] public readonly SignalHandle OnHealFinish;
        [SchematicDoc(@"{""name"": ""OnKnockback"", ""kind"": ""signal"", ""behavior"": ""Damageable"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Not handled by the Damageable. This functionality must be uniquely implemented for each object that uses this Component. The given value is the direction from the instigation position to this object."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Not handled by the Damageable. This functionality must be uniquely implemented for each object that uses this Component. The given value is the direction from the instigation position to this object.")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> OnKnockback;
        [OutputSignal] public readonly SignalHandle OnDeath;
        [OutputSignal] public readonly SignalHandle OnFullHealth;
        [OutputSignal] public readonly SignalHandle OnRevive;

        [SchematicDoc(@"{""name"": ""DamageLayer"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""int"", ""default"": ""0"", ""description"": ""Damage Layer (int) of Damageable.""}")]
        public readonly VariableHandle<int> DamageLayer = 0;
        [SchematicDoc(@"{""name"": ""Health"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""float"", ""default"": ""100f"", ""description"": ""Health (float) of Damageable.""}")]
        public readonly VariableHandle<float> Health = 100f;
        [SchematicDoc(@"{""name"": ""MaxHealth"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""float"", ""default"": ""100f"", ""description"": ""Max Health (float) of Damageable.""}")]
        public readonly VariableHandle<float> MaxHealth = 100f;
        [SchematicDoc(@"{""name"": ""IsAlive"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Is Alive (bool) of Damageable.""}")]
        public readonly VariableHandle<bool> IsAlive = true;

        protected override void OnEnable() => DamageManager.AddDamageable(Self, DamageLayer.Value);
        protected override void OnDisable() => DamageManager.RemoveDamageable(Self);

        /// <summary>Damages the selected Damageable.</summary>
        public void Damage(DamageInstigation instigation) => Damage(Selected, instigation);

        /// <summary>Heals the selected Damageable.</summary>
        public void Heal(DamageInstigation instigation) => Heal(Selected, instigation);

        /// <summary>Instantly kills the selected Damageable.</summary>
        public void Kill(DamageInstigation instigation) => Kill(Selected, instigation);

        /// <summary>Brings the selected Damageable back to full health and alive.</summary>
        public void Revive(DamageInstigation instigation) => Revive(Selected, instigation);

        public virtual void Damage(BehaviorInstance instance, DamageInstigation instigation)
        {
            OnDamageBegin.Invoke(instance);
            OnKnockback.Invoke(instance, instance.Transform.position - instigation.Position);

            if (!DamageInstigationData.Lookup.TryGetValue(instigation.DataID, out var data))
            {
                Debug.LogWarning($"DamageInstigationData with ID {instigation.DataID} not found!");
                return;
            }

            if (data.Duration > 0)
            {
                ApplyDamageOverTime(instance, data.Duration, data.Amount).Forget();
            }
            else
            {
                Health[instance] -= data.Amount;
                OnDamageFinish.Invoke(instance);

                if (Health[instance] <= 0f && IsAlive[instance])
                {
                    IsAlive[instance] = false;
                    OnDeath.Invoke(instance);
                }
            }
        }

        public virtual void Heal(BehaviorInstance instance, DamageInstigation instigation)
        {
            OnHealBegin.Invoke(instance);

            if (!DamageInstigationData.Lookup.TryGetValue(instigation.DataID, out var data))
            {
                Debug.LogWarning($"DamageInstigationData with ID {instigation.DataID} not found!");
                return;
            }

            if (data.Duration > 0)
            {
                ApplyHealingOverTime(instance, data.Duration, data.Amount).Forget();
            }
            else
            {
                Health[instance] = Mathf.Min(Health[instance] + data.Amount, MaxHealth[instance]);
                OnHealInstigation.Invoke(instance);

                if (Health[instance] == MaxHealth[instance])
                    OnFullHealth.Invoke(instance);
            }
        }

        public void Kill(BehaviorInstance instance, DamageInstigation instigation)
        {
            IsAlive[instance] = false;
            OnDeath.Invoke(instance);
        }

        public void Revive(BehaviorInstance instance, DamageInstigation instigation)
        {
            IsAlive[instance] = true;
            Health[instance] = MaxHealth[instance];
            OnRevive.Invoke(instance);
        }

        // Spans frames, so it works on the instance explicitly and stops if the object despawns or is reused.
        private async UniTaskVoid ApplyDamageOverTime(BehaviorInstance instance, float time, float amount)
        {
            int uid = instance.UID;
            int steps = Mathf.CeilToInt(time / Time.fixedDeltaTime);
            float damagePerStep = amount / steps;

            for (int i = 0; i < steps && instance.IsSpawnedAs(uid); i++)
            {
                Health[instance] -= damagePerStep;
                OnDamageContinued.Invoke(instance);

                if (Health[instance] <= 0f && IsAlive[instance])
                {
                    Health[instance] = 0f;
                    IsAlive[instance] = false;
                    OnDamageFinish.Invoke(instance);
                    OnDeath.Invoke(instance);
                    break;
                }

                await UniTask.WaitForFixedUpdate();
            }
        }

        private async UniTaskVoid ApplyHealingOverTime(BehaviorInstance instance, float time, float amount)
        {
            int uid = instance.UID;
            int steps = Mathf.CeilToInt(time / Time.fixedDeltaTime);
            float healPerStep = amount / steps;

            for (int i = 0; i < steps && instance.IsSpawnedAs(uid); i++)
            {
                Health[instance] = Mathf.Min(Health[instance] + healPerStep, MaxHealth[instance]);

                if (Health[instance] == MaxHealth[instance])
                {
                    OnHealFinish.Invoke(instance);
                    OnFullHealth.Invoke(instance);
                    return;
                }

                OnHealContinued.Invoke(instance);
                await UniTask.WaitForFixedUpdate();
            }

            if (instance.IsSpawnedAs(uid))
                OnHealFinish.Invoke(instance);
        }
    }
}
