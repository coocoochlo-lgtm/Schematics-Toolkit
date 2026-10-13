using System;

namespace Remedy.Schematics
{
    /// <summary>
    /// Who may send a networked Signal to the other players.
    /// </summary>
    /// <remarks>
    /// Mirrored by <see cref="SignalNetworkSender"/> on the Signal asset itself (Signal assets live in the Graph assembly,
    /// which can't see this one); the values are the same, in the same order.
    /// </remarks>
    public enum SignalSender
    {
        /// <summary>
        /// Only the object's authority (the server, or the owner in distributed authority) sends it; everyone else hears
        /// the authority's copy. Sends from anyone else stay unheard, so a Behavior that runs on every peer still fires it
        /// exactly once everywhere. The usual choice for results: OnDied, OnPickedUp, OnOpened.
        /// </summary>
        Authority,
        /// <summary>
        /// The object's owner (e.g. the player controlling it) sends it to the authority, which checks it really came
        /// from the owner and then fires it on everyone. The owner hears its own send straight away; sends from anyone
        /// else (the authority included, unless it's also the owner) are dropped, so other players' input never reaches
        /// an object they don't control. The usual choice for inputs: Fire, Reload, Throttle, Jump.
        /// </summary>
        OwnerToAuthority,
        /// <summary>Whoever sends it, it fires on everyone (chat, emotes, cosmetic pings).</summary>
        Anyone,
    }

    /// <summary>
    /// Marks a Behavior's Variable or Signal as synced over the network by default, so Behavior authors decide what
    /// matters in multiplayer and level designers don't have to.
    /// <code>
    /// [NetworkSync] public readonly VariableHandle&lt;float&gt; Health = 100f;
    /// [NetworkSync(SignalSender.OwnerToAuthority), InputSignal] public readonly SignalHandle Fire;
    /// </code>
    /// On a Variable: it's Network Sync (the authority sends its value whenever it changes). On a Signal: it's networked
    /// with the given sender. These are only defaults, applied when the Behavior Metadata is generated: the Network Sync
    /// toggle in the Inspector / IO Dock still wins once someone changes it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NetworkSyncAttribute : Attribute
    {
        public NetworkSyncAttribute() { }
        public NetworkSyncAttribute(SignalSender sender) { Sender = sender; }

        /// <summary>Who may send the Signal (only meaningful on Signals).</summary>
        public SignalSender Sender { get; } = SignalSender.Authority;
    }

    /// <summary>
    /// Marks a Behavior whose object is simulated by its owner rather than the server when networked (vehicles, mounts:
    /// whoever drives it moves it, so it responds instantly). Turning on an object's Network Sync then sets its
    /// NetworkTransform to owner authority, and its Box3D body is simulated by the owner.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class NetworkOwnerSimulatedAttribute : Attribute { }
}
