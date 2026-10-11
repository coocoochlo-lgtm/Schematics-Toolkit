// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Is Null"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when its Value is nothing (a missing object or reference)."", ""inputs"": [{""name"": ""Value"", ""type"": ""object""}], ""use_for"": [""checking a target still exists"", ""guarding against missing references""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Path = "Conditions"), Tags("Default")]
    public class IsNull : SchematicConditionalNode
    {
        [Input("Value")]
        public object value;

        public override bool Condition()
        {
            var value = GetInputValue<object>("Value");
            return value == null;
        }
    }
}