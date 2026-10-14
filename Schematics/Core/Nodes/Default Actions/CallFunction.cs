// project armada

#pragma warning disable 0414

using Remedy.Framework;
using BlueGraph;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Call Function"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Loops"", ""description"": ""Calls a function by name on another Schematic Graph (its On Function event), passing parameters."", ""inputs"": [{""name"": ""FunctionParameters"", ""type"": ""List<FunctionParameter>""}], ""use_for"": [""reusable routines"", ""one graph asking another to do something""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Pair with On Function in the target graph.""}")]
    [Serializable]
    [Node(Path = "Loops"), Tags("Default")]
    public class CallFunction : SchematicActionNode
    {
        [Tooltip("The Schematic Graph to call a Function on.")]
        [Editable]
        public SchematicGraph TargetGraph;

        [Editable]
        public string FunctionName;
        [Input]
        public List<FunctionParameter> FunctionParameters;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            if (TargetGraph == null)
                return;

            // On Function receives the name (to check it's the one being called) and the parameters list.
            var parameters = GetInputValue("FunctionParameters", FunctionParameters);
            TargetGraph.TriggerEvent<OnFunction>(instance, FunctionName, PortValues.ToUnion(parameters));
        }
    }

    [Serializable]
    public class FunctionParameter
    {
        public string Name;
        public Union Value;
    }
}