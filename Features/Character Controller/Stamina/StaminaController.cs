/*using SaintsField;
using SaintsField.Playa;*/
using UnityEngine;

namespace Remedy.CharacterControllers.Stamina
{
    [SchematicBehaviorPath("Characters/Movement/Stamina Controller")]
    public class StaminaController : SchematicBehavior
    {
        [Tooltip("Called from other scripts to use Stamina")]
        [InputSignal, Parameter("Amount", typeof(float))]
        public readonly SignalHandle<float> UseStamina;
        [Tooltip("Boolean Events that determine whether Stamina can regerate or not.")]
        [InputSignal, Parameter("Regenerate", typeof(bool))]
        public readonly SignalHandle<bool> RegenerateStamina;

        [Tooltip("Passes the amount that was consumed")]
        [OutputSignal, Parameter("Amount", typeof(float))]
        public readonly SignalHandle<float> OnStaminaConsumed;
        [Tooltip("Passes the current amount of stamina")]
        [OutputSignal, Parameter("Stamina", typeof(float))]
        public readonly SignalHandle<float> OnStaminaChanged;
        [Tooltip("Triggered when the Controller runs out of Stamina")]
        [OutputSignal]
        public readonly SignalHandle OnStaminaDrained;

        public readonly VariableHandle<StaminaProperties> Properties = (StaminaProperties)null;

        public readonly VariableHandle<bool> Regenerating = false;
        public readonly VariableHandle<float> Stamina = 100f;

        protected override void OnStart()
        {
            var instance = Self;
            UseStamina.Subscribe(value => ConsumeStamina(instance, value));
            RegenerateStamina.Subscribe(value => Regenerating[instance] = value);
        }

        protected override void OnEnable()
        {
            var properties = Properties.Value;
            if (properties != null)
                Stamina.Value = properties.MaxStamina;
        }

        protected override void OnUpdate()
        {
            var properties = Properties.Value;
            ref var stamina = ref Stamina.Ref;
            if (Regenerating.Value && properties != null)
            {
                if (properties.MaxStamina > stamina)
                    stamina += properties.StaminaReginAmount * Time.deltaTime;
                else
                    stamina = properties.MaxStamina;
            }
            OnStaminaChanged.Invoke(Self, stamina);
        }

        /// <summary>
        /// Attempts to consume some of the selected object's Stamina, and returns whether it was successful or not.
        /// </summary>
        public bool ConsumeStamina(float amount) => ConsumeStamina(Selected, amount);

        public bool ConsumeStamina(BehaviorInstance instance, float amount)
        {
            ref var stamina = ref Stamina.For(instance);
            if (stamina < amount) return false;
            stamina -= amount;
            OnStaminaConsumed.Invoke(instance, amount);
            if (stamina <= 0f)
                OnStaminaDrained.Invoke(instance);
            return true;
        }
    }
}
