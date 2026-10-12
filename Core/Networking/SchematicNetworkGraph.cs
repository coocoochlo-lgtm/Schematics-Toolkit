#if SCHEMATICS_NETCODE
using Remedy.Framework;
using Remedy.Schematics;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Remedy.Networking
{
    /// <summary>
    /// Syncs a Schematic Object's networked graph. Its graph events run on the object's authority only, and nodes set
    /// to <see cref="NodeNetworkMode.Everyone"/> are sent to every other peer to run there, with the same inputs.
    /// Variables marked Network Sync (Behavior and graph Variables) are sent by the authority when they change, and
    /// peers that join late get them all.
    /// Put it (and a NetworkObject) on the Schematic Object of any prefab whose graph is marked Networked.
    /// </summary>
    [AddComponentMenu("Schematics/Networking/Schematic Network Graph")]
    [RequireComponent(typeof(NetworkObject))]
    public class SchematicNetworkGraph : NetworkBehaviour
    {
        private static readonly Dictionary<GameObject, SchematicNetworkGraph> _byObject = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            _byObject.Clear();
            _nodes.Clear();
            GraphNetwork.HasAuthority = HasAuthorityOver;
            GraphNetwork.Replicate = Replicate;
        }

        [Tooltip("How many times a second the authority sends Variables marked Network Sync that changed.")]
        [SerializeField, Min(1f)] private float _variableSendRate = 20f;

        private SchematicObject _owner;
        private float _nextVariableSend;
        private static readonly List<(string key, object value)> _changes = new();

        public override void OnNetworkSpawn()
        {
            _byObject[gameObject] = this;
            _owner = GetComponent<SchematicObject>();
            // Joining late: ask the authority for every synced Variable's current value.
            if (!HasAuthority)
                RequestVariablesRpc();
        }

        private void Update()
        {
            if (!IsSpawned || !HasAuthority || _owner == null || Time.unscaledTime < _nextVariableSend)
                return;
            _nextVariableSend = Time.unscaledTime + 1f / Mathf.Max(1f, _variableSendRate);
            _changes.Clear();
            VariableSync.CollectChanges(_owner, _changes);
            if (_changes.Count > 0)
                VariablesRpc(Pack(_changes));
        }

        private static byte[] Pack(List<(string key, object value)> changes)
        {
            var keys = new string[changes.Count];
            var values = new Union[changes.Count];
            for (int i = 0; i < changes.Count; i++)
            {
                keys[i] = changes[i].key;
                values[i] = SignalArgs.ToUnion<object>(changes[i].value);
            }
            return UnionNetworkSerializer.Pack("", keys, values);
        }

        private void ApplyVariables(byte[] payload)
        {
            if (_owner == null)
                _owner = GetComponent<SchematicObject>();
            UnionNetworkSerializer.Unpack(payload, out _, out string[] keys, out Union[] values);
            for (int i = 0; i < keys.Length; i++)
            {
                var type = VariableSync.TypeOf(_owner, keys[i]);
                if (type != null)
                    VariableSync.Apply(_owner, keys[i], SignalArgs.FromUnion(values[i], type));
            }
        }

        [Rpc(SendTo.NotMe)]
        private void VariablesRpc(byte[] payload) => ApplyVariables(payload);

        [Rpc(SendTo.Authority)]
        private void RequestVariablesRpc(RpcParams rpcParams = default)
        {
            if (_owner == null)
                _owner = GetComponent<SchematicObject>();
            _changes.Clear();
            VariableSync.CollectChanges(_owner, _changes, all: true);
            if (_changes.Count > 0)
                AllVariablesRpc(Pack(_changes), RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void AllVariablesRpc(byte[] payload, RpcParams rpcParams) => ApplyVariables(payload);

        public override void OnNetworkDespawn()
        {
            _byObject.Remove(gameObject);
        }

        public override void OnDestroy()
        {
            _byObject.Remove(gameObject);
            base.OnDestroy();
        }

        // Offline, or before the object spawns on the network, everyone runs everything as usual.
        private static SchematicNetworkGraph For(GameObject instance)
        {
            if (instance == null)
                return null;
            if (_byObject.TryGetValue(instance, out var graph))
                return graph;
            var owner = instance.GetComponentInParent<SchematicObject>();
            return owner != null && _byObject.TryGetValue(owner.gameObject, out graph) ? graph : null;
        }

        private static bool HasAuthorityOver(GameObject instance)
        {
            var graph = For(instance);
            return graph == null || !graph.IsSpawned || graph.HasAuthority;
        }

        private static void Replicate(SchematicGraphNode node, GameObject instance, string[] ports, Union[] values)
        {
            var graph = For(instance);
            if (graph == null || !graph.IsSpawned)
                return;
            graph.RunNodeRpc(UnionNetworkSerializer.Pack(node.ID, ports, values));
        }

        [Rpc(SendTo.NotMe)]
        private void RunNodeRpc(byte[] payload)
        {
            UnionNetworkSerializer.Unpack(payload, out string nodeId, out string[] ports, out Union[] values);
            if (FindNode(nodeId) is SchematicActionNodeBase node)
                Flow.StartWithInputs(node, gameObject, ports, values);
            else
                Debug.LogWarning($"Couldn't find the replicated graph node {nodeId} on {name}.", this);
        }

        // Nodes by ID across every loaded graph (an object's own graph, its state graphs, singletons).
        private static readonly Dictionary<string, SchematicGraphNode> _nodes = new();

        private SchematicGraphNode FindNode(string id)
        {
            if (_nodes.TryGetValue(id, out var node) && node != null)
                return node;

            var own = GetComponent<SchematicObject>();
            if (own != null && own.SchematicGraph != null && own.SchematicGraph.GetNodeById(id) is SchematicGraphNode found)
                return _nodes[id] = found;

            foreach (var graph in Resources.FindObjectsOfTypeAll<SchematicGraph>())
                foreach (var candidate in graph.Nodes)
                    if (candidate is SchematicGraphNode schematicNode)
                        _nodes[candidate.ID] = schematicNode;
            return _nodes.TryGetValue(id, out node) ? node : null;
        }
    }
}
#endif
