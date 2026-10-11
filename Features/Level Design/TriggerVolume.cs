using UnityEngine;

namespace Remedy.LevelDesign
{
    /// <summary>
    /// Trigger Volumes are a base for any Volumes that would invoke Events related to the player's interactions with it.
    /// </summary>
    [SchematicBehaviorPath("Level Design/Trigger Volume")]
    [RequireComponent(typeof(BoxCollider))]
    public class TriggerVolume : SchematicBehavior
    {
        [OutputSignal]
        public readonly SignalHandle OnTrigger;

        public readonly VariableHandle<bool> ShowCollider = true;

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
