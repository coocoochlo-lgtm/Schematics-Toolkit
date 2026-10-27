using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>A transparent value pass-through used to organize long graph wires.</summary>
    [Serializable]
    [Node(Name = "Redirect", Path = "Utilities")]
    [Tags("Schematic Redirect Internal")]
    public sealed class SchematicRedirectNode : SchematicGraphNode, IDynamicPorts
    {
        public const string InputPort = "In";
        public const string OutputPort = "Out";
        public const string SearchOnlyTag = "Schematic Redirect Internal";

        [SerializeField] private SerializableType _valueType = typeof(float);

        public Type ValueType
        {
            get => _valueType.Type ?? typeof(float);
            set => _valueType = value ?? typeof(float);
        }

        public void SetValueType(Type type)
        {
            ValueType = type;
            SyncPorts();
        }

        public bool SyncPorts()
        {
            var type = ValueType;
            bool changed = EnsureDataPort(InputPort, type, PortDirection.Input);
            changed |= EnsureDataPort(OutputPort, type, PortDirection.Output, PortCapacity.Multiple);
            var input = GetPort(InputPort);
            var output = GetPort(OutputPort);
            if (input != null) input.Capacity = PortCapacity.Single;
            if (output != null) output.Capacity = PortCapacity.Multiple;
            return changed;
        }

        public override object OnRequestValue(Port port)
            => port != null && port.Direction == PortDirection.Output && port.Name == OutputPort
                ? GetInputValue<object>(InputPort)
                : null;
    }

    /// <summary>A pass-through for flow wires; running it immediately continues to its children.</summary>
    [Serializable]
    [Node(Name = "Flow Redirect", Path = "Utilities")]
    [Tags(SchematicRedirectNode.SearchOnlyTag)]
    public sealed class SchematicFlowRedirectNode : SchematicActionNode
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false) { }
    }
}
