using Remedy.Schematics;
//using SaintsField;
using UnityEngine;

namespace Remedy.Weapons
{

    [System.Serializable]
    public class FireMode
    {
        public enum AimMethod
        {
            None,
            LockToHolder,
            AimDelta,
            AimDirectly
        }
        public enum Redirection
        {
            None,
            NearestToMuzzleForward,
            NearestHolderPosition,
            NearestCenterOfScreen
        }
        public enum ShotDisperseMethod
        {
            None,
            Spread,
            Incremental
        }

        [Tooltip("The way the Muzzle is Aimed. LockToHolder: Follows the WeaponHolder's Transform, AimDelta: adds the Aim Input to the current orientation, AimDirectly: Sets the orientation to the Aim Input")]
        public AimMethod AimMode;
        public bool UsesAim => AimMode == AimMethod.AimDirectly || AimMode == AimMethod.AimDelta;
        [Tooltip("The projectile prefab: a Schematic Object with a Projectile Behavior.")]
        public SchematicObject Projectile;
        [Tooltip("The number of Projectiles shot")]
        public int ShotProjectileCount = 1;
        [Tooltip("The Offset, in the Muzzle's Local Space, that the Projectile is spawned in relation to the Muzzle's position.")]
        public Vector3 ProjectileSpawnOffset;
        [Tooltip("Spread will change the angle of each shot within a certain Random Range, Incremental will change the angle in incremental steps, per projectile shot (ignores the angle of the Muzzle). If any 'Forced Axes' are set below, each axis set to true will force the Projectile to the Muzzle's Rotation for specified Axis.")]
        public ShotDisperseMethod DisperseMethod;
        public bool IsDisperseRandom => DisperseMethod == ShotDisperseMethod.Spread;
        public bool IsDisperseIncremental => DisperseMethod == ShotDisperseMethod.Incremental;
        [Tooltip("Limits the axes in which the Projectile can move in, in relation to the Muzzle's Rotation.")]
        public Vector3Bool SpawnRotationClamp = Vector3Bool.True;
        [Tooltip("The Random Range min-max value for each axes of the shot when Fired")]
        //[ShowIf("IsDisperseRandom")]
        public float Spread = 5f;
        [Tooltip("The amount to increment the Angle by for each Projectile Shot when Fired.")]
        //[ShowIf("IsDisperseIncremental")]
        public Vector3 AngularIncrement = Vector3.zero;
        [Tooltip("For each forced Axes, the Projectile will be set to that axis of Rotation from the Muzzle.")]
        //[ShowIf("IsDisperseIncremental")]
        public Vector3Bool IncrementalForcedAxes = Vector3Bool.False;

        public bool InfiniteAmmo = false;
        public float FireRate;
        //[HideIf("InfiniteAmmo")]
        public int MaxAmmo;
        //[HideIf("InfiniteAmmo")]
        public bool CanReload;
        public bool RapidFire = false;
        public int ReloadTime = 2000;

        [Tooltip("Determines the way in which the desired Attack Target is chosen.")]
        public Redirection RedirectionMode = Redirection.NearestToMuzzleForward;
        public bool IsNearestMuzzleForwardRedirection => RedirectionMode == Redirection.NearestToMuzzleForward;
        [Tooltip("The Radius of the Sphere Cast to find the nearest Targetfrom the Muzzle's Forward")]
        //[ShowIf("IsNearestMuzzleForwardRedirection")]
        public float RedirectionCheckRadius;
        [Tooltip("The maximum Distance to check for targets.")]
        //[ShowIf("IsNearestMuzzleForwardRedirection")]
        public float MaxCheckDistance;

        [Range(0f, 1f)]
        [Tooltip("The strength of which the object with this Weapon is oriented toward the nearest attack target (rotationally)")]
        public float RotationalRedirectionStrength = 0.1f;
        [Tooltip("The strength of which the object with this Weapon is translated toward the nearest attack target (positionally)")]
        public float PositionalRedirectionStrength = 0.5f;

        [Tooltip("The amount of Pull for this Fire Mode")]
        public float Pull;

    }


}