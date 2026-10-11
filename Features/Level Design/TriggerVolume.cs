using UnityEngine;

namespace Remedy.LevelDesign
{
    /// <summary>
    /// Trigger Volumes are a base for any Volumes that would invoke Events related to the player's interactions with it.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Trigger Volume"", ""kind"": ""behavior"", ""menu"": ""Level Design/Trigger Volume"", ""description"": ""Trigger Volumes are a base for any Volumes that would invoke Events related to the player's interactions with it."", ""variables"": [""ShowCollider"", ""TriggerType""], ""signals"": [""OnTrigger""], ""use_for"": [""areas that react to the player entering or leaving""]}")]
    [SchematicBehaviorPath("Level Design/Trigger Volume")]
    [RequireComponent(typeof(BoxCollider))]
    public class TriggerVolume : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""OnTrigger"", ""kind"": ""signal"", ""behavior"": ""Trigger Volume"", ""direction"": ""output"", ""description"": ""Sent by Trigger Volume when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle OnTrigger;

        [SchematicDoc(@"{""name"": ""ShowCollider"", ""kind"": ""variable"", ""behavior"": ""Trigger Volume"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Show Collider (bool) of Trigger Volume.""}")]
        public readonly VariableHandle<bool> ShowCollider = true;

        [SchematicDoc(@"{""name"": ""TriggerType"", ""kind"": ""variable"", ""behavior"": ""Trigger Volume"", ""type"": ""int"", ""default"": ""0"", ""description"": ""Index into the Trigger Types defined in the Level Design Data.""}")]
        [Tooltip("Index into the Trigger Types defined in the Level Design Data.")]
        public readonly VariableHandle<int> TriggerType = 0;

        protected override void OnCreate()
        {
            var collider = Self.GetComponent<BoxCollider>();
            if (collider != null)
                collider.isTrigger = true;
        }

        protected override void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(GlobalSubsystem.PlayerTag))
                OnTrigger.Invoke();
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            if (!context.Get(ShowCollider))
                return;

            var collider = context.GetComponent<BoxCollider>();
            var triggerTypes = LevelDesignData.Instance.Triggers?.TriggerTypes;
            int triggerType = context.Get(TriggerType);
            if (collider == null || triggerTypes == null || triggerType < 0 || triggerType >= triggerTypes.Length)
                return;

            var definition = triggerTypes[triggerType];
            var center = context.Transform.position + collider.center;
            Gizmos.color = definition.Color;
            if (definition.Filled)
                Gizmos.DrawCube(center, collider.size);
            Gizmos.DrawWireCube(center, collider.size);
        }
    }
}
