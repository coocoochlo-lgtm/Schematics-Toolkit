using Dreamteck.Splines;
using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>
    /// Moves its object along a Dreamteck spline at a set speed: the Schematic counterpart to Dreamteck's
    /// SplineFollower, with every follower in the game updated by one Behavior.<br/>
    /// Can jump to the closest point on another spline (branching paths), snap to its spline's closest point, and fire
    /// the spline's Dreamteck triggers as it passes them.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Spline Agent"", ""kind"": ""behavior"", ""menu"": ""Splines/Spline Agent"", ""description"": ""Moves its object along a Dreamteck spline at a set speed: the Schematic counterpart to Dreamteck's SplineFollower, with every follower in the game updated by one Behavior. Can jump to the closest point on another spline (branching paths), snap to its spline's closest point, and fire the spline's Dreamteck triggers as it passes them."", ""variables"": [""Spline"", ""FindNearestSpline"", ""StartAtClosestPoint"", ""StartPercent"", ""Speed"", ""Wrap"", ""Paused"", ""FaceAlongSpline"", ""Offset"", ""CheckTriggers"", ""Percent""], ""signals"": [""SetSpeed"", ""SetPercent"", ""JumpToSpline"", ""JumpToNearestSpline"", ""SnapToClosestPoint"", ""ReachedEnd"", ""ChangedSpline""], ""use_for"": [""moving platforms on paths"", ""rails and grinding"", ""patrol routes"", ""roller coasters""]}")]
    [SchematicBehaviorPath("Splines/Spline Agent")]
    public class SplineAgent : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Spline"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""SplineComputer"", ""default"": ""null"", ""description"": ""The spline to follow. Prefabs can't reference scene splines: leave it empty and use Find Nearest Spline, or set it from a graph.""}")]
        [Tooltip("The spline to follow. Prefabs can't reference scene splines: leave it empty and use Find Nearest Spline, or set it from a graph.")]
        public readonly VariableHandle<SplineComputer> Spline = (SplineComputer)null;
        [SchematicDoc(@"{""name"": ""FindNearestSpline"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""With no spline set, start on the nearest spline in the scene.""}")]
        [Tooltip("With no spline set, start on the nearest spline in the scene.")]
        public readonly VariableHandle<bool> FindNearestSpline = true;
        [SchematicDoc(@"{""name"": ""StartAtClosestPoint"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Start at the closest point to where the object is, instead of at Start Percent.""}")]
        [Tooltip("Start at the closest point to where the object is, instead of at Start Percent.")]
        public readonly VariableHandle<bool> StartAtClosestPoint = true;
        [SchematicDoc(@"{""name"": ""StartPercent"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""double"", ""default"": ""0.0"", ""description"": ""Start Percent (double) of Spline Agent.""}")]
        public readonly VariableHandle<double> StartPercent = 0.0;

        [SchematicDoc(@"{""name"": ""Speed"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""float"", ""default"": ""5f"", ""description"": ""Units per second along the spline. Negative goes backwards.""}")]
        [Tooltip("Units per second along the spline. Negative goes backwards.")]
        public readonly VariableHandle<float> Speed = 5f;
        [SchematicDoc(@"{""name"": ""Wrap"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""SplineWrap"", ""default"": ""SplineWrap.Clamp"", ""description"": ""Wrap (SplineWrap) of Spline Agent.""}")]
        public readonly VariableHandle<SplineWrap> Wrap = SplineWrap.Clamp;
        [SchematicDoc(@"{""name"": ""Paused"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Paused (bool) of Spline Agent.""}")]
        public readonly VariableHandle<bool> Paused = false;
        [SchematicDoc(@"{""name"": ""FaceAlongSpline"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Turn the object to face along the spline.""}")]
        [Tooltip("Turn the object to face along the spline.")]
        public readonly VariableHandle<bool> FaceAlongSpline = true;
        [SchematicDoc(@"{""name"": ""Offset"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""Vector3"", ""default"": ""Vector3.zero"", ""description"": ""Offset from the spline: x to its right, y up, z forward.""}")]
        [Tooltip("Offset from the spline: x to its right, y up, z forward.")]
        public readonly VariableHandle<Vector3> Offset = Vector3.zero;
        [SchematicDoc(@"{""name"": ""CheckTriggers"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Fire the spline's Dreamteck triggers when passing them.""}")]
        [Tooltip("Fire the spline's Dreamteck triggers when passing them.")]
        public readonly VariableHandle<bool> CheckTriggers = false;

        /// <summary>The current position along the spline, 0-1.</summary>
        [SchematicDoc(@"{""name"": ""Percent"", ""kind"": ""variable"", ""behavior"": ""Spline Agent"", ""type"": ""double"", ""default"": ""0.0"", ""description"": ""The current position along the spline, 0-1.""}")]
        public readonly VariableHandle<double> Percent = 0.0;
        private readonly VariableHandle<bool> _atEnd = false;

        [SchematicDoc(@"{""name"": ""SetSpeed"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""input"", ""params"": [{""name"": ""Speed"", ""type"": ""float""}], ""description"": ""Tells Spline Agent to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Speed", typeof(float))]
        public readonly SignalHandle<float> SetSpeed;
        [SchematicDoc(@"{""name"": ""SetPercent"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""input"", ""params"": [{""name"": ""Percent"", ""type"": ""float""}], ""description"": ""Tells Spline Agent to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Percent", typeof(float))]
        public readonly SignalHandle<float> SetPercent;
        [SchematicDoc(@"{""name"": ""JumpToSpline"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""input"", ""params"": [{""name"": ""Spline"", ""type"": ""SplineComputer""}], ""description"": ""Tells Spline Agent to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal, Parameter("Spline", typeof(SplineComputer))]
        public readonly SignalHandle<SplineComputer> JumpToSpline;
        [SchematicDoc(@"{""name"": ""JumpToNearestSpline"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""input"", ""description"": ""Tells Spline Agent to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal]
        public readonly SignalHandle JumpToNearestSpline;
        [SchematicDoc(@"{""name"": ""SnapToClosestPoint"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""input"", ""description"": ""Tells Spline Agent to act."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
        [InputSignal]
        public readonly SignalHandle SnapToClosestPoint;

        [SchematicDoc(@"{""name"": ""ReachedEnd"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""output"", ""description"": ""Sent by Spline Agent when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal]
        public readonly SignalHandle ReachedEnd;
        [SchematicDoc(@"{""name"": ""ChangedSpline"", ""kind"": ""signal"", ""behavior"": ""Spline Agent"", ""direction"": ""output"", ""params"": [{""name"": ""Spline"", ""type"": ""SplineComputer""}], ""description"": ""Sent by Spline Agent when it happens."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [OutputSignal, Parameter("Spline", typeof(SplineComputer))]
        public readonly SignalHandle<SplineComputer> ChangedSpline;

        protected override void OnStart()
        {
            if (Spline.Value == null && FindNearestSpline.Value)
                Spline.Value = SplineQueries.Nearest(Self.Transform.position, out _);

            var spline = Spline.Value;
            if (spline != null)
                Percent.Value = StartAtClosestPoint.Value ? spline.ClosestPercent(Self.Transform.position) : StartPercent.Value;
            Place();

            SetSpeed.Subscribe(speed => Speed.Value = speed);
            SetPercent.Subscribe(percent => { Percent.Value = Mathf.Clamp01(percent); _atEnd.Value = false; Place(); });
            JumpToSpline.Subscribe(other => JumpTo(other));
            JumpToNearestSpline.Subscribe(() => JumpTo(SplineQueries.Nearest(Self.Transform.position, out _, exclude: Spline.Value)));
            SnapToClosestPoint.Subscribe(() =>
            {
                if (Spline.Value == null) return;
                Percent.Value = Spline.Value.ClosestPercent(Self.Transform.position);
                Place();
            });
        }

        protected override void OnUpdate()
        {
            var spline = Spline.Value;
            if (spline == null || Paused.Value)
                return;

            double from = Percent.Value;
            double to = spline.Advance(from, Speed.Value * Time.deltaTime, Wrap.Value, out bool hitEnd, out bool reversed);
            if (reversed)
                Speed.Value = -Speed.Value;
            Percent.Value = to;

            if (CheckTriggers.Value)
                spline.CheckTriggers(from, to);

            // Clamped agents report reaching the end once, until they move off it.
            if (hitEnd && Wrap.Value == SplineWrap.Clamp)
            {
                if (!_atEnd.Value)
                {
                    _atEnd.Value = true;
                    ReachedEnd.Invoke();
                }
            }
            else if (hitEnd)
                ReachedEnd.Invoke();
            else
                _atEnd.Value = false;

            Place();
        }

        /// <summary>
        /// Moves Self onto another spline at its closest point to where the object is now. Keeps the speed and direction.
        /// </summary>
        public void JumpTo(SplineComputer other)
        {
            if (other == null || Self == null)
                return;
            Spline.Value = other;
            Percent.Value = other.ClosestPercent(Self.Transform.position);
            _atEnd.Value = false;
            Place();
            ChangedSpline.Invoke(other);
        }

        /// <summary>Puts Self's object at its percent on the spline (with the offset), facing along it if asked.</summary>
        public void Place()
        {
            var spline = Spline.Value;
            if (spline == null || Self == null)
                return;
            var sample = spline.Evaluate(Percent.Value);
            var transform = Self.Transform;
            transform.position = sample.Offset(Offset.Value);
            if (FaceAlongSpline.Value)
            {
                var rotation = sample.rotation;
                // Facing backwards while travelling backwards.
                if (Speed.Value < 0f)
                    rotation = Quaternion.LookRotation(-sample.forward, sample.up);
                transform.rotation = rotation;
            }
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            var spline = context.Get(Spline);
            if (spline == null)
                return;
            var sample = spline.Closest(context.Transform.position);
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawLine(context.Transform.position, sample.position);
            Gizmos.DrawWireSphere(sample.position, 0.15f);
        }
    }
}
