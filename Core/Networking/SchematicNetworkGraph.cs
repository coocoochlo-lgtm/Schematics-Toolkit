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
    /// peers that join late get them all. It also carries the object's networked Signals (see
    /// <see cref="SignalNetworkBridge"/>) and the state of Behaviors that keep their own (<see cref="INetworkStateBehavior"/>,
    /// e.g. who sits in which vehicle seat), late joiners included.
    /// Put it (and a NetworkObject) on the Schematic Object of any prefab whose graph is marked Networked, or that uses
    /// networked Variables or Signals (the GameObject header's Network Sync toggle adds it).
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
            // Joining late: ask the authority for every synced Variable's current value (and Behavior state).
            if (!HasAuthority)
                RequestVariablesRpc();
        }

        #region Signals

        /// <summary>
        /// The Schematic Network Graph that carries an object's networked Signals and state: its own, or the nearest one
        /// above it (for Schematic Objects nested inside a networked one). Null if none is spawned.
        /// </summary>
        internal static SchematicNetworkGraph CarrierOf(GameObject gameObject)
        {
            for (var t = gameObject != null ? gameObject.transform : null; t != null; t = t.parent)
                if (_byObject.TryGetValue(t.gameObject, out var graph) && graph != null && graph.IsSpawned)
                    return graph;
            return null;
        }

        /// <summary>The path from this object down to <paramref name="target"/> ("" for this object itself).</summary>
        internal string PathTo(GameObject target)
        {
            if (target == null || target == gameObject)
                return "";
            string path = target.name;
            for (var parent = target.transform.parent; parent != null && parent != transform; parent = parent.parent)
                path = parent.name + "/" + path;
            return path;
        }

        private GameObject FindBelow(string path)
        {
            if (string.IsNullOrEmpty(path))
                return gameObject;
            var found = transform.Find(path);
            return found != null ? found.gameObject : null;
        }

        /// <summary>Sends a packed Signal: to the authority (from the owner), or to everyone else.</summary>
        internal void SendSignal(byte[] payload, bool toAuthority)
        {
            if (toAuthority)
                SignalToAuthorityRpc(payload);
            else
                SignalRpc(payload);
        }

        [Rpc(SendTo.NotMe)]
        private void SignalRpc(byte[] payload) => ReceiveSignal(payload);

        // OwnerToAuthority: the authority checks the sender owns the object, fires it, and passes it on to everyone else.
        [Rpc(SendTo.Authority)]
        private void SignalToAuthorityRpc(byte[] payload, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (sender != OwnerClientId)
            {
                Debug.LogWarning($"Client {sender} sent a Signal to {name} that only its owner may send. Ignored.", this);
                return;
            }
            if (!ReceiveSignal(payload))
                return;
            var others = new[] { sender, NetworkManager.LocalClientId };
            SignalRelayRpc(payload, RpcTarget.Not(others, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void SignalRelayRpc(byte[] payload, RpcParams rpcParams) => ReceiveSignal(payload);

        // Fires a Signal that came from another peer on this peer's copy of the object. What its listeners send in turn
        // stays local: every peer runs them itself.
        private bool ReceiveSignal(byte[] payload)
        {
            if (!SignalNetworkBridge.Unpack(payload, out var signal, out string path, out var args))
                return false;
            var target = FindBelow(path);
            var schematicObject = target != null ? target.GetComponent<SchematicObject>() : null;
            if (schematicObject == null || !schematicObject.IsSpawned)
                return false; // never fall back to UID 0, which would reach every object
            using (SignalNetwork.LocalOnly())
                signal.InvokeFromGraph(schematicObject.UID, args);
            return true;
        }

        #endregion

        #region Behavior state

        /// <summary>Sends a Behavior instance's <see cref="INetworkStateBehavior"/> state, if this peer is its authority.</summary>
        internal static void SendState(BehaviorInstance instance)
        {
            if (instance == null || !instance.IsSpawned || instance.Behavior is not INetworkStateBehavior)
                return;
            var graph = CarrierOf(instance.GameObject);
            if (graph == null || !graph.HasAuthority)
                return;
            var payload = graph.PackState(instance);
            if (payload != null)
                graph.StateRpc(payload);
        }

        private static readonly List<Union> _state = new();

        private byte[] PackState(BehaviorInstance instance)
        {
            if (instance.Behavior is not INetworkStateBehavior stateful)
                return null;
            _state.Clear();
            stateful.WriteNetworkState(instance, _state);
            var keys = new string[_state.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = "";
            string key = PathTo(instance.GameObject) + "|" + instance.Behavior.GetType().AssemblyQualifiedName;
            return UnionNetworkSerializer.Pack(key, keys, _state.ToArray());
        }

        [Rpc(SendTo.NotMe)]
        private void StateRpc(byte[] payload) => ApplyState(payload);

        [Rpc(SendTo.SpecifiedInParams)]
        private void AllStatesRpc(byte[] payload, RpcParams rpcParams) => ApplyState(payload);

        private void ApplyState(byte[] payload)
        {
            UnionNetworkSerializer.Unpack(payload, out string key, out _, out Union[] values);
            int split = key.IndexOf('|');
            if (split < 0)
                return;
            var target = FindBelow(key.Substring(0, split));
            var type = System.Type.GetType(key.Substring(split + 1));
            var instance = target != null && type != null ? ExecutionContext.FindInstance(target, type) : null;
            if (instance == null || !instance.IsSpawned || instance.Behavior is not INetworkStateBehavior stateful)
                return;
            using (SignalNetwork.LocalOnly())
                stateful.ReadNetworkState(instance, values);
        }

        // Late joiners: the state of every Behavior on this object (and objects nested in it) that keeps its own.
        private void SendAllStates(ulong clientId)
        {
            foreach (var schematicObject in GetComponentsInChildren<SchematicObject>(true))
            {
                if (!schematicObject.IsSpawned || CarrierOf(schematicObject.gameObject) != this)
                    continue;
                foreach (var instance in schematicObject.BehaviorInstances)
                {
                    if (!instance.IsSpawned || instance.Behavior is not INetworkStateBehavior)
                        continue;
                    var payload = PackState(instance);
                    if (payload != null)
                        AllStatesRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
                }
            }
        }

        #endregion

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
        private void VariablesRpc(byte[] payload)
        {
            using (SignalNetwork.LocalOnly())
                ApplyVariables(payload);
        }

        [Rpc(SendTo.Authority)]
        private void RequestVariablesRpc(RpcParams rpcParams = default)
        {
            if (_owner == null)
                _owner = GetComponent<SchematicObject>();
            _changes.Clear();
            VariableSync.CollectChanges(_owner, _changes, all: true);
            if (_changes.Count > 0)
                AllVariablesRpc(Pack(_changes), RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
            SendAllStates(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void AllVariablesRpc(byte[] payload, RpcParams rpcParams)
        {
            using (SignalNetwork.LocalOnly())
                ApplyVariables(payload);
        }

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
            // Every peer runs the replicated node itself, so networked Signals it sends stay local.
            if (FindNode(nodeId) is SchematicActionNodeBase node)
                using (SignalNetwork.LocalOnly())
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
