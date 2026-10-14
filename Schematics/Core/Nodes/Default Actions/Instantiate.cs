using BlueGraph;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Instantiate"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects"", ""description"": ""Creates a copy of a prefab at a position and rotation, and outputs the new object."", ""inputs"": [{""name"": ""Position"", ""type"": ""Vector3""}, {""name"": ""Rotation"", ""type"": ""Vector3""}, {""name"": ""Prefab"", ""type"": ""GameObject""}], ""outputs"": [{""name"": ""Instanced Object"", ""type"": ""GameObject""}], ""use_for"": [""spawning enemies, bullets, pickups and effects""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Uses the pool for Schematic prefabs.""}")]
    [Serializable]
    [Node(Path = "Objects"), Tags("Default")] 
    public class Instantiate : SchematicActionNode
    {
        [Input("Position")]
        public Vector3 InitPosition;
        [Input("Rotation")]
        public Vector3 Rotation;
        [Input("Prefab")]
        public GameObject Prefab;

        [Output("Instanced Object")]
        public GameObject InstancedObject;
        
        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var prefab = GetInputValue("Prefab", Prefab);
            if (prefab == null)
                throw new InvalidOperationException("Instantiate has no Prefab: connect one, or pick one on the node.");

            var position = GetInputValue("Position", InitPosition);
            var rotation = GetInputValue("Rotation", Rotation);
            SetOutputValue("Instanced Object", ObjectManager.Instantiate(prefab, position, Quaternion.Euler(rotation)));
        }
    }
}