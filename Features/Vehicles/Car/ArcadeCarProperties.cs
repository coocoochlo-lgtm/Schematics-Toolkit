using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Remedy.Vehicles
{
    /// <summary>How a drift starts.</summary>
    public enum DriftEntryMode
    {
        /// <summary>Hold the handbrake (drift button) while steering: Mario Kart's hop-drift, NFS's handbrake flick.</summary>
        Handbrake,
        /// <summary>Steer hard at speed and the car breaks loose by itself (no button needed).</summary>
        SharpSteer,
        /// <summary>Either of the above.</summary>
        HandbrakeOrSharpSteer,
        /// <summary>Never: the car only slides when its tyres actually give up.</summary>
        Off,
    }

    /// <summary>How a drift handles once it's started.</summary>
    public enum DriftHandling
    {
        /// <summary>The drift keeps its own heading, steered like a racing line; all the push goes along it and the nose is held at a big slide angle on top (Ridge Racer, Mario Kart).</summary>
        Arcade,
        /// <summary>The tyres just grip less and the slide is physics, helped along by the drift assists.</summary>
        Physics,
    }

    /// <summary>One mini-turbo stage: charge a drift past it, and letting go of the drift gives this boost.</summary>
    [Serializable]
    public class MiniTurboLevel
    {
        [Tooltip("Drift charge needed for this level (charge builds at Charge Rate per second while drifting).")]
        public float Charge = 1f;
        [Tooltip("Forward push while the boost lasts, in m/s².")]
        public float Acceleration = 10f;
        [Tooltip("How long the boost lasts, in seconds.")]
        public float Duration = 0.8f;
        [Tooltip("For effects: the spark colour of this level (blue, orange, purple in Mario Kart).")]
        public Color Color = Color.cyan;

        public MiniTurboLevel() { }

        public MiniTurboLevel(float charge, float acceleration, float duration, Color color)
        {
            Charge = charge;
            Acceleration = acceleration;
            Duration = duration;
            Color = color;
        }
    }

    /// <summary>
    /// Everything that makes an <see cref="ArcadeCar"/> feel the way it does: suspension, engine, steering, grip, drift
    /// and mini-turbo, and how hard it tries to stay on its wheels. Accelerations are in m/s², so the numbers mean
    /// the same for a light kart and a heavy truck. Right-click the asset's header for presets.
    /// </summary>
    [CreateAssetMenu(menuName = "Remedy Toolkit/Vehicles/Arcade Car Properties")]
    public class ArcadeCarProperties : ScriptableObjectWithID<ArcadeCarProperties>
    {
        [Header("Ground")]
        [Tooltip("What the wheels can stand on.")]
        public LayerMask GroundLayers = ~0;

        [Header("Suspension")]
        [Tooltip("How far each wheel hangs below its anchor when nothing presses on it (metres). Longer = softer, more body roll, bigger jumps soaked up.")]
        public float SuspensionRestLength = 0.45f;
        [Min(0f)]
        [Tooltip("How far below the top of the tire each ground probe continues (metres). Probes start at the top of the car body's bounds and shoot past the tyres on purpose: they only detect ground. The tyres push only on ground within Tire Placement Threshold of this, so a longer probe sticks better without lifting the car.")]
        public float GroundCheckDistance = 1.6f;
        [Range(0f, 1f)]
        [Tooltip("When no ground is found, places the tire bottom no farther than this fraction of Ground Check Distance below its anchor. A nearer hit moves it toward the surface. Ground sensing extends farther, from the body top to below the tire. Suspension Rest Length limits spring travel, not tire placement.")]
        public float TirePlacementThreshold = 0.5f;
        [Min(0f)]
        [Tooltip("A tyre whose ground cast suddenly misses keeps its last hit for up to this long (seconds), so a stray miss doesn't drop it off the ground. The first hit ends it at once (hits always count straight away). 0 = misses count at once.")]
        public float GroundHoldTime = 0.06f;
        [Min(0f)]
        [Tooltip("Landing after at least Ground Glue Min Air Time in the air, the car is held to the ground for this long (seconds), so it doesn't hop back off. 0 = off.")]
        public float GroundGlueTime = 0.3f;
        [Min(0f)]
        [Tooltip("How long it must have been in the air for a landing to be glued (seconds).")]
        public float GroundGlueMinAirTime = 0.15f;
        [Range(0f, 1f)]
        [Tooltip("How much of any speed carrying it away from the ground the glue takes off each step (1 = all of it).")]
        public float GroundGlueStrength = 0.8f;
        [Min(0f)]
        [Tooltip("An extra press into the ground while glued (m/s²), fading out over Ground Glue Time.")]
        public float GroundGluePress = 15f;
        [Min(0f)]
        [Tooltip("Position guard: a physics step that lands the car further than this (metres) from where its last position and velocity put it is a glitch, and it's put back where it was. Teleport on purpose with MarkTeleported. 0 = off.")]
        public float PositionGuardDistance = 5f;
        [Tooltip("Spring stiffness: m/s² of push per metre compressed. ~10 / Stiffness is how far the car sags at rest (60 → 17 cm). Higher = stiffer, twitchier.")]
        public float SpringStiffness = 60f;
        [Tooltip("Spring damping: stops the car bouncing. ~2 × √Stiffness × 0.5 is a good start. Too low bounces, too high feels like bricks.")]
        public float SpringDamping = 7f;
        [Tooltip("The most a single spring can push (m/s²), a bump stop for big landings so the car doesn't get launched.")]
        public float MaxSuspensionAcceleration = 60f;
        [Tooltip("The suspension (ground casts and springs) points at the ground the tyres stand on, not along the car's own up, so leaning and pitching don't swing the casts off the road. Off: along the car's up.")]
        public bool CastTowardGround = true;
        [Min(0f)]
        [Tooltip("How fast a spring's push can rise (m/s² per second; 1500 = 30 per step at 50 Hz). A tyre slammed back onto the road ramps up over a few steps instead of kicking in one; letting go is always immediate. 0 = no limit.")]
        public float SpringMaxRise = 1500f;
        [Range(0f, 1f)]
        [Tooltip("The least damping the springs get, as a fraction of what stops a bounce dead (from Spring Stiffness). Stops stiff springs bouncing the wheels off the road in turns. 0 = only Spring Damping.")]
        public float MinSpringDampingRatio = 0.35f;

        [Header("Engine")]
        [Tooltip("Top speed under its own power, in m/s (×3.6 for km/h: 45 m/s ≈ 160 km/h).")]
        public float MaxSpeed = 45f;
        [Tooltip("Acceleration at full throttle from a standstill, in m/s² (10 ≈ 0-100 km/h in ~3 s before the curve kicks in).")]
        public float Acceleration = 14f;
        [Tooltip("Acceleration multiplier by speed: x = speed / Max Speed (0..1), y = multiplier. Falling off towards the top speed feels like gears running out.")]
        public AnimationCurve AccelerationCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.75f), new Keyframe(1f, 0.1f));
        [Tooltip("Top speed in reverse, in m/s.")]
        public float MaxReverseSpeed = 12f;
        [Tooltip("Acceleration in reverse, in m/s².")]
        public float ReverseAcceleration = 8f;
        [Tooltip("Maximum braking deceleration, in m/s². Scales with Brake input and the Target Speed controller.")]
        public float BrakeDeceleration = 25f;
        [Tooltip("How quickly the car coasts to a stop off the throttle, per second (0.25 loses a quarter of its speed per second).")]
        public float RollingResistance = 0.25f;
        [Min(0f)]
        [Tooltip("Easing off (faster than the Target Speed still held): how quickly the engine settles the car down to it (per second of the speed over: 0.6 takes about 40% of the excess away in the first second). 0 = it just coasts down. Nothing held (0) always coasts.")]
        public float EngineBraking = 0.6f;
        [Tooltip("How quickly a wheel's impact-induced target-speed loss recovers while grounded, per second. Wheels recover immediately while airborne.")]
        public float TargetSpeedGroundRecovery = 0.5f;
        [Range(0f, 1f)]
        [Tooltip("Placeholder ground-material resistance (0..1) until the vehicle surface-material system is available. Increases target-speed loss on landing and slows recovery.")]
        public float GroundMaterialResistance = 0.5f;
        [Range(0f, 1f)]
        [Tooltip("How strongly normalized impact kinetic energy reduces the struck wheel's target speed (0..1).")]
        public float ImpactTargetSpeedLoss = 0.5f;

        [Header("Steering")]
        [Tooltip("Front wheel angle at full lock, in degrees.")]
        public float MaxSteerAngle = 32f;
        [Tooltip("How much of the lock is available by speed: x = speed / Max Speed, y = fraction. Less lock at speed stops twitchy high-speed handling.")]
        public AnimationCurve SteerBySpeed = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.35f));
        [Tooltip("How fast the wheels turn towards the input (full lock per second). Lower = smoother for keyboards.")]
        public float SteerSpeed = 5f;
        [Tooltip("How fast the wheels straighten when the input lets go (full lock per second).")]
        public float SteerReturnSpeed = 8f;
        [Min(0f)]
        [Tooltip("The hardest the steering may ask the car to corner (m/s² sideways, 10 ≈ 1 g): the lock is capped so a turn never asks for more, so it shrinks with the square of speed (on top of Steer By Speed). 0 = no cap.")]
        public float CorneringLimit = 25f;
        [Min(0f)]
        [Tooltip("Speed lost while cornering, per m/s² of sideways pull (0.15: a 20 m/s² turn slows by 3 m/s²). 0 = turning is free.")]
        public float CorneringDrag = 0.15f;
        [Range(0f, 1f)]
        [Tooltip("How much of the throttle a turn at Cornering Limit takes away (less in gentler turns), so the engine doesn't just refill what cornering costs.")]
        public float CorneringThrottleCut = 0.5f;

        [Header("Grip")]
        [Tooltip("How hard the front tyres resist sliding sideways (per second: the fraction of sideways speed removed each second, capped at one physics step). More front than rear = understeer; less = oversteer.")]
        public float FrontGrip = 14f;
        [Tooltip("How hard the rear tyres resist sliding sideways. Lower than the front makes the tail playful.")]
        public float RearGrip = 12f;
        [Tooltip("The tyres' limit: the most sideways acceleration one tyre can give (m/s², 10 ≈ 1 g). Past it the car slides whatever the grip. Real road cars ~9; arcade ~25-35.")]
        public float MaxTyreAcceleration = 30f;
        [Tooltip("Tyres grip more on the side the car leans onto (weight transfer). Off: every tyre grips the same.")]
        public bool GripScalesWithLoad = true;
        [Range(0f, 1f)]
        [Tooltip("How much of that weight transfer reaches the grip (0 = none, 1 = all of it). All of it piles grip onto the outside tyres in a hard turn, which trips the car over.")]
        public float LoadGripAmount = 0.5f;
        [Range(0f, 1f)]
        [Tooltip("Where the engine and brakes push the car: 0 = at the road (realistic: the nose dips braking and lifts accelerating), 1 = at the centre of mass height (no dive or squat).")]
        public float TyreForceHeight = 0.6f;
        [Range(0f, 1f)]
        [Tooltip("Where the sideways grip pushes the car: 0 = at the road (realistic: hard cornering rolls it over), 1 = at the centre of mass height (cornering never rolls it). The main 'flips vs stays planted' dial; the Whoah Lean gives the cartoon lean instead.")]
        public float SideForceHeight = 0.9f;
        [Range(0f, 90f)]
        [Tooltip("The steepest a tyre's ground can be from the car's own up (degrees). A tyre touching a kerb or wall with its side reports it nearly sideways, and grip from it launches the car. 0 = no limit.")]
        public float ContactNormalMaxAngle = 50f;
        [Range(0f, 90f)]
        [Tooltip("Ground further than this from the car's up (degrees) is a tyre's sidewall touching, not the tyre standing on it: it doesn't count as grounded, so a car on its side can't drive on two tyres. 0 = no limit.")]
        public float TyreContactMaxAngle = 60f;

        [Header("Surfaces")]
        [Range(0f, 1f)]
        [Tooltip("How much the ground's Surface Material Friction changes grip, traction and braking (relative to the default material's 0.6: ice grips far less, rubber more). 0 = surfaces don't change grip.")]
        public float SurfaceGripInfluence = 1f;
        [Range(0f, 1f)]
        [Tooltip("Grip lost on fully wet ground (Wetness 1), as a fraction.")]
        public float SurfaceWetGripLoss = 0.3f;
        [Min(0f)]
        [Tooltip("Drag on soft ground (Hardness under the default 0.5: mud, sand, snow), per second at the softest: it slows the car even with grip.")]
        public float SurfaceSoftDrag = 1.5f;
        [Range(0f, 1f)]
        [Tooltip("Engine push lost on the softest ground (a fraction).")]
        public float SurfaceSoftDriveLoss = 0.4f;
        [Range(0f, 1f)]
        [Tooltip("How much easier drifting gets on wet ground (or a material's own \"Drift\" property, 0..1): longer, looser drifts that lose less speed.")]
        public float SurfaceWetDriftBonus = 0.6f;

        [Header("Handbrake")]
        [Tooltip("Grip of the handbrake wheels while it's pulled (a multiplier: 0.3 = 30% grip). Low = easy flicks into a slide.")]
        public float HandbrakeGrip = 0.3f;
        [Tooltip("How much the handbrake slows the car, in m/s².")]
        public float HandbrakeDeceleration = 6f;

        [Header("Drift")]
        [Tooltip("How a drift starts.")]
        public DriftEntryMode DriftEntry = DriftEntryMode.HandbrakeOrSharpSteer;
        [Tooltip("Arcade: the drift has its own heading, steered like a racing line, and the nose hangs out at a big angle (exaggerated, easy). Physics: the tyres grip less and it's a real slide (the Physics-style settings below).")]
        public DriftHandling DriftStyle = DriftHandling.Arcade;
        [Tooltip("Slowest speed (m/s) a drift can start at; it ends below half of this.")]
        public float DriftMinSpeed = 12f;
        [Range(0f, 1f)]
        [Tooltip("How far the steering must be turned (0..1) to start a drift.")]
        public float DriftSteerThreshold = 0.8f;
        [Tooltip("On: the drift lasts while the handbrake / drift button is held (Mario Kart). Off: it lasts while the car is still sliding (NFS).")]
        public bool HoldButtonToDrift = false;
        [Tooltip("A little hop (m/s upward) when a drift starts, like a kart. 0 for none.")]
        public float DriftHop = 0f;
        [Tooltip("Physics style: Rear grip while drifting (a multiplier). Lower = wider, longer slides.")]
        public float DriftRearGrip = 0.35f;
        [Tooltip("Physics style: Front grip while drifting (a multiplier). Keep it near 1 so the front still steers the slide.")]
        public float DriftFrontGrip = 0.9f;
        [Tooltip("Physics style: How strongly the steering swings the car's tail while drifting (yaw, rad/s² at full lock). The 'I can steer my drift' feel.")]
        public float DriftSteerAssist = 3f;
        [Tooltip("Physics style: A constant turn into the drift's direction (rad/s²), whatever the steering: Mario Kart drifts always curve. 0 for NFS-style.")]
        public float DriftAutoTurn = 0f;
        [Tooltip("Physics style: The widest slide angle the assist allows, in degrees. Past it the car is pulled back (counter-steer), so it doesn't spin out.")]
        public float DriftMaxAngle = 45f;
        [Tooltip("Physics style: How hard the car is pulled back when the slide goes past Drift Max Angle (an automatic counter-steer). 0 lets it spin.")]
        public float DriftCounterSteer = 8f;
        [Tooltip("Physics style: How quickly the car's speed swings round to its drift heading while drifting (per second). Higher keeps your speed through drifts (arcade); 0 = pure physics, sliding scrubs speed.")]
        public float DriftMomentumKeep = 1.5f;
        [Tooltip("Physics style: During a handbrake drift, how quickly its movement heading realigns toward the car's forward direction (degrees per second).")]
        public float ForcedDriftRealignSpeed = 180f;
        [Range(0f, 1f)]
        [Tooltip("Physics style: Minimum fraction of Forced Drift Realign Speed when actual speed reaches its target. Lower values keep the drift heading longer.")]
        public float ForcedDriftMinRealignFraction = 0.1f;
        [Tooltip("Physics style: NFS-style drifts end once the slide angle drops under this (degrees).")]
        public float DriftExitAngle = 8f;

        [Tooltip("Arcade style: the slide angle (degrees, nose from heading) steering fully out of the drift...")]
        [Range(0f, 85f)] public float DriftAngleMin = 20f;
        [Tooltip("...and steering fully into it. Halfway between with the wheels straight.")]
        [Range(0f, 85f)] public float DriftAngleMax = 55f;
        [Tooltip("Arcade style: how fast the steering turns the drift's heading (degrees per second at full lock).")]
        public float DriftTurnRate = 60f;
        [Min(0f)]
        [Tooltip("Arcade style: the hardest a drift may corner (m/s² sideways): its turn rate is capped to this at speed, so fast drifts sweep wide and slow ones whip round. 0 = no cap.")]
        public float DriftCorneringLimit = 25f;
        [Tooltip("Arcade style: a constant turn into the drift (degrees per second), whatever the steering: Mario Kart drifts always curve. 0 for NFS-style.")]
        public float DriftAutoTurnRate = 0f;
        [Tooltip("Arcade style: how quickly the car's travel swings onto the heading (per second). High = on rails, low = floaty.")]
        public float DriftHeadingGrip = 6f;
        [Tooltip("Arcade style: how hard the nose is swung to the slide angle (rad/s² per radian off).")]
        public float DriftAngleStrength = 30f;
        [Tooltip("Arcade style: damps the nose's swing (lower lets it wag past the angle).")]
        public float DriftAngleDamping = 6f;
        [Tooltip("Arcade style: speed lost per second at a sideways slide (a fraction: 0.15 = 15%); less at smaller angles. 0 = drifting keeps every bit of speed.")]
        public float DriftSpeedLoss = 0.15f;
        [Range(-1f, 1f)]
        [Tooltip("Arcade style, without Hold Button To Drift: the drift ends once the steering into it drops under this (0.1 = letting go of the stick; -0.3 = only counter-steering).")]
        public float DriftExitSteer = 0.1f;
        [Min(0f)]
        [Tooltip("Arcade style: how long the tyres take to grip fully again after a drift (seconds), so it slides out instead of snapping straight.")]
        public float DriftExitTime = 0.35f;

        [Header("Mini-Turbo")]
        [Tooltip("Drifting charges a boost that fires when the drift ends (Mario Kart). Off: drifts are just drifts.")]
        public bool MiniTurbo = true;
        [Tooltip("Charge gained per second of drifting.")]
        public float ChargeRate = 1f;
        [Tooltip("Extra charge for steering into the drift (tight lines charge faster): × (1 + bonus × how far in).")]
        public float TightTurnChargeBonus = 1f;
        [Tooltip("The stages, lowest first. Ending a drift past a stage's charge gives its boost.")]
        public MiniTurboLevel[] TurboLevels =
        {
            new MiniTurboLevel(0.8f, 8f, 0.6f, new Color(0.3f, 0.6f, 1f)),
            new MiniTurboLevel(1.8f, 11f, 1.0f, new Color(1f, 0.55f, 0.1f)),
            new MiniTurboLevel(3.0f, 14f, 1.4f, new Color(0.8f, 0.3f, 1f)),
        };

        [Header("Boost")]
        [Tooltip("Forward push of the Boost input (nitro), in m/s².")]
        public float BoostAcceleration = 12f;
        [Tooltip("How long one Boost input lasts, in seconds.")]
        public float BoostDuration = 1.5f;
        [Tooltip("How much faster than Max Speed the car may go while boosting (m/s), from either a Boost or a mini-turbo.")]
        public float BoostMaxSpeedBonus = 12f;

        [Header("Mass")]
        [Tooltip("The car's mass in kg. 0 = from its shapes' density.")]
        public float Mass = 1200f;
        [Tooltip("Moves the centre of mass from where the shapes put it (car space, metres). Lower (negative y) is the classic arcade trick for a car that's hard to roll.")]
        public Vector3 CenterOfMassOffset = new Vector3(0f, -0.35f, 0.05f);
        [Tooltip("Multiplies the car's resistance to rotating. Higher feels heavier and more stable (slower to spin or roll); lower is nimble.")]
        public float InertiaScale = 1.3f;

        [Header("Staying Upright")]
        [Tooltip("How hard the grounded car levels toward the virtual plane formed by tire contacts. Scales down when fewer tires support the car. 0 disables orientation correction.")]
        public float StabilizeStrength = 8f;
        [Tooltip("Damps roll and pitch while the tires meaningfully support the car.")]
        public float StabilizeDamping = 2f;
        [Min(0f)]
        [Tooltip("Time for tilt correction to fade in/out and turn toward a new support normal. Softens changes in tire contact over ramps; 0 is immediate.")]
        public float StabilizeTransitionTime = 0.25f;
        [Range(0f, 1f)]
        [Tooltip("Cancels tire and suspension torque that would roll or pitch the chassis, while preserving yaw torque for steering. 1 keeps the chassis on the virtual tire-support plane.")]
        public float GroundPlaneTorqueCompensation = 1f;
        [Range(0f, 20f)]
        [Tooltip("Maximum inward chassis lean toward the turn while grounded, in degrees. Wheel suspension movement and appearance are unaffected.")]
        public float GroundedTurnLeanAngle = 5f;
        [Range(0f, 1f)]
        [Tooltip("The least leveling the grounded car keeps however few tyres hold it up (a fraction). Without it the leveling let go right as the car tipped.")]
        public float UprightSupportFloor = 0.35f;
        [Min(0f)]
        [Tooltip("When exactly one tire is grounded, maximum car speed for the stuck recovery timer (m/s).")]
        public float SingleTireRecoveryMaxSpeed = 1f;
        [Min(0f)]
        [Tooltip("Maximum measured car movement speed while counting as stuck on one tire (m/s).")]
        public float SingleTireRecoveryMovementSpeed = 0.15f;
        [Min(0f)]
        [Tooltip("How long the car must remain nearly still with exactly one tire grounded before it rights toward that surface normal (seconds).")]
        public float SingleTireRecoveryDelay = 2f;
        [Min(0f)]
        [Tooltip("Righting angular acceleration toward the grounded surface normal during single-tire stuck recovery (rad/s²).")]
        public float SingleTireRecoveryStrength = 18f;
        [Min(0f)]
        [Tooltip("Angular damping during single-tire stuck recovery.")]
        public float SingleTireRecoveryDamping = 4f;
        [Range(0f, 1f)]
        [Tooltip("Minimum fraction of Max Speed before sticky force applies to grounded tires.")]
        public float StickyThreshold = 0.1f;
        [FormerlySerializedAs("Downforce")]
        [Tooltip("Pushes each grounded tire along the car's -up direction. Force scales linearly with forward speed (m/s² per m/s); can press tires onto walls or ceilings when the car is oriented to them.")]
        public float StickyForce = 0.15f;
        [Tooltip("In the air, how hard the car levels itself (towards world up). Makes landings land on the wheels. 0 = it keeps whatever spin it had.")]
        public float AirLevelStrength = 2f;
        [Tooltip("Damps roll and pitch spin in the air.")]
        public float AirLevelDamping = 1f;
        [Range(0f, 89f)]
        [Tooltip("In the air the car levels to the surface it's aimed at (where its wheels point, along its motion): the steepest that counts (degrees from flat), so it never lines up with a wall.")]
        public float AirAimMaxSlope = 60f;
        [Min(0f)]
        [Tooltip("After the player lets go of air control, the air leveling fades back in from nothing over this long (seconds).")]
        public float AirLevelReturnTime = 0.6f;
        [Min(0f)]
        [Tooltip("Air control: how fast steering rolls the car (degrees per second at full input, through the gates below). Rocket League, gently. 0 (with Air Pitch Rate 0) = the old Air Control push instead.")]
        public float AirRollRate = 60f;
        [Min(0f)]
        [Tooltip("Air control: how fast Target Speed pitches the car (degrees per second at full input; forward tips the nose down).")]
        public float AirPitchRate = 60f;
        [Min(0f)]
        [Tooltip("Roll (steering, around the car's length): how strongly the car's spin follows what the air control asks for (per second). ~2 feels natural; higher is snappier.")]
        public float AirRollResponse = 2f;
        [Min(0f)]
        [FormerlySerializedAs("AirControlResponse")]
        [Tooltip("Pitch (Target Speed, around the car's width): how strongly the car's spin follows what the air control asks for (per second). ~2 feels natural; higher is snappier.")]
        public float AirPitchResponse = 2f;
        [Min(0.01f)]
        [Tooltip("Spatial gate: air control opens as the ground gets further than the tyres can reach, fully this far beyond (metres), per tyre, averaged.")]
        public float AirGateDistance = 1f;
        [Min(0.01f)]
        [Tooltip("Temporal gate: air control opens over this long after a tyre leaves the ground (seconds), per tyre, averaged; it closes four times as fast when the tyre's back down.")]
        public float AirGateTime = 0.4f;
        [Min(0f)]
        [Tooltip("Optional gate for pitching: above 0, a Target Speed held from the ground doesn't pitch the car until it's moved in the air, then pitching opens over this long (seconds). 0 = Target Speed pitches straight away (through the gates).")]
        public float AirThrottleGateTime = 0f;
        [Tooltip("Only with Air Roll Rate and Air Pitch Rate at 0: forces at ungrounded tire anchors roll the car with steer and pitch it with signed Target Speed, in rad/s², for as long as they're held (holding the throttle spins it forward). 0 = no air control.")]
        public float AirControl = 4f;
        [Min(0f)]
        [Tooltip("Maximum angular acceleration from the car's own tire and rotation-assist forces, in rad/s². Those forces are blended through this guard; 0 disables the limit.")]
        public float TorqueGuardMaxAngularAcceleration = 30f;
        [Min(0f)]
        [Tooltip("Time for the torque guard to blend toward its limited angular acceleration. 0 applies the limit immediately.")]
        public float TorqueGuardBlendTime = 0.15f;
        [Tooltip("Log angular-acceleration spikes with the previous step's tire, stabilization, handling-assist and physics-residual contributions.")]
        public bool LogTorqueGuardDiagnostics;
        [Min(0f)]
        [Tooltip("Minimum measured or unexplained angular acceleration that triggers a torque diagnostic log (rad/s²).")]
        public float TorqueGuardDiagnosticThreshold = 30f;
        [Tooltip("The car counts as flipped when tilted more than this (degrees from upright).")]
        public float FlippedTilt = 100f;
        [Tooltip("How hard a flipped car turns itself back over (rad/s²). 0 = it stays flipped (send Unflip, or let the player get out and push).")]
        public float SelfRightStrength = 12f;
        [Tooltip("How long (seconds) a car must lie flipped, nearly stopped, before it rights itself.")]
        public float SelfRightDelay = 1.5f;
        [Tooltip("A flipped car only rights itself while slower than this (m/s), so it doesn't magically flip mid-tumble.")]
        public float SelfRightMaxSpeed = 3f;
        [Tooltip("A hop (m/s upward) as the car starts righting itself, so it has room to turn over.")]
        public float SelfRightLift = 3f;
        [Range(0f, 180f)]
        [Tooltip("Tilted further than this (degrees from upright) and nearly stopped counts as stuck, so it rights itself after Self Right Delay: 70 includes lying on its side, not just its roof.")]
        public float SelfRightTilt = 70f;
        [Min(0f)]
        [Tooltip("Stuck on its roof or side for Self Right Delay: how long the automatic roll back onto the wheels takes (seconds). It rolls over the nose the short way round, floaty, and lands level. 0 = the old spring (turns toward upright with Self Right Strength).")]
        public float SelfRightRollTime = 0.6f;

        [Header("Whoah Lean")]
        [Range(0f, 45f)]
        [Tooltip("How far a hard turn throws the body outward, cartoon style (degrees at full Whoah Lean At). The Roll Save brings it back as it winds up. 0 = off.")]
        public float WhoahLeanAngle = 12f;
        [Min(0.1f)]
        [Tooltip("The turn's sideways pull (m/s², 10 ≈ 1 g) that gives the full lean.")]
        public float WhoahLeanAt = 15f;
        [Min(0.05f)]
        [Tooltip("How fast the lean wobbles (per second).")]
        public float WhoahWobble = 1.4f;
        [Range(0f, 1f)]
        [Tooltip("How quickly the wobble settles (1 = no wobble, low = wobbles on and on).")]
        public float WhoahWobbleDamping = 0.25f;

        [Header("Feet Down")]
        [Min(0f)]
        [Tooltip("While some tyres stand on the ground and others are lifted, how hard the lifted ones are pulled down (rad/s², times the share of tyres lifted: half of it with two of four up). A twist only, so it doesn't shove the car. 0 = off.")]
        public float FeetDownStrength = 12f;
        [Min(0f)]
        [Tooltip("Damps that twist, so it settles instead of slapping down.")]
        public float FeetDownDamping = 3f;
        [Range(0f, 45f)]
        [Tooltip("Feet Down only acts with the car tilted at least this far from the ground it stands on (degrees), so it pulls a tipping car back down but leaves a car driving off a ramp's edge alone.")]
        public float FeetDownMinTilt = 8f;

        [Header("Rollover")]
        [Range(0f, 1f)]
        [Tooltip("How uneven the car's load may get across it before it acts (the Load Transfer variable: 0 even, 1 one side carrying everything). Past it, the two below fade in.")]
        public float RolloverThreshold = 0.6f;
        [Min(0f)]
        [Tooltip("At full risk, how fast the car's ground velocity is calmed (per second), at the centre of mass so it can't tip the car. Only the sideways slide unless Rollover Damps All.")]
        public float RolloverDamping = 4f;
        [Tooltip("Damp all of the car's ground velocity (it slows down too), not just its sideways slide.")]
        public bool RolloverDampsAll;
        [Range(0f, 1f)]
        [Tooltip("At full risk, how far the sideways grip's push moves up toward the centre of mass height (1 = all the way): it still turns the car, but can't trip it.")]
        public float RolloverGripLift = 1f;

        [Header("Roll Save")]
        [Tooltip("How fast the roll save winds up (per second) while the car is in a big turn. It goes from -1 to 1, so 1 = a full second of big turn before it starts pushing, two to full strength.")]
        public float RollSaveRate = 1f;
        [Tooltip("How fast it falls back toward -1 (per second) once the turn is over.")]
        public float RollSaveRelaxRate = 2f;
        [Range(0f, 1f)]
        [Tooltip("How far the wheels must be turned (0-1) for a big turn.")]
        public float RollSaveSteer = 0.6f;
        [Min(0f)]
        [Tooltip("...and how fast the car must be going (m/s).")]
        public float RollSaveMinSpeed = 8f;
        [Range(0f, 90f)]
        [Tooltip("Tilted further than this from the ground (degrees, e.g. up on two wheels) also winds it up, turning or not. 0 = only big turns.")]
        public float RollSaveTilt = 15f;
        [Min(0f)]
        [Tooltip("How hard it rolls the car back onto its wheels at full save (rad/s²); scales with the save above 0. 0 = off.")]
        public float RollSaveStrength = 25f;
        [Min(0f)]
        [Tooltip("Damps the roll while saving, so it lands on its wheels instead of bouncing over the other way. Lower = bouncier.")]
        public float RollSaveDamping = 3f;

        [Header("Jumps")]
        [Min(0f)]
        [Tooltip("Leaving the ground at least this fast (m/s, away from the ground it was on) is a jump: a ramp or a crest. Slower is a bump. 0 = launches never count.")]
        public float JumpMinLaunchSpeed = 2f;
        [Tooltip("Driving off a ledge (the ground dropping well away under the tyres) is a jump too, with the hang but no boost.")]
        public bool LedgeJumps = true;
        [Min(0f)]
        [Tooltip("How long it must be in the air before it counts (seconds), so bumps and skips don't.")]
        public float JumpMinAirTime = 0.12f;
        [Min(0f)]
        [Tooltip("Extra upward speed a launched jump gets (m/s), whatever the speed: small ramps still pop on a slow car.")]
        public float JumpBoost = 1.5f;
        [Min(0f)]
        [Tooltip("More boost per m/s it launched at.")]
        public float JumpBoostScale = 0.4f;
        [Min(0f)]
        [Tooltip("The most boost (m/s), so fast cars don't go to orbit.")]
        public float JumpBoostMax = 5f;
        [Min(0f)]
        [Tooltip("How long the boost takes to lift the car (seconds), easing in and out. 0 = all at once.")]
        public float JumpBoostTime = 0.25f;
        [Min(0f)]
        [Tooltip("At the peak of a jump, how long it hovers there (seconds). 0 = no hover.")]
        public float HoverTime = 0.35f;
        [Range(0f, 1f)]
        [Tooltip("Gravity during the hover (a fraction of normal): 0 = it hangs still, higher = it starts sinking.")]
        public float HoverGravity = 0.05f;
        [Min(0f)]
        [Tooltip("Faster jumps last longer: Jump Boost Time, Hover Time and Max Hang Time are multiplied by 1 + this x (takeoff speed / Max Speed). 1 = twice as long at top speed; 0 = the same at any speed.")]
        public float JumpTimeSpeedFactor = 1f;
        [Range(0f, 1f)]
        [Tooltip("Gravity around the peak of a jump (a fraction of normal): low = a long float at the top.")]
        public float HangGravity = 0.4f;
        [Min(0.1f)]
        [Tooltip("How close to the peak the float is (m/s of upward or downward speed): wider = floatier for longer.")]
        public float PeakFloatWindow = 4f;
        [Min(0f)]
        [Tooltip("Gravity on the way down, after the float (1 = normal, above 1 = a snappier landing).")]
        public float FallGravity = 1.2f;
        [Min(0f)]
        [Tooltip("The float ends after this long in the air (seconds), easing back to normal gravity. 0 = no float.")]
        public float MaxHangTime = 2.5f;

        [Header("Bounce")]
        [Min(0f)]
        [Tooltip("Hitting a wall or another car at least this fast (m/s, into it) bounces the car off: it keeps its slide along the wall instead of grinding. 0 = off.")]
        public float BounceMinSpeed = 2f;
        [Range(0f, 90f)]
        [Tooltip("How steep a surface must be, from the ground the car is on, to bounce off (degrees): flatter is a floor or a ramp to drive up.")]
        public float BounceMinWallAngle = 45f;
        [Min(0f)]
        [Tooltip("Bounces are found by a cast from each tyre along the car's velocity, reaching past the body's edge by this much (metres) beyond this step's travel: more catches walls a moment earlier.")]
        public float BounceLookAhead = 0.1f;
        [Min(0f)]
        [Tooltip("How long a found wall is checked before the bounce (seconds): the tyres that found it cast again along the way the car came in each step, and the first step that misses it drops the bounce at once. 0 = straight away.")]
        public float BounceConfirmTime = 0.06f;
        [Min(0f)]
        [Tooltip("Push away from what was hit, per m/s it was hit at (m/s). Half each against another car (it bounces too).")]
        public float BouncePush = 0.35f;
        [Min(0f)]
        [Tooltip("The most push (m/s), so a huge hit doesn't fire the car across the map.")]
        public float BounceMaxPush = 8f;
        [Range(0f, 1f)]
        [Tooltip("Speed lost along the wall on a head-on hit (a fraction; glancing hits lose less). 0 = keeps every bit.")]
        public float BounceSpeedLoss = 0.3f;
        [Min(0f)]
        [Tooltip("Hits at least this hard (m/s) on the ground also hop the car, so the tyres leave the ground and nothing grinds: picked up, moved over, plopped down.")]
        public float BounceHopSpeed = 6f;
        [Min(0f)]
        [Tooltip("The hop (m/s upward; half at Bounce Hop Speed, all of it at three times that). 0 = no hop.")]
        public float BounceHop = 2.5f;
        [Range(0f, 2f)]
        [Tooltip("How firmly the nose turns along the wall after a bounce, so it lands pointing down the road. 0 = off.")]
        public float BounceAlign = 1f;
        [Min(0f)]
        [Tooltip("How long the bounce is in control (seconds): it holds the car's motion (the slide and push, no spin, the nose easing along the wall) while the tyres and assists stand aside.")]
        public float BounceAlignTime = 0.35f;
        [Min(1f)]
        [Tooltip("The hardest the bounce may push to hold the car's motion (m/s² and rad/s²), so it stays physical against the wall and the tyres.")]
        public float BounceHoldAcceleration = 80f;
        [Range(0f, 1f)]
        [Tooltip("How much grip the tyres lose right after a bounce (returning over Bounce Grip Time), so they don't snap the car round as it lands.")]
        public float BounceGripEase = 0.6f;
        [Min(0f)]
        [Tooltip("How long the grip takes to come back (seconds).")]
        public float BounceGripTime = 0.3f;
        [Min(0f)]
        [Tooltip("Optional: an off-centre hit swings the tail (rad/s per m/s hit at). 0 = off: forgiving.")]
        public float BounceSpinKick = 0f;
        [Min(0f)]
        [Tooltip("The least time between bounces (seconds), so one hit doesn't bounce twice.")]
        public float BounceCooldown = 0.25f;
        [Tooltip("Body Visual's wobble on a bounce: degrees of tip per m/s hit at.")]
        public float BodyWobble = 0.8f;
        [Tooltip("The most it tips (degrees).")]
        public float BodyWobbleMax = 12f;
        [Tooltip("Squash per m/s hit at (a fraction of its height).")]
        public float BodySquash = 0.012f;
        [Min(0.1f)]
        [Tooltip("How fast the body wobbles (per second).")]
        public float BodyWobbleFrequency = 3f;
        [Range(0f, 1f)]
        [Tooltip("How quickly the wobble settles (1 = no wobble, low = jiggles on).")]
        public float BodyWobbleDamping = 0.3f;

        [Header("Feedback")]
        [Tooltip("Direction-adjusted impacts slower than this (m/s) don't send On Crash. Impacts aligned with the nearest tire's ground normal count less than side-on impacts.")]
        public float MinCrashSpeed = 4f;
        [Tooltip("Shorter time in the air than this (seconds) doesn't send On Landed (bumps).")]
        public float MinAirTimeForLanding = 0.3f;

#if UNITY_EDITOR
        // Starting points for the two feels the vehicle kit is built around. Tweak from there.

        [ContextMenu("Preset/Street Racer (NFS Underground)")]
        private void PresetStreet()
        {
            UnityEditor.Undo.RecordObject(this, "Street Racer Preset");
            MaxSpeed = 55f; Acceleration = 13f; BrakeDeceleration = 28f;
            MaxSteerAngle = 30f; SteerSpeed = 4f; SteerReturnSpeed = 7f;
            FrontGrip = 16f; RearGrip = 13f; MaxTyreAcceleration = 28f; TyreForceHeight = 0.55f;
            HandbrakeGrip = 0.25f; HandbrakeDeceleration = 5f;
            DriftEntry = DriftEntryMode.HandbrakeOrSharpSteer; DriftMinSpeed = 14f; DriftSteerThreshold = 0.8f;
            HoldButtonToDrift = false; DriftHop = 0f;
            DriftRearGrip = 0.3f; DriftFrontGrip = 0.95f; DriftSteerAssist = 2.5f; DriftAutoTurn = 0f;
            DriftMaxAngle = 50f; DriftCounterSteer = 6f; DriftMomentumKeep = 1.2f; DriftExitAngle = 8f;
            DriftStyle = DriftHandling.Arcade; DriftAngleMin = 25f; DriftAngleMax = 55f; DriftTurnRate = 65f;
            DriftAutoTurnRate = 0f; DriftHeadingGrip = 6f; DriftSpeedLoss = 0.12f; DriftExitSteer = 0.1f; DriftExitTime = 0.4f;
            SideForceHeight = 0.85f; LoadGripAmount = 0.5f; MinSpringDampingRatio = 0.35f; ContactNormalMaxAngle = 50f;
            UprightSupportFloor = 0.35f; WhoahLeanAngle = 10f; RollSaveStrength = 25f;
            MiniTurbo = false;
            BoostAcceleration = 14f; BoostDuration = 2f; BoostMaxSpeedBonus = 15f;
            Mass = 1300f; CenterOfMassOffset = new Vector3(0f, -0.3f, 0.05f); InertiaScale = 1.4f;
            StabilizeStrength = 6f; StabilizeDamping = 2f; GroundPlaneTorqueCompensation = 1f;
            GroundedTurnLeanAngle = 4f; StickyForce = 0.2f;
            AirLevelStrength = 1.5f; AirControl = 2f; SelfRightStrength = 12f; SelfRightDelay = 2f;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Preset/Kart (Mario Kart)")]
        private void PresetKart()
        {
            UnityEditor.Undo.RecordObject(this, "Kart Preset");
            MaxSpeed = 32f; Acceleration = 18f; BrakeDeceleration = 30f;
            MaxSteerAngle = 35f; SteerSpeed = 7f; SteerReturnSpeed = 10f;
            FrontGrip = 20f; RearGrip = 20f; MaxTyreAcceleration = 35f; TyreForceHeight = 0.85f;
            HandbrakeGrip = 0.6f; HandbrakeDeceleration = 1f;
            DriftEntry = DriftEntryMode.Handbrake; DriftMinSpeed = 8f; DriftSteerThreshold = 0.3f;
            HoldButtonToDrift = true; DriftHop = 2.5f;
            DriftRearGrip = 0.45f; DriftFrontGrip = 1f; DriftSteerAssist = 2f; DriftAutoTurn = 2.5f;
            DriftMaxAngle = 35f; DriftCounterSteer = 12f; DriftMomentumKeep = 3f; DriftExitAngle = 5f;
            DriftStyle = DriftHandling.Arcade; DriftAngleMin = 20f; DriftAngleMax = 45f; DriftTurnRate = 45f;
            DriftAutoTurnRate = 40f; DriftHeadingGrip = 10f; DriftSpeedLoss = 0.05f; DriftExitTime = 0.25f;
            SideForceHeight = 1f; LoadGripAmount = 0.3f; MinSpringDampingRatio = 0.4f; ContactNormalMaxAngle = 45f;
            UprightSupportFloor = 0.5f; WhoahLeanAngle = 8f; RollSaveStrength = 30f;
            MiniTurbo = true; ChargeRate = 1f; TightTurnChargeBonus = 1f;
            BoostAcceleration = 12f; BoostDuration = 1.2f; BoostMaxSpeedBonus = 10f;
            Mass = 300f; CenterOfMassOffset = new Vector3(0f, -0.25f, 0f); InertiaScale = 1.2f;
            StabilizeStrength = 14f; StabilizeDamping = 3f; GroundPlaneTorqueCompensation = 1f;
            GroundedTurnLeanAngle = 7f; StickyForce = 0.25f;
            AirLevelStrength = 4f; AirControl = 3f; SelfRightStrength = 20f; SelfRightDelay = 0.75f;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Preset/Physical (flips easily)")]
        private void PresetPhysical()
        {
            UnityEditor.Undo.RecordObject(this, "Physical Preset");
            FrontGrip = 12f; RearGrip = 12f; MaxTyreAcceleration = 12f; TyreForceHeight = 0.15f;
            DriftEntry = DriftEntryMode.Off; MiniTurbo = false;
            DriftMomentumKeep = 0f; DriftCounterSteer = 0f; DriftStyle = DriftHandling.Physics;
            SideForceHeight = 0.15f; LoadGripAmount = 1f; UprightSupportFloor = 0f;
            WhoahLeanAngle = 0f; RollSaveStrength = 0f;
            CenterOfMassOffset = new Vector3(0f, -0.1f, 0f); InertiaScale = 1f;
            StabilizeStrength = 0f; StabilizeDamping = 0.5f; GroundPlaneTorqueCompensation = 0f;
            GroundedTurnLeanAngle = 0f; StickyForce = 0.05f;
            AirLevelStrength = 0f; AirControl = 0f; SelfRightStrength = 0f;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
