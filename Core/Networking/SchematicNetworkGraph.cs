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

        public override void OnNetworkSpawn()
        {
            _byObject[gameObject] = this;
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
