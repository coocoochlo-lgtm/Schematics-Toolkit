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
        [Editable]
        public bool Local;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null)
                throw new InvalidOperationException("Set Position requires a target GameObject.");

            var position = GetInputValue<Vector3>("Position", NewPosition);
            if (Local)
                target.transform.localPosition = position;
            else
                target.transform.position = position;
        }
    }
}