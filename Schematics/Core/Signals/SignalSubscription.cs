using System;
using UnityEngine;

/// <summary>
/// A subscription made through a <c>SignalHandle</c>. Subscriptions are released automatically when either the listening
/// instance or the object it listens to despawns; <see cref="Dispose"/> releases one early.
/// </summary>
public readonly struct SignalSubscription : IDisposable
{
    private readonly SignalData _data;
    private readonly int _uid;
    private readonly SignalListener _listener;

    internal SignalSubscription(SignalData data, int uid, SignalListener listener)
    {
        _data = data;
        _uid = uid;
        _listener = listener;
    }

    public void Dispose()
    {
        if (_data != null)
            _data.Unsubscribe(_uid, _listener);
    }

    /// <summary>
    /// Subscribes a listener for the target UID. When subscribed from inside a lifecycle method, the handler later runs
    /// with that listening instance selected (so its Variables read its own values) and the subscription is released
    /// when the listener despawns.
    /// </summary>
    internal static SignalSubscription Create(SignalData data, int uid, SignalListener listener, BehaviorInstance owner)
    {
        data.Subscribe(uid, listener);
        var subscription = new SignalSubscription(data, uid, listener);
        owner?.TrackSubscription(subscription);
        return subscription;
    }

    // Selects the listening instance around a handler call, the same way the Runtime does around lifecycle methods.
    internal static bool Enter(BehaviorInstance listener, out BehaviorInstance previousSelection, out BehaviorInstance previousCurrent)
    {
        previousSelection = null;
        previousCurrent = null;
        if (listener == null)
            return true;
        // Disabled instances skip everything, Signals included.
        if (!listener.IsSpawned || !listener.Enabled)
            return false;

        previousSelection = listener.Behavior.Cursor.Instance;
        previousCurrent = ExecutionContext.CurrentInstance;
        listener.Behavior.Cursor.Select(listener);
        ExecutionContext.CurrentInstance = listener;
        return true;
    }

    internal static void Exit(BehaviorInstance listener, BehaviorInstance previousSelection, BehaviorInstance previousCurrent)
    {
        if (listener == null)
            return;
        listener.Behavior.Cursor.Select(previousSelection);
        ExecutionContext.CurrentInstance = previousCurrent;
    }

    internal static void Report(Exception exception, BehaviorInstance listener)
        => Debug.LogException(exception, listener != null ? listener.GameObject : null);
}
