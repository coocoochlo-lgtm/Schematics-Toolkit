using Remedy.Framework;
using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

/// <summary>
/// Sends its Signal to the object running this graph, where Schematic Behaviors and other graphs hear it.
/// Has an input pin per Signal parameter (added by the editor when the Signal is dropped in).
/// </summary>
[Serializable]
[Node(Name = "Send a Signal", Path = "ScriptableEvents/Events"), Tags("Object")]
public class SendSignalNode : SchematicActionNode
{
    [Editable]
    public SignalData Signal;

    [NonSerialized] private Union[] _arguments;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        if (Signal == null)
            return;

        // Sent to the object running this graph, by the same UID Schematic Behaviors and ExecutionContext.Pick use.
        // With no spawned object (e.g. a Singleton Graph) the UID is 0, which reaches every instance.
        int uid = instance != null && SignalData.UIDResolver != null ? SignalData.UIDResolver(instance) : 0;

        var parameters = Signal.Parameters.Contents;
        if (parameters.Count == 0)
        {
            Signal.InvokeFromGraph(uid);
            return;
        }

        if (_arguments == null || _arguments.Length != parameters.Count)
            _arguments = new Union[parameters.Count];
        for (int i = 0; i < parameters.Count; i++)
        {
            string port = parameters[i].Name;
            // Nodes made before parameters had their own pins used a single "Value" pin.
            if (GetPort(port) == null && i == 0 && GetPort("Value") != null)
                port = "Value";
            _arguments[i] = GetPort(port) != null ? GetInputValue<Union>(port, Union.Null) : Union.Null;
        }
        Signal.InvokeFromGraph(uid, _arguments);
    }
}

/// <summary>
/// Fires when its Signal is sent to the object running this graph, from a Schematic Behavior or another graph.
/// Subscribed by the Schematic Object when it spawns, under its UID.
/// </summary>
[Serializable]
[Node(Name = "On Signal Received"), Tags("Object")]
public class OnSignalReceivedNode : SchematicEventNode
{
    [Editable]
    public SignalData Signal;
}
