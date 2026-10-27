using Remedy.Schematics;
using UnityEngine;

/// <summary>
/// The graph an AI Controller runs: it decides what to press, and presses it with Send Input / Release Input, exactly
/// as a player would with a gamepad. It runs on the controller's own object (On Create, On Update... work as usual);
/// Controller Info gives the object it's controlling.
/// </summary>
[SchematicType("AI Controllers", Order = 25, Icon = "SchematicTab:ai-controller", NewAssetName = "New AI Controller")]
[CreateAssetMenu(fileName = "New AI Controller", menuName = "Remedy Toolkit/Schematics/AI Controller Graph")]
public sealed class AIControllerGraph : SchematicGraph
{
    public override string Title => "AI Controller: " + name;
}
