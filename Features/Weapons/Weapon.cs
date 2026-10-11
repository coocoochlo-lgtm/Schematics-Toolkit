//using SaintsField;
using UnityEngine;
using System;
//using SaintsField.Playa;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Remedy.Framework;
using Remedy.Schematics;
using Remedy.Weapons.Projectiles;
using Remedy.Common;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Remedy.Weapons
{
    /// <summary>
    /// A Weapon prefab: held in the Sockets of a <see cref="WeaponHolder"/>, firing Projectiles from its Fire Modes' Muzzles.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Weapon"", ""kind"": ""behavior"", ""menu"": ""Weapons/Weapon"", ""description"": ""A Weapon prefab: held in the Sockets of a WeaponHolder, firing Projectiles from its Fire Modes' Muzzles."", ""variables"": [""PrimaryHandle"", ""SecondHandle"", ""FireModeMuzzles"", ""InWorld"", ""UsePhysics"", ""Data"", ""Holder"", ""WeaponHolderRigidbody"", ""AttackDirection"", ""ID""], ""signals"": [""OnPickedup"", ""OnDropped"", ""OnPull"", ""OnRedirectDirection"", ""OnRedirectPosition""], ""use_for"": [""guns, launchers and other firing weapons""]}")]
    [SchematicBehaviorPath("Weapons/Weapon")]
    public class Weapon : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""OnPickedup"", ""kind"": ""signal"", ""behavior"": ""Weapon"", ""direction"": ""output"", ""params"": [{""name"": ""Holder"", ""type"": ""GameObject""}], ""description"": ""Invoked when the Weapon is picked up by a WeaponHolder. The GameObject possessing the WeaponHolder Component is passed."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Invoked when the Weapon is picked up by a WeaponHolder. The GameObject possessing the WeaponHolder Component is passed.")]
        [OutputSignal, Parameter("Holder", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnPickedup;
        [SchematicDoc(@"{""name"": ""OnDropped"", ""kind"": ""signal"", ""behavior"": ""Weapon"", ""direction"": ""output"", ""description"": ""Invoked when the Weapon is dropped by a WeaponHolder"", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Invoked when the Weapon is dropped by a WeaponHolder")]
        [OutputSignal]
        public readonly SignalHandle OnDropped;

        [SchematicDoc(@"{""name"": ""OnPull"", ""kind"": ""signal"", ""behavior"": ""Weapon"", ""direction"": ""output"", ""params"": [{""name"": ""Pull"", ""type"": ""Vector3""}], ""description"": ""Invoked after this Fire Mode shoots, intended to pull the Object containing the Weapon toward the direction it is Attacking."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Invoked after this Fire Mode shoots, intended to pull the Object containing the Weapon toward the direction it is Attacking.")]
        [OutputSignal, Parameter("Pull", typeof(Vector3))]
        public readonly SignalHandle<Vector3> OnPull;
        [SchematicDoc(@"{""name"": ""OnRedirectDirection"", ""kind"": ""signal"", ""behavior"": ""Weapon"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Invoked before this Fire Mode shoots, redirecting the object toward the best Target, rotationally (must be handled externally)"", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Invoked before this Fire Mode shoots, redirecting the object toward the best Target, rotationally (must be handled externally)")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> OnRedirectDirection;
        [SchematicDoc(@"{""name"": ""OnRedirectPosition"", ""kind"": ""signal"", ""behavior"": ""Weapon"", ""direction"": ""output"", ""params"": [{""name"": ""Direction"", ""type"": ""Vector3""}], ""description"": ""Invoked before this Fire Mode shoots, redirecting the object toward the best Target, positionally (must be handled externally)"", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Invoked before this Fire Mode shoots, redirecting the object toward the best Target, positionally (must be handled externally)")]
        [OutputSignal, Parameter("Direction", typeof(Vector3))]
        public readonly SignalHandle<Vector3> OnRedirectPosition;

        // When two Handles are added to the Weapon, the Weapon will use the first for translation, and the angle to the next one for rotation.
        [SchematicDoc(@"{""name"": ""PrimaryHandle"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""WeaponHandle"", ""default"": ""null"", ""description"": ""The handles of the Weapon, used to place the Weapon into a Weapon Holder's Weapon Socket.""}")]
        [Tooltip("The handles of the Weapon, used to place the Weapon into a Weapon Holder's Weapon Socket.")]
        public readonly VariableHandle<WeaponHandle> PrimaryHandle = (WeaponHandle)null;
        [SchematicDoc(@"{""name"": ""SecondHandle"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""WeaponHandle"", ""default"": ""null"", ""description"": ""Second Handle (WeaponHandle) of Weapon.""}")]
        public readonly VariableHandle<WeaponHandle> SecondHandle = (WeaponHandle)null;

        // Muzzles are paired with their FireModes here (received from the Weapon Data). When a WeaponHolder fires or
        // reloads, it finds the FireMode to act upon by its index in this list.
        [SchematicDoc(@"{""name"": ""FireModeMuzzles"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""FireModeMuzzlePair[]"", ""default"": ""null"", ""description"": ""The Transforms of the Muzzles to use for the FireModes of the Weapons.""}")]
        [Tooltip("The Transforms of the Muzzles to use for the FireModes of the Weapons.")]
        public readonly VariableHandle<FireModeMuzzlePair[]> FireModeMuzzles = (FireModeMuzzlePair[])null;

        [SchematicDoc(@"{""name"": ""InWorld"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""If true, the weapon will be placed in the world.""}")]
        [Tooltip("If true, the weapon will be placed in the world.")]
        public readonly VariableHandle<bool> InWorld = true;
        [SchematicDoc(@"{""name"": ""UsePhysics"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""If the Weapon is not being held, and this is true, it will use Rigidbody physics.""}")]
        [Tooltip("If the Weapon is not being held, and this is true, it will use Rigidbody physics.")]
        public readonly VariableHandle<bool> UsePhysics = true;
        [SchematicDoc(@"{""name"": ""Data"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""WeaponData"", ""default"": ""null"", ""description"": ""The Data of the Weapon, as a Scriptable Object""}")]
        [Tooltip("The Data of the Weapon, as a Scriptable Object")]
        public readonly VariableHandle<WeaponData> Data = (WeaponData)null;

        [SchematicDoc(@"{""name"": ""Holder"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""GameObject"", ""default"": ""null"", ""description"": ""The object holding, controlling and managing this Weapon (the one with the Weapon Holder).""}")]
        [Tooltip("The object holding, controlling and managing this Weapon (the one with the Weapon Holder).")]
        public readonly VariableHandle<GameObject> Holder = (GameObject)null;
        [SchematicDoc(@"{""name"": ""WeaponHolderRigidbody"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""Rigidbody"", ""default"": ""null"", ""description"": ""The Rigidbody of the WeaponHolder""}")]
        [Tooltip("The Rigidbody of the WeaponHolder")]
        public readonly VariableHandle<Rigidbody> WeaponHolderRigidbody = (Rigidbody)null;
        [SchematicDoc(@"{""name"": ""AttackDirection"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""Vector3"", ""default"": ""Vector3.zero"", ""description"": ""Attack Direction (Vector3) of Weapon.""}")]
        public readonly VariableHandle<Vector3> AttackDirection = Vector3.zero;

        // TODO: Replace with a system that logs prefab ID's in Weapon Data to make Weapon Systems more clear.
        [SchematicDoc(@"{""name"": ""ID"", ""kind"": ""variable"", ""behavior"": ""Weapon"", ""type"": ""int"", ""default"": ""0"", ""description"": ""ID (int) of Weapon.""}")]
        public readonly VariableHandle<int> ID = 0;

        private readonly VariableHandle<Quaternion> _aimTarget = Quaternion.identity;
        private readonly VariableHandle<float> _lastFireTime = float.NegativeInfinity;

        protected override void OnEnable()
        {
            var primary = PrimaryHandle.Value;
            if (primary == null || primary.Transform == null)
            {
                Debug.LogError("The Primary Handle of the Weapon hasn't been set, so it can't be used. Please set the Primary Handle Reference in the Inspector.", Self.GameObject);
                Self.Disable();
                return;
            }
            primary.Offset = Self.Transform.position - primary.Transform.position;

            var muzzles = FireModeMuzzles.Value;
            if (muzzles != null)
                foreach (var muzzle in muzzles)
                    if (muzzle != null)
                        muzzle.Data = Data.Value;

            var rigidbody = Self.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = !UsePhysics.Value;
        }

        protected override void OnUpdate()
        {
            var primary = PrimaryHandle.Value;
            if (primary == null || primary.Socket == null || primary.Socket.Transform == null) return;

            var transform = Self.Transform;
            var socket = primary.Socket;
            Vector3 offset = socket.Transform.rotation * socket.PositionOffset;
            transform.position = socket.Transform.position + offset;

            // Rotation: if there's a second handle, aim at it
            var second = SecondHandle.Value;
            if (second != null && second.Transform != null && second.Socket != null && second.Socket.Transform != null)
            {
                Vector3 direction = (second.Socket.Transform.position - socket.Transform.position).normalized;
                if (direction.sqrMagnitude > 0.0001f) // prevent NaN
                    transform.rotation = Quaternion.LookRotation(direction);
            }
            else
            {
                // Otherwise match the rotation of the primary handle
                transform.rotation = socket.Transform.rotation;
                transform.eulerAngles += socket.AngularOffset;
            }
        }

        protected override void OnDisable()
        {
            ReleaseSockets(Self);
        }

        /// <summary>
        /// Places the selected Weapon's Handles into the Holder's Sockets, starting at the given socket.
        /// </summary>
        /// <returns>False if the needed Sockets aren't available.</returns>
        public bool Equip(GameObject holder, WeaponSocket[] sockets, int socket = 0)
        {
            var instance = Selected;
            var primary = PrimaryHandle.Value;
            var second = SecondHandle.Value;
            bool twoHanded = second != null && second.Transform != null;

            if (sockets == null || socket < 0 || socket >= sockets.Length || sockets[socket] == null)
                return false;
            if (twoHanded && (socket + 1 >= sockets.Length || sockets[socket + 1] == null))
                return false;

            ReleaseSockets(instance);

            primary.Socket = sockets[socket];
            primary.Socket.ActiveWeapon = instance;
            if (twoHanded)
            {
                second.Socket = sockets[socket + 1];
                second.Socket.ActiveWeapon = instance;
            }

            Holder.Value = holder;
            WeaponHolderRigidbody.Value = holder != null ? holder.GetComponentInParent<Rigidbody>() : null;

            var rigidbody = instance.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = true;

            OnPickedup.Invoke(instance, holder);
            return true;
        }

        /// <summary>
        /// Takes the selected Weapon out of its Holder's Sockets.
        /// </summary>
        public void Drop()
        {
            var instance = Selected;
            if (Holder.Value == null)
                return;

            ReleaseSockets(instance);
            Holder.Value = null;
            WeaponHolderRigidbody.Value = null;

            var rigidbody = instance.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = !UsePhysics.Value;

            OnDropped.Invoke(instance);
        }

        private void ReleaseSockets(BehaviorInstance instance)
        {
            Release(PrimaryHandle[instance], instance);
            Release(SecondHandle[instance], instance);

            static void Release(WeaponHandle handle, BehaviorInstance weapon)
            {
                if (handle?.Socket == null) return;
                if (handle.Socket.ActiveWeapon == weapon)
                    handle.Socket.ActiveWeapon = null;
                handle.Socket = null;
            }
        }

        public void Aim(Vector2 input, Transform muzzleTransform)
        {
            if (muzzleTransform == null)
                return;

            var muzzles = FireModeMuzzles.Value;
            if (muzzles == null)
                return;

            ref var aimTarget = ref _aimTarget.Ref;
            foreach (var fireMode in muzzles)
            {
                if (fireMode?.FireMode == null) continue;

                switch (fireMode.FireMode.AimMode)
                {
                    case FireMode.AimMethod.AimDelta:
                        aimTarget *= Quaternion.Euler(-input.y, input.x, 0);
                        break;

                    case FireMode.AimMethod.AimDirectly:
                        if (input.sqrMagnitude > 0.0001f)
                        {
                            float angle = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
                            aimTarget = Quaternion.Euler(0, angle, 0);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Fires the selected Weapon using the given Fire Mode.
        /// </summary>
        public void Fire(int mode = 0, bool pressed = false)
        {
            var instance = Selected;
            var muzzles = FireModeMuzzles.Value;
            if (muzzles == null || mode < 0 || mode >= muzzles.Length || muzzles[mode]?.FireMode == null)
                return;

            var muzzleDefinition = muzzles[mode];
            var fireMode = muzzleDefinition.FireMode;
            ref var lastFireTime = ref _lastFireTime.Ref;

            if ((muzzleDefinition.AmmoCount > 0 || fireMode.InfiniteAmmo)
                    && !muzzleDefinition.IsReloading
                    && (Time.time >= lastFireTime + fireMode.FireRate)
                    && (fireMode.RapidFire || pressed)
                    && muzzleDefinition.Muzzles != null && muzzleDefinition.Muzzles.Count > 0
                    && fireMode.Projectile != null)
            {
                List<Transform> muzzleTransforms = new();

                switch (muzzleDefinition.MuzzleSelection)
                {
                    case FireModeMuzzlePair.MuzzleSelectionStyle.All:
                        muzzleTransforms = muzzleDefinition.Muzzles;
                        break;
                    case FireModeMuzzlePair.MuzzleSelectionStyle.Random:
                        muzzleTransforms.Add(muzzleDefinition.Muzzles[UnityEngine.Random.Range(0, muzzleDefinition.Muzzles.Count)]);
                        break;
                    case FireModeMuzzlePair.MuzzleSelectionStyle.Incremental:
                        muzzleDefinition.MuzzleIndex = (muzzleDefinition.MuzzleIndex + 1) % muzzleDefinition.Muzzles.Count;
                        muzzleTransforms.Add(muzzleDefinition.Muzzles[muzzleDefinition.MuzzleIndex]);
                        break;
                }

                var projectileData = Projectile.DataOf(fireMode.Projectile);

                foreach (var muzzleTransform in muzzleTransforms)
                {
                    if (muzzleTransform == null) continue;

                    HandleRedirection(instance, fireMode, projectileData, muzzleTransform);

                    muzzleDefinition.AmmoCount--;
                    lastFireTime = Time.time;

                    OnPull.Invoke(instance, muzzleTransform.forward * fireMode.Pull);

                    // For each shot in a fire, determines the orientation uniquely
                    for (int i = 0; i < fireMode.ShotProjectileCount; i++)
                    {
                        var projectile = InstantiateProjectile(fireMode, muzzleTransform);
                        if (projectile == null) continue;

                        switch (fireMode.DisperseMethod)
                        {
                            case FireMode.ShotDisperseMethod.None:
                                OrientToMuzzle(fireMode, projectile, muzzleTransform);
                                break;
                            case FireMode.ShotDisperseMethod.Spread:
                                OrientToMuzzleWithSpread(fireMode, projectile, muzzleTransform);
                                break;
                            case FireMode.ShotDisperseMethod.Incremental:
                                OrientIncrementally(fireMode, projectile, i, muzzleTransform);
                                break;
                        }
                        HandleAffix(projectileData, projectile);
                    }
                }
            }
        }

        /// <summary>
        /// Calls Redirection Events based on the Type of Redirection to apply for this FireMode
        /// </summary>
        private void HandleRedirection(BehaviorInstance instance, FireMode mode, ProjectileData projectileData, Transform muzzleTransform)
        {
            switch (mode.RedirectionMode)
            {
                case FireMode.Redirection.NearestToMuzzleForward:
                    LayerMask layers = projectileData != null ? projectileData.CollisionLayer : (LayerMask)Physics.DefaultRaycastLayers;
                    if (Physics.SphereCast(muzzleTransform.position, mode.RedirectionCheckRadius, muzzleTransform.forward, out RaycastHit hit, mode.MaxCheckDistance, layers))
                    {
                        Vector3 direction = hit.point - muzzleTransform.position;
                        if (direction.magnitude < 0.01f) break;

                        OnRedirectPosition.Invoke(instance, direction.normalized);
                    }
                    break;
            }
        }

        /// <summary>
        /// Spawns the Projectile from its pool and aims it at whatever is at the center of the screen.
        /// </summary>
        private Transform InstantiateProjectile(FireMode mode, Transform muzzleTransform)
        {
            Vector3 screenCenter = new Vector3(Screen.width / 2f, Screen.height / 2f, 0);
            Ray ray = RemedyGameManager.MainCamera.ScreenPointToRay(screenCenter);

            var spawned = SchematicsManager.Spawn(mode.Projectile, muzzleTransform.position, Quaternion.LookRotation(ray.direction));
            var projectile = spawned != null ? spawned.GetSchematicBehavior<Projectile>() : null;
            if (projectile == null)
                return spawned != null ? spawned.transform : null;

            projectile.ParentFireMode.Value = mode;

            var data = projectile.Data.Value;
            if (data != null)
            {
                projectile.AimTargetPosition.Value = Physics.Raycast(ray, out RaycastHit hitInfo, data.LineUpDistance, data.CollisionLayer)
                    ? hitInfo.point
                    : ray.origin + ray.direction * data.LineUpDistance;
            }

            // TODO: Allow Signals to be passed from the WeaponHolder to the Projectile, so that functionality can be
            // performed per-character per-weapon per-projectile (subscribe to projectile.OnDestroyed for spawned.UID).

            return spawned.transform;
        }

        /// <summary>
        /// Simply orients to the Rotation of the Muzzle
        /// </summary>
        private void OrientToMuzzle(FireMode mode, Transform projectile, Transform muzzleTransform)
        {
            projectile.eulerAngles = muzzleTransform.eulerAngles * mode.SpawnRotationClamp;
            projectile.position = muzzleTransform.TransformPoint(mode.ProjectileSpawnOffset);
        }

        /// <summary>
        /// Orients to the Rotation of the Muzzle with some Random Spread
        /// </summary>
        private void OrientToMuzzleWithSpread(FireMode mode, Transform projectile, Transform muzzleTransform)
        {
            projectile.eulerAngles = muzzleTransform.eulerAngles * mode.SpawnRotationClamp
                                                        + new Vector3().RandomRange(-mode.Spread, mode.Spread);
            projectile.position = muzzleTransform.TransformPoint(mode.ProjectileSpawnOffset);
        }

        /// <summary>
        /// Sets the angle of motion in increments, with optional hard lock to the orientation of the Muzzle
        /// </summary>
        private void OrientIncrementally(FireMode mode, Transform projectile, int index, Transform muzzleTransform)
        {
            var euler = mode.AngularIncrement * index * !mode.IncrementalForcedAxes
                                                        + muzzleTransform.eulerAngles * mode.IncrementalForcedAxes * mode.SpawnRotationClamp;

            // The offset is applied in the incremented orientation, so it moves the Projectile in the correct direction, locally.
            projectile.eulerAngles = euler;
            projectile.position = muzzleTransform.position + projectile.rotation * Vector3.Scale(mode.ProjectileSpawnOffset, muzzleTransform.lossyScale);
        }

        /// <summary>
        /// Affixes the spawned Projectile to the Weapon Holder's Rigidbody if its Data is set to Fixed.
        /// </summary>
        private void HandleAffix(ProjectileData data, Transform projectile)
        {
            if (data != null && data.Fixed)
            {
                FixedJoint joint = projectile.gameObject.AddComponent<FixedJoint>();
                joint.connectedBody = WeaponHolderRigidbody.Value;
            }
        }

        /// <summary>
        /// Reloads the given Fire Mode of the selected Weapon, or all of them with -1.
        /// </summary>
        public void Reload(int mode = -1)
        {
            var instance = Selected;
            var muzzles = FireModeMuzzles.Value;
            if (muzzles == null || mode >= muzzles.Length)
                return;

            if (mode == -1)
                ReloadAll(instance, muzzles).Forget();
            else
                ReloadMode(instance, muzzles[mode]).Forget();
        }

        private static async UniTaskVoid ReloadAll(BehaviorInstance instance, FireModeMuzzlePair[] muzzles)
        {
            // One after another, as before.
            foreach (var muzzle in muzzles)
            {
                if (!instance.IsSpawned) return;
                await ReloadAsync(instance, muzzle);
            }
        }

        private static async UniTaskVoid ReloadMode(BehaviorInstance instance, FireModeMuzzlePair muzzle) => await ReloadAsync(instance, muzzle);

        private static async UniTask ReloadAsync(BehaviorInstance instance, FireModeMuzzlePair muzzle)
        {
            if (muzzle?.FireMode == null || !muzzle.FireMode.CanReload || muzzle.IsReloading)
                return;

            // Each spawn has its own copy of the muzzles, so they can be changed after the wait without selecting.
            int uid = instance.UID;
            muzzle.IsReloading = true;
            await UniTask.Delay(muzzle.FireMode.ReloadTime);

            if (!muzzle.IsReloading || !instance.IsSpawnedAs(uid))
                return; // Cancelled, or the Weapon was despawned meanwhile.

            muzzle.AmmoCount = muzzle.FireMode.MaxAmmo;
            muzzle.IsReloading = false;
        }

        /// <summary>
        /// Cancels a reload of the selected Weapon.
        /// </summary>
        public void CancelReload(int mode)
        {
            var muzzles = FireModeMuzzles.Value;
            if (muzzles != null && mode >= 0 && mode < muzzles.Length && muzzles[mode] != null)
                muzzles[mode].IsReloading = false;
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            // Handles as cubes, each Fire Mode's muzzles as spheres of its own color.
            Gizmos.color = Color.cyan;
            DrawHandle(context.Get(PrimaryHandle));
            DrawHandle(context.Get(SecondHandle));

            var muzzles = context.Get(FireModeMuzzles);
            if (muzzles == null)
                return;
            for (int i = 0; i < muzzles.Length; i++)
            {
                if (muzzles[i]?.Muzzles == null) continue;
                Gizmos.color = Color.HSVToRGB((i * 0.17f) % 1f, 0.8f, 1f);
                foreach (var muzzle in muzzles[i].Muzzles)
                    if (muzzle != null)
                    {
                        Gizmos.DrawWireSphere(muzzle.position, 0.03f);
                        Gizmos.DrawRay(muzzle.position, muzzle.forward * 0.25f);
                    }
            }

            static void DrawHandle(WeaponHandle handle)
            {
                if (handle?.Transform != null)
                    Gizmos.DrawWireCube(handle.Transform.position, Vector3.one * 0.04f);
            }
        }

        /// <summary>
        /// Weapon Handles are what are used to Translate/Orient Weapons relative to the Weapon Holder's available Sockets.
        /// </summary>
        [Serializable]
        public class WeaponHandle
        {
            public Vector3 Offset;
            [Tooltip("The world location of the Weapon Handle")]
            public Transform Transform;
            [Tooltip("The Socket that the Handle is placed in.")]
            [NonSerialized] public WeaponSocket Socket;
            [Tooltip("The Special ID of the socket for this Handle to be placed at.")]
            public int SpecialID = -1;
        }

        [Serializable]
        public class FireModeMuzzlePair
        {
            // TODO: Make these Customizable Events like the Weapon Input Events on the WeaponHolder.
            // This is vastly more modular. 

            /*        public ScriptableEvent OnReload;
                    public ScriptableEvent OnFire;*/

            public enum MuzzleSelectionStyle
            {
                All,
                Incremental,
                Random
            }
            [Tooltip("The method in which Muzzle Transform(s) is picked for the given relative FireMode.")]
            public MuzzleSelectionStyle MuzzleSelection;

            public bool InfiniteAmmo
            {
                get
                {
                    if (FireMode == null) return true;
                    else return FireMode.InfiniteAmmo;
                }
            }

            //[HideIf("InfiniteAmmo")]
            public int AmmoCount;
            [HideInInspector]
            public bool IsReloading;

            //[Dropdown("GetFiremodes")]
            public FireMode FireMode;
            public List<Transform> Muzzles = new();

            [HideInInspector]
            public WeaponData Data;
            /// <summary>
            /// The Index of the current Muzzle (only used when choosing a Muzzle Incrementally)
            /// </summary>
            [HideInInspector]
            public int MuzzleIndex = 0;

            /*public DropdownList<FireMode> GetFiremodes()
            {
                var list = new DropdownList<FireMode>();

                int i = 0;
                foreach (var fireMode in Data.FireModes)
                {
                    list.Add("Mode_" + i, fireMode);
                    i++;
                }

                return list;
            }*/
        }

        [Serializable]
        public class WeaponInputEvent
        {
            public SignalData OnInput;
            //[InfoBox("A FireMode value of -1 will trigger the event for all FireModes at once.")]
            public int FireMode = -1;
            [Tooltip("The Event fired when the event actually occurs")]
            public SignalData OnTriggered;
        }

        [Serializable]
        public class WeaponEvent
        {
            //[Dropdown("GetWeapons")]
            public WeaponData Weapon;
            public WeaponEventType EventType;
            public SignalData Event;

            public bool IsFireModeEvent => EventType == WeaponEventType.Fire || EventType == WeaponEventType.Reload;

/*            [Dropdown("GetFireModes")]
            [ShowIf("IsFireModeEvent")]*/
            public FireMode FireMode;

            [NonSerialized]
            public GameObject Holder;

            /*public DropdownList<WeaponData> GetWeapons()
            {
                DropdownList<WeaponData> list = new();

                var weapons = Resources.LoadAll<WeaponData>("");

                if (weapons.Length == 0)
                    list.Add("No Weapons", null);
                else
                {
                    foreach (var weapon in weapons)
                    {
                        list.Add(weapon.name, weapon);
                    }
                }

                return list;
            }*/

/*
            public DropdownList<FireMode> GetFireModes()
            {
                DropdownList<FireMode> list = new();

                if (Weapon == null)
                    list.Add("Weapon Not Chosen", null);
                else
                {
                    int i = 0;
                    foreach (var mode in Weapon.FireModes)
                    {
                        list.Add("Mode_" + i, mode);
                        i++;
                    }
                }

                return list;
            }*/
        }


        public enum WeaponEventType
        {
            Fire,
            Reload,
            Equip,
            Unequip
        }

#if UNITY_EDITOR
        [MenuItem("Remedy/Weapons/Create Weapon")]
        public static void CreateWeaponPrefab()
        {
            string path = "Assets/Content/Prefabs/Weapons/New Weapon.prefab";

            // Create a new Schematic Object with a Weapon Behavior
            GameObject weaponObject = new GameObject("NewWeapon");
            var schematic = weaponObject.AddComponent<SchematicObject>();
            schematic.AttachBehavior<Weapon>();

            // Ensure the directory exists
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            // Create the prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(weaponObject, path);

            // Destroy the temporary GameObject in the scene
            GameObject.DestroyImmediate(weaponObject);

            Debug.Log("Weapon prefab created at: " + path, prefab);
        }
#endif
    }
}
