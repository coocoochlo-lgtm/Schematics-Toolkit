using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A Variable that belongs to a Schematic Graph rather than to a Behavior. Every Schematic Object running the graph
    /// gets its own value, starting from <see cref="Default"/> (or the object's override). Read and written with the
    /// Get/Set Graph Variable nodes, by this graph or (with a target object) by other graphs.
    /// </summary>
    [Serializable]
    public class GraphVariable
    {
        [Tooltip("Stays the same when the Variable is renamed; nodes and overrides refer to this.")]
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "New Variable";
        public SerializableType Type = typeof(float);
        public SchematicValue Default = new(0f);
        [Tooltip("Sync this Variable over the network: the object's authority sends its value to everyone else.")]
        public bool Networked;
        [TextArea(1, 4)]
        public string Description;

        public Type ValueType => this.Type.Type;
    }

    /// <summary>A parameter of a graph Signal: a name and a type.</summary>
    [Serializable]
    public class GraphSignalParameter
    {
        public string Name = "Value";
        public SerializableType Type = typeof(float);
    }

    /// <summary>
    /// A Signal that belongs to a Schematic Graph. Its <see cref="Asset"/> is saved inside the graph asset, and is routed
    /// per object exactly like Behavior Signals: sending it reaches the graphs and Behaviors of one Schematic Object.
    /// </summary>
    [Serializable]
    public class GraphSignal
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "New Signal";
        public SignalData Asset;
        public List<GraphSignalParameter> Parameters = new();
        [TextArea(1, 4)]
        public string Description;

        /// <summary>
        /// Which way the Signal goes, seen from outside the graph: other graphs send its Input Signals (and this graph
        /// reacts to them), and react to its Output Signals (which this graph sends). Stored on the <see cref="Asset"/>,
        /// like <see cref="Networked"/>, so nodes in other graphs can check it without loading this one.
        /// </summary>
        public GraphSignalDirection Direction
        {
            get => Asset != null ? Asset.GraphDirection : GraphSignalDirection.Unspecified;
            set { if (Asset != null) Asset.GraphDirection = value; }
        }

        /// <summary>
        /// Network Sync: sending it fires it for every player. Stored on the Signal's <see cref="Asset"/>, which is what
        /// travels at runtime, so the graph and the asset can never disagree.
        /// </summary>
        public bool Networked
        {
            get => Asset != null && Asset.Networked;
            set { if (Asset != null) Asset.Networked = value; }
        }

        /// <summary>Who may send it when <see cref="Networked"/> (stored on the <see cref="Asset"/>).</summary>
        public SignalNetworkSender Sender
        {
            get => Asset != null ? Asset.Sender : SignalNetworkSender.Authority;
            set { if (Asset != null) Asset.Sender = value; }
        }
    }

    /// <summary>
    /// The direction of a graph's own Signal. Unspecified only for Signals made before graph Signals had a direction
    /// (the editor fills it in from how the graph uses them) and for Signals that aren't a graph's.
    /// </summary>
    public enum GraphSignalDirection
    {
        Unspecified = 0,
        /// <summary>Other graphs send it; the graph that declares it reacts to it.</summary>
        Input = 1,
        /// <summary>The graph that declares it sends it; other graphs react to it.</summary>
        Output = 2,
    }

    /// <summary>
    /// The rules that make graph Signals directional. From another graph you may only send a graph's Input Signals
    /// and react to its Output Signals; inside the graph that declares them it's the other way round (it reacts to its
    /// own Inputs and sends its own Outputs). Signals without a direction (Behavior Signals, global ones) are free.
    /// </summary>
    public static class GraphSignalRules
    {
        /// <summary>Why <paramref name="user"/> may not send this Signal, or null if it may.</summary>
        public static string SendError(SchematicGraph user, SignalData signal)
        {
            if (user == null || signal == null)
                return null;
            var direction = signal.GraphDirection;
            if (direction == GraphSignalDirection.Unspecified)
                return null;
            bool own = user.FindSignal(signal) != null;
            if (own && direction == GraphSignalDirection.Input)
                return $"\"{signal.name}\" is one of this graph's Input Signals: other graphs send it, and this graph reacts to it (On Received). Make it an Output Signal to send it from here.";
            if (!own && direction == GraphSignalDirection.Output)
                return $"\"{signal.name}\" is an Output Signal of another graph: only that graph sends it. React to it here with On Received instead.";
            return null;
        }

        /// <summary>Why <paramref name="user"/> may not react to this Signal, or null if it may.</summary>
        public static string ReceiveError(SchematicGraph user, SignalData signal)
        {
            if (user == null || signal == null)
                return null;
            var direction = signal.GraphDirection;
            if (direction == GraphSignalDirection.Unspecified)
                return null;
            bool own = user.FindSignal(signal) != null;
            if (own && direction == GraphSignalDirection.Output)
                return $"\"{signal.name}\" is one of this graph's Output Signals: this graph sends it, and other graphs react to it. Make it an Input Signal to react to it here.";
            if (!own && direction == GraphSignalDirection.Input)
                return $"\"{signal.name}\" is an Input Signal of another graph: other graphs send it, only that graph reacts to it. Send it from here instead.";
            return null;
        }

        /// <summary>True if <paramref name="user"/> should send (rather than react to) this graph Signal; null if either is fine.</summary>
        public static bool? ShouldSend(SchematicGraph user, SignalData signal)
        {
            if (user == null || signal == null || signal.GraphDirection == GraphSignalDirection.Unspecified)
                return null;
            bool own = user.FindSignal(signal) != null;
            return own ? signal.GraphDirection == GraphSignalDirection.Output : signal.GraphDirection == GraphSignalDirection.Input;
        }
    }

    /// <summary>An object's own starting value for one of a graph's Variables.</summary>
    [Serializable]
    public class GraphVariableOverride
    {
        public SchematicGraph Graph;
        public string Id;
        public SchematicValue Value = new();
    }
}
