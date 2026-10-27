using Remedy.Framework;
using System;
using System.Collections.Generic;

// Instance routing: Signals addressed to one spawned Schematic Object by its UID, the same ID used by
// ExecutionContext.Pick, Behavior Variables and GetSchematicBehavior. UID 0 means "every instance".
public partial class SignalData
{
    /// <summary>
    /// Maps an object (a GameObject or Component) to the UID of the spawned Schematic Object it belongs to, or 0.
    /// Set by Schematics Core, which owns the UIDs.
    /// </summary>
    public static Func<UnityEngine.Object, int> UIDResolver;

    // Listeners by the UID they listen to.
    private readonly Dictionary<int, List<SignalListener>> _listeners = new();
    // Every Signal that has listeners for a UID, so they can all be released when that object despawns.
    private static readonly Dictionary<int, List<SignalData>> _signalsByUID = new();

    // How many invokes of this Signal are running (a listener can send it again).
    private int _invokeDepth;

    // Objects that aren't Schematic Objects but take Signals sent to them (a playing Sound Cue): each has its own
    // address, below 0 so it never meets a Schematic Object's UID.
    private static readonly Dictionary<UnityEngine.GameObject, int> _addresses = new();
    private static int _nextAddress = -1;

    /// <summary>
    /// Gives an object that isn't a Schematic Object an address of its own, so Signals sent to it (Send a Signal's To)
    /// reach only it rather than everyone. Returns its address (the same each time, until <see cref="ForgetAddress"/>).
    /// </summary>
    public static int MakeAddressable(UnityEngine.GameObject gameObject)
    {
        if (gameObject == null)
            return 0;
        if (!_addresses.TryGetValue(gameObject, out int address))
            _addresses[gameObject] = address = _nextAddress--;
        return address;
    }

    /// <summary>Takes an object's own address away (and every listener on it).</summary>
    public static void ForgetAddress(UnityEngine.GameObject gameObject)
    {
        if (gameObject != null && _addresses.TryGetValue(gameObject, out int address))
        {
            _addresses.Remove(gameObject);
            ReleaseUID(address);
        }
    }

    /// <summary>
    /// The address Signals sent to an object use: its Schematic Object's UID, else its own address if it has one
    /// (<see cref="MakeAddressable"/>), else 0 (every object).
    /// </summary>
    public static int UIDOf(UnityEngine.Object target)
    {
        if (target == null)
            return 0;
        int uid = UIDResolver != null ? UIDResolver(target) : 0;
        if (uid != 0)
            return uid;
        var gameObject = target as UnityEngine.GameObject ?? (target as UnityEngine.Component)?.gameObject;
        return gameObject != null && _addresses.TryGetValue(gameObject, out int address) ? address : 0;
    }

    public void Subscribe(int uid, SignalListener listener)
    {
        if (listener == null)
            return;

        if (!_listeners.TryGetValue(uid, out var list))
        {
            list = new List<SignalListener>(2);
            _listeners.Add(uid, list);

            if (!_signalsByUID.TryGetValue(uid, out var signals))
                _signalsByUID[uid] = signals = new List<SignalData>(4);
            signals.Add(this);
        }
        list.Add(listener);
    }

    public void Unsubscribe(int uid, SignalListener listener)
    {
        if (_listeners.TryGetValue(uid, out var list))
            list.Remove(listener);
    }

    /// <summary>
    /// Drops every listener subscribed to the given UID, on every Signal. Called when that object despawns.
    /// </summary>
    public static void ReleaseUID(int uid)
    {
        if (!_signalsByUID.TryGetValue(uid, out var signals))
            return;

        foreach (var signal in signals)
            if (signal != null)
                signal._listeners.Remove(uid);
        _signalsByUID.Remove(uid);
    }

    /// <summary>
    /// Clears all instance routing. Called when play mode starts without a domain reload.
    /// </summary>
    public static void ResetInstanceRouting()
    {
        foreach (var signals in _signalsByUID.Values)
            foreach (var signal in signals)
                if (signal != null)
                    signal._listeners.Clear();
        _signalsByUID.Clear();
        _addresses.Clear();
        _nextAddress = -1;
    }

    #region Broadcasting

    /// <summary>Sends the Signal to every object listening to it, from anywhere (input, managers, tools).</summary>
    public void Broadcast() => Invoke(0);

    /// <summary>
    /// Sends the Signal to every object listening to it, with a value of any type: listeners that take another type
    /// get it converted (or their type's default if it can't be), and listeners that take <c>object</c> get it as is.
    /// </summary>
    public void Broadcast<T1>(T1 a) => Invoke(0, a);
    public void Broadcast<T1, T2>(T1 a, T2 b) => Invoke(0, a, b);
    public void Broadcast<T1, T2, T3>(T1 a, T2 b, T3 c) => Invoke(0, a, b, c);
    public void Broadcast<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d) => Invoke(0, a, b, c, d);

    /// <summary>
    /// Sends the Signal to every listener with the default value of each parameter (false, 0, Vector2.zero, null...),
    /// e.g. when an input is released.
    /// </summary>
    public void BroadcastDefault() => InvokeFromGraph(0, DefaultArguments());

    // A parameter type's default as a value of that type (0, false, Vector2.zero, Quaternion.identity...), not
    // nothing: a released stick used to send null, which listeners taking "any value" got as null instead of (0, 0).
    // Only references (an object, a string...) are sent as null.
    private static Union DefaultOf(Union.ValueType type)
    {
        if (type == Union.ValueType.Quaternion)
            return SignalArgs.ToUnion(UnityEngine.Quaternion.identity);
        if (Union.TypeLookup.TryGetValue(type, out var valueType) && valueType != null && valueType.IsValueType
            && valueType != typeof(Union) && valueType != typeof(UnityEngine.SceneManagement.Scene))
            return SignalArgs.ToUnion(Activator.CreateInstance(valueType));
        return Union.Null;
    }

    /// <summary>
    /// The default value of each parameter (false, 0, Vector2.zero, null...), as arguments: what a released input
    /// sends.
    /// </summary>
    public Union[] DefaultArguments()
    {
        int count = Math.Max(1, Parameters.Contents.Count);
        var args = new Union[count];
        for (int i = 0; i < count; i++)
            args[i] = i < Parameters.Contents.Count ? DefaultOf(Parameters[i].Type) : Union.Null;
        return args;
    }

    #endregion

    #region Sending to a group

    // Used by Controller Possession: one press of a player's button reaches the objects that player possesses, not
    // everyone, and listeners of "every object" hear it once however many objects that is.

    /// <summary>
    /// Sends the Signal to several objects at once: the listeners of each UID, and the listeners of every object (UID
    /// 0) once.
    /// </summary>
    public void InvokeTo(IReadOnlyList<int> uids, Union[] args)
    {
        args ??= Array.Empty<Union>();
        if (Remedy.Schematics.SignalNetwork.Wants(this))
        {
            // Networked: each send goes through the network as usual.
            for (int i = 0; i < uids.Count; i++)
                InvokeFromGraph(uids[i], args);
            return;
        }
        for (int i = 0; i < uids.Count; i++)
            ReportSent(uids[i]);
        _invokeDepth++;
        try
        {
            var listeners = Snapshot();
            for (int i = 0; i < uids.Count; i++)
                if (uids[i] != 0 && _listeners.TryGetValue(uids[i], out var own))
                    listeners.AddRange(own);
            if (_listeners.TryGetValue(0, out var any))
                listeners.AddRange(any);
            foreach (var l in listeners)
                l.Receive(args);
        }
        finally { _invokeDepth--; }

        if (HasComponentSubscribers)
            for (int i = 0; i < uids.Count; i++)
                if (uids[i] != 0)
                    DeliverToComponents(uids[i], args);
    }

    /// <summary>Sends the Signal to every object except the given UIDs (and to the listeners of every object).</summary>
    public void InvokeExcept(ICollection<int> excluded, Union[] args)
    {
        args ??= Array.Empty<Union>();
        if (excluded == null || excluded.Count == 0 || Remedy.Schematics.SignalNetwork.Wants(this))
        {
            InvokeFromGraph(0, args);
            return;
        }
        ReportSent(0);
        _invokeDepth++;
        try
        {
            var listeners = Snapshot();
            foreach (var entry in _listeners)
                if (entry.Key == 0 || !excluded.Contains(entry.Key))
                    listeners.AddRange(entry.Value);
            foreach (var l in listeners)
                l.Receive(args);
        }
        finally { _invokeDepth--; }

        if (_subs.Count > 0 && UIDResolver != null)
            for (int i = 0; i < _subs.Count; i++)
            {
                var sub = _subs[i];
                if (sub != null && !excluded.Contains(UIDResolver(sub.Parent)))
                    sub.Invoke(args);
            }
    }

    // The listener list for the current nesting level, emptied. Callers increment _invokeDepth first.
    private List<SignalListener> Snapshot()
    {
        int level = _invokeDepth - 1;
        while (_snapshots.Count <= level)
            _snapshots.Add(new List<SignalListener>());
        var result = _snapshots[level];
        result.Clear();
        return result;
    }

    #endregion

    #region Invoking

#if UNITY_EDITOR
    /// <summary>
    /// Editor only: a Signal was sent, to a UID (0: to everyone). The IO Dock flashes its rows. (Not in builds.)
    /// </summary>
    public static event Action<SignalData, int> EditorSent;
#endif

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void ReportSent(int uid)
    {
#if UNITY_EDITOR
        EditorSent?.Invoke(this, uid);
#endif
    }

    // The same, for an old-style invoke aimed at an object (its UID worked out only when someone's listening).
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    internal void ReportSentTo(UnityEngine.Object target)
    {
#if UNITY_EDITOR
        if (EditorSent != null)
            EditorSent(this, ReferenceEquals(target, null) ? 0 : UIDResolver?.Invoke(target) ?? 0);
#endif
    }

    // A Signal marked Networked first goes through SignalNetwork (when the networking layer is installed), which passes
    // it on to the other players and says whether this peer hears it itself (see SignalNetworkSender).

    public void Invoke(int uid)
    {
        if (Remedy.Schematics.SignalNetwork.Wants(this) && !Remedy.Schematics.SignalNetwork.Dispatch(this, uid, Array.Empty<Union>()))
            return;
        ReportSent(uid);
        ForEachListener(uid, l =>
        {
            if (l is SignalListener0 typed) typed.Action();
            else l.Receive(Array.Empty<Union>());
        });
        DeliverToComponents(uid, Array.Empty<Union>());
    }

    public void Invoke<T1>(int uid, T1 a)
    {
        if (Remedy.Schematics.SignalNetwork.Wants(this) && !Remedy.Schematics.SignalNetwork.Dispatch(this, uid, new[] { SignalArgs.ToUnion(a) }))
            return;
        ReportSent(uid);
        _invokeDepth++;
        try
        {
            Union[] args = null;
            foreach (var l in Listeners(uid))
            {
                if (l is SignalListener<T1> typed) typed.Action(a);
                else l.Receive(args ??= Pack(a));
            }
            if (HasComponentSubscribers) DeliverToComponents(uid, args ?? Pack(a));
        }
        finally { _invokeDepth--; }
    }

    public void Invoke<T1, T2>(int uid, T1 a, T2 b)
    {
        if (Remedy.Schematics.SignalNetwork.Wants(this) && !Remedy.Schematics.SignalNetwork.Dispatch(this, uid, new[] { SignalArgs.ToUnion(a), SignalArgs.ToUnion(b) }))
            return;
        ReportSent(uid);
        _invokeDepth++;
        try
        {
            Union[] args = null;
            foreach (var l in Listeners(uid))
            {
                if (l is SignalListener<T1, T2> typed) typed.Action(a, b);
                else l.Receive(args ??= Pack(a, b));
            }
            if (HasComponentSubscribers) DeliverToComponents(uid, args ?? Pack(a, b));
        }
        finally { _invokeDepth--; }
    }

    public void Invoke<T1, T2, T3>(int uid, T1 a, T2 b, T3 c)
    {
        if (Remedy.Schematics.SignalNetwork.Wants(this) && !Remedy.Schematics.SignalNetwork.Dispatch(this, uid, new[] { SignalArgs.ToUnion(a), SignalArgs.ToUnion(b), SignalArgs.ToUnion(c) }))
            return;
        ReportSent(uid);
        _invokeDepth++;
        try
        {
            Union[] args = null;
            foreach (var l in Listeners(uid))
            {
                if (l is SignalListener<T1, T2, T3> typed) typed.Action(a, b, c);
                else l.Receive(args ??= Pack(a, b, c));
            }
            if (HasComponentSubscribers) DeliverToComponents(uid, args ?? Pack(a, b, c));
        }
        finally { _invokeDepth--; }
    }

    public void Invoke<T1, T2, T3, T4>(int uid, T1 a, T2 b, T3 c, T4 d)
    {
        if (Remedy.Schematics.SignalNetwork.Wants(this) && !Remedy.Schematics.SignalNetwork.Dispatch(this, uid, new[] { SignalArgs.ToUnion(a), SignalArgs.ToUnion(b), SignalArgs.ToUnion(c), SignalArgs.ToUnion(d) }))
            return;
        ReportSent(uid);
        _invokeDepth++;
        try
        {
            Union[] args = null;
            foreach (var l in Listeners(uid))
            {
                if (l is SignalListener<T1, T2, T3, T4> typed) typed.Action(a, b, c, d);
                else l.Receive(args ??= Pack(a, b, c, d));
            }
            if (HasComponentSubscribers) DeliverToComponents(uid, args ?? Pack(a, b, c, d));
        }
        finally { _invokeDepth--; }
    }

    /// <summary>
    /// Sends from a Schematic Graph, with Union arguments. UID 0 reaches every instance.
    /// </summary>
    public void InvokeFromGraph(int uid, params Union[] args)
    {
        args ??= Array.Empty<Union>();
        if (Remedy.Schematics.SignalNetwork.Wants(this) && !Remedy.Schematics.SignalNetwork.Dispatch(this, uid, args))
            return;
        ReportSent(uid);
        _invokeDepth++;
        try
        {
            foreach (var l in Listeners(uid))
                l.Receive(args);
        }
        finally { _invokeDepth--; }

        if (HasComponentSubscribers)
            DeliverToComponents(uid, args);
    }

    #endregion

    #region Delivery

    // Copies the listeners for a UID (plus the "every instance" listeners) into a list for the current nesting
    // level, so callbacks can subscribe, despawn or send Signals without breaking the loop. UID 0 sends to everyone.
    // Callers increment _invokeDepth first.
    private List<SignalListener> Listeners(int uid)
    {
        int level = _invokeDepth - 1;
        while (_snapshots.Count <= level)
            _snapshots.Add(new List<SignalListener>());

        var result = _snapshots[level];
        result.Clear();

        if (uid == 0)
        {
            foreach (var list in _listeners.Values)
                result.AddRange(list);
        }
        else
        {
            if (_listeners.TryGetValue(uid, out var own)) result.AddRange(own);
            if (_listeners.TryGetValue(0, out var any)) result.AddRange(any);
        }
        return result;
    }
    private readonly List<List<SignalListener>> _snapshots = new();

    private void ForEachListener(int uid, Action<SignalListener> deliver)
    {
        _invokeDepth++;
        try
        {
            foreach (var l in Listeners(uid))
                deliver(l);
        }
        finally { _invokeDepth--; }
    }

    private Union[] Pack<T1>(T1 a)
    {
        var args = _invokeDepth > 1 ? new Union[1] : Scratch(1);
        args[0] = SignalArgs.ToUnion(a);
        return args;
    }
    private Union[] Pack<T1, T2>(T1 a, T2 b)
    {
        var args = _invokeDepth > 1 ? new Union[2] : Scratch(2);
        args[0] = SignalArgs.ToUnion(a); args[1] = SignalArgs.ToUnion(b);
        return args;
    }
    private Union[] Pack<T1, T2, T3>(T1 a, T2 b, T3 c)
    {
        var args = _invokeDepth > 1 ? new Union[3] : Scratch(3);
        args[0] = SignalArgs.ToUnion(a); args[1] = SignalArgs.ToUnion(b); args[2] = SignalArgs.ToUnion(c);
        return args;
    }
    private Union[] Pack<T1, T2, T3, T4>(T1 a, T2 b, T3 c, T4 d)
    {
        var args = _invokeDepth > 1 ? new Union[4] : Scratch(4);
        args[0] = SignalArgs.ToUnion(a); args[1] = SignalArgs.ToUnion(b); args[2] = SignalArgs.ToUnion(c); args[3] = SignalArgs.ToUnion(d);
        return args;
    }

    // Reused for Union conversions at the outermost invoke; nested invokes get their own arrays.
    // Reused argument arrays. Not kept through a script reload: Unity would bring a null one back as an empty array
    // (which "??=" then kept, so sending a value threw IndexOutOfRange), so the size is checked too.
    [NonSerialized] private Union[] _scratch1, _scratch2, _scratch3, _scratch4;
    private Union[] Scratch(int count) => count switch
    {
        1 => Sized(ref _scratch1, 1),
        2 => Sized(ref _scratch2, 2),
        3 => Sized(ref _scratch3, 3),
        _ => Sized(ref _scratch4, 4),
    };

    private static Union[] Sized(ref Union[] array, int count)
        => array != null && array.Length == count ? array : (array = new Union[count]);

    private bool HasComponentSubscribers => _subs.Count > 0;

    // Old-style subscriptions (owned by a component rather than a UID) whose owner belongs to the target object.
    private void DeliverToComponents(int uid, Union[] args)
    {
        if (_subs.Count == 0 || UIDResolver == null)
            return;

        for (int i = 0; i < _subs.Count; i++)
        {
            var sub = _subs[i];
            if (sub == null)
                continue;
            if (uid == 0 || UIDResolver(sub.Parent) == uid)
                sub.Invoke(args);
        }
    }

    // Old-style invokes (targeting a component) also reach the listeners of the object that component belongs to.
    internal void DeliverComponentInvokeToListeners(UnityEngine.Object target, Union[] args)
    {
        if (_listeners.Count == 0)
            return;

        int uid = ReferenceEquals(target, null) ? 0 : UIDResolver?.Invoke(target) ?? 0;
        if (uid == 0 && !ReferenceEquals(target, null))
            return; // the component isn't part of a spawned Schematic Object

        _invokeDepth++;
        try
        {
            foreach (var l in Listeners(uid))
                l.Receive(args ?? Array.Empty<Union>());
        }
        finally { _invokeDepth--; }
    }

    #endregion
}
