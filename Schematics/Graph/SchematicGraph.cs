using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Remedy.Schematics.Utils;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Remedy.Schematics
{
    public class SchematicGraph : Graph
    {
        /// <summary>
        /// Used with SignalRef, moving Signal ID's after this position down one to match the List.
        /// </summary>
        public Action<byte> OnSignalRemoved;

        public override string Title
        {
            get { return Prefab == null ? "SCHEMATIC" : "$ Prefab: " + Prefab.name; }
        }

        private static Dictionary<int, SchematicGraph> _lookup = new Dictionary<int, SchematicGraph>();

        // Serialized index for persistence
        //[SerializeField, ReadOnly]
        private int _id = -1;
        public int ID => _id;

        private static int _idCounter = 0;

        public static Dictionary<int, SchematicGraph> Lookup
        {
            get
            {
                if (_lookup.Count == 0)
                {
                    _idCounter = 0;

                    var objs = Resources.LoadAll<SchematicGraph>("");
                    // Clear old lookup to avoid duplication on domain reload
                    _lookup.Clear();

                    // Assign stable indices based on loaded order
                    for (int i = 0; i < objs.Length; i++)
                    {
                        var obj = objs[i];
                        if (obj._id != i)
                        {
                            obj._id = i;
                            _idCounter++;

#if UNITY_EDITOR
                            UnityEditor.EditorUtility.SetDirty(obj);
                            UnityEditor.AssetDatabase.SaveAssets();
#endif
                        }
                        if (!_lookup.ContainsKey(i))
                        {
                            _lookup[i] = obj;
                        }
                    }
                }
                return _lookup;
            }
        }

        private void OnEnable()
        {
            if (_id == -1)
                _id = _idCounter++;
        }

        public GameObject Prefab;

        [SerializeField]
        private List<SignalData> _runtimeSignalCache;
        [SerializeField]
        private List<VariableData> _runtimeVariableCache;

        public bool TryGetSignal(int id, out SignalData signalData)
        {
            if(_runtimeSignalCache.Count > id)
            {
                signalData = _runtimeSignalCache[id];
                return true;
            }
            signalData = null;
            return false;
        }

        public bool TryGetVariable(int id, out VariableData variableData)
        {
            if (_runtimeSignalCache.Count > id)
            {
                variableData = _runtimeVariableCache[id];
                return true;
            }

            variableData = null;
            return false;
        }

#if UNITY_EDITOR
        public GUID PrefabGUID;
        public GlobalObjectId PrefabID;
        public List<GlobalObjectId> Components = new();

        public void ResetSignalCache()
        {
            _editorSignalCache.Clear();
        }

        public void AddSignalToCache(SignalData signal)
        {
            _editorSignalCache[signal.name] = signal;
        }

        private SerializableDictionary<string, SignalData> _editorSignalCache = new();
        public IReadOnlyDictionary<string, SignalData> EditorSignalCache => _editorSignalCache.GetReadOnlyDictionary();
        [SerializeField]
        private SerializableDictionary<string, VariableData> _editorVariableCache = new();
        public IReadOnlyDictionary<string, VariableData> EditorVariableCache => _editorVariableCache.GetReadOnlyDictionary();
#endif

        [SerializeField]
        [TextArea(1, 3)]
        private string _description;
        
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
            return _eventNodesByType[typeof(TEventNode)];
        }

        /// <summary>
        /// Trigger all the Events of the given type present in the Graph
        /// </summary>
        /// <typeparam name="TEventNode"></typeparam>
        public void TriggerEvent<TEventNode>(GameObject instance, params Union[] args) where TEventNode : SchematicEventNode
        {
            var eventArr = GetEventArray<TEventNode>();

            if (eventArr != null && eventArr.Length > 0)
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
