using System;
using System.Collections.Generic;
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>
    /// Seats for any vehicle: who sits where, who drives, getting in and out. It knows nothing about how the vehicle
    /// moves, so a car, boat, plane, mech or horse all share it: put it next to the vehicle's own Behavior (e.g.
    /// <see cref="ArcadeCar"/>), which only needs to ask <see cref="HasDriver"/> (or <see cref="DriverOf"/>).
    /// </summary>
    /// <remarks>
    /// Sitting down parents the occupant to the seat's anchor (keeping its world scale), takes its Box3D body out of
    /// the simulation (or makes it kinematic), and can switch off its Behaviors and hide it; getting out puts all of
    /// that back and places it at the seat's exit, checking the way out isn't blocked.<br/>
    /// Vehicles don't know about players. A graph routes input to the vehicle while someone sits in a driver seat:
    /// e.g. the player's graph sends <c>Enter</c> to the vehicle, and the vehicle's graph forwards input Signals to
    /// its movement Behavior while <see cref="HasDriver"/> is on (or the movement Behavior listens to input itself,
    /// like ArcadeCar's Driver Inputs).<br/>
    /// Networked (Network Sync on the vehicle): the vehicle's authority decides who sits where. The Enter / Exit / Switch
    /// Seat Signals are networked (any player may send them; only the authority acts on them), the seating is sent to
    /// every player as Behavior state (late joiners too), and each player seats people on their own copy, firing the
    /// On Entered / On Exited / On Driver Changed Signals locally. Networked occupants are never re-parented (Netcode
    /// only allows that on the server, and it fights their NetworkTransform): they follow the seat's anchor every frame
    /// on every peer instead. The vehicle's NetworkObject is handed to the driver (Features/Vehicles/Networking), so the
    /// driver simulates it.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Vehicle Seats"", ""kind"": ""behavior"", ""menu"": ""Vehicles/Seats"", ""description"": ""Seats for any vehicle (car, boat, plane, mech, horse...): who sits where, who drives, getting in and out. Each seat has an anchor, a driver flag, an exit, and its own Signals (On Entered, On Exited, Enter Request, Exit Request), all created in the IO Dock with + Seat."", ""variables"": [""Seats"", ""Driver"", ""HasDriver"", ""OccupantCount"", ""IsOccupied"", ""MaxEnterDistance"", ""DriverSeatFirst"", ""ExitBlockingLayers""], ""signals"": [""Enter"", ""EnterSeat"", ""Exit"", ""ExitAll"", ""SwitchSeat"", ""OnOccupantEntered"", ""OnOccupantExited"", ""OnDriverChanged"", ""OnFull"", ""OnEmpty""], ""use_for"": [""getting in and out of vehicles"", ""driver and passenger seats"", ""turrets and gunner seats"", ""mounts""], ""notes"": ""Movement Behaviors (ArcadeCar, or your own boat/plane) read HasDriver; graphs route player input to the vehicle while someone drives. Multiplayer: Enter/Exit/EnterSeat/SwitchSeat/ExitAll are networked (anyone may send; the vehicle's authority decides), seating is synced to everyone including late joiners, and the On ... Signals fire on every player's copy. Networked occupants follow their seat instead of being parented. The driver becomes the vehicle's owner and simulates it.""}")]
    [SchematicBehaviorPath("Vehicles/Seats")]
    [NetworkOwnerSimulated]
    public class VehicleSeats : SchematicBehavior, INetworkStateBehavior
    {
        #region Variables

        [SchematicDoc(@"{""name"": ""Seats"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""Seat[]"", ""default"": ""null"", ""description"": ""The vehicle's seats: anchor, driver flag, exit, occupant options and per-seat Signals. Add them with + Seat in the IO Dock.""}")]
        [Tooltip("The vehicle's seats: anchor, driver flag, exit, occupant options and per-seat Signals. Add them with + Seat in the IO Dock.")]
        public readonly VariableHandle<Seat[]> Seats = (Seat[])null;

        [SchematicDoc(@"{""name"": ""Driver"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""GameObject"", ""default"": ""null"", ""description"": ""Who sits in the (first occupied) driver seat, or null. Read-only: set by getting in and out.""}")]
        [Tooltip("Who sits in the (first occupied) driver seat, or null. Read-only: set by getting in and out.")]
        public readonly VariableHandle<GameObject> Driver = (GameObject)null;

        [SchematicDoc(@"{""name"": ""HasDriver"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True while someone sits in a driver seat. Movement Behaviors only drive while this is on; graphs use it to decide whether to forward input.""}")]
        [Tooltip("True while someone sits in a driver seat. Movement Behaviors only drive while this is on.")]
        public readonly VariableHandle<bool> HasDriver = false;

        [SchematicDoc(@"{""name"": ""OccupantCount"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""int"", ""default"": ""0"", ""description"": ""How many seats are taken. Read-only.""}")]
        [Tooltip("How many seats are taken. Read-only.")]
        public readonly VariableHandle<int> OccupantCount = 0;

        [SchematicDoc(@"{""name"": ""IsOccupied"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True while anyone is in the vehicle. Read-only.""}")]
        [Tooltip("True while anyone is in the vehicle. Read-only.")]
        public readonly VariableHandle<bool> IsOccupied = false;

        [SchematicDoc(@"{""name"": ""MaxEnterDistance"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""float"", ""default"": ""0"", ""description"": ""How close (metres, to the seat) someone must be for the Enter Signals to seat them. 0 = any distance (e.g. when an interaction trigger already checked).""}")]
        [Tooltip("How close (metres, to the seat) someone must be for the Enter Signals to seat them. 0 = any distance.")]
        public readonly VariableHandle<float> MaxEnterDistance = 0f;

        [SchematicDoc(@"{""name"": ""DriverSeatFirst"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Enter (without a seat number) fills a free driver seat first. Off: passenger seats first, the driver seat last (e.g. a bus that the player shouldn't drive by accident).""}")]
        [Tooltip("Enter (without a seat number) fills a free driver seat first. Off: passenger seats first, the driver seat last.")]
        public readonly VariableHandle<bool> DriverSeatFirst = true;

        [SchematicDoc(@"{""name"": ""ExitBlockingLayers"", ""kind"": ""variable"", ""behavior"": ""Vehicle Seats"", ""type"": ""LayerMask"", ""default"": ""Everything"", ""description"": ""Layers that block the way out of a seat. If a seat's exit is blocked, the other side and then the roof are tried.""}")]
        [Tooltip("Layers that block the way out of a seat. If a seat's exit is blocked, the other side and then the roof are tried.")]
        public readonly VariableHandle<LayerMask> ExitBlockingLayers = (LayerMask)(-1);

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""Enter"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""input"", ""params"": [{""name"": ""Occupant"", ""type"": ""GameObject""}], ""description"": ""Seats the occupant in the first free seat (a driver seat first, if Driver Seat First). Does nothing if the vehicle is full or they're further than Max Enter Distance."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock), e.g. when the player interacts with the vehicle.""}")]
        [Tooltip("Seats the occupant in the first free seat (a driver seat first, if Driver Seat First).")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Occupant", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Enter;

        [SchematicDoc(@"{""name"": ""EnterSeat"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""input"", ""params"": [{""name"": ""Occupant"", ""type"": ""GameObject""}, {""name"": ""Seat"", ""type"": ""int""}], ""description"": ""Seats the occupant in a specific seat (its index in Seats), if it's free."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock). Each seat's own Enter Request does the same without the index.""}")]
        [Tooltip("Seats the occupant in a specific seat (its index in Seats), if it's free.")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Occupant", typeof(GameObject))]
        [Parameter("Seat", typeof(int))]
        public readonly SignalHandle<GameObject, int> EnterSeat;

        [SchematicDoc(@"{""name"": ""Exit"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""input"", ""params"": [{""name"": ""Occupant"", ""type"": ""GameObject""}], ""description"": ""Gets the occupant out, at their seat's exit."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock), e.g. on the player's interact button while seated.""}")]
        [Tooltip("Gets the occupant out, at their seat's exit.")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Occupant", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Exit;

        [SchematicDoc(@"{""name"": ""ExitAll"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""input"", ""description"": ""Gets everyone out (e.g. the vehicle is about to explode)."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [Tooltip("Gets everyone out.")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle ExitAll;

        [SchematicDoc(@"{""name"": ""SwitchSeat"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""input"", ""params"": [{""name"": ""Occupant"", ""type"": ""GameObject""}, {""name"": ""Seat"", ""type"": ""int""}], ""description"": ""Moves someone already in the vehicle to another free seat (sends their old seat's exit and the new seat's enter Signals)."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [Tooltip("Moves someone already in the vehicle to another free seat.")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Occupant", typeof(GameObject))]
        [Parameter("Seat", typeof(int))]
        public readonly SignalHandle<GameObject, int> SwitchSeat;

        [SchematicDoc(@"{""name"": ""OnOccupantEntered"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""output"", ""params"": [{""name"": ""Occupant"", ""type"": ""GameObject""}, {""name"": ""Seat"", ""type"": ""int""}], ""description"": ""Sent when anyone sits down, with the seat's index. For one seat only, use that seat's own On Entered."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when anyone sits down, with the seat's index.")]
        [OutputSignal]
        [Parameter("Occupant", typeof(GameObject))]
        [Parameter("Seat", typeof(int))]
        public readonly SignalHandle<GameObject, int> OnOccupantEntered;

        [SchematicDoc(@"{""name"": ""OnOccupantExited"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""output"", ""params"": [{""name"": ""Occupant"", ""type"": ""GameObject""}, {""name"": ""Seat"", ""type"": ""int""}], ""description"": ""Sent when anyone gets out, with the seat's index. The occupant can be null if it was destroyed while seated."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when anyone gets out, with the seat's index.")]
        [OutputSignal]
        [Parameter("Occupant", typeof(GameObject))]
        [Parameter("Seat", typeof(int))]
        public readonly SignalHandle<GameObject, int> OnOccupantExited;

        [SchematicDoc(@"{""name"": ""OnDriverChanged"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""output"", ""params"": [{""name"": ""Driver"", ""type"": ""GameObject""}], ""description"": ""Sent when the driver changes: the new driver, or null when nobody drives any more. A good place to switch input maps and cameras."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the driver changes: the new driver, or null when nobody drives any more.")]
        [OutputSignal]
        [Parameter("Driver", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnDriverChanged;

        [SchematicDoc(@"{""name"": ""OnFull"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""output"", ""description"": ""Sent when the last free seat is taken."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the last free seat is taken.")]
        [OutputSignal]
        public readonly SignalHandle OnFull;

        [SchematicDoc(@"{""name"": ""OnEmpty"", ""kind"": ""signal"", ""behavior"": ""Vehicle Seats"", ""direction"": ""output"", ""description"": ""Sent when the last occupant gets out."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the last occupant gets out.")]
        [OutputSignal]
        public readonly SignalHandle OnEmpty;

        #endregion

        #region C# API

        /// <summary>Raised for every vehicle when someone sits down: (vehicle, occupant, seat index).</summary>
        public static event Action<GameObject, GameObject, int> OccupantEntered;

        /// <summary>Raised for every vehicle when someone gets out: (vehicle, occupant, seat index).</summary>
        public static event Action<GameObject, GameObject, int> OccupantExited;

        /// <summary>
        /// Raised for every vehicle when its driver changes: (vehicle, new driver or null). The vehicle networking hands
        /// the vehicle's ownership to the driver from here.
        /// </summary>
        public static event Action<GameObject, GameObject> DriverChanged;

        // Who sits where, across every vehicle, so an occupant can be found without knowing its vehicle.
        private readonly struct SeatRef
        {
            public readonly BehaviorInstance Vehicle;
            public readonly int Seat;
            public SeatRef(BehaviorInstance vehicle, int seat) { Vehicle = vehicle; Seat = seat; }
        }

        private static readonly Dictionary<GameObject, SeatRef> _seated = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _seated.Clear();
            _drivenBy.Clear();
            OccupantEntered = null;
            OccupantExited = null;
            DriverChanged = null;
        }

        /// <summary>
        /// Puts <paramref name="occupant"/> in a seat of <paramref name="vehicle"/> (a Schematic Object with Vehicle
        /// Seats): the given seat index, or the first free one with -1. Returns false if it couldn't (no seats, full,
        /// taken). Someone already in another vehicle gets out of it first.
        /// </summary>
        public static bool TryEnter(GameObject vehicle, GameObject occupant, int seat = -1)
        {
            var instance = ExecutionContext.FindInstance<VehicleSeats>(vehicle);
            return instance != null && ((VehicleSeats)instance.Behavior).TryEnterSeat(instance, occupant, seat);
        }

        /// <summary>Gets <paramref name="occupant"/> out of whatever vehicle they're in. False if they weren't seated.</summary>
        public static bool ExitVehicle(GameObject occupant, bool placeAtExit = true)
        {
            if (occupant == null || !_seated.TryGetValue(occupant, out var seat) || seat.Vehicle == null)
                return false;
            return ((VehicleSeats)seat.Vehicle.Behavior).TryExitSeat(seat.Vehicle, occupant, placeAtExit);
        }

        /// <summary>The vehicle <paramref name="occupant"/> sits in, or null.</summary>
        public static GameObject VehicleOf(GameObject occupant)
            => occupant != null && _seated.TryGetValue(occupant, out var seat) && seat.Vehicle != null ? seat.Vehicle.GameObject : null;

        /// <summary>The index of the seat <paramref name="occupant"/> sits in, or -1.</summary>
        public static int SeatOf(GameObject occupant)
            => occupant != null && _seated.TryGetValue(occupant, out var seat) ? seat.Seat : -1;

        /// <summary>True while <paramref name="occupant"/> sits in any vehicle.</summary>
        public static bool IsSeated(GameObject occupant) => occupant != null && _seated.ContainsKey(occupant);

        /// <summary>True while <paramref name="occupant"/> sits in a driver seat.</summary>
        public static bool IsDriving(GameObject occupant)
        {
            if (occupant == null || !_seated.TryGetValue(occupant, out var seat) || seat.Vehicle == null || !seat.Vehicle.IsSpawned)
                return false;
            var seats = ((VehicleSeats)seat.Vehicle.Behavior).Seats[seat.Vehicle];
            return seats != null && seat.Seat < seats.Length && seats[seat.Seat] != null && seats[seat.Seat].IsDriver;
        }

        /// <summary>
        /// The driver of <paramref name="vehicle"/>, or null (also when it has no Vehicle Seats). Movement Behaviors of
        /// any kind of vehicle use this to decide whether to drive.
        /// </summary>
        public static GameObject DriverOf(GameObject vehicle)
        {
            var instance = ExecutionContext.FindInstance<VehicleSeats>(vehicle);
            return instance != null ? ((VehicleSeats)instance.Behavior).Driver[instance] : null;
        }

        /// <summary>True if <paramref name="vehicle"/> has Vehicle Seats at all (a vehicle without them is driven by graphs or AI only).</summary>
        public static bool HasSeats(GameObject vehicle)
            => ExecutionContext.FindInstance<VehicleSeats>(vehicle) != null;

        /// <summary>The occupant of a seat (by index) of this instance, or null.</summary>
        public GameObject OccupantOf(BehaviorInstance instance, int seat)
        {
            var seats = Seats[instance];
            return seats != null && seat >= 0 && seat < seats.Length && seats[seat] != null ? seats[seat].Occupant : null;
        }

        /// <summary>
        /// The first free seat: driver seats first if <paramref name="driverFirst"/>, otherwise passenger seats first
        /// and driver seats last. -1 if the vehicle is full.
        /// </summary>
        public int FindFreeSeat(BehaviorInstance instance, bool driverFirst)
        {
            var seats = Seats[instance];
            if (seats == null)
                return -1;
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantDriver = driverFirst ? pass == 0 : pass == 1;
                for (int i = 0; i < seats.Length; i++)
                    if (seats[i] != null && seats[i].IsDriver == wantDriver && seats[i].Occupant == null)
                        return i;
            }
            return -1;
        }

        /// <summary>
        /// Seats <paramref name="occupant"/> in this vehicle: in <paramref name="seatIndex"/>, or the first free seat
        /// with -1. With <paramref name="checkDistance"/>, Max Enter Distance applies (the Enter Signals use it).
        /// </summary>
        public bool TryEnterSeat(BehaviorInstance instance, GameObject occupant, int seatIndex = -1, bool checkDistance = false)
        {
            if (instance == null || !instance.IsSpawned || occupant == null || !DecidesSeating(instance))
                return false;
            // Nobody sits inside themselves (or inside something they're carrying).
            if (occupant == instance.GameObject || instance.Transform.IsChildOf(occupant.transform))
                return false;

            var seats = Seats[instance];
            if (seats == null || seats.Length == 0)
                return false;

            // Already in this vehicle: that's a seat switch (or nothing to do).
            if (_seated.TryGetValue(occupant, out var current) && current.Vehicle == instance)
                return seatIndex < 0 || seatIndex == current.Seat || TryMoveToSeat(instance, occupant, seatIndex);

            if (seatIndex < 0)
                seatIndex = FindFreeSeat(instance, DriverSeatFirst[instance]);
            if (seatIndex < 0 || seatIndex >= seats.Length || seats[seatIndex] == null || seats[seatIndex].Occupant != null)
                return false;

            if (checkDistance)
            {
                float max = MaxEnterDistance[instance];
                if (max > 0f && Vector3.Distance(occupant.transform.position, AnchorOf(instance, seats[seatIndex]).position) > max)
                    return false;
            }

            // In another vehicle: out of that one first (no need to place them at its exit).
            if (current.Vehicle != null)
                ReleaseFromCurrentSeat(occupant);

            Occupy(instance, seats, seatIndex, occupant);
            return true;
        }

        public bool TryEnterSeat(GameObject occupant, int seatIndex = -1) => TryEnterSeat(Selected, occupant, seatIndex);

        /// <summary>Gets <paramref name="occupant"/> out of this vehicle, at their seat's exit if <paramref name="placeAtExit"/>.</summary>
        public bool TryExitSeat(BehaviorInstance instance, GameObject occupant, bool placeAtExit = true)
        {
            if (instance == null || occupant == null || !_seated.TryGetValue(occupant, out var seat) || seat.Vehicle != instance || !DecidesSeating(instance))
                return false;
            var seats = Seats[instance];
            if (seats == null || seat.Seat >= seats.Length)
            {
                _seated.Remove(occupant);
                return false;
            }
            Release(instance, seats, seat.Seat, placeAtExit);
            return true;
        }

        public bool TryExitSeat(GameObject occupant) => TryExitSeat(Selected, occupant);

        /// <summary>Moves someone already in this vehicle to another free seat.</summary>
        public bool TryMoveToSeat(BehaviorInstance instance, GameObject occupant, int seatIndex)
        {
            if (instance == null || occupant == null || !_seated.TryGetValue(occupant, out var current) || current.Vehicle != instance || !DecidesSeating(instance))
                return false;
            var seats = Seats[instance];
            if (seats == null || seatIndex < 0 || seatIndex >= seats.Length || seats[seatIndex] == null)
                return false;
            if (seatIndex == current.Seat)
                return true;
            if (seats[seatIndex].Occupant != null)
                return false;

            Release(instance, seats, current.Seat, placeAtExit: false);
            Occupy(instance, seats, seatIndex, occupant);
            return true;
        }

        /// <summary>Gets everyone out of this vehicle.</summary>
        public void EmptyAllSeats(BehaviorInstance instance, bool placeAtExit = true)
        {
            if (instance != null && DecidesSeating(instance))
                ReleaseAll(instance, placeAtExit);
        }

        /// <summary>
        /// Whether this peer decides who sits in the vehicle: always offline; online, only the vehicle's authority (the
        /// C# API does nothing elsewhere, so every player keeps the same seating; send the Signals instead, which reach
        /// the authority).
        /// </summary>
        public static bool DecidesSeating(BehaviorInstance instance)
            => instance != null && SchematicNetwork.HasAuthority(instance.GameObject);

        // Everyone out, on this peer only (a despawning vehicle, or the authority's EmptyAllSeats).
        private void ReleaseAll(BehaviorInstance instance, bool placeAtExit)
        {
            if (instance == null || !instance.IsSpawned)
                return;
            var seats = Seats[instance];
            if (seats == null)
                return;
            for (int i = 0; i < seats.Length; i++)
                if (seats[i] != null && !ReferenceEquals(seats[i].Occupant, null))
                    Release(instance, seats, i, placeAtExit);
        }

        /// <summary>Where a seat puts its occupant: its anchor, or the vehicle itself if it has none.</summary>
        public static Transform AnchorOf(BehaviorInstance instance, Seat seat)
            => seat != null && seat.Anchor != null ? seat.Anchor : instance.Transform;

        #endregion

        #region Lifecycle

        protected override void OnSetup(BehaviorSetupContext context)
        {
            // One driver seat to start with, on a child of its own (more with + Seat in the IO Dock).
            var anchor = new GameObject("Seat_Driver").transform;
            anchor.SetParent(context.Transform, false);
            anchor.localPosition = new Vector3(-0.4f, 0.3f, 0.1f);
            context.Set(Seats, new[] { new Seat("Driver", anchor, isDriver: true) });
        }

        protected override void OnStart()
        {
            var instance = Self;
            // Requests: any player may send them (they're networked); only the authority acts on them, and the seating
            // it decides reaches everyone as state. TryEnterSeat and friends check that themselves.
            Enter.Subscribe(occupant => TryEnterSeat(instance, occupant, -1, checkDistance: true));
            EnterSeat.Subscribe((occupant, seat) => TryEnterSeat(instance, occupant, seat, checkDistance: true));
            Exit.Subscribe(occupant => TryExitSeat(instance, occupant));
            ExitAll.Subscribe(() => EmptyAllSeats(instance));
            SwitchSeat.Subscribe((occupant, seat) => TryMoveToSeat(instance, occupant, seat));

            var seats = Seats.Value;
            if (seats != null)
            {
                for (int i = 0; i < seats.Length; i++)
                {
                    var seat = seats[i];
                    if (seat == null) continue;
                    int index = i;
                    seat.EnterRequest.Subscribe<GameObject>(instance, occupant => TryEnterSeat(instance, occupant, index, checkDistance: true));
                    seat.ExitRequest.Subscribe(instance, () =>
                    {
                        var occupant = OccupantOf(instance, index);
                        if (occupant != null)
                            TryExitSeat(instance, occupant);
                    });
                }
            }

            RefreshSummary(instance, seats);
        }

        protected override void OnUpdate()
        {
            // An occupant that was destroyed or despawned while seated frees its seat.
            var seats = Seats.Value;
            if (seats == null)
                return;
            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seats[i];
                if (seat == null || ReferenceEquals(seat.Occupant, null))
                    continue;
                if (seat.Occupant == null || !seat.Occupant.activeSelf)
                    Release(Self, seats, i, placeAtExit: false);
            }
        }

        protected override void OnLateUpdate()
        {
            // Occupants that aren't parented to their seat follow it instead (on every peer, for networked occupants).
            var seats = Seats.Value;
            if (seats == null)
                return;
            foreach (var seat in seats)
            {
                if (seat == null || seat.Occupant == null || seat.State == null || seat.State.Attached)
                    continue;
                var anchor = AnchorOf(Self, seat);
                seat.Occupant.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            }
        }

        // Despawning or disabling the vehicle never takes its passengers with it (into its pool, or out of the world).
        // Every peer lets them out of its own copy (no need to wait for the authority's word on a vehicle that's gone).
        protected override void OnDisable() => ReleaseAll(Self, placeAtExit: true);

        #endregion

        #region Seating

        private void Occupy(BehaviorInstance instance, Seat[] seats, int index, GameObject occupant)
        {
            var seat = seats[index];
            var anchor = AnchorOf(instance, seat);
            var transform = occupant.transform;
            var state = new SeatState
            {
                PreviousParent = transform.parent,
                PreviousLocalScale = transform.localScale,
            };
            Vector3 worldScale = transform.lossyScale;

            // The body first, so it doesn't fight the move.
            var body = occupant.GetComponent<Box3DBody>();
            if (body != null)
            {
                state.Body = body;
                switch (seat.OccupantPhysics)
                {
                    case OccupantPhysicsMode.DisableBody:
                        state.BodyWasEnabled = body.enabled;
                        body.enabled = false;
                        break;
                    case OccupantPhysicsMode.Kinematic:
                        state.BodyTypeChanged = true;
                        state.PreviousBodyType = body.BodyType;
                        body.SetKinematic(true);
                        break;
                }
            }

            if (seat.DisableOccupantBehaviors)
            {
                var schematic = occupant.GetComponent<SchematicObject>();
                if (schematic != null)
                    foreach (var behavior in schematic.BehaviorInstances)
                        if (behavior.Enabled && behavior.IsSpawned)
                        {
                            behavior.Disable();
                            state.DisabledBehaviors.Add(behavior);
                        }
            }

            if (seat.HideOccupant)
                foreach (var renderer in occupant.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        state.HiddenRenderers.Add(renderer);
                    }

            // Networked occupants aren't re-parented: Netcode only allows that on the server (and between NetworkObjects),
            // and it would fight their NetworkTransform. They follow the anchor every frame instead (OnLateUpdate).
            state.Attached = seat.AttachOccupant && !SchematicNetwork.IsNetworked(occupant);
            if (state.Attached)
            {
                transform.SetParent(anchor, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                // Keep the size it had in the world, whatever the vehicle's scale.
                Vector3 anchorScale = anchor.lossyScale;
                transform.localScale = new Vector3(
                    SafeDivide(worldScale.x, anchorScale.x),
                    SafeDivide(worldScale.y, anchorScale.y),
                    SafeDivide(worldScale.z, anchorScale.z));
            }
            else
                transform.SetPositionAndRotation(anchor.position, anchor.rotation);

            if (!string.IsNullOrEmpty(seat.PoseParameter))
            {
                state.Animator = occupant.GetComponentInChildren<Animator>();
                SetPose(state.Animator, seat.PoseParameter, seat.PoseId);
            }

            seat.Occupant = occupant;
            seat.State = state;
            _seated[occupant] = new SeatRef(instance, index);

            bool driverChanged = RefreshSummary(instance, seats, out int before);
            bool wasFull = before == CountSeats(seats);

            SchematicNetwork.MarkStateChanged(instance);

            seat.OnEntered.Invoke(instance, occupant);
            OnOccupantEntered.Invoke(instance, occupant, index);
            if (driverChanged)
            {
                HandOverControl(instance.GameObject, Driver[instance]);
                OnDriverChanged.Invoke(instance, Driver[instance]);
                DriverChanged?.Invoke(instance.GameObject, Driver[instance]);
            }
            if (!wasFull && OccupantCount[instance] == CountSeats(seats))
                OnFull.Invoke(instance);
            OccupantEntered?.Invoke(instance.GameObject, occupant, index);
        }

        // Vehicles taken over by their driver's controller (see HandOverControl), so it's let go again when they leave.
        private static readonly Dictionary<GameObject, Controller> _drivenBy = new();

        /// <summary>
        /// Controller Possession: whoever controls the driver controls the vehicle too (as well as the driver), so their
        /// input reaches it; when the driver gets out (or someone else drives), it's let go again. A vehicle a
        /// controller possessed directly (an AI driving it with no character) is left alone.
        /// </summary>
        private static void HandOverControl(GameObject vehicle, GameObject driver)
        {
            var schematic = Possession.SchematicOf(vehicle);
            if (schematic == null)
                return;
            var driverController = driver != null ? Possession.PossessorOf(Possession.SchematicOf(driver)) : null;
            if (_drivenBy.TryGetValue(vehicle, out var previous))
            {
                _drivenBy.Remove(vehicle);
                if (previous != null && previous != driverController && Possession.PossessorOf(schematic) == previous)
                    Possession.Unpossess(schematic);
            }
            if (driverController != null && driverController.Possess(vehicle))
                _drivenBy[vehicle] = driverController;
        }

        private void Release(BehaviorInstance instance, Seat[] seats, int index, bool placeAtExit)
        {
            var seat = seats[index];
            var occupant = seat.Occupant;
            var state = seat.State;
            seat.Occupant = null;
            seat.State = null;
            if (!ReferenceEquals(occupant, null))
                _seated.Remove(occupant);

            if (occupant != null && state != null)
            {
                var transform = occupant.transform;
                Pose exit = placeAtExit ? FindExit(instance, seat) : default;

                if (state.Attached && transform.parent == AnchorOf(instance, seat))
                {
                    transform.SetParent(state.PreviousParent, true);
                    transform.localScale = state.PreviousLocalScale;
                }
                if (placeAtExit)
                    transform.SetPositionAndRotation(exit.position, exit.rotation);

                foreach (var renderer in state.HiddenRenderers)
                    if (renderer != null)
                        renderer.enabled = true;

                if (state.Animator != null)
                    SetPose(state.Animator, seat.PoseParameter, 0);

                var body = state.Body;
                if (body != null)
                {
                    if (seat.OccupantPhysics == OccupantPhysicsMode.DisableBody)
                        body.enabled = state.BodyWasEnabled; // its OnEnable moves the native body to the Transform
                    else if (state.BodyTypeChanged && !body.IsNetworkProxy())
                    {
                        body.BodyType = state.PreviousBodyType;
                        if (body.BodyType == Box3DBodyType.Dynamic)
                        {
                            // A body that just stopped following its Transform starts where the Transform is.
                            body.Position = transform.position;
                            body.Rotation = transform.rotation;
                        }
                    }

                    if (placeAtExit && seat.InheritVehicleVelocity && body.enabled && body.BodyType == Box3DBodyType.Dynamic)
                    {
                        var vehicleBody = instance.GetComponent<Box3DBody>();
                        if (vehicleBody != null)
                            body.SetVelocity(vehicleBody.GetVelocity());
                    }
                }

                // Last, so movement Behaviors wake up already standing at the exit.
                foreach (var behavior in state.DisabledBehaviors)
                    if (behavior != null && behavior.IsSpawned)
                        behavior.Enable();
            }

            if (!instance.IsSpawned)
                return;

            bool driverChanged = RefreshSummary(instance, seats, out _);
            SchematicNetwork.MarkStateChanged(instance);
            seat.OnExited.Invoke(instance, occupant);
            OnOccupantExited.Invoke(instance, occupant, index);
            if (driverChanged)
            {
                HandOverControl(instance.GameObject, Driver[instance]);
                OnDriverChanged.Invoke(instance, Driver[instance]);
                DriverChanged?.Invoke(instance.GameObject, Driver[instance]);
            }
            if (OccupantCount[instance] == 0)
                OnEmpty.Invoke(instance);
            OccupantExited?.Invoke(instance.GameObject, occupant, index);
        }

        /// <summary>
        /// Where a seat lets its occupant out: its Exit Point, or Exit Offset from the anchor. If something blocks the
        /// way (a wall, another car), the mirrored side is tried, then the roof. Faces the vehicle's heading, upright.
        /// </summary>
        public Pose FindExit(BehaviorInstance instance, Seat seat)
        {
            var anchor = AnchorOf(instance, seat);
            var vehicleBody = instance.GetComponent<Box3DBody>();
            LayerMask mask = ExitBlockingLayers[instance];
            Vector3 from = anchor.position;

            Vector3 heading = Vector3.ProjectOnPlane(instance.Transform.forward, Vector3.up);
            Quaternion rotation = heading.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(heading, Vector3.up) : Quaternion.identity;

            if (seat.ExitPoint != null)
            {
                Vector3 exitPoint = seat.ExitPoint.position;
                if (!Blocked(from, exitPoint, mask, vehicleBody))
                    return new Pose(exitPoint, seat.ExitPoint.rotation);
            }
            else
            {
                Vector3 target = ExitTarget(anchor, seat.ExitOffset);
                if (!Blocked(from, target, mask, vehicleBody))
                    return new Pose(target, rotation);
            }

            // The other side.
            Vector3 mirrored = ExitTarget(anchor, Vector3.Scale(seat.ExitPoint != null ? anchor.InverseTransformDirection(seat.ExitPoint.position - from) : seat.ExitOffset, new Vector3(-1f, 1f, 1f)));
            if (!Blocked(from, mirrored, mask, vehicleBody))
                return new Pose(mirrored, rotation);

            // Out through the roof (always world up, even for a vehicle lying on its side).
            return new Pose(instance.Transform.position + Vector3.up * RoofExitHeight, rotation);
        }

        /// <summary>How far above the vehicle's origin someone climbs out when both sides are blocked.</summary>
        public const float RoofExitHeight = 2.5f;

        /// <summary>The unchecked exit position of a seat (used by gizmos too).</summary>
        public static Vector3 ExitTarget(Transform anchor, Vector3 offset) => anchor.position + anchor.rotation * offset;

        private static bool Blocked(Vector3 from, Vector3 to, LayerMask mask, Box3DBody ignore)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.01f)
                return false;
            // A little extra, so the occupant doesn't end up with their nose in the wall.
            return SchematicPhysics.Raycast(from, delta / distance, out _, distance + 0.3f, mask, ignore);
        }

        // Updates Driver / HasDriver / OccupantCount / IsOccupied. Returns true if the driver changed.
        private bool RefreshSummary(BehaviorInstance instance, Seat[] seats) => RefreshSummary(instance, seats, out _);

        private bool RefreshSummary(BehaviorInstance instance, Seat[] seats, out int previousCount)
        {
            GameObject driver = null;
            int count = 0;
            if (seats != null)
                foreach (var seat in seats)
                {
                    if (seat == null || seat.Occupant == null) continue;
                    count++;
                    if (seat.IsDriver && driver == null)
                        driver = seat.Occupant;
                }

            previousCount = OccupantCount[instance];
            var previousDriver = Driver[instance];
            Driver[instance] = driver;
            HasDriver[instance] = driver != null;
            OccupantCount[instance] = count;
            IsOccupied[instance] = count > 0;
            return previousDriver != driver;
        }

        private static int CountSeats(Seat[] seats)
        {
            int count = 0;
            if (seats != null)
                foreach (var seat in seats)
                    if (seat != null) count++;
            return count;
        }

        private static void SetPose(Animator animator, string parameter, int value)
        {
            if (animator == null || string.IsNullOrEmpty(parameter) || animator.runtimeAnimatorController == null)
                return;
            foreach (var p in animator.parameters)
                if (p.name == parameter && p.type == AnimatorControllerParameterType.Int)
                {
                    animator.SetInteger(parameter, value);
                    return;
                }
        }

        private static float SafeDivide(float a, float b) => Mathf.Abs(b) > 1e-5f ? a / b : a;

        #endregion

        #region Networking

        // Who sits in each seat, by index (null for an empty seat). Occupants travel as their NetworkObject, so they
        // must be networked objects (players usually are); anyone else arrives as nobody.
        void INetworkStateBehavior.WriteNetworkState(BehaviorInstance instance, List<Union> state)
        {
            var seats = Seats[instance];
            if (seats == null)
                return;
            foreach (var seat in seats)
                state.Add(seat != null && seat.Occupant != null ? new Union(seat.Occupant) : Union.Null);
        }

        // Seats the people the authority seated (and lets out the ones it didn't), firing the usual Signals locally.
        void INetworkStateBehavior.ReadNetworkState(BehaviorInstance instance, Union[] state)
        {
            var seats = Seats[instance];
            if (seats == null || state == null || !instance.IsSpawned)
                return;
            int count = Mathf.Min(seats.Length, state.Length);
            var wanted = new GameObject[count];
            for (int i = 0; i < count; i++)
                wanted[i] = state[i].Type == Union.ValueType.Null ? null : SignalArgs.FromUnion<GameObject>(state[i]);

            // Out first, so seat switches find their new seat free.
            for (int i = 0; i < count; i++)
                if (seats[i] != null && !ReferenceEquals(seats[i].Occupant, null) && seats[i].Occupant != wanted[i])
                    Release(instance, seats, i, placeAtExit: !IsInList(wanted, seats[i].Occupant));

            for (int i = 0; i < count; i++)
            {
                var occupant = wanted[i];
                if (occupant == null || seats[i] == null || seats[i].Occupant == occupant)
                    continue;
                // Still in another seat here (another vehicle, or a seat switch the other way round): out of it first.
                ReleaseFromCurrentSeat(occupant);
                if (seats[i].Occupant == null)
                    Occupy(instance, seats, i, occupant);
            }
        }

        // Out of whatever seat they're in on this peer, without asking for authority (the caller already decided).
        private static void ReleaseFromCurrentSeat(GameObject occupant)
        {
            if (occupant == null || !_seated.TryGetValue(occupant, out var current) || current.Vehicle == null)
                return;
            var vehicle = current.Vehicle;
            var behavior = (VehicleSeats)vehicle.Behavior;
            var seats = vehicle.IsSpawned ? behavior.Seats[vehicle] : null;
            if (seats != null && current.Seat < seats.Length && seats[current.Seat] != null && seats[current.Seat].Occupant == occupant)
                behavior.Release(vehicle, seats, current.Seat, placeAtExit: false);
            else
                _seated.Remove(occupant);
        }

        private static bool IsInList(GameObject[] list, GameObject item)
        {
            foreach (var entry in list)
                if (entry == item)
                    return true;
            return false;
        }

        #endregion

        #region Gizmos

        /// <summary>Each seat's colour in gizmos and the IO Dock, so they're easy to tell apart.</summary>
        public static Color SeatColor(int index) => Color.HSVToRGB((0.55f + index * 0.17f) % 1f, 0.75f, 1f);

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var seats = context.Get(Seats);
            if (seats == null)
                return;

            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seats[i];
                if (seat == null) continue;
                var anchor = seat.Anchor != null ? seat.Anchor : context.Transform;
                Vector3 position = anchor.position;
                var color = SeatColor(i);

                Gizmos.color = color;
                Gizmos.DrawWireSphere(position, 0.22f);
                Gizmos.DrawRay(position, anchor.forward * 0.45f);
                if (seat.Occupant != null)
                    Gizmos.DrawSphere(position, 0.12f);

                if (seat.IsDriver)
                {
                    // A little steering wheel in front of the driver.
                    Vector3 wheel = position + anchor.forward * 0.4f + anchor.up * 0.25f;
                    DrawCircle(wheel, anchor.forward, 0.14f);
                }

                Vector3 exit = seat.ExitPoint != null ? seat.ExitPoint.position : ExitTarget(anchor, seat.ExitOffset);
                Gizmos.color = new Color(color.r, color.g, color.b, 0.6f);
                Gizmos.DrawLine(position, exit);
                Gizmos.DrawWireCube(exit, new Vector3(0.3f, 0.02f, 0.3f));

#if UNITY_EDITOR
                var label = new GUIStyle(UnityEditor.EditorStyles.miniBoldLabel) { normal = { textColor = color } };
                string text = $"{i}: {seat.Name}{(seat.IsDriver ? " (Driver)" : "")}";
                if (seat.Occupant != null)
                    text += $"\n{seat.Occupant.name}";
                UnityEditor.Handles.Label(position + anchor.up * 0.35f, text, label);
#endif
            }
        }

        private static void DrawCircle(Vector3 center, Vector3 normal, float radius)
        {
            Vector3 a = Vector3.Cross(normal, Vector3.up);
            if (a.sqrMagnitude < 0.001f) a = Vector3.Cross(normal, Vector3.right);
            a = a.normalized * radius;
            Vector3 b = Vector3.Cross(normal.normalized, a);
            const int segments = 16;
            Vector3 previous = center + a;
            for (int s = 1; s <= segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                Vector3 next = center + a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        #endregion
    }
}
