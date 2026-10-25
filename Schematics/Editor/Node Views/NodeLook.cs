using System.Reflection;
using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Every node takes the Variable nodes' look (IODock.uss): a gradient body with its title on it and a thin border in
/// the node's colour, its [NodeColor] (which used to fill the title bar). Variable nodes (Get and Set, a Behavior's or
/// the graph's) show the Variable's ring, and their ring and border take the colour of the Variable's type (the wires'
/// colours). Signal nodes keep their own styling.
/// </summary>
[InitializeOnLoad]
internal static class NodeLook
{
    static NodeLook() => CanvasView.NodeViewAdded += Style;

    private static void Style(NodeView view)
    {
        var node = view?.Target;
        if (node == null || node is SendSignalNode || node is OnSignalReceivedNode)
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
            view.style.unityBackgroundImageTintColor = color;
        }
        else
        {
            var nodeColor = node.GetType().GetCustomAttribute<NodeColorAttribute>(true);
            color = nodeColor != null ? nodeColor.Color : new Color(0.78f, 0.78f, 0.78f);
        }

        // The title on the body, not a bar of the node's colour: the colour goes to the border.
        view.titleContainer.style.backgroundColor = StyleKeyword.Null;
        var border = view.Q("node-border");
        if (border != null)
        {
            var edge = new Color(color.r, color.g, color.b, 0.75f);
            border.style.borderTopColor = border.style.borderBottomColor = border.style.borderLeftColor = border.style.borderRightColor = edge;
        }
    }
}
