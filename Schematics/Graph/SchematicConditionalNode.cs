// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;

namespace Remedy.Schematics
{
    [Output("▶", typeof(bool))]
    [NodeColor(NodeColors.Condition)]
    public class SchematicConditionalNode : SchematicGraphNode
    {
        [Tooltip("Inverts the value of the Condition")]
        [Editable]
        public bool Not = false;

        public override object OnRequestValue(Port port)
        {
            if(port.Name == "▶")
                return Condition() ^ Not;
            return null;
        }

        public virtual bool Condition()
        {
            return true;
        }
    }
}