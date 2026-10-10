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
    }

    #region Invoking

    public void Invoke(int uid)
    {
        ForEachListener(uid, l =>
        {
            if (l is SignalListener0 typed) typed.Action();
            else l.Receive(Array.Empty<Union>());
        });
        DeliverToComponents(uid, Array.Empty<Union>());
    }

    public void Invoke<T1>(int uid, T1 a)
    {
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
    private Union[] _scratch1, _scratch2, _scratch3, _scratch4;
    private Union[] Scratch(int count) => count switch
    {
        1 => _scratch1 ??= new Union[1],
        2 => _scratch2 ??= new Union[2],
        3 => _scratch3 ??= new Union[3],
        _ => _scratch4 ??= new Union[4],
    };

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
