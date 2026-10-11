using BlueGraph;
using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Broadcast Method Invocation"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects"", ""description"": ""Calls a method by name, with arguments, on every script of a target object."", ""inputs"": [{""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Name"", ""type"": ""string""}, {""name"": ""Arguments"", ""type"": ""List<object>""}], ""use_for"": [""talking to plain Unity scripts that aren't Schematic Behaviors""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Prefer Signals for Schematic Behaviors.""}")]
    [Serializable]
    [Node(Path = "Objects"), Tags("Object")]
    public class BroadcastMethodInvocation : SchematicActionNode
    {
        [Input("Target")]
        public GameObject Target;
        [Input("Name")]
        public string MethodName;
        [Input("Arguments")]
        public List<object> MethodArguments;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            Target = GetOutputValue<GameObject>("Target");
            MethodName = GetOutputValue<string>("Name");
            MethodArguments = GetOutputValue<List<object>>("Arguments");

            var components = Target.GetComponents<MonoBehaviour>();
           
            foreach(var component in components)
            {
                component.CallMethod(MethodName, MethodArguments.ToArray());
            }
        }
    }
} 