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
        [Tooltip("Total distance from each wheel anchor checked for ground (metres).")]
        public float GroundCheckDistance = 1.6f;
        [Range(0f, 1f)]
        [Tooltip("Places the tire's outer edge at this fraction of Ground Check Distance when the ground is farther away. A nearer hit moves the tire toward the ground; the full cast still checks beyond the tire. Suspension Rest Length limits spring travel, not tire placement.")]
        public float TirePlacementThreshold = 0.5f;
        [Tooltip("Spring stiffness: m/s² of push per metre compressed. ~10 / Stiffness is how far the car sags at rest (60 → 17 cm). Higher = stiffer, twitchier.")]
        public float SpringStiffness = 60f;
        [Tooltip("Spring damping: stops the car bouncing. ~2 × √Stiffness × 0.5 is a good start. Too low bounces, too high feels like bricks.")]
        public float SpringDamping = 7f;
        [Tooltip("The most a single spring can push (m/s²), a bump stop for big landings so the car doesn't get launched.")]
        public float MaxSuspensionAcceleration = 60f;

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
        [Tooltip("Where the tyres push the car: 0 = at the road (realistic: hard cornering leans the car and can roll it over), 1 = at the centre of mass height (cornering never rolls it). The main 'flips vs stays planted' dial; 0.5-0.7 feels arcade but still flips on kerbs and crashes.")]
        public float TyreForceHeight = 0.6f;

        [Header("Handbrake")]
        [Tooltip("Grip of the handbrake wheels while it's pulled (a multiplier: 0.3 = 30% grip). Low = easy flicks into a slide.")]
        public float HandbrakeGrip = 0.3f;
        [Tooltip("How much the handbrake slows the car, in m/s².")]
        public float HandbrakeDeceleration = 6f;

        [Header("Drift")]
        [Tooltip("How a drift starts.")]
        public DriftEntryMode DriftEntry = DriftEntryMode.HandbrakeOrSharpSteer;
        [Tooltip("Slowest speed (m/s) a drift can start at; it ends below half of this.")]
        public float DriftMinSpeed = 12f;
        [Range(0f, 1f)]
        [Tooltip("How far the steering must be turned (0..1) to start a drift.")]
        public float DriftSteerThreshold = 0.8f;
        [Tooltip("On: the drift lasts while the handbrake / drift button is held (Mario Kart). Off: it lasts while the car is still sliding (NFS).")]
        public bool HoldButtonToDrift = false;
        [Tooltip("A little hop (m/s upward) when a drift starts, like a kart. 0 for none.")]
        public float DriftHop = 0f;
        [Tooltip("Rear grip while drifting (a multiplier). Lower = wider, longer slides.")]
        public float DriftRearGrip = 0.35f;
        [Tooltip("Front grip while drifting (a multiplier). Keep it near 1 so the front still steers the slide.")]
        public float DriftFrontGrip = 0.9f;
        [Tooltip("How strongly the steering swings the car's tail while drifting (yaw, rad/s² at full lock). The 'I can steer my drift' feel.")]
        public float DriftSteerAssist = 3f;
        [Tooltip("A constant turn into the drift's direction (rad/s²), whatever the steering: Mario Kart drifts always curve. 0 for NFS-style.")]
        public float DriftAutoTurn = 0f;
        [Tooltip("The widest slide angle the assist allows, in degrees. Past it the car is pulled back (counter-steer), so it doesn't spin out.")]
        public float DriftMaxAngle = 45f;
        [Tooltip("How hard the car is pulled back when the slide goes past Drift Max Angle (an automatic counter-steer). 0 lets it spin.")]
        public float DriftCounterSteer = 8f;
        [Tooltip("How quickly the car's speed swings round to its drift heading while drifting (per second). Higher keeps your speed through drifts (arcade); 0 = pure physics, sliding scrubs speed.")]
        public float DriftMomentumKeep = 1.5f;
        [Tooltip("During a handbrake drift, how quickly its movement heading realigns toward the car's forward direction (degrees per second).")]
        public float ForcedDriftRealignSpeed = 180f;
        [Range(0f, 1f)]
        [Tooltip("Minimum fraction of Forced Drift Realign Speed when actual speed reaches its target. Lower values keep the drift heading longer.")]
        public float ForcedDriftMinRealignFraction = 0.1f;
        [Tooltip("NFS-style drifts end once the slide angle drops under this (degrees).")]
        public float DriftExitAngle = 8f;

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
        [Tooltip("At low speed, how hard the car levels toward the support-weighted ground normal. Fades to zero at Max Speed and while fewer than about half the tires support the car. Compensates torque from tire forces. 0 = pure physics.")]
        public float StabilizeStrength = 8f;
        [Tooltip("At low speed, damps roll and pitch while the tires meaningfully support the car. Fades to zero at Max Speed.")]
        public float StabilizeDamping = 2f;
        [Min(0f)]
        [Tooltip("Time for tilt correction to fade in/out and turn toward a new support normal. Softens changes in tire contact over ramps; 0 is immediate.")]
        public float StabilizeTransitionTime = 0.25f;
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
        [Tooltip("In the air, grounded tires are ignored; forces at ungrounded tire anchors roll the car with steer and pitch it with signed Target Speed, in rad/s². 0 = no air control.")]
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

        [Header("Feedback")]
        [Tooltip("Impacts slower than this (m/s) don't send On Crash.")]
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
            MiniTurbo = false;
            BoostAcceleration = 14f; BoostDuration = 2f; BoostMaxSpeedBonus = 15f;
            Mass = 1300f; CenterOfMassOffset = new Vector3(0f, -0.3f, 0.05f); InertiaScale = 1.4f;
            StabilizeStrength = 6f; StabilizeDamping = 2f; StickyForce = 0.2f;
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
            MiniTurbo = true; ChargeRate = 1f; TightTurnChargeBonus = 1f;
            BoostAcceleration = 12f; BoostDuration = 1.2f; BoostMaxSpeedBonus = 10f;
            Mass = 300f; CenterOfMassOffset = new Vector3(0f, -0.25f, 0f); InertiaScale = 1.2f;
            StabilizeStrength = 14f; StabilizeDamping = 3f; StickyForce = 0.25f;
            AirLevelStrength = 4f; AirControl = 3f; SelfRightStrength = 20f; SelfRightDelay = 0.75f;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Preset/Physical (flips easily)")]
        private void PresetPhysical()
        {
            UnityEditor.Undo.RecordObject(this, "Physical Preset");
            FrontGrip = 12f; RearGrip = 12f; MaxTyreAcceleration = 12f; TyreForceHeight = 0.15f;
            DriftEntry = DriftEntryMode.Off; MiniTurbo = false;
            DriftMomentumKeep = 0f; DriftCounterSteer = 0f;
            CenterOfMassOffset = new Vector3(0f, -0.1f, 0f); InertiaScale = 1f;
            StabilizeStrength = 0f; StabilizeDamping = 0.5f; StickyForce = 0.05f;
            AirLevelStrength = 0f; AirControl = 0f; SelfRightStrength = 0f;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
