using BlueGraph.Editor;
using UnityEngine.UIElements;

[CustomNodeView(typeof(SendSignalNode))]
public class SendSignalNodeView : NodeView
{
    protected override void OnInitialize()
    {
        base.OnInitialize();
        AddToClassList("send-signal-node");
    }
}
