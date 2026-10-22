using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Box3D;
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Schematics;
using Remedy.Surfaces;
using SurfaceMaterial = Remedy.Surfaces.SurfaceMaterial;
using Unity.Mathematics;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>
    /// An arcade car on a real Box3D body: Need for Speed Underground / Mario Kart handling (easy drifts, mini-turbos,
    /// a car that wants to stay on its wheels) while crashes, ramps and rollovers are still physics.
    /// </summary>
    /// <remarks>
    /// The wheels are virtual tires: every physics step each one sweeps a tire-sized sphere down from its anchor and
    /// pushes the body with a spring, sideways grip and the engine, at that wheel. Drifting is a little state machine
    /// on top (less rear grip, a steer assist that swings the tail, a counter-steer that stops spin-outs, and speed
    /// carried round the slide). Staying upright is a set of dials, all in the Properties: a lowered centre of mass,
    /// where the tyres push
    /// (<see cref="ArcadeCarProperties.TyreForceHeight"/>), ground stabilizing, air levelling, and self-righting.
    /// Turn them all to 0 and you have a car that rolls like a real one.<br/>
    /// Works with or without <see cref="VehicleSeats"/>: with seats it only drives while someone's in a driver seat
    /// (or with Always Active, for AI); without, whatever sends its input Signals drives it.
    /// Networked: one peer simulates it (the driver, who becomes its owner, when its NetworkTransform is set to owner
    /// authority, which Network Sync does for vehicles; otherwise the server) and everyone else gets a proxy that
    /// follows its synced Transform, with wheels that still spin and steer. The input Signals are networked from the
    /// owner (so other players' keys can't steer it), Driver Inputs are mirrored to everyone a few times a second, and
    /// the drift / boost / flip / crash Signals are sent by whoever simulates (they're networked with Anyone as sender:
    /// only the simulating peer ever fires them, whether that's the driver or the server), so effects play for every
    /// player (proxies
    /// also update Is Drifting, Drift Level, Is Boosting and Is Flipped from them).
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Arcade Car"", ""kind"": ""behavior"", ""menu"": ""Vehicles/Arcade Car"", ""description"": ""An arcade car on a real Box3D body: NFS Underground / Mario Kart handling (drifts, mini-turbos, a car that wants to stay on its wheels) while crashes and rollovers are still physics. Tire-sized sphere probes; everything tunable in its Properties asset."", ""variables"": [""Properties"", ""Wheels"", ""AlwaysActive"", ""ParkWhenEmpty"", ""DriverInputs"", ""PlayerDriverOnly"", ""Speed"", ""ForwardSpeed"", ""IsGrounded"", ""GroundedWheels"", ""IsDrifting"", ""DriftAngle"", ""DriftCharge"", ""DriftLevel"", ""IsBoosting"", ""IsFlipped"", ""AirTime"", ""RollSave"", ""LoadTransfer"", ""BodyVisual"", ""IsJumping""], ""signals"": [""Target Speed"", ""Brake"", ""Steer"", ""Drive"", ""Handbrake"", ""Boost"", ""Unflip"", ""OnDriftStarted"", ""OnDriftEnded"", ""OnDriftLevel"", ""OnBoost"", ""OnLanded"", ""OnFlipped"", ""OnRighted"", ""OnCrash"", ""OnBounce"", ""OnJump""], ""use_for"": [""drivable cars and karts"", ""racing games"", ""AI traffic""], ""notes"": ""Needs a Box3D Body and a shape (added for you). Add Vehicle Seats so it only drives with a driver. Multiplayer: the driver owns and simulates the car; its input Signals are networked from the owner, and drift/boost/flip/crash Signals reach every player for effects.""}")]
    [SchematicBehaviorPath("Vehicles/Arcade Car")]
    [NetworkOwnerSimulated]
    [RequireComponent(typeof(Box3DBody), typeof(Box3DBoxShape), typeof(SchematicImpactRelay))]
    public class ArcadeCar : SchematicBehavior
    {
        #region Variables

        [SchematicDoc(@"{""name"": ""Properties"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""ArcadeCarProperties"", ""default"": ""null"", ""description"": ""The car's tuning: suspension, engine, steering, grip, drift, mini-turbo and staying upright. Created for each car by the IO Dock; right-click its header for presets (Street Racer, Kart, Physical).""}")]
        [Tooltip("The car's tuning: suspension, engine, steering, grip, drift, mini-turbo and staying upright.")]
        [SchematicProperties]
        public readonly VariableHandle<ArcadeCarProperties> Properties = (ArcadeCarProperties)null;

        [SchematicDoc(@"{""name"": ""Wheels"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""Wheel[]"", ""default"": ""null"", ""description"": ""The wheels: anchor (top of the suspension), optional visual, radius, and whether each steers, drives and has the handbrake. Four are created when the Behavior is added.""}")]
        [Tooltip("The wheels: anchor (top of the suspension), optional visual, radius, and whether each steers, drives and has the handbrake.")]
        public readonly VariableHandle<Wheel[]> Wheels = (Wheel[])null;

        [SchematicDoc(@"{""name"": ""BodyVisual"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""The car's body mesh (a child, not the car itself): it wobbles and squashes when the car bounces off something (Properties' Body Wobble). Only the look: the physics don't move. None: no wobble.""}")]
        [Tooltip("The car's body mesh (a child): it wobbles and squashes on a bounce. Only the look. None: no wobble.")]
        public readonly VariableHandle<Transform> BodyVisual = (Transform)null;

        [SchematicDoc(@"{""name"": ""AlwaysActive"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Drive even with nobody in the driver seat (AI cars, testing). Without Vehicle Seats the car is always active.""}")]
        [Tooltip("Drive even with nobody in the driver seat (AI cars, testing). Without Vehicle Seats the car is always active.")]
        public readonly VariableHandle<bool> AlwaysActive = false;

        [SchematicDoc(@"{""name"": ""ParkWhenEmpty"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Brakes hold the car still while nobody drives it, so a parked car doesn't roll away.""}")]
        [Tooltip("Brakes hold the car still while nobody drives it, so a parked car doesn't roll away.")]
        public readonly VariableHandle<bool> ParkWhenEmpty = true;

        [SchematicDoc(@"{""name"": ""DriverInputs"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""VehicleInputBinding[]"", ""default"": ""null"", ""description"": ""Input Signals the car listens to itself while the player drives (like the Weapon Holder's Weapon Inputs): pick a Signal (e.g. an Input Map's) and what it does. Optional: a graph can send Target Speed/Steer/... instead.""}")]
        [Tooltip("Input Signals the car listens to itself while the player drives: pick a Signal (e.g. an Input Map's) and what it does.")]
        public readonly VariableHandle<VehicleInputBinding[]> DriverInputs = (VehicleInputBinding[])null;

        [SchematicDoc(@"{""name"": ""PlayerDriverOnly"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Driver Inputs only count while the driver is tagged as the player, so AI-driven cars ignore the player's keys. Off: any driver.""}")]
        [Tooltip("Driver Inputs only count while the driver is tagged as the player, so AI-driven cars ignore the player's keys.")]
        public readonly VariableHandle<bool> PlayerDriverOnly = true;

        [SchematicDoc(@"{""name"": ""Speed"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""How fast the car moves, in km/h (any direction). For speedometers. Read-only.""}")]
        [Tooltip("How fast the car moves, in km/h (any direction). Read-only.")]
        public readonly VariableHandle<float> Speed = 0f;

        [SchematicDoc(@"{""name"": ""ForwardSpeed"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Speed along the car's nose in m/s: negative while reversing. Read-only.""}")]
        [Tooltip("Speed along the car's nose in m/s: negative while reversing. Read-only.")]
        public readonly VariableHandle<float> ForwardSpeed = 0f;

        [SchematicDoc(@"{""name"": ""IsGrounded"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True while any wheel touches the ground. Read-only.""}")]
        [Tooltip("True while any wheel touches the ground. Read-only.")]
        public readonly VariableHandle<bool> IsGrounded = false;

        [SchematicDoc(@"{""name"": ""GroundedWheels"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""int"", ""default"": ""0"", ""description"": ""How many wheels touch the ground (2 on one side = up on two wheels). Read-only.""}")]
        [Tooltip("How many wheels touch the ground. Read-only.")]
        public readonly VariableHandle<int> GroundedWheels = 0;

        [SchematicDoc(@"{""name"": ""IsDrifting"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True during a drift. Read-only.""}")]
        [Tooltip("True during a drift. Read-only.")]
        public readonly VariableHandle<bool> IsDrifting = false;

        [SchematicDoc(@"{""name"": ""DriftAngle"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""The slide angle in degrees between where the car points and where it goes (positive: sliding to its right). For drift scores and tyre smoke. Read-only.""}")]
        [Tooltip("The slide angle in degrees between where the car points and where it goes. Read-only.")]
        public readonly VariableHandle<float> DriftAngle = 0f;

        [SchematicDoc(@"{""name"": ""DriftCharge"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Mini-turbo charge built up in the current drift. Read-only.""}")]
        [Tooltip("Mini-turbo charge built up in the current drift. Read-only.")]
        public readonly VariableHandle<float> DriftCharge = 0f;

        [SchematicDoc(@"{""name"": ""DriftLevel"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""int"", ""default"": ""0"", ""description"": ""The mini-turbo level the current drift has reached (0 = none yet). Its colour is in the Properties' Turbo Levels. Read-only.""}")]
        [Tooltip("The mini-turbo level the current drift has reached (0 = none yet). Read-only.")]
        public readonly VariableHandle<int> DriftLevel = 0;

        [SchematicDoc(@"{""name"": ""IsBoosting"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True while a Boost or mini-turbo pushes the car. Read-only.""}")]
        [Tooltip("True while a Boost or mini-turbo pushes the car. Read-only.")]
        public readonly VariableHandle<bool> IsBoosting = false;

        [SchematicDoc(@"{""name"": ""IsFlipped"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True while the car lies on its side or roof. Read-only.""}")]
        [Tooltip("True while the car lies on its side or roof. Read-only.")]
        public readonly VariableHandle<bool> IsFlipped = false;

        [SchematicDoc(@"{""name"": ""IsJumping"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True during a jump (off a ramp, a crest or a ledge: not a bump), from takeoff until the tyres are back down. Read-only.""}")]
        [Tooltip("True during a jump (not a bump), until the tyres are back down. Read-only.")]
        public readonly VariableHandle<bool> IsJumping = false;

        [SchematicDoc(@"{""name"": ""AirTime"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Seconds since all four wheels left the ground (0 on the ground). Read-only.""}")]
        [Tooltip("Seconds since all wheels left the ground (0 on the ground). Read-only.")]
        public readonly VariableHandle<float> AirTime = 0f;

        [SchematicDoc(@"{""name"": ""RollSave"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""-1"", ""description"": ""The roll save, -1 to 1: climbs while the car is in a big turn (or tipping), falls back otherwise. Past 0 it pushes the car back onto its wheels, harder the higher it gets. For tyre squeal or a wobble effect. Read-only.""}")]
        [Tooltip("The roll save, -1 to 1: climbs in a big turn, and past 0 pushes the car back onto its wheels. Read-only.")]
        public readonly VariableHandle<float> RollSave = -1f;

        [SchematicDoc(@"{""name"": ""LoadTransfer"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""How unevenly the car's weight sits across it, -1 to 1: 0 evenly (whatever the slope), toward 1 all on the right tyres (the left about to lift), toward -1 all on the left. Past Rollover Threshold the car calms its slide and its grip stops tipping it. For tyre squeal or a tipping warning. Read-only.""}")]
        [Tooltip("How unevenly the weight sits across the car, -1 (all on the left tyres) to 1 (all on the right). Read-only.")]
        public readonly VariableHandle<float> LoadTransfer = 0f;

        // Everything else that changes per step, in one place per spawned car.
        private readonly VariableHandle<CarState> _state = (CarState)null;

        // The Vehicle Seats on the same GameObject, if any.
        private readonly BehaviorHandle<VehicleSeats> _seats;

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""Target Speed"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""params"": [{""name"": ""Speed"", ""type"": ""float""}], ""description"": ""Desired signed speed as a fraction of the car's maximum in that direction: 1 is full speed forward, -1 is braking and then full reverse, and 0 (nothing held) does nothing: no engine, no brakes, the car just rolls. It drives up to a positive target and never brakes down to one (past it the engine stops and the car coasts). A negative one brakes, harder the further it's pushed, while the car still rolls forward, then reverses once it's stopped. Values are clamped to -1..1 and hold until changed."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock), e.g. from an input or an AI graph.""}")]
        [Tooltip("Desired signed speed: 1 is full speed forward, -1 brakes then reverses, 0 (nothing held) does nothing (the car rolls). It never brakes down to a positive target, it coasts. Clamped to -1..1.")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        [Parameter("Speed", typeof(float))]
        public readonly SignalHandle<float> TargetSpeed;

        [SchematicDoc(@"{""name"": ""Brake"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""params"": [{""name"": ""Amount"", ""type"": ""float""}], ""description"": ""Independent brake override, 0..1. Use a negative Target Speed to reverse."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [Tooltip("Independent brake override, 0..1. Use a negative Target Speed to reverse.")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        [Parameter("Amount", typeof(float))]
        public readonly SignalHandle<float> Brake;

        [SchematicDoc(@"{""name"": ""Steer"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""params"": [{""name"": ""Amount"", ""type"": ""float""}], ""description"": ""Steer, -1 (left) to 1 (right)."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [Tooltip("Steer, -1 (left) to 1 (right).")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        [Parameter("Amount", typeof(float))]
        public readonly SignalHandle<float> Steer;

        [SchematicDoc(@"{""name"": ""Drive"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""params"": [{""name"": ""Stick"", ""type"": ""Vector2""}], ""description"": ""All of it from one stick: x steers, y throttles (up) or brakes/reverses (down)."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock), e.g. straight from a Move input.""}")]
        [Tooltip("All of it from one stick: x steers, y throttles (up) or brakes/reverses (down).")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        [Parameter("Stick", typeof(Vector2))]
        public readonly SignalHandle<Vector2> Drive;

        [SchematicDoc(@"{""name"": ""Handbrake"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""params"": [{""name"": ""Held"", ""type"": ""bool""}], ""description"": ""Handbrake / drift button, held. With steering at speed it starts a drift."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock): true when pressed, false when released.""}")]
        [Tooltip("Handbrake / drift button, held. With steering at speed it starts a drift.")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        [Parameter("Held", typeof(bool))]
        public readonly SignalHandle<bool> Handbrake;

        [SchematicDoc(@"{""name"": ""Boost"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""description"": ""Fires a boost (nitro, a boost pad, a mushroom) using the Properties' Boost settings. Graphs keep track of how many the player has."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [Tooltip("Fires a boost using the Properties' Boost settings.")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        public readonly SignalHandle Boost;

        [SchematicDoc(@"{""name"": ""Unflip"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""description"": ""Turns a flipped car back onto its wheels now (a 'reset car' button), even if self-righting is off."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [Tooltip("Turns a flipped car back onto its wheels now, even if self-righting is off.")]
        [InputSignal, NetworkSync(SignalSender.OwnerToAuthority)]
        public readonly SignalHandle Unflip;

        [SchematicDoc(@"{""name"": ""OnDriftStarted"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""description"": ""Sent when a drift starts: tyre smoke, sparks, a screech."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when a drift starts.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle OnDriftStarted;

        [SchematicDoc(@"{""name"": ""OnDriftEnded"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Charge"", ""type"": ""float""}], ""description"": ""Sent when a drift ends, with the charge it built up (for drift scores)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when a drift ends, with the charge it built up.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Charge", typeof(float))]
        public readonly SignalHandle<float> OnDriftEnded;

        [SchematicDoc(@"{""name"": ""OnDriftLevel"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Level"", ""type"": ""int""}], ""description"": ""Sent when a drift's charge reaches the next mini-turbo level (change the spark colour)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when a drift's charge reaches the next mini-turbo level.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Level", typeof(int))]
        public readonly SignalHandle<int> OnDriftLevel;

        [SchematicDoc(@"{""name"": ""OnBoost"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Level"", ""type"": ""int""}], ""description"": ""Sent when a boost starts: the mini-turbo level, or 0 for a Boost input."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when a boost starts: the mini-turbo level, or 0 for a Boost input.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Level", typeof(int))]
        public readonly SignalHandle<int> OnBoost;

        [SchematicDoc(@"{""name"": ""OnLanded"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Air Time"", ""type"": ""float""}], ""description"": ""Sent when the car lands after a jump, with how long it was in the air (seconds)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the car lands after a jump, with how long it was in the air.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Air Time", typeof(float))]
        public readonly SignalHandle<float> OnLanded;

        [SchematicDoc(@"{""name"": ""OnFlipped"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""description"": ""Sent when the car ends up on its side or roof."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the car ends up on its side or roof.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle OnFlipped;

        [SchematicDoc(@"{""name"": ""OnRighted"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""description"": ""Sent when a flipped car is back on its wheels."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when a flipped car is back on its wheels.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle OnRighted;

        [SchematicDoc(@"{""name"": ""OnCrash"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Speed"", ""type"": ""float""}], ""description"": ""Sent when the direction-adjusted impact speed exceeds Properties' Min Crash Speed. Impacts aligned with the nearest tire's ground normal count less. The adjusted speed is passed in m/s. For damage, sounds and camera shake."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the direction-adjusted impact speed exceeds Min Crash Speed. Impacts aligned with the nearest tire's ground normal count less; the adjusted speed is passed in m/s.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Speed", typeof(float))]
        public readonly SignalHandle<float> OnCrash;

        [SchematicDoc(@"{""name"": ""OnBounce"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Strength"", ""type"": ""float""}], ""description"": ""Sent when the car bounces off a wall or another car (Properties' Bounce): the speed it hit at, in m/s (half against another car, which bounces too). For a bonk sound, sparks and camera shake."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the car bounces off a wall or another car; the speed it hit at (m/s).")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Strength", typeof(float))]
        public readonly SignalHandle<float> OnBounce;

        [SchematicDoc(@"{""name"": ""OnJump"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Launch"", ""type"": ""float""}], ""description"": ""Sent when the car really jumps (off a ramp, a crest or a ledge; bumps don't count): how fast it left the ground, in m/s (0 off a ledge). For a whoosh, a camera move or a trick window. On Landed follows when it's down."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the car really jumps (not a bump): how fast it left the ground (m/s).")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Launch", typeof(float))]
        public readonly SignalHandle<float> OnJump;

        #endregion

        #region Setup

        protected override void OnSetup(BehaviorSetupContext context)
        {
            // Four wheels at sensible spots for a ~1.8 x 4 m car with its origin at the middle of the body.
            context.Set(Wheels, new[]
            {
                MakeWheel(context.Transform, "Front Left", new Vector3(-0.85f, -0.1f, 1.35f), steers: true, drives: false, handbrake: false),
                MakeWheel(context.Transform, "Front Right", new Vector3(0.85f, -0.1f, 1.35f), steers: true, drives: false, handbrake: false),
                MakeWheel(context.Transform, "Rear Left", new Vector3(-0.85f, -0.1f, -1.35f), steers: false, drives: true, handbrake: true),
                MakeWheel(context.Transform, "Rear Right", new Vector3(0.85f, -0.1f, -1.35f), steers: false, drives: true, handbrake: true),
            });

            static Wheel MakeWheel(Transform car, string name, Vector3 position, bool steers, bool drives, bool handbrake)
            {
                var anchor = new GameObject("Wheel_" + name.Replace(" ", "")).transform;
                anchor.SetParent(car, false);
                anchor.localPosition = position;
                return new Wheel(name, anchor, steers, drives, handbrake);
            }
        }

        protected override void OnStart()
        {
            var instance = Self;
            var wheels = Wheels.Value;
            var properties = Properties.Value;
            var state = new CarState { Wheels = new WheelState[wheels != null ? wheels.Length : 0] };
            var car = instance.Transform;
            _cars.RemoveWhere(c => c == null);
            _cars.Add(instance.GameObject);
            var bodyVisual = BodyVisual.Value;
            if (bodyVisual != null && bodyVisual != car)
            {
                state.BodyRestRotation = bodyVisual.localRotation;
                state.BodyRestScale = bodyVisual.localScale;
                state.HasBodyRest = true;
            }
            state.LastMovementPosition = car.position;
            state.HasMovementPosition = true;
            for (int i = 0; i < state.Wheels.Length; i++)
            {
                var wheel = wheels[i];
                state.Wheels[i].Extension = properties != null
                    ? Mathf.Max(0f, Mathf.Max(0f, properties.GroundCheckDistance)
                        * Mathf.Clamp01(properties.TirePlacementThreshold) - Mathf.Max(0.05f, wheel?.Radius ?? 0.3f))
                    : 0.45f;
                state.Wheels[i].VisualRest = Quaternion.identity;
                state.Wheels[i].TargetSpeedMultiplier = 1f;
                if (wheel?.Anchor == null)
                    continue;
                state.Wheels[i].IsFront = car.InverseTransformPoint(wheel.Anchor.position).z >= 0f;
                if (HasMovableVisual(wheel))
                    state.Wheels[i].VisualRest = VisualRestRotation(car, wheel.Visual);
            }
            _state.Value = state;

            TargetSpeed.Subscribe(value => state.SetTargetSpeed(value));
            Brake.Subscribe(value => state.Brake = Mathf.Clamp01(value));
            Steer.Subscribe(value => state.SteerInput = Mathf.Clamp(value, -1f, 1f));
            Drive.Subscribe(value => state.SetStick(value));
            Handbrake.Subscribe(value => state.Handbrake = value);
            Boost.Subscribe(() => state.BoostQueued = true);
            Unflip.Subscribe(() => state.UnflipQueued = true);

            // Input Signals the car hears itself, only while the player drives it.
            var bindings = DriverInputs.Value;
            if (bindings != null)
                foreach (var binding in bindings)
                {
                    if (binding?.Signal == null) continue;
                    var bound = binding;
                    bound.Signal.Subscribe<object>(instance, value =>
                    {
                        if (VehicleInput.AcceptsPlayerInput(instance.GameObject, PlayerDriverOnly[instance]))
                            state.Apply(bound, value);
                    });
                }

            // Proxies (another player simulates this car) keep their display Variables from the simulator's Signals.
            OnDriftStarted.Subscribe(() =>
            {
                if (!IsProxy(instance)) return;
                IsDrifting[instance] = true;
                DriftLevel[instance] = 0;
                DriftCharge[instance] = 0f;
            });
            OnDriftEnded.Subscribe(_ =>
            {
                if (!IsProxy(instance)) return;
                IsDrifting[instance] = false;
                DriftLevel[instance] = 0;
                DriftCharge[instance] = 0f;
            });
            OnDriftLevel.Subscribe(level => { if (IsProxy(instance)) DriftLevel[instance] = level; });
            OnBoost.Subscribe(level =>
            {
                var p = Properties[instance];
                if (!IsProxy(instance) || p == null) return;
                float duration = level > 0 && p.TurboLevels != null && level <= p.TurboLevels.Length && p.TurboLevels[level - 1] != null
                    ? p.TurboLevels[level - 1].Duration : p.BoostDuration;
                state.BoostTime = Mathf.Max(state.BoostTime, duration);
            });
            OnFlipped.Subscribe(() => { if (IsProxy(instance)) IsFlipped[instance] = true; });
            OnRighted.Subscribe(() => { if (IsProxy(instance)) IsFlipped[instance] = false; });
        }

        // Another peer simulates this car; this one shows a proxy.
        private static bool IsProxy(BehaviorInstance instance)
        {
            var body = instance.IsSpawned ? instance.GetComponent<Box3DBody>() : null;
            return body != null && body.IsNetworkProxy();
        }

        #endregion

        #region Networking

        /// <summary>How many times a second the owner sends its Driver Inputs (as the input Signals) to everyone else.</summary>
        public const float InputSendRate = 15f;
        // A Target Speed this close to 0 is nothing held: no drive, no brake.
        private const float TargetSpeedDeadZone = 0.01f;
        // A negative Target Speed brakes while the car rolls forward faster than this (m/s), then reverses.
        private const float ReverseBrakeSpeed = 0.5f;

        // The owner mirrors its inputs to everyone else, at most InputSendRate times a second and only when they change,
        // through the (networked) input Signals: proxies show the steering, and the authority has them if it takes over.
        protected override void OnUpdate()
        {
            var instance = Self;
            var state = _state.Value;
            var car = instance.GameObject;
            if (state == null || !SchematicNetwork.IsNetworked(car) || !SchematicNetwork.IsLocallyControlled(car))
                return;

            // Owner but not simulating (server-authority Transform): button presses go to whoever simulates.
            if (IsProxy(instance))
            {
                if (state.BoostQueued) { Boost.Invoke(instance); state.BoostQueued = false; }
                if (state.UnflipQueued) { Unflip.Invoke(instance); state.UnflipQueued = false; }
            }

            float now = Time.unscaledTime;
            if (now < state.NextInputSend)
                return;
            bool changed = false;
            if (state.UseTargetSpeed && (!state.SentTargetSpeedEnabled || !Mathf.Approximately(state.TargetSpeed, state.SentTargetSpeed)))
            {
                state.SentTargetSpeed = state.TargetSpeed;
                state.SentTargetSpeedEnabled = true;
                TargetSpeed.Invoke(instance, state.TargetSpeed);
                changed = true;
            }
            else if (!state.UseTargetSpeed && state.SentTargetSpeedEnabled)
            {
                state.SentTargetSpeed = 0f;
                state.SentTargetSpeedEnabled = false;
                TargetSpeed.Invoke(instance, 0f);
                changed = true;
            }
            if (!Mathf.Approximately(state.Brake, state.SentBrake)) { state.SentBrake = state.Brake; Brake.Invoke(instance, state.Brake); changed = true; }
            if (Mathf.Abs(state.SteerInput - state.SentSteer) > 0.01f || (state.SteerInput == 0f && state.SentSteer != 0f))
            {
                state.SentSteer = state.SteerInput;
                Steer.Invoke(instance, state.SteerInput);
                changed = true;
            }
            if (state.Handbrake != state.SentHandbrake) { state.SentHandbrake = state.Handbrake; Handbrake.Invoke(instance, state.Handbrake); changed = true; }
            if (changed)
                state.NextInputSend = now + 1f / InputSendRate;
        }

        // A proxy doesn't simulate, but its wheels still roll and steer, and its speed Variables follow its motion.
        private void UpdateProxy(BehaviorInstance instance, ArcadeCarProperties p, Wheel[] wheels, CarState state, float dt)
        {
            var car = instance.Transform;
            Vector3 position = car.position;
            Vector3 velocity = state.HasProxyPosition && dt > 0f ? (position - state.ProxyPosition) / dt : Vector3.zero;
            state.ProxyPosition = position;
            state.HasProxyPosition = true;
            // Smoothed, since the synced Transform arrives in steps.
            state.ProxyVelocity = Vector3.Lerp(state.ProxyVelocity, velocity, 0.3f);

            float forwardSpeed = Vector3.Dot(state.ProxyVelocity, car.forward);
            Speed.Value = state.ProxyVelocity.magnitude * 3.6f;
            ForwardSpeed.Value = forwardSpeed;

            bool turningIn = Mathf.Abs(state.SteerInput) > Mathf.Abs(state.Steer);
            state.Steer = Mathf.MoveTowards(state.Steer, IsActive(instance) ? state.SteerInput : 0f, (turningIn ? p.SteerSpeed : p.SteerReturnSpeed) * dt);
            float speedFraction = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(0.1f, p.MaxSpeed));
            float steerAngle = state.Steer * p.MaxSteerAngle * p.SteerBySpeed.Evaluate(speedFraction);
            steerAngle = LimitSteer(steerAngle, forwardSpeed, Wheelbase(wheels, car), p.CorneringLimit);
            for (int i = 0; i < wheels.Length && i < state.Wheels.Length; i++)
            {
                ref var w = ref state.Wheels[i];
                w.SteerAngle = wheels[i] != null && wheels[i].Steers ? steerAngle : 0f;
                w.RollSpeed = forwardSpeed;
            }

            if (state.BoostTime > 0f)
                state.BoostTime -= dt;
            IsBoosting.Value = state.BoostTime > 0f;
        }

        #endregion

        #region C# API

        /// <summary>Sets target forward speed (0..1), brake, steering and handbrake for AI written in C#. Values hold until changed.</summary>
        public void SetInputs(BehaviorInstance instance, float throttle, float brake, float steer, bool handbrake = false)
        {
            var state = _state[instance];
            if (state == null) return;
            state.SetTargetSpeed(Mathf.Clamp01(throttle));
            state.Brake = Mathf.Clamp01(brake);
            state.SteerInput = Mathf.Clamp(steer, -1f, 1f);
            state.Handbrake = handbrake;
        }

        public void SetInputs(float throttle, float brake, float steer, bool handbrake = false) => SetInputs(Selected, throttle, brake, steer, handbrake);

        /// <summary>Pushes the car forward at <paramref name="acceleration"/> m/s² for <paramref name="duration"/> seconds (boost pads, power-ups).</summary>
        public void StartBoost(BehaviorInstance instance, float acceleration, float duration)
        {
            var state = _state[instance];
            if (state == null) return;
            state.BoostAcceleration = Mathf.Max(acceleration, state.BoostTime > 0f ? state.BoostAcceleration : 0f);
            state.BoostTime = Mathf.Max(state.BoostTime, duration);
        }

        /// <summary>True if the car drives right now: someone's in a driver seat, it's Always Active, or it has no Vehicle Seats.</summary>
        public bool IsActive(BehaviorInstance instance)
        {
            if (AlwaysActive[instance])
                return true;
            var seats = _seats[instance];
            return seats == null || ((VehicleSeats)seats.Behavior).HasDriver[seats];
        }

        #endregion

        #region Physics

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            var p = Properties.Value;
            var wheels = Wheels.Value;
            var state = _state.Value;
            if (p == null || wheels == null || state == null || state.Wheels.Length != wheels.Length)
                return;

            var body = instance.GetComponent<Box3DBody>();
            // Only one peer simulates: proxies follow their synced Transform (and just animate), and a kinematic car is
            // being carried.
            if (body != null && body.IsNetworkProxy())
            {
                UpdateProxy(instance, p, wheels, state, Time.fixedDeltaTime);
                return;
            }
            state.HasProxyPosition = false;
            if (!body.IsLive() || body.IsKinematic())
                return;
            var native = body.Body;
            if (!state.BodyConfigured)
                ConfigureBody(native, p, state);

            float dt = Time.fixedDeltaTime;
            var car = instance.Transform;
            Quaternion rotation = car.rotation;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;

            Vector3 gravity = SchematicPhysics.Gravity;
            float g = gravity.magnitude;
            Vector3 worldUp = g > 0.001f ? -gravity / g : Vector3.up;

            float mass = native.GetMass();
            Vector3 centerOfMass = (Vector3)native.GetWorldCenterOfMass();
            Vector3 velocity = native.GetLinearVelocity();
            Vector3 angularVelocity = native.GetAngularVelocity();
            // Position guard: a step that lands the car far from where its last position and velocity put it (more
            // than Position Guard Distance off) is a glitch, not driving: it goes back to where it was, moving as it
            // was. (A deliberate teleport: call MarkTeleported first.)
            Vector3 bodyPosition = (Vector3)native.GetPosition();
            if (p.PositionGuardDistance > 0f && state.HasGuardPosition && dt > 0f)
            {
                Vector3 expected = state.GuardPosition + state.GuardVelocity * dt;
                float allowed = p.PositionGuardDistance + (state.GuardVelocity.magnitude + velocity.magnitude) * dt;
                if (Vector3.Distance(bodyPosition, expected) > allowed)
                {
                    native.SetTransform(state.GuardPosition, state.GuardRotation);
                    native.SetLinearVelocity((float3)state.GuardVelocity);
                    native.SetAngularVelocity((float3)state.GuardAngularVelocity);
                    return;
                }
            }
            state.GuardPosition = bodyPosition;
            state.GuardRotation = native.GetRotation();
            state.GuardVelocity = velocity;
            state.GuardAngularVelocity = angularVelocity;
            state.HasGuardPosition = true;

            Vector3 carPosition = car.position;
            float movementSpeed = 0f;
            if (state.HasMovementPosition && dt > 0f)
                movementSpeed = Vector3.Distance(carPosition, state.LastMovementPosition) / dt;
            state.LastMovementPosition = carPosition;
            state.HasMovementPosition = true;
            if (state.HasDiagnosticAngularVelocity && dt > 0f)
            {
                Vector3 measuredAngularAcceleration = (angularVelocity - state.PreviousAngularVelocity) / dt;
                Vector3 unexplainedAcceleration = measuredAngularAcceleration - state.LastGuardedAngularAcceleration;
                float threshold = Mathf.Max(0f, p.TorqueGuardDiagnosticThreshold);
                if (p.LogTorqueGuardDiagnostics && Time.time >= state.NextTorqueDiagnosticTime
                    && Mathf.Max(measuredAngularAcceleration.magnitude, unexplainedAcceleration.magnitude) >= threshold)
                {
                    Debug.LogWarning(
                        $"[Arcade Car torque] measured={measuredAngularAcceleration:F1} rad/s², "
                        + $"unexplained={unexplainedAcceleration:F1}; prior guarded={state.LastGuardedAngularAcceleration:F1}; "
                        + $"tire={state.LastTireAngularAcceleration:F1}, stabilization={state.LastStabilizationAngularAcceleration:F1}, "
                        + $"rotation assists={state.LastRotationAssistAngularAcceleration:F1}, "
                        + $"air control={state.LastAirControlAngularAcceleration:F1}; "
                        + $"grounded tires={state.LastGroundedWheels}/{wheels.Length}. "
                        + "Large unexplained values point to Box3D contacts or other forces outside the car controller.",
                        instance.GameObject);
                    state.NextTorqueDiagnosticTime = Time.time + 0.5f;
                }
            }
            state.PreviousAngularVelocity = angularVelocity;
            state.HasDiagnosticAngularVelocity = true;
            float forwardSpeed = Vector3.Dot(velocity, forward);
            Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, up);
            float planarSpeed = planarVelocity.magnitude;
            float driftAngle = planarSpeed > 1f ? Vector3.SignedAngle(forward, planarVelocity, up) : 0f;
            float inverseMaxSpeed = 1f / Mathf.Max(0.1f, p.MaxSpeed);
            float speedFraction = Mathf.Clamp01(Mathf.Abs(forwardSpeed) * inverseMaxSpeed);
            int driveCount = 0, handbrakeCount = 0;
            foreach (var wheel in wheels)
            {
                if (wheel?.Anchor == null) continue;
                if (wheel.Drives) driveCount++;
                if (wheel.HasHandbrake) handbrakeCount++;
            }
            float wheelCount = wheels.Length;
            float massShare = mass / Mathf.Max(1f, wheelCount);

            float targetSpeedMultiplier = 1f;
            float supportedWheelLoad = 0f;
            Vector3 wheelForceTorque = Vector3.zero;
            for (int i = 0; i < state.Wheels.Length; i++)
            {
                ref var wheelState = ref state.Wheels[i];
                if (!wheelState.Grounded)
                    wheelState.TargetSpeedMultiplier = 1f;
                else
                {
                    float resistance = 1f + Mathf.Max(0f, p.GroundMaterialResistance);
                    wheelState.TargetSpeedMultiplier = Mathf.MoveTowards(wheelState.TargetSpeedMultiplier, 1f,
                        Mathf.Max(0f, p.TargetSpeedGroundRecovery) * dt / resistance);
                }
                targetSpeedMultiplier *= wheelState.TargetSpeedMultiplier;
            }

            // Inputs: nobody driving means no input (and the parking brake).
            bool active = IsActive(instance);
            if (!active && state.WasActive)
                state.ClearInputs();
            state.WasActive = active;
            float effectiveTargetSpeed = state.TargetSpeed * targetSpeedMultiplier;
            float throttle = active && state.UseTargetSpeed ? Mathf.Max(effectiveTargetSpeed, 0f) : 0f;
            float brakeInput = active ? state.Brake : 0f;
            float brake = Mathf.Max(brakeInput, active && state.UseTargetSpeed ? Mathf.Max(-effectiveTargetSpeed, 0f) : 0f);
            float steerInput = active ? state.SteerInput : 0f;
            bool handbrake = active && state.Handbrake;
            bool parked = !active && ParkWhenEmpty.Value;

            // Steering eases towards the input, with less lock at speed.
            bool turningIn = Mathf.Abs(steerInput) > Mathf.Abs(state.Steer) || Mathf.Sign(steerInput) != Mathf.Sign(state.Steer);
            state.Steer = Mathf.MoveTowards(state.Steer, steerInput, (turningIn ? p.SteerSpeed : p.SteerReturnSpeed) * dt);
            float steerAngle = state.Steer * p.MaxSteerAngle * p.SteerBySpeed.Evaluate(speedFraction);
            steerAngle = LimitSteer(steerAngle, forwardSpeed, Wheelbase(wheels, car), p.CorneringLimit);

            // Boosts.
            if (state.BoostQueued)
            {
                state.BoostQueued = false;
                StartBoost(instance, p.BoostAcceleration, p.BoostDuration);
                OnBoost.Invoke(instance, 0);
            }
            bool boosting = state.BoostTime > 0f;
            float topSpeed = p.MaxSpeed + (boosting ? p.BoostMaxSpeedBonus : 0f);

            // What the engine and brakes want this step (m/s², for the whole car).
            float driveAcceleration = 0f, brakeDeceleration = 0f;
            float driftThrottle = throttle;
            if (active && state.UseTargetSpeed)
            {
                // Target Speed is how fast to go, as a fraction of top speed. It drives up to a positive target and
                // never brakes down to one: past it (or with nothing held, 0) the engine just stops and the car rolls
                // on. Only a negative target brakes, while the car still rolls forward; stopped, it reverses up to it.
                float targetSpeed = effectiveTargetSpeed;
                float forwardFraction = forwardSpeed / Mathf.Max(0.1f, topSpeed);
                float reverseFraction = -forwardSpeed / Mathf.Max(0.1f, p.MaxReverseSpeed);
                float targetDrive = 0f, targetBrake = 0f;
                // Easing off (going faster than a target that's still held): Engine Braking settles the car to it,
                // a gentle drag growing with how far over it is. Not with nothing held (0 just coasts).
                float engineBraking = 0f;
                if (targetSpeed > TargetSpeedDeadZone)
                {
                    targetDrive = Mathf.Clamp01(targetSpeed - forwardFraction);
                    float over = forwardSpeed - targetSpeed * topSpeed;
                    if (over > 0f && p.EngineBraking > 0f)
                        engineBraking = -Mathf.Min(over * p.EngineBraking, over / dt);
                }
                else if (targetSpeed < -TargetSpeedDeadZone)
                {
                    if (forwardSpeed > ReverseBrakeSpeed)
                        targetBrake = Mathf.Clamp01(-targetSpeed);
                    else
                    {
                        targetDrive = Mathf.Clamp01(-targetSpeed - reverseFraction);
                        float over = -forwardSpeed + targetSpeed * p.MaxReverseSpeed;
                        if (over > 0f && p.EngineBraking > 0f)
                            engineBraking = Mathf.Min(over * p.EngineBraking, over / dt);
                    }
                }
                if (brakeInput > 0.01f)
                {
                    targetDrive = 0f;
                    targetBrake = Mathf.Max(targetBrake, brakeInput);
                }

                if (targetDrive > 0f)
                {
                    if (targetSpeed < 0f)
                        driveAcceleration = -p.ReverseAcceleration * targetDrive;
                    else
                        driveAcceleration = p.Acceleration * p.AccelerationCurve.Evaluate(speedFraction) * targetDrive;
                }
                brakeDeceleration = p.BrakeDeceleration * targetBrake;
                if (targetDrive <= 0f && brakeDeceleration <= 0f)
                    driveAcceleration = engineBraking;
                driftThrottle = Mathf.Max(driftThrottle, Mathf.Max(effectiveTargetSpeed, 0f));
            }
            else
            {
                if (brakeInput > 0.01f)
                {
                    brakeDeceleration = Mathf.Max(brakeDeceleration, p.BrakeDeceleration * brakeInput);
                }
            }
            if (parked)
                brakeDeceleration = p.BrakeDeceleration;

            UpdateDrift(instance, p, state, active, handbrake, steerInput, driftThrottle, planarSpeed, forwardSpeed, driftAngle, mass, dt, up);
            bool drifting = IsDrifting.Value;
            // Cornering eats into the throttle: the engine doesn't just refill what a hard turn costs.
            if (!drifting && driveAcceleration > 0f && p.CorneringLimit > 0f && p.CorneringThrottleCut > 0f)
                driveAcceleration *= 1f - Mathf.Clamp01(Mathf.Abs(state.LateralAcceleration) / p.CorneringLimit)
                    * Mathf.Clamp01(p.CorneringThrottleCut);
            // Arcade drifts: the drift keeps its own heading, all the push goes along it, and the nose is swung to a
            // slide angle on top (the tyres stop gripping sideways). Physics drifts: the tyres just grip less.
            bool arcadeDrift = drifting && p.DriftStyle == DriftHandling.Arcade;
            bool forcedDrift = drifting && handbrake && !arcadeDrift;
            if (forcedDrift)
            {
                Vector3 carHeading = Vector3.ProjectOnPlane(forward, up).normalized;
                if (!state.ForcedDriftActive || state.ForcedDriftHeading.sqrMagnitude < 0.001f)
                    state.ForcedDriftHeading = carHeading;
                float targetMagnitude = Mathf.Abs(effectiveTargetSpeed);
                float actualSpeedFraction = forwardSpeed >= 0f
                    ? forwardSpeed / Mathf.Max(0.1f, topSpeed)
                    : -forwardSpeed / Mathf.Max(0.1f, p.MaxReverseSpeed);
                float speedRatio = targetMagnitude > 0.001f
                    ? Mathf.Clamp01(actualSpeedFraction / targetMagnitude)
                    : 0f;
                float realignRate = p.ForcedDriftRealignSpeed
                    * Mathf.Lerp(1f, Mathf.Clamp01(p.ForcedDriftMinRealignFraction), speedRatio);
                state.ForcedDriftHeading = Vector3.RotateTowards(state.ForcedDriftHeading, carHeading,
                    realignRate * Mathf.Deg2Rad * dt, 0f).normalized;
            }
            state.ForcedDriftActive = forcedDrift;

            // Wheels.
            //
            // READ THIS BEFORE CHANGING ANYTHING ABOUT THE GROUND CASTS OR THE SUSPENSION:
            // The ground casts are DETECTION, not suspension. Each is bent: straight down the car's own up from the
            // top of the body to the top of the tyre, then on toward the ground the tyres stood on (the support plane), and it
            // deliberately shoots far past the tyre, through where the ground will be (Ground Check Distance), so
            // ground is seen early and contact doesn't flicker: a longer cast makes the car stick better, and that
            // is on purpose.
            // The car must still sit on its TYRES: a tyre only pushes (spring, damper, grip, sticky force) when the
            // ground is within its reach, Tire Placement Threshold x Ground Check Distance below its anchor. A hit
            // further along the cast is ground nearby (it counts for the support plane and for being grounded), but
            // it holds nothing up. Never let a hit at the far end of a cast put force on the car: it rides on the
            // ends of the casts and bounces there. (This has been broken and corrected many times; the gate is in the
            // second loop below, "Only the ground a tyre reaches pushes".)
            int grounded = 0;
            Vector3 normalSum = Vector3.zero;
            Vector3 groundPointSum = Vector3.zero;
            float groundCheckDistanceForWheels = Mathf.Max(0f, p.GroundCheckDistance);
            float contactTransitionTime = Mathf.Max(0f, p.StabilizeTransitionTime);
            float contactBlendStep = contactTransitionTime > 0f ? dt / contactTransitionTime : 1f;
            // Springs kept where one physics step can follow them: stiffness below what overshoots in a step, and at
            // least Min Spring Damping Ratio of the damping that stops a bounce dead (stiff and underdamped springs
            // bounced the wheels off the road in turns, swinging grip from side to side).
            float stiffness = Mathf.Min(Mathf.Max(0f, p.SpringStiffness), 0.25f / (dt * dt));
            float springDamping = Mathf.Min(Mathf.Max(p.SpringDamping, 2f * Mathf.Sqrt(stiffness) * Mathf.Clamp01(p.MinSpringDampingRatio)),
                0.9f / dt);
            // After an arcade drift the tyres grip again gradually, so the car slides out of it instead of snapping.
            // After a bounce the tyres grip again gradually too, so they don't snap the car round as it lands.
            // While a bounce is in control (Bounce Align Time) it owns the car's motion: the tyres' grip, engine and
            // sticky force, the drift and the driving assists stand aside, so nothing pushes against it (the
            // springs still hold the car up). Grip starts coming back once it lets go.
            bool bounceHeld = state.BounceAlignTime > 0f;
            if (!bounceHeld)
                state.BounceGrip = Mathf.MoveTowards(state.BounceGrip, 1f, p.BounceGripTime > 0f ? dt / p.BounceGripTime : 1f);
            state.DriftGripReturn = arcadeDrift ? 0f
                : Mathf.MoveTowards(state.DriftGripReturn, 1f, p.DriftExitTime > 0f ? dt / p.DriftExitTime : 1f);
            float contactMaxAngle = p.ContactNormalMaxAngle > 0f ? p.ContactNormalMaxAngle * Mathf.Deg2Rad : Mathf.PI;
            // The suspension (casts and springs) points at the ground the tyres stood on last step, not along the
            // car's own up: a leaning or pitching body no longer swings its casts off the road (they used to let go
            // early, then find it again already squashed and kick). Kept within Contact Normal Max Angle of the
            // car's up, and only with two tyres on the ground (one bad hit can't steer the casts); the car's own up
            // otherwise (in the air, flipped).
            Vector3 suspensionUp = up;
            if (p.CastTowardGround && state.HasGroundPlane && state.LastGroundedWheels >= 2)
                suspensionUp = Vector3.RotateTowards(up, state.GroundPlaneNormal, contactMaxAngle, 0f).normalized;
            float springMaxRise = p.SpringMaxRise > 0f ? p.SpringMaxRise * dt : float.MaxValue;
            float tyreContactCos = p.TyreContactMaxAngle > 0f ? Mathf.Cos(p.TyreContactMaxAngle * Mathf.Deg2Rad) : -2f;

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                ref var w = ref state.Wheels[i];
                if (wheel?.Anchor == null)
                {
                    w.WasGrounded = w.Grounded;
                    w.Grounded = false;
                    continue;
                }

                Vector3 origin = wheel.Anchor.position;
                w.SteerAngle = wheel.Steers ? steerAngle : 0f;
                w.WasGrounded = w.Grounded;
                float tireRadius = Mathf.Max(0.05f, wheel.Radius);
                w.SuspensionUpLocal = Quaternion.Inverse(rotation) * suspensionUp;
                // The bent cast (see "READ THIS" above): straight down the car's own up from the top of the body to
                // the top of the tyre, then on toward the ground the tyres stood on (as far as before: its end is
                // where it was when it bent at the tyre's centre).
                float bodyTopOffset = BodyTopOffsetAlong(native, origin, up);
                float tyreDrop = w.Extension - tireRadius;
                float beyond = Mathf.Max(0f, groundCheckDistanceForWheels);
                w.Grounded = CastWheel(origin, bodyTopOffset, up, suspensionUp, tireRadius, tyreDrop, beyond,
                    p.GroundLayers, body, out w.GroundHit, out bool ceiling, out GameObject groundObject);
                // A ceiling isn't ground: on its back the car's roof lies in the floor, so the cast (from the top of
                // the body) starts inside it and "hits" it at once. Something touching the cast where it starts, at
                // the body's top, or a surface facing away from the car, is a ceiling: cast again from the wheel
                // itself, under whatever is on the roof.
                w.HasCeiling = ceiling;
                if (ceiling)
                {
                    w.CeilingPoint = w.GroundHit.point;
                    w.Grounded = CastWheel(origin, 0f, up, suspensionUp, tireRadius, tyreDrop, beyond,
                        p.GroundLayers, body, out w.GroundHit, out _, out groundObject);
                }
                // A tyre touching the ground with its sidewall isn't standing on it (a car on its side, a tyre
                // against a kerb's face): ground further than Tyre Contact Max Angle from the car's up doesn't count,
                // so a car on its side can't drive along on two tyres.
                w.Sidewall = false;
                if (w.Grounded && Vector3.Dot(w.GroundHit.normal.normalized, up) < tyreContactCos)
                {
                    w.Sidewall = true;
                    w.Grounded = false;
                }
                // Only hits count at once: a tyre that had ground and whose cast suddenly finds nothing keeps its last
                // ground for up to Ground Hold Time, so one stray miss doesn't drop it. (Not a sidewall or a ceiling:
                // those are real answers, and a car on its side or roof lets go straight away.) It keeps the ground's
                // surface, not the old hit: each held step the distance is measured again from where the tyre is now
                // to that surface, so the spring and the tyre's reach follow the car (a frozen distance kept pushing
                // as if still pressed into the ground, and a long hold stopped the car settling). Ground further than
                // the cast reaches, or turned edge-on, ends the hold.
                if (w.Grounded)
                {
                    w.LastGroundHit = w.GroundHit;
                    w.HasLastGroundHit = true;
                    w.MissTime = 0f;
                    UpdateWheelSurface(ref w, groundObject, i, instance.GameObject);
                }
                else if (w.HasLastGroundHit && !w.Sidewall && !ceiling && w.MissTime + dt <= p.GroundHoldTime)
                {
                    Vector3 heldNormal = w.LastGroundHit.normal;
                    float facing = Vector3.Dot(up, heldNormal);
                    float heldDistance = facing > 0.1f
                        ? Vector3.Dot(origin - w.LastGroundHit.point, heldNormal) / facing
                        : float.MaxValue;
                    if (heldDistance >= 0f && heldDistance <= tyreDrop + tireRadius + beyond)
                    {
                        w.MissTime += dt;
                        w.GroundHit = w.LastGroundHit;
                        w.GroundHit.distance = heldDistance;
                        w.GroundHit.point = origin - up * heldDistance;
                        w.Grounded = true;
                    }
                    else
                        w.HasLastGroundHit = false;
                }
                else
                    w.HasLastGroundHit = false;
                if (w.Grounded)
                {
                    grounded++;
                    // A tyre's sphere touching a kerb or wall with its side reports a sideways normal: kept within
                    // Contact Normal Max Angle of the car's up, so grip can't point up and launch the car.
                    Vector3 castNormal = Vector3.RotateTowards(up, w.GroundHit.normal, contactMaxAngle, 0f).normalized;
                    // Blended only while it stays on the ground (softens a change of surface): a tyre just touching
                    // down takes the ground as it is, not turned from whatever it last had (after a roll that was
                    // upside down, and the plane swung through the side and threw the car back over).
                    w.GroundNormal = w.HasBlendedContact && w.WasGrounded && contactTransitionTime > 0f
                        ? Vector3.RotateTowards(w.ContactNormal, castNormal, Mathf.PI * contactBlendStep, 0f).normalized
                        : castNormal;
                    // The support plane uses the ground's own normal: kept to the car's up (as above), it followed
                    // the car whenever the car tilted further than that from the road.
                    w.RawGroundNormal = w.GroundHit.normal.normalized;
                    normalSum += w.RawGroundNormal;
                    groundPointSum += w.GroundHit.point;
                }
            }

            // Actual contacts define a shared support plane; virtual wheel forces fade with real tire support.
            // The ground under the car: the tyres' contact normals, leaving out any far from the rest (a tyre on a
            // kerb's side or a wall), so one odd hit doesn't tilt it.
            Vector3 sharedGroundNormal = grounded > 0 ? normalSum.normalized : Vector3.zero;
            if (grounded > 1 && contactMaxAngle < Mathf.PI)
            {
                Vector3 agreeing = Vector3.zero;
                float agreeCos = Mathf.Cos(contactMaxAngle);
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i]?.Anchor != null && state.Wheels[i].Grounded
                        && Vector3.Dot(state.Wheels[i].RawGroundNormal, sharedGroundNormal) >= agreeCos)
                        agreeing += state.Wheels[i].RawGroundNormal;
                if (agreeing.sqrMagnitude > 0.001f)
                    sharedGroundNormal = agreeing.normalized;
            }
            Vector3 sharedGroundPoint = grounded > 0 ? groundPointSum / grounded : Vector3.zero;
            // Rollover risk (from last step's load transfer): 0 below Rollover Threshold, 1 with one side's tyres
            // carrying everything.
            float rolloverRisk = p.RolloverThreshold < 1f
                ? Mathf.Clamp01((Mathf.Abs(state.LoadTransfer) - p.RolloverThreshold) / (1f - p.RolloverThreshold))
                : 0f;
            // At risk the sideways grip pushes higher, toward the centre of mass height: it turns the car as hard
            // but can't trip it.
            float sideForceHeight = Mathf.Lerp(p.SideForceHeight, 1f, rolloverRisk * Mathf.Clamp01(p.RolloverGripLift));
            float loadLeft = 0f, loadRight = 0f;
            // Tyres actually on the ground (in reach, not just seen by the long casts): none, and the car's in the air.
            int touching = 0;
            float spatialGates = 0f, temporalGates = 0f;
            int gatedWheels = 0;
            float virtualGroundWeight = grounded > 0 ? Mathf.Clamp01((float)grounded / Mathf.Max(1f, wheelCount)) : 0f;

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                ref var w = ref state.Wheels[i];
                if (wheel?.Anchor == null)
                    continue;

                Vector3 origin = wheel.Anchor.position;
                float radius = Mathf.Max(0.05f, wheel.Radius);
                float rest = Mathf.Max(0.01f, p.SuspensionRestLength);
                float placementDistance = groundCheckDistanceForWheels * Mathf.Clamp01(p.TirePlacementThreshold);
                float maxExtension = Mathf.Max(0f, placementDistance - radius);
                bool actualGrounded = w.Grounded;
                bool wasGrounded = w.WasGrounded;
                float targetForceWeight = actualGrounded ? 1f : virtualGroundWeight;
                Vector3 targetNormal;
                float targetContactDistance;
                bool hasContactCandidate = true;

                if (actualGrounded)
                {
                    var hit = w.GroundHit;
                    targetNormal = w.GroundNormal;
                    targetContactDistance = hit.distance;
                }
                else
                {
                    w.TargetSpeedMultiplier = 1f;
                    w.RollSpeed *= 0.99f;
                    float upNormal = Vector3.Dot(suspensionUp, sharedGroundNormal);
                    targetContactDistance = upNormal > 0.1f
                        ? Vector3.Dot(origin - sharedGroundPoint, sharedGroundNormal) / upNormal
                        : float.MaxValue;
                    if (grounded == 0 || targetContactDistance < 0f
                        || targetContactDistance > groundCheckDistanceForWheels)
                    {
                        hasContactCandidate = false;
                        targetForceWeight = 0f;
                        targetNormal = w.HasBlendedContact ? w.ContactNormal : up;
                        targetContactDistance = w.HasBlendedContact ? w.ContactDistance : groundCheckDistanceForWheels;
                    }
                    else
                    {
                        targetNormal = sharedGroundNormal;
                    }
                }

                if (!hasContactCandidate && (!w.HasBlendedContact || w.ContactForceWeight <= 0f))
                {
                    w.ContactForceWeight = 0f;
                    w.Extension = maxExtension;
                }

                if (!w.HasBlendedContact)
                {
                    w.ContactNormal = targetNormal;
                    w.ContactDistance = targetContactDistance;
                    w.ContactForceWeight = targetForceWeight;
                    w.HasBlendedContact = true;
                }
                else
                {
                    w.ContactNormal = !actualGrounded && contactTransitionTime > 0f
                        ? Vector3.RotateTowards(w.ContactNormal, targetNormal, Mathf.PI * contactBlendStep, 0f).normalized
                        : targetNormal;
                    float maxDistanceStep = contactTransitionTime > 0f
                        ? Mathf.Max(groundCheckDistanceForWheels, rest) * contactBlendStep
                        : float.MaxValue;
                    w.ContactDistance = Mathf.MoveTowards(w.ContactDistance, targetContactDistance, maxDistanceStep);
                    w.ContactForceWeight = Mathf.MoveTowards(w.ContactForceWeight, targetForceWeight, contactBlendStep);
                }
                Vector3 normal = w.ContactNormal;
                float contactDistance = w.ContactDistance;
                // Only the ground a tyre reaches pushes (see "READ THIS" above): a hit further down the cast is
                // detection only, and puts no force on the car.
                float forceWeight = contactDistance <= placementDistance ? w.ContactForceWeight : 0f;
                bool tyreDown = actualGrounded && w.GroundHit.distance <= placementDistance;
                if (tyreDown)
                    touching++;
                // Air control gates, per tyre: spatial, how far its ground is beyond its reach (all the way open
                // Air Gate Distance beyond, or with no ground seen); temporal, how long it's been off the ground
                // (opening over Air Gate Time, closing four times as fast once it's back down).
                float clear = !actualGrounded ? 1f
                    : Mathf.Clamp01((w.GroundHit.distance - placementDistance) / Mathf.Max(0.01f, p.AirGateDistance));
                spatialGates += tyreDown ? 0f : Mathf.SmoothStep(0f, 1f, clear);
                float gateTime = Mathf.Max(0.01f, p.AirGateTime);
                w.AirGate = tyreDown ? Mathf.MoveTowards(w.AirGate, 0f, 4f * dt / gateTime)
                    : Mathf.MoveTowards(w.AirGate, 1f, dt / gateTime);
                temporalGates += Mathf.SmoothStep(0f, 1f, w.AirGate);
                gatedWheels++;
                Vector3 contactPoint = actualGrounded ? w.GroundHit.point : origin - suspensionUp * contactDistance;
                w.Point = contactPoint;
                w.Normal = normal;

                // Spring and damper along the suspension, pushing at the wheel.
                float extension = Mathf.Clamp(contactDistance - radius, 0f, maxExtension);
                float compression = rest - Mathf.Min(extension, rest);
                w.Extension = extension;
                Vector3 hubVelocity = native.GetWorldPointVelocity(origin);
                float suspensionSpeed = Vector3.Dot(hubVelocity, suspensionUp);
                // The step a tyre finds the ground again, its speed is the jump from no ground to ground: no damping
                // that step (it fired a big extra kick), the spring alone.
                // (The damper works within the spring's travel only: with the spring at full stretch it doesn't
                // push the car up from ground it isn't touching.)
                float damping = (actualGrounded && !wasGrounded) || compression <= 0f ? 0f : springDamping * suspensionSpeed;
                float springAcceleration = Mathf.Clamp(stiffness * compression - damping, 0f, p.MaxSuspensionAcceleration);
                // Rises at most Spring Max Rise (a one-step jump from nothing to full push becomes a short ramp);
                // falls at once, so a tyre leaving the ground lets go.
                springAcceleration = Mathf.Min(springAcceleration, w.SpringPush + springMaxRise);
                w.SpringPush = springAcceleration;
                float carried = springAcceleration * forceWeight;
                if (Vector3.Dot(origin - centerOfMass, right) >= 0f)
                    loadRight += carried;
                else
                    loadLeft += carried;
                Vector3 suspensionForce = suspensionUp * (springAcceleration * massShare * forceWeight);
                native.ApplyForce(suspensionForce, origin, true);
                wheelForceTorque += Vector3.Cross(origin - centerOfMass, suspensionForce);

                // How much weight the tyre carries (1 = its share of the car at rest): more weight, more grip.
                // (Only Load Grip Amount of it, so a hard turn doesn't pile all the grip onto the outside tyres and trip the car.)
                float load = p.GripScalesWithLoad && g > 0.001f
                    ? Mathf.Lerp(1f, Mathf.Clamp(springAcceleration / g, 0f, 2f), Mathf.Clamp01(p.LoadGripAmount))
                    : 1f;

                // The tyre's directions on the ground.
                Vector3 wheelForward = Quaternion.AngleAxis(w.SteerAngle, up) * forward;
                Vector3 rollDirection = Vector3.ProjectOnPlane(wheelForward, normal).normalized;
                Vector3 sideDirection = Vector3.Cross(normal, rollDirection);
                Vector3 contactVelocity = native.GetWorldPointVelocity(contactPoint);
                Vector3 stickyForce = Vector3.zero;
                if (p.StickyForce > 0f && speedFraction >= Mathf.Clamp01(p.StickyThreshold))
                    stickyForce = -normal * (p.StickyForce * Mathf.Abs(forwardSpeed) * massShare * forceWeight * (bounceHeld ? 0f : 1f));
                native.ApplyForce(stickyForce, contactPoint, true);
                wheelForceTorque += Vector3.Cross(contactPoint - centerOfMass, stickyForce);
                if (actualGrounded && !wasGrounded)
                {
                    float landingSpeed = Mathf.Max(0f, -Vector3.Dot(contactVelocity, normal));
                    ApplyWheelTargetSpeedLoss(ref w, landingSpeed, massShare, p,
                        1f + Mathf.Max(0f, p.GroundMaterialResistance));
                }
                float sideSpeed = Vector3.Dot(contactVelocity, sideDirection);
                float rollSpeed = Vector3.Dot(contactVelocity, rollDirection);
                w.RollSpeed = rollSpeed;

                // Sideways grip: cancel sliding, up to what the tyre can hold.
                float grip = w.IsFront ? p.FrontGrip : p.RearGrip;
                if (drifting && !arcadeDrift)
                    grip *= (w.IsFront ? p.DriftFrontGrip : p.DriftRearGrip) * (1f - 0.5f * DriftEase(wheels, state, p));
                grip *= (arcadeDrift ? 0f : state.DriftGripReturn) * state.BounceGrip;
                if (wheel.HasHandbrake && handbrake)
                    grip *= p.HandbrakeGrip;
                // The ground it's on (Surface Materials): Friction is grip (cornering, traction, braking), relative
                // to the default material's; Wetness takes some of it away; soft ground (Hardness under the
                // default's) drags and eats drive.
                SurfaceFactors(w.Surface, p, out float surfaceGrip, out float softness, out _);
                grip *= surfaceGrip;
                float limit = p.MaxTyreAcceleration * load * surfaceGrip;
                float sideAcceleration = Mathf.Clamp(-sideSpeed * Mathf.Min(grip, 1f / dt), -limit, limit);

                // Along the tyre: engine, brakes, handbrake, rolling resistance.
                float rollAcceleration = 0f;
                if (wheel.Drives && driveCount > 0)
                    rollAcceleration += driveAcceleration * wheelCount / driveCount
                        * Mathf.Min(1f, surfaceGrip) * (1f - softness * Mathf.Clamp01(p.SurfaceSoftDriveLoss));
                if (brakeDeceleration > 0f)
                    rollAcceleration -= Mathf.Sign(rollSpeed) * Mathf.Min(brakeDeceleration * Mathf.Min(1f, surfaceGrip), Mathf.Abs(rollSpeed) / dt);
                if (actualGrounded && softness > 0f)
                    rollAcceleration -= rollSpeed * softness * Mathf.Max(0f, p.SurfaceSoftDrag);
                if (wheel.HasHandbrake && handbrake && handbrakeCount > 0)
                    rollAcceleration -= Mathf.Sign(rollSpeed) * Mathf.Min(p.HandbrakeDeceleration * wheelCount / handbrakeCount, Mathf.Abs(rollSpeed) / dt);
                if (Mathf.Approximately(driveAcceleration, 0f) && brakeDeceleration <= 0f)
                    rollAcceleration -= rollSpeed * p.RollingResistance;
                if (arcadeDrift)
                    rollAcceleration = 0f; // pushed along the drift's heading instead (below)
                if (bounceHeld)
                {
                    rollAcceleration = 0f; // the bounce holds the car's motion (below)
                    sideAcceleration = 0f;
                }

                // Pushed somewhere between the road (rolls the car) and the centre of mass height (doesn't).
                // (Engine and brakes at Tyre Force Height; sideways grip at Side Force Height, the one that rolls the car.)
                float heightToCenter = Vector3.Dot(centerOfMass - contactPoint, up);
                Vector3 at = contactPoint + up * (heightToCenter * p.TyreForceHeight);
                Vector3 sideAt = contactPoint + up * (heightToCenter * sideForceHeight);
                Vector3 rollForce = rollDirection * (rollAcceleration * massShare * forceWeight);
                Vector3 sideForce = sideDirection * (sideAcceleration * massShare * forceWeight);
                Vector3 tyreForce = rollForce + sideForce;
                native.ApplyForce(rollForce, at, true);
                native.ApplyForce(sideForce, sideAt, true);
                wheelForceTorque += Vector3.Cross(at - centerOfMass, rollForce) + Vector3.Cross(sideAt - centerOfMass, sideForce);

                float normalSupport = Mathf.Max(0f, Vector3.Dot(suspensionForce, normal))
                    + Mathf.Abs(Vector3.Dot(stickyForce + tyreForce, normal));
                if (massShare > 0f && g > 0.001f)
                {
                    float tireSupport = Mathf.Clamp01(normalSupport / (massShare * g));
                    supportedWheelLoad += tireSupport;
                }
            }

            bool isGrounded = grounded > 0;
            Vector3 groundNormal = isGrounded ? sharedGroundNormal : worldUp;
            if (groundNormal.sqrMagnitude < 0.001f)
                groundNormal = up;
            bool slowSingleTireContact = grounded == 1
                && velocity.magnitude <= Mathf.Max(0f, p.SingleTireRecoveryMaxSpeed)
                && movementSpeed <= Mathf.Max(0f, p.SingleTireRecoveryMovementSpeed);
            state.SingleTireStationaryTime = slowSingleTireContact
                ? state.SingleTireStationaryTime + dt
                : 0f;
            bool singleTireRecovery = slowSingleTireContact
                && state.SingleTireStationaryTime >= Mathf.Max(0f, p.SingleTireRecoveryDelay);
            IsGrounded.Value = isGrounded;
            GroundedWheels.Value = grounded;

            // Air control's gates, merged by kind (the tyres' average) and multiplied: none on the ground, opening
            // as the car leaves it, more the higher and longer it's up.
            float airGate = gatedWheels > 0 ? (spatialGates / gatedWheels) * (temporalGates / gatedWheels) : 0f;
            UpdateJump(instance, native, p, state, velocity, gravity, touching, gatedWheels > 0 ? spatialGates / gatedWheels : 0f, mass, dt);
            ApplyGroundGlue(native, p, state, velocity, groundNormal, touching, mass, dt);
            // Pitch comes from Target Speed. Optionally (Air Throttle Gate Time above 0) it has its own gate: shut
            // while any tyre is down, opening only once Target Speed moves from where it was at takeoff, so one held
            // off a ramp doesn't pitch the car.
            float throttleNow = Mathf.Clamp(state.TargetSpeed, -1f, 1f);
            if (p.AirThrottleGateTime <= 0f)
                state.AirThrottleGate = 1f;
            else if (touching > 0)
            {
                state.AirThrottleBase = throttleNow;
                state.AirThrottleOpen = false;
                state.AirThrottleGate = 0f;
            }
            else
            {
                if (Mathf.Abs(throttleNow - state.AirThrottleBase) > 0.1f)
                    state.AirThrottleOpen = true;
                state.AirThrottleGate = Mathf.MoveTowards(state.AirThrottleGate, state.AirThrottleOpen ? 1f : 0f,
                    dt / Mathf.Max(0.01f, p.AirThrottleGateTime));
            }
            float rollStick = active ? Mathf.Clamp(steerInput, -1f, 1f) : 0f;
            float pitchStick = active ? throttleNow * Mathf.SmoothStep(0f, 1f, state.AirThrottleGate) : 0f;
            bool airControl = (p.AirRollRate > 0f || p.AirPitchRate > 0f) && airGate > 0.001f;
            // How much the player is steering the car in the air (any real input counts in full, through the gates):
            // the leveling stands aside that much.
            float airInput = airControl
                ? airGate * Mathf.Clamp01(Mathf.Max(Mathf.Abs(rollStick), Mathf.Abs(pitchStick)) * 3f)
                : 0f;
            // The air leveling is off while the player steers the car in the air, and fades back in from nothing
            // over Air Level Return Time once they let go.
            state.AirLevelReturn = airInput > 0.001f ? 0f
                : Mathf.MoveTowards(state.AirLevelReturn, 1f, dt / Mathf.Max(0.01f, p.AirLevelReturnTime));

            // Rotation helpers, as angular accelerations (turned into torque with the body's own inertia below).
            Vector3 angular = Vector3.zero;
            Vector3 rollPitchVelocity = angularVelocity - up * Vector3.Dot(angularVelocity, up);
            float supportFraction = supportedWheelLoad / Mathf.Max(1f, wheelCount);
            float supportFactor = Mathf.InverseLerp(0.25f, 0.75f, supportFraction);
            // (Never below Upright Support Floor on the ground: the leveling used to let go just as the car tipped.)
            float stabilizationFactor = isGrounded ? Mathf.Max(supportFactor, Mathf.Clamp01(p.UprightSupportFloor)) : 0f;
            // In the air: no tyre on the ground. (Not "no cast sees ground": the casts reach far past the tyres on
            // purpose, so they still saw it under a jumping car, which never counted as flying, and the air tilt
            // never ran.) Tipped far over, it's only flying with open space under it, not lying on its roof or side.
            bool airborne = touching == 0
                && (Vector3.Angle(up, worldUp) < 60f || IsAirborne(body, centerOfMass, worldUp, p));
            bool groundLeveling = (isGrounded && (p.StabilizeStrength > 0f || p.StabilizeDamping > 0f))
                || singleTireRecovery;
            bool airLeveling = airborne && (p.AirLevelStrength > 0f || p.AirLevelDamping > 0f);
            Vector3 stabilizationCompensation = Vector3.zero;
            Vector3 targetUp = isGrounded ? groundNormal : worldUp;
            // In the air: level to the surface the player is aiming the car at (see AimedLanding), so "I'm landing
            // there" gets helped onto it. The player's own rolling and pitching is the air control below, which this
            // stands aside for (and fades back in after).
            if (airborne)
                targetUp = AimedLanding(centerOfMass, up, velocity, worldUp, p, body);
            Vector3 planeForward = Vector3.ProjectOnPlane(forward, groundNormal).normalized;

            // The "whoah" lean: the turn's sideways pull throws the body outward, on a wobbly spring of its own, and
            // the Roll Save takes it back as it winds up.
            Vector3 previousVelocity = state.HasLeanVelocity ? state.LeanPreviousVelocity : velocity;
            Vector3 acceleration = state.HasLeanVelocity ? (velocity - state.LeanPreviousVelocity) / dt : Vector3.zero;
            state.LeanPreviousVelocity = velocity;
            state.HasLeanVelocity = true;
            float lateral = isGrounded && planeForward.sqrMagnitude > 0.001f
                ? Vector3.Dot(acceleration, Vector3.Cross(groundNormal, planeForward))
                : 0f;
            state.LateralAcceleration = Mathf.Lerp(state.LateralAcceleration, lateral, 1f - Mathf.Exp(-dt / 0.1f));

            // Load transfer (see the LoadTransfer variable), lightly smoothed.
            float transfer = loadLeft + loadRight > 0.5f ? (loadRight - loadLeft) / (loadLeft + loadRight) : 0f;
            state.LoadTransfer = Mathf.Lerp(state.LoadTransfer, transfer, 1f - Mathf.Exp(-dt / 0.05f));
            LoadTransfer.Value = state.LoadTransfer;

            // Rollover damping: at risk, the car's ground velocity (only the sideways slide, unless Rollover Damps
            // All) is calmed at the centre of mass, which can't tip it, more the more at risk. Not in arcade drifts
            // (their slide is the point).
            if (isGrounded && !arcadeDrift && !bounceHeld && rolloverRisk > 0f && p.RolloverDamping > 0f)
            {
                Vector3 groundVelocity = Vector3.ProjectOnPlane(velocity, groundNormal);
                Vector3 calmed = p.RolloverDampsAll
                    ? groundVelocity
                    : Vector3.Project(groundVelocity, Vector3.ProjectOnPlane(right, groundNormal).normalized);
                float fraction = 1f - Mathf.Exp(-p.RolloverDamping * rolloverRisk * dt);
                native.ApplyForceToCenter(-calmed * (fraction / dt * mass), true);
            }

            // Cornering drag: turning costs speed, in proportion to how hard (tyres scrubbing). Arcade drifts have
            // their own (Drift Speed Loss).
            if (isGrounded && !arcadeDrift && !bounceHeld && p.CorneringDrag > 0f && planarSpeed > 0.5f)
            {
                float scrub = Mathf.Min(Mathf.Abs(state.LateralAcceleration) * p.CorneringDrag, planarSpeed / dt);
                native.ApplyForceToCenter(-planarVelocity / planarSpeed * (scrub * mass), true);
            }
            float whoahTarget = 0f;
            if (isGrounded && !singleTireRecovery && p.WhoahLeanAngle > 0f && p.WhoahLeanAt > 0f)
                whoahTarget = p.WhoahLeanAngle * Mathf.Clamp(state.LateralAcceleration / p.WhoahLeanAt, -1f, 1f)
                    * (1f - Mathf.Max(0f, state.RollSave));
            float wobble = 2f * Mathf.PI * Mathf.Max(0.05f, p.WhoahWobble);
            float whoahAcceleration = (whoahTarget - state.WhoahLean) * wobble * wobble
                - state.WhoahLeanSpeed * 2f * Mathf.Clamp01(p.WhoahWobbleDamping) * wobble;
            state.WhoahLeanSpeed += whoahAcceleration * dt;
            state.WhoahLean += state.WhoahLeanSpeed * dt;

            float leanAngle = (p.GroundedTurnLeanAngle > 0f ? -state.Steer * p.GroundedTurnLeanAngle : 0f) + state.WhoahLean;
            bool leaning = isGrounded && !singleTireRecovery && planeForward.sqrMagnitude > 0.001f;
            if (leaning && Mathf.Abs(leanAngle) > 0.001f)
                targetUp = Quaternion.AngleAxis(leanAngle, planeForward) * groundNormal;
            if (targetUp.sqrMagnitude < 0.001f)
                targetUp = up;
            float transitionTime = Mathf.Max(0f, p.StabilizeTransitionTime);
            float blendStep = transitionTime > 0f ? dt / transitionTime : 1f;

            if (groundLeveling || airLeveling)
            {
                // Starting to level again (after the air, a roll, a flip): from the target itself, not from
                // whatever it was aiming at back then.
                if (state.UprightNormal.sqrMagnitude < 0.001f || state.UprightBlend <= 0f)
                    state.UprightNormal = targetUp;
                else if (transitionTime <= 0f)
                    state.UprightNormal = targetUp;
                else
                    state.UprightNormal = Vector3.RotateTowards(state.UprightNormal, targetUp,
                        Mathf.PI * blendStep, 0f).normalized;
            }
            state.UprightBlend = Mathf.MoveTowards(state.UprightBlend,
                groundLeveling || airLeveling ? 1f : 0f, blendStep);
            state.UprightSupportBlend = Mathf.MoveTowards(state.UprightSupportBlend,
                singleTireRecovery ? 1f : stabilizationFactor, blendStep);
            state.UprightAirBlend = Mathf.MoveTowards(state.UprightAirBlend,
                airLeveling ? 1f : 0f, blendStep);

            float uprightStrength = Mathf.Lerp(p.StabilizeStrength, p.AirLevelStrength, state.UprightAirBlend);
            float uprightDamping = Mathf.Lerp(p.StabilizeDamping, p.AirLevelDamping, state.UprightAirBlend);
            float uprightSupport = Mathf.Lerp(state.UprightSupportBlend, 1f, state.UprightAirBlend);
            if (singleTireRecovery)
            {
                uprightStrength = Mathf.Max(uprightStrength, p.SingleTireRecoveryStrength);
                uprightDamping = Mathf.Max(uprightDamping, p.SingleTireRecoveryDamping);
                uprightSupport = 1f;
            }
            // In-air turning (the air leveling and air control) is applied straight to the body after the torque
            // guard, not through it: the guard only ever passes a changing assist through backwards (it cancels a
            // steady one), which flipped the car over in the air and inverted air control. On the ground everything
            // still goes through the guard as before.
            Vector3 airAngular = Vector3.zero;
            if (state.UprightBlend > 0f)
            {
                Vector3 leveling = (UprightError(up, state.UprightNormal, forward) * uprightStrength
                    - rollPitchVelocity * uprightDamping) * (uprightSupport * state.UprightBlend * Mathf.Lerp(1f, state.AirLevelReturn, state.UprightAirBlend));
                if (airborne)
                    airAngular += leveling;
                else
                    angular += leveling;
                // The wobble's own motion, so the body follows it instead of the leveling smoothing it away.
                if (leaning)
                    angular += planeForward * ((whoahAcceleration + state.WhoahLeanSpeed * uprightDamping) * Mathf.Deg2Rad
                        * (uprightSupport * state.UprightBlend));
            }
            // Air control (Rocket League, gently): steering asks for a roll rate and throttle for a pitch rate, up to
            // Air Roll / Pitch Rate at full input through the gates, and the car's spin is driven toward it. Let go
            // and it's handed back to the leveling. A rate, so it turns as long as asked and stops when not.
            if (airControl && airInput > 0f && !state.SelfRighting && !state.AutoRolling)
            {
                // The rate asked for is the input through the gates; the car is driven toward it at that axis's own
                // response on each axis that has input (the gates aren't applied twice). Steering left rolls it left;
                // Target Speed forward tips the nose down.
                if (Mathf.Abs(rollStick) > 0.05f)
                {
                    float wantedRoll = -rollStick * airGate * p.AirRollRate * Mathf.Deg2Rad;
                    airAngular += forward * ((wantedRoll - Vector3.Dot(angularVelocity, forward)) * Mathf.Max(0f, p.AirRollResponse));
                }
                if (Mathf.Abs(pitchStick) > 0.05f)
                {
                    float wantedPitch = pitchStick * airGate * p.AirPitchRate * Mathf.Deg2Rad;
                    airAngular += right * ((wantedPitch - Vector3.Dot(angularVelocity, right)) * Mathf.Max(0f, p.AirPitchResponse));
                }
            }

            // Keep surface-following roll/pitch independent of suspension visuals and steering yaw.
            if (isGrounded && p.GroundPlaneTorqueCompensation > 0f)
            {
                // Half on one tyre, full from two: it used to let go entirely on one, right when the car tipped.
                float supportBlend = Mathf.Clamp01(grounded * 0.5f);
                stabilizationCompensation = -Vector3.ProjectOnPlane(wheelForceTorque, groundNormal)
                    * (Mathf.Clamp01(p.GroundPlaneTorqueCompensation) * supportBlend);
            }

            Vector3 airControlTorque = Vector3.zero;
            if (airborne)
            {
                // Really in the air (not just lying on its roof or side with the wheels off the ground).
                // (The old push: rolls with steer and pitches with Target Speed for as long as it's held, so holding
                // the throttle off a ramp spun the car forward. Only with Air Tilt Angle at 0.)
                if (active && p.AirControl > 0f && !airControl)
                    airControlTorque = ApplyAirWheelControl(native, wheels, state.Wheels, centerOfMass, up, right, forward,
                        steerInput, effectiveTargetSpeed, p.AirControl, rotation, state);
            }

            // Drift: the steering swings the tail, the counter-steer stops a spin, and the speed follows the nose.
            if (drifting && isGrounded && !arcadeDrift && !bounceHeld)
            {
                float yaw = state.Steer * p.DriftSteerAssist + state.DriftDirection * p.DriftAutoTurn;
                float excess = Mathf.Abs(driftAngle) - p.DriftMaxAngle;
                if (excess > 0f)
                    yaw += Mathf.Sign(driftAngle) * excess * Mathf.Deg2Rad * p.DriftCounterSteer;
                angular += up * yaw;

                if (p.DriftMomentumKeep > 0f && planarSpeed > 1f)
                {
                    Vector3 headingDirection = forcedDrift
                        ? state.ForcedDriftHeading
                        : Vector3.ProjectOnPlane(forward, up).normalized;
                    Vector3 heading = headingDirection * planarSpeed;
                    Vector3 change = (heading - planarVelocity) * Mathf.Min(1f, p.DriftMomentumKeep * dt);
                    native.ApplyForceToCenter(change * (mass / dt), true);
                }
            }

            state.DriftHeadingActive = arcadeDrift && isGrounded && !bounceHeld;
            if (state.DriftHeadingActive)
                ApplyArcadeDrift(native, p, state, DriftEase(wheels, state, p), groundNormal, forward, velocity, angularVelocity,
                    driveAcceleration, brakeDeceleration, topSpeed, mass, dt, ref angular);

            // Bounce: a wall (or another car) hit since last step picks the car up, carries it away and drops it
            // back, keeping its slide along the wall. Then the nose eases along the wall for a moment.
            DetectBounce(native, body, wheels, state.Wheels, p, state, velocity, up, groundNormal, dt);
            if (state.PendingBounce)
            {
                state.PendingBounce = false;
                ApplyBounce(instance, native, p, state, state.BounceBefore, velocity, groundNormal, isGrounded,
                    centerOfMass, forward);
            }
            // Flipped: report it, and turn back over after a while (or on Unflip).
            UpdateFlipped(instance, p, state, up, worldUp, velocity.magnitude, dt, out bool righting);
            Vector3 rollSpin = Vector3.zero;
            bool autoRolling = false;
            if (righting && p.SelfRightRollTime > 0f)
                autoRolling = UpdateAutoRoll(native, p, state, rotation, up, forward, worldUp, g, mass, dt, out rollSpin);
            else if (righting)
            {
                float strength = Mathf.Max(p.SelfRightStrength, 8f);
                angular += UprightError(up, worldUp, forward) * strength - rollPitchVelocity * 2f;
                if (!state.SelfRighting)
                {
                    state.SelfRighting = true;
                    native.ApplyForceToCenter(worldUp * (p.SelfRightLift * mass / dt), true);
                }
            }

            // Feet down: while some tyres stand on the ground and others are lifted, the lifted ones are pulled down
            // the car's own down, as a twist only (no shove), so the car settles back onto all its tyres.
            // (Only with the car actually tipped against the ground: off a ramp's edge the front tyres lift first
            // because the ground dropped away, and pulling them down pitched the car forward as it launched.)
            if (p.FeetDownStrength > 0f && grounded > 0 && grounded < wheelCount && !righting && !airborne
                && Vector3.Angle(up, groundNormal) >= p.FeetDownMinTilt)
            {
                Vector3 twist = Vector3.zero;
                int lifted = 0;
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i]?.Anchor == null || state.Wheels[i].Grounded)
                        continue;
                    twist += Vector3.Cross(wheels[i].Anchor.position - centerOfMass, -up);
                    lifted++;
                }
                twist = Vector3.ProjectOnPlane(twist, up);
                if (lifted > 0 && twist.sqrMagnitude > 1e-6f)
                {
                    Vector3 axis = twist.normalized;
                    float share = lifted / Mathf.Max(1f, wheelCount);
                    angular += axis * (p.FeetDownStrength * share
                        - Vector3.Dot(rollPitchVelocity, axis) * p.FeetDownDamping * share);
                }
            }

            // Roll save: a big turn (or tipping onto one side) winds it up toward 1; past 0 it rolls the car back
            // onto its wheels, harder the further it's wound. Cartoony on purpose.
            Vector3 saveGround = isGrounded ? groundNormal : worldUp;
            float tilt = Vector3.Angle(up, saveGround);
            bool bigTurn = !airborne && !righting
                && ((Mathf.Abs(state.Steer) >= p.RollSaveSteer && Mathf.Abs(forwardSpeed) >= p.RollSaveMinSpeed)
                    || (p.RollSaveTilt > 0f && tilt >= p.RollSaveTilt && tilt < p.FlippedTilt));
            state.RollSave = Mathf.Clamp(state.RollSave
                + (bigTurn ? p.RollSaveRate : -p.RollSaveRelaxRate) * dt, -1f, 1f);
            if (state.RollSave > 0f && p.RollSaveStrength > 0f && !airborne && !righting)
            {
                float save = state.RollSave;
                angular += (UprightError(up, saveGround, forward) * p.RollSaveStrength
                    - rollPitchVelocity * p.RollSaveDamping) * save;
            }
            RollSave.Value = state.RollSave;

            // For the gizmo: the plane the tyres stand on and where the car is leveling to.
            state.HasGroundPlane = isGrounded;
            state.GroundPlaneNormal = groundNormal;
            state.GroundPlanePoint = sharedGroundPoint;
            state.GroundTargetUp = state.UprightBlend > 0f ? state.UprightNormal : targetUp;

            // A bounce in control: the assists' own turning stands aside (the bounce holds the spin below).
            if (bounceHeld && !righting)
                angular = Vector3.zero;
            Vector3 requestedTorque = wheelForceTorque + stabilizationCompensation
                + (Vector3)Torque(angular, rotation, state) + airControlTorque;
            Vector3 guardedAngularAcceleration = ApplyTorqueGuard(native, requestedTorque, p, dt, rotation, state);
            // The auto-roll is a move, not a push: it sets the spin that follows its path, so the floor can't stall it.
            if (autoRolling)
                native.SetAngularVelocity((float3)rollSpin);
            // The bounce in control: each step it reads how the car is actually moving and pushes back by the
            // difference from how the bounce wants it to move (within Bounce Hold Acceleration), whatever is
            // pushing (tyres, the wall, the guard's leftovers). Applied after the torque guard, so it isn't delayed.
            else if (bounceHeld)
                HoldBounce(native, p, state, velocity, angularVelocity, groundNormal, forward, rotation, mass, dt);
            else if (airAngular.sqrMagnitude > 0f)
                native.ApplyTorque(Torque(airAngular, rotation, state), true);
            state.LastGuardedAngularAcceleration = guardedAngularAcceleration;
            state.LastTireAngularAcceleration = AngularAcceleration(wheelForceTorque, rotation, state);
            state.LastStabilizationAngularAcceleration = AngularAcceleration(stabilizationCompensation, rotation, state);
            state.LastRotationAssistAngularAcceleration = angular;
            state.LastAirControlAngularAcceleration = AngularAcceleration(airControlTorque, rotation, state);
            state.LastGroundedWheels = grounded;

            // Boost push along the road.
            if (boosting)
            {
                state.BoostTime -= dt;
                if (forwardSpeed < topSpeed)
                {
                    Vector3 direction = state.DriftHeadingActive ? state.DriftHeading
                        : isGrounded ? Vector3.ProjectOnPlane(forward, groundNormal).normalized : forward;
                    native.ApplyForceToCenter(direction * (state.BoostAcceleration * mass), true);
                }
            }
            IsBoosting.Value = state.BoostTime > 0f;

            // Air time and landings.
            if (!isGrounded)
                state.AirTime += dt;
            else
            {
                if (!state.WasGrounded && state.AirTime >= p.MinAirTimeForLanding)
                    OnLanded.Invoke(instance, state.AirTime);
                state.AirTime = 0f;
            }
            state.WasGrounded = isGrounded;
            AirTime.Value = state.AirTime;

            Speed.Value = velocity.magnitude * 3.6f;
            ForwardSpeed.Value = forwardSpeed;
            DriftAngle.Value = driftAngle;
        }

        // An arcade drift: its heading turns with the steering (plus Drift Auto Turn Rate into the drift), the car's
        // speed is swung onto it and the engine, brakes and drag push along it only; the nose is held at a slide
        // angle on top, wider steering into the drift and narrower steering out of it.
        private static void ApplyArcadeDrift(Body body, ArcadeCarProperties p, CarState state, float driftEase, Vector3 planeUp,
            Vector3 forward, Vector3 velocity, Vector3 angularVelocity, float driveAcceleration, float brakeDeceleration,
            float topSpeed, float mass, float dt, ref Vector3 angular)
        {
            Vector3 planar = Vector3.ProjectOnPlane(velocity, planeUp);
            float speed = planar.magnitude;
            Vector3 nose = Vector3.ProjectOnPlane(forward, planeUp).normalized;
            Vector3 heading = Vector3.ProjectOnPlane(state.DriftHeading, planeUp);
            // Starts along the car's travel, and starts again from it when something (a wall) turns the car hard.
            if (!state.HasDriftHeading || heading.sqrMagnitude < 0.001f
                || (speed > 2f && Vector3.Dot(planar / speed, heading.normalized) < 0.5f))
                heading = speed > 1f ? planar : nose;
            heading = heading.normalized;
            state.HasDriftHeading = true;

            float into = Mathf.Clamp(state.Steer * state.DriftDirection, -1f, 1f);
            float turnRate = state.Steer * p.DriftTurnRate + state.DriftDirection * p.DriftAutoTurnRate; // degrees/s
            // No sharper than Drift Cornering Limit allows at this speed: fast drifts sweep wide, slow ones whip round.
            if (p.DriftCorneringLimit > 0f && speed > 1f)
            {
                float maxRate = p.DriftCorneringLimit / speed * Mathf.Rad2Deg;
                turnRate = Mathf.Clamp(turnRate, -maxRate, maxRate);
            }
            heading = (Quaternion.AngleAxis(turnRate * dt, planeUp) * heading).normalized;
            state.DriftHeading = heading;

            // The nose: swung to the slide angle, turning along with the heading.
            float slide = Mathf.Lerp(p.DriftAngleMin, p.DriftAngleMax, (into + 1f) * 0.5f);
            Vector3 wantedNose = Quaternion.AngleAxis(state.DriftDirection * slide, planeUp) * heading;
            float yawError = Vector3.SignedAngle(nose, wantedNose, planeUp) * Mathf.Deg2Rad;
            float yawRate = Vector3.Dot(angularVelocity, planeUp);
            angular += planeUp * (yawError * p.DriftAngleStrength + (turnRate * Mathf.Deg2Rad - yawRate) * p.DriftAngleDamping);

            // The travel: swung onto the heading, keeping its speed.
            // (Wet or slidy ground, the drift ease: the car follows its heading more loosely and loses less speed.)
            Vector3 push = (heading * speed - planar) * (Mathf.Min(1f, p.DriftHeadingGrip * (1f - 0.5f * driftEase) * dt) / dt);
            float along = Vector3.Dot(planar, heading);
            float pushAlong = along < topSpeed ? driveAcceleration : 0f;
            float slowing = brakeDeceleration + speed * p.DriftSpeedLoss * (1f - driftEase) * Mathf.Sin(slide * Mathf.Deg2Rad);
            if (Mathf.Approximately(driveAcceleration, 0f) && brakeDeceleration <= 0f)
                slowing += along * p.RollingResistance;
            pushAlong -= Mathf.Sign(along) * Mathf.Min(Mathf.Abs(slowing), Mathf.Abs(along) / dt);
            push += heading * pushAlong;
            body.ApplyForceToCenter((float3)(push * mass), true);
        }

        // How much of a surface's normal must lie along the ground plane for it to count as a wall (from Bounce Min
        // Wall Angle: 45° = more than ~0.71).
        private static float WallFlatness(ArcadeCarProperties p)
            => Mathf.Sin(Mathf.Clamp(p.BounceMinWallAngle, 0f, 90f) * Mathf.Deg2Rad);

        // Bounces are found by casts, not by the physics contacts (whose surfaces gave odd results): every step each
        // tyre casts a sphere (half its size, from the top of the tyre) along the car's velocity on the ground
        // plane, as far as the body reaches past that tyre in that direction plus this step's travel and Bounce Look
        // Ahead. A wall-like surface (Bounce Min Wall Angle) the car is moving into at Bounce Min Speed or more starts
        // a candidate: the tyres that found one decide its direction (weighted by how hard each is coming in), its
        // strength (the hardest) and where on the car it struck (their hit points). Then, for Bounce Confirm Time,
        // those tyres cast again along that first approach direction (the car's velocity no longer points into the
        // wall once it's touching), and it only bounces if every step still finds the wall: any miss and it's
        // dropped (a glance that came and went never was a proper collision).
        private static void DetectBounce(Body body, Box3DBody ignore, Wheel[] wheels, WheelState[] wheelStates,
            ArcadeCarProperties p, CarState state, Vector3 velocity, Vector3 up, Vector3 planeUp, float dt)
        {
            if (p.BounceMinSpeed <= 0f || state.PendingBounce)
                return;
            float wall = WallFlatness(p);
            float lookAhead = Mathf.Max(0f, p.BounceLookAhead);

            if (state.BounceCandidate)
            {
                state.BounceCandidateTime += dt;
                state.BounceCandidateSteps++;
                bool stillThere = false;
                for (int i = 0; i < wheels.Length && i < 32; i++)
                    if ((state.BounceStruckWheels & (1u << i)) != 0
                        && CastWall(body, ignore, wheels[i], ref wheelStates[i], p, state.BounceApproach, Vector3.zero, up, planeUp,
                            lookAhead + 0.1f, wall, out _, out _, out _, out _))
                    {
                        stillThere = true;
                        break;
                    }
                // Any miss means there's no wall: the candidate's dropped the moment a step doesn't find it.
                if (!stillThere)
                {
                    state.BounceCandidate = false;
                    return;
                }
                state.BounceFoundSteps++;
                if (state.BounceCandidateTime < p.BounceConfirmTime)
                    return;
                state.BounceCandidate = false;
                if (state.BounceFoundSteps >= state.BounceCandidateSteps)
                {
                    state.PendingBounce = true;
                    state.LastBounceTime = Time.time;
                }
                return;
            }

            if (Time.time < state.BounceReadyTime)
                return;
            Vector3 planar = Vector3.ProjectOnPlane(velocity, planeUp);
            float speed = planar.magnitude;
            if (speed < p.BounceMinSpeed)
                return;
            Vector3 direction = planar / speed;
            Vector3 directionSum = Vector3.zero, pointSum = Vector3.zero;
            float strongest = 0f;
            int struck = 0;
            uint struckWheels = 0;
            bool offCar = false;
            for (int i = 0; i < wheels.Length && i < wheelStates.Length && i < 32; i++)
            {
                if (!CastWall(body, ignore, wheels[i], ref wheelStates[i], p, direction, planar, up, planeUp,
                        speed * dt + lookAhead, wall, out Vector3 away, out float into, out Vector3 point, out GameObject other))
                    continue;
                directionSum += away * into;
                pointSum += point;
                strongest = Mathf.Max(strongest, into);
                offCar |= IsCar(other);
                struckWheels |= 1u << i;
                struck++;
            }
            if (struck == 0)
                return;
            Vector3 awayFromWall = Vector3.ProjectOnPlane(directionSum, planeUp);
            if (awayFromWall.sqrMagnitude < 1e-6f)
                return;
            state.BounceAway = awayFromWall.normalized;
            state.BounceSpeed = strongest;
            state.BouncePoint = pointSum / struck;
            state.BounceOffCar = offCar;
            state.BounceBefore = velocity;
            state.BounceApproach = direction;
            state.BounceStruckWheels = struckWheels;
            if (p.BounceConfirmTime <= 0f)
            {
                state.PendingBounce = true;
                state.LastBounceTime = Time.time;
                return;
            }
            state.BounceCandidate = true;
            state.BounceCandidateTime = 0f;
            state.BounceCandidateSteps = 0;
            state.BounceFoundSteps = 0;
        }

        // One tyre's wall cast: a sphere half the tyre's size, from the top of the tyre along
        // `direction`, as far as the body reaches past the tyre that way plus `beyond`. The nearest wall-like surface
        // facing it (and, with a `planar` velocity, one it's moving into at Bounce Min Speed or more): its direction
        // away from the wall, how fast the car comes into it, where, and what it is.
        private static bool CastWall(Body body, Box3DBody ignore, Wheel wheel, ref WheelState wheelState, ArcadeCarProperties p,
            Vector3 direction, Vector3 planar, Vector3 up, Vector3 planeUp, float beyond, float wall,
            out Vector3 away, out float into, out Vector3 point, out GameObject other)
        {
            away = Vector3.zero;
            into = 0f;
            point = Vector3.zero;
            other = null;
            if (wheel?.Anchor == null)
                return false;
            float radius = Mathf.Max(0.05f, wheel.Radius);
            Vector3 centre = wheel.Anchor.position - up * wheelState.Extension;
            // How far the body sticks out past this tyre the way it's looking (the bumper's overhang).
            B3Aabb bounds = body.ComputeAABB();
            Vector3 lower = (Vector3)(float3)bounds.LowerBound, upper = (Vector3)(float3)bounds.UpperBound;
            float reach = 0f;
            for (int c = 0; c < 8; c++)
            {
                var corner = new Vector3((c & 1) != 0 ? upper.x : lower.x, (c & 2) != 0 ? upper.y : lower.y, (c & 4) != 0 ? upper.z : lower.z);
                reach = Mathf.Max(reach, Vector3.Dot(corner - centre, direction));
            }
            var hits = _bounceHits ??= new RaycastHit[16];
            var objects = _bounceObjects ??= new GameObject[16];
            // From the top of the tyre, half its size: the sphere's bottom stays above the tyre's centre, so kerbs,
            // bumps and anything else under the tyres aren't walls.
            int count = SchematicPhysics.SphereCastAll(centre + up * radius, radius * 0.5f, direction,
                reach + beyond, p.GroundLayers, hits, objects, ignore, includeSensors: false);
            // For the gizmo.
            wheelState.WallCastFrom = centre + up * radius;
            wheelState.WallCastTo = wheelState.WallCastFrom + direction * (reach + beyond);
            wheelState.WallCastRadius = radius * 0.5f;
            wheelState.WallCastTime = Time.time;
            float nearest = float.MaxValue;
            bool found = false;
            for (int h = 0; h < count; h++)
            {
                Vector3 flat = Vector3.ProjectOnPlane(hits[h].normal, planeUp);
                if (flat.magnitude < wall || hits[h].distance >= nearest || Vector3.Dot(flat, direction) >= 0f)
                    continue;
                flat.Normalize();
                float coming = planar.sqrMagnitude > 0f ? -Vector3.Dot(planar, flat) : 0f;
                if (planar.sqrMagnitude > 0f && coming < p.BounceMinSpeed)
                    continue;
                nearest = hits[h].distance;
                away = flat;
                into = coming;
                point = hits[h].point;
                other = objects[h];
                found = true;
            }
            return found;
        }

        private static RaycastHit[] _bounceHits;
        private static GameObject[] _bounceObjects;

        // The bounce: the velocity into the wall goes, the slide along it stays (less a little, more head-on), a push
        // carries the car away from the wall, and a hard enough hit on the ground hops it so the tyres don't grind
        // (none in the air). Against another car each bounces half as hard (it gets its own impact).
        private void ApplyBounce(BehaviorInstance instance, Body body, ArcadeCarProperties p, CarState state,
            Vector3 before, Vector3 now, Vector3 planeUp, bool grounded, Vector3 centerOfMass, Vector3 forward)
        {
            Vector3 away = state.BounceAway;
            float share = state.BounceOffCar ? 0.5f : 1f;
            Vector3 planar = Vector3.ProjectOnPlane(before, planeUp);
            float speed = planar.magnitude;
            float intoWall = -Vector3.Dot(planar, away);
            Vector3 along = intoWall > 0f ? planar + away * intoWall : planar;
            float headOn = speed > 0.1f ? Mathf.Clamp01(intoWall / speed) : 1f;
            along *= 1f - Mathf.Clamp01(p.BounceSpeedLoss) * headOn;
            float push = Mathf.Min(state.BounceSpeed * p.BouncePush, p.BounceMaxPush) * share;
            float rising = Mathf.Max(0f, Vector3.Dot(now, planeUp));
            Vector3 result = along + away * push + planeUp * rising;
            if (grounded && p.BounceHop > 0f && state.BounceSpeed >= p.BounceHopSpeed)
            {
                float hop = p.BounceHop * Mathf.Lerp(0.5f, 1f,
                    Mathf.InverseLerp(p.BounceHopSpeed, p.BounceHopSpeed * 3f, state.BounceSpeed));
                result += planeUp * Mathf.Max(0f, hop - rising);
            }
            body.SetLinearVelocity((float3)result);
            state.BounceTargetVelocity = along + away * push;
            state.TorqueGuardAcceleration = Vector3.zero; // nothing smoothed from before the hit carries over

            // The hit's own spin goes: the physics gives an off-centre hit a twist, and with the car hopped off its
            // tyres (and their grip eased) nothing stopped it, so a head-on hit could spin it round and round.
            // Yaw is dropped and the hit's tumble halved; only the Spin Kick (if on) adds a deliberate swing.
            Vector3 spin = (Vector3)(float3)body.GetAngularVelocity();
            spin = Vector3.ProjectOnPlane(spin, planeUp) * 0.5f;
            if (p.BounceSpinKick > 0f)
            {
                float side = Mathf.Sign(Vector3.Dot(Vector3.Cross(state.BouncePoint - centerOfMass, away), planeUp));
                spin += planeUp * (side * p.BounceSpinKick * state.BounceSpeed * share);
            }
            body.SetAngularVelocity((float3)spin);

            // For Bounce Align Time the car's yaw is held steady, and the nose eases along the wall (the way it was
            // already going) when it slides along one, so it lands pointing down the road. (A head-on hit has no
            // slide to turn to: just steadied.)
            state.BounceAlignTime = Mathf.Max(0.1f, p.BounceAlignTime);
            state.BounceAlignHasDirection = p.BounceAlign > 0f && along.sqrMagnitude > 1f;
            if (state.BounceAlignHasDirection)
                state.BounceAlignDirection = Vector3.Dot(along, forward) >= 0f ? along.normalized : -along.normalized;
            state.BounceGrip = 1f - Mathf.Clamp01(p.BounceGripEase);
            state.HasDriftHeading = false;
            state.BounceReadyTime = Time.time + Mathf.Max(0f, p.BounceCooldown);

            // The body mesh's wobble: tipped away from the wall, and squashed, on its springs (see OnLateUpdate).
            float wobble = 2f * Mathf.PI * Mathf.Max(0.1f, p.BodyWobbleFrequency);
            float tilt = Mathf.Min(state.BounceSpeed * share * p.BodyWobble, p.BodyWobbleMax);
            Vector3 tiltAxis = Vector3.Cross(planeUp, away);
            if (tiltAxis.sqrMagnitude > 0.001f)
                state.WobbleTiltSpeed += Quaternion.Inverse(instance.Transform.rotation) * tiltAxis.normalized * (tilt * wobble);
            state.WobbleSquashSpeed += Mathf.Min(state.BounceSpeed * share * p.BodySquash, 0.3f) * wobble;

            OnBounce.Invoke(instance, state.BounceSpeed * share);
        }

        // The bounce holding the car: its ground velocity on the bounce's (the slide along the wall plus the push
        // away), its roll and pitch still, and its yaw either still or easing the nose along the wall. Counter-forces
        // for the difference each step, capped at Bounce Hold Acceleration so it stays physical.
        private static void HoldBounce(Body body, ArcadeCarProperties p, CarState state, Vector3 velocity,
            Vector3 angularVelocity, Vector3 planeUp, Vector3 forward, Quaternion rotation, float mass, float dt)
        {
            state.BounceAlignTime -= dt;
            float most = Mathf.Max(1f, p.BounceHoldAcceleration);

            Vector3 planar = Vector3.ProjectOnPlane(velocity, planeUp);
            Vector3 wanted = Vector3.ProjectOnPlane(state.BounceTargetVelocity, planeUp);
            Vector3 fix = Vector3.ClampMagnitude((wanted - planar) / dt, most);
            body.ApplyForceToCenter((float3)(fix * mass), true);

            float wantedYaw = 0f;
            Vector3 nose = Vector3.ProjectOnPlane(forward, planeUp);
            Vector3 along = Vector3.ProjectOnPlane(state.BounceAlignDirection, planeUp);
            if (state.BounceAlignHasDirection && p.BounceSpinKick <= 0f && nose.sqrMagnitude > 0.001f && along.sqrMagnitude > 0.001f)
                wantedYaw = Mathf.Clamp(Vector3.SignedAngle(nose, along, planeUp) * Mathf.Deg2Rad * 8f * p.BounceAlign, -6f, 6f);
            Vector3 wantedSpin = planeUp * wantedYaw;
            if (p.BounceSpinKick > 0f)
                wantedSpin = planeUp * Vector3.Dot(angularVelocity, planeUp); // a Spin Kick keeps its swing
            Vector3 spinFix = Vector3.ClampMagnitude((wantedSpin - angularVelocity) / dt, most);
            body.ApplyTorque(Torque(spinFix, rotation, state), true);
        }

        // Jumps: leaving the ground is noted at takeoff (how fast it went away from the ground it was on); once it's
        // been up Jump Min Air Time, it's a jump if it launched at Jump Min Launch Speed or more (a ramp, a crest), or
        // the ground has dropped well away (a ledge); a bump is neither. A launch gets a boost (more on a slow car than
        // its speed alone would give), lifted over Jump Boost Time; at the peak it hovers for Hover Time; around it
        // gravity eases off (Hang Gravity), and on the way down it's Fall Gravity, for a float and then a snappy
        // landing. Max Hang Time ends the float.
        private void UpdateJump(BehaviorInstance instance, Body body, ArcadeCarProperties p, CarState state,
            Vector3 velocity, Vector3 gravity, int touching, float clearance, float mass, float dt)
        {
            Vector3 worldUp = gravity.sqrMagnitude > 0.001f ? -gravity.normalized : Vector3.up;
            if (touching > 0)
            {
                // Landing after real air time arms the ground glue (ApplyGroundGlue).
                if (state.InAirFor >= p.GroundGlueMinAirTime && p.GroundGlueTime > 0f)
                    state.GlueTime = p.GroundGlueTime;
                if (state.Jumping)
                {
                    state.Jumping = false;
                    IsJumping.Value = false;
                }
                state.InAirFor = -1f;
                state.TakeoffNormal = state.HasGroundPlane ? state.GroundPlaneNormal : worldUp;
                return;
            }
            if (state.InAirFor < 0f)
            {
                // Takeoff: how fast it's leaving the ground it was on.
                state.InAirFor = 0f;
                state.TakeoffLaunch = Mathf.Max(0f, Vector3.Dot(velocity, state.TakeoffNormal));
                // Faster takeoffs float longer: the hover and hang times are scaled by 1 + Jump Time Speed Factor x
                // (speed / Max Speed), from the speed it left the ground at.
                float takeoffSpeed = Vector3.ProjectOnPlane(velocity, state.TakeoffNormal).magnitude;
                state.JumpTimeScale = 1f + Mathf.Max(0f, p.JumpTimeSpeedFactor)
                    * Mathf.Clamp01(takeoffSpeed / Mathf.Max(0.1f, p.MaxSpeed));
            }
            state.InAirFor += dt;

            if (!state.Jumping && state.InAirFor >= p.JumpMinAirTime)
            {
                bool launched = p.JumpMinLaunchSpeed > 0f && state.TakeoffLaunch >= p.JumpMinLaunchSpeed;
                bool ledge = p.LedgeJumps && clearance >= 0.5f;
                if (launched || ledge)
                {
                    state.Jumping = true;
                    IsJumping.Value = true;
                    // The boost is spread over Jump Boost Time (a lift, easing in and out), not added at once.
                    state.JumpBoostTotal = launched
                        ? Mathf.Min(p.JumpBoost + state.TakeoffLaunch * p.JumpBoostScale, p.JumpBoostMax)
                        : 0f;
                    state.JumpBoostClock = 0f;
                    state.HoverClock = -1f;
                    OnJump.Invoke(instance, launched ? state.TakeoffLaunch : 0f);
                }
            }
            if (!state.Jumping)
                return;

            // The boost's lift: a sine-shaped push over Jump Boost Time that adds up to the whole boost.
            bool lifting = false;
            // (Its time, like the hover's, stretches with the takeoff speed.)
            float boostTime = p.JumpBoostTime * state.JumpTimeScale;
            if (state.JumpBoostTotal > 0f && state.JumpBoostClock < boostTime)
            {
                float time = Mathf.Max(0.01f, boostTime);
                float lift = state.JumpBoostTotal * Mathf.PI / (2f * time) * Mathf.Sin(Mathf.PI * state.JumpBoostClock / time);
                body.ApplyForceToCenter((float3)(worldUp * (lift * mass)), true);
                state.JumpBoostClock += dt;
                lifting = true;
            }
            else if (state.JumpBoostTotal > 0f && boostTime <= 0f && state.JumpBoostClock <= 0f)
            {
                // No lift time: all at once.
                body.SetLinearVelocity((float3)(velocity + worldUp * state.JumpBoostTotal));
                velocity += worldUp * state.JumpBoostTotal;
                state.JumpBoostClock = 1f;
            }

            float maxHangTime = p.MaxHangTime * state.JumpTimeScale;
            float hoverTime = p.HoverTime * state.JumpTimeScale;
            if (maxHangTime <= 0f || state.InAirFor > maxHangTime)
                return;
            float rising = Vector3.Dot(velocity, worldUp);

            // The hover: reaching the peak (after the lift), it hangs there for Hover Time at Hover Gravity.
            if (!lifting && state.HoverClock < 0f && rising <= 0f && hoverTime > 0f)
                state.HoverClock = 0f;
            if (state.HoverClock >= 0f && state.HoverClock < hoverTime)
            {
                state.HoverClock += dt;
                body.ApplyForceToCenter((float3)(gravity * ((Mathf.Clamp01(p.HoverGravity) - 1f) * mass)), true);
                return;
            }

            // The hang: how close it is to the peak (upward speed near 0), and how it eases back at the end.
            float nearPeak = 1f - Mathf.Clamp01(Mathf.Abs(rising) / Mathf.Max(0.1f, p.PeakFloatWindow));
            float scale = rising >= 0f
                ? Mathf.Lerp(1f, p.HangGravity, nearPeak)
                : Mathf.Lerp(p.FallGravity, p.HangGravity, nearPeak);
            float fade = Mathf.Clamp01((maxHangTime - state.InAirFor) / Mathf.Max(0.01f, maxHangTime * 0.2f));
            scale = Mathf.Lerp(1f, scale, fade);
            // (Gravity itself still pulls; this adds the difference.)
            body.ApplyForceToCenter((float3)(gravity * ((scale - 1f) * mass)), true);
        }

        // Ground glue: for Ground Glue Time after landing, the car is held to the ground: any speed carrying it away
        // from the ground is taken off (Ground Glue Strength of it each step) and it's pressed down with Ground Glue
        // Press, fading out over the time, so a landing doesn't hop back off.
        private static void ApplyGroundGlue(Body body, ArcadeCarProperties p, CarState state, Vector3 velocity,
            Vector3 groundNormal, int touching, float mass, float dt)
        {
            if (state.GlueTime <= 0f)
                return;
            state.GlueTime -= dt;
            if (touching == 0 || dt <= 0f)
                return;
            float left = Mathf.Clamp01(state.GlueTime / Mathf.Max(0.01f, p.GroundGlueTime));
            float away = Vector3.Dot(velocity, groundNormal);
            Vector3 push = -groundNormal * (p.GroundGluePress * left);
            if (away > 0f)
                push -= groundNormal * (away / dt * Mathf.Clamp01(p.GroundGlueStrength));
            body.ApplyForceToCenter((float3)(push * mass), true);
        }

        /// <summary>
        /// Call before moving a car on purpose (a respawn, a teleporter), so the position guard doesn't take the jump
        /// for a glitch and put it back.
        /// </summary>
        public void MarkTeleported(BehaviorInstance instance)
        {
            var state = _state[instance];
            if (state != null)
                state.HasGuardPosition = false;
        }

        private void UpdateDrift(BehaviorInstance instance, ArcadeCarProperties p, CarState state, bool active, bool handbrake,
            float steerInput, float throttle, float planarSpeed, float forwardSpeed, float driftAngle, float mass, float dt, Vector3 up)
        {
            bool grounded = state.WasGrounded;
            if (!IsDrifting.Value)
            {
                if (!active || !grounded || p.DriftEntry == DriftEntryMode.Off || planarSpeed < p.DriftMinSpeed || forwardSpeed <= 0f)
                    return;
                bool steering = Mathf.Abs(steerInput) >= p.DriftSteerThreshold;
                bool byHandbrake = handbrake && steering;
                bool bySteer = steering && throttle > 0.5f && Mathf.Abs(state.Steer) >= p.DriftSteerThreshold;
                bool start = p.DriftEntry switch
                {
                    DriftEntryMode.Handbrake => byHandbrake,
                    DriftEntryMode.SharpSteer => bySteer,
                    DriftEntryMode.HandbrakeOrSharpSteer => byHandbrake || bySteer,
                    _ => false,
                };
                if (!start)
                    return;

                IsDrifting.Value = true;
                state.DriftDirection = Mathf.Sign(steerInput);
                state.HasDriftHeading = false;
                state.DriftTime = 0f;
                state.DriftLevel = 0;
                DriftCharge.Value = 0f;
                DriftLevel.Value = 0;
                if (p.DriftHop > 0f)
                {
                    var body = instance.GetComponent<Box3DBody>().Body;
                    body.ApplyForceToCenter((float3)(up * (p.DriftHop * mass / dt)), true);
                }
                OnDriftStarted.Invoke(instance);
                return;
            }

            state.DriftTime += dt;
            // (An arcade drift holds its own angle, so without the button it ends when the steering lets go of it.)
            bool held = p.HoldButtonToDrift
                ? handbrake
                : p.DriftStyle == DriftHandling.Arcade
                    ? handbrake || state.Steer * state.DriftDirection >= p.DriftExitSteer || state.DriftTime < 0.3f
                    : handbrake || Mathf.Abs(driftAngle) > p.DriftExitAngle || state.DriftTime < 0.3f;
            bool spunOut = Mathf.Abs(driftAngle) > 100f;
            bool tooSlow = planarSpeed < p.DriftMinSpeed * 0.5f || forwardSpeed < 0f;
            bool airborneTooLong = state.AirTime > 0.6f;

            if (!active || !held || spunOut || tooSlow || airborneTooLong)
            {
                EndDrift(instance, p, state, giveBoost: active && !spunOut && grounded);
                return;
            }

            // Mini-turbo charge: faster while steering into the drift.
            if (p.MiniTurbo && grounded)
            {
                float into = Mathf.Clamp01(state.Steer * state.DriftDirection);
                float charge = DriftCharge.Value + p.ChargeRate * dt * (1f + p.TightTurnChargeBonus * into);
                DriftCharge.Value = charge;

                int level = 0;
                if (p.TurboLevels != null)
                    for (int i = 0; i < p.TurboLevels.Length; i++)
                        if (p.TurboLevels[i] != null && charge >= p.TurboLevels[i].Charge)
                            level = i + 1;
                if (level > state.DriftLevel)
                {
                    state.DriftLevel = level;
                    DriftLevel.Value = level;
                    OnDriftLevel.Invoke(instance, level);
                }
            }
        }

        private void EndDrift(BehaviorInstance instance, ArcadeCarProperties p, CarState state, bool giveBoost)
        {
            IsDrifting.Value = false;
            OnDriftEnded.Invoke(instance, DriftCharge.Value);

            int level = state.DriftLevel;
            if (giveBoost && p.MiniTurbo && level > 0 && p.TurboLevels != null && level <= p.TurboLevels.Length && p.TurboLevels[level - 1] != null)
            {
                var stage = p.TurboLevels[level - 1];
                StartBoost(instance, stage.Acceleration, stage.Duration);
                OnBoost.Invoke(instance, level);
            }

            state.DriftLevel = 0;
            DriftLevel.Value = 0;
            DriftCharge.Value = 0f;
        }

        private void UpdateFlipped(BehaviorInstance instance, ArcadeCarProperties p, CarState state, Vector3 up, Vector3 worldUp,
            float speed, float dt, out bool righting)
        {
            float tilt = Vector3.Angle(up, worldUp);
            bool tilted = tilt > p.FlippedTilt;

            if (tilted && speed < p.SelfRightMaxSpeed)
                state.FlippedTime += dt;
            else if (!tilted)
                state.FlippedTime = 0f;
            // Stuck: on its roof or its side (past Self Right Tilt), nearly stopped. Counted on its own, so a car
            // lying on its side (90°, not "flipped") still rights itself.
            bool stuckTilted = tilt > Mathf.Min(p.FlippedTilt, p.SelfRightTilt);
            if (stuckTilted && speed < p.SelfRightMaxSpeed)
                state.StuckTime += dt;
            else if (!stuckTilted)
                state.StuckTime = 0f;

            if (!IsFlipped.Value && tilted && state.FlippedTime > 0.5f)
            {
                IsFlipped.Value = true;
                OnFlipped.Invoke(instance);
            }

            // Back on its wheels (near enough): stop righting; a car that's merely leaning ignores Unflip. (An
            // auto-roll finishes its move first.)
            if (tilt < 45f && !state.AutoRolling)
            {
                state.SelfRighting = false;
                state.UnflipQueued = false;
                state.StuckTime = 0f;
                if (IsFlipped.Value)
                {
                    IsFlipped.Value = false;
                    state.FlippedTime = 0f;
                    OnRighted.Invoke(instance);
                }
            }

            righting = state.UnflipQueued
                       || (p.SelfRightStrength > 0f && state.StuckTime >= p.SelfRightDelay)
                       || state.SelfRighting || state.AutoRolling;
        }

        // The auto-roll: stuck on its roof or side for Self Right Delay, the car hops (Self Right Lift) and rolls
        // over its nose back onto its wheels in Self Right Roll Time, the short way round, ending level and facing
        // the way it was. Returns false once it's done (out: the spin to set this step).
        private static bool UpdateAutoRoll(Body body, ArcadeCarProperties p, CarState state, Quaternion rotation,
            Vector3 up, Vector3 forward, Vector3 worldUp, float g, float mass, float dt, out Vector3 spin)
        {
            spin = Vector3.zero;
            if (!state.AutoRolling)
            {
                Vector3 axis = Vector3.ProjectOnPlane(forward, worldUp);
                if (axis.sqrMagnitude < 0.01f)
                    axis = Vector3.ProjectOnPlane(rotation * Vector3.down, worldUp); // nose straight up or down
                if (axis.sqrMagnitude < 0.01f)
                    axis = Vector3.forward;
                axis.Normalize();
                Vector3 upAcross = Vector3.ProjectOnPlane(up, axis);
                float angle = upAcross.sqrMagnitude > 0.001f ? Vector3.SignedAngle(upAcross, worldUp, axis) : 180f;
                if (Mathf.Abs(angle) > 179f)
                    angle = 180f; // right on its roof: either way, pick one
                state.AutoRolling = true;
                state.SelfRighting = true;
                state.AutoRollAxis = axis;
                state.AutoRollAngle = angle;
                state.AutoRollStart = rotation;
                state.AutoRollEnd = Quaternion.LookRotation(axis, worldUp);
                state.AutoRollTime = 0f;
                body.ApplyForceToCenter((float3)(worldUp * (p.SelfRightLift * mass / dt)), true);
            }

            float duration = Mathf.Max(0.05f, p.SelfRightRollTime);
            state.AutoRollTime += dt;
            if (state.AutoRollTime >= duration)
            {
                state.AutoRolling = false;
                state.FlippedTime = 0f;
                state.StuckTime = 0f;
                spin = Vector3.zero;
                // Lands with a clean slate: nothing remembered from upside down pulls it back over.
                state.UprightNormal = worldUp;
                state.TorqueGuardAcceleration = Vector3.zero;
                state.HasGroundPlane = false;
                for (int i = 0; i < state.Wheels.Length; i++)
                    state.Wheels[i].ContactNormal = worldUp;
                return true; // this last step: stop the spin
            }

            // Floaty while it turns over (half of gravity held up), so it clears the floor.
            body.ApplyForceToCenter((float3)(worldUp * (0.5f * g * mass)), true);

            // Where it should be a step from now: rolled along the eased path, settling level at the end.
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((state.AutoRollTime + dt) / duration));
            Quaternion rolled = Quaternion.AngleAxis(state.AutoRollAngle * s, state.AutoRollAxis) * state.AutoRollStart;
            Quaternion target = Quaternion.Slerp(rolled, state.AutoRollEnd, s * s);
            (target * Quaternion.Inverse(rotation)).ToAngleAxis(out float degrees, out Vector3 turnAxis);
            if (degrees > 180f)
                degrees -= 360f;
            if (float.IsNaN(turnAxis.x) || float.IsInfinity(turnAxis.x))
                return true;
            spin = turnAxis.normalized * (degrees * Mathf.Deg2Rad / dt);
            return true;
        }

        // With no wheel touching, is there ground right under the car? (Lying on its roof or side isn't flying, and
        // shouldn't get air control or levelling, so a flipped car only turns back over if self-righting says so.)
        private static bool IsAirborne(Box3DBody body, Vector3 centerOfMass, Vector3 worldUp, ArcadeCarProperties p)
            => !SchematicPhysics.Raycast(centerOfMass, -worldUp, out _, p.SuspensionRestLength + AirborneClearance, p.GroundLayers, body);

        /// <summary>How far below the centre of mass (plus the suspension length) the ground must be for the car to count as flying.</summary>
        public const float AirborneClearance = 1.2f;

        /// <summary>How far below the car (metres) the air leveling looks for the ground it'll land on.</summary>
        public const float AirLandingProbe = 40f;

        // Steering kept within what the tyres can corner at this speed: a turn of wheel angle θ at speed v asks
        // for about v² tan θ / wheelbase of sideways grip, so the most lock shrinks with the square of speed.
        private static float LimitSteer(float steerAngle, float forwardSpeed, float wheelbase, float corneringLimit)
        {
            float speed = Mathf.Abs(forwardSpeed);
            if (corneringLimit <= 0f || speed < 1f)
                return steerAngle;
            float most = Mathf.Atan(wheelbase * corneringLimit / (speed * speed)) * Mathf.Rad2Deg;
            return Mathf.Clamp(steerAngle, -most, most);
        }

        // Front axle to rear axle (steering wheels to the others, along the car), in metres.
        private static float Wheelbase(Wheel[] wheels, Transform car)
        {
            float front = 0f, rear = 0f;
            int fronts = 0, rears = 0;
            foreach (var wheel in wheels)
            {
                if (wheel?.Anchor == null)
                    continue;
                float z = car.InverseTransformPoint(wheel.Anchor.position).z;
                if (wheel.Steers) { front += z; fronts++; }
                else { rear += z; rears++; }
            }
            return fronts > 0 && rears > 0 ? Mathf.Max(0.5f, Mathf.Abs(front / fronts - rear / rears)) : 2.5f;
        }

        // The project's default ground (its Friction and Hardness are "normal": surfaces count relative to them).
        private const float NormalFriction = 0.6f, NormalHardness = 0.5f;

        // What a tyre is driving on: the object its ground cast hit (its Surface Material Tag, or the project's
        // default), or a painted surface there, looked for every fourth step per tyre (staggered).
        private static void UpdateWheelSurface(ref WheelState w, GameObject ground, int index, GameObject car)
        {
            if (ground != w.SurfaceObject)
            {
                w.SurfaceObject = ground;
                w.TaggedSurface = SurfaceMaterialTag.Of(ground) ?? SurfaceLibrary.DefaultMaterial;
                w.Surface = w.TaggedSurface;
            }
            if (((Time.frameCount + index) & 3) != 0)
                return;
            Vector3 normal = w.GroundHit.normal;
            w.Surface = SurfacePaintLayer.Raycast(w.GroundHit.point + normal * 0.3f, -normal, 0.6f, out SurfaceHit painted)
                && painted.Material != null
                ? painted.Material
                : w.TaggedSurface;
        }

        // A surface's effect on a tyre: its grip (Friction relative to normal, less on wet ground; Surface Grip
        // Influence of the difference), how soft it is (0 normal or harder, 1 softest) and how much easier it makes
        // drifting (its Wetness, or its own "Drift" property).
        private static void SurfaceFactors(SurfaceMaterial surface, ArcadeCarProperties p, out float grip, out float softness,
            out float driftEase)
        {
            if (surface == null)
            {
                grip = 1f;
                softness = 0f;
                driftEase = 0f;
                return;
            }
            float friction = Mathf.Clamp(surface.Friction / NormalFriction, 0f, 2f);
            grip = Mathf.Lerp(1f, friction, Mathf.Clamp01(p.SurfaceGripInfluence))
                * (1f - Mathf.Clamp01(surface.Wetness) * Mathf.Clamp01(p.SurfaceWetGripLoss));
            softness = Mathf.Clamp01((NormalHardness - surface.Hardness) / NormalHardness);
            driftEase = Mathf.Clamp01((surface.TryGet("Drift", out float drift) ? drift : surface.Wetness)
                * Mathf.Clamp01(p.SurfaceWetDriftBonus));
        }

        // The tyres on the ground's average drift ease.
        private static float DriftEase(Wheel[] wheels, CarState state, ArcadeCarProperties p)
        {
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < wheels.Length && i < state.Wheels.Length; i++)
            {
                if (!state.Wheels[i].Grounded)
                    continue;
                SurfaceFactors(state.Wheels[i].Surface, p, out _, out _, out float ease);
                sum += ease;
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        // The bent ground cast: from `startAbove` over the anchor straight down the car's `up` to the top of the tyre
        // (`tyreDrop` under the anchor; negative if it's above it), then `beyond` further along -`toward` (the ground
        // the tyres stood on). The hit's distance comes back as the distance down the suspension from the anchor to
        // the ground (to the bend, then along `toward`, plus the tyre's radius). `ceiling`: the first hit was a ceiling
        // (touching where the cast starts, or facing away from the car), and nothing came back.
        private static bool CastWheel(Vector3 anchor, float startAbove, Vector3 up, Vector3 toward, float radius,
            float tyreDrop, float beyond, LayerMask mask, Box3DBody body, out RaycastHit hit, out bool ceiling,
            out GameObject hitObject)
        {
            ceiling = false;
            float first = startAbove + tyreDrop;
            hitObject = null;
            if (first > 0f && SchematicPhysics.SphereCast(anchor + up * startAbove, radius, -up, out hit, out hitObject,
                    first, mask, body))
            {
                if (IsCeiling(hit, up))
                {
                    ceiling = true;
                    return false;
                }
                hit.distance = hit.distance - startAbove + radius;
                return true;
            }
            Vector3 bend = anchor - up * tyreDrop;
            if (beyond > 0f && SchematicPhysics.SphereCast(bend, radius, -toward, out hit, out hitObject, beyond, mask, body))
            {
                if (Vector3.Dot(hit.normal, toward) <= 0.05f)
                {
                    ceiling = true;
                    return false;
                }
                hit.distance = tyreDrop + hit.distance + radius;
                return true;
            }
            hit = default;
            return false;
        }

        // The surface the player is aiming the car at, to land on: looked for along where the car's wheels point
        // carried on along its motion (between its down and its velocity), then straight along its down, then
        // along its motion, then straight down. A surface counts if it faces the look and isn't steeper than Air Aim
        // Max Slope from world up (not a wall); world up if none.
        private static Vector3 AimedLanding(Vector3 centerOfMass, Vector3 up, Vector3 velocity, Vector3 worldUp,
            ArcadeCarProperties p, Box3DBody body)
        {
            Vector3 down = -up;
            Vector3 moving = velocity.sqrMagnitude > 1f ? velocity.normalized : -worldUp;
            Vector3 aimed = down + moving;
            float steepest = Mathf.Cos(Mathf.Clamp(p.AirAimMaxSlope, 0f, 89f) * Mathf.Deg2Rad);
            for (int i = 0; i < 4; i++)
            {
                Vector3 look = i switch { 0 => aimed, 1 => down, 2 => moving, _ => -worldUp };
                if (look.sqrMagnitude < 0.01f)
                    continue;
                look.Normalize();
                if (SchematicPhysics.Raycast(centerOfMass, look, out RaycastHit hit, AirLandingProbe, p.GroundLayers, body)
                    && Vector3.Dot(hit.normal, -look) > 0.1f && Vector3.Dot(hit.normal, worldUp) >= steepest)
                    return hit.normal.normalized;
            }
            return worldUp;
        }

        // A probe hit that isn't ground: touching where the probe starts (the floor a flipped roof lies in, or
        // something on the roof), or a surface facing away from the car's up.
        private static bool IsCeiling(in RaycastHit hit, Vector3 up)
            => hit.distance <= 0.001f || Vector3.Dot(hit.normal, up) <= 0.05f;

        // The rotation (axis × angle, radians) that takes `up` to `target`; upside down, it rolls over the nose axis.
        private static Vector3 UprightError(Vector3 up, Vector3 target, Vector3 fallbackAxis)
        {
            Vector3 axis = Vector3.Cross(up, target);
            float angle = Vector3.Angle(up, target) * Mathf.Deg2Rad;
            if (axis.sqrMagnitude < 1e-6f)
            {
                if (Vector3.Dot(up, target) > 0f)
                    return Vector3.zero;
                axis = fallbackAxis;
            }
            return axis.normalized * angle;
        }

        // Torque for an angular acceleration, using the body's own inertia, so tuning means the same for any car.
        private static float3 Torque(Vector3 angularAcceleration, Quaternion rotation, CarState state)
        {
            Vector3 local = Quaternion.Inverse(rotation) * angularAcceleration;
            Vector3 localTorque = state.InertiaX * local.x + state.InertiaY * local.y + state.InertiaZ * local.z;
            return rotation * localTorque;
        }

        private static Vector3 AngularAcceleration(Vector3 torque, Quaternion rotation, CarState state)
        {
            Vector3 localTorque = Quaternion.Inverse(rotation) * torque;
            var inertia = new float3x3(state.InertiaX, state.InertiaY, state.InertiaZ);
            Vector3 localAcceleration = math.mul(math.inverse(inertia), (float3)localTorque);
            return rotation * localAcceleration;
        }

        private static float BodyTopOffsetAlong(Body body, Vector3 reference, Vector3 up)
        {
            B3Aabb bounds = body.ComputeAABB();
            Vector3 lower = (Vector3)(float3)bounds.LowerBound;
            Vector3 upper = (Vector3)(float3)bounds.UpperBound;
            Vector3 center = (lower + upper) * 0.5f;
            Vector3 halfExtents = (upper - lower) * 0.5f;
            float topHeight = Vector3.Dot(center, up)
                + Vector3.Dot(new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z)), halfExtents);
            return Mathf.Max(0f, topHeight - Vector3.Dot(reference, up));
        }

        private static Vector3 ApplyTorqueGuard(Body body, Vector3 requestedTorque, ArcadeCarProperties properties,
            float dt, Quaternion rotation, CarState state)
        {
            float maximum = Mathf.Max(0f, properties.TorqueGuardMaxAngularAcceleration);
            Vector3 requestedAcceleration = AngularAcceleration(requestedTorque, rotation, state);
            if (maximum > 0f && requestedAcceleration.magnitude > maximum)
                requestedAcceleration = requestedAcceleration.normalized * maximum;

            float blendTime = Mathf.Max(0f, properties.TorqueGuardBlendTime);
            float blend = blendTime > 0f ? 1f - Mathf.Exp(-dt / blendTime) : 1f;
            // Blended as a vector, axis by axis. (Turning the direction and the size separately swung a steering
            // torque that changed sides through roll and pitch on the way, so the ground plane never fully held.)
            state.TorqueGuardAcceleration = Vector3.Lerp(state.TorqueGuardAcceleration, requestedAcceleration, blend);
            Vector3 guardedAcceleration = state.TorqueGuardAcceleration;
            Vector3 guardedTorque = (Vector3)Torque(guardedAcceleration, rotation, state);
            Vector3 correction = guardedTorque - requestedTorque;
            if (correction.sqrMagnitude > 0f)
                body.ApplyTorque(correction, true);
            return guardedAcceleration;
        }

        private static Vector3 ApplyAirWheelControl(Body body, Wheel[] wheels, WheelState[] wheelStates,
            Vector3 centerOfMass, Vector3 up, Vector3 right, Vector3 forward, float steer, float targetSpeed,
            float strength, Quaternion rotation, CarState state)
        {
            float rollMoment = Vector3.Dot((Vector3)Torque(forward * (steer * strength), rotation, state), forward);
            float pitchMoment = Vector3.Dot((Vector3)Torque(right * (targetSpeed * strength), rotation, state), right);

            float rightArmSquared = 0f;
            float forwardArmSquared = 0f;
            float armCross = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i]?.Anchor == null || wheelStates[i].Grounded)
                    continue;

                Vector3 offset = wheels[i].Anchor.position - centerOfMass;
                float rightArm = Vector3.Dot(offset, right);
                float forwardArm = Vector3.Dot(offset, forward);
                rightArmSquared += rightArm * rightArm;
                forwardArmSquared += forwardArm * forwardArm;
                armCross += rightArm * forwardArm;
            }

            float determinant = rightArmSquared * forwardArmSquared - armCross * armCross;
            if (determinant < 1e-6f)
                return Vector3.zero;

            float rightCoefficient = (rollMoment * forwardArmSquared + pitchMoment * armCross) / determinant;
            float forwardCoefficient = (-pitchMoment * rightArmSquared - rollMoment * armCross) / determinant;
            Vector3 totalForce = Vector3.zero;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i]?.Anchor == null || wheelStates[i].Grounded)
                    continue;

                Vector3 anchor = wheels[i].Anchor.position;
                Vector3 offset = anchor - centerOfMass;
                float verticalForce = rightCoefficient * Vector3.Dot(offset, right)
                    + forwardCoefficient * Vector3.Dot(offset, forward);
                Vector3 force = up * verticalForce;
                body.ApplyForce(force, anchor, true);
                totalForce += force;
            }

            // Keep the per-tire forces a pure rotational control rather than also accelerating the chassis.
            if (totalForce.sqrMagnitude > 0f)
                body.ApplyForceToCenter(-totalForce, true);
            return (Vector3)Torque(forward * (steer * strength) + right * (targetSpeed * strength), rotation, state);
        }

        // Mass, centre of mass and inertia from the Properties, on top of what the shapes give (so it's the same every spawn).
        private static void ConfigureBody(Body native, ArcadeCarProperties p, CarState state)
        {
            native.SetBullet(true);
            native.ApplyMassFromShapes();
            var massData = native.GetMassData();
            float inertiaScale = Mathf.Max(0.01f, p.InertiaScale);
            if (p.Mass > 0f && massData.Mass > 0f)
            {
                inertiaScale *= p.Mass / massData.Mass;
                massData.Mass = p.Mass;
            }
            massData.Center += (float3)p.CenterOfMassOffset;
            massData.Inertia.Cx *= inertiaScale;
            massData.Inertia.Cy *= inertiaScale;
            massData.Inertia.Cz *= inertiaScale;
            native.SetMassData(massData);

            var inertia = native.GetLocalRotationalInertia();
            state.InertiaX = inertia.Cx;
            state.InertiaY = inertia.Cy;
            state.InertiaZ = inertia.Cz;
            state.BodyConfigured = true;
        }

        protected override void OnImpact(PhysicsImpact impact)
        {
            var p = Properties.Value;
            var wheels = Wheels.Value;
            var state = _state.Value;
            Vector3 impactDirection = impact.Direction.normalized;
            Vector3 groundNormal = Self.Transform.up;
            float nearestGroundDistance = float.MaxValue;
            if (wheels != null && state != null && state.Wheels.Length == wheels.Length)
            {
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i]?.Anchor == null) continue;
                    ref var wheelState = ref state.Wheels[i];
                    if (!wheelState.Grounded && (!wheelState.HasBlendedContact || wheelState.ContactForceWeight <= 0f))
                        continue;
                    Vector3 contactPoint = wheelState.Grounded ? wheelState.GroundHit.point : wheelState.Point;
                    float distance = (contactPoint - impact.Point).sqrMagnitude;
                    if (distance >= nearestGroundDistance)
                        continue;
                    nearestGroundDistance = distance;
                    groundNormal = wheelState.Grounded ? wheelState.GroundNormal : wheelState.ContactNormal;
                }
            }
            if (groundNormal.sqrMagnitude < 0.001f)
                groundNormal = Self.Transform.up;
            float crashRatio = impactDirection.sqrMagnitude > 0.001f
                ? Mathf.Clamp01(1f - Mathf.Abs(Vector3.Dot(impactDirection, groundNormal.normalized)))
                : 1f;
            float crashSpeed = impact.Speed * crashRatio;
            if (p != null && wheels != null && state != null && state.Wheels.Length == wheels.Length
                && wheels.Length > 0 && !IsProxy(Self))
            {
                int nearestWheel = -1;
                float nearestDistance = float.MaxValue;
                for (int i = 0; i < wheels.Length; i++)
                {
                    if (wheels[i]?.Anchor == null) continue;
                    float distance = (wheels[i].Anchor.position - impact.Point).sqrMagnitude;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestWheel = i;
                    }
                }
                if (nearestWheel >= 0)
                {
                    var body = Self.GetComponent<Box3DBody>();
                    float mass = p.Mass > 0f ? p.Mass : body != null && body.IsLive() ? body.Body.GetMass() : 0f;
                    float massShare = mass / wheels.Length;
                    ApplyWheelTargetSpeedLoss(ref state.Wheels[nearestWheel], crashSpeed, massShare, p, 1f);
                }
            }
            // Whoever simulates the car reports crashes (OnCrash is networked), so proxies don't add their own.
            if (p != null && crashSpeed >= p.MinCrashSpeed && !IsProxy(Self))
                OnCrash.Invoke(Self, crashSpeed);
        }

        private static void ApplyWheelTargetSpeedLoss(ref WheelState wheel, float impactSpeed, float wheelMass,
            ArcadeCarProperties properties, float resistance)
        {
            if (impactSpeed <= 0f || wheelMass <= 0f)
                return;

            float maximumSpeed = Mathf.Max(0.1f, properties.MaxSpeed);
            float impactEnergy = 0.5f * wheelMass * impactSpeed * impactSpeed;
            float maximumEnergy = 0.5f * wheelMass * maximumSpeed * maximumSpeed;
            float normalizedEnergy = impactEnergy / maximumEnergy;
            float loss = Mathf.Clamp01(normalizedEnergy * properties.ImpactTargetSpeedLoss * resistance);
            wheel.TargetSpeedMultiplier *= 1f - loss;
        }

        #endregion

        #region Visuals

        protected override void OnLateUpdate()
        {
            var wheels = Wheels.Value;
            var state = _state.Value;
            if (wheels == null || state == null || state.Wheels.Length != wheels.Length)
                return;

            var car = Self.Transform;
            Quaternion rotation = car.rotation;
            Vector3 up = rotation * Vector3.up;
            float dt = Time.deltaTime;
            UpdateBodyWobble(car, BodyVisual.Value, Properties.Value, state, dt);

            // The tyres where the suspension has them, in the car's Schematic Bounds: its bottom is where they meet
            // the road (a camera checking for ground under the car looks from there).
            var bounds = SchematicBounds.Of(Self.GameObject);
            if (bounds != null)
                for (int i = 0; i < wheels.Length; i++)
                    if (wheels[i]?.Anchor != null)
                        bounds.SetSphere(wheels[i], wheels[i].Anchor.position - up * state.Wheels[i].Extension,
                            Mathf.Max(0.05f, wheels[i].Radius));

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                if (!HasMovableVisual(wheel))
                    continue;
                ref var w = ref state.Wheels[i];
                float radius = Mathf.Max(0.05f, wheel.Radius);
                w.Spin = (w.Spin + w.RollSpeed / radius * Mathf.Rad2Deg * dt) % 360f;

                Vector3 position = wheel.Anchor.position - up * w.Extension;
                // Spin about the axle first, then steer about the car's up.
                Quaternion turn = rotation * Quaternion.AngleAxis(w.SteerAngle, Vector3.up) * Quaternion.AngleAxis(w.Spin, Vector3.right);
                wheel.Visual.SetPositionAndRotation(position, turn * w.VisualRest);
            }
        }

        // A visual that is the anchor (or holds it) would move the probe's origin with it, so it's left alone.
        private static bool HasMovableVisual(Wheel wheel)
            => wheel != null && wheel.Anchor != null && wheel.Visual != null && wheel.Visual != wheel.Anchor && !wheel.Anchor.IsChildOf(wheel.Visual);

        // Each wheel mesh's own orientation relative to the car, remembered from the first time it's seen (pooled cars
        // come back with their wheels mid-spin).
        private sealed class RestPose { public Quaternion Rotation; }
        private static readonly ConditionalWeakTable<Transform, RestPose> _visualRest = new();

        // Every spawned car, so a bounce knows when it hit another (both bounce, each half as hard).
        private static readonly HashSet<GameObject> _cars = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCars() => _cars.Clear();

        private static bool IsCar(GameObject other)
        {
            for (var t = other != null ? other.transform : null; t != null; t = t.parent)
                if (_cars.Contains(t.gameObject))
                    return true;
            return false;
        }

        // The body mesh tips and squashes on two springs after a bounce, then settles back to its rest pose (and
        // is left alone once settled, so it doesn't fight anything else animating it).
        private static void UpdateBodyWobble(Transform car, Transform visual, ArcadeCarProperties p, CarState state, float dt)
        {
            if (visual == null || p == null || !state.HasBodyRest || dt <= 0f)
                return;
            bool moving = state.WobbleTilt.sqrMagnitude > 1e-4f || state.WobbleTiltSpeed.sqrMagnitude > 1e-4f
                || Mathf.Abs(state.WobbleSquash) > 1e-4f || Mathf.Abs(state.WobbleSquashSpeed) > 1e-4f;
            if (!moving)
            {
                if (state.WobbleShown)
                {
                    visual.localRotation = state.BodyRestRotation;
                    visual.localScale = state.BodyRestScale;
                    state.WobbleShown = false;
                }
                return;
            }
            float w = 2f * Mathf.PI * Mathf.Max(0.1f, p.BodyWobbleFrequency);
            float c = 2f * Mathf.Clamp01(p.BodyWobbleDamping) * w;
            state.WobbleTiltSpeed += (-state.WobbleTilt * (w * w) - state.WobbleTiltSpeed * c) * dt;
            state.WobbleTilt += state.WobbleTiltSpeed * dt;
            state.WobbleSquashSpeed += (-state.WobbleSquash * (w * w) - state.WobbleSquashSpeed * c) * dt;
            state.WobbleSquash += state.WobbleSquashSpeed * dt;

            // The tilt is kept in the car's space; turned into the mesh's parent's space here.
            float degrees = state.WobbleTilt.magnitude;
            Quaternion tip = Quaternion.identity;
            if (degrees > 1e-4f)
            {
                Vector3 axis = car.rotation * (state.WobbleTilt / degrees);
                if (visual.parent != null)
                    axis = visual.parent.InverseTransformDirection(axis);
                tip = Quaternion.AngleAxis(degrees, axis);
            }
            visual.localRotation = tip * state.BodyRestRotation;
            float squash = Mathf.Clamp(state.WobbleSquash, -0.5f, 0.5f);
            Vector3 rest = state.BodyRestScale;
            visual.localScale = new Vector3(rest.x * (1f + squash * 0.5f), rest.y * (1f - squash), rest.z * (1f + squash * 0.5f));
            state.WobbleShown = true;
        }

        private static Quaternion VisualRestRotation(Transform car, Transform visual)
        {
            if (!_visualRest.TryGetValue(visual, out var rest))
            {
                rest = new RestPose { Rotation = Quaternion.Inverse(car.rotation) * visual.rotation };
                _visualRest.Add(visual, rest);
            }
            return rest.Rotation;
        }

        #endregion

        #region Gizmos

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var wheels = context.Get(Wheels);
            var p = context.Get(Properties);
            var car = context.Transform;
            float rest = p != null ? p.SuspensionRestLength : 0.45f;
            var state = context.Instance != null && context.Instance.IsSpawned ? _state[context.Instance] : null;
            var body = context.GetComponent<Box3DBody>();
            Vector3 up = car.up;

            if (wheels != null)
                for (int i = 0; i < wheels.Length; i++)
                {
                    var wheel = wheels[i];
                    if (wheel?.Anchor == null) continue;
                    float radius = Mathf.Max(0.05f, wheel.Radius);
                    bool live = state != null && i < state.Wheels.Length;
                    var w = live ? state.Wheels[i] : default;
                    Vector3 origin = wheel.Anchor.position;
                    float bodyTopOffset = live && body != null && body.IsLive()
                        ? BodyTopOffsetAlong(body.Body, origin, up)
                        : 0f;
                    Vector3 probeOrigin = origin + up * bodyTopOffset;

                    // The tire-sized ground sweep: green on the ground, red in the air, cyan in Edit Mode.
                    Gizmos.color = !live ? Color.cyan : w.Grounded ? Color.green : Color.red;
                    float checkDistance = Mathf.Max(0f, p != null ? p.GroundCheckDistance : 1.6f);
                    // Bent: down the car's up to the tyre, then toward the ground the tyres stood on.
                    Vector3 bend = origin - up * ((live ? w.Extension : rest) - radius);
                    Vector3 toward = live && w.SuspensionUpLocal.sqrMagnitude > 0.5f ? car.rotation * w.SuspensionUpLocal : up;
                    Vector3 castEnd = bend - toward * Mathf.Max(0f, checkDistance);
                    Gizmos.DrawLine(probeOrigin, bend);
                    Gizmos.DrawLine(bend, castEnd);
                    Gizmos.DrawWireSphere(castEnd, radius);
                    if (live && w.Sidewall)
                    {
                        // Touching with its sidewall: not standing on it.
                        Gizmos.color = new Color(1f, 0.85f, 0.1f);
                        Gizmos.DrawWireSphere(w.GroundHit.point, 0.1f);
#if UNITY_EDITOR
                        UnityEditor.Handles.Label(w.GroundHit.point, "sidewall", UnityEditor.EditorStyles.miniLabel);
#endif
                    }
                    Gizmos.DrawWireSphere(probeOrigin, 0.04f);

                    // The ceiling check where the probe starts, at the body's top: purple, red with the hit when it
                    // found one (a roof in the floor, something on the roof).
                    bool ceiling = live && w.HasCeiling;
                    Gizmos.color = ceiling ? Color.red : new Color(0.65f, 0.4f, 1f);
                    Gizmos.DrawWireSphere(probeOrigin, radius);
                    if (ceiling)
                    {
                        Gizmos.DrawLine(probeOrigin, w.CeilingPoint);
                        Gizmos.DrawWireCube(w.CeilingPoint, Vector3.one * 0.12f);
#if UNITY_EDITOR
                        UnityEditor.Handles.Label(w.CeilingPoint, "ceiling", UnityEditor.EditorStyles.miniLabel);
#endif
                    }

                    // The wheel, steered.
                    Vector3 center = origin - up * (live ? w.Extension : rest);
                    Vector3 axle = Quaternion.AngleAxis(live ? w.SteerAngle : 0f, up) * car.right;
                    Gizmos.color = wheel.Drives ? new Color(1f, 0.6f, 0.1f) : Color.white;
                    DrawCircle(center, axle, radius);
                    if (live && w.Grounded)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawRay(w.Point, w.Normal * 0.3f);
                    }
#if UNITY_EDITOR
                    if (!live)
                        UnityEditor.Handles.Label(center, wheel.Name, UnityEditor.EditorStyles.miniLabel);
#endif
                }

            // The wall casts (bounces): green while the check over time is positive for that tyre (a wall it found
            // that every step since has found again, or the bounce it confirmed), red otherwise.
            if (state != null && wheels != null)
                for (int i = 0; i < wheels.Length && i < state.Wheels.Length; i++)
                {
                    var w = state.Wheels[i];
                    if (Time.time - w.WallCastTime > 0.1f)
                        continue;
                    bool struck = i < 32 && (state.BounceStruckWheels & (1u << i)) != 0;
                    bool positive = struck && (state.BounceCandidate || Time.time - state.LastBounceTime < 0.5f);
                    Gizmos.color = positive ? Color.green : Color.red;
                    Gizmos.DrawLine(w.WallCastFrom, w.WallCastTo);
                    Gizmos.DrawWireSphere(w.WallCastTo, w.WallCastRadius);
                }

            // Centre of mass: the real one while playing, an estimate (origin + offset) in Edit Mode.
            Vector3 com = body != null && body.IsLive()
                ? (Vector3)body.Body.GetWorldCenterOfMass()
                : car.TransformPoint(p != null ? p.CenterOfMassOffset : Vector3.zero);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(com, 0.12f);
            Gizmos.DrawLine(com - up * 0.25f, com + up * 0.25f);

            if (state != null && state.HasGroundPlane)
                DrawGroundPlane(state, car, com, up);
            // Where the last bounce struck (from the tyre casts), for half a second.
            if (state != null && Time.time - state.LastBounceTime < 0.5f)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(state.BouncePoint, 0.25f);
                Gizmos.DrawRay(state.BouncePoint, state.BounceAway * 1.5f);
            }
        }

        // The tyre-support plane (cyan, through the contacts), its normal, where the car is leveling to (magenta) and
        // its own up (yellow). With Ground Plane Torque Compensation at 1 the yellow arrow should sit on the magenta one.
        private static void DrawGroundPlane(CarState state, Transform car, Vector3 com, Vector3 up)
        {
            Vector3 normal = state.GroundPlaneNormal;
            Vector3 center = state.GroundPlanePoint;
            Vector3 along = Vector3.ProjectOnPlane(car.forward, normal);
            if (along.sqrMagnitude < 0.001f) along = Vector3.ProjectOnPlane(car.up, normal);
            along = along.normalized;
            Vector3 side = Vector3.Cross(normal, along);
            const float halfLength = 2.4f, halfWidth = 1.3f;
            Vector3 a = center + along * halfLength + side * halfWidth, b = center + along * halfLength - side * halfWidth;
            Vector3 c = center - along * halfLength - side * halfWidth, d = center - along * halfLength + side * halfWidth;
            Gizmos.color = new Color(0.2f, 0.9f, 1f);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
            Gizmos.DrawLine(a, c); Gizmos.DrawLine(b, d);
            Gizmos.DrawRay(center, normal * 1.2f);
#if UNITY_EDITOR
            var corners = new[] { a, b, c, d };
            UnityEditor.Handles.color = new Color(0.2f, 0.9f, 1f, 0.08f);
            UnityEditor.Handles.DrawAAConvexPolygon(corners);
#endif
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(com, state.GroundTargetUp * 1.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(com, up * 1.5f);
            if (state.DriftHeadingActive)
            {
                // The drift's heading (orange): where all the push goes.
                Gizmos.color = new Color(1f, 0.55f, 0f);
                Gizmos.DrawRay(com, state.DriftHeading * 3f);
                Gizmos.DrawWireSphere(com + state.DriftHeading * 3f, 0.08f);
            }
            if (state.RollSave > 0f)
            {
                Gizmos.color = Color.Lerp(new Color(1f, 0.6f, 0.1f), Color.red, state.RollSave);
                Gizmos.DrawWireSphere(com + up * 1.5f, 0.1f + 0.2f * state.RollSave);
            }
#if UNITY_EDITOR
            float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(normal, car.forward),
                Vector3.ProjectOnPlane(up, car.forward), car.forward);
            UnityEditor.Handles.Label(com + up * 1.7f, $"roll {roll:0.0}°  save {state.RollSave:0.00}",
                UnityEditor.EditorStyles.miniLabel);
#endif
        }

        private static void DrawCircle(Vector3 center, Vector3 axis, float radius)
        {
            Vector3 a = Vector3.Cross(axis, Vector3.up);
            if (a.sqrMagnitude < 0.001f) a = Vector3.Cross(axis, Vector3.forward);
            a = a.normalized * radius;
            Vector3 b = Vector3.Cross(axis.normalized, a);
            const int segments = 20;
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

    /// <summary>One spawned car's changing state: inputs, drift, boost, wheels.</summary>
    internal sealed class CarState
    {
        public float Brake, SteerInput, Steer, TargetSpeed, SentTargetSpeed;
        public bool UseTargetSpeed, SentTargetSpeedEnabled, ForcedDriftActive;
        public Vector3 ForcedDriftHeading;
        public bool Handbrake, BoostQueued, UnflipQueued;
        public bool WasActive;

        public float DriftDirection, DriftTime;
        public int DriftLevel;

        public float BoostTime, BoostAcceleration;

        public float FlippedTime;
        public bool SelfRighting;
        public bool AutoRolling;
        public float StuckTime;
        public Vector3 AutoRollAxis;
        public float AutoRollAngle, AutoRollTime;
        public Quaternion AutoRollStart, AutoRollEnd;

        public float AirTime;
        public bool WasGrounded = true;
        public Vector3 UprightNormal;
        public Vector3 TorqueGuardAcceleration;
        public float RollSave = -1f;
        public float LoadTransfer;
        public bool PendingBounce, BounceOffCar;
        public Vector3 BounceAway, BouncePoint, BounceAlignDirection;
        public float BounceSpeed, BounceReadyTime, BounceAlignTime;
        public bool BounceAlignHasDirection;
        public float AirThrottleBase, AirThrottleGate;
        public bool AirThrottleOpen;
        public bool Jumping;
        public float AirLevelReturn = 1f;
        public float InAirFor = -1f, TakeoffLaunch;
        public float JumpBoostTotal, JumpBoostClock, HoverClock = -1f;
        public float JumpTimeScale = 1f;
        public float GlueTime;
        public Vector3 GuardPosition, GuardVelocity, GuardAngularVelocity;
        public quaternion GuardRotation;
        public bool HasGuardPosition;
        public Vector3 TakeoffNormal = Vector3.up;
        public Vector3 BounceTargetVelocity;
        public Vector3 BounceBefore, BounceApproach;
        public float LastBounceTime = -10f;
        public bool BounceCandidate;
        public float BounceCandidateTime;
        public int BounceCandidateSteps, BounceFoundSteps;
        public uint BounceStruckWheels;
        public float BounceGrip = 1f;
        public Quaternion BodyRestRotation;
        public Vector3 BodyRestScale = Vector3.one;
        public bool HasBodyRest, WobbleShown;
        public Vector3 WobbleTilt, WobbleTiltSpeed;
        public float WobbleSquash, WobbleSquashSpeed;
        public Vector3 DriftHeading;
        public bool HasDriftHeading, DriftHeadingActive;
        public float DriftGripReturn = 1f;
        public Vector3 LeanPreviousVelocity;
        public bool HasLeanVelocity;
        public float LateralAcceleration, WhoahLean, WhoahLeanSpeed;
        public bool HasGroundPlane;
        public Vector3 GroundPlaneNormal, GroundPlanePoint, GroundTargetUp;
        public Vector3 PreviousAngularVelocity;
        public bool HasDiagnosticAngularVelocity;
        public float NextTorqueDiagnosticTime;
        public Vector3 LastGuardedAngularAcceleration;
        public Vector3 LastTireAngularAcceleration, LastStabilizationAngularAcceleration;
        public Vector3 LastRotationAssistAngularAcceleration, LastAirControlAngularAcceleration;
        public int LastGroundedWheels;
        public float UprightBlend, UprightAirBlend, UprightSupportBlend;
        public Vector3 LastMovementPosition;
        public bool HasMovementPosition;
        public float SingleTireStationaryTime;

        public bool BodyConfigured;
        public Vector3 InertiaX, InertiaY, InertiaZ;

        public WheelState[] Wheels;

        // Networking: what the owner last sent, and a proxy's motion.
        public float SentBrake, SentSteer, NextInputSend;
        public bool SentHandbrake;
        public Vector3 ProxyPosition, ProxyVelocity;
        public bool HasProxyPosition;

        public void SetStick(Vector2 stick)
        {
            SteerInput = Mathf.Clamp(stick.x, -1f, 1f);
            SetTargetSpeed(stick.y);
        }

        public void SetTargetSpeed(float speed)
        {
            TargetSpeed = Mathf.Clamp(speed, -1f, 1f);
            UseTargetSpeed = true;
        }

        public void ClearInputs()
        {
            Brake = SteerInput = TargetSpeed = 0f;
            UseTargetSpeed = false;
            Handbrake = false;
        }

        /// <summary>Applies one Driver Input binding's value.</summary>
        public void Apply(VehicleInputBinding binding, object value)
        {
            float sign = binding.Invert ? -1f : 1f;
            switch (binding.Action)
            {
                case VehicleInputAction.Throttle: SetTargetSpeed(Mathf.Clamp01(VehicleInput.ToFloat(value) * sign)); break;
                case VehicleInputAction.Brake: Brake = Mathf.Clamp01(VehicleInput.ToFloat(value) * sign); break;
                case VehicleInputAction.Steer:
                    float steer = value is Vector2 stick ? stick.x : VehicleInput.ToFloat(value);
                    SteerInput = Mathf.Clamp(steer * sign, -1f, 1f);
                    break;
                case VehicleInputAction.TargetSpeed:
                    SetTargetSpeed(VehicleInput.ToFloat(value) * sign);
                    break;
                case VehicleInputAction.Drive: SetStick(VehicleInput.ToVector2(value) * sign); break;
                case VehicleInputAction.Handbrake: Handbrake = VehicleInput.ToBool(value) != binding.Invert; break;
                // Buttons: a parameterless Signal arrives with no value, which counts as a press.
                case VehicleInputAction.Boost: if (value == null || VehicleInput.ToBool(value)) BoostQueued = true; break;
                case VehicleInputAction.Unflip: if (value == null || VehicleInput.ToBool(value)) UnflipQueued = true; break;
            }
        }
    }

    /// <summary>One wheel's state from the last physics step.</summary>
    internal struct WheelState
    {
        public bool Grounded;
        public bool WasGrounded;
        public bool HasBlendedContact;
        public RaycastHit GroundHit;
        public Vector3 GroundNormal;
        /// <summary>The ground's own normal where the tyre touches (not kept to the car's up).</summary>
        public Vector3 RawGroundNormal;
        public Vector3 ContactNormal;
        public float ContactDistance, ContactForceWeight;
        public float TargetSpeedMultiplier;
        public bool IsFront;
        public float Extension;
        public float SteerAngle;
        public float RollSpeed;
        public float Spin;
        public Vector3 Point;
        public Vector3 Normal;
        public Quaternion VisualRest;
        /// <summary>The probe hit a ceiling (at the body's top) this step, and where.</summary>
        public bool HasCeiling;
        public Vector3 CeilingPoint;
        /// <summary>The air control's temporal gate for this tyre (0 on the ground, 1 after Air Gate Time off it).</summary>
        public float AirGate;
        /// <summary>Touching the ground with its sidewall this step (so not grounded).</summary>
        public bool Sidewall;
        /// <summary>The last real ground hit, kept through a stray miss (Ground Hold Time).</summary>
        public RaycastHit LastGroundHit;
        public bool HasLastGroundHit;
        /// <summary>What the tyre is driving on (Surface Materials), and the object it came from.</summary>
        public SurfaceMaterial Surface, TaggedSurface;
        public GameObject SurfaceObject;
        public float MissTime;
        /// <summary>This tyre's last wall cast, for the gizmo.</summary>
        public Vector3 WallCastFrom, WallCastTo;
        public float WallCastRadius, WallCastTime;
        /// <summary>The suspension's direction last step, in the car's space (toward the ground it stood on).</summary>
        public Vector3 SuspensionUpLocal;
        /// <summary>The spring's push last step (m/s²), so a sudden rise can be limited.</summary>
        public float SpringPush;
    }
}
