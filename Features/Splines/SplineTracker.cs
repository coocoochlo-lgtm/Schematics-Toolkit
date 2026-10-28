using Dreamteck.Splines;
using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>
    /// Keeps its object on the point of a spline closest to a target, like a camera dolly tracking the player or a
    /// guard pacing alongside them: the Schematic counterpart to Dreamteck's SplineProjector.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Spline Tracker"", ""kind"": ""behavior"", ""menu"": ""Splines/Spline Tracker"", ""description"": ""Keeps its object on the point of a spline closest to a target, like a camera dolly tracking the player or a guard pacing alongside them: the Schematic counterpart to Dreamteck's SplineProjector."", ""variables"": [""Spline"", ""FindNearestSpline"", ""Target"", ""Offset"", ""FaceAlongSpline"", ""LookAtTarget"", ""Smoothing"", ""Percent""], ""signals"": [""SetTarget""], ""use_for"": [""camera dollies following the player along a path"", ""guards pacing alongside a target""]}")]
    [SchematicBehaviorPath("Splines/Spline Tracker")]
    public class SplineTracker : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Spline"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""SplineComputer"", ""default"": ""null"", ""description"": ""The spline to stay on (empty: the nearest one, when Find Nearest Spline is on).""}")]
        public readonly VariableHandle<SplineComputer> Spline = (SplineComputer)null;
        [SchematicDoc(@"{""name"": ""FindNearestSpline"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""With no spline set, use the nearest spline in the scene.""}")]
        [Tooltip("With no spline set, use the nearest spline in the scene.")]
        public readonly VariableHandle<bool> FindNearestSpline = true;
        [SchematicDoc(@"{""name"": ""Target"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""What to stay closest to.""}")]
        [Tooltip("What to stay closest to.")]
        public readonly VariableHandle<Transform> Target = (Transform)null;
        [SchematicDoc(@"{""name"": ""Offset"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""Vector3"", ""default"": ""Vector3.zero"", ""description"": ""Offset from the spline: x to its right, y up, z forward.""}")]
        [Tooltip("Offset from the spline: x to its right, y up, z forward.")]
        public readonly VariableHandle<Vector3> Offset = Vector3.zero;
        [SchematicDoc(@"{""name"": ""FaceAlongSpline"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Turn the object to face along the spline as it moves.""}")]
        public readonly VariableHandle<bool> FaceAlongSpline = true;
        [SchematicDoc(@"{""name"": ""LookAtTarget"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Turn to look at the target instead (for cameras).""}")]
        [Tooltip("Turn to look at the target instead (for cameras).")]
        public readonly VariableHandle<bool> LookAtTarget = false;
        [Tooltip("Roughly how many seconds it lags behind the closest point; 0 snaps there.")]
        [SchematicDoc(@"{""name"": ""Smoothing"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""float"", ""default"": ""0f"", ""description"": ""How smoothly it glides to its new spot (0 snaps straight there; higher eases more).""}")]
        public readonly VariableHandle<float> Smoothing = 0f;

        /// <summary>The tracked point's position along the spline, 0-1.</summary>
        [SchematicDoc(@"{""name"": ""Percent"", ""kind"": ""variable"", ""behavior"": ""Spline Tracker"", ""type"": ""double"", ""default"": ""0.0"", ""description"": ""The tracked point's position along the spline, 0-1.""}")]
        public readonly VariableHandle<double> Percent = 0.0;

        [SchematicDoc(@"{""name"": ""SetTarget"", ""kind"": ""signal"", ""behavior"": ""Spline Tracker"", ""direction"": ""input"", ""params"": [{""name"": ""Target"", ""type"": ""Transform""}], ""description"": ""Send a Transform to track that instead."", ""how"": ""Send it with a Send a Signal node (drag it from the IO Dock) to make the Behavior act.""}")]
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
