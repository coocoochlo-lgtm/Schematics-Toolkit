using BlueGraph;
using BlueGraph.Editor;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// A node whose pins can be set right on them (<see cref="IInlinePinValues"/>), shown while nothing's wired in:
    /// numbers with the control their function gave them (a slider, a toggle, a dropdown), other types with a value
    /// field.
    /// </summary>
    public class InlinePinValuesNodeView : NodeView
    {
        protected override void AddInputPort(Port port)
        {
            base.AddInputPort(port);
            if (Target is not IInlinePinValues node || !node.InlinePin(port.Name, out var pin))
                return;
            var view = Inputs[Inputs.Count - 1];

            void Set(object value)
            {
                var graph = Target.Graph as UnityEngine.Object;
                if (graph != null)
                    Undo.RecordObject(graph, "Set " + pin.Name);
                node.SetInlineValue(pin, value);
                if (graph != null)
                    EditorUtility.SetDirty(graph);
            }

            VisualElement control;
            if (pin.IsNumber)
            {
                var parameter = pin.AsParameter();
                float current = node.InlineValue(pin) switch { float f => f, int i => i, bool b => b ? 1f : 0f, _ => 0f };
                control = FunctionParameterControls.Create(parameter, current, value => Set(value));
            }
            else
            {
                // Vectors, colours, text, objects: the same value field as Variables' defaults.
                var type = pin.ValueType;
                control = new IMGUIContainer(() =>
                {
                    if (SchematicValueField.Draw(GUIContent.none, type, node.InlineValue(pin), out var value))
                        Set(value);
                });
                control.style.minWidth = 120;
                control.tooltip = string.IsNullOrWhiteSpace(pin.UI?.Description) ? pin.Name : pin.UI.Description;
            }
            var container = new VisualElement();
            container.AddToClassList("property-field-container");
            container.Add(control);
            view.SetEditorField(container);
        }
    }

    /// <summary>A Call node: its inputs' controls, and a double-click on its title opens the Function Graph it calls.</summary>
    [CustomNodeView(typeof(CallFunctionNode))]
    public class CallFunctionNodeView : InlinePinValuesNodeView
    {
        protected override void OnInitialize()
        {
            base.OnInitialize();
            titleContainer.tooltip = "Double-click to open the Function Graph it calls.";
            titleContainer.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2 && Target is CallFunctionNode { Calls: { } called })
                {
                    AssetDatabase.OpenAsset(called);
                    e.StopPropagation();
                }
            });
        }
    }

    /// <summary>A Return node: its outputs' controls.</summary>
    [CustomNodeView(typeof(ReturnNode))]
    public class ReturnNodeView : InlinePinValuesNodeView
    { }

    /// <summary>
    /// Function Graphs marked In Library, in the node search of logic graphs (those offering Default nodes) under
    /// Functions and their Path: picking one makes a Call node calling it.
    /// </summary>
    public class FunctionGraphSearchProvider : ISearchProvider
    {
        private static readonly string[] CallTags = { "Default" };

        public bool IsSupported(IGraph graph) => graph is SchematicGraph;

        public IEnumerable<SearchResult> GetSearchResults(SearchFilter filter)
        {
            if (!DefaultSearchProvider.Allows(filter.IncludeTags, CallTags))
                yield break;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(FunctionGraph)))
            {
                var graph = AssetDatabase.LoadAssetAtPath<FunctionGraph>(AssetDatabase.GUIDToAssetPath(guid));
                if (graph == null || !graph.Function.InLibrary)
                    continue;
                // Dragged from a pin: functions with a pin of that type (or any flow pin).
                if (filter.SourcePort != null && filter.SourcePort.Type != typeof(ActionPort))
                {
                    var pins = filter.SourcePort.Direction == PortDirection.Output ? graph.Function.Inputs : graph.Function.Outputs;
                    if (!pins.Any(p => p != null && p.ValueType != null && (p.ValueType.IsAssignableFrom(filter.SourcePort.Type) || filter.SourcePort.Type.IsAssignableFrom(p.ValueType))))
                        continue;
                }
                var path = new List<string> { GraphFunctions.SearchFolder };
                path.AddRange((graph.Function.Path ?? "").Split('/').Select(p => p.Trim()).Where(p => p.Length > 0));
                yield return new SearchResult { Name = graph.name, Path = path, UserData = graph };
            }
        }

        public Node Instantiate(SearchResult result)
        {
            if (result.UserData is not FunctionGraph graph)
                return null;
            var node = NodeReflection.GetNodeType(typeof(CallFunctionNode))?.CreateInstance() as CallFunctionNode;
            if (node == null)
                return null;
            node.Calls = graph;
            node.Name = graph.name;
            node.SyncPorts();
            return node;
        }
    }

    /// <summary>Keeps On Called and Return out of other graphs' searches (On Called is made with every Function Graph).</summary>
    [InitializeOnLoad]
    internal static class FunctionGraphEditor
    {
        static FunctionGraphEditor()
        {
            DefaultSearchProvider.ExclusiveTags.Add(FunctionGraph.Tag);
            DefaultSearchProvider.ExclusiveTags.Add(FunctionGraph.EntryOnlyTag);
        }

        /// <summary>
        /// Brings every node using a Function Graph's inputs and outputs up to date (its own On Called and Return, and
        /// the Call nodes calling it in the graphs that are loaded; others catch up when opened). A renamed pin keeps
        /// its wires.
        /// </summary>
        public static void SyncNodes(FunctionGraph function, string renamedFrom = null, string renamedTo = null)
        {
            if (function == null)
                return;
            bool rename = !string.IsNullOrEmpty(renamedFrom) && !string.IsNullOrEmpty(renamedTo) && renamedFrom != renamedTo;
            foreach (var graph in UnityEngine.Resources.FindObjectsOfTypeAll<SchematicGraph>().Where(g => g != null && EditorUtility.IsPersistent(g)))
            {
                bool changed = false;
                foreach (var node in graph.Nodes)
                {
                    bool uses = node is CallFunctionNode call ? call.Calls == function : (node is OnCalledNode || node is ReturnNode) && graph == function;
                    if (!uses || node is not IDynamicPorts dynamic)
                        continue;
                    if (!changed)
                        Undo.RecordObject(graph, "Edit Function");
                    changed = true;
                    node.Validate();
                    if (rename && node.GetPort(renamedTo) == null && node.GetPort(renamedFrom) is { } renamed && renamed.Connections.All(c => c.Port != null))
                        renamed.Name = renamedTo;
                    dynamic.SyncPorts();
                    node.Validate();
                }
                if (changed)
                {
                    EditorUtility.SetDirty(graph);
                    foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
                        if (window != null && window.SchematicGraph == graph && window.Canvas != null)
                            window.Canvas.Reload();
                }
            }
        }
    }
}
