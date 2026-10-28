using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// The wires crossing the edge of a selection of nodes: what comes in from the rest of the graph, and what goes out.
    /// </summary>
    public sealed class CollapseBoundary
    {
        public readonly List<Node> Nodes;
        /// <summary>Wires from an output outside to an input inside.</summary>
        public readonly List<(Port outside, Port inside)> In = new();
        /// <summary>Wires from an output inside to an input outside.</summary>
        public readonly List<(Port inside, Port outside)> Out = new();
        /// <summary>Where the nodes sit (their positions' bounds, in graph coordinates).</summary>
        public readonly Rect Bounds;

        public CollapseBoundary(IEnumerable<Node> nodes)
        {
            Nodes = nodes.ToList();
            var inside = new HashSet<Node>(Nodes);
            foreach (var node in Nodes)
                foreach (var port in node.Ports.Values)
                    foreach (var other in port.ConnectedPorts)
                    {
                        if (other?.Node == null || inside.Contains(other.Node))
                            continue;
                        if (port.Direction == PortDirection.Input)
                            In.Add((other, port));
                        else
                            Out.Add((port, other));
                    }
            if (Nodes.Count > 0)
            {
                var min = Nodes[0].Position;
                var max = min;
                foreach (var node in Nodes)
                {
                    min = Vector2.Min(min, node.Position);
                    max = Vector2.Max(max, node.Position);
                }
                Bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
    }

    /// <summary>
    /// Making the new graph out of a selection: the copies of the selected nodes (already in it, moved to start a little
    /// right of where its entry goes), wiring inside it, and how the node that calls it is wired back into the graph
    /// the nodes came from.
    /// </summary>
    public sealed class CollapseBuild
    {
        public readonly SchematicGraph Function;
        public readonly CollapseBoundary Boundary;
        private readonly Dictionary<Node, Node> _copies;
        internal readonly List<(Port outside, string callPin)> LinksIn = new();
        internal readonly List<(string callPin, Port outside)> LinksOut = new();

        /// <summary>Where the copied nodes start (left) and end (right) in the new graph, and their middle height.</summary>
        public readonly float Left, Right, Middle;

        internal CollapseBuild(SchematicGraph function, CollapseBoundary boundary, Dictionary<Node, Node> copies, float left, float right, float middle)
        {
            Function = function;
            Boundary = boundary;
            _copies = copies;
            Left = left;
            Right = right;
            Middle = middle;
        }

        /// <summary>The copy (in the new graph) of a selected node.</summary>
        public Node Copy(Node original) => original != null && _copies.TryGetValue(original, out var copy) ? copy : null;

        /// <summary>The copy (in the new graph) of a port on a selected node.</summary>
        public Port Copy(Port original) => original?.Node != null && _copies.TryGetValue(original.Node, out var copy) ? copy.GetPort(original.Name) : null;

        /// <summary>Wires two ports in the new graph.</summary>
        public void Wire(Port output, Port input)
        {
            if (output == null || input == null)
            {
                Debug.LogWarning($"Collapse to Function: couldn't wire {output?.Node?.Name}.{output?.Name} to {input?.Node?.Name}.{input?.Name} in {Function.name}.", Function);
                return;
            }
            Function.AddEdge(output, input);
        }

        /// <summary>Adds a node to the new graph at a position.</summary>
        public T Add<T>(Vector2 position, string name = null) where T : Node
        {
            var node = (T)NodeReflection.Instantiate(typeof(T));
            if (!string.IsNullOrEmpty(name))
                node.Name = name;
            node.Position = position;
            Function.AddNode(node);
            return node;
        }

        /// <summary>Back in the original graph: an output outside goes into one of the calling node's pins.</summary>
        public void LinkIn(Port outside, string callPin) => LinksIn.Add((outside, callPin));

        /// <summary>Back in the original graph: one of the calling node's pins goes into an input outside.</summary>
        public void LinkOut(string callPin, Port outside) => LinksOut.Add((callPin, outside));

        /// <summary>A name not taken yet (adds 2, 3... to repeats).</summary>
        public static string Unique(ISet<string> taken, string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? "Value" : name.Trim();
            string result = name;
            for (int i = 2; taken.Contains(result); i++)
                result = name + " " + i;
            taken.Add(result);
            return result;
        }
    }

    /// <summary>
    /// One kind of graph's Collapse to Function: which graphs it works in, what can't be collapsed, the kind of graph
    /// made, and how its entry and exit are built.
    /// </summary>
    public abstract class FunctionCollapser
    {
        public abstract bool Fits(Graph graph);

        /// <summary>Where new functions go by default.</summary>
        public abstract string Folder { get; }

        /// <summary>Why the selection can't be collapsed (shown to the person), or null if it can.</summary>
        public abstract string Check(CollapseBoundary boundary);

        public abstract SchematicGraph CreateGraph();

        /// <summary>Adds the new graph's entry and exit, its inputs and outputs, and wires them; returns the calling node (not yet in any graph).</summary>
        public abstract Node Build(CollapseBuild build);
    }

    /// <summary>
    /// Collapse to Function: the selected nodes move into a new function graph, and a node calling it takes their place,
    /// wired the way they were. Wires coming into the selection become the function's inputs, wires going out its
    /// outputs. From a node's or the canvas's right-click menu.
    /// </summary>
    [InitializeOnLoad]
    public static class CollapseToFunction
    {
        private static readonly List<FunctionCollapser> _collapsers = new();

        static CollapseToFunction()
        {
            Register(new LogicFunctionCollapser());
            NodeView.ExtendContextMenu -= AddToNodeMenu;
            NodeView.ExtendContextMenu += AddToNodeMenu;
            CanvasView.ExtendContextMenu -= AddToCanvasMenu;
            CanvasView.ExtendContextMenu += AddToCanvasMenu;
        }

        /// <summary>Adds one kind of graph's Collapse to Function (call from an [InitializeOnLoad] class's static constructor).</summary>
        public static void Register(FunctionCollapser collapser)
        {
            if (collapser == null)
                return;
            _collapsers.RemoveAll(c => c.GetType() == collapser.GetType());
            _collapsers.Add(collapser);
        }

        private static FunctionCollapser For(Graph graph) => graph == null ? null : _collapsers.LastOrDefault(c => c.Fits(graph));

        private static void AddToNodeMenu(NodeView view, ContextualMenuPopulateEvent evt)
        {
            var canvas = view.GetFirstAncestorOfType<CanvasView>();
            if (canvas == null || For(canvas.Graph) == null)
                return;
            var selected = canvas.SelectedElements.OfType<NodeView>().Select(v => v.Target).Where(n => n != null).ToList();
            // Right-clicking a node outside the selection acts on that node alone.
            if (!selected.Contains(view.Target))
                selected = new List<Node> { view.Target };
            evt.menu.AppendAction("Collapse to Function...", _ => Collapse(canvas, selected));
        }

        private static void AddToCanvasMenu(CanvasView canvas, ContextualMenuPopulateEvent evt)
        {
            if (For(canvas.Graph) == null)
                return;
            var selected = canvas.SelectedElements.OfType<NodeView>().Select(v => v.Target).Where(n => n != null).ToList();
            evt.menu.AppendAction("Collapse Selection to Function...", _ => Collapse(canvas, selected),
                selected.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        /// <summary>Moves nodes of the canvas's graph into a new function graph (asking where to save it), called from where they were.</summary>
        public static void Collapse(CanvasView canvas, IList<Node> nodes)
        {
            var graph = canvas?.Graph as SchematicGraph;
            var collapser = For(graph);
            if (graph == null || collapser == null || nodes == null || nodes.Count == 0)
                return;

            var boundary = new CollapseBoundary(nodes);
            string problem = collapser.Check(boundary);
            if (problem == null)
                foreach (var node in boundary.Nodes)
                    if (NodeReflection.GetNodeType(node.GetType()) is { Deletable: false })
                    {
                        problem = $"{node.Name} has to stay in this graph: leave it out of the selection.";
                        break;
                    }
            if (problem != null)
            {
                EditorUtility.DisplayDialog("Collapse to Function", problem, "OK");
                return;
            }

            string folder = collapser.Folder;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            string path = EditorUtility.SaveFilePanelInProject("Collapse to Function", "New Function", "asset",
                "Save the new function. The selected nodes move into it, and a node calling it takes their place.", folder);
            if (string.IsNullOrEmpty(path))
                return;

            var function = collapser.CreateGraph();
            function.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(function, path);

            // The selected nodes, copied into the new graph with only the wires between them; laid out from x = 300
            // (the entry goes at 0), keeping their arrangement.
            var copies = CopyNodes(boundary.Nodes);
            var offset = new Vector2(300f - boundary.Bounds.xMin, -boundary.Bounds.center.y);
            var map = new Dictionary<Node, Node>();
            for (int i = 0; i < copies.Count; i++)
            {
                copies[i].Position += offset;
                map[boundary.Nodes[i]] = copies[i];
            }
            function.AddNodes(copies);
            var build = new CollapseBuild(function, boundary, map, 300f, 300f + boundary.Bounds.width, 0f);

            Node caller;
            try
            {
                caller = collapser.Build(build);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                caller = null;
            }
            EditorUtility.SetDirty(function);
            AssetDatabase.SaveAssets();
            if (caller == null)
            {
                Debug.LogError($"Collapse to Function: couldn't build {function.name}; the original graph is unchanged.", function);
                return;
            }

            // The original graph: the calling node where the nodes were, wired as they were, and the nodes gone.
            Undo.RegisterCompleteObjectUndo(graph, "Collapse to Function");
            caller.Position = boundary.Bounds.center;
            graph.AddNode(caller);
            if (caller is IDynamicPorts dynamic)
                dynamic.SyncPorts();
            foreach (var node in boundary.Nodes)
                graph.RemoveNode(node);
            foreach (var (outside, pin) in build.LinksIn)
                Connect(graph, outside, caller.GetPort(pin));
            foreach (var (pin, outside) in build.LinksOut)
                Connect(graph, caller.GetPort(pin), outside);
            EditorUtility.SetDirty(graph);
            canvas.Reload();
            Debug.Log($"Collapsed {boundary.Nodes.Count} node{(boundary.Nodes.Count == 1 ? "" : "s")} into {function.name}.", function);
        }

        private static void Connect(Graph graph, Port output, Port input)
        {
            if (output == null || input == null)
            {
                Debug.LogWarning($"Collapse to Function: couldn't wire {output?.Node?.Name}.{output?.Name} to {input?.Node?.Name}.{input?.Name}.");
                return;
            }
            if (output.ConnectedPorts.Contains(input))
                return;
            graph.AddEdge(output, input);
        }

        // Copies nodes the way copy and paste does (new IDs, only the wires among them kept), in the same order.
        private static List<Node> CopyNodes(List<Node> nodes)
        {
            var holder = ScriptableObject.CreateInstance<CopyPasteGraph>();
            holder.Nodes = new List<Node>(nodes);
            string json = JsonUtility.ToJson(holder);
            UnityEngine.Object.DestroyImmediate(holder);
            var copy = CopyPasteGraph.Deserialize(json, Array.Empty<string>());
            var result = copy.Nodes;
            UnityEngine.Object.DestroyImmediate(copy);
            return result;
        }
    }

    /// <summary>
    /// Collapse to Function in logic graphs: makes a Function Graph. The flow coming in starts at its On Called, the
    /// flow going out leaves through its Return, values wired in become its inputs, values wired out its outputs, and a
    /// Call node takes the nodes' place.
    /// </summary>
    internal sealed class LogicFunctionCollapser : FunctionCollapser
    {
        public override bool Fits(Graph graph)
            => graph is SchematicGraph && Attribute.GetCustomAttributes(graph.GetType(), typeof(IncludeTagsAttribute), true)
                .OfType<IncludeTagsAttribute>().Any(a => a.Tags.Contains("Default"));

        public override string Folder => FunctionGraph.Folder;

        private static bool IsFlow(Port port) => port.Type == typeof(ActionPort);

        public override string Check(CollapseBoundary boundary)
        {
            var events = boundary.Nodes.FirstOrDefault(n => n is SchematicEventNode);
            if (events != null)
                return $"{events.Name} is an event: events start flows in this graph, so they can't move into a function. Leave it out of the selection (its flow becomes the function's way in).";

            if (boundary.Nodes.FirstOrDefault(n => n is ReturnNode) is { } exit)
                return $"{exit.Name} ends this function: it has to stay in this graph. Leave it out of the selection.";

            var graph = boundary.Nodes[0].Graph as SchematicGraph;
            if (boundary.Nodes.OfType<SendSignalNode>().FirstOrDefault(n => n.Signal != null && n.Signal.GraphDirection == GraphSignalDirection.Output && graph != null && graph.FindSignal(n.Signal) != null) is { } send)
                return $"{send.Name} sends \"{send.Signal.name}\", one of this graph's Output Signals, and only this graph may send those. Leave it out of the selection (send it after the Call).";

            var entries = boundary.In.Where(w => IsFlow(w.inside)).Select(w => w.inside).Distinct().ToList();
            if (entries.Count > 1)
                return "The flow comes into the selection at more than one node (" + string.Join(", ", entries.Select(p => p.Node.Name)) + "). A function starts in one place: select so the flow enters at one node.";

            // Every way out has to lead to the same place, since a function returns to one.
            var exits = boundary.Out.Where(w => IsFlow(w.inside)).GroupBy(w => w.inside)
                .Select(g => string.Join("|", g.Select(w => w.outside.Node.ID + "/" + w.outside.Name).OrderBy(s => s))).Distinct().ToList();
            if (exits.Count > 1)
                return "The flow leaves the selection to different places (a Branch's True and False going different ways, say). A function returns to one place: include the nodes after it, or select less.";

            if (entries.Count == 0 && boundary.Nodes.Any(n => n.Ports.Values.Any(p => IsFlow(p) && p.Direction == PortDirection.Input)))
                return "No flow comes into the selection, so nothing would ever call the function. Select nodes the flow runs through.";
            if (entries.Count == 0)
                return "There's no flow in the selection (only values). A function runs when flow reaches its Call: select nodes the flow runs through.";
            return null;
        }

        public override SchematicGraph CreateGraph() => ScriptableObject.CreateInstance<FunctionGraph>();

        public override Node Build(CollapseBuild build)
        {
            var function = (FunctionGraph)build.Function;
            var boundary = build.Boundary;
            // Variable nodes reading "this graph's" Variables keep reading the graph they came from.
            var from = boundary.Nodes[0].Graph as SchematicGraph;
            foreach (var original in boundary.Nodes)
                if (original is GraphVariableNodeBase { Owner: null } && build.Copy(original) is GraphVariableNodeBase copy)
                    copy.Owner = from;

            var inputNames = new HashSet<string>(StringComparer.Ordinal);
            var outputNames = new HashSet<string>(StringComparer.Ordinal);

            // Values coming in: one input per output outside (one value feeding several nodes stays one input).
            var inputs = new List<(FunctionPin pin, List<(Port outside, Port inside)> wires)>();
            foreach (var group in boundary.In.Where(w => !IsFlow(w.inside)).GroupBy(w => w.outside))
            {
                var wires = group.ToList();
                var type = group.Key.Type ?? wires[0].inside.Type ?? typeof(object);
                var pin = new FunctionPin { Name = CollapseBuild.Unique(inputNames, wires[0].inside.Name), Type = type };
                function.Function.Inputs.Add(pin);
                inputs.Add((pin, wires));
            }
            // Values going out: one output per output inside.
            var outputs = new List<(FunctionPin pin, Port inside, List<Port> outside)>();
            foreach (var group in boundary.Out.Where(w => !IsFlow(w.inside)).GroupBy(w => w.inside))
            {
                var pin = new FunctionPin { Name = CollapseBuild.Unique(outputNames, group.Key.Name), Type = group.Key.Type ?? typeof(object) };
                function.Function.Outputs.Add(pin);
                outputs.Add((pin, group.Key, group.Select(w => w.outside).ToList()));
            }
            var flowIn = boundary.In.Where(w => IsFlow(w.inside)).ToList();
            var flowOut = boundary.Out.Where(w => IsFlow(w.inside)).ToList();

            // On Called: the way in, and the inputs.
            var entry = build.Add<OnCalledNode>(new Vector2(0, build.Middle), "On Called");
            entry.SyncPorts();
            foreach (var inside in flowIn.Select(w => w.inside).Distinct())
                build.Wire(entry.GetPort("▶"), build.Copy(inside));
            foreach (var (pin, wires) in inputs)
                foreach (var (_, inside) in wires)
                    build.Wire(entry.GetPort(pin.Name), build.Copy(inside));

            // Return: the way out, and the outputs. With values going out but no flow, it goes where the flow ends.
            if (flowOut.Count > 0 || outputs.Count > 0)
            {
                var exit = build.Add<ReturnNode>(new Vector2(build.Right + 350f, build.Middle), "Return");
                exit.SyncPorts();
                var exitPorts = flowOut.Select(w => w.inside).Distinct().ToList();
                if (exitPorts.Count == 0)
                    exitPorts = boundary.Nodes.SelectMany(n => n.Ports.Values)
                        .Where(p => IsFlow(p) && p.Direction == PortDirection.Output && !p.ConnectedPorts.Any()).ToList();
                foreach (var inside in exitPorts)
                    build.Wire(build.Copy(inside), exit.GetPort("▷"));
                foreach (var (pin, inside, _) in outputs)
                    build.Wire(build.Copy(inside), exit.GetPort(pin.Name));
            }

            // The Call node, and how it's wired where the nodes were.
            var call = (CallFunctionNode)NodeReflection.Instantiate(typeof(CallFunctionNode));
            call.Calls = function;
            call.Name = function.name;
            foreach (var outside in flowIn.Select(w => w.outside).Distinct())
                build.LinkIn(outside, "▷");
            foreach (var outside in flowOut.Select(w => w.outside).Distinct())
                build.LinkOut("▶", outside);
            foreach (var (pin, wires) in inputs)
                build.LinkIn(wires[0].outside, pin.Name);
            foreach (var (pin, _, outsides) in outputs)
                foreach (var outside in outsides)
                    build.LinkOut(call.OutputPin(pin), outside);
            return call;
        }
    }
}
