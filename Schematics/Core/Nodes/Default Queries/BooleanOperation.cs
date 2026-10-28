using BlueGraph;
using System;

namespace Remedy.Schematics
{
    [Serializable]
    [NodeColor(NodeColors.Math)]
    // Replaced by a node per gate (And, Or, Xor, Not in MathNodes.cs), like MathOperation.
    [Node(Name = "Boolean Operation (Old)", Path = "Logic"), Tags(MathOperation.LegacyTag)]
    public class BooleanOperation : SchematicGraphNode
    {
        public override void OnValidate()
        {
            base.OnValidate();
            Warning = "Old: each gate is its own node now (And, Or, Xor, Not). This one is replaced by it when the graph is opened.";
        }

        public override SchematicGraphNode Replacement()
        {
            SchematicGraphNode node;
            switch (Operator)
            {
                case Operation.Not:
                    node = new NotNode { In = A, Name = "Not" };
                    break;
                case Operation.Or:
                    node = new OrNode { A = A, B = B, Name = "Or" };
                    break;
                case Operation.ExclusiveOr:
                    node = new XorNode { A = A, B = B, Name = "Xor" };
                    break;
                default:
                    node = new AndNode { A = A, B = B, Name = "And" };
                    break;
            }
            node.AddPortsFromAttributes();
            return node;
        }

        // Not's In and Out were A and Result.
        public override string OldPortFor(string newPort) => newPort switch { "In" => "A", "Out" => "Result", _ => newPort };

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
