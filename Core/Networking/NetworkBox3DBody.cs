#if SCHEMATICS_NETCODE
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Unity.Netcode;
using UnityEngine;

namespace Remedy.Networking
{
    /// <summary>
    /// Syncs a Box3D body over the network: the peer with authority simulates it, and everyone else gets it as a
    /// kinematic proxy that follows the synced Transform (from a NetworkTransform) while still pushing their local
    /// bodies around. Added by the GameObject's Network Sync toggle.
    /// </summary>
    [AddComponentMenu("Schematics/Networking/Network Box3D Body")]
    [RequireComponent(typeof(Box3DBody))]
    public class NetworkBox3DBody : NetworkBehaviour
    {
        private Box3DBody _body;
        private Box3DBodyType _authoredType;

        private void Awake()
        {
            _body = GetComponent<Box3DBody>();
            _authoredType = _body.BodyType;
        }

        public override void OnNetworkSpawn() => Apply();
        public override void OnGainedOwnership() => Apply();
        public override void OnLostOwnership() => Apply();

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
            bool proxy = IsSpawned && !HasAuthority;
            _body.SetNetworkProxy(proxy);
            if (!proxy)
                _body.BodyType = _authoredType;
        }
    }
}
#endif
