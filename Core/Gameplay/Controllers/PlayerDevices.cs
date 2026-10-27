using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tells Singleton Graphs about devices, so a game can set up its own players: On Device Connected fires for every
/// device there is when the game starts and for each one plugged in later, and On Device Disconnected when one goes.
/// Using On Device Connected in a Singleton is what separates the players (see <see cref="Possession"/>): without it,
/// every device belongs to the default controller and nothing here runs.
/// <para>A keyboard and mouse count as one device: with a keyboard there, mice aren't announced on their own, and
/// Create Player Controller on the keyboard brings them along.</para>
/// </summary>
public static class PlayerDevices
{
    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (_hooked)
            InputSystem.onDeviceChange -= OnDeviceChange;
        _hooked = false;
    }

    /// <summary>True if a running Singleton Graph sets the players up (it has an On Device Connected).</summary>
    public static bool GameSetsUpPlayers
    {
        get
        {
            foreach (var host in SingletonGraphSpawner.Spawned)
                if (host != null && host.SchematicGraph != null && host.SchematicGraph.GetEventArray<OnDeviceConnected>() is { Length: > 0 })
                    return true;
            return false;
        }
    }

    /// <summary>Called once input starts: separates the players and announces every device, if the game wants that.</summary>
    internal static void Start()
    {
        if (_hooked || !GameSetsUpPlayers)
            return;
        Possession.Separate();
        _hooked = true;
        InputSystem.onDeviceChange += OnDeviceChange;
        foreach (var device in InputSystem.devices)
            Announce(device);
    }

    private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        switch (change)
        {
            case InputDeviceChange.Added:
                Announce(device);
                break;
            case InputDeviceChange.Reconnected:
                // Back again: it's still its player's. Announced only if nobody holds it.
                if (PlayerController.OfDevice(device) == null)
                    Announce(device);
                break;
            case InputDeviceChange.Disconnected:
                Lost(device, PlayerController.OfDevice(device));
                break;
            case InputDeviceChange.Removed:
                var holder = PlayerController.OfDevice(device);
                if (holder != null)
                    holder.RemoveDevice(device);
                Lost(device, holder);
                break;
        }
    }

    /// <summary>Whether a device is one a player holds (not a sensor like an accelerometer).</summary>
    public static bool IsPlayerDevice(InputDevice device)
        => device != null && !(device is Sensor);

    /// <summary>What kind of device it is: Gamepad, Keyboard, Mouse, Joystick, Touchscreen, Pen, or Other.</summary>
    public static string KindOf(InputDevice device) => device switch
    {
        Gamepad => "Gamepad",
        Keyboard => "Keyboard",
        Mouse => "Mouse",
        Joystick => "Joystick",
        Touchscreen => "Touchscreen",
        Pen => "Pen",
        _ => "Other",
    };

    private static bool AnyKeyboard()
    {
        foreach (var device in InputSystem.devices)
            if (device is Keyboard)
                return true;
        return false;
    }

    private static void Announce(InputDevice device)
    {
        if (!IsPlayerDevice(device))
            return;
        // A mouse goes with the keyboard.
        if (device is Mouse && AnyKeyboard())
            return;
        Trigger<OnDeviceConnected>(new Union(device.deviceId), new Union(KindOf(device)), new Union(device.displayName ?? device.name));
    }

    private static void Lost(InputDevice device, PlayerController holder)
    {
        if (!IsPlayerDevice(device) || (device is Mouse && AnyKeyboard()))
            return;
        Trigger<OnDeviceDisconnected>(new Union(device.deviceId), new Union(KindOf(device)), new Union((Component)holder));
    }

    private static void Trigger<TEvent>(params Union[] args) where TEvent : SchematicEventNode
    {
        foreach (var host in SingletonGraphSpawner.Spawned)
            if (host != null && host.SchematicGraph != null)
                host.SchematicGraph.TriggerEvent<TEvent>(host.gameObject, args);
    }
}
