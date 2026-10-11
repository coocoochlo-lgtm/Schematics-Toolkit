// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Comparison"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""Compares two numbers (less than, equal to, greater than, ...) and outputs true or false. Its numbers can be typed in or connected."", ""inputs"": [{""name"": ""First Value"", ""type"": ""float""}, {""name"": ""Second Value"", ""type"": ""float""}], ""use_for"": [""health checks (health <= 0)"", ""distance checks"", ""score thresholds"", ""timers running out""], ""notes"": ""A true/false value: connect it to an If node's Condition. Pick the operator on the node. Feed its output into If.""}")]
    [Serializable]
    [Node(Path = "Conditions"), Tags("Default")]
    public class CompaCastrison : SchematicConditionalNode
    {
        public enum ComparisonOperator
        {
            LessThan,
            LessThanOrEqualTo,
            EqualTo,
            GreaterThanOrEqualTo,
            GreaterThan
        }

        [Input("First Value", Editable = true)]
        public float FirstValue;
        [Input("Second Value", Editable = true)]
        public float SecondValue;

        public ComparisonOperator Operator;

        public override bool Condition()
        {
            if (GetPort("First Value").ConnectionCount != 0)
                FirstValue = GetInputValue<float>("First Value");

            if (GetPort("Second Value").ConnectionCount != 0)
                SecondValue = GetInputValue<float>("Second Value");

            switch(Operator)
            {
                case ComparisonOperator.LessThan: return FirstValue < SecondValue;
                case ComparisonOperator.LessThanOrEqualTo: return FirstValue <= SecondValue;
                case ComparisonOperator.EqualTo: return FirstValue == SecondValue;
                case ComparisonOperator.GreaterThan: return FirstValue > SecondValue;
                case ComparisonOperator.GreaterThanOrEqualTo: return FirstValue >= SecondValue;
            }
            return false;
        }
    }
}