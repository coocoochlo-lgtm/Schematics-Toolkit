using Remedy.Framework;
using System;

/// <summary>
/// A subscription to a Signal for one Schematic Object instance (by UID). Typed listeners get their values directly
/// from code; values from a Schematic Graph arrive as Unions and are converted through <see cref="SignalArgs"/>.
/// </summary>
public abstract class SignalListener
{
    /// <summary>Called with Union arguments, when the Signal was sent from a Schematic Graph or an old-style component.</summary>
    public abstract void Receive(Union[] args);
}

/// <summary>Listens from a Schematic Graph: always receives Unions.</summary>
public sealed class GraphSignalListener : SignalListener
{
    private readonly Action<Union[]> _action;
    public GraphSignalListener(Action<Union[]> action) { _action = action; }
    public override void Receive(Union[] args) => _action(args);
}

public sealed class SignalListener0 : SignalListener
{
    public readonly Action Action;
    public SignalListener0(Action action) { Action = action; }
    public override void Receive(Union[] args) => Action();
}

public sealed class SignalListener<T1> : SignalListener
{
    public readonly Action<T1> Action;
    public SignalListener(Action<T1> action) { Action = action; }
    public override void Receive(Union[] args) => Action(Arg<T1>(args, 0));
    internal static T Arg<T>(Union[] args, int i) => args != null && i < args.Length ? SignalArgs.FromUnion<T>(args[i]) : default;
}

public sealed class SignalListener<T1, T2> : SignalListener
{
    public readonly Action<T1, T2> Action;
    public SignalListener(Action<T1, T2> action) { Action = action; }
    public override void Receive(Union[] args) => Action(SignalListener<T1>.Arg<T1>(args, 0), SignalListener<T1>.Arg<T2>(args, 1));
}

public sealed class SignalListener<T1, T2, T3> : SignalListener
{
    public readonly Action<T1, T2, T3> Action;
    public SignalListener(Action<T1, T2, T3> action) { Action = action; }
    public override void Receive(Union[] args) => Action(SignalListener<T1>.Arg<T1>(args, 0), SignalListener<T1>.Arg<T2>(args, 1), SignalListener<T1>.Arg<T3>(args, 2));
}

public sealed class SignalListener<T1, T2, T3, T4> : SignalListener
{
    public readonly Action<T1, T2, T3, T4> Action;
    public SignalListener(Action<T1, T2, T3, T4> action) { Action = action; }
    public override void Receive(Union[] args) => Action(SignalListener<T1>.Arg<T1>(args, 0), SignalListener<T1>.Arg<T2>(args, 1), SignalListener<T1>.Arg<T3>(args, 2), SignalListener<T1>.Arg<T4>(args, 3));
}
