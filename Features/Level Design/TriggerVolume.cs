using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelDesign
{
    /// <summary>
    /// Trigger Volumes are a base for any Volumes that would invoke Events related to the player's interactions with it.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Trigger Volume"", ""kind"": ""behavior"", ""menu"": ""Level Design/Trigger Volume"", ""description"": ""Trigger Volumes are a base for any Volumes that would invoke Events related to the player's interactions with it."", ""variables"": [""ShowCollider"", ""TriggerType"", ""FilterTag""], ""signals"": [""OnTrigger""], ""use_for"": [""areas that react to the player entering or leaving""]}")]
    [SchematicBehaviorPath("Level Design/Trigger Volume")]
    [RequireComponent(typeof(BoxCollider))]
    public class TriggerVolume : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""OnTrigger"", ""kind"": ""signal"", ""behavior"": ""Trigger Volume"", ""direction"": ""output"", ""description"": ""Sent when something enters the volume."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        [NetworkSync(SignalSender.Authority)]
        public readonly SignalHandle OnTrigger;

        [SchematicDoc(@"{""name"": ""ShowCollider"", ""kind"": ""variable"", ""behavior"": ""Trigger Volume"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Draw the volume's shape in the Scene view so it's easy to see and place.""}")]
        public readonly VariableHandle<bool> ShowCollider = true;

        [SchematicDoc(@"{""name"": ""TriggerType"", ""kind"": ""variable"", ""behavior"": ""Trigger Volume"", ""type"": ""int"", ""default"": ""0"", ""description"": ""Index into the Trigger Types defined in the Level Design Data.""}")]
        [Tooltip("Index into the Trigger Types defined in the Level Design Data.")]
        public readonly VariableHandle<int> TriggerType = 0;

        [SchematicDoc(@"{""name"": ""FilterTag"", ""kind"": ""variable"", ""behavior"": ""Trigger Volume"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Only objects with this Int Tag set it off. Untagged: anything.""}")]
        [Tooltip("Only objects with this Int Tag set it off (the player, by default). Untagged: anything.")]
        public readonly VariableHandle<IntTag> FilterTag = new IntTag(IntTags.Player);

        protected override void OnCreate()
        {
            var collider = Self.GetComponent<BoxCollider>();
            if (collider != null)
                collider.isTrigger = true;
        }

        protected override void OnTriggerEnter(Collider other)
        {
            var tag = FilterTag.Value;
            if (!tag.IsTagged || HasTag(other, tag))
                OnTrigger.Invoke();
        }

        // The collider's own object, its body, or the Schematic Object it belongs to (tags usually live on the root).
        private static bool HasTag(Collider other, IntTag tag)
        {
            if (other.gameObject.HasIntTag(tag))
                return true;
            var body = other.attachedRigidbody;
            if (body != null && body.gameObject != other.gameObject && body.gameObject.HasIntTag(tag))
                return true;
            var owner = other.GetComponentInParent<SchematicObject>();
            return owner != null && owner.gameObject != other.gameObject && owner.gameObject.HasIntTag(tag);
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
