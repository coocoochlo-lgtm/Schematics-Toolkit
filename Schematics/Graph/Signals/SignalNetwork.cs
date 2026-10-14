using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Who may send a networked Signal to the other players. The same values as <c>SignalSender</c> (used by the
    /// <c>[NetworkSync]</c> attribute in Schematics Core), kept here because Signal assets live in this assembly.
    /// </summary>
    public enum SignalNetworkSender
    {
        /// <summary>Only the object's authority sends it; sends from anyone else stay unheard.</summary>
        Authority,
        /// <summary>The object's owner sends it to the authority, which checks it and fires it on everyone; others' sends are dropped.</summary>
        OwnerToAuthority,
        /// <summary>Whoever sends it, it fires on everyone.</summary>
        Anyone,
    }

    /// <summary>
    /// The seam between Signals and the networking layer (Remedy.Networking, when Netcode for GameObjects is installed),
    /// like <see cref="GraphNetwork"/> is for graphs. Signals don't reference Netcode: when a Signal marked Networked is
    /// sent on this peer, <see cref="Send"/> passes it on to the other players. With nothing installed, Signals stay
    /// local as before.
    /// </summary>
    public static class SignalNetwork
    {
        /// <summary>
        /// Set by the networking layer. Called when a networked Signal is sent on this peer (never for one arriving from
        /// another peer): (Signal, target UID or 0 for everyone, arguments). Returns whether this peer should hear the
        /// Signal itself now; false when it isn't allowed to send it and the allowed sender's copy will arrive instead.
        /// The arguments array may be reused after the call, so read it straight away.
        /// </summary>
        public static Func<SignalData, int, Union[], bool> Send;

        private static int _localDepth;

        /// <summary>
        /// True while Signals arriving from another peer (or anything every peer runs itself, such as a replicated
        /// node) are being delivered. Signals sent meanwhile stay on this peer: every peer sends its own copy, so
        /// passing them on would fire them twice.
        /// </summary>
        public static bool IsReceiving => _localDepth > 0;

        /// <summary>
        /// Keeps Signals sent inside the <c>using</c> block on this peer only (not passed on, never held back).
        /// The networking layer wraps what it receives in one; use it around code every peer runs for itself.
        /// </summary>
        public static LocalScope LocalOnly()
        {
            _localDepth++;
            return new LocalScope(true);
        }

        /// <summary>Ends a <see cref="LocalOnly"/> block.</summary>
        public readonly struct LocalScope : IDisposable
        {
            private readonly bool _active;
            internal LocalScope(bool active) { _active = active; }
            public void Dispose()
            {
                if (_active && _localDepth > 0)
                    _localDepth--;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _localDepth = 0;

        /// <summary>Whether a send of this Signal should go through the networking layer.</summary>
        internal static bool Wants(SignalData signal) => Send != null && _localDepth == 0 && signal != null && signal.Networked;

        /// <summary>Hands a networked send to the networking layer. Returns whether this peer hears it itself.</summary>
        internal static bool Dispatch(SignalData signal, int uid, Union[] args)
        {
            try { return Send(signal, uid, args ?? Array.Empty<Union>()); }
            catch (Exception exception)
            {
                Debug.LogException(exception, signal);
                return true;
            }
        }

        #region Finding Signals by id

        private static readonly Dictionary<string, SignalData> _byUid = new();

        internal static void Register(SignalData signal, string uid)
        {
            if (signal != null && !string.IsNullOrEmpty(uid))
                _byUid[uid] = signal;
        }

        /// <summary>
        /// The loaded Signal asset with this id (<see cref="SignalData.Uid"/>), or null. Used by the networking layer to
        /// find the Signal a peer sent.
        /// </summary>
        public static SignalData Find(string uid)
        {
            if (string.IsNullOrEmpty(uid))
                return null;
            if (_byUid.TryGetValue(uid, out var signal) && signal != null)
                return signal;

            // Not seen yet (or loaded before ids were tracked): look through everything loaded once.
            foreach (var loaded in Resources.FindObjectsOfTypeAll<SignalData>())
                if (loaded != null && loaded.HasUid)
                    _byUid[loaded.Uid] = loaded;
            return _byUid.TryGetValue(uid, out signal) ? signal : null;
        }

        #endregion
    }
}
