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
