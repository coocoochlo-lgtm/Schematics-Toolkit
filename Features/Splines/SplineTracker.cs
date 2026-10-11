using Dreamteck.Splines;
using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>
    /// Keeps its object on the point of a spline closest to a target, like a camera dolly tracking the player or a
    /// guard pacing alongside them: the Schematic counterpart to Dreamteck's SplineProjector.
    /// </summary>
    [SchematicBehaviorPath("Splines/Spline Tracker")]
    public class SplineTracker : SchematicBehavior
    {
        public readonly VariableHandle<SplineComputer> Spline = (SplineComputer)null;
        [Tooltip("With no spline set, use the nearest spline in the scene.")]
        public readonly VariableHandle<bool> FindNearestSpline = true;
        [Tooltip("What to stay closest to.")]
        public readonly VariableHandle<Transform> Target = (Transform)null;
        [Tooltip("Offset from the spline: x to its right, y up, z forward.")]
        public readonly VariableHandle<Vector3> Offset = Vector3.zero;
        public readonly VariableHandle<bool> FaceAlongSpline = true;
        [Tooltip("Turn to look at the target instead (for cameras).")]
        public readonly VariableHandle<bool> LookAtTarget = false;
        [Tooltip("Roughly how many seconds it lags behind the closest point; 0 snaps there.")]
        public readonly VariableHandle<float> Smoothing = 0f;

        /// <summary>The tracked point's position along the spline, 0-1.</summary>
        public readonly VariableHandle<double> Percent = 0.0;

        [InputSignal, Parameter("Target", typeof(Transform))]
        public readonly SignalHandle<Transform> SetTarget;

        protected override void OnStart()
        {
            if (Spline.Value == null && FindNearestSpline.Value)
                Spline.Value = SplineQueries.Nearest(Self.Transform.position, out _);
            SetTarget.Subscribe(target => Target.Value = target);
        }

        protected override void OnLateUpdate()
        {
            var spline = Spline.Value;
            var target = Target.Value;
            if (spline == null || target == null)
                return;

            var sample = spline.Closest(target.position);
            Percent.Value = sample.percent;

            var transform = Self.Transform;
            Vector3 position = sample.Offset(Offset.Value);
            float smoothing = Smoothing.Value;
            transform.position = smoothing > 0f
                ? Vector3.Lerp(transform.position, position, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, smoothing)))
                : position;

            if (LookAtTarget.Value)
            {
                Vector3 toTarget = target.position - transform.position;
                if (toTarget.sqrMagnitude > 1e-8f)
                    transform.rotation = Quaternion.LookRotation(toTarget, sample.up);
            }
            else if (FaceAlongSpline.Value)
                transform.rotation = sample.rotation;
        }
    }
}
