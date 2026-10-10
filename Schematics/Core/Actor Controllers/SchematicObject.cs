using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// The base of an Object within the Schematic Framework.
    /// Describes how instances of the Object should interface with the rest of the Schematic Toolkit
    /// </summary>
    [RequireComponent(typeof(ManagerHandshaker))]
    public class SchematicObject : MonoBehaviour
    {
        public SchematicGraph SchematicGraph;

        /// <summary>
        /// The Schematic Behaviors attached through the Inspector, keyed by the GameObject they're attached to
        /// (this object or one of its children).
        /// </summary>
        [HideInInspector]
        public SerializableDictionary<GameObject, BehaviorAttachmentList> Behaviors = new();

        /// <summary>
        /// Identifies this object in the <see cref="ExecutionContext"/> while it is spawned; 0 otherwise.
        /// A new UID is assigned every time the object is spawned, including from its pool.
        /// </summary>
        public int UID { get; internal set; }
        public bool IsSpawned => UID != 0;

        /// <summary>
        /// The pool this object belongs to, or null if it was instantiated directly or placed in a scene.
        /// </summary>
        public SchematicPool Pool { get; internal set; }

        private List<BehaviorInstance> _behaviorInstances;

        /// <summary>
        /// This object's Behavior Instances: one per attached Behavior, on the root and children alike.
        /// </summary>
        public IReadOnlyList<BehaviorInstance> BehaviorInstances => (IReadOnlyList<BehaviorInstance>)_behaviorInstances ?? Array.Empty<BehaviorInstance>();

        private void Awake()
        {
            // Pooled objects are registered by their pool. Anything else (placed in a scene, or instantiated
            // without the pool) registers itself here.
            if (Pool == null)
                ExecutionContext.Register(this);
        }

        private void OnEnable()
        {
            if(SchematicGraph == null)
                return;

            SchematicGraph.ReconstructPortConnections();

            Assign(SchematicGraph);

            foreach (var oninvokeNode in SchematicGraph.FlowOnInvokeCache)
                oninvokeNode.UpdateCaches();
        }

        private void Start()
        {
            if (SchematicGraph != null)
                SchematicGraph.TriggerEvent<OnCreate>(gameObject);
        }

        private void Update()
        {
            if (SchematicGraph != null)
                SchematicGraph.TriggerEvent<OnUpdate>(gameObject);
        }

        private void OnDestroy()
        {
            if (SchematicGraph != null)
                SchematicGraph.TriggerEvent<OnDestroy>(gameObject);

            ExecutionContext.Unregister(this);
            ExecutionContext.Untrack(this);
            Pool?.Forget(this);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.CollisionEnter, collision, null);
            if (SchematicGraph == null) return;
            SchematicGraph.TriggerEvent<OnCollisionEnter>(gameObject, collision.collider, collision.impulse, collision.relativeVelocity, collision.articulationBody, collision.transform, collision.gameObject);
        }

        protected virtual void OnCollisionExit(Collision collision)
        {
            ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.CollisionExit, collision, null);
            if (SchematicGraph == null) return;
            SchematicGraph.TriggerEvent<OnCollisionExit>(gameObject, collision.collider, collision.impulse, collision.relativeVelocity, collision.articulationBody, collision.transform, collision.gameObject);
        }
        protected virtual void OnTriggerEnter(Collider collider)
        {
            ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.TriggerEnter, null, collider);
            if (SchematicGraph == null) return;
            SchematicGraph.TriggerEvent<OnCollisionEnter>(gameObject, collider);
        }

        protected virtual void OnTriggerExit(Collider collider)
        {
            ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.TriggerExit, null, collider);
            if (SchematicGraph == null) return;
            SchematicGraph.TriggerEvent<OnCollisionExit>(gameObject, collider);
        }

        public void OnValidate()
        {
            // Add OnValidate Event
        }

        public void Assign(SchematicGraph graph)
        {
            this.SchematicGraph = graph;
        }

        #region Behaviors

        /// <summary>
        /// Despawns this object: back into its pool if it came from one, otherwise destroyed.
        /// </summary>
        public void Destroy() => SchematicsManager.Destroy(this);

        /// <summary>
        /// Enables or disables this object's Behaviors, calling OnEnable/OnDisable for each one that changes.
        /// </summary>
        /// <param name="behaviorType">Only this Behavior. Null for all of them.</param>
        /// <param name="target">Only Behaviors attached to this GameObject. Null for the root and all children.</param>
        public void SetBehaviorsEnabled(bool enabled, Type behaviorType = null, GameObject target = null)
        {
            if (_behaviorInstances == null)
                return;

            for (int i = 0; i < _behaviorInstances.Count; i++)
            {
                var instance = _behaviorInstances[i];
                if (behaviorType != null && !behaviorType.IsInstanceOfType(instance.Behavior))
                    continue;
                if (target != null && instance.GameObject != target)
                    continue;
                instance.SetEnabled(enabled);
            }
        }

        public void SetBehaviorEnabled<TBehavior>(bool enabled, GameObject target = null) where TBehavior : SchematicBehavior
            => SetBehaviorsEnabled(enabled, typeof(TBehavior), target);

        /// <summary>
        /// Finds this object's instance of a Behavior.
        /// </summary>
        /// <param name="target">The GameObject it is attached to. Null to take the first one found.</param>
        public BehaviorInstance GetBehavior<TBehavior>(GameObject target = null) where TBehavior : SchematicBehavior
            => GetBehavior(typeof(TBehavior), target);

        public BehaviorInstance GetBehavior(Type behaviorType, GameObject target = null)
        {
            if (_behaviorInstances == null)
                return null;

            foreach (var instance in _behaviorInstances)
                if (behaviorType.IsInstanceOfType(instance.Behavior) && (target == null || instance.GameObject == target))
                    return instance;
            return null;
        }

        /// <summary>
        /// Builds the Behavior Instances from <see cref="Behaviors"/> the first time this object spawns.
        /// They are kept for the object's whole life, so OnCreate runs once even when it is pooled.
        /// </summary>
        internal List<BehaviorInstance> GetOrCreateBehaviorInstances()
        {
            if (_behaviorInstances != null)
                return _behaviorInstances;

            _behaviorInstances = new List<BehaviorInstance>();

            foreach (var entry in Behaviors)
            {
                // Instantiate remaps these keys to this instance's own children. A missing key means the child was deleted.
                var target = entry.Key;
                if (target == null || entry.Value?.Items == null)
                    continue;

                foreach (var attachment in entry.Value.Items)
                {
                    if (attachment?.Metadata == null)
                        continue;

                    BehaviorRuntime runtime;
                    try
                    {
                        runtime = ExecutionContext.GetRuntime(attachment.Metadata);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, this);
                        continue;
                    }

                    if (runtime == null)
                    {
                        Debug.LogWarning($"{name}: the Behavior type of {attachment.Metadata.name} could not be found. Was it renamed or deleted?", this);
                        continue;
                    }

                    var instance = new BehaviorInstance(this, target, runtime, attachment);
                    _behaviorInstances.Add(instance);
                    ExecutionContext.Track(instance);

                    // Unity only sends physics messages to the colliding object itself, so children need a relay.
                    if (runtime.HasPhysicsCallbacks && target != gameObject && !target.TryGetComponent(out SchematicPhysicsRelay _))
                        target.AddComponent<SchematicPhysicsRelay>().Owner = this;
                }
            }

            return _behaviorInstances;
        }

        /// <summary>
        /// Subscribes the graph's "On Signal Received" nodes under this object's UID, so they hear Signals sent to this
        /// object from Behaviors or other graphs. Released by the Execution Context when the object despawns.
        /// </summary>
        internal void SubscribeGraphSignals()
        {
            if (SchematicGraph == null)
                return;

            foreach (var node in SchematicGraph.FlowOnInvokeCache)
            {
                if (node == null || node.Signal == null)
                    continue;

                var receiver = node;
                var target = gameObject;
                bool isInvoking = false;

                node.Signal.Subscribe(UID, new GraphSignalListener(args =>
                {
                    // A graph that sends the Signal it listens to would otherwise recurse forever.
                    if (isInvoking) return;
                    try
                    {
                        isInvoking = true;
                        receiver.Trigger(target, args);
                    }
                    finally
                    {
                        isInvoking = false;
                    }
                }));
            }
        }

        /// <summary>
        /// Sends a physics message to the enabled Behavior Instances attached to <paramref name="source"/>.
        /// </summary>
        internal void ForwardPhysics(GameObject source, BehaviorRuntime.PhysicsMessage message, Collision collision, Collider other)
        {
            if (_behaviorInstances == null)
                return;

            for (int i = 0; i < _behaviorInstances.Count; i++)
            {
                var instance = _behaviorInstances[i];
                if (instance.GameObject == source)
                    instance.Runtime.DispatchPhysics(instance, message, collision, other);
            }
        }

        #endregion
    }
}
