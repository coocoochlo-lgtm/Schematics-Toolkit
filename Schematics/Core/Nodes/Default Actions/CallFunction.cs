// project armada

#pragma warning disable 0414

using Remedy.Framework;
using BlueGraph;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace Remedy.Schematics
{
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
            if(TargetGraph != null)
            {
                TargetGraph?.TriggerEvent<OnFunction>(instance, FunctionName, FunctionParameters.ToArray());
            }
        }
    }

    [Serializable]
    public class FunctionParameter
    {
        public string Name;
        public Union Value;
    }
}