using System;

/// <summary>
/// The Schematic counterpart to <c>[RequireComponent]</c>: attaching this Behavior also attaches the listed Behaviors to
/// the same GameObject (in the Inspector, with <c>AttachBehavior</c>, and with <c>AddBehavior</c> at runtime).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequireBehaviorAttribute : Attribute
{
    public Type[] Behaviors { get; }

    public RequireBehaviorAttribute(params Type[] behaviors)
    {
        Behaviors = behaviors ?? Array.Empty<Type>();
    }
}
