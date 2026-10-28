using Remedy.Schematics;
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
        [Tooltip("Sent when the object is hit (before the damage is applied): flash, play a hurt sound.")]
        [OutputSignal] [NetworkSync(SignalSender.Authority)] public readonly SignalHandle OnDamageBegin;
        [Tooltip("Sent when healing starts.")]
        [OutputSignal] [NetworkSync(SignalSender.Authority)] public readonly SignalHandle OnHealBegin;
        [Tooltip("Sent when an instant heal is applied.")]
        [OutputSignal] public readonly SignalHandle OnHealInstigation;
        [Tooltip("Sent on each tick of damage over time.")]
        [OutputSignal] public readonly SignalHandle OnDamageContinued;
        [Tooltip("Sent on each tick of healing over time.")]
        [OutputSignal] public readonly SignalHandle OnHealContinued;
        [Tooltip("Sent when a hit (or damage over time) has finished being applied.")]
        [OutputSignal] public readonly SignalHandle OnDamageFinish;
        [Tooltip("Sent when healing over time has finished.")]
        [OutputSignal] public readonly SignalHandle OnHealFinish;
        [SchematicDoc(@"{""name"": ""OnKnockback"", ""kind"": ""signal"", ""behavior"": ""Damageable"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Not handled by the Damageable. This functionality must be uniquely implemented for each object that uses this Component. The given value is the direction from the instigation position to this object."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Not handled by the Damageable. This functionality must be uniquely implemented for each object that uses this Component. The given value is the direction from the instigation position to this object.")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        [NetworkSync(SignalSender.Authority)]
        public readonly SignalHandle<Vector3> OnKnockback;
        [Tooltip("Sent when its health runs out.")]
        [OutputSignal] [NetworkSync(SignalSender.Authority)] public readonly SignalHandle OnDeath;
        [Tooltip("Sent when healing brings it back to full health.")]
        [OutputSignal] [NetworkSync(SignalSender.Authority)] public readonly SignalHandle OnFullHealth;
        [Tooltip("Sent when the object is revived after dying.")]
        [OutputSignal] [NetworkSync(SignalSender.Authority)] public readonly SignalHandle OnRevive;

        [SchematicDoc(@"{""name"": ""DamageLayer"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""int"", ""default"": ""0"", ""description"": ""Which damage layer it's on (set up in the Damage Data): damage only reaches Damageables on the layers it's meant for (friendly fire, for example).""}")]
        public readonly VariableHandle<int> DamageLayer = 0;
        [SchematicDoc(@"{""name"": ""Health"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""float"", ""default"": ""100f"", ""description"": ""How much health it has right now.""}")]
        [NetworkSync]
        public readonly VariableHandle<float> Health = 100f;
        [SchematicDoc(@"{""name"": ""MaxHealth"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""float"", ""default"": ""100f"", ""description"": ""The most health it can have (healing stops here).""}")]
        [NetworkSync]
        public readonly VariableHandle<float> MaxHealth = 100f;
        [SchematicDoc(@"{""name"": ""IsAlive"", ""kind"": ""variable"", ""behavior"": ""Damageable"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""True until its health runs out (and again after Revive).""}")]
        [NetworkSync]
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
