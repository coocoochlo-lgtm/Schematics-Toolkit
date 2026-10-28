using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Collapse to Function in Mesh Graphs: makes a Mesh Graph. Meshes wired in become its Mesh Inputs, numbers its
    /// Parameters, the one mesh wired out goes to its Output, and a Function node takes the nodes' place.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class MeshFunctionCollapser : FunctionCollapser
    {
        static MeshFunctionCollapser() => CollapseToFunction.Register(new MeshFunctionCollapser());

        public override bool Fits(Graph graph) => graph is MeshGraph;

        public override string Folder => MeshGraph.Folder;

        public override string Check(CollapseBoundary boundary)
        {
            if (boundary.Nodes.FirstOrDefault(n => n is MeshOutputNode) is { } output)
                return $"{output.Name} is this graph's result: it has to stay. Leave it out of the selection.";
            var odd = boundary.In.FirstOrDefault(w => w.outside.Type != typeof(MeshData) && w.outside.Type != typeof(float));
            if (odd.inside != null)
                return $"{odd.inside.Node.Name}'s {odd.inside.Name} is wired in from outside, and a mesh function only takes meshes and numbers. Include {odd.outside.Node.Name} too, or leave {odd.inside.Node.Name} out.";
            var results = boundary.Out.Select(w => w.inside).Distinct().ToList();
            if (results.Count == 0)
                return "Nothing in the selection is used by the rest of the graph, so the function would give back nothing.";
            if (results.Count > 1 || results[0].Type != typeof(MeshData))
                return "A mesh function gives back one mesh, and the rest of the graph uses " +
                       string.Join(", ", results.Select(p => $"{p.Node.Name}'s {p.Name}")) + " from the selection. Select so only one mesh leaves it.";
            return null;
        }

        public override SchematicGraph CreateGraph() => ScriptableObject.CreateInstance<MeshGraph>();

        public override Node Build(CollapseBuild build)
        {
            var boundary = build.Boundary;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var call = (MeshFunctionNode)BlueGraph.Editor.NodeReflection.Instantiate(typeof(MeshFunctionNode));
            call.Name = build.Function.name;
            call.Calls = (MeshGraph)build.Function;

            // What comes in: a Mesh Input per mesh, a Parameter per number (one each, however many nodes it feeds).
            float y = build.Middle - 60f * boundary.In.Select(w => w.outside).Distinct().Count() / 2f;
            foreach (var group in boundary.In.GroupBy(w => w.outside))
            {
                var outside = group.Key;
                string name = CollapseBuild.Unique(names, outside.Node is MeshParameterNode p ? p.ParameterName : group.First().inside.Name);
                Port source;
                if (outside.Type == typeof(MeshData))
                {
                    var input = build.Add<MeshInputNode>(new Vector2(0, y), "Mesh Input");
                    input.InputName = name;
                    source = input.GetPort(MeshNode.MeshPort);
                }
                else
                {
                    var parameter = build.Add<MeshParameterNode>(new Vector2(0, y), "Parameter");
                    parameter.ParameterName = name;
                    if (outside.Node is MeshParameterNode from)
                    {
                        parameter.Default = from.Default;
                        parameter.UI = JsonUtility.FromJson<ParameterUI>(JsonUtility.ToJson(from.UI));
                    }
                    source = parameter.GetPort("Value");
                }
                y += 60f;
                foreach (var (_, inside) in group)
                    build.Wire(source, build.Copy(inside));
                build.LinkIn(outside, MeshFunctionNode.PinFor(name));
            }

            // What goes out: the one mesh, into the new graph's Output.
            var result = boundary.Out[0].inside;
            var exit = build.Add<MeshOutputNode>(new Vector2(build.Right + 350f, build.Middle), "Output");
            build.Wire(build.Copy(result), exit.GetPort(MeshNode.MeshPort));
            foreach (var outside in boundary.Out.Select(w => w.outside).Distinct())
                build.LinkOut(MeshNode.MeshPort, outside);
            return call;
        }
    }
}
