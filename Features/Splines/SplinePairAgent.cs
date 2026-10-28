using Dreamteck.Splines;
using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>
    /// Two points on a spline kept a set distance apart, like the front and back wheels of a cart or the two bogies of a
    /// train car. The object's Spline Agent moves the front point; the back point trails it. The object can sit between
    /// them facing from back to front, and Front/Back objects can be placed on each point.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Spline Pair Agent"", ""kind"": ""behavior"", ""menu"": ""Splines/Spline Pair Agent"", ""description"": ""Two points on a spline kept a set distance apart, like the front and back wheels of a cart or the two bogies of a train car. The object's Spline Agent moves the front point; the back point trails it. The object can sit between them facing from back to front, and Front/Back objects can be placed on each point."", ""variables"": [""Distance"", ""DistanceMeasure"", ""PlaceBetween"", ""Front"", ""Back"", ""BackPercent""], ""use_for"": [""carts, trains and vehicles with front and back wheels on a track""]}")]
    [SchematicBehaviorPath("Splines/Spline Pair Agent")]
    [RequireBehavior(typeof(SplineAgent))]
    public class SplinePairAgent : SchematicBehavior
    {
        public enum Measure
        {
            /// <summary>The points are this far apart in a straight line (rigid, like a cart's axles).</summary>
            StraightLine,
            /// <summary>The points are this far apart along the spline (like two followers at a set gap).</summary>
            AlongSpline,
        }

        [SchematicDoc(@"{""name"": ""Distance"", ""kind"": ""variable"", ""behavior"": ""Spline Pair Agent"", ""type"": ""float"", ""default"": ""2f"", ""description"": ""How far behind the front point the back point is.""}")]
        [Tooltip("How far behind the front point the back point is.")]
        public readonly VariableHandle<float> Distance = 2f;
        [SchematicDoc(@"{""name"": ""DistanceMeasure"", ""kind"": ""variable"", ""behavior"": ""Spline Pair Agent"", ""type"": ""Measure"", ""default"": ""Measure.StraightLine"", ""description"": ""How the gap between the two points is measured: Straight Line (rigid, like a cart's axles) or along the spline.""}")]
        public readonly VariableHandle<Measure> DistanceMeasure = Measure.StraightLine;
        [SchematicDoc(@"{""name"": ""PlaceBetween"", ""kind"": ""variable"", ""behavior"": ""Spline Pair Agent"", ""type"": ""bool"", ""default"": ""true"", ""description"": ""Put the object between the two points, facing from the back point to the front one.""}")]
        [Tooltip("Put the object between the two points, facing from the back point to the front one.")]
        public readonly VariableHandle<bool> PlaceBetween = true;
        [SchematicDoc(@"{""name"": ""Front"", ""kind"": ""variable"", ""behavior"": ""Spline Pair Agent"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Optional object placed on the front point.""}")]
        [Tooltip("Optional object placed on the front point.")]
        public readonly VariableHandle<Transform> Front = (Transform)null;
        [SchematicDoc(@"{""name"": ""Back"", ""kind"": ""variable"", ""behavior"": ""Spline Pair Agent"", ""type"": ""Transform"", ""default"": ""null"", ""description"": ""Optional object placed on the back point.""}")]
        [Tooltip("Optional object placed on the back point.")]
        public readonly VariableHandle<Transform> Back = (Transform)null;

        /// <summary>The back point's position along the spline, 0-1.</summary>
        [SchematicDoc(@"{""name"": ""BackPercent"", ""kind"": ""variable"", ""behavior"": ""Spline Pair Agent"", ""type"": ""double"", ""default"": ""0.0"", ""description"": ""The back point's position along the spline, 0-1.""}")]
        public readonly VariableHandle<double> BackPercent = 0.0;

        // The Spline Agent on the same GameObject, which moves the front point.
        private readonly BehaviorHandle<SplineAgent> _agent;

        protected override void OnSetup(BehaviorSetupContext context)
        {
            var front = new GameObject("Front").transform;
            front.SetParent(context.Transform, false);
            var back = new GameObject("Back").transform;
            back.SetParent(context.Transform, false);
            back.localPosition = new Vector3(0f, 0f, -2f);
            context.Set(Front, front);
            context.Set(Back, back);
        }

        // LateUpdate: after the Spline Agent has moved the front point this frame.
        protected override void OnLateUpdate()
        {
            var agent = _agent.Selected;
            if (agent == null)
                return;
            SplineComputer spline = agent.Spline.Value;
            if (spline == null)
                return;

            double frontPercent = agent.Percent.Value;
            // The back point trails the direction of travel.
            var behind = agent.Speed.Value >= 0f ? Spline.Direction.Backward : Spline.Direction.Forward;
            double backPercent = DistanceMeasure.Value == Measure.StraightLine
                ? spline.PercentAtStraightDistance(frontPercent, Distance.Value, behind)
                : spline.Travel(frontPercent, Distance.Value, behind);
            BackPercent.Value = backPercent;

            var frontSample = spline.Evaluate(frontPercent);
            var backSample = spline.Evaluate(backPercent);
            Vector3 offset = agent.Offset.Value;
            Vector3 frontPosition = frontSample.Offset(offset);
            Vector3 backPosition = backSample.Offset(offset);

            // The object first: Front and Back are usually its children and would move with it.
            if (PlaceBetween.Value)
            {
                Vector3 along = frontPosition - backPosition;
                Vector3 up = Vector3.Slerp(backSample.up, frontSample.up, 0.5f);
                Self.Transform.position = (frontPosition + backPosition) * 0.5f;
                if (along.sqrMagnitude > 1e-8f)
                    Self.Transform.rotation = Quaternion.LookRotation(along, up);
            }

            if (Front.Value != null)
                Front.Value.SetPositionAndRotation(frontPosition, frontSample.rotation);
            if (Back.Value != null)
                Back.Value.SetPositionAndRotation(backPosition, backSample.rotation);
        }
    }
}
