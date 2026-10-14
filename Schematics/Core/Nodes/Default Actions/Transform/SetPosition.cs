using BlueGraph;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Set Position"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects/Transform"", ""description"": ""Moves an object (or, with no Target connected, the object running the graph) to a position."", ""inputs"": [{""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Position"", ""type"": ""Vector3""}], ""use_for"": [""teleporting"", ""respawning"", ""placing objects""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
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
<<<<<<< HEAD
            // With nothing connected, it moves the object running the graph.
            Target = GetInputValue<GameObject>("Target", instance);
            if (Target == null)
                Target = instance;
            NewPosition = GetInputValue<Vector3>("Position", NewPosition);
           
=======
            // With nothing connected (or an empty object connected), it moves the object running the graph.
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            if (target == null)
                return;
>>>>>>> ef6557b7644f528f8dc0f5ffe08fe51715ac82f8

            // Read into locals rather than the fields: the fields are the inline values shown on the node, and writing
            // a connected value into them would replace what you typed there.
            target.transform.position = GetInputValue("Position", NewPosition);
        }
    }
}