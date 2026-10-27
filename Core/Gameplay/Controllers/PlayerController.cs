using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

/// <summary>
/// A player: the devices they hold (a gamepad, or a keyboard and mouse) and the objects they possess. Their devices'
/// input reaches only those objects, so players on one screen each drive their own character.
/// <para>A game that doesn't set up players gets one default Player Controller that every device and every object
/// belongs to. A game sets players up from a Singleton Graph: On Device Connected → Create Player Controller → spawn a
/// character → Possess. From then on (see <see cref="Possession"/>) the default is off and each object is
/// possessed by hand.</para>
/// Each player reads their own copy of the Input Map, limited to their devices, so two gamepads pushing the same stick
/// at once never get mixed up.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class PlayerController : Controller
{
    private static readonly List<PlayerController> _players = new();
    private static PlayerController _default;
    private static Transform _root;

    private readonly List<InputDevice> _devices = new();
    private InputReader _reader;
    // This player's own copy of the Input Map's actions, limited to their devices.
    private UnityEngine.InputSystem.InputActionMap _map;
    // Their own Input Map, or null to use the game's (Remedy Input's current map).
    private string _mapName;

    public override bool IsPlayer => true;

    /// <summary>The default controller (everything answers to it until the game sets up its players).</summary>
    public bool IsDefault => _default == this;

    public override string DisplayName => IsDefault ? "Player" : $"Player {Index + 1}";

    public override string NetworkId => IsDefault ? "d" : "p" + Index;

    /// <summary>The devices this player holds.</summary>
    public IReadOnlyList<InputDevice> Devices => _devices;

    /// <summary>The Input Map this player uses (theirs if they've been given one, else the game's).</summary>
    public string InputMap => _mapName ?? (RemedyInput.Instance != null ? RemedyInput.Instance.CurrentMapName : null);

    /// <summary>The players the game has set up, in the order they joined (empty while there's only the default).</summary>
    public static IReadOnlyList<PlayerController> Players => _players;

    /// <summary>Raised when the game sets up a new player.</summary>
    public static event Action<PlayerController> PlayerAdded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _players.Clear();
        _default = null;
        _root = null;
    }

    /// <summary>Where controllers live: one object kept across scenes.</summary>
    internal static Transform Root
    {
        get
        {
            if (_root == null)
            {
                var root = new GameObject("[Controllers]");
                DontDestroyOnLoad(root);
                _root = root.transform;
            }
            return _root;
        }
    }

    /// <summary>The default controller, made if it doesn't exist yet.</summary>
    public static PlayerController EnsureDefault()
    {
        if (_default != null)
            return _default;
        var gameObject = new GameObject("Player (default)");
        gameObject.transform.SetParent(Root, false);
        _default = gameObject.AddComponent<PlayerController>();
        _default.Index = 0;
        Possession.Register(_default);
        Possession.Default = _default;
        return _default;
    }

    /// <summary>
    /// Sets up a new player, holding a device if one is given (a keyboard brings the mouse with it, and the other way
    /// round). Setting up a player separates the game's players: the default controller turns off.
    /// </summary>
    public static PlayerController Create(InputDevice device = null)
    {
        Possession.Separate();
        var gameObject = new GameObject($"Player {_players.Count + 1}");
        gameObject.transform.SetParent(Root, false);
        var player = gameObject.AddComponent<PlayerController>();
        player.Index = _players.Count;
        _players.Add(player);
        Possession.Register(player);
        if (device != null)
            player.AddDevice(device);
        else
            player.Rebuild();
        PlayerAdded?.Invoke(player);
        return player;
    }

    /// <summary>The player holding a device, or null.</summary>
    public static PlayerController OfDevice(InputDevice device)
    {
        if (device == null)
            return null;
        foreach (var player in _players)
            if (player != null && player._devices.Contains(device))
                return player;
        return null;
    }

    /// <summary>
    /// Gives this player a device (taking it from whoever held it). A keyboard brings the mice nobody holds with it,
    /// and a mouse the keyboards, unless <paramref name="pairKeyboardAndMouse"/> is off.
    /// </summary>
    public void AddDevice(InputDevice device, bool pairKeyboardAndMouse = true)
    {
        if (device == null || IsDefault)
            return;
        var holder = OfDevice(device);
        if (holder != null && holder != this)
            holder.RemoveDevice(device);
        if (!_devices.Contains(device))
            _devices.Add(device);

        if (pairKeyboardAndMouse && (device is Keyboard || device is Mouse))
            foreach (var other in InputSystem.devices)
                if (other != device && (device is Keyboard ? other is Mouse : other is Keyboard)
                    && OfDevice(other) == null && !_devices.Contains(other))
                    _devices.Add(other);
        Rebuild();
    }

    /// <summary>Takes a device away from this player.</summary>
    public void RemoveDevice(InputDevice device)
    {
        if (_devices.Remove(device))
            Rebuild();
    }

    /// <summary>
    /// Gives this player their own Input Map (a menu map while others play, say). Null or empty: back to the game's
    /// map. Set Input Map on Remedy Input switches every player that hasn't got their own.
    /// </summary>
    public void SetInputMap(string mapName)
    {
        _mapName = string.IsNullOrEmpty(mapName) ? null : mapName;
        Rebuild();
    }

    /// <summary>Every player reads the game's Input Map again (it changed, or the players were just separated).</summary>
    internal static void RebuildAll()
    {
        foreach (var player in _players)
            if (player != null)
                player.Rebuild();
    }

    /// <summary>Held inputs are sent again every frame.</summary>
    internal static void UpdateAll()
    {
        foreach (var player in _players)
            if (player != null && player._reader != null)
                player._reader.Update();
    }

    /// <summary>Builds this player's own copy of their Input Map, limited to their devices, and starts reading it.</summary>
    internal void Rebuild()
    {
        // The default reads Remedy Input's own actions (every device), not a copy.
        if (IsDefault)
            return;
        _reader ??= new InputReader(this);
        _reader.Unbind(disable: false);
        if (_map != null)
        {
            _map.Disable();
            _map = null;
        }

        var input = RemedyInput.Instance;
        var definition = input != null ? input.FindMap(InputMap) : null;
        if (definition == null || definition.Inputs == null || _devices.Count == 0)
            return;

        var map = new UnityEngine.InputSystem.InputActionMap(DisplayName);
        var used = new HashSet<string>();
        var pairs = new List<(InputAction action, RemedyInput.InputActionEvent input)>();
        foreach (var entry in definition.Inputs)
        {
            var source = entry?.Input;
            if (source == null)
                continue;
            // Action names must be unique within a map.
            string name = string.IsNullOrWhiteSpace(entry.Name) ? "Input" : entry.Name;
            string unique = name;
            for (int n = 2; !used.Add(unique); n++)
                unique = $"{name} {n}";

            var action = map.AddAction(unique, source.type, null, source.interactions, source.processors, null, source.expectedControlType);
            foreach (var saved in RemedyInput.InputActionEvent.SavedBindings(source))
            {
                var binding = saved;
                binding.action = action.name;
                binding.id = Guid.NewGuid();
                action.AddBinding(binding);
            }
            pairs.Add((action, entry));
        }

        // Only this player's devices are read.
        map.devices = new ReadOnlyArray<InputDevice>(_devices.ToArray());
        foreach (var (action, entry) in pairs)
            _reader.Bind(action, entry);
        map.Enable();
        _map = map;
    }

    protected override void OnDestroy()
    {
        _reader?.Unbind(disable: false);
        if (_map != null)
            _map.Disable();
        _map = null;
        _players.Remove(this);
        if (_default == this)
            _default = null;
        base.OnDestroy();
    }
}
