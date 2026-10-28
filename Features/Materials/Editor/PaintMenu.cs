using BlueGraph;
using BlueGraph.Editor;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Materials
{
    /// <summary>
    /// Painting on any node of a Smart Material, from its right-click menu. "Paint Where This Applies" limits the
    /// node's effect to where you paint (a Layer over what came in, for a material; times the painted mask, for a
    /// mask). "Paint (input)" puts a Paint Mask on one of its mask inputs, over whatever was wired there, so a
    /// procedural mask can be touched up by hand. Either opens the painter.
    /// </summary>
    [InitializeOnLoad]
    internal static class PaintMenu
    {
        static PaintMenu()
        {
            NodeView.ExtendContextMenu -= AddToNodeMenu;
            NodeView.ExtendContextMenu += AddToNodeMenu;
        }

        private static void AddToNodeMenu(NodeView view, ContextualMenuPopulateEvent evt)
        {
            var canvas = view.GetFirstAncestorOfType<CanvasView>();
            if (canvas == null || canvas.Graph is not SmartMaterialGraph graph || view.Target is not MaterialNode node || node is MaterialOutputNode)
                return;

            if (node is PaintedNode painted)
                evt.menu.AppendAction("Paint...", _ => PaintWindow.Open(graph, painted));

            // Where the node applies.
            var materialOut = Output(node, typeof(MaterialMaps));
            var maskOut = Output(node, typeof(MaskMap));
            if (materialOut != null)
            {
                var under = MaterialComingIn(node);
                evt.menu.AppendAction(under != null ? "Paint Where This Applies" : "Paint Where This Applies (needs a material wired in)",
                    _ => PaintMaterial(canvas, graph, node, materialOut, under),
                    under != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
            else if (maskOut != null && node is not PaintMaskNode)
                evt.menu.AppendAction("Paint Where This Applies", _ => PaintMask(canvas, graph, node, maskOut));

            // Each mask input.
            int index = 0;
            foreach (var port in node.Ports.Values.Where(p => p.Direction == PortDirection.Input && p.Type == typeof(MaskMap)).ToList())
            {
                int slot = index++;
                if (node is PaintMaskNode)
                    continue;
                evt.menu.AppendAction($"Paint {port.Name}", _ => PaintInput(canvas, graph, node, port, slot));
            }
        }

        private static Port Output(Node node, System.Type type)
            => node.Ports.Values.FirstOrDefault(p => p.Direction == PortDirection.Output && p.Type == type);

        // The material this node works on (what's wired into its In or Base), which its effect is painted over.
        private static Port MaterialComingIn(Node node)
        {
            foreach (var name in new[] { "In", "Base" })
            {
                var port = node.GetPort(name);
                if (port != null && port.Type == typeof(MaterialMaps))
                    foreach (var source in port.ConnectedPorts)
                        if (source != null)
                            return source;
            }
            return null;
        }

        // A material node: a Layer over what came in, with it on top through a Paint Mask; what it fed now takes the Layer.
        private static void PaintMaterial(CanvasView canvas, SmartMaterialGraph graph, MaterialNode node, Port output, Port under)
        {
            Undo.RegisterCompleteObjectUndo(graph, "Paint Where This Applies");
            var feeds = output.ConnectedPorts.Where(p => p != null).ToList();
            var layer = NewNode<LayerMaterialNode>($"{node.Name} (Painted)", node.Position + new Vector2(300f, 0f));
            var paint = NewNode<PaintMaskNode>($"Where {node.Name} Goes", node.Position + new Vector2(0f, 260f));
            paint.In = 0f;
            graph.AddNode(layer);
            graph.AddNode(paint);
            foreach (var target in feeds)
            {
                graph.RemoveEdge(output, target);
                graph.AddEdge(layer.GetPort(MaterialNode.MaterialPort), target);
            }
            graph.AddEdge(under, layer.GetPort("Base"));
            graph.AddEdge(output, layer.GetPort("Top"));
            graph.AddEdge(paint.GetPort(MaterialNode.MaskPort), layer.GetPort("Mask"));
            Finish(canvas, graph, paint);
        }

        // A mask node: times a Paint Mask; what it fed now takes the product.
        private static void PaintMask(CanvasView canvas, SmartMaterialGraph graph, MaterialNode node, Port output)
        {
            Undo.RegisterCompleteObjectUndo(graph, "Paint Where This Applies");
            var feeds = output.ConnectedPorts.Where(p => p != null).ToList();
            var blend = NewNode<MaterialMultiplyNode>($"{node.Name} (Painted)", node.Position + new Vector2(300f, 0f));
            var paint = NewNode<PaintMaskNode>($"Where {node.Name} Goes", node.Position + new Vector2(0f, 260f));
            paint.In = 0f;
            graph.AddNode(blend);
            graph.AddNode(paint);
            foreach (var target in feeds)
            {
                graph.RemoveEdge(output, target);
                graph.AddEdge(blend.GetPort(MaterialMathNode.ResultPort), target);
            }
            graph.AddEdge(output, blend.GetPort("A"));
            graph.AddEdge(paint.GetPort(MaterialNode.MaskPort), blend.GetPort("B"));
            Finish(canvas, graph, paint);
        }

        // A mask input: a Paint Mask over whatever was wired there (or the number typed on it).
        private static void PaintInput(CanvasView canvas, SmartMaterialGraph graph, MaterialNode node, Port input, int slot)
        {
            Undo.RegisterCompleteObjectUndo(graph, $"Paint {input.Name}");
            var sources = input.ConnectedPorts.Where(p => p != null).ToList();
            var paint = NewNode<PaintMaskNode>($"{node.Name} {input.Name} (Painted)", node.Position + new Vector2(-300f, slot * 180f));
            // Unwired, it was the number typed on it: the painting starts from that.
            var field = NodeReflection.GetNodeType(node.GetType())?.GetPortByName(input.Name)?.Field;
            paint.In = sources.Count == 0 && field != null && field.FieldType == typeof(float) ? (float)field.GetValue(node) : 0f;
            graph.AddNode(paint);
            foreach (var source in sources)
            {
                graph.RemoveEdge(source, input);
                graph.AddEdge(source, paint.GetPort("In"));
            }
            graph.AddEdge(paint.GetPort(MaterialNode.MaskPort), input);
            Finish(canvas, graph, paint);
        }

        private static void Finish(CanvasView canvas, SmartMaterialGraph graph, PaintMaskNode paint)
        {
            EditorUtility.SetDirty(graph);
            graph.Touch();
            canvas.Reload();
            PaintWindow.Open(graph, paint);
        }

        private static T NewNode<T>(string name, Vector2 position) where T : Node
        {
            var node = (T)NodeReflection.GetNodeType(typeof(T)).CreateInstance();
            node.Name = name;
            node.Position = position;
            return node;
        }
    }
}
