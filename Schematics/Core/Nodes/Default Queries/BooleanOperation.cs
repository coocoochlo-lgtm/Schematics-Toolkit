using BlueGraph;
using System;

namespace Remedy.Schematics
{
    [Serializable]
    [Node(Path = "Logic"), Tags("Default")]
    public class BooleanOperation : SchematicGraphNode
    {
        public enum Operation
        {
            And,
            Or,
            ExclusiveOr,
            Not
        }

        [Input("A", Editable = true)]
        public bool A;

        [Input("B", Editable = true)]
        public bool B;

        [Output("Result")]
        public bool Result;

        [Editable]
        public Operation Operator;

        public override object OnRequestValue(Port port)
        {
            var a = GetInputValue<bool>(nameof(A), A);
            var b = GetInputValue<bool>(nameof(B), B);

            switch (Operator)
            {
                case Operation.And: Result = a && b; break;
                case Operation.Or: Result = a || b; break;
                case Operation.ExclusiveOr: Result = a ^ b; break;
                case Operation.Not: Result = !a; break;
                default: throw new ArgumentOutOfRangeException(nameof(Operator), Operator, null);
            }

            return Result;
        }
    }
}
