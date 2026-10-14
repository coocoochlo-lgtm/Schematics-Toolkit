using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [Node(Path = "Math"), Tags("Default")]
    public class MathOperation : SchematicGraphNode
    {
        public enum Operation
        {
            Add,
            Subtract,
            Multiply,
            Divide,
            Modulo,
            Minimum,
            Maximum,
            Power
        }

        [Input("A", Editable = true)]
        public float A;

        [Input("B", Editable = true)]
        public float B;

        [Output("Result")]
        public float Result;

        [Editable]
        public Operation Operator;

        public override object OnRequestValue(Port port)
        {
            var a = GetInputValue<float>(nameof(A), A);
            var b = GetInputValue<float>(nameof(B), B);

            switch (Operator)
            {
                case Operation.Add: Result = a + b; break;
                case Operation.Subtract: Result = a - b; break;
                case Operation.Multiply: Result = a * b; break;
                case Operation.Divide: Result = a / b; break;
                case Operation.Modulo: Result = a % b; break;
                case Operation.Minimum: Result = Mathf.Min(a, b); break;
                case Operation.Maximum: Result = Mathf.Max(a, b); break;
                case Operation.Power: Result = Mathf.Pow(a, b); break;
                default: throw new ArgumentOutOfRangeException(nameof(Operator), Operator, null);
            }

            return Result;
        }
    }
}
