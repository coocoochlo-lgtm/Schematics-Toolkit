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
    public class SchematicGraph : Graph
    {
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
        public SerializableDictionary<SerializableType, SchematicEventNode[]> EventNodesByType => _eventNodesByType ??= new(); 
        [HideInInspector]
        [SerializeField]
        private SendSignalNode[] _flowInvokeNodesCache = new SendSignalNode[0];
        public SendSignalNode[] FlowInvokeNodesCache => _flowInvokeNodesCache;
        [HideInInspector]
        [SerializeField]
        private OnSignalReceivedNode[] _flowOnInvokeNodesCache = new OnSignalReceivedNode[0];
        public OnSignalReceivedNode[] FlowOnInvokeCache => _flowOnInvokeNodesCache;

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
