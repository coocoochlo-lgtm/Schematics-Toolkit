// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    // Controller Possession in graphs: who controls which object, and input given by a controller (an AI pressing
    // buttons the same way a player does). See Possession for how input is routed.

    [SchematicDoc(@"{""name"": ""On Possessed"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Controllers"", ""description"": ""Starts a flow when a controller (a player or an AI) takes control of this object. Outputs that controller."", ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""pointing a player's camera at their new character"", ""showing a HUD when a vehicle is taken over"", ""switching an enemy's AI off while a player controls it""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Possessed", Path = "Events/Controllers"), Tags("Object")]
    public class OnPossessed : SchematicEventNode
    {
        [Tooltip("The controller (player or AI) that took control of this object.")]
        [Output("Controller")] public Controller Controller;
    }

    [SchematicDoc(@"{""name"": ""On Unpossessed"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Controllers"", ""description"": ""Starts a flow when the controller that had this object lets it go (Unpossess, or it possessed something else's place). Outputs that controller."", ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""parking a vehicle when its driver leaves"", ""handing an object back to its own AI""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Not sent when the object despawns.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Unpossessed", Path = "Events/Controllers"), Tags("Object")]
    public class OnUnpossessed : SchematicEventNode
    {
        [Tooltip("The controller that let it go.")]
        [Output("Controller")] public Controller Controller;
    }

    [SchematicDoc(@"{""name"": ""Possess"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Gives an object to a controller: from now on that controller's input reaches it (its devices, for a player; its graph or what it learned, for an AI). Takes it from whoever had it. With Target empty, the object running this graph."", ""inputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Keep Others"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Possessed"", ""type"": ""bool""}], ""use_for"": [""giving each player their character after they join"", ""getting into a vehicle"", ""taking over another unit""], ""notes"": ""Runs when flow reaches it. Keep Others off (the default): the controller first lets go of what it had. On: it controls both (a rider and their horse).""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Possess", Path = "Controllers"), Tags("Object")]
    public class PossessNode : SchematicActionNode
    {
        [Tooltip("The controller (player or AI) to give the object to.")]
        [Input("Controller")] public Controller Controller;
        [Input("Target")] public GameObject Target;
        [Tooltip("Let the controller keep what it already controls as well. Off: it lets go of everything else.")]
        [Input("Keep Others", Editable = true)] public bool KeepOthers;
        [Tooltip("True if it worked.")]
        [Output("Possessed")] public bool Possessed;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var controller = GetInputValue<Controller>("Controller", null);
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            bool done = false;
            if (controller != null && target != null)
            {
                if (!GetInputValue("Keep Others", KeepOthers))
                {
                    var schematic = Possession.SchematicOf(target);
                    for (int i = controller.Possessed.Count - 1; i >= 0; i--)
                        if (i < controller.Possessed.Count && controller.Possessed[i] != schematic)
                            Possession.Unpossess(controller.Possessed[i]);
                }
                done = controller.Possess(target);
            }
            else if (controller == null)
                Debug.LogWarning($"[{Name}] No Controller is connected, so nothing was possessed.", instance);
            SetOutputValue("Possessed", done);
        }
    }

    [SchematicDoc(@"{""name"": ""Unpossess"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Lets an object go: no controller gives it input any more (until something possesses it; in a game that hasn't set up its players, it answers to everyone's input again). With Target empty, the object running this graph."", ""inputs"": [{""name"": ""Target"", ""type"": ""GameObject""}], ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""getting out of a vehicle"", ""a character that's been knocked out""], ""notes"": ""Runs when flow reaches it. Controller is who had it (null if nobody).""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Unpossess", Path = "Controllers"), Tags("Object")]
    public class UnpossessNode : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Tooltip("The controller that had it (empty if nobody did).")]
        [Output("Controller")] public Controller Controller;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            var schematic = Possession.SchematicOf(target);
            var previous = Possession.PossessorOf(schematic);
            Possession.Unpossess(schematic);
            SetOutputValue("Controller", previous);
        }
    }

    [SchematicDoc(@"{""name"": ""Get Controller"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Controllers"", ""description"": ""The controller an object answers to: whoever possesses it, or (in a game that hasn't set up its players) the default controller. Null if nobody controls it. With Target empty, the object running this graph."", ""inputs"": [{""name"": ""Target"", ""type"": ""GameObject""}], ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Is Player"", ""type"": ""bool""}, {""name"": ""Is Possessed"", ""type"": ""bool""}], ""use_for"": [""is a player driving this?"", ""which player picked this up""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Get Controller", Path = "Controllers"), Tags("Object")]
    public class GetControllerNode : SchematicGraphNode
    {
        [Input("Target")] public GameObject Target;
        [Tooltip("Whoever controls the object (empty if nobody does).")]
        [Output("Controller")] public Controller Controller;
        [Tooltip("True if it's a player rather than an AI.")]
        [Output("Is Player")] public bool IsPlayer;
        [Tooltip("True if a controller has taken it over (rather than the default player).")]
        [Output("Is Possessed")] public bool IsPossessed;

        public override object OnRequestValue(Port port)
        {
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = RunningInstance;
            var controller = Possession.ControllerOf(target);
            switch (port.Name)
            {
                case "Is Player": return controller != null && controller.IsPlayer;
                case "Is Possessed": return Possession.PossessorOf(Possession.SchematicOf(target)) != null;
                default: return controller;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Input Controller"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Controllers"", ""description"": ""The controller whose input started this flow: in an On Signal Received for an input, which player (or AI) pressed it. Null outside input. Singletons hear every player's input, so this is how a pause menu knows who paused."", ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Index"", ""type"": ""int""}], ""use_for"": [""who pressed Pause"", ""who pressed Join""], ""notes"": ""A value node: no flow arrows. Read it straight away in the flow the input started: after a Wait it's gone.""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Input Controller", Path = "Controllers"), Tags("Object")]
    public class InputControllerNode : SchematicGraphNode
    {
        [Tooltip("The player (or AI) whose input started this flow; empty outside input.")]
        [Output("Controller")] public Controller Controller;
        [Output("Index")] public int Index;

        public override object OnRequestValue(Port port)
        {
            var current = Possession.Current;
            return port.Name == "Index" ? (object)(current != null ? current.Index : -1) : current;
        }
    }

    [SchematicDoc(@"{""name"": ""Controller Info"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Controllers"", ""description"": ""About a controller: whether it's a player, its number (Player 1 is 0), its name, the object it mainly controls (the first it possessed) and how many it controls."", ""inputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""outputs"": [{""name"": ""Is Player"", ""type"": ""bool""}, {""name"": ""Index"", ""type"": ""int""}, {""name"": ""Name"", ""type"": ""string""}, {""name"": ""Possessed Object"", ""type"": ""GameObject""}, {""name"": ""Possessed Count"", ""type"": ""int""}], ""use_for"": [""finding a player's character"", ""labelling a split-screen view""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs. With no Controller wired: the one running this graph (an AI Controller Graph), or the one controlling this object.""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Controller Info", Path = "Controllers"), Tags("Object")]
    public class ControllerInfoNode : SchematicGraphNode
    {
        [Tooltip("The controller to ask about.")]
        [Input("Controller")] public Controller Controller;
        [Tooltip("True if it's a player rather than an AI.")]
        [Output("Is Player")] public bool IsPlayer;
        [Output("Index")] public int Index;
        [Output("Name")] public string ControllerName;
        [Tooltip("The object it mainly controls (the first it took over).")]
        [Output("Possessed Object")] public GameObject PossessedObject;
        [Tooltip("How many objects it controls.")]
        [Output("Possessed Count")] public int PossessedCount;

        public override object OnRequestValue(Port port)
        {
            var controller = ControllerNodes.Resolve(this, GetInputValue<Controller>("Controller", null), RunningInstance);
            switch (port.Name)
            {
                case "Is Player": return controller != null && controller.IsPlayer;
                case "Index": return controller != null ? controller.Index : -1;
                case "Name": return controller != null ? controller.DisplayName : "";
                case "Possessed Count": return controller != null ? controller.Possessed.Count : 0;
                default:
                    var primary = controller != null ? controller.Primary : null;
                    return primary != null ? primary.gameObject : null;
            }
        }
    }

    [SchematicDoc(@"{""name"": ""Send Input"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Gives input as a controller: sends an Input Signal (an Input Map's, e.g. Input Move) with a value to the objects that controller possesses, exactly as a press of a player's button would. How an AI Controller Graph drives its character."", ""inputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Value"", ""type"": ""object""}], ""use_for"": [""an AI walking its character (Input Move, a Vector2)"", ""an AI pressing Jump or Fire"", ""a scripted moment pressing a button for the player""], ""notes"": ""Runs when flow reaches it. Pick the Signal on the node. Value unwired: true for a button (bool), else the Signal's default. A held stick is sent every frame (On Update) and let go with Release Input. Controller unwired: the one running this graph (an AI Controller Graph), or the one controlling this object.""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Send Input", Path = "Controllers"), Tags("Object")]
    public class SendInputNode : SchematicActionNode
    {
        [Editable] public SignalData Signal;
        [Tooltip("The controller to act as (an AI Controller Graph's own, usually).")]
        [Input("Controller")] public Controller Controller;
        [Input("Value")] public object Value;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var controller = ControllerNodes.Resolve(this, GetInputValue<Controller>("Controller", null), instance);
            if (controller == null || Signal == null)
            {
                if (Signal == null)
                    Debug.LogWarning($"[{Name}] No Input Signal is picked on the node.", instance);
                return;
            }
            var parameters = Signal.Parameters.Contents;
            if (parameters.Count == 0)
            {
                controller.SendInput(Signal);
                return;
            }
            Union value;
            if (HasInputConnections("Value"))
                value = GetInputValue<Union>("Value", Union.Null);
            else
                value = parameters[0].Value.Type == Union.ValueType.Bool ? new Union(true) : Signal.DefaultArguments()[0];
            controller.SendInput(Signal, value);
        }
    }

    [SchematicDoc(@"{""name"": ""Release Input"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Lets go of an input as a controller: the objects it possesses get the Input Signal's default (false, 0, (0, 0)), as when a player lets go of a button or stick."", ""inputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""an AI stopping (Input Move back to (0, 0))"", ""an AI letting go of Fire""], ""notes"": ""Runs when flow reaches it. Pick the Signal on the node. Controller unwired: the one running this graph, or the one controlling this object.""}")]
    [Serializable]
    [NodeColor(NodeColors.Controller)]
    [Node(Name = "Release Input", Path = "Controllers"), Tags("Object")]
    public class ReleaseInputNode : SchematicActionNode
    {
        [Editable] public SignalData Signal;
        [Tooltip("The controller to act as.")]
        [Input("Controller")] public Controller Controller;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var controller = ControllerNodes.Resolve(this, GetInputValue<Controller>("Controller", null), instance);
            if (controller != null && Signal != null)
                controller.ReleaseInput(Signal);
        }
    }

    internal static class ControllerNodes
    {
        /// <summary>
        /// The controller a node means when none is wired: the one running its graph (an AI Controller Graph runs on its
        /// controller's object), else the one controlling the object running it.
        /// </summary>
        public static Controller Resolve(SchematicGraphNode node, Controller wired, GameObject instance)
        {
            if (wired != null)
                return wired;
            if (instance == null)
                return null;
            var own = instance.GetComponentInParent<Controller>();
            return own != null ? own : Possession.ControllerOf(instance);
        }
    }
}
