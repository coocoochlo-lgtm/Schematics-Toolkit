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
            if(awaiting)
            {
                //await UniTask.WaitForEndOfFrame();

                InitPosition = GetInputValue<Vector3>("Position", InitPosition);
                Rotation = GetInputValue<Vector3>("Rotation", Rotation);
                Prefab = GetInputValue<GameObject>("Prefab", Prefab);

                SetOutputValue(nameof(InstancedObject), ObjectManager.Instantiate(Prefab, InitPosition, Quaternion.Euler(Rotation)));

                return;
            }
            else
            {
                InitPosition = GetInputValue<Vector3>("Position");
                Rotation = GetInputValue<Vector3>("Rotation");
                Prefab = GetInputValue<GameObject>("Prefab");

                SetOutputValue(nameof(InstancedObject), ObjectManager.Instantiate(Prefab, InitPosition, Quaternion.Euler(Rotation)));
            }
        }

        public override object OnRequestValue(Port port)
        {
            if (InstancedObject == null)
                return Prefab;
            return InstancedObject;
        }
    }
}