#if SCHEMATICS_NETCODE
using Remedy.Framework;
using Remedy.Schematics;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Remedy.Networking
{
    /// <summary>
    /// Passes Signals marked Network Sync on to the other players. Installed into <see cref="SignalNetwork"/> (and the
    /// <see cref="SchematicNetwork"/> questions Behaviors ask) when Netcode for GameObjects is in the project.
    /// </summary>
    /// <remarks>
    /// <para>A Signal sent to an object travels through that object's <see cref="SchematicNetworkGraph"/> (the nearest one
    /// on it or above it), naming the Signal by its id and the object by its path below the graph's NetworkObject. A
    /// Signal sent to everyone (UID 0) travels as a named message, through the server.</para>
    /// <para>Who may send (<see cref="SignalNetworkSender"/>): <b>Authority</b>: only the object's authority (for Signals sent
    /// to everyone, the server or session owner); anyone else's send is dropped, since the authority's copy will arrive.
    /// <b>OwnerToAuthority</b>: the owner sends to the authority, which checks the sender really owns the object, fires
    /// it, and passes it on to everyone else (when the authority owns the object itself, it just sends it to everyone);
    /// anyone else's send, the authority's included, is dropped. For Signals sent to everyone, any player may send it
    /// through the server.
    /// <b>Anyone</b>: every send fires on everyone.</para>
    /// <para>Whatever arrives is delivered inside <see cref="SignalNetwork.LocalOnly"/>, so Signals its listeners send in
    /// turn stay local (every peer runs those listeners itself): nothing echoes back and forth. Graph events still only
    /// run on the object's authority (<see cref="GraphNetwork"/>), so a networked graph never handles one twice.</para>
    /// </remarks>
    internal static class SignalNetworkBridge
    {
        private const string GlobalMessage = "Remedy.Schematics.Signal";
        private const float RateLimit = 30f;

        private static readonly HashSet<NetworkManager> _hooked = new();
        private static readonly HashSet<SignalData> _warnedArguments = new();
        private static readonly HashSet<SignalData> _warnedIds = new();
        private static readonly Dictionary<(SignalData, int), Rate> _rates = new();

        private struct Rate
        {
            public float WindowStart;
            public int Count;
            public bool Warned;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Install()
        {
            _hooked.Clear();
            _warnedArguments.Clear();
            _warnedIds.Clear();
            _rates.Clear();

            SignalNetwork.Send = Send;
            SchematicNetwork.IsNetworkedHook = IsNetworked;
            SchematicNetwork.IsOwnerHook = IsOwner;
            SchematicNetwork.StateChangedHook = SchematicNetworkGraph.SendState;

            NetworkManager.OnInstantiated -= Hook;
            NetworkManager.OnInstantiated += Hook;
        }

        // The NetworkManager usually exists by the time the first scene has loaded.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void HookExisting()
        {
            NetworkManager.OnInstantiated -= Hook;
            NetworkManager.OnInstantiated += Hook;
            if (NetworkManager.Singleton != null)
                Hook(NetworkManager.Singleton);
        }

        private static void Hook(NetworkManager manager)
        {
            if (manager == null || !_hooked.Add(manager))
                return;
            manager.OnServerStarted += () => Register(manager);
            manager.OnClientStarted += () => Register(manager);
            if (manager.IsListening)
                Register(manager);
        }

        private static void Register(NetworkManager manager)
        {
            manager.CustomMessagingManager?.RegisterNamedMessageHandler(GlobalMessage,
                (sender, reader) => OnGlobalMessage(manager, sender, reader));
        }

        #region Questions Behaviors ask

        private static NetworkObject NetworkObjectOf(GameObject gameObject)
        {
            if (gameObject == null)
                return null;
            var networkObject = gameObject.GetComponentInParent<NetworkObject>();
            return networkObject != null && networkObject.IsSpawned ? networkObject : null;
        }

        private static bool IsNetworked(GameObject gameObject) => NetworkObjectOf(gameObject) != null;

        private static bool IsOwner(GameObject gameObject)
        {
            var networkObject = NetworkObjectOf(gameObject);
            return networkObject == null || networkObject.IsOwner;
        }

        #endregion

        #region Sending

        // Returns whether this peer hears the Signal itself.
        private static bool Send(SignalData signal, int uid, Union[] args)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
                return true; // offline: as if networking weren't there

            if (!signal.HasUid)
            {
                if (_warnedIds.Add(signal))
                    Debug.LogWarning($"The networked Signal '{signal.name}' has no saved id yet, so other players can't recognise it. Save the asset (or refresh Behavior Metadata) in the editor; it stays local until then.", signal);
                return true;
            }

            if (uid == 0)
                return SendToEveryone(manager, signal, args);

            var target = ExecutionContext.Objects.TryGetValue(uid, out var schematicObject) ? schematicObject : null;
            var carrier = target != null ? SchematicNetworkGraph.CarrierOf(target.gameObject) : null;
            if (carrier == null || !carrier.IsSpawned)
                return true; // not a networked object: a local Signal

            bool authority = carrier.HasAuthority;
            switch (signal.Sender)
            {
                case SignalNetworkSender.Authority:
                    if (!authority)
                        return false;
                    Check(signal, uid, args);
                    carrier.SendSignal(Pack(signal, carrier.PathTo(target.gameObject), args), toAuthority: false);
                    return true;

                case SignalNetworkSender.OwnerToAuthority:
                    // Only the owner's sends count (on the authority too, unless it owns the object): code that runs
                    // on every peer then still fires it once, and nobody else's input reaches someone else's object.
                    if (!carrier.IsOwner)
                        return false;
                    Check(signal, uid, args);
                    carrier.SendSignal(Pack(signal, carrier.PathTo(target.gameObject), args), toAuthority: !authority);
                    return true;

                default:
                    Check(signal, uid, args);
                    carrier.SendSignal(Pack(signal, carrier.PathTo(target.gameObject), args), toAuthority: false);
                    return true;
            }
        }

        // Signals sent to everyone: the server (or the session owner, in distributed authority) is their authority.
        private static bool SendToEveryone(NetworkManager manager, SignalData signal, Union[] args)
        {
            bool distributed = manager.DistributedAuthorityMode;
            bool authority = distributed ? manager.LocalClientId == manager.CurrentSessionOwner : manager.IsServer;
            if (signal.Sender == SignalNetworkSender.Authority && !authority)
                return false;

            Check(signal, 0, args);
            var payload = Pack(signal, "", args);
            if (authority || distributed)
                SendGlobal(manager, payload, exclude: manager.LocalClientId);
            else
                SendGlobalTo(manager, NetworkManager.ServerClientId, payload); // the server passes it on
            return true;
        }

        private static void SendGlobal(NetworkManager manager, byte[] payload, ulong exclude, ulong alsoExclude = ulong.MaxValue)
        {
            var ids = manager.ConnectedClientsIds;
            if (ids == null)
                return;
            foreach (var id in ids)
                if (id != exclude && id != alsoExclude && id != manager.LocalClientId)
                    SendGlobalTo(manager, id, payload);
        }

        private static void SendGlobalTo(NetworkManager manager, ulong clientId, byte[] payload)
        {
            var messaging = manager.CustomMessagingManager;
            if (messaging == null)
                return;
            using var writer = new FastBufferWriter(payload.Length + 8, Allocator.Temp);
            writer.WriteValueSafe(payload);
            messaging.SendNamedMessage(GlobalMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
        }

        private static void OnGlobalMessage(NetworkManager manager, ulong sender, FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte[] payload);
            if (payload == null || !Unpack(payload, out var signal, out _, out var args))
                return;

            // Client-server: the server checks the sender may send it, and passes it on to everyone else.
            if (!manager.DistributedAuthorityMode && manager.IsServer && sender != manager.LocalClientId)
            {
                if (signal.Sender == SignalNetworkSender.Authority)
                {
                    Debug.LogWarning($"Client {sender} sent the Signal '{signal.name}', which only the server may send. Ignored.", signal);
                    return;
                }
                SendGlobal(manager, payload, exclude: sender);
            }

            using (SignalNetwork.LocalOnly())
                signal.InvokeFromGraph(0, args);
        }

        #endregion

        #region Payloads

        internal static byte[] Pack(SignalData signal, string path, Union[] args)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(signal.Uid);
            writer.Write(path ?? "");
            int count = args != null ? args.Length : 0;
            writer.Write(count);
            for (int i = 0; i < count; i++)
                UnionNetworkSerializer.WriteUnion(writer, args[i]);
            return stream.ToArray();
        }

        internal static bool Unpack(byte[] payload, out SignalData signal, out string path, out Union[] args)
        {
            using var reader = new BinaryReader(new MemoryStream(payload));
            string uid = reader.ReadString();
            path = reader.ReadString();
            int count = reader.ReadInt32();
            args = new Union[count];
            for (int i = 0; i < count; i++)
                args[i] = UnionNetworkSerializer.ReadUnion(reader);

            signal = SignalNetwork.Find(uid);
            if (signal == null)
            {
                Debug.LogWarning($"A networked Signal arrived that isn't loaded on this peer (id {uid}). Is the asset in the build?");
                return false;
            }
            return true;
        }

        // Arguments that can't travel arrive as null: warn once per Signal. Too many sends a second: warn once too.
        private static void Check(SignalData signal, int uid, Union[] args)
        {
            if (args != null && !_warnedArguments.Contains(signal))
                for (int i = 0; i < args.Length; i++)
                    if (!UnionNetworkSerializer.CanSend(args[i]))
                    {
                        _warnedArguments.Add(signal);
                        string parameter = i < signal.Parameters.Contents.Count ? signal.Parameters.Contents[i].Name : $"#{i + 1}";
                        Debug.LogWarning($"The networked Signal '{signal.name}' was sent with a value for '{parameter}' that can't travel over the network " +
                                         "(an object without a spawned NetworkObject, an asset, a list...). Other players get null for it. " +
                                         "Turn on Network Sync for that object, or send something they can find themselves.", signal);
                        break;
                    }

            var key = (signal, uid);
            _rates.TryGetValue(key, out var rate);
            float now = Time.unscaledTime;
            if (now - rate.WindowStart >= 1f)
            {
                rate.WindowStart = now;
                rate.Count = 0;
            }
            rate.Count++;
            if (rate.Count > RateLimit && !rate.Warned)
            {
                rate.Warned = true;
                Debug.LogWarning($"The networked Signal '{signal.name}' is sent more than {RateLimit} times a second. Each send is a network message: " +
                                 "for values that change every frame use a Variable with Network Sync instead, or send only on change.", signal);
            }
            _rates[key] = rate;
        }

        #endregion
    }
}
#endif
