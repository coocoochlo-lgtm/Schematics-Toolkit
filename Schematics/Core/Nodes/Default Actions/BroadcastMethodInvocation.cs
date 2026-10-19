using BlueGraph;
using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;
using System.Collections;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Broadcast Method Invocation"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects"", ""description"": ""Calls a method by name, with arguments, on every script of a target object."", ""inputs"": [{""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Name"", ""type"": ""string""}, {""name"": ""Arguments"", ""type"": ""List<object>""}], ""use_for"": [""talking to plain Unity scripts that aren't Schematic Behaviors""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Prefer Signals for Schematic Behaviors.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
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
            // (These used to be read with GetOutputValue on input names, which never found anything.)
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            string methodName = GetInputValue("Name", MethodName);
            if (target == null || string.IsNullOrEmpty(methodName))
                return;

            var list = GetInputValue<IList>("Arguments", MethodArguments);
            var arguments = new object[list != null ? list.Count : 0];
            for (int i = 0; i < arguments.Length; i++)
                arguments[i] = list[i] is Union union ? SignalArgs.ToObject(union) : list[i];

            foreach (var component in target.GetComponents<MonoBehaviour>())
                if (component != null)
                    component.CallMethod(methodName, arguments);
        }
    }
} 