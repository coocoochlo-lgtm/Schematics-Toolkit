/*using SaintsField;
using SaintsField.Playa;*/
using System;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Weapons
{
    /// <summary>
    /// The WeaponHolder should be attached to a GameObject which is the child of a Character, Vehicle, or whatever, seperate from motion behaviour.
    /// It has a unique Collider (Trigger) that is used for interactions with Weapons that can be picked up
    /// </summary>
    [SchematicBehaviorPath("Weapons/Holder")]
    [RequireComponent(typeof(SphereCollider))]
    public class WeaponHolder : SchematicBehavior
    {
        [Tooltip("Device inputs that are passed to Equipped Weapons")]
        public readonly VariableHandle<WeaponInputEvent[]> WeaponInputs = (WeaponInputEvent[])null;

        [Tooltip("Equips the Weapon with the given ID (its ID within the WeaponData Lookup).")]
        [InputSignal, Parameter("Weapon ID", typeof(int))]
        public readonly SignalHandle<int> EquipInput;
        [Tooltip("The current Aim Forward, updated from the Camera Controller.")]
        [InputSignal, Parameter("Aim", typeof(Vector3))]
        public readonly SignalHandle<Vector3> AimInput;

        public readonly VariableHandle<AimBone[]> AimBones = (AimBone[])null;

        // The way a Weapon fills sockets is based on the number of (and SocketIndex of) WeaponHandles that are applied to the Weapon Prefab.
        // If there are two WeaponHandles, the weapon will need the two specified WeaponSockets open on the WeaponHolder for it to be equipped.
        [Tooltip("Weapon Sockets are parent Transforms for Weapons that are added to this Weapon Holder and determine the orientation of them")]
        public readonly VariableHandle<WeaponSocket[]> WeaponSockets = (WeaponSocket[])null;

        [Tooltip("The weapon we're currently using.")]
        public readonly VariableHandle<int> ActiveWeaponSlot = 0;

        private readonly VariableHandle<Vector3> _aimInput = Vector3.zero;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            // Start with one socket, on a child of its own.
            var socket = new GameObject("WeaponSocket_0").transform;
            socket.SetParent(context.Transform, false);
            context.Set(WeaponSockets, new[] { new WeaponSocket(socket) { Name = "Primary" } });
        }

        protected override void OnCreate()
        {
            var collider = Self.GetComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.includeLayers = 1 << GlobalWeaponData.Instance.WeaponCollisionLayer;
        }

        protected override void OnStart()
        {
            var instance = Self;
            EquipInput.Subscribe(weaponID => Equip(instance, weaponID));
            AimInput.Subscribe(aim => _aimInput[instance] = aim);

            var inputs = WeaponInputs.Value;
            if (inputs == null)
                return;

            foreach (var weaponInput in inputs)
            {
                if (weaponInput?.InputEvent == null) continue;

                weaponInput.InputEvent.Subscribe(instance, () =>
                {
                    if (weaponInput.Event == WeaponInputEvent.WeaponEventType.Fire)
                        Fire(instance, weaponInput.Socket, weaponInput.Mode);
                    else
                        Reload(instance, weaponInput.Socket, weaponInput.Mode);
                });
            }
        }

        protected override void OnUpdate()
        {
            var sockets = WeaponSockets.Value;
            if (sockets == null)
                return;

            var aim = _aimInput.Value;
            foreach (var socket in sockets)
            {
                var weapon = socket != null ? WeaponIn(socket) : null;
                if (weapon != null)
                    weapon.AttackDirection.Value = aim;
            }
        }

        protected override void OnDisable()
        {
            var sockets = WeaponSockets.Value;
            if (sockets == null)
                return;
            for (int i = 0; i < sockets.Length; i++)
                UnEquip(Self, i);
        }

        /// <summary>
        /// The Weapon in a Socket, with it selected. Null if the Socket is empty.
        /// </summary>
        public static Weapon WeaponIn(WeaponSocket socket)
        {
            var weapon = socket?.ActiveWeapon;
            return weapon != null && weapon.IsSpawned ? weapon.GetSchematicBehavior<Weapon>() : null;
        }

        /// <summary>
        /// Equips the Weapon with the given WeaponID (the ID within the WeaponData.Lookup)
        /// </summary>
        /// <param name="socket">The socket to place the Weapon in. If a weapon is in said socket, it will be replaced.</param>
        public void Equip(BehaviorInstance instance, int weaponID, int socket = 0)
        {
            var sockets = WeaponSockets[instance];
            if (sockets == null || socket < 0 || socket >= sockets.Length)
                return;

            var weaponData = WeaponData.Lookup[weaponID];
            if (weaponData == null || weaponData.Prefab == null)
                return;

            UnEquip(instance, socket);

            var spawned = SchematicsManager.Spawn(weaponData.Prefab);
            var weapon = spawned.GetSchematicBehavior<Weapon>();
            if (weapon == null || !weapon.Equip(instance.GameObject, sockets, socket))
            {
                Debug.LogWarning($"{weaponData.name} couldn't be equipped in socket {socket} of {instance.GameObject.name}.", instance.GameObject);
                SchematicsManager.Destroy(spawned);
            }
        }

        public void Equip(int weaponID, int socket = 0) => Equip(Selected, weaponID, socket);

        /// <summary>
        /// Drops the Weapon in the given Socket and returns it to its pool.
        /// </summary>
        public void UnEquip(BehaviorInstance instance, int socket = 0)
        {
            var sockets = WeaponSockets[instance];
            if (sockets == null || socket < 0 || socket >= sockets.Length)
                return;

            var weapon = WeaponIn(sockets[socket]);
            if (weapon == null)
                return;

            var owner = weapon.Selected.Owner;
            weapon.Drop();
            SchematicsManager.Destroy(owner);
        }

        public void UnEquip(int socket = 0) => UnEquip(Selected, socket);

        /// <summary>
        /// Shoot the given Weapon using the given Mode.
        /// </summary>
        public void Fire(BehaviorInstance instance, int socket = 0, int mode = 0)
        {
            var sockets = WeaponSockets[instance];
            if (sockets != null && socket >= 0 && socket < sockets.Length)
                WeaponIn(sockets[socket])?.Fire(mode, true);
        }

        public void Fire(int socket = 0, int mode = 0) => Fire(Selected, socket, mode);

        /// <summary>
        /// Reload the given Weapon for the given Mode.
        /// </summary>
        public void Reload(BehaviorInstance instance, int socket = 0, int mode = 0)
        {
            var sockets = WeaponSockets[instance];
            if (sockets != null && socket >= 0 && socket < sockets.Length)
                WeaponIn(sockets[socket])?.Reload(mode);
        }

        public void Reload(int socket = 0, int mode = 0) => Reload(Selected, socket, mode);

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var sockets = context.Get(WeaponSockets);
            if (sockets == null)
                return;

            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            foreach (var socket in sockets)
            {
                if (socket?.Transform == null) continue;
                var position = socket.Transform.position + socket.Transform.rotation * socket.PositionOffset;
                Gizmos.DrawWireSphere(position, 0.05f);
                Gizmos.DrawRay(position, socket.Transform.rotation * Quaternion.Euler(socket.AngularOffset) * Vector3.forward * 0.2f);
            }
        }
    }

    /// <summary>
    /// The Definition of a Weapon's Input Event. Use this to create special Inputs to pass to Equippable Weapons,
    /// allowing them to perform special functionality.
    /// </summary>
    [Serializable]
    public class WeaponInputEvent
    {
        [Tooltip("The Socket for which this functionality should occur.")]
        public int Socket;
        [Tooltip("The Mode for the Weapon that this is performed on, if the Weapon has different modes of Fire.")]
        public int Mode;
        public enum WeaponEventType
        {
            Fire,
            Reload
        }
        [Tooltip("The Unique Functionality that the Weapon should perform")]
        public WeaponEventType Event;

        [Tooltip("The Signal, sent to this object, that triggers this Weapon Event")]
        public SignalData InputEvent;
    }

    /// <summary>
    /// Weapon Sockets are used for translating and orienting Weapons relative to the Weapon Holder.
    /// For a character, Socket Transforms would likely be parented to hand bones, so the character visually holds the Weapon.
    /// Orientation of the Weapon is handled per-Weapon in the Weapon Script.
    /// </summary>
    [Serializable]
    public class WeaponSocket
    {
        [Tooltip("The Name of the Slot, which can be used to Query the Transform of the WeaponSlot within the WeaponHolder.")]
        public string Name = "";
        [Tooltip("The object's existence in the world.")]
        public Transform Transform;
        [Tooltip("Optional ID for the Slot (for special cases of weapon Transformation).")]
        public int SpecialID = -1;
        [Tooltip("The Weapon in this Socket.")]
        [NonSerialized] public BehaviorInstance ActiveWeapon;
        public Vector3 PositionOffset;
        [Tooltip("The Offset at for the held Weapon rotationally from the bone socket's rotation.")]
        public Vector3 AngularOffset;

        public WeaponSocket() { }

        public WeaponSocket(Transform transform)
        {
            Transform = transform;
        }
    }

    [Serializable]
    public class AimBone
    {
        public Transform Limb;
        public Vector3 EulerOffset = Vector3.zero;
        public float RedirectSpeed = 5f;
    }
}
