using System.Runtime.CompilerServices;
using Box3D;
using Box3D.Hybrid;
using Remedy.Box3DPhysics;
using Remedy.Schematics;
using Unity.Mathematics;
using UnityEngine;

namespace Remedy.Vehicles
{
    /// <summary>
    /// An arcade car on a real Box3D body: Need for Speed Underground / Mario Kart handling (easy drifts, mini-turbos,
    /// a car that wants to stay on its wheels) while crashes, ramps and rollovers are still physics.
    /// </summary>
    /// <remarks>
    /// The wheels are rays: every physics step each one casts down from its anchor and pushes the body with a spring,
    /// sideways grip and the engine, at that wheel. Drifting is a little state machine on top (less rear grip, a steer
    /// assist that swings the tail, a counter-steer that stops spin-outs, speed carried round the slide). Staying
    /// upright is a set of dials, all in the Properties: a lowered centre of mass, where the tyres push
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
    [SchematicDoc(@"{""name"": ""Arcade Car"", ""kind"": ""behavior"", ""menu"": ""Vehicles/Arcade Car"", ""description"": ""An arcade car on a real Box3D body: NFS Underground / Mario Kart handling (drifts, mini-turbos, a car that wants to stay on its wheels) while crashes and rollovers are still physics. Raycast wheels; everything tunable in its Properties asset."", ""variables"": [""Properties"", ""Wheels"", ""AlwaysActive"", ""ParkWhenEmpty"", ""DriverInputs"", ""PlayerDriverOnly"", ""Speed"", ""ForwardSpeed"", ""IsGrounded"", ""GroundedWheels"", ""IsDrifting"", ""DriftAngle"", ""DriftCharge"", ""DriftLevel"", ""IsBoosting"", ""IsFlipped"", ""AirTime""], ""signals"": [""Target Speed"", ""Brake"", ""Steer"", ""Drive"", ""Handbrake"", ""Boost"", ""Unflip"", ""OnDriftStarted"", ""OnDriftEnded"", ""OnDriftLevel"", ""OnBoost"", ""OnLanded"", ""OnFlipped"", ""OnRighted"", ""OnCrash""], ""use_for"": [""drivable cars and karts"", ""racing games"", ""AI traffic""], ""notes"": ""Needs a Box3D Body and a shape (added for you). Add Vehicle Seats so it only drives with a driver. Multiplayer: the driver owns and simulates the car; its input Signals are networked from the owner, and drift/boost/flip/crash Signals reach every player for effects.""}")]
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

        [SchematicDoc(@"{""name"": ""AirTime"", ""kind"": ""variable"", ""behavior"": ""Arcade Car"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Seconds since all four wheels left the ground (0 on the ground). Read-only.""}")]
        [Tooltip("Seconds since all wheels left the ground (0 on the ground). Read-only.")]
        public readonly VariableHandle<float> AirTime = 0f;

        // Everything else that changes per step, in one place per spawned car.
        private readonly VariableHandle<CarState> _state = (CarState)null;

        // The Vehicle Seats on the same GameObject, if any.
        private readonly BehaviorHandle<VehicleSeats> _seats;

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""Target Speed"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""input"", ""params"": [{""name"": ""Speed"", ""type"": ""float""}], ""description"": ""Desired signed speed as a fraction of the car's maximum in that direction: -1 is maximum reverse, 1 is maximum forward, and 0 targets a stop. The car accelerates or brakes to approach it; when changing direction it brakes until current speed is within 20% of its current direction's maximum, then accelerates the other way. Values are clamped to -1..1 and hold until changed."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock), e.g. from an input or an AI graph.""}")]
        [Tooltip("Desired signed speed: -1 is maximum reverse, 1 is maximum forward, 0 targets a stop. The car accelerates or brakes to approach it; it brakes before reversing until within 20% of the current direction's maximum speed. Clamped to -1..1.")]
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

        [SchematicDoc(@"{""name"": ""OnCrash"", ""kind"": ""signal"", ""behavior"": ""Arcade Car"", ""direction"": ""output"", ""params"": [{""name"": ""Speed"", ""type"": ""float""}], ""description"": ""Sent when the car hits something (or something hits it) faster than the Properties' Min Crash Speed, with the impact speed in m/s. For damage, sounds and camera shake."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [Tooltip("Sent when the car hits something faster than Min Crash Speed, with the impact speed in m/s.")]
        [OutputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Speed", typeof(float))]
        public readonly SignalHandle<float> OnCrash;

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
            for (int i = 0; i < state.Wheels.Length; i++)
            {
                var wheel = wheels[i];
                state.Wheels[i].Extension = properties != null ? properties.SuspensionRestLength : 0.45f;
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
        private const float DirectionChangeBrakeThreshold = 0.2f;

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
                float targetSpeed = effectiveTargetSpeed;
                float inverseForwardLimit = 1f / Mathf.Max(0.1f, topSpeed);
                float inverseReverseLimit = 1f / Mathf.Max(0.1f, p.MaxReverseSpeed);
                float normalizedSpeed = forwardSpeed >= 0f
                    ? forwardSpeed * inverseForwardLimit
                    : forwardSpeed * inverseReverseLimit;
                float speedError;
                bool changingDirection = targetSpeed != 0f && normalizedSpeed * targetSpeed < 0f;
                if (changingDirection && Mathf.Abs(normalizedSpeed) > DirectionChangeBrakeThreshold)
                {
                    speedError = -1f;
                }
                else
                {
                    speedError = Mathf.Abs(targetSpeed) - Mathf.Abs(normalizedSpeed);
                }

                float targetDrive = Mathf.Clamp01(speedError);
                float targetBrake = Mathf.Clamp01(-speedError);
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
            bool forcedDrift = drifting && handbrake;
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
            int grounded = 0;
            Vector3 normalSum = Vector3.zero;

            for (int i = 0; i < wheels.Length; i++)
            {
                var wheel = wheels[i];
                ref var w = ref state.Wheels[i];
                if (wheel?.Anchor == null)
                {
                    w.Grounded = false;
                    continue;
                }

                Vector3 origin = wheel.Anchor.position;
                float radius = Mathf.Max(0.05f, wheel.Radius);
                float rest = Mathf.Max(0.01f, p.SuspensionRestLength);
                w.SteerAngle = wheel.Steers ? steerAngle : 0f;

                bool wasGrounded = w.Grounded;
                w.Grounded = SchematicPhysics.SphereCast(origin, 0f, -up, out var hit, out _,
                    rest + radius, p.GroundLayers, body);
                if (!w.Grounded)
                {
                    w.Extension = rest;
                    w.RollSpeed *= 0.99f;
                    w.TargetSpeedMultiplier = 1f;
                    continue;
                }
                grounded++;
                normalSum += hit.normal;
                w.Point = hit.point;
                w.Normal = hit.normal;

                // Spring and damper along the suspension, pushing at the wheel.
                float extension = Mathf.Clamp(hit.distance - radius, 0f, rest);
                float compression = rest - extension;
                w.Extension = extension;
                Vector3 hubVelocity = native.GetWorldPointVelocity(origin);
                float suspensionSpeed = Vector3.Dot(hubVelocity, up);
                float springAcceleration = Mathf.Clamp(p.SpringStiffness * compression - p.SpringDamping * suspensionSpeed, 0f, p.MaxSuspensionAcceleration);
                native.ApplyForce(up * (springAcceleration * massShare), origin, true);

                // How much weight the tyre carries (1 = its share of the car at rest): more weight, more grip.
                float load = p.GripScalesWithLoad && g > 0.001f ? Mathf.Clamp(springAcceleration / g, 0f, 2f) : 1f;

                // The tyre's directions on the ground.
                Vector3 normal = hit.normal;
                Vector3 wheelForward = Quaternion.AngleAxis(w.SteerAngle, up) * forward;
                Vector3 rollDirection = Vector3.ProjectOnPlane(wheelForward, normal).normalized;
                Vector3 sideDirection = Vector3.Cross(normal, rollDirection);
                Vector3 contactVelocity = native.GetWorldPointVelocity(hit.point);
                if (p.StickyForce > 0f && speedFraction >= Mathf.Clamp01(p.StickyThreshold))
                    native.ApplyForce(-up * (p.StickyForce * Mathf.Abs(forwardSpeed) * massShare), hit.point, true);
                if (!wasGrounded)
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
                if (drifting)
                    grip *= w.IsFront ? p.DriftFrontGrip : p.DriftRearGrip;
                if (wheel.HasHandbrake && handbrake)
                    grip *= p.HandbrakeGrip;
                float limit = p.MaxTyreAcceleration * load;
                float sideAcceleration = Mathf.Clamp(-sideSpeed * Mathf.Min(grip, 1f / dt), -limit, limit);

                // Along the tyre: engine, brakes, handbrake, rolling resistance.
                float rollAcceleration = 0f;
                if (wheel.Drives && driveCount > 0)
                    rollAcceleration += driveAcceleration * wheelCount / driveCount;
                if (brakeDeceleration > 0f)
                    rollAcceleration -= Mathf.Sign(rollSpeed) * Mathf.Min(brakeDeceleration, Mathf.Abs(rollSpeed) / dt);
                if (wheel.HasHandbrake && handbrake && handbrakeCount > 0)
                    rollAcceleration -= Mathf.Sign(rollSpeed) * Mathf.Min(p.HandbrakeDeceleration * wheelCount / handbrakeCount, Mathf.Abs(rollSpeed) / dt);
                if (Mathf.Approximately(driveAcceleration, 0f) && brakeDeceleration <= 0f)
                    rollAcceleration -= rollSpeed * p.RollingResistance;

                // Pushed somewhere between the road (rolls the car) and the centre of mass height (doesn't).
                float heightToCenter = Vector3.Dot(centerOfMass - hit.point, up);
                Vector3 at = hit.point + up * (heightToCenter * p.TyreForceHeight);
                native.ApplyForce((rollDirection * rollAcceleration + sideDirection * sideAcceleration) * massShare, at, true);
            }

            bool isGrounded = grounded > 0;
            Vector3 groundNormal = isGrounded ? normalSum.normalized : worldUp;
            IsGrounded.Value = isGrounded;
            GroundedWheels.Value = grounded;

            // Rotation helpers, as angular accelerations (turned into torque with the body's own inertia below).
            Vector3 angular = Vector3.zero;
            Vector3 rollPitchVelocity = angularVelocity - up * Vector3.Dot(angularVelocity, up);

            if (isGrounded)
            {
                if (p.StabilizeStrength > 0f || p.StabilizeDamping > 0f)
                    angular += UprightError(up, groundNormal, forward) * p.StabilizeStrength - rollPitchVelocity * p.StabilizeDamping;
            }
            else if (IsAirborne(body, centerOfMass, worldUp, p))
            {
                // Really in the air (not just lying on its roof or side with the wheels off the ground).
                if (active && p.AirControl > 0f)
                    angular += (right * (throttle - brake) + up * steerInput) * p.AirControl;
                if (p.AirLevelStrength > 0f || p.AirLevelDamping > 0f)
                    angular += UprightError(up, worldUp, forward) * p.AirLevelStrength - rollPitchVelocity * p.AirLevelDamping;
            }

            // Drift: the steering swings the tail, the counter-steer stops a spin, and the speed follows the nose.
            if (drifting && isGrounded)
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

            // Flipped: report it, and turn back over after a while (or on Unflip).
            UpdateFlipped(instance, p, state, up, worldUp, velocity.magnitude, dt, out bool righting);
            if (righting)
            {
                float strength = Mathf.Max(p.SelfRightStrength, 8f);
                angular += UprightError(up, worldUp, forward) * strength - rollPitchVelocity * 2f;
                if (!state.SelfRighting)
                {
                    state.SelfRighting = true;
                    native.ApplyForceToCenter(worldUp * (p.SelfRightLift * mass / dt), true);
                }
            }

            if (angular.sqrMagnitude > 0f)
                native.ApplyTorque(Torque(angular, rotation, state), true);

            // Boost push along the road.
            if (boosting)
            {
                state.BoostTime -= dt;
                if (forwardSpeed < topSpeed)
                {
                    Vector3 direction = isGrounded ? Vector3.ProjectOnPlane(forward, groundNormal).normalized : forward;
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
            bool held = p.HoldButtonToDrift
                ? handbrake
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

            if (!IsFlipped.Value && tilted && state.FlippedTime > 0.5f)
            {
                IsFlipped.Value = true;
                OnFlipped.Invoke(instance);
            }

            // Back on its wheels (near enough): stop righting; a car that's merely leaning ignores Unflip.
            if (tilt < 45f)
            {
                state.SelfRighting = false;
                state.UnflipQueued = false;
                if (IsFlipped.Value)
                {
                    IsFlipped.Value = false;
                    state.FlippedTime = 0f;
                    OnRighted.Invoke(instance);
                }
            }

            righting = state.UnflipQueued
                       || (IsFlipped.Value && p.SelfRightStrength > 0f && state.FlippedTime >= p.SelfRightDelay)
                       || state.SelfRighting;
        }

        // With no wheel touching, is there ground right under the car? (Lying on its roof or side isn't flying, and
        // shouldn't get air control or levelling, so a flipped car only turns back over if self-righting says so.)
        private static bool IsAirborne(Box3DBody body, Vector3 centerOfMass, Vector3 worldUp, ArcadeCarProperties p)
            => !SchematicPhysics.Raycast(centerOfMass, -worldUp, out _, p.SuspensionRestLength + AirborneClearance, p.GroundLayers, body);

        /// <summary>How far below the centre of mass (plus the suspension length) the ground must be for the car to count as flying.</summary>
        public const float AirborneClearance = 1.2f;

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

        // Mass, centre of mass and inertia from the Properties, on top of what the shapes give (so it's the same every spawn).
        private static void ConfigureBody(Body native, ArcadeCarProperties p, CarState state)
        {
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
                    ApplyWheelTargetSpeedLoss(ref state.Wheels[nearestWheel], impact.Speed, massShare, p, 1f);
                }
            }
            // Whoever simulates the car reports crashes (OnCrash is networked), so proxies don't add their own.
            if (p != null && impact.Speed >= p.MinCrashSpeed && !IsProxy(Self))
                OnCrash.Invoke(Self, impact.Speed);
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

        // A visual that is the anchor (or holds it) would move the ray's origin with it, so it's left alone.
        private static bool HasMovableVisual(Wheel wheel)
            => wheel != null && wheel.Anchor != null && wheel.Visual != null && wheel.Visual != wheel.Anchor && !wheel.Anchor.IsChildOf(wheel.Visual);

        // Each wheel mesh's own orientation relative to the car, remembered from the first time it's seen (pooled cars
        // come back with their wheels mid-spin).
        private sealed class RestPose { public Quaternion Rotation; }
        private static readonly ConditionalWeakTable<Transform, RestPose> _visualRest = new();

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

                    // The suspension ray: green on the ground, red in the air, cyan in Edit Mode.
                    Gizmos.color = !live ? Color.cyan : w.Grounded ? Color.green : Color.red;
                    Gizmos.DrawLine(origin, origin - up * (rest + radius));
                    Gizmos.DrawWireSphere(origin, 0.04f);

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

            // Centre of mass: the real one while playing, an estimate (origin + offset) in Edit Mode.
            var body = context.GetComponent<Box3DBody>();
            Vector3 com = body != null && body.IsLive()
                ? (Vector3)body.Body.GetWorldCenterOfMass()
                : car.TransformPoint(p != null ? p.CenterOfMassOffset : Vector3.zero);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(com, 0.12f);
            Gizmos.DrawLine(com - up * 0.25f, com + up * 0.25f);
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

        public float AirTime;
        public bool WasGrounded = true;

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
        public float TargetSpeedMultiplier;
        public bool IsFront;
        public float Extension;
        public float SteerAngle;
        public float RollSpeed;
        public float Spin;
        public Vector3 Point;
        public Vector3 Normal;
        public Quaternion VisualRest;
    }
}
