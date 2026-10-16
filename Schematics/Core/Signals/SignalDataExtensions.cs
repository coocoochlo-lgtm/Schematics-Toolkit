using System;

/// <summary>
/// Sending and listening on any Signal asset per Schematic Object instance: for Signals defined in the editor (for
/// example the entries of a Behavior's Properties asset) rather than as SignalHandle fields. Works exactly like
/// SignalHandle: addressed by the target object's UID, and handlers run with the subscribing instance selected.
/// </summary>
public static class SignalDataExtensions
{

    /// <summary>Sends the Signal to the target instance's object.</summary>
    public static void Invoke(this SignalData signal, BehaviorInstance target)
    {
        if (signal != null) signal.Invoke(BehaviorSignal.UIDOf(target));
    }

    /// <summary>Listens for the Signal on the target instance's object.</summary>
    public static SignalSubscription Subscribe(this SignalData signal, BehaviorInstance target, Action handler)
        => signal == null ? default : SignalRouting.Subscribe(signal, BehaviorSignal.UIDOf(target), handler);

    /// <summary>Sends the Signal to the target instance's object.</summary>
    public static void Invoke<T1>(this SignalData signal, BehaviorInstance target, T1 a)
    {
        if (signal != null) signal.Invoke(BehaviorSignal.UIDOf(target), a);
    }

    /// <summary>Listens for the Signal on the target instance's object.</summary>
    public static SignalSubscription Subscribe<T1>(this SignalData signal, BehaviorInstance target, Action<T1> handler)
        => signal == null ? default : SignalRouting.Subscribe(signal, BehaviorSignal.UIDOf(target), handler);

    /// <summary>Sends the Signal to the target instance's object.</summary>
    public static void Invoke<T1, T2>(this SignalData signal, BehaviorInstance target, T1 a, T2 b)
    {
        if (signal != null) signal.Invoke(BehaviorSignal.UIDOf(target), a, b);
    }

    /// <summary>Listens for the Signal on the target instance's object.</summary>
    public static SignalSubscription Subscribe<T1, T2>(this SignalData signal, BehaviorInstance target, Action<T1, T2> handler)
        => signal == null ? default : SignalRouting.Subscribe(signal, BehaviorSignal.UIDOf(target), handler);

    /// <summary>Sends the Signal to the target instance's object.</summary>
    public static void Invoke<T1, T2, T3>(this SignalData signal, BehaviorInstance target, T1 a, T2 b, T3 c)
    {
        if (signal != null) signal.Invoke(BehaviorSignal.UIDOf(target), a, b, c);
    }

    /// <summary>Listens for the Signal on the target instance's object.</summary>
    public static SignalSubscription Subscribe<T1, T2, T3>(this SignalData signal, BehaviorInstance target, Action<T1, T2, T3> handler)
        => signal == null ? default : SignalRouting.Subscribe(signal, BehaviorSignal.UIDOf(target), handler);

    /// <summary>Sends the Signal to the target instance's object.</summary>
    public static void Invoke<T1, T2, T3, T4>(this SignalData signal, BehaviorInstance target, T1 a, T2 b, T3 c, T4 d)
    {
        if (signal != null) signal.Invoke(BehaviorSignal.UIDOf(target), a, b, c, d);
    }

    /// <summary>Listens for the Signal on the target instance's object.</summary>
    public static SignalSubscription Subscribe<T1, T2, T3, T4>(this SignalData signal, BehaviorInstance target, Action<T1, T2, T3, T4> handler)
        => signal == null ? default : SignalRouting.Subscribe(signal, BehaviorSignal.UIDOf(target), handler);
}


/// <summary>
/// The one place subscriptions are made, for SignalHandles and Signal assets alike.
/// </summary>
internal static class SignalRouting
{

    public static SignalSubscription Subscribe(SignalData data, int uid, Action handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // Subscribed from inside a lifecycle method: run the handler with that instance selected,
        // and release the subscription when it despawns.
        var listener = ExecutionContext.CurrentInstance;
        Action wrapped = handler;
        if (listener != null)
        {
            wrapped = () =>
            {
                if (!SignalSubscription.Enter(listener, out var selection, out var current)) return;
                try { handler(); }
                catch (Exception exception) { SignalSubscription.Report(exception, listener); }
                finally { SignalSubscription.Exit(listener, selection, current); }
            };
        }
        return SignalSubscription.Create(data, uid, new SignalListener0(wrapped), listener);
    }

    public static SignalSubscription Subscribe<T1>(SignalData data, int uid, Action<T1> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // Subscribed from inside a lifecycle method: run the handler with that instance selected,
        // and release the subscription when it despawns.
        var listener = ExecutionContext.CurrentInstance;
        Action<T1> wrapped = handler;
        if (listener != null)
        {
            wrapped = (a) =>
            {
                if (!SignalSubscription.Enter(listener, out var selection, out var current)) return;
                try { handler(a); }
                catch (Exception exception) { SignalSubscription.Report(exception, listener); }
                finally { SignalSubscription.Exit(listener, selection, current); }
            };
        }
        return SignalSubscription.Create(data, uid, new SignalListener<T1>(wrapped), listener);
    }

    public static SignalSubscription Subscribe<T1, T2>(SignalData data, int uid, Action<T1, T2> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // Subscribed from inside a lifecycle method: run the handler with that instance selected,
        // and release the subscription when it despawns.
        var listener = ExecutionContext.CurrentInstance;
        Action<T1, T2> wrapped = handler;
        if (listener != null)
        {
            wrapped = (a, b) =>
            {
                if (!SignalSubscription.Enter(listener, out var selection, out var current)) return;
                try { handler(a, b); }
                catch (Exception exception) { SignalSubscription.Report(exception, listener); }
                finally { SignalSubscription.Exit(listener, selection, current); }
            };
        }
        return SignalSubscription.Create(data, uid, new SignalListener<T1, T2>(wrapped), listener);
    }

    public static SignalSubscription Subscribe<T1, T2, T3>(SignalData data, int uid, Action<T1, T2, T3> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // Subscribed from inside a lifecycle method: run the handler with that instance selected,
        // and release the subscription when it despawns.
        var listener = ExecutionContext.CurrentInstance;
        Action<T1, T2, T3> wrapped = handler;
        if (listener != null)
        {
            wrapped = (a, b, c) =>
            {
                if (!SignalSubscription.Enter(listener, out var selection, out var current)) return;
                try { handler(a, b, c); }
                catch (Exception exception) { SignalSubscription.Report(exception, listener); }
                finally { SignalSubscription.Exit(listener, selection, current); }
            };
        }
        return SignalSubscription.Create(data, uid, new SignalListener<T1, T2, T3>(wrapped), listener);
    }

    public static SignalSubscription Subscribe<T1, T2, T3, T4>(SignalData data, int uid, Action<T1, T2, T3, T4> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // Subscribed from inside a lifecycle method: run the handler with that instance selected,
        // and release the subscription when it despawns.
        var listener = ExecutionContext.CurrentInstance;
        Action<T1, T2, T3, T4> wrapped = handler;
        if (listener != null)
        {
            wrapped = (a, b, c, d) =>
            {
                if (!SignalSubscription.Enter(listener, out var selection, out var current)) return;
                try { handler(a, b, c, d); }
                catch (Exception exception) { SignalSubscription.Report(exception, listener); }
                finally { SignalSubscription.Exit(listener, selection, current); }
            };
        }
        return SignalSubscription.Create(data, uid, new SignalListener<T1, T2, T3, T4>(wrapped), listener);
    }
}
