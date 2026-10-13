#if SCHEMATICS_NETCODE
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Remedy.Networking
{
    /// <summary>
    /// Syncs a Box3D body over the network: one peer simulates it, and everyone else gets it as a kinematic proxy that
    /// follows the synced Transform (from a NetworkTransform) while still pushing their local bodies around. Added by
    /// the GameObject's Network Sync toggle.
    /// </summary>
    /// <remarks>
    /// The peer that simulates is the one whose Transform changes count: the owner when the NetworkTransform's
    /// Authority Mode is Owner (vehicles: whoever drives owns and moves it, see Features/Vehicles), otherwise the
    /// authority (the server, or the owner in distributed authority). Ownership changes hand the body over.
    /// </remarks>
    [AddComponentMenu("Schematics/Networking/Network Box3D Body")]
    [RequireComponent(typeof(Box3DBody))]
    public class NetworkBox3DBody : NetworkBehaviour
    {
        private Box3DBody _body;
        private Box3DBodyType _authoredType;
        private NetworkTransform _networkTransform;

        private void Awake()
        {
            _body = GetComponent<Box3DBody>();
            _authoredType = _body.BodyType;
            _networkTransform = GetComponent<NetworkTransform>();
        }

        /// <summary>True on the one peer that simulates the body (everyone else has a kinematic proxy).</summary>
        public bool Simulates
        {
            get
            {
                if (!IsSpawned)
                    return true;
                bool ownerMoves = _networkTransform != null && _networkTransform.AuthorityMode == NetworkTransform.AuthorityModes.Owner;
                return ownerMoves ? IsOwner : HasAuthority;
            }
        }

        public override void OnNetworkSpawn() => Apply();
        public override void OnGainedOwnership() => Apply();
        public override void OnLostOwnership() => Apply();
        protected override void OnOwnershipChanged(ulong previous, ulong current) => Apply();

        public override void OnNetworkDespawn()
        {
            _body.SetNetworkProxy(false);
            _body.BodyType = _authoredType;
        }

        public override void OnDestroy()
        {
            if (_body != null)
                _body.SetNetworkProxy(false);
            base.OnDestroy();
        }

        private void Apply()
        {
            bool proxy = IsSpawned && !Simulates;
            _body.SetNetworkProxy(proxy);
            if (!proxy)
                _body.BodyType = _authoredType;
        }
    }
}
#endif
