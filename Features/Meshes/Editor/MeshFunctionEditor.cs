using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Remedy.Meshes
{
    /// <summary>
    /// A Function node in the graph: each of its graph's Parameters drawn on its pin with the control it was given (a
    /// slider, a toggle, a dropdown), shown while nothing's wired in. Double-click the title to open the graph it calls.
    /// </summary>
    [CustomNodeView(typeof(MeshFunctionNode))]
    public class MeshFunctionNodeView : NodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            titleContainer.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && Target is MeshFunctionNode { Calls: { } called })
                {
                    AssetDatabase.OpenAsset(called);
                    e.StopPropagation();
                }
            });
            titleContainer.tooltip = "Double-click to open the graph it calls.";
        }

        protected override void AddInputPort(Port port)
        {
            base.AddInputPort(port);
            var node = (MeshFunctionNode)Target;
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
    /// Mesh Graphs marked In Library, offered in Mesh Graphs' node search under Functions (their Library Path as
    /// folders): picking one makes a Function node calling it.
    /// </summary>
    public class MeshFunctionSearchProvider : ISearchProvider
    {
        public bool IsSupported(IGraph graph) => graph is MeshGraph;

        public IEnumerable<SearchResult> GetSearchResults(SearchFilter filter)
        {
            // Dragged from a pin: only meshes connect to a Function's model or its Mesh Inputs.
            if (filter.SourcePort != null && filter.SourcePort.Type != typeof(MeshData))
                yield break;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(MeshGraph)))
            {
                var graph = AssetDatabase.LoadAssetAtPath<MeshGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph == null || !graph.Function.InLibrary || ReferenceEquals(graph, filter.Graph))
                    continue;
                if (filter.SourcePort != null && filter.SourcePort.Direction == PortDirection.Output && graph.FunctionInputs.Count == 0)
                    continue;
                var path = new List<string> { GraphFunctions.SearchFolder };
                path.AddRange((graph.Function.Path ?? "").Split('/').Select(p => p.Trim()).Where(p => p.Length > 0));
                yield return new SearchResult { Name = graph.name, Path = path, UserData = graph };
            }
        }

        public Node Instantiate(SearchResult result)
        {
            if (result.UserData is not MeshGraph graph)
                return null;
            var node = NodeReflection.GetNodeType(typeof(MeshFunctionNode))?.CreateInstance() as MeshFunctionNode;
            if (node == null)
                return null;
            node.Calls = graph;
            node.Name = graph.name;
            node.SyncPorts();
            return node;
        }
    }
}
