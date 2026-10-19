using BlueGraph;
using BlueGraph.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// The view of nodes with numbered flow outputs (Random, Split, Switch...): their pins in number order, with + and −
    /// buttons under them to add a pin or remove the last one (and its wires). Both can be undone.
    /// </summary>
    [CustomNodeView(typeof(DynamicFlowNode))]
    public class DynamicFlowNodeView : NodeView
    {
        private Button _add;
        private Button _remove;

        protected override void OnInitialize()
        {
            base.OnInitialize();
            var node = (DynamicFlowNode)Target;

            // Numbered pins first, in order; fixed outputs (Switch's Default) after them.
            int count = node.PinCount;
            for (int i = count - 1; i >= 0; i--)
                GetOutputPort(node.PinName(i))?.SendToBack();

            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, paddingRight = 4, paddingTop = 2, paddingBottom = 2 } };
            _remove = new Button(() => Change(add: false)) { text = "−", tooltip = "Remove the last output (and its wires)" };
            _add = new Button(() => Change(add: true)) { text = "+", tooltip = "Add an output" };
            foreach (var button in new[] { _remove, _add })
            {
                button.style.width = 20;
                button.style.height = 16;
                button.style.paddingLeft = button.style.paddingRight = 0;
                button.style.marginLeft = 2;
                button.style.marginRight = 0;
                row.Add(button);
            }
            outputContainer.Add(row);
            Refresh();
        }

        private void Refresh()
        {
            var node = (DynamicFlowNode)Target;
            _add.SetEnabled(node.CanAddPin);
            _remove.SetEnabled(node.CanRemovePin);
        }

        private void Change(bool add)
        {
            var node = (DynamicFlowNode)Target;
            var graph = node.Graph;
            if (graph != null)
                Undo.RegisterCompleteObjectUndo(graph, add ? "Add Output" : "Remove Output");
            if (add)
                node.AddPin();
            else
                node.RemovePin();
            node.UpdateCaches();
            if (graph != null)
                EditorUtility.SetDirty(graph);

            // Rebuild the canvas from the graph, so the new pin (or the removed one's wires) show. After this click is
            // handled: the rebuild replaces this view.
            var canvas = Canvas;
            canvas?.schedule.Execute(canvas.Reload);
        }
    }
}
