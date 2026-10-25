using BlueGraph.Editor;
using Remedy.Schematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Variable nodes (Get and Set, a Behavior's or the graph's) look like the Signal nodes (IODock.uss), with the
/// Variable's ring: here, the ring and the node's border take the colour of the Variable's type (the wires' colours).
/// </summary>
[InitializeOnLoad]
internal static class VariableNodeLook
{
    static VariableNodeLook() => CanvasView.NodeViewAdded += Style;

    private static void Style(NodeView view)
    {
        var type = view?.Target switch
        {
            GetBehaviorVariableNode get => BehaviorReflection.Get(get.Target.Type)?.FindVariableType(get.Variable),
            SetBehaviorVariableNode set => BehaviorReflection.Get(set.Target.Type)?.FindVariableType(set.Variable),
            GraphVariableNodeBase graphVariable => graphVariable.Definition?.ValueType,
            _ => null,
        };
        bool variable = view?.Target is GetBehaviorVariableNode || view?.Target is SetBehaviorVariableNode || view?.Target is GraphVariableNodeBase;
        if (!variable)
            return;
        var color = DockRows.ColorOf(type);
        view.style.unityBackgroundImageTintColor = color;
        var border = view.Q("node-border");
        if (border != null)
        {
            var edge = new Color(color.r, color.g, color.b, 0.75f);
            border.style.borderTopColor = border.style.borderBottomColor = border.style.borderLeftColor = border.style.borderRightColor = edge;
        }
    }
}
