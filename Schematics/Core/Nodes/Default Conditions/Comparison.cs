// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Comparison"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""Compares two numbers (less than, equal to, greater than, ...) and outputs true or false. Its numbers can be typed in or connected."", ""inputs"": [{""name"": ""First Value"", ""type"": ""number""}, {""name"": ""Second Value"", ""type"": ""number""}], ""use_for"": [""health checks (health <= 0)"", ""distance checks"", ""score thresholds"", ""timers running out""], ""notes"": ""A true/false value: connect it to an If node's Condition. Pick the operator on the node; takes any numbers (int, float, double...). For a single comparison, Greater Than, Less Than, Equal etc. are separate nodes too.""}")]
    [Serializable]
    [Node(Name = "Comparison", Path = "Conditions"), Tags("Default")]
    public class CompaCastrison : SchematicConditionalNode
    {
        public enum ComparisonOperator
        {
            LessThan,
            LessThanOrEqualTo,
            EqualTo,
            GreaterThanOrEqualTo,
            GreaterThan,
            NotEqualTo
        }

        // The ports take any value (int, long, double, Union outputs...); the fields are what's typed in.
        [Input("First Value", typeof(object), Editable = true)]
        public float FirstValue;
        [Input("Second Value", typeof(object), Editable = true)]
        public float SecondValue;

        [Editable]
        public ComparisonOperator Operator;

        public override bool Condition()
        {
            // Connected numbers are read as they are (compared in the most precise type of the two); unconnected, the
            // numbers typed on the node (never overwritten).
            var order = NumberComparison.Compare(Read("First Value", FirstValue), Read("Second Value", SecondValue));
            switch (Operator)
            {
                case ComparisonOperator.LessThan: return order == NumberComparison.Order.Less;
                case ComparisonOperator.LessThanOrEqualTo: return order == NumberComparison.Order.Less || order == NumberComparison.Order.Equal;
                case ComparisonOperator.EqualTo: return order == NumberComparison.Order.Equal;
                case ComparisonOperator.GreaterThan: return order == NumberComparison.Order.Greater;
                case ComparisonOperator.GreaterThanOrEqualTo: return order == NumberComparison.Order.Greater || order == NumberComparison.Order.Equal;
                case ComparisonOperator.NotEqualTo: return order != NumberComparison.Order.Equal;
            }
            return false;
        }

        public override void OnValidate()
        {
            base.OnValidate();
            // Graphs saved before these took any number have float ports.
            EnsureDataPort("First Value", typeof(object), PortDirection.Input);
            EnsureDataPort("Second Value", typeof(object), PortDirection.Input);
        }

        private object Read(string port, float typed)
            => TryGetInputValue<object>(port, out var value) && NumberComparison.IsNumber(value) ? value : typed;
    }
}