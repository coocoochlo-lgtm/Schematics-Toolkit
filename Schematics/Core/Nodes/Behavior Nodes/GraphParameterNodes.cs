using BlueGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Sets the Parameters of a running graph of any kind (a playing Sound Cue, an object's graph): the one wired into
    /// Graph (a Graph Variable, a Play Sound Cue's Player, an object), with one pin per Parameter of its graph.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Set Graph Parameters"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic/Graph"", ""description"": ""Sets a running graph's Parameters while it runs: the graph wired into Graph (a Graph Variable, a Play Sound Cue's Player, or an object: every graph of that kind running on it). One pin per Parameter of its graph (the Graph Variable's graph, the Play Sound Cue's cue, or the one picked in Of); only the wired pins are set."", ""inputs"": [{""name"": ""Graph"", ""type"": ""Object""}], ""use_for"": [""an engine sound's RPM and Throttle every frame"", ""music intensity as a fight heats up"", ""any running graph's settings from another graph""], ""notes"": ""Runs when flow reaches it: on On Update for values that keep changing, or when they change. Unwired Graph: the graphs of that kind on the object running this one.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Set Graph Parameters", Path = "Schematic/Graph"), Tags("Object")]
    public class SetGraphParametersNode : SchematicActionNode, IDynamicPorts
    {
        public const string GraphPort = "Graph";

        [Input(GraphPort)] public UnityEngine.Object Target;
        [Editable, Tooltip("Whose Parameters get pins: empty takes it from what's wired into Graph (a Graph Variable's graph, a Play Sound Cue's cue).")]
        public SchematicGraph Of;

        /// <summary>The graph whose Parameters it sets: picked in Of, else the one behind what's wired into Graph.</summary>
        public SchematicGraph GraphFor()
        {
            if (Of != null)
                return Of;
            var port = GetPort(GraphPort);
            if (port == null)
                return null;
            foreach (var source in port.ConnectedPorts)
            {
                if (source?.Node is IGraphSource giver && giver.GraphBehind(source.Name) is SchematicGraph given)
                    return given;
                if (source?.Node is GraphVariableNodeBase variable && variable.Definition?.Graph != null)
                    return variable.Definition.Graph;
            }
            return null;
        }

        // A Parameter's pin: named after it, unless that's Graph.
        private static string PortFor(string parameter) => parameter == GraphPort ? parameter + " (Parameter)" : parameter;

        public bool SyncPorts()
        {
            var graph = GraphFor();
            var parameters = graph != null ? graph.GraphParameters : Array.Empty<GraphParameter>();
            return DynamicPorts.SyncInputs(this, parameters.Select(p => (PortFor(p.Name), p.Type ?? typeof(float))),
                port => port.Name != GraphPort && port.Type != typeof(ActionPort));
        }

        public override void OnValidate()
        {
            base.OnValidate();
            if (SyncPorts())
                DynamicPorts.Notify(this);
        }

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var graph = GraphFor();
            var target = HasInputConnections(GraphPort) ? GetInputValue<UnityEngine.Object>(GraphPort, null) : instance;
            if (target == null)
                return;
            var runners = SchematicGraphRunner.RunnersOn(target, graph);
            if (runners.Count == 0)
                return;
            graph ??= runners[0].RunningGraph;
            if (graph == null)
                return;
            // The wired pins' values, read once for every runner they go to.
            var values = new List<(string name, object value)>();
            foreach (var parameter in graph.GraphParameters)
            {
                string port = PortFor(parameter.Name);
                if (GetPort(port) == null || !HasInputConnections(port))
                    continue;
                object value = parameter.Type == typeof(float) ? GetInputValue(port, 0f) : GetInputValue<object>(port, parameter.Default);
                values.Add((parameter.Name, value));
            }
            foreach (var runner in runners)
                foreach (var (name, value) in values)
                    runner.SetGraphParameter(name, value);
        }
    }
}
