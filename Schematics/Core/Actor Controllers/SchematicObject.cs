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

        [SerializeField, HideInInspector]
        private int _objectId;

        [SerializeField, HideInInspector]
        private List<GraphVariableOverride> _graphOverrides = new();

        /// <summary>This object's own starting values for graph Variables (any graph it runs).</summary>
        public List<GraphVariableOverride> GraphOverrides => _graphOverrides ??= new();

        public SchematicValue FindGraphOverride(SchematicGraph graph, string id)
        {
            foreach (var entry in GraphOverrides)
                if (entry != null && entry.Graph == graph && entry.Id == id)
                    return entry.Value;
            return null;
        }

        /// <summary>Sets (or, with null, removes) this object's starting value for a graph Variable.</summary>
        public void SetGraphOverride(SchematicGraph graph, string id, object value, Type type)
        {
            GraphOverrides.RemoveAll(e => e == null || (e.Graph == graph && e.Id == id));
            if (type != null)
                GraphOverrides.Add(new GraphVariableOverride { Graph = graph, Id = id, Value = new SchematicValue(value, type) });
        }

        private Dictionary<SchematicGraph, GraphVariableStore> _graphStores;

        /// <summary>Raised when an object starts keeping values for one of its graphs (networking listens).</summary>
        public static event Action<GraphVariableStore> GraphStoreCreated;

        /// <summary>
        /// This object's values for a graph's Variables: its own graph by default, or any other graph it runs (a state's
        /// graph, a Timeline Graph). Made on first use; reset every time the object spawns.
        /// </summary>
        public GraphVariableStore GraphVariables(SchematicGraph graph = null)
        {
            graph ??= SchematicGraph;
            if (graph == null)
                return null;
            _graphStores ??= new Dictionary<SchematicGraph, GraphVariableStore>();
            if (_graphStores.TryGetValue(graph, out var store))
            {
                store.Refresh();
                return store;
            }
            store = new GraphVariableStore(this, graph);
            _graphStores[graph] = store;
            GraphStoreCreated?.Invoke(store);
            return store;
        }

        /// <summary>Every graph whose Variables this object is keeping right now.</summary>
        public IEnumerable<GraphVariableStore> GraphStores => _graphStores != null ? _graphStores.Values : Array.Empty<GraphVariableStore>();

        // A new spawn starts from the starting values.
        internal void ResetGraphVariables() => _graphStores?.Clear();

        /// <summary>
        /// Which Schematic Object (prefab) this is, the same for every instance of it and stable across sessions.
        /// Assigned in the editor when the Schematic is created, from the prefab asset's GUID (a duplicated prefab
        /// gets its own). 0 for objects that aren't Schematic prefabs (built at runtime or placed by hand).
        /// </summary>
        public int ObjectID => _objectId;

        /// <summary>Sets the ObjectID: the editor does this for prefabs; runtime-built objects (singletons) may too.</summary>
        public void AssignObjectID(int id) => _objectId = id;

        /// <summary>A stable, non-zero ObjectID for a key (e.g. a prefab asset GUID): FNV-1a, kept positive.</summary>
        public static int ObjectIDFor(string key)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in key ?? "")
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                int id = (int)(hash & 0x7FFFFFFF);
                return id == 0 ? 1 : id;
            }
        }

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

        /// <summary>Raised after an object's On Create ran (Timeline Graphs that play on start listen).</summary>
        public static event Action<SchematicObject> Started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Started = null;
            GraphStoreCreated = null;
        }

        private void Start()
        {
            if (SchematicGraph != null)
                SchematicGraph.TriggerEvent<OnCreate>(gameObject);
            Started?.Invoke(this);
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

        protected virtual void OnCollisionStay(Collision collision)
        {
            ForwardStay(gameObject, BehaviorRuntime.PhysicsMessage.CollisionStay, collision, null);
        }

        protected virtual void OnTriggerStay(Collider collider)
        {
            ForwardStay(gameObject, BehaviorRuntime.PhysicsMessage.TriggerStay, null, collider);
        }

        protected virtual void OnTriggerExit(Collider collider)
        {
            ForwardPhysics(gameObject, BehaviorRuntime.PhysicsMessage.TriggerExit, null, collider);
            if (SchematicGraph == null) return;
            SchematicGraph.TriggerEvent<OnCollisionExit>(gameObject, collider);
        }

#if UNITY_EDITOR
        // Behaviors draw their Gizmos for every object they're attached to, in Edit Mode as well as Play Mode.
        private void OnDrawGizmos()
        {
            foreach (var entry in Behaviors)
            {
                var target = entry.Key;
                if (target == null || entry.Value?.Items == null)
                    continue;

                foreach (var attachment in entry.Value.Items)
                {
                    var behaviorType = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                    var behavior = behaviorType != null ? GizmoBehaviors.Get(behaviorType) : null;
                    if (behavior == null)
                        continue;

                    BehaviorInstance instance = null;
                    if (_behaviorInstances != null)
                        foreach (var candidate in _behaviorInstances)
                            if (candidate.Attachment == attachment) { instance = candidate; break; }

                    // Spawned: draw with the live Behavior so the selection and Variables are the real ones.
                    var drawing = instance != null ? instance.Behavior : behavior;
                    var previous = drawing.Selected;
                    if (instance != null && instance.IsSpawned) drawing.Cursor.Select(instance);
                    try
                    {
                        drawing.OnDrawGizmos(new BehaviorGizmoContext(target, this, instance, attachment, drawing));
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, target);
                    }
                    finally
                    {
                        drawing.Cursor.Select(previous);
                    }
                }
            }
        }

        // One Behavior object per type for drawing Gizmos in Edit Mode, when there are no Runtimes.
        // Null for types that don't draw Gizmos, so the check costs one lookup per repaint.
        private static class GizmoBehaviors
        {
            private static readonly Dictionary<Type, SchematicBehavior> _cache = new();

            public static SchematicBehavior Get(Type type)
            {
                if (_cache.TryGetValue(type, out var behavior))
                    return behavior;
                try
                {
                    behavior = SchematicBehavior.Create(type);
                    if (!behavior.Overrides(nameof(SchematicBehavior.OnDrawGizmos)))
                        behavior = null;
                }
                catch (Exception exception) { Debug.LogException(exception); }
                _cache[type] = behavior;
                return behavior;
            }
        }
#endif

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

                    AddInstance(target, runtime, attachment);
                }
            }

            return _behaviorInstances;
        }

        private BehaviorInstance AddInstance(GameObject target, BehaviorRuntime runtime, BehaviorAttachment attachment)
        {
            var instance = new BehaviorInstance(this, target, runtime, attachment);
            _behaviorInstances.Add(instance);
            ExecutionContext.Track(instance);

            // Unity only sends physics messages to the colliding object itself, so children need a relay.
            if (runtime.HasPhysicsCallbacks && target != gameObject && !target.TryGetComponent(out SchematicPhysicsRelay _))
                target.AddComponent<SchematicPhysicsRelay>().Owner = this;

            return instance;
        }

        /// <summary>
        /// Finds the saved attachment of a Behavior type, on the given GameObject or (with null) on any of them.
        /// </summary>
        public BehaviorAttachment FindAttachment(Type behaviorType, GameObject target = null)
        {
            foreach (var entry in Behaviors)
            {
                if (target != null && entry.Key != target)
                    continue;
                if (entry.Value?.Items == null)
                    continue;
                foreach (var attachment in entry.Value.Items)
                    if (attachment?.Metadata != null && attachment.Metadata.BehaviorType.Type == behaviorType)
                        return attachment;
            }
            return null;
        }

        /// <summary>
        /// Attaches a Behavior to the saved data, as adding it in the Inspector does (including its required components
        /// and OnSetup). For editor scripts that build prefabs. At runtime, use <see cref="AddBehavior"/>.
        /// </summary>
        public BehaviorAttachment AttachBehavior(Type behaviorType, GameObject target = null)
        {
            if (target == null)
                target = gameObject;

            var existing = FindAttachment(behaviorType, target);
            if (existing != null)
                return existing;

            foreach (var requiredBehavior in SchematicBehavior.GetRequiredBehaviors(behaviorType))
                AttachBehavior(requiredBehavior, target);

            foreach (var required in SchematicBehavior.GetRequiredComponents(behaviorType))
                if (!target.GetComponent(required))
                    target.AddComponent(required);

            if (!Behaviors.TryGetValue(target, out var list) || list == null)
            {
                list = new BehaviorAttachmentList();
                Behaviors[target] = list;
            }

            var attachment = new BehaviorAttachment { Metadata = BehaviorMetadata.Get(behaviorType) };
            if (attachment.Metadata == null)
                Debug.LogWarning($"{behaviorType.Name} has no Behavior Metadata yet. Use Tools/Remedy/Schematics/Refresh Behavior Metadata, then attach it again.", this);
            list.Items.Add(attachment);

            SchematicBehavior.RunSetup(behaviorType, target, attachment);
            return attachment;
        }

        public BehaviorAttachment AttachBehavior<TBehavior>(GameObject target = null) where TBehavior : SchematicBehavior
            => AttachBehavior(typeof(TBehavior), target);

        /// <summary>
        /// Attaches a Behavior at runtime, the Schematic counterpart to AddComponent. If this object is spawned, the
        /// new instance spawns right away (OnCreate, OnStart, OnEnable). Returns the existing instance if the
        /// Behavior is already attached to the target. Runtime attachments aren't saved to the prefab.
        /// </summary>
        /// <param name="target">The GameObject to attach to: this object or one of its children. Defaults to this object.</param>
        public BehaviorInstance AddBehavior<TBehavior>(GameObject target = null, bool enabled = true) where TBehavior : SchematicBehavior
            => AddBehavior(typeof(TBehavior), target, enabled);

        public BehaviorInstance AddBehavior(Type behaviorType, GameObject target = null, bool enabled = true)
        {
            if (target == null)
                target = gameObject;

            GetOrCreateBehaviorInstances();
            var existing = GetBehavior(behaviorType, target);
            if (existing != null)
                return existing;

            foreach (var requiredBehavior in SchematicBehavior.GetRequiredBehaviors(behaviorType))
                AddBehavior(requiredBehavior, target, enabled);

            foreach (var required in SchematicBehavior.GetRequiredComponents(behaviorType))
                if (!target.GetComponent(required))
                    target.AddComponent(required);

            var runtime = ExecutionContext.GetRuntime(behaviorType);
            var instance = AddInstance(target, runtime, new BehaviorAttachment { Metadata = runtime.Metadata, EnabledByDefault = enabled });

            if (IsSpawned)
                ExecutionContext.SpawnAttached(instance);
            return instance;
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

        // Stay messages arrive every physics step for every contact; only Behaviors that handle them are visited.
        internal void ForwardStay(GameObject source, BehaviorRuntime.PhysicsMessage message, Collision collision, Collider other)
        {
            if (_behaviorInstances == null)
                return;

            for (int i = 0; i < _behaviorInstances.Count; i++)
            {
                var instance = _behaviorInstances[i];
                if (instance.Runtime.HasStayCallbacks && instance.GameObject == source)
                    instance.Runtime.DispatchPhysics(instance, message, collision, other);
            }
        }

        /// <summary>
        /// Sends a physics message to the enabled Behavior Instances attached to <paramref name="source"/>.
        /// </summary>
        /// <summary>
        /// Delivers a physics impact to the Behaviors attached to a GameObject (a Schematic Object or one of its
        /// children). Called by physics bridges such as the Box3D impact relay.
        /// </summary>
        public static void SendImpact(GameObject target, in PhysicsImpact impact)
        {
            if (target == null)
                return;
            var owner = target.GetComponentInParent<SchematicObject>();
            if (owner == null || owner._behaviorInstances == null)
                return;

            for (int i = 0; i < owner._behaviorInstances.Count; i++)
            {
                var instance = owner._behaviorInstances[i];
                if (instance.GameObject == target)
                    instance.Runtime.DispatchImpact(instance, impact);
            }
        }

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
