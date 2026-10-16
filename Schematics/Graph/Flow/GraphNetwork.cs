using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>How a node behaves in a networked graph.</summary>
    public enum NodeNetworkMode
    {
        /// <summary>Runs wherever its flow runs (the object's authority, in a networked graph).</summary>
        Local,
        /// <summary>
        /// Runs on every peer: when the authority reaches it, everyone else runs it too, with the same input values, and
        /// carries on with the nodes after it.
        /// </summary>
        Everyone,
    }

    /// <summary>
    /// The seam between graphs and the networking layer (Remedy.Networking, when Netcode for GameObjects is installed).
    /// Graphs don't reference Netcode; the networking layer fills these in. With nothing installed, every graph runs
    /// locally as before.
    /// </summary>
    public static class GraphNetwork
    {
        /// <summary>
        /// Whether this peer runs the given object's networked graph events (it has authority over it). Unset: true.
        /// </summary>
        public static Func<GameObject, bool> HasAuthority;

        /// <summary>
        /// Sends a node to the other peers, to run on their copy of the object with these input values. Set by the
        /// networking layer.
        /// </summary>
        public static Action<SchematicGraphNode, GameObject, string[], Union[]> Replicate;

        /// <summary>Whether this peer should run a networked graph's events for the object.</summary>
        internal static bool RunsEventsFor(SchematicGraph graph, GameObject instance)
            => graph == null || !graph.Networked || HasAuthority == null || instance == null || HasAuthority(instance);

        [ThreadStatic] private static List<string> _ports;
        [ThreadStatic] private static List<Union> _values;

        /// <summary>Sends an Everyone node about to run on the authority to the other peers.</summary>
        internal static void ReplicateIfNeeded(SchematicGraphNode node, GameObject instance, FlowScope scope)
        {
            if (Replicate == null || node.NetworkMode != NodeNetworkMode.Everyone || node.Graph is not SchematicGraph graph || !graph.Networked)
                return;
            if (instance == null || (HasAuthority != null && !HasAuthority(instance)))
                return;

            _ports ??= new List<string>();
            _values ??= new List<Union>();
            _ports.Clear();
            _values.Clear();
            foreach (var port in node.Ports.Values)
            {
                if (port.Direction != BlueGraph.PortDirection.Input || port.Type == typeof(ActionPort))
                    continue;
                if (node.PeekInput(port.Name, scope, out var value))
                {
                    _ports.Add(port.Name);
                    _values.Add(value);
                }
            }
            Replicate(node, instance, _ports.ToArray(), _values.ToArray());
        }
    }
}
