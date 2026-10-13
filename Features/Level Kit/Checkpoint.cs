using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// A checkpoint: when an object with the Agent Tag enters its zone, it becomes that agent's respawn point and
    /// sends On Reached. <see cref="Respawn(GameObject)"/> (or the Respawn Agent Signal, e.g. from a death graph) puts
    /// the agent back at its last checkpoint.
    /// </summary>
    /// <remarks>
    /// With Only Forward, a checkpoint with a lower Order than the agent's current one doesn't take over, so running
    /// back through an early checkpoint doesn't lose progress.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Checkpoint"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Checkpoint"", ""description"": ""When a tagged object enters its zone it becomes that object's respawn point and sends On Reached. Respawn Agent (or Checkpoint.Respawn in C#) puts an object back at its last checkpoint. IsReached is network synced."", ""variables"": [""ZoneCenter"", ""ZoneSize"", ""AgentTag"", ""SpawnOffset"", ""Order"", ""OnlyForward"", ""Flag"", ""ReachedColor"", ""IsReached""], ""signals"": [""RespawnAgent"", ""Activate"", ""OnReached""], ""use_for"": [""respawn points along a level"", ""save points"", ""race checkpoints""], ""notes"": ""Pairs with Checkpoint Agent (Level Design) or any death graph: send Respawn Agent with the player.""}")]
    [SchematicBehaviorPath("Level Kit/Checkpoint")]
    public class Checkpoint : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""ZoneCenter"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""Vector3"", ""default"": ""(0, 1.5, 0)"", ""description"": ""Centre of the trigger box (local).""}")]
        [Tooltip("Centre of the trigger box (local).")]
        public readonly VariableHandle<Vector3> ZoneCenter = new Vector3(0f, 1.5f, 0f);

        [SchematicDoc(@"{""name"": ""ZoneSize"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""Vector3"", ""default"": ""(3, 3, 3)"", ""description"": ""Size of the trigger box (local).""}")]
        [Tooltip("Size of the trigger box (local).")]
        public readonly VariableHandle<Vector3> ZoneSize = new Vector3(3f, 3f, 3f);

        [SchematicDoc(@"{""name"": ""AgentTag"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Who it saves. Untagged: any physics body.""}")]
        [Tooltip("Who it saves. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> AgentTag = new IntTag(IntTags.Player);

        [SchematicDoc(@"{""name"": ""SpawnOffset"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""Vector3"", ""default"": ""(0, 1, 0)"", ""description"": ""Where agents respawn, relative to the checkpoint (local).""}")]
        [Tooltip("Where agents respawn, relative to the checkpoint (local).")]
        public readonly VariableHandle<Vector3> SpawnOffset = new Vector3(0f, 1f, 0f);

        [SchematicDoc(@"{""name"": ""Order"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""int"", ""default"": ""0"", ""description"": ""Its place along the level (higher is further). Used by Only Forward.""}")]
        [Tooltip("Its place along the level (higher is further). Used by Only Forward.")]
        public readonly VariableHandle<int> Order = 0;

        [SchematicDoc(@"{""name"": ""OnlyForward"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Don't replace an agent's checkpoint with one of a lower Order.""}")]
        [Tooltip("Don't replace an agent's checkpoint with one of a lower Order.")]
        public readonly VariableHandle<bool> OnlyForward = true;

        [SchematicDoc(@"{""name"": ""Flag"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Tinted with Reached Color once reached. Empty: nothing changes colour.""}")]
        [Tooltip("Tinted with Reached Color once reached. Empty: nothing changes colour.")]
        public readonly VariableHandle<Transform> Flag = (Transform)null;

        [SchematicDoc(@"{""name"": ""ReachedColor"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""Color"", ""default"": ""green"", ""description"": ""The Flag's colour once reached.""}")]
        [Tooltip("The Flag's colour once reached.")]
        public readonly VariableHandle<Color> ReachedColor = new Color(0.25f, 0.9f, 0.35f, 1f);

        [SchematicDoc(@"{""name"": ""IsReached"", ""kind"": ""variable"", ""behavior"": ""Checkpoint"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether anyone has reached it. Network synced.""}")]
        [Tooltip("Whether anyone has reached it.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsReached = false;

        private readonly VariableHandle<bool> _tinted = false;

        [SchematicDoc(@"{""name"": ""RespawnAgent"", ""kind"": ""signal"", ""behavior"": ""Checkpoint"", ""direction"": ""input"", ""params"": [{""name"": ""Agent"", ""type"": ""GameObject""}], ""description"": ""Puts the agent back at its last checkpoint (any checkpoint's Signal works for any agent)."", ""how"": ""Send it with a Send a Signal node, e.g. from the player's death graph.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Agent", typeof(GameObject))]
        public readonly SignalHandle<GameObject> RespawnAgent;

        [SchematicDoc(@"{""name"": ""Activate"", ""kind"": ""signal"", ""behavior"": ""Checkpoint"", ""direction"": ""input"", ""params"": [{""name"": ""Agent"", ""type"": ""GameObject""}], ""description"": ""Makes this the agent's checkpoint without entering the zone."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Agent", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Activate;

        [SchematicDoc(@"{""name"": ""OnReached"", ""kind"": ""signal"", ""behavior"": ""Checkpoint"", ""direction"": ""output"", ""params"": [{""name"": ""Agent"", ""type"": ""GameObject""}], ""description"": ""Sent when this becomes an agent's checkpoint."", ""how"": ""React to it with On Signal Received (save, play a jingle).""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Agent", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnReached;

        private readonly struct Saved
        {
            public readonly BehaviorInstance Checkpoint;
            public readonly int Order;
            public Saved(BehaviorInstance checkpoint, int order) { Checkpoint = checkpoint; Order = order; }
        }

        private static readonly Dictionary<GameObject, Saved> _saved = new();
        private readonly List<GameObject> _agents = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _saved.Clear();

        /// <summary>Where an agent would respawn. False if it hasn't reached a checkpoint (or that checkpoint is gone).</summary>
        public static bool TryGetRespawnPoint(GameObject agent, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            agent = LevelKitQuery.AgentOf(agent);
            if (agent == null || !_saved.TryGetValue(agent, out var saved) || saved.Checkpoint == null || saved.Checkpoint.GameObject == null)
                return false;
            var instance = saved.Checkpoint;
            var checkpoint = (Checkpoint)instance.Behavior;
            position = instance.Transform.TransformPoint(checkpoint.SpawnOffset[instance]);
            rotation = Quaternion.Euler(0f, instance.Transform.eulerAngles.y, 0f);
            return true;
        }

        /// <summary>Puts an agent back at its last checkpoint (body teleported and stopped). False if it has none.</summary>
        public static bool Respawn(GameObject agent)
        {
            agent = LevelKitQuery.AgentOf(agent);
            if (!TryGetRespawnPoint(agent, out var position, out var rotation))
                return false;
            LevelKitQuery.Teleport(agent, position, rotation);
            return true;
        }

        protected override void OnStart()
        {
            var instance = Self;
            RespawnAgent.Subscribe(agent => { if (LevelKitQuery.HasAuthority(instance)) Respawn(agent); });
            Activate.Subscribe(agent => Reach(instance, agent));
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!LevelKitQuery.HasAuthority(instance))
                return;
            var transform = instance.Transform;
            var center = transform.TransformPoint(ZoneCenter.Value);
            var half = Vector3.Scale(ZoneSize.Value, transform.lossyScale) * 0.5f;
            LevelKitQuery.AgentsInBox(center, transform.rotation, half, -1, AgentTag.Value, _agents, transform);
            foreach (var agent in _agents)
                Reach(instance, agent);
        }

        protected override void OnUpdate()
        {
            bool reached = IsReached.Value;
            if (reached == _tinted.Value || Flag.Value == null)
                return;
            _tinted.Value = reached;
            if (reached) LevelKitQuery.Tint(Flag.Value, ReachedColor.Value);
            else foreach (var renderer in Flag.Value.GetComponentsInChildren<Renderer>(true)) renderer.SetPropertyBlock(null);
        }

        private void Reach(BehaviorInstance instance, GameObject agent)
        {
            agent = LevelKitQuery.AgentOf(agent);
            if (agent == null || !LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                int order = Order.Value;
                if (_saved.TryGetValue(agent, out var current) && current.Checkpoint != null && current.Checkpoint.GameObject != null)
                {
                    if (current.Checkpoint == instance)
                        return;
                    if (OnlyForward.Value && order < current.Order)
                        return;
                }
                _saved[agent] = new Saved(instance, order);
                IsReached.Value = true;
                OnReached.Invoke(agent);
            }
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            LevelKitQuery.DrawLocalBox(context.Transform, context.Get(ZoneCenter), context.Get(ZoneSize), new Color(0.3f, 0.9f, 0.4f, 0.8f));
            Gizmos.color = Color.white;
            var spawn = context.Transform.TransformPoint(context.Get(SpawnOffset));
            Gizmos.DrawWireSphere(spawn, 0.25f);
            Gizmos.DrawLine(spawn, spawn + context.Transform.forward * 0.6f);
        }
    }
}
