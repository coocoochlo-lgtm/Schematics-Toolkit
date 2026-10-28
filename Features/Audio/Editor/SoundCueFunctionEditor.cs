using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Audio
{
    /// <summary>
    /// A Sound Cue Call node: each of its cue's Parameters drawn on its pin with the control it was given (a slider,
    /// a toggle, a dropdown), shown while nothing's wired in. Double-click the title to open the cue it calls.
    /// </summary>
    [CustomNodeView(typeof(SoundCueCallNode))]
    public class SoundCueCallNodeView : NodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            titleContainer.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && Target is SoundCueCallNode { Calls: { } called })
                {
                    AssetDatabase.OpenAsset(called);
                    e.StopPropagation();
                }
            });
            titleContainer.tooltip = "Double-click to open the Sound Cue it calls.";
        }

        protected override void AddInputPort(Port port)
        {
            base.AddInputPort(port);
            var node = (SoundCueCallNode)Target;
            if (!node.ParameterFor(port.Name, out var parameter))
                return;
            var view = Inputs[Inputs.Count - 1];
            var control = FunctionParameterControls.Create(parameter, node.ValueOf(parameter), value =>
            {
                var graph = node.Graph as UnityEngine.Object;
                if (graph != null)
                    Undo.RecordObject(graph, "Set " + parameter.Name);
                node.SetValue(parameter, value);
                if (graph != null)
                    EditorUtility.SetDirty(graph);
            });
            var container = new VisualElement();
            container.AddToClassList("property-field-container");
            container.Add(control);
            view.SetEditorField(container);
        }
    }

    /// <summary>
    /// Sound Cues marked In Library, offered in Sound Cues' node search under Functions (their Library Path as folders):
    /// picking one makes a Call node calling it.
    /// </summary>
    public class SoundFunctionSearchProvider : ISearchProvider
    {
        public bool IsSupported(IGraph graph) => graph is SoundCueGraph;

        public IEnumerable<SearchResult> GetSearchResults(SearchFilter filter)
        {
            // Dragged from a pin: only sound (numbers) connects.
            if (filter.SourcePort != null && filter.SourcePort.Type != typeof(float))
                yield break;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SoundCueGraph)))
            {
                var graph = AssetDatabase.LoadAssetAtPath<SoundCueGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph == null || !graph.Function.InLibrary || ReferenceEquals(graph, filter.Graph))
                    continue;
                if (filter.SourcePort != null && filter.SourcePort.Direction == PortDirection.Output && graph.FunctionInputs.Count == 0 && graph.GraphParameters.Count == 0)
                    continue;
                var path = new List<string> { GraphFunctions.SearchFolder };
                path.AddRange((graph.Function.Path ?? "").Split('/').Select(p => p.Trim()).Where(p => p.Length > 0));
                yield return new SearchResult { Name = graph.name, Path = path, UserData = graph };
            }
        }

        public Node Instantiate(SearchResult result)
        {
            if (result.UserData is not SoundCueGraph graph)
                return null;
            var node = NodeReflection.GetNodeType(typeof(SoundCueCallNode))?.CreateInstance() as SoundCueCallNode;
            if (node == null)
                return null;
            node.Calls = graph;
            node.Name = graph.name;
            node.SyncPorts();
            return node;
        }
    }

    /// <summary>
    /// Collapse to Function in Sound Cues: makes a Sound Cue. What's wired in becomes its Sound Inputs (a Parameter of
    /// this cue becomes one of its Parameters instead), the one sound wired out goes to its Output, and a Call node
    /// takes the nodes' place.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class SoundFunctionCollapser : FunctionCollapser
    {
        static SoundFunctionCollapser() => CollapseToFunction.Register(new SoundFunctionCollapser());

        public override bool Fits(Graph graph) => graph is SoundCueGraph;

        public override string Folder => SoundCueGraph.Folder;

        public override string Check(CollapseBoundary boundary)
        {
            if (boundary.Nodes.FirstOrDefault(n => n is SoundCueOutput) is { } output)
                return $"{output.Name} is this cue's result: it has to stay. Leave it out of the selection.";
            var results = boundary.Out.Select(w => w.inside).Distinct().ToList();
            if (results.Count == 0)
                return "Nothing in the selection is used by the rest of the cue, so the function would give back silence.";
            if (results.Count > 1)
                return "A Sound Cue function gives back one sound, and the rest of the cue uses " +
                       string.Join(", ", results.Select(p => $"{p.Node.Name}'s {p.Name}")) + " from the selection. Select so only one leaves it.";
            return null;
        }

        public override SchematicGraph CreateGraph()
        {
            var cue = ScriptableObject.CreateInstance<SoundCueGraph>();
            // Functions are part of the cue calling them, so the calling cue decides how it's heard.
            cue.Channels = SoundCueGraph.SoundCueChannels.Mono;
            return cue;
        }

        public override Node Build(CollapseBuild build)
        {
            var boundary = build.Boundary;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var call = (SoundCueCallNode)NodeReflection.Instantiate(typeof(SoundCueCallNode));
            call.Name = build.Function.name;
            call.Calls = (SoundCueGraph)build.Function;

            // What comes in: a Parameter for each of this cue's Parameters wired in (keeping its default and control),
            // a Sound Input for anything else (one each, however many nodes it feeds).
            float y = build.Middle - 60f * boundary.In.Select(w => w.outside).Distinct().Count() / 2f;
            foreach (var group in boundary.In.GroupBy(w => w.outside))
            {
                var outside = group.Key;
                string name = CollapseBuild.Unique(names, outside.Node is SoundParameterNode p ? p.ParameterName : group.First().inside.Name);
                Port source;
                if (outside.Node is SoundParameterNode from)
                {
                    var parameter = build.Add<SoundParameterNode>(new Vector2(0, y), "Parameter");
                    parameter.ParameterName = name;
                    parameter.Default = from.Default;
                    parameter.Glide = 0f;
                    parameter.UI = JsonUtility.FromJson<ParameterUI>(JsonUtility.ToJson(from.UI));
                    source = parameter.GetPort("Value");
                }
                else
                {
                    var input = build.Add<SoundInputNode>(new Vector2(0, y), "Sound Input");
                    input.InputName = name;
                    source = input.GetPort("Audio");
                }
                y += 60f;
                foreach (var (_, inside) in group)
                    build.Wire(source, build.Copy(inside));
                build.LinkIn(outside, SoundCueCallNode.PinFor(name));
            }

            // What goes out: the one sound, into the new cue's Output.
            var result = boundary.Out[0].inside;
            var exit = build.Add<SoundCueOutput>(new Vector2(build.Right + 350f, build.Middle), "Output");
            build.Wire(build.Copy(result), exit.GetPort(SoundCueOutput.AudioPort));
            foreach (var outside in boundary.Out.Select(w => w.outside).Distinct())
                build.LinkOut(SoundCueCallNode.AudioPort, outside);
            return call;
        }
    }
}
