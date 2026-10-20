using Remedy.Schematics;
/*using SaintsField;
using SaintsField.Playa;*/
using UnityEngine;

namespace Remedy.CharacterControllers.Stamina
{
    [SchematicDoc(@"{""name"": ""Stamina Controller"", ""kind"": ""behavior"", ""menu"": ""Characters/Movement/Stamina Controller"", ""description"": ""A stamina pool that is spent by Use Stamina, regenerates when allowed, and reports changes and running out through its Signals."", ""variables"": [""Properties"", ""Regenerating"", ""Stamina""], ""signals"": [""UseStamina"", ""RegenerateStamina"", ""OnStaminaConsumed"", ""OnStaminaChanged"", ""OnStaminaDrained""], ""use_for"": [""sprinting"", ""flying time limits"", ""ability costs""]}")]
    [SchematicBehaviorPath("Characters/Movement/Stamina Controller")]
    public class StaminaController : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""UseStamina"", ""kind"": ""signal"", ""behavior"": ""Stamina Controller"", ""direction"": ""input"", ""params"": [{""name"": ""Amount"", ""type"": ""float""}], ""description"": ""Called from other scripts to use Stamina"", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [Tooltip("Called from other scripts to use Stamina")]
        [InputSignal, Parameter("Amount", typeof(float))]
        public readonly SignalHandle<float> UseStamina;
        [SchematicDoc(@"{""name"": ""RegenerateStamina"", ""kind"": ""signal"", ""behavior"": ""Stamina Controller"", ""direction"": ""input"", ""params"": [{""name"": ""Regenerate"", ""type"": ""bool""}], ""description"": ""Boolean Events that determine whether Stamina can regerate or not."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [Tooltip("Boolean Events that determine whether Stamina can regerate or not.")]
        [InputSignal, Parameter("Regenerate", typeof(bool))]
        public readonly SignalHandle<bool> RegenerateStamina;

        [SchematicDoc(@"{""name"": ""OnStaminaConsumed"", ""kind"": ""signal"", ""behavior"": ""Stamina Controller"", ""direction"": ""output"", ""params"": [{""name"": ""Amount"", ""type"": ""float""}], ""description"": ""Passes the amount that was consumed"", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Passes the amount that was consumed")]
        [OutputSignal, Parameter("Amount", typeof(float))]
        public readonly SignalHandle<float> OnStaminaConsumed;
        [SchematicDoc(@"{""name"": ""OnStaminaChanged"", ""kind"": ""signal"", ""behavior"": ""Stamina Controller"", ""direction"": ""output"", ""params"": [{""name"": ""Stamina"", ""type"": ""float""}], ""description"": ""Passes the current amount of stamina"", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Passes the current amount of stamina")]
        [OutputSignal, Parameter("Stamina", typeof(float))]
        public readonly SignalHandle<float> OnStaminaChanged;
        [SchematicDoc(@"{""name"": ""OnStaminaDrained"", ""kind"": ""signal"", ""behavior"": ""Stamina Controller"", ""direction"": ""output"", ""description"": ""Triggered when the Controller runs out of Stamina"", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Triggered when the Controller runs out of Stamina")]
        [OutputSignal]
        [NetworkSync(SignalSender.Authority)]
        public readonly SignalHandle OnStaminaDrained;

        [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Stamina Controller"", ""type"": ""StaminaProperties"", ""default"": ""null"", ""description"": ""Properties (StaminaProperties) of Stamina Controller.""}")]
        public readonly VariableHandle<StaminaProperties> Properties = (StaminaProperties)null;

        [SchematicDoc(@"{""name"": ""Regenerating"", ""kind"": ""variable"", ""behavior"": ""Stamina Controller"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Regenerating (bool) of Stamina Controller.""}")]
        public readonly VariableHandle<bool> Regenerating = false;
        [SchematicDoc(@"{""name"": ""Stamina"", ""kind"": ""variable"", ""behavior"": ""Stamina Controller"", ""type"": ""float"", ""default"": ""100f"", ""description"": ""Stamina (float) of Stamina Controller.""}")]
        [NetworkSync]
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
