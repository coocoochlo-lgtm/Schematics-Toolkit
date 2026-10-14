using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>How a <see cref="Door"/>'s panel moves.</summary>
    public enum DoorMotion
    {
        /// <summary>Turns around its pivot's up axis by Open Angle (put the pivot on the hinge).</summary>
        Swing,
        /// <summary>Slides by Slide Offset (in the pivot's parent space).</summary>
        Slide,
    }

    /// <summary>
    /// A door that swings or slides open: Open, Close, Toggle and Use (which checks the lock and the key) from graphs
    /// or other Behaviors, and optionally opens by itself when someone with the right tag comes close.
    /// </summary>
    /// <remarks>
    /// The panel should be a child (its pivot on the hinge for swinging), ideally with a kinematic Box3D body so it
    /// pushes things as it moves: kinematic bodies follow their Transform, so animating the Transform is all it takes.
    /// <c>IsOpen</c> and <c>IsLocked</c> are synced: the authority decides, every peer animates.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Door"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Door"", ""description"": ""A door that swings or slides open. Open, Close, Toggle and Use (checks the lock and key) Signals, optional auto-open when a tagged object comes close, and an optional required key."", ""variables"": [""Panel"", ""IsOpen"", ""IsLocked"", ""RequiredKey"", ""ConsumeKey"", ""Motion"", ""OpenAngle"", ""SlideOffset"", ""OpenTime"", ""AutoOpenRadius"", ""AutoOpenTag"", ""AutoCloseDelay""], ""signals"": [""Open"", ""Close"", ""Toggle"", ""Use"", ""Lock"", ""Unlock"", ""OnOpened"", ""OnClosed"", ""OnLockedAttempt""], ""use_for"": [""doors and gates"", ""locked doors that need a key"", ""automatic sliding doors""], ""notes"": ""IsOpen and IsLocked are network synced; the authority decides and every peer animates.""}")]
    [SchematicBehaviorPath("Level Kit/Door")]
    public class Door : SchematicBehavior
    {
        #region Variables

        [SchematicDoc(@"{""name"": ""Panel"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""The moving part (its pivot on the hinge for swinging doors). Empty: the object itself.""}")]
        [Tooltip("The moving part (its pivot on the hinge for swinging doors). Empty: the object itself.")]
        public readonly VariableHandle<Transform> Panel = (Transform)null;

        [SchematicDoc(@"{""name"": ""IsOpen"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether the door is (or is moving to) open. Set it to start open. Network synced.""}")]
        [Tooltip("Whether the door is (or is moving to) open. Set it to start open.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsOpen = false;

        [SchematicDoc(@"{""name"": ""IsLocked"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""A locked door ignores Open, Toggle and auto-open; Use opens it only with the Required Key. Network synced.""}")]
        [Tooltip("A locked door ignores Open, Toggle and auto-open; Use opens it only with the Required Key.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsLocked = false;

        [SchematicDoc(@"{""name"": ""RequiredKey"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""string"", ""default"": """", ""description"": ""The key id (as given by a Key Item) that unlocks this door through Use or auto-open. Empty: no key unlocks it, only the Unlock Signal.""}")]
        [Tooltip("The key id (as given by a Key Item) that unlocks this door through Use or auto-open. Empty: only the Unlock Signal unlocks it.")]
        public readonly VariableHandle<string> RequiredKey = "";

        [SchematicDoc(@"{""name"": ""ConsumeKey"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Take the key away when it unlocks the door.""}")]
        [Tooltip("Take the key away when it unlocks the door.")]
        public readonly VariableHandle<bool> ConsumeKey = false;

        [SchematicDoc(@"{""name"": ""Motion"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""DoorMotion"", ""default"": ""Swing"", ""description"": ""Swing around the panel's up axis, or Slide by Slide Offset.""}")]
        [Tooltip("Swing around the panel's up axis, or Slide by Slide Offset.")]
        public readonly VariableHandle<DoorMotion> Motion = DoorMotion.Swing;

        [SchematicDoc(@"{""name"": ""OpenAngle"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""float"", ""default"": ""95"", ""description"": ""Degrees a swinging door turns (negative swings the other way).""}")]
        [Tooltip("Degrees a swinging door turns (negative swings the other way).")]
        public readonly VariableHandle<float> OpenAngle = 95f;

        [SchematicDoc(@"{""name"": ""SlideOffset"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""Vector3"", ""default"": ""(1.5, 0, 0)"", ""description"": ""How far a sliding door moves, in the panel's parent space.""}")]
        [Tooltip("How far a sliding door moves, in the panel's parent space.")]
        public readonly VariableHandle<Vector3> SlideOffset = new Vector3(1.5f, 0f, 0f);

        [SchematicDoc(@"{""name"": ""OpenTime"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""float"", ""default"": ""0.6"", ""description"": ""Seconds to fully open or close. 0 snaps.""}")]
        [Tooltip("Seconds to fully open or close. 0 snaps.")]
        public readonly VariableHandle<float> OpenTime = 0.6f;

        [SchematicDoc(@"{""name"": ""AutoOpenRadius"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Opens by itself while an object with Auto Open Tag is this close (metres). 0: off.""}")]
        [Tooltip("Opens by itself while an object with Auto Open Tag is this close (metres). 0: off.")]
        public readonly VariableHandle<float> AutoOpenRadius = 0f;

        [SchematicDoc(@"{""name"": ""AutoOpenTag"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Who opens an auto-opening door. Untagged: any physics body.""}")]
        [Tooltip("Who opens an auto-opening door. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> AutoOpenTag = new IntTag(IntTags.Player);

        [SchematicDoc(@"{""name"": ""AutoCloseDelay"", ""kind"": ""variable"", ""behavior"": ""Door"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Closes this many seconds after opening (once nobody is in the auto-open radius). 0: stays open.""}")]
        [Tooltip("Closes this many seconds after opening (once nobody is in the auto-open radius). 0: stays open.")]
        public readonly VariableHandle<float> AutoCloseDelay = 0f;

        // Local state: how open the panel is (0..1) and the auto-close countdown. Not synced: each peer animates.
        private readonly VariableHandle<float> _progress = 0f;
        private readonly VariableHandle<float> _closeTimer = 0f;

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""Open"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""input"", ""description"": ""Opens the door unless it's locked."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock), e.g. from a Lever's On Turned On.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Open;

        [SchematicDoc(@"{""name"": ""Close"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""input"", ""description"": ""Closes the door."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Close;

        [SchematicDoc(@"{""name"": ""Toggle"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""input"", ""description"": ""Opens a closed door (unless locked) or closes an open one."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock).""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Toggle;

        [SchematicDoc(@"{""name"": ""Use"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""input"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Someone tries the door: toggles it, or if locked, unlocks and opens it when they carry the Required Key (otherwise sends On Locked Attempt)."", ""how"": ""Send it with a Send a Signal node when the player interacts with the door.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Use;

        [SchematicDoc(@"{""name"": ""Lock"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""input"", ""description"": ""Locks the door (it stays as it is: close it first if you want it shut)."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Lock;

        [SchematicDoc(@"{""name"": ""Unlock"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""input"", ""description"": ""Unlocks the door without a key (e.g. from a Key Lock's On Unlocked or a puzzle)."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Unlock;

        [SchematicDoc(@"{""name"": ""OnOpened"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""output"", ""description"": ""Sent when the door starts opening."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnOpened;

        [SchematicDoc(@"{""name"": ""OnClosed"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""output"", ""description"": ""Sent when the door starts closing."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnClosed;

        [SchematicDoc(@"{""name"": ""OnLockedAttempt"", ""kind"": ""signal"", ""behavior"": ""Door"", ""direction"": ""output"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Sent when someone tries the locked door without its key (play a rattle, show a hint)."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [OutputSignal, NetworkSync]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnLockedAttempt;

        #endregion

        // Where each door's panel sits when closed (captured when it spawns).
        private static readonly Dictionary<BehaviorInstance, Pose> _closedPose = new();
        private readonly List<GameObject> _nearby = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _closedPose.Clear();

        protected override void OnStart()
        {
            var instance = Self;
            var panel = PanelOf(instance);
            _closedPose[instance] = new Pose(panel.localPosition, panel.localRotation);
            _progress.Value = IsOpen.Value ? 1f : 0f;
            Apply(instance);

            Open.Subscribe(() => SetOpen(instance, true, null));
            Close.Subscribe(() => SetOpen(instance, false, null));
            Toggle.Subscribe(() => SetOpen(instance, !IsOpen.Value, null));
            Use.Subscribe(user => TryUse(instance, user));
            Lock.Subscribe(() => { if (LevelKitQuery.HasAuthority(instance)) IsLocked.Value = true; });
            Unlock.Subscribe(() => { if (LevelKitQuery.HasAuthority(instance)) IsLocked.Value = false; });
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!LevelKitQuery.HasAuthority(instance))
                return;

            float radius = AutoOpenRadius.Value;
            bool someoneNear = false;
            if (radius > 0f)
            {
                LevelKitQuery.AgentsInSphere(instance.Transform.position, radius, -1, AutoOpenTag.Value, _nearby, instance.Transform);
                foreach (var agent in _nearby)
                {
                    someoneNear = true;
                    if (!IsOpen.Value)
                    {
                        if (IsLocked.Value && !TryUnlockWithKey(instance, agent))
                            continue;
                        SetOpen(instance, true, agent);
                    }
                    break;
                }
            }

            // Auto close: counts down once nobody keeps it open.
            if (IsOpen.Value && AutoCloseDelay.Value > 0f)
            {
                if (someoneNear) _closeTimer.Value = AutoCloseDelay.Value;
                else if ((_closeTimer.Value -= Time.fixedDeltaTime) <= 0f) SetOpen(instance, false, null);
            }
        }

        protected override void OnUpdate()
        {
            var instance = Self;
            float target = IsOpen.Value ? 1f : 0f;
            float time = OpenTime.Value;
            float progress = _progress.Value;
            if (Mathf.Approximately(progress, target))
                return;
            _progress.Value = time <= 0f ? target : Mathf.MoveTowards(progress, target, Time.deltaTime / time);
            Apply(instance);
        }

        protected override void OnDisable()
        {
            // Back to closed, so a pooled door respawns closed.
            var instance = Self;
            if (_closedPose.TryGetValue(instance, out var pose))
            {
                var panel = PanelOf(instance);
                panel.localPosition = pose.position;
                panel.localRotation = pose.rotation;
            }
        }

        protected override void OnDestroy() => _closedPose.Remove(Self);

        /// <summary>Opens or closes the selected door from C# (the authority's call counts).</summary>
        public void SetOpen(bool open) => SetOpen(Selected, open, null);

        private void SetOpen(BehaviorInstance instance, bool open, GameObject user)
        {
            if (instance == null || !LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (open && IsLocked.Value)
                {
                    if (user != null) OnLockedAttempt.Invoke(user);
                    return;
                }
                if (IsOpen.Value == open)
                    return;
                IsOpen.Value = open;
                _closeTimer.Value = AutoCloseDelay.Value;
                if (open) OnOpened.Invoke();
                else OnClosed.Invoke();
            }
        }

        private void TryUse(BehaviorInstance instance, GameObject user)
        {
            if (!LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (IsLocked.Value && !TryUnlockWithKey(instance, user))
                {
                    OnLockedAttempt.Invoke(user);
                    return;
                }
                SetOpen(instance, !IsOpen.Value, user);
            }
        }

        private bool TryUnlockWithKey(BehaviorInstance instance, GameObject user)
        {
            using (Pick(instance))
            {
                string key = RequiredKey.Value;
                if (string.IsNullOrEmpty(key) || user == null || !KeyRing.Has(user, key))
                    return false;
                if (ConsumeKey.Value)
                    KeyRing.Take(user, key);
                IsLocked.Value = false;
                return true;
            }
        }

        private Transform PanelOf(BehaviorInstance instance)
        {
            var panel = Panel[instance];
            return panel != null ? panel : instance.Transform;
        }

        private void Apply(BehaviorInstance instance)
        {
            if (!_closedPose.TryGetValue(instance, out var closed))
                return;
            var panel = PanelOf(instance);
            float t = Mathf.SmoothStep(0f, 1f, _progress[instance]);
            if (Motion[instance] == DoorMotion.Swing)
            {
                panel.localPosition = closed.position;
                panel.localRotation = closed.rotation * Quaternion.Euler(0f, OpenAngle[instance] * t, 0f);
            }
            else
            {
                panel.localRotation = closed.rotation;
                panel.localPosition = closed.position + SlideOffset[instance] * t;
            }
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            float radius = context.Get(AutoOpenRadius);
            if (radius > 0f)
            {
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
                Gizmos.DrawWireSphere(context.Transform.position, radius);
            }

            var panel = context.Get(Panel);
            if (panel == null) panel = context.Transform;
            Gizmos.color = context.Get(IsLocked) ? new Color(1f, 0.3f, 0.2f, 0.9f) : new Color(0.4f, 1f, 0.4f, 0.9f);
            if (context.Get(Motion) == DoorMotion.Slide && panel.parent != null)
            {
                var offset = panel.parent.TransformVector(context.Get(SlideOffset));
                Gizmos.DrawLine(panel.position, panel.position + offset);
            }
            else
            {
                var open = Quaternion.AngleAxis(context.Get(OpenAngle), panel.up) * panel.right;
                Gizmos.DrawLine(panel.position + Vector3.up * 0.05f, panel.position + Vector3.up * 0.05f + open);
            }
        }
    }
}
