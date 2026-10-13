using System;
using System.Collections.Generic;
using Box3D.Hybrid;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>
    /// What happens to the occupant's own Box3D body while it sits in a seat.
    /// </summary>
    public enum OccupantPhysicsMode
    {
        /// <summary>The body leaves the simulation until the occupant gets out. Best for characters: their collider
        /// can't shove the vehicle from the inside, and nothing can knock them out of the seat.</summary>
        DisableBody,
        /// <summary>The body turns kinematic and rides along (it still blocks other things, e.g. a turret gunner that
        /// can be hit). Its previous type comes back on exit. Keep its collider clear of the vehicle's own shapes.</summary>
        Kinematic,
        /// <summary>Hands off: the seat doesn't touch the occupant's body at all.</summary>
        LeaveAlone,
    }

    /// <summary>
    /// One place to sit in a vehicle: where the occupant goes, whether they drive, where they get out, and the Signals
    /// sent when someone gets in or out. Seats are shared by every kind of vehicle (car, boat, plane, mech, horse...)
    /// through <see cref="VehicleSeats"/>. Create them with "+ Seat" in the IO Dock, which also makes the seat's
    /// child anchor and its Signals for you.
    /// </summary>
    [Serializable]
    public class Seat
    {
        [Tooltip("The seat's name, shown in the IO Dock, the Scene view and on its Signals (e.g. \"Driver\", \"Gunner\").")]
        public string Name = "Seat";

        [Tooltip("Where the occupant sits: they're placed (and, with Attach Occupant, parented) here with no offset, facing its forward. Usually a child of the vehicle.")]
        public Transform Anchor;

        [Tooltip("Whoever sits here controls the vehicle: they become the Driver, and HasDriver turns on. A vehicle can have more than one driver seat (e.g. a pilot and a co-pilot); the first occupied one wins.")]
        public bool IsDriver;

        [Header("Getting Out")]
        [Tooltip("Where the occupant is placed when they get out. Optional: without it, Exit Offset is used.")]
        public Transform ExitPoint;

        [Tooltip("Used when there's no Exit Point: an offset from the anchor, in the anchor's space (x is to its right). If the way out is blocked, the other side and then the roof are tried.")]
        public Vector3 ExitOffset = new Vector3(-1.5f, 0f, 0f);

        [Tooltip("The occupant leaves with the vehicle's velocity (bailing out of a moving car keeps you moving).")]
        public bool InheritVehicleVelocity = true;

        [Header("Occupant")]
        [Tooltip("Parent the occupant to the anchor so it moves with the vehicle for free. Off: the occupant stays where it is in the hierarchy and is moved onto the anchor every frame instead (for objects that mustn't be re-parented).")]
        public bool AttachOccupant = true;

        [Tooltip("What happens to the occupant's Box3D body while seated.")]
        public OccupantPhysicsMode OccupantPhysics = OccupantPhysicsMode.DisableBody;

        [Tooltip("Turns off the occupant's Schematic Behaviors (its movement controller, mostly) while seated, and back on when it gets out. Its Schematic Graph keeps running, so it can still send input to the vehicle.")]
        public bool DisableOccupantBehaviors = true;

        [Tooltip("Hides the occupant's renderers while seated (closed cockpits, a rider inside a mech).")]
        public bool HideOccupant;

        [Header("Animation")]
        [Tooltip("A pose for the occupant's animation: sent to its Animator's Pose Parameter, and readable from graphs (e.g. 1 = drive, 2 = sit, 3 = man the gun).")]
        public int PoseId = 1;

        [Tooltip("An int parameter on the occupant's Animator that gets Pose Id on entering and 0 on leaving. Leave empty to drive the animation from a graph instead.")]
        public string PoseParameter = "";

        [Header("Signals")]
        [Tooltip("Sent to this vehicle when someone sits here (Occupant: GameObject). Created by \"+ Seat\" in the IO Dock; drag it into a graph to react.")]
        public SignalData OnEntered;

        [Tooltip("Sent to this vehicle when someone leaves this seat (Occupant: GameObject).")]
        public SignalData OnExited;

        [Tooltip("Send this to the vehicle to put someone in this seat (Occupant: GameObject), e.g. from a door's interaction.")]
        public SignalData EnterRequest;

        [Tooltip("Send this to the vehicle to make whoever sits here get out.")]
        public SignalData ExitRequest;

        /// <summary>Who's sitting here right now (runtime only).</summary>
        [NonSerialized] public GameObject Occupant;

        // What the seat changed on the occupant, so it can all be put back (runtime only).
        [NonSerialized] internal SeatState State;

        /// <summary>True while nobody sits here.</summary>
        public bool IsFree => Occupant == null;

        public Seat() { }

        public Seat(string name, Transform anchor, bool isDriver = false)
        {
            Name = name;
            Anchor = anchor;
            IsDriver = isDriver;
        }
    }

    /// <summary>
    /// Everything a seat changed on its occupant, put back when they get out.
    /// </summary>
    internal sealed class SeatState
    {
        public Transform PreviousParent;
        public Vector3 PreviousLocalScale;
        public Box3DBody Body;
        public bool BodyWasEnabled;
        public bool BodyTypeChanged;
        public Box3DBodyType PreviousBodyType;
        public readonly List<BehaviorInstance> DisabledBehaviors = new();
        public readonly List<Renderer> HiddenRenderers = new();
        public Animator Animator;
    }
}
