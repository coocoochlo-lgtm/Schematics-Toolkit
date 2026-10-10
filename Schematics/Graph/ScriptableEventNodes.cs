using Remedy.Framework;
using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

[Serializable]
[Node(Name = "Send a Signal", Path = "ScriptableEvents/Events"), Tags("Object")]
public class SendSignalNode : SchematicActionNode
{
    [Editable]
    public SignalData Signal;
    [Input]
    public Union Value;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        if (Signal == null)
            return;

        // Sent to the object running this graph, by the same UID Schematic Behaviors and ExecutionContext.Pick use.
        // With no spawned object (e.g. a Singleton Graph) the UID is 0, which reaches every instance.
        int uid = instance != null && SignalData.UIDResolver != null ? SignalData.UIDResolver(instance) : 0;

        if (Signal.Parameters.Contents.Count == 0)
            Signal.InvokeFromGraph(uid);
        else
            Signal.InvokeFromGraph(uid, GetInputValue<Union>(nameof(Value), default));
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
 
[Node(Name = "Set Variable", Path = "Schematic/Variables"), Tags("Object")]
public class SetScriptableVariable : SchematicActionNode
{
    [Editable]
    public ScriptableVariable Target;
    [Input]
    public Union Value;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var newVal = GetInputValue<Union>(nameof(Value), default);
        Target.Value = newVal;
    } 
}

[Node(Name = "Get Variable", Path = "Schematic/Variables"), Tags("Object")]
public class GetScriptableVariable : SchematicActionNode
{
    [Editable]
    public ScriptableVariable Target;
    [Output]
    public Union Value;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        SetOutputValue(nameof(Value), Target.Value);
    }
}