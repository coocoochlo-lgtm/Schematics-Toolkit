using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>
    /// Holds the Signals of a vehicle prefab's seats (as sub-assets), so each seat gets its own On Entered, On Exited,
    /// Enter Request and Exit Request that graphs can use. Created next to the prefab by "+ Seat" in the IO Dock
    /// ("&lt;Prefab&gt; Seat Signals.asset"); you never need to edit it by hand.
    /// </summary>
    public class VehicleSeatSignals : ScriptableObject
    {
        [Tooltip("Every seat Signal in this asset, in the order they were made.")]
        public List<SignalData> Signals = new();
    }
}
