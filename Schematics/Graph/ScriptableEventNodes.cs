using Remedy.Framework;
using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

/// <summary>
/// Sends its Signal to the object running this graph, where Schematic Behaviors and other graphs hear it.
/// Has an input pin per Signal parameter (added by the editor when the Signal is dropped in), and a "To" pin to send it
/// to another object instead.
/// </summary>
[SchematicDoc(@"{""name"": ""Send a Signal"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""ScriptableEvents/Events"", ""description"": ""Sends its Signal to the object running this graph, where Schematic Behaviors and other graphs hear it. Has an input pin per Signal parameter (added by the editor when the Signal is dropped in)."", ""use_for"": [""tell a Behavior to do something (its Input Signals)"", ""notify other graphs"", ""pass values like damage amounts or targets"", ""send a Signal to another object (wire it into To)""], ""notes"": ""Wire an object into To to send it there instead of to this object. Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Made by dragging a Signal from the IO Dock. An action: wire flow into it; its inputs are the Signal's parameters.""}")]
[Serializable]
[Node(Name = "Send a Signal", Path = "ScriptableEvents/Events"), Tags("Object")]
public class SendSignalNode : SchematicActionNode
{
    /// <summary>The optional input naming the object to send to (empty: the object running this graph).</summary>
    public const string ToPort = "To";

    [Editable]
    public SignalData Signal;

    [NonSerialized] private Union[] _arguments;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        if (Signal == null)
            return;

        // "To" wired: that object (any Schematic Object, or one of its children). A wired but empty target sends nothing,
        // rather than reaching everyone.
        var target = instance;
        if (GetPort(ToPort) != null && HasInputConnections(ToPort))
        {
            target = GetInputValue<GameObject>(ToPort, null);
            if (target == null)
                return;
        }

        // Sent to the object by the same UID Schematic Behaviors and ExecutionContext.Pick use.
        // With no spawned object (e.g. a Singleton Graph) the UID is 0, which reaches every instance.
        int uid = target != null && SignalData.UIDResolver != null ? SignalData.UIDResolver(target) : 0;

        // Set to run on Everyone in a networked graph, every peer runs this node itself: a networked Signal then stays
        // local, or each peer would also pass it on and it would fire twice.
        using var local = NetworkMode == NodeNetworkMode.Everyone && Graph is SchematicGraph { Networked: true }
            ? SignalNetwork.LocalOnly() : default;

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
[SchematicDoc(@"{""name"": ""On Signal Received"", ""kind"": ""node"", ""flow"": ""event"", ""description"": ""Fires when its Signal is sent to the object running this graph, from a Schematic Behavior or another graph. Subscribed by the Schematic Object when it spawns, under its UID."", ""use_for"": [""react when a Behavior reports something (its Output Signals, e.g. OnDied, OnPickedUp)"", ""react to Signals sent by other graphs""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Made by dragging a Signal from the IO Dock. An event: starts a flow; its outputs are the Signal's parameters.""}")]
[Serializable]
[Node(Name = "On Signal Received"), Tags("Object")]
public class OnSignalReceivedNode : SchematicEventNode
{
    [Editable]
    public SignalData Signal;
}
