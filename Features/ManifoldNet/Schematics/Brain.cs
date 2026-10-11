using Remedy.Schematics;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    /// <summary>
    /// A learning brain for a Schematic Object, run by a Manifold Neuron Network. Its inputs and outputs are slots you
    /// define in the IO Dock; the graph feeds the inputs on <see cref="OnThink"/> (Set Brain Input) and reads the
    /// decisions on <see cref="OnDecided"/> (Get Brain Output). Teach it (Teach Brain Output), praise or punish it
    /// (Reward Brain), and it learns as it plays.
    /// <para>Shared: every instance of the object thinks with one brain (and learns together). Per Instance: each gets its own.</para>
    /// </summary>
    [SchematicBehaviorPath("AI/Brain")]
    public class Brain : SchematicBehavior
    {
        [Tooltip("The brain's design: its slots, how it learns, and what it has learned. Created next to the prefab from the IO Dock.")]
        public readonly VariableHandle<BrainDefinition> Definition = (BrainDefinition)null;

        [Tooltip("Before the brain thinks: set its inputs here (Set Brain Input). Also a good time to teach it.")]
        [OutputSignal]
        public readonly SignalHandle OnThink;

        [Tooltip("The brain has decided: read its outputs here (Get Brain Output) and act on them.")]
        [OutputSignal]
        public readonly SignalHandle OnDecided;

        // Behavior-level: every instance's agent, whichever object it's on.
        private readonly Dictionary<BehaviorInstance, BrainAgent> _agents = new();

        protected override void OnStart()
        {
            var definition = Definition.Value;
            if (definition == null)
            {
                Debug.LogWarning($"{Self.GameObject.name}'s Brain has no Brain Definition. Create one from its section in the IO Dock.", Self.GameObject);
                return;
            }
            var agent = new BrainAgent(Self, definition);
            _agents[Self] = agent;
            BrainHub.Join(this, agent, ObjectIdOf(Self, definition));
        }

        protected override void OnDestroy()
        {
            if (_agents.TryGetValue(Self, out var agent))
            {
                BrainHub.Leave(agent);
                _agents.Remove(Self);
            }
        }

        /// <summary>
        /// Which Schematic Object this is, for <c>Networks[objectID]</c>: its graph (one per prefab) and the definition.
        /// </summary>
        private static int ObjectIdOf(BehaviorInstance instance, BrainDefinition definition)
        {
            var graph = instance.Owner != null ? instance.Owner.SchematicGraph : null;
            unchecked { return (graph != null ? graph.GetInstanceID() : 0) * 486187739 + definition.GetInstanceID(); }
        }

        /// <summary>The brain of an instance (null if it has none, or no definition).</summary>
        public BrainAgent AgentOf(BehaviorInstance instance)
            => instance != null && _agents.TryGetValue(instance, out var agent) ? agent : null;

        /// <summary>The brain of a GameObject's Brain behavior (null if it has none).</summary>
        public static BrainAgent AgentOn(GameObject gameObject)
        {
            if (gameObject == null)
                return null;
            var instance = ExecutionContext.FindInstance(gameObject, typeof(Brain));
            return instance?.Behavior is Brain brain ? brain.AgentOf(instance) : null;
        }

        internal void Think(BrainAgent agent) => OnThink.Invoke(agent.Instance);
        internal void Decided(BrainAgent agent) => OnDecided.Invoke(agent.Instance);
    }
}
