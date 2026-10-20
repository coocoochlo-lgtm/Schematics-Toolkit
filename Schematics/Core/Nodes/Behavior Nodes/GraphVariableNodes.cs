using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Base for nodes that read or write a graph Variable: this graph's own, or (with <see cref="Owner"/> set) another
    /// graph's. The value belongs to one Schematic Object: the one running the flow, or the one wired into "Of".
    /// </summary>
    [Serializable]
    public abstract class GraphVariableNodeBase : SchematicActionNode
    {
        public const string ValuePort = "Value";
        public const string OfPort = "Of";

        [HideInInspector, Tooltip("The graph that declares the Variable. Empty: the graph this node is in.")]
        public SchematicGraph Owner;

        [HideInInspector]
        public string VariableId;

        /// <summary>The graph that declares the Variable.</summary>
        public SchematicGraph DeclaringGraph => Owner != null ? Owner : Graph as SchematicGraph;

        public GraphVariable Definition => DeclaringGraph != null ? DeclaringGraph.FindVariable(VariableId) : null;

        /// <summary>The store of the object this node acts on (null if it doesn't run that graph's Variables).</summary>
        protected GraphVariableStore ResolveStore(GameObject instance, out int index)
        {
            index = -1;
            var graph = DeclaringGraph;
            if (graph == null)
                return null;

            var target = instance;
            if (GetPort(OfPort) != null && HasInputConnections(OfPort))
                target = GetInputValue<GameObject>(OfPort, null);
            var owner = target != null ? target.GetComponentInParent<SchematicObject>() : null;
            if (owner == null)
            {
                Debug.LogWarning($"[{Name}] {(target != null ? target.name : "Nothing")} isn't a Schematic Object, so it has no graph Variables.", instance);
                return null;
            }

            var store = owner.GraphVariables(graph);
            index = store != null ? store.IndexOf(VariableId) : -1;
            if (index < 0)
                Debug.LogWarning($"[{Name}] {graph.name} has no Variable {VariableId} anymore.", instance);
            return store;
        }

        /// <summary>Adds a data port if it's missing or has the wrong type. Used by the editor when building the node.</summary>
        public bool EnsurePort(string name, Type type, PortDirection direction)
        {
            var existing = GetPort(name);
            if (existing != null)
            {
                if (existing.Type == type && existing.Direction == direction)
                    return false;
                RemovePort(existing);
            }
            AddPort(new Port
            {
                Name = name,
                Type = type ?? typeof(object),
                Direction = direction,
                Capacity = direction == PortDirection.Input ? PortCapacity.Single : PortCapacity.Multiple,
            });
            return true;
        }

        /// <summary>Makes the node's pins match its Variable (its type may have changed). True if anything changed.</summary>
        public bool SyncPorts()
        {
            var type = Definition?.ValueType ?? typeof(object);
            bool changed = EnsurePort(OfPort, typeof(GameObject), PortDirection.Input);
            changed |= EnsurePort(ValuePort, type, this is SetGraphVariableNode ? PortDirection.Input : PortDirection.Output);
            var remove = new List<Port>();
            foreach (var port in Ports.Values)
                if (port.Type != typeof(ActionPort) && port.Name != OfPort && port.Name != ValuePort)
                    remove.Add(port);
            foreach (var port in remove)
                RemovePort(port);
            if (changed || remove.Count > 0)
                IsDirty = true;
            return changed || remove.Count > 0;
        }
    }

    /// <summary>
    /// Reads a graph Variable: one of this graph's own (made in the Graph tab), or another graph's. Each Schematic
    /// Object has its own value; wire an object into "Of" to read another object's.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Get Graph Variable"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic/Graph"", ""description"": ""Reads a graph Variable: a value that belongs to the graph (made in the Graph tab) rather than to a Behavior. Each object running the graph has its own value; wire an object into Of to read another object's."", ""use_for"": [""remember things in the graph itself (a counter, a phase, a chosen target)"", ""read a cutscene's or another graph's state""], ""notes"": ""Made by dragging a Variable from the Graph tab and choosing Get.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Get Graph Variable", Path = "Schematic/Graph"), Tags("Object")]
    public class GetGraphVariableNode : GraphVariableNodeBase
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var store = ResolveStore(instance, out int index);
            if (store == null || index < 0)
                return;
            SetOutputValue(ValuePort, SignalArgs.ToUnion<object>(store.Get(index)));
        }

        // A Variable can be read any time, so connected inputs get its current value even when flow never passed here.
        protected override bool AnswersOnRequest => true;

        public override object OnRequestValue(Port port)
        {
            var instance = RunningInstance;
            if (instance == null && !HasInputConnections(OfPort))
                return null;
            var store = ResolveStore(instance, out int index);
            return store != null && index >= 0 ? store.Get(index) : null;
        }
    }

    /// <summary>
    /// Writes a graph Variable: one of this graph's own (made in the Graph tab), or another graph's. Each Schematic
    /// Object has its own value; wire an object into "Of" to write another object's.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Set Graph Variable"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic/Graph"", ""description"": ""Writes a graph Variable: a value that belongs to the graph (made in the Graph tab) rather than to a Behavior. Each object running the graph has its own value; wire an object into Of to write another object's."", ""use_for"": [""keep track of progress or state in the graph"", ""set a cutscene's or another graph's Variables""], ""notes"": ""Made by dragging a Variable from the Graph tab and choosing Set. An action: wire flow into it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Set Graph Variable", Path = "Schematic/Graph"), Tags("Object")]
    public class SetGraphVariableNode : GraphVariableNodeBase
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var store = ResolveStore(instance, out int index);
            if (store == null || index < 0)
                return;
            var type = store.TypeOf(index);
            store.Set(index, SignalArgs.FromUnion(GetInputValue<Union>(ValuePort, Union.Null), type));
        }
    }
}
