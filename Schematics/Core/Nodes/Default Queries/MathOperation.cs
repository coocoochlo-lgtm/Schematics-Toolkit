using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [NodeColor(NodeColors.Math)]
    // Replaced by a node per operation (MathNodes.cs): kept so graphs saved with it still load and run, and swapped
    // for its operation's node when the graph is next opened.
    [Node(Name = "Math Operation (Old)", Path = "Math"), Tags(LegacyTag)]
    public class MathOperation : SchematicGraphNode
    {
        /// <summary>The tag of nodes kept only so old graphs load: no graph offers it, so they aren't in the node search.</summary>
        public const string LegacyTag = "Legacy";

        public override void OnValidate()
        {
            base.OnValidate();
            Warning = "Old: each operation is its own node now (Add, Multiply...). This one is replaced by it when the graph is opened.";
        }

        public override SchematicGraphNode Replacement()
        {
            BinaryMathNode node = Operator switch
            {
                Operation.Subtract => new MathSubtractNode(),
                Operation.Multiply => new MathMultiplyNode(),
                Operation.Divide => new MathDivideNode(),
                Operation.Modulo => new MathModuloNode(),
                Operation.Minimum => new MathMinNode(),
                Operation.Maximum => new MathMaxNode(),
                Operation.Power => new MathPowerNode(),
                _ => new MathAddNode(),
            };
            node.A = A;
            node.B = B;
            node.Name = node.Op.ToString();
            node.AddPortsFromAttributes();
            return node;
        }

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
