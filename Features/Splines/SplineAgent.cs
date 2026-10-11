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
    [SchematicBehaviorPath("Splines/Spline Agent")]
    public class SplineAgent : SchematicBehavior
    {
        [Tooltip("The spline to follow. Prefabs can't reference scene splines: leave it empty and use Find Nearest Spline, or set it from a graph.")]
        public readonly VariableHandle<SplineComputer> Spline = (SplineComputer)null;
        [Tooltip("With no spline set, start on the nearest spline in the scene.")]
        public readonly VariableHandle<bool> FindNearestSpline = true;
        [Tooltip("Start at the closest point to where the object is, instead of at Start Percent.")]
        public readonly VariableHandle<bool> StartAtClosestPoint = true;
        public readonly VariableHandle<double> StartPercent = 0.0;

        [Tooltip("Units per second along the spline. Negative goes backwards.")]
        public readonly VariableHandle<float> Speed = 5f;
        public readonly VariableHandle<SplineWrap> Wrap = SplineWrap.Clamp;
        public readonly VariableHandle<bool> Paused = false;
        [Tooltip("Turn the object to face along the spline.")]
        public readonly VariableHandle<bool> FaceAlongSpline = true;
        [Tooltip("Offset from the spline: x to its right, y up, z forward.")]
        public readonly VariableHandle<Vector3> Offset = Vector3.zero;
        [Tooltip("Fire the spline's Dreamteck triggers when passing them.")]
        public readonly VariableHandle<bool> CheckTriggers = false;

        /// <summary>The current position along the spline, 0-1.</summary>
        public readonly VariableHandle<double> Percent = 0.0;
        private readonly VariableHandle<bool> _atEnd = false;

        [InputSignal, Parameter("Speed", typeof(float))]
        public readonly SignalHandle<float> SetSpeed;
        [InputSignal, Parameter("Percent", typeof(float))]
        public readonly SignalHandle<float> SetPercent;
        [InputSignal, Parameter("Spline", typeof(SplineComputer))]
        public readonly SignalHandle<SplineComputer> JumpToSpline;
        [InputSignal]
        public readonly SignalHandle JumpToNearestSpline;
        [InputSignal]
        public readonly SignalHandle SnapToClosestPoint;

        [OutputSignal]
        public readonly SignalHandle ReachedEnd;
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
