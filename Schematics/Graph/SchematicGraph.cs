using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Remedy.Schematics
{
    [SchematicType("Objects", Order = 0, ListsPrefabs = true, Icon = "SchematicTab:object", NewAssetName = "New Schematic Object")]
    public class SchematicGraph : Graph, ISchematicAsset
    {
        /// <summary>Swaps outdated nodes for the ones replacing them (see <see cref="SchematicGraphNode.Replacement"/>).</summary>
        public override void OnGraphValidate()
        {
            base.OnGraphValidate();
            bool replaced = false;
            foreach (var node in new System.Collections.Generic.List<BlueGraph.Node>(Nodes))
            {
                if (node is not SchematicGraphNode old)
                    continue;
                SchematicGraphNode replacement;
                try
                {
                    replacement = old.Replacement();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    continue;
                }
                if (replacement == null)
                    continue;
                replacement.CustomName = old.CustomName;
                ReplaceNode(old, replacement, old.OldPortFor);
                replaced = true;
            }
#if UNITY_EDITOR
            if (replaced)
                UnityEditor.EditorApplication.delayCall += () => { if (this != null) UnityEditor.EditorUtility.SetDirty(this); };
#endif
        }

        [SerializeField, HideInInspector]
        private string _uid;

        /// <summary>This graph's permanent id (see <see cref="ISchematicAsset"/>).</summary>
        public string Uid => SchematicUid.Ensure(ref _uid, this);

        public void RegenerateUid() => _uid = SchematicUid.New();

        public override string Title
        {
            get { return Prefab == null ? "SCHEMATIC" : "$ Prefab: " + Prefab.name; }
        }

        public GameObject Prefab;

#if UNITY_EDITOR
        public GUID PrefabGUID;
        public GlobalObjectId PrefabID;
#endif

        [SerializeField]
        [TextArea(1, 3)]
        private string _description;

        [Tooltip("Sync this graph over the network: its events run on the object's authority, and nodes set to Everyone run on every peer.")]
        public bool Networked;
        
        [SerializeField, HideInInspector]
        private List<GraphVariable> _variables = new();
        [SerializeField, HideInInspector]
        private List<GraphSignal> _signals = new();

        /// <summary>This graph's own Variables: every object running it has its own values.</summary>
        public List<GraphVariable> Variables => _variables ??= new();

        /// <summary>This graph's own Signals, routed per object like Behavior Signals.</summary>
        public List<GraphSignal> Signals => _signals ??= new();

        /// <summary>A graph Variable by id (or, failing that, by name).</summary>
        /// <summary>
        /// The graph's Parameters: values whatever runs it gives it, set with Set Graph Parameters (a Sound Cue's
        /// Parameter nodes, say). None by default.
        /// </summary>
        public virtual IReadOnlyList<GraphParameter> GraphParameters => Array.Empty<GraphParameter>();

        [SerializeField, HideInInspector]
        private GraphFunctionInfo _function = new();

        /// <summary>Whether graphs of this kind can be called as functions yet (a Function node for them exists).</summary>
        public virtual bool CallableAsFunction => false;

        /// <summary>Whether (and where) it's offered as a function in the node search, and what it's for.</summary>
        public GraphFunctionInfo Function => _function ??= new();

        /// <summary>What the graph does (shown with it as a function, and given to the assistant).</summary>
        public string Description
        {
            get => _description;
            set => _description = value;
        }

        /// <summary>
        /// What a call to it as a function takes besides its Parameters: a Mesh Graph's Mesh Inputs, say. None by
        /// default.
        /// </summary>
        public virtual IReadOnlyList<GraphPinInfo> FunctionInputs => Array.Empty<GraphPinInfo>();

        /// <summary>What a call to it as a function gives back (a Mesh Graph's model). None by default.</summary>
        public virtual IReadOnlyList<GraphPinInfo> FunctionOutputs => Array.Empty<GraphPinInfo>();

        public GraphVariable FindVariable(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            foreach (var variable in Variables)
                if (variable != null && variable.Id == idOrName) return variable;
            foreach (var variable in Variables)
                if (variable != null && variable.Name == idOrName) return variable;
            return null;
        }

        public int IndexOfVariable(string id)
        {
            for (int i = 0; i < Variables.Count; i++)
                if (Variables[i] != null && Variables[i].Id == id) return i;
            return -1;
        }

        /// <summary>The graph Signal whose asset this is, or null.</summary>
        public GraphSignal FindSignal(SignalData asset)
        {
            if (asset == null) return null;
            foreach (var signal in Signals)
                if (signal != null && signal.Asset == asset) return signal;
            return null;
        }

        [HideInInspector]
        public bool Returned;
        public object ReturnValue;

        [HideInInspector]
        [SerializeField]
        private SerializableDictionary<SerializableType, SchematicEventNode[]> _eventNodesByType;
        public SerializableDictionary<SerializableType, SchematicEventNode[]> EventNodesByType
        {
            get
            {
                EnsureRuntimeCaches();
                return _eventNodesByType ??= new();
            }
        }
        [HideInInspector]
        [SerializeField]
        private SendSignalNode[] _flowInvokeNodesCache = new SendSignalNode[0];
        public SendSignalNode[] FlowInvokeNodesCache
        {
            get
            {
                EnsureRuntimeCaches();
                return _flowInvokeNodesCache;
            }
        }
        [HideInInspector]
        [SerializeField]
        private OnSignalReceivedNode[] _flowOnInvokeNodesCache = new OnSignalReceivedNode[0];
        public OnSignalReceivedNode[] FlowOnInvokeCache
        {
            get
            {
                EnsureRuntimeCaches();
                return _flowOnInvokeNodesCache;
            }
        }

        // The event and Signal node caches above are saved with the graph, but saved as copies of the nodes (they're
        // plain serialized fields, not references), and the editor only rebuilds them in edit mode when the graph is
        // validated. A copy can therefore be stale (an On Start still wired to a node that was replaced), and copies
        // don't get the real nodes' connections. So the first time a playing game (or a build) asks for them, they're
        // rebuilt from the graph's real nodes, once per play session.
        private static int _cacheSession;
        [NonSerialized] private int _builtForSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewCacheSession() => _cacheSession++;

        private void EnsureRuntimeCaches()
        {
            if (!Application.isPlaying || _builtForSession == _cacheSession)
                return;
            _builtForSession = _cacheSession; // first, so the rebuild's own use of the caches doesn't come back here
            CacheNodesByType();
        }

//        [ReadOnly]
        [Tooltip("A Dictionary pairing the Prefab's original Children to the Instantiated copies of them.")]
        public SerializableDictionary<UnityEngine.Object, UnityEngine.Object> OriginalToInstantiatedChildren = new();
//        [ReadOnly]
        [Tooltip("A Dictionary pairing the Prefab's original Children's paths to their original references.")]
        public Dictionary<string, GameObject> PathsToChildInstances = new();

        [HideInInspector]
        public int CurrentSchematicInstance = 0;

        /// <summary>
        /// Get the given Event Node from the Graph
        /// </summary>
        /// <typeparam name="TEventNode"></typeparam>
        /// <returns></returns>
        public SchematicEventNode[] GetEventArray<TEventNode>() where TEventNode : SchematicEventNode
        {
            // A graph without any node of this type has no entry.
            return EventNodesByType.TryGetValue(typeof(TEventNode), out var nodes) ? nodes : null;
        }

        /// <summary>
        /// Trigger all the Events of the given type present in the Graph
        /// </summary>
        /// <typeparam name="TEventNode"></typeparam>
        public void TriggerEvent<TEventNode>(GameObject instance, params Union[] args) where TEventNode : SchematicEventNode
        {
            var eventArr = GetEventArray<TEventNode>();

            if (eventArr == null || eventArr.Length == 0)
                return;
            // A networked graph's events run on the object's authority only; other peers run what it replicates.
            if (!GraphNetwork.RunsEventsFor(this, instance))
                return;
            foreach (var node in eventArr) { node.Trigger(instance, args); }
        }

        protected override void ResetExtendedNodeCaches()
        {
            EventNodesByType.Clear();
            _flowInvokeNodesCache = new SendSignalNode[0];
            _flowOnInvokeNodesCache = new OnSignalReceivedNode[0];
        }

        protected override void CacheNodeByType(Node node, CacheToAttribute attr)
        {
            base.CacheNodeByType(node, attr);
            
            if(attr != null)
            {
                if (typeof(SchematicEventNode).IsAssignableFrom(node.GetType()))
                {
                    var eventNode = (SchematicEventNode)node;
                    if (!EventNodesByType.ContainsKey(attr.Type))
                        EventNodesByType.Add(attr.Type, new SchematicEventNode[0]);
                    EventNodesByType[attr.Type] = EventNodesByType[attr.Type].Append(eventNode).ToArray();
                }
            }
            if (attr == null || attr.CacheAsBoth)
            {
                var type = node.GetType();

                if (typeof(SchematicEventNode).IsAssignableFrom(type))
                {
                    var eventNode = (SchematicEventNode)node;
                    if (!EventNodesByType.ContainsKey(type) || EventNodesByType[type] == null)
                        EventNodesByType[type] = new SchematicEventNode[0];
                    EventNodesByType[type] = EventNodesByType[type].Append(eventNode).ToArray();
                }

                if (typeof(SendSignalNode).IsAssignableFrom(type))
                {
                    if (!_flowInvokeNodesCache.Contains(node))
                        _flowInvokeNodesCache = _flowInvokeNodesCache.Append((SendSignalNode)node).ToArray();

                    var invokeNode = (SendSignalNode)node;
                    invokeNode.UpdateCaches();
                }
                if (typeof(OnSignalReceivedNode).IsAssignableFrom(type))
                {
                    if (!_flowOnInvokeNodesCache.Contains(node))
                        _flowOnInvokeNodesCache = _flowOnInvokeNodesCache.Append((OnSignalReceivedNode)node).ToArray();

                    var invokeNode = (OnSignalReceivedNode)node;
                    invokeNode.UpdateCaches();
                }
            }
        }
    }

    public enum ComparisonOperator
    {
        Equal,
        GreaterThan,
        LessThan,
        EqualOrGreaterThan,
        EqualOrLessThan
    }

    [Serializable]
    public class ScriptGraphPropertySerializer
    {
        public string Name;
        //[Expandable]
        public ScriptGraphParameter reference;
    }

    /// <summary>
    /// A helper class for Instances of Node Connections so that the Graph can resolve connections if they're broken.
    /// </summary>
    [Serializable]
    public class ConnectionInfo
    {
        public Port Port;
        public List<Connection> Connections = new();

        public ConnectionInfo(Port port)
        {
            Port = port;
        }

        /// <summary>
        /// Iterates through the Ports that were meant to be connected and reconnects them if they're not
        /// </summary>
        public void Rewire()
        {
            Port.Connections = Connections;
        }
    }
}
