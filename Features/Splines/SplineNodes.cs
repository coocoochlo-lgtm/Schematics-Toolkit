using BlueGraph;
using Dreamteck.Splines;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Splines
{
    /// <summary>The point on a spline closest to a position.</summary>
    [Serializable]
    [Node(Name = "Closest Point On Spline", Path = "Splines"), Tags("Default")]
    public class ClosestPointOnSplineNode : SchematicActionNode
    {
        [Input("Spline")] public SplineComputer Spline;
        [Input("Position")] public Vector3 Position;

        [Output("Point")] public Vector3 Point;
        [Output("Percent")] public float Percent;
        [Output("Distance")] public float Distance;
        [Output("Forward")] public Vector3 Forward;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var spline = GetInputValue<SplineComputer>("Spline", Spline);
            if (spline == null)
                throw new InvalidOperationException("No spline is connected.");
            Vector3 position = GetInputValue<Vector3>("Position", Position);

            var sample = spline.Closest(position);
            SetOutputValue("Point", sample.position);
            SetOutputValue("Percent", (float)sample.percent);
            SetOutputValue("Distance", Vector3.Distance(position, sample.position));
            SetOutputValue("Forward", sample.forward);
        }
    }

    /// <summary>The point at a 0-1 position along a spline.</summary>
    [Serializable]
    [Node(Name = "Evaluate Spline", Path = "Splines"), Tags("Default")]
    public class EvaluateSplineNode : SchematicActionNode
    {
        [Input("Spline")] public SplineComputer Spline;
        [Input("Percent")] public float Percent;

        [Output("Point")] public Vector3 Point;
        [Output("Forward")] public Vector3 Forward;
        [Output("Up")] public Vector3 Up;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var spline = GetInputValue<SplineComputer>("Spline", Spline);
            if (spline == null)
                throw new InvalidOperationException("No spline is connected.");
            var sample = spline.Evaluate(Mathf.Clamp01(GetInputValue<float>("Percent", Percent)));
            SetOutputValue("Point", sample.position);
            SetOutputValue("Forward", sample.forward);
            SetOutputValue("Up", sample.up);
        }
    }

    /// <summary>
    /// Moves an object's Spline Agent onto another spline at its closest point (branching paths). With no spline
    /// connected, it jumps to the nearest other spline in the scene.
    /// </summary>
    [Serializable]
    [Node(Name = "Jump To Spline", Path = "Splines"), Tags("Default")]
    public class JumpToSplineNode : SchematicActionNode
    {
        [Input("Agent")] public GameObject Agent;
        [Input("Spline")] public SplineComputer Spline;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>("Agent", Agent);
            if (target == null)
                target = instance;
            var agentInstance = ExecutionContext.FindInstance(target, typeof(SplineAgent));
            if (agentInstance == null)
                throw new InvalidOperationException($"{target.name} has no Spline Agent.");

            var agent = (SplineAgent)agentInstance.Behavior;
            using (agent.Pick(agentInstance))
            {
                var spline = GetInputValue<SplineComputer>("Spline", Spline);
                if (spline == null)
                    spline = SplineQueries.Nearest(target.transform.position, out _, exclude: agent.Spline.Value);
                agent.JumpTo(spline);
            }
        }
    }
}
