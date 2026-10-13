using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>Whether a <see cref="Switch"/> stays where it's put or springs back.</summary>
    public enum SwitchMode
    {
        /// <summary>Each activation flips it on or off (a lever).</summary>
        Toggle,
        /// <summary>Activation turns it on, and it turns itself off after Momentary Time (a button).</summary>
        Momentary,
    }

    /// <summary>How a <see cref="Switch"/>'s handle shows its state.</summary>
    public enum SwitchHandleMotion
    {
        /// <summary>Tilts around its local X axis, from -Handle Angle (off) to +Handle Angle (on): a lever.</summary>
        Rotate,
        /// <summary>Sinks along its local down axis by Press Depth while on: a button.</summary>
        Press,
    }

    /// <summary>
    /// A lever or a button: Activate it (from a graph, an interaction, or by touching it) and it reports On Activated,
    /// On Turned On and On Turned Off. Wire those to Doors, Platforms, Spawners or your own graph.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Switch"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Switch (Lever or Button)"", ""description"": ""A lever (Toggle) or a button (Momentary). Activate it with a Signal, or by touching it when Touch Radius is set; it reports On Activated, On Turned On and On Turned Off. IsOn is network synced."", ""variables"": [""IsOn"", ""Mode"", ""MomentaryTime"", ""Handle"", ""HandleMotion"", ""HandleAngle"", ""PressDepth"", ""TouchRadius"", ""UserTag"", ""Cooldown""], ""signals"": [""Activate"", ""SetOn"", ""OnActivated"", ""OnTurnedOn"", ""OnTurnedOff""], ""use_for"": [""levers that open doors"", ""buttons that start platforms or spawners"", ""puzzle switches""]}")]
    [SchematicBehaviorPath("Level Kit/Switch (Lever or Button)")]
    public class Switch : SchematicBehavior
    {
        #region Variables

        [SchematicDoc(@"{""name"": ""IsOn"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether the switch is on. Set it to start on. Network synced.""}")]
        [Tooltip("Whether the switch is on. Set it to start on.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsOn = false;

        [SchematicDoc(@"{""name"": ""Mode"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""SwitchMode"", ""default"": ""Toggle"", ""description"": ""Toggle: each activation flips it (lever). Momentary: on, then off again after Momentary Time (button).""}")]
        [Tooltip("Toggle: each activation flips it (lever). Momentary: on, then off again after Momentary Time (button).")]
        public readonly VariableHandle<SwitchMode> Mode = SwitchMode.Toggle;

        [SchematicDoc(@"{""name"": ""MomentaryTime"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""float"", ""default"": ""1"", ""description"": ""Seconds a Momentary switch stays on.""}")]
        [Tooltip("Seconds a Momentary switch stays on.")]
        public readonly VariableHandle<float> MomentaryTime = 1f;

        [SchematicDoc(@"{""name"": ""Handle"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""The part that moves (lever stick pivot or button cap). Empty: nothing moves.""}")]
        [Tooltip("The part that moves (lever stick pivot or button cap). Empty: nothing moves.")]
        public readonly VariableHandle<Transform> Handle = (Transform)null;

        [SchematicDoc(@"{""name"": ""HandleMotion"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""SwitchHandleMotion"", ""default"": ""Rotate"", ""description"": ""Rotate (lever: tilts around local X) or Press (button: sinks by Press Depth).""}")]
        [Tooltip("Rotate (lever: tilts around local X) or Press (button: sinks by Press Depth).")]
        public readonly VariableHandle<SwitchHandleMotion> HandleMotion = SwitchHandleMotion.Rotate;

        [SchematicDoc(@"{""name"": ""HandleAngle"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""float"", ""default"": ""35"", ""description"": ""How far a lever tilts each way from upright (degrees).""}")]
        [Tooltip("How far a lever tilts each way from upright (degrees).")]
        public readonly VariableHandle<float> HandleAngle = 35f;

        [SchematicDoc(@"{""name"": ""PressDepth"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""float"", ""default"": ""0.05"", ""description"": ""How far a button sinks while on (metres).""}")]
        [Tooltip("How far a button sinks while on (metres).")]
        public readonly VariableHandle<float> PressDepth = 0.05f;

        [SchematicDoc(@"{""name"": ""TouchRadius"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""float"", ""default"": ""0"", ""description"": ""Activates when an object with User Tag comes this close (metres). 0: only the Activate Signal.""}")]
        [Tooltip("Activates when an object with User Tag comes this close (metres). 0: only the Activate Signal.")]
        public readonly VariableHandle<float> TouchRadius = 0f;

        [SchematicDoc(@"{""name"": ""UserTag"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Who can activate it by touch. Untagged: any physics body.""}")]
        [Tooltip("Who can activate it by touch. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> UserTag = new IntTag(IntTags.Player);

        [SchematicDoc(@"{""name"": ""Cooldown"", ""kind"": ""variable"", ""behavior"": ""Switch"", ""type"": ""float"", ""default"": ""0.3"", ""description"": ""Seconds before it can be activated again.""}")]
        [Tooltip("Seconds before it can be activated again.")]
        public readonly VariableHandle<float> Cooldown = 0.3f;

        // Local: handle animation (0 off .. 1 on), timers, and whether someone was touching it last step.
        private readonly VariableHandle<float> _visual = 0f;
        private readonly VariableHandle<float> _offAt = 0f;
        private readonly VariableHandle<float> _readyAt = 0f;
        private readonly VariableHandle<bool> _touched = false;

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""Activate"", ""kind"": ""signal"", ""behavior"": ""Switch"", ""direction"": ""input"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Pulls the lever / presses the button (User may be null)."", ""how"": ""Send it with a Send a Signal node when the player interacts with it.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> Activate;

        [SchematicDoc(@"{""name"": ""SetOn"", ""kind"": ""signal"", ""behavior"": ""Switch"", ""direction"": ""input"", ""params"": [{""name"": ""On"", ""type"": ""bool""}], ""description"": ""Sets the switch on or off directly (e.g. to reset a puzzle)."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("On", typeof(bool))]
        public readonly SignalHandle<bool> SetOn;

        [SchematicDoc(@"{""name"": ""OnActivated"", ""kind"": ""signal"", ""behavior"": ""Switch"", ""direction"": ""output"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Sent every time it's activated, with who did it."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [OutputSignal, NetworkSync]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnActivated;

        [SchematicDoc(@"{""name"": ""OnTurnedOn"", ""kind"": ""signal"", ""behavior"": ""Switch"", ""direction"": ""output"", ""description"": ""Sent when it turns on."", ""how"": ""React to it with On Signal Received, e.g. send a Door's Open.""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnTurnedOn;

        [SchematicDoc(@"{""name"": ""OnTurnedOff"", ""kind"": ""signal"", ""behavior"": ""Switch"", ""direction"": ""output"", ""description"": ""Sent when it turns off."", ""how"": ""React to it with On Signal Received, e.g. send a Door's Close.""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnTurnedOff;

        #endregion

        private static readonly Dictionary<BehaviorInstance, Pose> _restPose = new();
        private readonly List<GameObject> _nearby = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _restPose.Clear();

        protected override void OnStart()
        {
            var instance = Self;
            var handle = Handle.Value;
            if (handle != null)
                _restPose[instance] = new Pose(handle.localPosition, handle.localRotation);
            _visual.Value = IsOn.Value ? 1f : 0f;
            Apply(instance);

            Activate.Subscribe(user => DoActivate(instance, user));
            SetOn.Subscribe(on => Set(instance, on));
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!LevelKitQuery.HasAuthority(instance))
                return;

            if (Mode.Value == SwitchMode.Momentary && IsOn.Value && Time.time >= _offAt.Value)
                Set(instance, false);

            float radius = TouchRadius.Value;
            if (radius > 0f)
            {
                int count = LevelKitQuery.AgentsInSphere(instance.Transform.position, radius, -1, UserTag.Value, _nearby, instance.Transform);
                bool touched = count > 0;
                // Only arriving counts, so standing on a button doesn't machine-gun it.
                if (touched && !_touched.Value)
                    DoActivate(instance, _nearby[0]);
                _touched.Value = touched;
            }
        }

        protected override void OnUpdate()
        {
            var instance = Self;
            float target = IsOn.Value ? 1f : 0f;
            if (Mathf.Approximately(_visual.Value, target))
                return;
            _visual.Value = Mathf.MoveTowards(_visual.Value, target, Time.deltaTime * 8f);
            Apply(instance);
        }

        protected override void OnDestroy() => _restPose.Remove(Self);

        /// <summary>Activates the selected switch from C# (the authority's call counts).</summary>
        public void ActivateFrom(GameObject user) => DoActivate(Selected, user);

        private void DoActivate(BehaviorInstance instance, GameObject user)
        {
            if (instance == null || !LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (Time.time < _readyAt.Value)
                    return;
                _readyAt.Value = Time.time + Cooldown.Value;
                OnActivated.Invoke(user);
                if (Mode.Value == SwitchMode.Momentary)
                {
                    _offAt.Value = Time.time + MomentaryTime.Value;
                    Set(instance, true);
                }
                else
                    Set(instance, !IsOn.Value);
            }
        }

        private void Set(BehaviorInstance instance, bool on)
        {
            if (!LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (IsOn.Value == on)
                    return;
                IsOn.Value = on;
                if (on) OnTurnedOn.Invoke();
                else OnTurnedOff.Invoke();
            }
        }

        private void Apply(BehaviorInstance instance)
        {
            var handle = Handle[instance];
            if (handle == null || !_restPose.TryGetValue(instance, out var rest))
                return;
            float t = _visual[instance];
            if (HandleMotion[instance] == SwitchHandleMotion.Rotate)
            {
                float angle = Mathf.Lerp(-HandleAngle[instance], HandleAngle[instance], Mathf.SmoothStep(0f, 1f, t));
                handle.localRotation = rest.rotation * Quaternion.Euler(angle, 0f, 0f);
            }
            else
                handle.localPosition = rest.position - rest.rotation * Vector3.up * (PressDepth[instance] * t);
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            float radius = context.Get(TouchRadius);
            if (radius <= 0f) return;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(context.Transform.position, radius);
        }
    }
}
