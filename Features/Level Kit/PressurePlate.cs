using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// A plate that's pressed while enough things stand on it: players, crates, anything with a Box3D body (optionally
    /// only objects with a tag). Sends On Pressed and On Released.
    /// </summary>
    /// <remarks>
    /// Detection is a Box3D overlap query of its zone (an oriented box in the object's local space) each physics step,
    /// on the authority. Unity trigger colliders aren't used because the toolkit simulates with Box3D, not Unity
    /// physics (Trigger Volume still relies on Unity triggers, which only fire with Unity colliders).
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Pressure Plate"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Pressure Plate"", ""description"": ""Pressed while enough physics bodies (optionally only ones with a tag) are in its zone; sends On Pressed and On Released. Checks with Box3D overlap queries. IsPressed and Occupants are network synced."", ""variables"": [""Plate"", ""ZoneCenter"", ""ZoneSize"", ""RequiredTag"", ""Layers"", ""RequiredCount"", ""ReleaseDelay"", ""PressDepth"", ""IsPressed"", ""Occupants""], ""signals"": [""OnPressed"", ""OnReleased"", ""OnOccupantsChanged""], ""use_for"": [""crate-on-plate puzzles"", ""floor switches"", ""traps that trigger when stepped on""]}")]
    [SchematicBehaviorPath("Level Kit/Pressure Plate")]
    public class PressurePlate : SchematicBehavior
    {
        #region Variables

        [SchematicDoc(@"{""name"": ""Plate"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""The part that sinks while pressed. Empty: nothing moves.""}")]
        [Tooltip("The part that sinks while pressed. Empty: nothing moves.")]
        public readonly VariableHandle<Transform> Plate = (Transform)null;

        [SchematicDoc(@"{""name"": ""ZoneCenter"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""Vector3"", ""default"": ""(0, 0.4, 0)"", ""description"": ""Centre of the detection box, in the object's local space.""}")]
        [Tooltip("Centre of the detection box, in the object's local space.")]
        public readonly VariableHandle<Vector3> ZoneCenter = new Vector3(0f, 0.4f, 0f);

        [SchematicDoc(@"{""name"": ""ZoneSize"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""Vector3"", ""default"": ""(1.4, 0.6, 1.4)"", ""description"": ""Size of the detection box (local).""}")]
        [Tooltip("Size of the detection box (local).")]
        public readonly VariableHandle<Vector3> ZoneSize = new Vector3(1.4f, 0.6f, 1.4f);

        [SchematicDoc(@"{""name"": ""RequiredTag"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""IntTag"", ""default"": ""Untagged"", ""description"": ""Only objects with this Int Tag press it. Untagged: any physics body.""}")]
        [Tooltip("Only objects with this Int Tag press it. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> RequiredTag = new IntTag(IntTags.Untagged);

        [SchematicDoc(@"{""name"": ""Layers"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""LayerMask"", ""default"": ""Everything"", ""description"": ""Physics layers that can press it.""}")]
        [Tooltip("Physics layers that can press it.")]
        public readonly VariableHandle<LayerMask> Layers = (LayerMask)(-1);

        [SchematicDoc(@"{""name"": ""RequiredCount"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""int"", ""default"": ""1"", ""description"": ""How many objects must be on it at once.""}")]
        [Tooltip("How many objects must be on it at once.")]
        public readonly VariableHandle<int> RequiredCount = 1;

        [SchematicDoc(@"{""name"": ""ReleaseDelay"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""float"", ""default"": ""0.15"", ""description"": ""Seconds it stays pressed after the last object leaves (stops flicker from bouncing objects).""}")]
        [Tooltip("Seconds it stays pressed after the last object leaves (stops flicker from bouncing objects).")]
        public readonly VariableHandle<float> ReleaseDelay = 0.15f;

        [SchematicDoc(@"{""name"": ""PressDepth"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""float"", ""default"": ""0.06"", ""description"": ""How far the plate sinks while pressed (metres).""}")]
        [Tooltip("How far the plate sinks while pressed (metres).")]
        public readonly VariableHandle<float> PressDepth = 0.06f;

        [SchematicDoc(@"{""name"": ""IsPressed"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether it's pressed right now. Read-only. Network synced.""}")]
        [Tooltip("Whether it's pressed right now. Read-only.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsPressed = false;

        [SchematicDoc(@"{""name"": ""Occupants"", ""kind"": ""variable"", ""behavior"": ""Pressure Plate"", ""type"": ""int"", ""default"": ""0"", ""description"": ""How many matching objects are on it. Read-only. Network synced.""}")]
        [Tooltip("How many matching objects are on it. Read-only.")]
        [NetworkSync]
        public readonly VariableHandle<int> Occupants = 0;

        private readonly VariableHandle<float> _releaseAt = 0f;
        private readonly VariableHandle<float> _visual = 0f;

        #endregion

        #region Signals

        [SchematicDoc(@"{""name"": ""OnPressed"", ""kind"": ""signal"", ""behavior"": ""Pressure Plate"", ""direction"": ""output"", ""description"": ""Sent when it becomes pressed."", ""how"": ""React to it with On Signal Received, e.g. open a Door.""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnPressed;

        [SchematicDoc(@"{""name"": ""OnReleased"", ""kind"": ""signal"", ""behavior"": ""Pressure Plate"", ""direction"": ""output"", ""description"": ""Sent when it stops being pressed."", ""how"": ""React to it with On Signal Received, e.g. close a Door.""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnReleased;

        [SchematicDoc(@"{""name"": ""OnOccupantsChanged"", ""kind"": ""signal"", ""behavior"": ""Pressure Plate"", ""direction"": ""output"", ""params"": [{""name"": ""Count"", ""type"": ""int""}], ""description"": ""Sent when the number of objects on it changes."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock).""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Count", typeof(int))]
        public readonly SignalHandle<int> OnOccupantsChanged;

        #endregion

        private static readonly Dictionary<BehaviorInstance, Vector3> _plateRest = new();
        private readonly List<GameObject> _agents = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _plateRest.Clear();

        protected override void OnStart()
        {
            var plate = Plate.Value;
            if (plate != null)
                _plateRest[Self] = plate.localPosition;
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (!LevelKitQuery.HasAuthority(instance))
                return;

            var transform = instance.Transform;
            var center = transform.TransformPoint(ZoneCenter.Value);
            var half = Vector3.Scale(ZoneSize.Value, transform.lossyScale) * 0.5f;
            int count = LevelKitQuery.AgentsInBox(center, transform.rotation, half, Layers.Value, RequiredTag.Value, _agents, transform);

            if (count != Occupants.Value)
            {
                Occupants.Value = count;
                OnOccupantsChanged.Invoke(count);
            }

            bool enough = count >= Mathf.Max(1, RequiredCount.Value);
            if (enough)
            {
                _releaseAt.Value = Time.time + ReleaseDelay.Value;
                if (!IsPressed.Value)
                {
                    IsPressed.Value = true;
                    OnPressed.Invoke();
                }
            }
            else if (IsPressed.Value && Time.time >= _releaseAt.Value)
            {
                IsPressed.Value = false;
                OnReleased.Invoke();
            }
        }

        protected override void OnUpdate()
        {
            var plate = Plate.Value;
            if (plate == null || !_plateRest.TryGetValue(Self, out var rest))
                return;
            float target = IsPressed.Value ? 1f : 0f;
            if (Mathf.Approximately(_visual.Value, target))
                return;
            _visual.Value = Mathf.MoveTowards(_visual.Value, target, Time.deltaTime * 10f);
            plate.localPosition = rest - Vector3.up * (PressDepth.Value * _visual.Value);
        }

        protected override void OnDestroy() => _plateRest.Remove(Self);

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            bool pressed = context.Instance != null && context.Instance.IsSpawned && context.Get(IsPressed);
            LevelKitQuery.DrawLocalBox(context.Transform, context.Get(ZoneCenter), context.Get(ZoneSize),
                pressed ? new Color(0.3f, 1f, 0.3f, 0.8f) : new Color(1f, 0.6f, 0.2f, 0.8f), filled: true);
        }
    }
}
