// project armada

#pragma warning disable 0414

using Remedy.Framework;
using BlueGraph;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""On Function"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events"", ""description"": ""Starts a flow when Call Function calls this graph with a matching Function Name; the call's parameters come out as outputs."", ""outputs"": [{""name"": ""FunctionParameters"", ""type"": ""List<FunctionParameter>""}], ""use_for"": [""reusable routines one graph asks another to run""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Pair with Call Function. Set Function Name to match.""}")]
    [Serializable]
    [Node(Path = "Events"), Tags("Object")]
    public class OnFunction : SchematicEventNode
    {
        [Editable]
        public string FunctionName;
        [Output]
        public List<FunctionParameter> FunctionParameters;

        protected override void OnTrigger(GameObject instance, params Union[] arguments)
        {
            // Called by Call Function with (name, parameters).
            if (arguments.Length == 0 || FunctionName != PortValues.ReferenceOf(arguments[0]) as string)
            {
                _blocked = true;
                return;
            }

            var parameters = arguments.Length > 1 ? PortValues.ReferenceOf(arguments[1]) as List<FunctionParameter> : null;
            SetOutputValue("FunctionParameters", parameters != null ? PortValues.ToUnion(parameters) : Union.Null);

            // Each parameter is also available by name, for nodes that read it that way.
            if (parameters != null)
                foreach (var parameter in parameters)
                    if (parameter != null && !string.IsNullOrEmpty(parameter.Name))
                        SetOutputValue(parameter.Name, parameter.Value);
        }
    }
}