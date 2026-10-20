using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// A pickup (coin, gem, star): collected when an object with the Collector Tag comes close, it adds Amount to the
    /// <see cref="Counter"/> with its Counter Id, sends On Collected, and hides (optionally coming back later).
    /// </summary>
    /// <remarks>
    /// It hides rather than being destroyed, so collecting is just the synced <c>IsCollected</c> Variable: every peer
    /// hides it, and a late joiner sees it gone too.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Collectible"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Collectible"", ""description"": ""A pickup (coin, gem): collected when a tagged object comes within Pickup Radius; adds Amount to the Counter with the same Counter Id, sends On Collected, hides, and can respawn. IsCollected is network synced."", ""variables"": [""CounterId"", ""Amount"", ""CollectorTag"", ""PickupRadius"", ""RespawnDelay"", ""SpinSpeed"", ""Visual"", ""IsCollected""], ""signals"": [""Collect"", ""Respawn"", ""OnCollected""], ""use_for"": [""coins and gems"", ""collect-them-all goals"", ""score pickups""]}")]
    [SchematicBehaviorPath("Level Kit/Collectible")]
    public class Collectible : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""CounterId"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""string"", ""default"": ""Coins"", ""description"": ""The Counter it adds to. Empty: none.""}")]
        [Tooltip("The Counter it adds to. Empty: none.")]
        public readonly VariableHandle<string> CounterId = "Coins";

        [SchematicDoc(@"{""name"": ""Amount"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""int"", ""default"": ""1"", ""description"": ""How much it adds.""}")]
        [Tooltip("How much it adds.")]
        public readonly VariableHandle<int> Amount = 1;

        [SchematicDoc(@"{""name"": ""CollectorTag"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Who can pick it up. Untagged: any physics body.""}")]
        [Tooltip("Who can pick it up. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> CollectorTag = new IntTag(IntTags.Player);

        [SchematicDoc(@"{""name"": ""PickupRadius"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""float"", ""default"": ""0.8"", ""description"": ""How close the collector must come (metres). 0: only the Collect Signal.""}")]
        [Tooltip("How close the collector must come (metres). 0: only the Collect Signal.")]
        public readonly VariableHandle<float> PickupRadius = 0.8f;

        [SchematicDoc(@"{""name"": ""RespawnDelay"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Seconds until it comes back. 0: never.""}")]
        [Tooltip("Seconds until it comes back. 0: never.")]
        public readonly VariableHandle<float> RespawnDelay = 0f;

        [SchematicDoc(@"{""name"": ""SpinSpeed"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""float"", ""default"": ""90"", ""description"": ""Degrees per second the Visual spins around up. 0: still.""}")]
        [Tooltip("Degrees per second the Visual spins around up. 0: still.")]
        public readonly VariableHandle<float> SpinSpeed = 90f;

        [SchematicDoc(@"{""name"": ""Visual"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""What spins and hides. Empty: the object itself.""}")]
        [Tooltip("What spins and hides. Empty: the object itself.")]
        public readonly VariableHandle<Transform> Visual = (Transform)null;

        [SchematicDoc(@"{""name"": ""IsCollected"", ""kind"": ""variable"", ""behavior"": ""Collectible"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether it's been picked up (hidden). Network synced.""}")]
        [Tooltip("Whether it's been picked up (hidden).")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsCollected = false;

        private readonly VariableHandle<float> _respawnAt = 0f;

        [SchematicDoc(@"{""name"": ""Collect"", ""kind"": ""signal"", ""behavior"": ""Collectible"", ""direction"": ""input"", ""params"": [{""name"": ""Collector"", ""type"": ""GameObject""}], ""description"": ""Collects it for someone, wherever they are."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Collector", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Collect;

        [SchematicDoc(@"{""name"": ""Respawn"", ""kind"": ""signal"", ""behavior"": ""Collectible"", ""direction"": ""input"", ""description"": ""Brings it back right away."", ""how"": ""Send it with a Send a Signal node (e.g. when the level resets).""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Respawn;

        [SchematicDoc(@"{""name"": ""OnCollected"", ""kind"": ""signal"", ""behavior"": ""Collectible"", ""direction"": ""output"", ""params"": [{""name"": ""Collector"", ""type"": ""GameObject""}], ""description"": ""Sent when it's picked up, with who did it (play a sound, spawn sparkles)."", ""how"": ""React to it with On Signal Received.""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Collector", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnCollected;

        private readonly List<GameObject> _nearby = new();

        protected override void OnStart()
        {
            var instance = Self;
            Collect.Subscribe(collector => DoCollect(instance, collector));
            Respawn.Subscribe(() => { if (LevelKitQuery.HasAuthority(instance)) IsCollected.Value = false; });
            LevelKitQuery.SetRenderersVisible(VisualOf(instance), !IsCollected.Value);
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!LevelKitQuery.HasAuthority(instance))
                return;

            if (IsCollected.Value)
            {
                if (RespawnDelay.Value > 0f && Time.time >= _respawnAt.Value)
                    IsCollected.Value = false;
                return;
            }

            float radius = PickupRadius.Value;
            if (radius > 0f && LevelKitQuery.AgentsInSphere(instance.Transform.position, radius, -1, CollectorTag.Value, _nearby, instance.Transform) > 0)
                DoCollect(instance, _nearby[0]);
        }

        protected override void OnUpdate()
        {
            var instance = Self;
            var visual = VisualOf(instance);
            bool visible = !IsCollected.Value;
            LevelKitQuery.SetRenderersVisible(visual, visible);
            if (visible && SpinSpeed.Value != 0f)
                visual.Rotate(Vector3.up, SpinSpeed.Value * Time.deltaTime, Space.World);
        }

        private void DoCollect(BehaviorInstance instance, GameObject collector)
        {
            if (!LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (IsCollected.Value)
                    return;
                IsCollected.Value = true;
                _respawnAt.Value = Time.time + RespawnDelay.Value;
                Counter.AddTo(CounterId.Value, Amount.Value);
                OnCollected.Invoke(collector);
            }
        }

        private Transform VisualOf(BehaviorInstance instance)
        {
            var visual = Visual[instance];
            return visual != null ? visual : instance.Transform;
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            float radius = context.Get(PickupRadius);
            if (radius <= 0f) return;
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(context.Transform.position, radius);
        }
    }
}
