using BlueGraph;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [Serializable]
    [Node(Path = "Objects/Transform"), Tags("Object")]
    public class SetPosition : SchematicActionNode
    {
        [Input("Target")]
        public GameObject Target;
        [Input("Position")]
        public Vector3 NewPosition;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            // With nothing connected, it moves the object running the graph.
            Target = GetInputValue<GameObject>("Target", instance);
            if (Target == null)
                Target = instance;
            NewPosition = GetInputValue<Vector3>("Position", NewPosition);

            // (This used to write Node.Position, the node's place on the canvas, and lost the Z axis.)
            Target.transform.position = NewPosition;
        }
    }
}