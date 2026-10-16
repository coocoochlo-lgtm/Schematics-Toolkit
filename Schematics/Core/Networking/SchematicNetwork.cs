using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A Behavior that has state its Variables can't carry (who sits in which seat, a list of objects...), and wants it
    /// the same for every player: the authority writes it whenever <see cref="SchematicNetwork.MarkStateChanged"/> is
    /// called, and everyone else (including players who join later) reads it.
    /// </summary>
    /// <remarks>
    /// Values travel as <see cref="Union"/>s: numbers, vectors, strings, and GameObjects/components that are part of a
    /// spawned NetworkObject. Signals sent while the state is being read stay on that peer (every peer reads the same
    /// state, so each fires its own copy).
    /// </remarks>
    public interface INetworkStateBehavior
    {
        /// <summary>Writes the instance's state (on the authority).</summary>
        void WriteNetworkState(BehaviorInstance instance, List<Union> state);

        /// <summary>Applies the authority's state (on everyone else).</summary>
        void ReadNetworkState(BehaviorInstance instance, Union[] state);
    }

    /// <summary>
    /// The seam between Schematic Behaviors and the networking layer (Remedy.Networking, when Netcode for GameObjects
    /// is installed). Behaviors ask these questions without referencing Netcode; the networking layer fills in the
    /// hooks. Offline (or with nothing installed) everything answers as for a single player: you have authority over
    /// everything, control everything, and nothing is networked.
    /// </summary>
    public static class SchematicNetwork
    {
        /// <summary>Hook: whether a GameObject belongs to a NetworkObject that is spawned on the network.</summary>
        public static Func<GameObject, bool> IsNetworkedHook;

        /// <summary>Hook: whether this peer owns (controls) the NetworkObject a GameObject belongs to.</summary>
        public static Func<GameObject, bool> IsOwnerHook;

        /// <summary>Hook: sends a Behavior instance's <see cref="INetworkStateBehavior"/> state now (if this peer is its authority).</summary>
        public static Action<BehaviorInstance> StateChangedHook;

        /// <summary>True while the networking layer is applying state or Signals that came from another peer.</summary>
        public static bool IsApplyingRemote => SignalNetwork.IsReceiving;

        /// <summary>
        /// Whether this peer decides things for the object (runs its graph events, sends its synced Variables and
        /// state). Always true offline or for objects that aren't networked.
        /// </summary>
        public static bool HasAuthority(GameObject gameObject)
            => gameObject == null || GraphNetwork.HasAuthority == null || GraphNetwork.HasAuthority(gameObject);

        /// <summary>Whether the object is part of a NetworkObject spawned on the network. False offline.</summary>
        public static bool IsNetworked(GameObject gameObject)
            => gameObject != null && IsNetworkedHook != null && IsNetworkedHook(gameObject);

        /// <summary>
        /// Whether this peer controls the object: it owns its NetworkObject, or the object isn't networked at all (so a
        /// local player, an offline game, or a plain scene object). Use it before reading local input for an object.
        /// </summary>
        public static bool IsLocallyControlled(GameObject gameObject)
            => gameObject == null || IsOwnerHook == null || !IsNetworked(gameObject) || IsOwnerHook(gameObject);

        /// <summary>
        /// Tells the networking layer an <see cref="INetworkStateBehavior"/>'s state changed, so the authority sends it.
        /// Does nothing offline, or on peers without authority.
        /// </summary>
        public static void MarkStateChanged(BehaviorInstance instance)
        {
            if (instance != null && StateChangedHook != null)
                StateChangedHook(instance);
        }
    }
}
