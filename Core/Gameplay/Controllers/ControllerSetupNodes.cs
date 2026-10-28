// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using Remedy.Schematics;
using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Setting up players and AIs from graphs. Players are set up in a Singleton Graph: On Device Connected → Create Player
// Controller → spawn their character → Possess. Using On Device Connected is what separates the players.

[SchematicDoc(@"{""name"": ""On Device Connected"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Controllers"", ""description"": ""In a Singleton Graph: starts a flow for every device there is when the game starts, and for each one plugged in later (a keyboard and mouse count as one). Having this node is what makes a game set up its own players: every device stops being the default player's, and objects only get input from the player who possesses them."", ""outputs"": [{""name"": ""Device Id"", ""type"": ""int""}, {""name"": ""Kind"", ""type"": ""string""}, {""name"": ""Device Name"", ""type"": ""string""}], ""use_for"": [""local multiplayer: a player per gamepad"", ""drop-in co-op"", ""a join screen""], ""notes"": ""Starts a flow. Kind is Gamepad, Keyboard, Mouse, Joystick, Touchscreen, Pen or Other. Wire Device Id into Create Player Controller (or Add Device, to give it to a player who's already there).""}")]
[Serializable]
[NodeColor(NodeColors.Event)]
[Node(Name = "On Device Connected", Path = "Events/Controllers"), Tags("Runtime")]
public class OnDeviceConnected : SchematicEventNode
{
    [Tooltip("The device's number: pass it to Create Player Controller or Add Device to give it to a player.")]
    [Output("Device Id")] public int DeviceId;
    [Tooltip("What sort of device it is (a gamepad, a keyboard and mouse, a joystick...).")]
    [Output("Kind")] public string Kind;
    [Tooltip("The device's own name, as the system reports it (for showing in a menu).")]
    [Output("Device Name")] public string DeviceName;
}

[SchematicDoc(@"{""name"": ""On Device Disconnected"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Controllers"", ""description"": ""In a Singleton Graph that sets up its players: starts a flow when a device is unplugged or loses its connection, with the player who held it (null if nobody). A device that comes back stays its player's."", ""outputs"": [{""name"": ""Device Id"", ""type"": ""int""}, {""name"": ""Kind"", ""type"": ""string""}, {""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""pausing when a player's gamepad dies"", ""dropping a player out""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run.""}")]
[Serializable]
[NodeColor(NodeColors.Event)]
[Node(Name = "On Device Disconnected", Path = "Events/Controllers"), Tags("Runtime")]
public class OnDeviceDisconnected : SchematicEventNode
{
    [Tooltip("The number of the device that went away.")]
    [Output("Device Id")] public int DeviceId;
    [Tooltip("What sort of device it was.")]
    [Output("Kind")] public string Kind;
    [Tooltip("The player who held it (empty if nobody did): pause their game or show a reconnect message.")]
    [Output("Controller")] public Controller Controller;
}

[SchematicDoc(@"{""name"": ""Create Player Controller"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Sets up a new player holding a device (from On Device Connected; a keyboard brings the mouse along). The player's devices give input only to what they possess: spawn them a character and Possess it. The first one also turns the default player off."", ""inputs"": [{""name"": ""Device Id"", ""type"": ""int""}], ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Index"", ""type"": ""int""}], ""use_for"": [""a player per gamepad"", ""joining a game""], ""notes"": ""Runs when flow reaches it. Index is the player's number from 0 (Player 1 is 0). A device that already belongs to a player is moved to the new one.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Create Player Controller", Path = "Controllers"), Tags("Runtime", "Object")]
public class CreatePlayerControllerNode : SchematicActionNode
{
    [Tooltip("The device the new player holds (the Device Id from On Device Connected).")]
    [Input("Device Id")] public int DeviceId = -1;
    [Tooltip("The new player: Possess a character with it so the device controls it.")]
    [Output("Controller")] public Controller Controller;
    [Output("Index")] public int Index;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var device = HasInputConnections("Device Id") ? InputSystem.GetDeviceById(GetInputValue("Device Id", DeviceId)) : null;
        var player = PlayerController.Create(device);
        SetOutputValue("Controller", player);
        SetOutputValue("Index", player.Index);
    }
}

[SchematicDoc(@"{""name"": ""Add Device"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Gives a device to a player who's already set up (taking it from whoever held it): a second gamepad for the same player, or a device that was plugged in again."", ""inputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Device Id"", ""type"": ""int""}], ""notes"": ""Runs when flow reaches it.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Add Device", Path = "Controllers"), Tags("Runtime", "Object")]
public class AddDeviceNode : SchematicActionNode
{
    [Tooltip("The player to give the device to.")]
    [Input("Controller")] public Controller Controller;
    [Tooltip("The device to give them (from On Device Connected).")]
    [Input("Device Id")] public int DeviceId = -1;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var player = GetInputValue<Controller>("Controller", null) as PlayerController;
        var device = InputSystem.GetDeviceById(GetInputValue("Device Id", DeviceId));
        if (player == null || device == null)
        {
            Debug.LogWarning($"[{Name}] Needs a Player Controller and a device that's there.", instance);
            return;
        }
        player.AddDevice(device);
    }
}

[SchematicDoc(@"{""name"": ""Get Player Controller"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Controllers"", ""description"": ""A player by number (Player 1 is 0), and how many players there are. In a game that doesn't set up its players, index 0 is the default player."", ""inputs"": [{""name"": ""Index"", ""type"": ""int""}], ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Count"", ""type"": ""int""}], ""use_for"": [""finding Player 2's character"", ""for each player""], ""notes"": ""A value node: no flow arrows. Controller is null when there's no such player.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Get Player Controller", Path = "Controllers"), Tags("Object")]
public class GetPlayerControllerNode : SchematicGraphNode
{
    [Input("Index", Editable = true)] public int Index;
    [Tooltip("The player with that number (empty if there's no such player yet).")]
    [Output("Controller")] public Controller Controller;
    [Output("Count")] public int Count;

    public override object OnRequestValue(Port port)
    {
        var players = PlayerController.Players;
        bool separated = Possession.Separated;
        if (port.Name == "Count")
            return separated ? players.Count : Possession.Default != null ? 1 : 0;
        int index = GetInputValue("Index", Index);
        if (!separated)
            return index == 0 ? Possession.Default : null;
        return index >= 0 && index < players.Count ? players[index] : null;
    }
}

[SchematicDoc(@"{""name"": ""Set Input Map"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Switches to another Input Map (from the Global tab's Input System): for one player if a Controller is wired (a player in a menu while the others play), otherwise for everyone. A player given their own map keeps it until it's set to empty."", ""inputs"": [{""name"": ""Controller"", ""type"": ""Controller""}, {""name"": ""Map Name"", ""type"": ""string""}], ""use_for"": [""menu controls vs gameplay controls"", ""driving controls while in a vehicle""], ""notes"": ""Runs when flow reaches it. For one player, an empty Map Name puts them back on everyone's map.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Set Input Map", Path = "Controllers"), Tags("Object")]
public class SetInputMapNode : SchematicActionNode
{
    [Tooltip("The player to switch (from Create or Get Player Controller). Leave it unwired to switch everyone.")]
    [Input("Controller")] public Controller Controller;
    [Tooltip("The Input Map to switch to, by its name in the Input System (Global tab). Empty goes back to the default.")]
    [Input("Map Name", Editable = true)] public string MapName;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        string map = GetInputValue("Map Name", MapName);
        var player = GetInputValue<Controller>("Controller", null) as PlayerController;
        if (player != null && !player.IsDefault)
            player.SetInputMap(map);
        else if (!string.IsNullOrEmpty(map))
            RemedyInput.Instance.SetInputMap(map);
    }
}

[SchematicDoc(@"{""name"": ""Create AI Controller"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Makes an AI that controls objects through their input, like a player: Possess something with it. It runs AI Graph if one is given (Send Input presses its buttons), and learns from Teacher if one is given: it watches what the teacher's objects go through and how the teacher controls them, and (with no AI Graph) controls objects of the same kind that way itself."", ""inputs"": [{""name"": ""AI Graph"", ""type"": ""AIControllerGraph""}, {""name"": ""Teacher"", ""type"": ""Controller""}, {""name"": ""Skill"", ""type"": ""ControllerSkillData""}], ""outputs"": [{""name"": ""Controller"", ""type"": ""Controller""}], ""use_for"": [""bots that drive cars and characters with the same controls as players"", ""bots that learn to play like you"", ""filling empty player slots""], ""notes"": ""Runs when flow reaches it. Learning needs Remedy ManifoldNet in the project. Skill (a Learned Controls asset) is what it learned before: it starts out knowing it, and in the editor what it learns is saved back into it when Play Mode ends. Without one, what it learns lasts while the game runs (or use Save AI Skill with a file name).""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Create AI Controller", Path = "Controllers"), Tags("Object")]
public class CreateAIControllerNode : SchematicActionNode
{
    [Tooltip("The AI Controller Graph that decides what it does (its Send Input nodes press its buttons). Optional if it learns from a Teacher or plays a Skill.")]
    [Input("AI Graph", Editable = true)] public AIControllerGraph AIGraph;
    [Tooltip("A player (or another AI) to learn from by watching: what their objects go through and what they press.")]
    [Input("Teacher")] public Controller Teacher;
    [Tooltip("Something learned before (a Learned Controls asset) to start with.")]
    [Input("Skill", Editable = true)] public ControllerSkillData Skill;
    [Tooltip("The new AI: Possess an object with it to have it controlled.")]
    [Output("Controller")] public Controller Controller;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var controller = AIController.Create(GetInputValue("AI Graph", AIGraph), GetInputValue<Controller>("Teacher", null), GetInputValue("Skill", Skill));
        SetOutputValue("Controller", controller);
    }
}

[SchematicDoc(@"{""name"": ""Set AI Learning"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Changes how an AI Controller learns: who it learns from (Teacher; unwired keeps its teacher, and Stop Learning lets them go), and whether it controls what it possesses with what it has learned (Use Learned)."", ""inputs"": [{""name"": ""AI"", ""type"": ""Controller""}, {""name"": ""Teacher"", ""type"": ""Controller""}, {""name"": ""Stop Learning"", ""type"": ""bool""}, {""name"": ""Use Learned"", ""type"": ""bool""}], ""use_for"": [""training a bot by playing, then letting it play"", ""an AI graph handing control to what was learned""], ""notes"": ""Runs when flow reaches it. AI unwired: the AI Controller running this graph.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Set AI Learning", Path = "Controllers"), Tags("Object")]
public class SetAILearningNode : SchematicActionNode
{
    [Tooltip("The AI Controller to change.")]
    [Input("AI")] public Controller AI;
    [Tooltip("A new player (or AI) to learn from. Unwired: keeps the one it has.")]
    [Input("Teacher")] public Controller Teacher;
    [Tooltip("True: stops learning from its teacher (it keeps what it's learned).")]
    [Input("Stop Learning", Editable = true)] public bool StopLearning;
    [Tooltip("True: it controls what it possesses with what it has learned. False: only its AI Graph (if any) does.")]
    [Input("Use Learned", Editable = true)] public bool UseLearned = true;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var ai = GetInputValue<Controller>("AI", null) as AIController;
        if (ai == null && instance != null)
            ai = instance.GetComponentInParent<AIController>();
        if (ai == null)
        {
            Debug.LogWarning($"[{Name}] No AI Controller: wire one into AI, or use this in an AI Controller Graph.", instance);
            return;
        }
        bool act = GetInputValue("Use Learned", UseLearned);
        var teacher = GetInputValue<Controller>("Teacher", null);
        if (GetInputValue("Stop Learning", StopLearning))
            ai.StopLearning();
        else if (teacher != null)
            ai.LearnFrom(teacher, act);
        ai.UseLearnedControls = act;
    }
}

[SchematicDoc(@"{""name"": ""Save AI Skill"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Keeps what an AI Controller has learned: into a file in the player's save folder when File Name is given (works in builds: a bot trained by the player stays trained next time), otherwise into its Learned Controls asset (in the editor, saved to disk)."", ""inputs"": [{""name"": ""AI"", ""type"": ""Controller""}, {""name"": ""File Name"", ""type"": ""string""}], ""outputs"": [{""name"": ""Saved"", ""type"": ""bool""}], ""use_for"": [""keeping a bot the player trained"", ""checkpoints while training""], ""notes"": ""Runs when flow reaches it. AI unwired: the AI Controller running this graph.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Save AI Skill", Path = "Controllers"), Tags("Object")]
public class SaveAISkillNode : SchematicActionNode
{
    [Tooltip("The AI Controller whose learning to save.")]
    [Input("AI")] public Controller AI;
    [Tooltip("Saves into this file in the player's save folder (works in builds). Empty: saves into its Learned Controls asset instead (editor).")]
    [Input("File Name", Editable = true)] public string FileName;
    [Tooltip("True if it saved.")]
    [Output("Saved")] public bool Saved;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var ai = AINodes.Resolve(GetInputValue<Controller>("AI", null), instance);
        SetOutputValue("Saved", ai != null && ai.SaveSkill(GetInputValue("File Name", FileName)) != null);
    }
}

[SchematicDoc(@"{""name"": ""Load AI Skill"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Controllers"", ""description"": ""Gives an AI Controller something learned before: a Learned Controls asset, or a file Save AI Skill wrote. With no AI Graph, it plays with it straight away."", ""inputs"": [{""name"": ""AI"", ""type"": ""Controller""}, {""name"": ""Skill"", ""type"": ""ControllerSkillData""}, {""name"": ""File Name"", ""type"": ""string""}], ""outputs"": [{""name"": ""Loaded"", ""type"": ""bool""}], ""use_for"": [""bringing back a bot the player trained"", ""picking a difficulty (one asset per skill level)""], ""notes"": ""Runs when flow reaches it. Only objects of the kinds it learned on can be controlled with it, and only while the Input Maps' inputs are the same.""}")]
[Serializable]
[NodeColor(NodeColors.Controller)]
[Node(Name = "Load AI Skill", Path = "Controllers"), Tags("Object")]
public class LoadAISkillNode : SchematicActionNode
{
    [Tooltip("The AI Controller to give the skill to.")]
    [Input("AI")] public Controller AI;
    [Tooltip("A Learned Controls asset to load.")]
    [Input("Skill", Editable = true)] public ControllerSkillData Skill;
    [Tooltip("A file written by Save AI Skill to load instead (from the player's save folder).")]
    [Input("File Name", Editable = true)] public string FileName;
    [Tooltip("True if something was loaded.")]
    [Output("Loaded")] public bool Loaded;

    protected override void OnTrigger(GameObject instance, bool awaiting = false)
    {
        var ai = AINodes.Resolve(GetInputValue<Controller>("AI", null), instance);
        SetOutputValue("Loaded", ai != null && ai.LoadSkill(GetInputValue("Skill", Skill), GetInputValue("File Name", FileName)));
    }
}

internal static class AINodes
{
    /// <summary>The AI Controller wired in, else the one running the graph (with a warning if there's neither).</summary>
    public static AIController Resolve(Controller wired, GameObject instance)
    {
        var ai = wired as AIController;
        if (ai == null && instance != null)
            ai = instance.GetComponentInParent<AIController>();
        if (ai == null)
            Debug.LogWarning("No AI Controller: wire one into AI, or use this in an AI Controller Graph.", instance);
        return ai;
    }
}
