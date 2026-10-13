#if SCHEMATICS_NETCODE
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Remedy.Vehicles.Networking
{
    /// <summary>
    /// Hands a networked vehicle to its driver: when someone takes a driver seat, the vehicle's NetworkObject becomes
    /// theirs, so they simulate it (no input lag) and everyone else sees a proxy; when nobody drives, it goes back to the
    /// server. Nothing to set up: it listens to <see cref="VehicleSeats.DriverChanged"/> for every vehicle.
    /// </summary>
    /// <remarks>
    /// For the driver to actually move the vehicle, its NetworkTransform must use owner authority (the GameObject's
    /// "Simulated by Owner" toggle, on by default for vehicles when Network Sync is turned on). With server authority the
    /// server keeps simulating it and the driver's input reaches it through the car's networked input Signals.<br/>
    /// Client-server: only the server changes ownership. Distributed authority: the current owner hands it over, which
    /// needs the NetworkObject's Ownership to include Transferable.
    /// </remarks>
    internal static class VehicleOwnership
    {
        private static readonly HashSet<NetworkObject> _warned = new();

        // After VehicleSeats clears its static events (SubsystemRegistration).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            _warned.Clear();
            VehicleSeats.DriverChanged -= OnDriverChanged;
            VehicleSeats.DriverChanged += OnDriverChanged;
        }

        private static void OnDriverChanged(GameObject vehicle, GameObject driver)
        {
            var vehicleObject = vehicle != null ? vehicle.GetComponentInParent<NetworkObject>() : null;
            if (vehicleObject == null || !vehicleObject.IsSpawned)
                return;
            var manager = vehicleObject.NetworkManager;
            if (manager == null || !manager.IsListening)
                return;

            var driverObject = driver != null ? driver.GetComponentInParent<NetworkObject>() : null;
            bool driverNetworked = driverObject != null && driverObject.IsSpawned;

            if (!manager.DistributedAuthorityMode)
            {
                // The server decides; a driver that isn't networked (an AI) leaves it with the server.
                if (!manager.IsServer)
                    return;
                ulong owner = driverNetworked ? driverObject.OwnerClientId : NetworkManager.ServerClientId;
                if (vehicleObject.OwnerClientId != owner)
                    vehicleObject.ChangeOwnership(owner);
                return;
            }

            // Distributed authority: whoever owns it hands it to the new driver (and keeps it when nobody drives).
            if (!vehicleObject.HasAuthority || !driverNetworked || vehicleObject.OwnerClientId == driverObject.OwnerClientId)
                return;
            if (!vehicleObject.IsOwnershipTransferable)
            {
                if (_warned.Add(vehicleObject))
                    Debug.LogWarning($"{vehicle.name}'s NetworkObject isn't Transferable, so its driver can't take it over. Add Transferable to its Ownership.", vehicle);
                return;
            }
            vehicleObject.ChangeOwnership(driverObject.OwnerClientId);
        }
    }
}
#endif
