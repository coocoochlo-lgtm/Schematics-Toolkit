using System.Reflection;
using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Every node gets a thin border in its colour, its [NodeColor] (IODock.uss centres its title). Variable nodes (Get and
/// Set, a Behavior's or the graph's) show the bar and ring of their IO Dock rows beside their name, and their border,
/// bar and ring take the colour of the Variable's type (the wires' colours). Signal nodes keep their own styling.
/// </summary>
[InitializeOnLoad]
internal static class NodeLook
{
    static NodeLook() => CanvasView.NodeViewAdded += Style;

    private static void Style(NodeView view)
    {
        var node = view?.Target;
        // (Signal nodes, and math nodes' glossy squares, are styled by their own views.)
        if (node == null || node is SendSignalNode || node is OnSignalReceivedNode || node is ISquareIconNode)
            return;

        bool variable = node is GetBehaviorVariableNode || node is SetBehaviorVariableNode || node is GraphVariableNodeBase;
        Color color;
        if (variable)
        {
            var type = node switch
            {
                GetBehaviorVariableNode get => BehaviorReflection.Get(get.Target.Type)?.FindVariableType(get.Variable),
                SetBehaviorVariableNode set => BehaviorReflection.Get(set.Target.Type)?.FindVariableType(set.Variable),
                GraphVariableNodeBase graphVariable => graphVariable.Definition?.ValueType,
                _ => null,
            };
            color = DockRows.ColorOf(type);
            // Its title bar a darker shade of its colour (the Schematic green, deepened).
            var titleColor = node.GetType().GetCustomAttribute<NodeColorAttribute>(true);
            if (titleColor != null)
                view.titleContainer.style.backgroundColor = Color.Lerp(titleColor.Color, Color.black, 0.35f);
            // Its bar and ring beside its name, as on its row in the IO Dock.
            var label = view.titleContainer.Q<Label>("title-label");
            if (label != null && view.titleContainer.Q(className: "node-variable-mark") == null)
            {
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("node-variable-mark");
                var chip = new VisualElement { pickingMode = PickingMode.Ignore };
                chip.style.width = 4;
                chip.style.height = 14;
                chip.style.marginRight = 4;
                chip.style.backgroundColor = color;
                mark.Add(chip);
                bool graphVariable = node is GraphVariableNodeBase;
                var ring = new Image { image = graphVariable ? DockRows.GraphVariableIcon : DockRows.VariableIcon, tintColor = color, pickingMode = PickingMode.Ignore };
                ring.style.width = ring.style.height = 16;
                mark.Add(ring);
                label.parent.Insert(label.parent.IndexOf(label), mark);
            }
        }
        else
        {
            var nodeColor = node.GetType().GetCustomAttribute<NodeColorAttribute>(true);
            color = nodeColor != null ? nodeColor.Color : new Color(0.78f, 0.78f, 0.78f);
        }

        // The body: flat, with the same gloss as the math squares (in place of its old gradient).
        var body = view.Q("contents");
        if (body != null)
        {
            body.style.backgroundImage = StyleKeyword.None;
            body.style.backgroundColor = NodeGloss.BodyColor;
            NodeGloss.Apply(body, 6f, NodeGloss.BodyShine, NodeGloss.BodyShade);
        }

        // The border in the node's colour (its title bar keeps its own).
        var border = view.Q("node-border");
        if (border != null)
        {
            var edge = new Color(color.r, color.g, color.b, 0.75f);
            border.style.borderTopColor = border.style.borderBottomColor = border.style.borderLeftColor = border.style.borderRightColor = edge;
        }
    }
}
