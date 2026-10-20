using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// A key to pick up: whoever collects it carries its Key Id (<see cref="KeyRing"/>), which opens Doors with that
    /// Required Key and unlocks <see cref="KeyLock"/>s with the same Key Id.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Key Item"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Key Item"", ""description"": ""A key pickup: whoever collects it carries its Key Id, which unlocks Doors (Required Key) and Key Locks with the same id. IsCollected is network synced."", ""variables"": [""KeyId"", ""CollectorTag"", ""PickupRadius"", ""SpinSpeed"", ""Visual"", ""IsCollected""], ""signals"": [""Collect"", ""OnCollected""], ""use_for"": [""keys for locked doors"", ""keycards"", ""quest items that unlock something""]}")]
    [SchematicBehaviorPath("Level Kit/Key Item")]
    public class KeyItem : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""KeyId"", ""kind"": ""variable"", ""behavior"": ""Key Item"", ""type"": ""string"", ""default"": ""Red Key"", ""description"": ""The key this gives. Doors and Key Locks name the same id.""}")]
        [Tooltip("The key this gives. Doors and Key Locks name the same id.")]
        public readonly VariableHandle<string> KeyId = "Red Key";

        [SchematicDoc(@"{""name"": ""CollectorTag"", ""kind"": ""variable"", ""behavior"": ""Key Item"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Who can pick it up. Untagged: any physics body.""}")]
        [Tooltip("Who can pick it up. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> CollectorTag = new IntTag(IntTags.Player);

        [SchematicDoc(@"{""name"": ""PickupRadius"", ""kind"": ""variable"", ""behavior"": ""Key Item"", ""type"": ""float"", ""default"": ""0.9"", ""description"": ""How close the collector must come (metres). 0: only the Collect Signal.""}")]
        [Tooltip("How close the collector must come (metres). 0: only the Collect Signal.")]
        public readonly VariableHandle<float> PickupRadius = 0.9f;

        [SchematicDoc(@"{""name"": ""SpinSpeed"", ""kind"": ""variable"", ""behavior"": ""Key Item"", ""type"": ""float"", ""default"": ""60"", ""description"": ""Degrees per second the Visual spins.""}")]
        [Tooltip("Degrees per second the Visual spins.")]
        public readonly VariableHandle<float> SpinSpeed = 60f;

        [SchematicDoc(@"{""name"": ""Visual"", ""kind"": ""variable"", ""behavior"": ""Key Item"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""What spins and hides. Empty: the object itself.""}")]
        [Tooltip("What spins and hides. Empty: the object itself.")]
        public readonly VariableHandle<Transform> Visual = (Transform)null;

        [SchematicDoc(@"{""name"": ""IsCollected"", ""kind"": ""variable"", ""behavior"": ""Key Item"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether it's been picked up. Network synced.""}")]
        [Tooltip("Whether it's been picked up.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsCollected = false;

        [SchematicDoc(@"{""name"": ""Collect"", ""kind"": ""signal"", ""behavior"": ""Key Item"", ""direction"": ""input"", ""params"": [{""name"": ""Collector"", ""type"": ""GameObject""}], ""description"": ""Gives the key to someone, wherever they are."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Collector", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Collect;

        [SchematicDoc(@"{""name"": ""OnCollected"", ""kind"": ""signal"", ""behavior"": ""Key Item"", ""direction"": ""output"", ""params"": [{""name"": ""Collector"", ""type"": ""GameObject""}], ""description"": ""Sent when it's picked up, with who did it (show it on the HUD)."", ""how"": ""React to it with On Signal Received.""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Collector", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnCollected;

        private readonly List<GameObject> _nearby = new();

        protected override void OnStart()
        {
            var instance = Self;
            Collect.Subscribe(collector => DoCollect(instance, collector));
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (IsCollected.Value || !LevelKitQuery.HasAuthority(instance))
                return;
            float radius = PickupRadius.Value;
            if (radius > 0f && LevelKitQuery.AgentsInSphere(instance.Transform.position, radius, -1, CollectorTag.Value, _nearby, instance.Transform) > 0)
                DoCollect(instance, _nearby[0]);
        }

        protected override void OnUpdate()
        {
            var visual = Visual.Value != null ? Visual.Value : Self.Transform;
            bool visible = !IsCollected.Value;
            LevelKitQuery.SetRenderersVisible(visual, visible);
            if (visible && SpinSpeed.Value != 0f)
                visual.Rotate(Vector3.up, SpinSpeed.Value * Time.deltaTime, Space.World);
        }

        private void DoCollect(BehaviorInstance instance, GameObject collector)
        {
            if (collector == null || !LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (IsCollected.Value)
                    return;
                IsCollected.Value = true;
                KeyRing.Give(collector, KeyId.Value);
                OnCollected.Invoke(LevelKitQuery.AgentOf(collector));
            }
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            float radius = context.Get(PickupRadius);
            if (radius <= 0f) return;
            Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(context.Transform.position, radius);
        }
    }
}
