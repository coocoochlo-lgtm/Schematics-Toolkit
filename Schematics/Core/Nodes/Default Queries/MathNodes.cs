using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    // Logic graph math, one node per operation (Add, Multiply, Sin...) and per logic gate (And, Or, Xor, Not): small
    // square nodes with no title bar and an icon of what each does (its symbol, or a plot of its curve), values in on
    // the left, the result out on the right.

    /// <summary>A logic graph math node: one <see cref="MathOp"/>, drawn as a square with its icon.</summary>
    [Serializable]
    [NodeColor("#29A84D")]
    public abstract class MathNode : SchematicGraphNode, ISquareIconNode
    {
        public abstract MathOp Op { get; }

        public string Symbol => MathOps.Symbol(Op);
        public Vector2 PlotRange => MathOps.PlotRange(Op);
        public float PlotB => MathOps.PlotB(Op);
        public bool IsFunction => MathOps.IsUnary(Op);
        public void Evaluate(float[] a, float[] b, float[] result, int count) => MathOps.Apply(Op, a, b, result, count);
    }

    /// <summary>An operation on two values (A and B), out of Result.</summary>
    [Serializable]
    public abstract class BinaryMathNode : MathNode
    {
        [Input("A", Editable = true)] public float A;
        [Input("B", Editable = true)] public float B;
        [Output("Result")] public float Result;

        protected BinaryMathNode() => B = MathOps.DefaultB(Op);

        public override object OnRequestValue(Port port)
            => Result = MathOps.Apply(Op, GetInputValue<float>(nameof(A), A), GetInputValue<float>(nameof(B), B));
    }

    /// <summary>An operation on one value (In), out of Out.</summary>
    [Serializable]
    [NodeColor("#EDB51A")]
    public abstract class UnaryMathNode : MathNode
    {
        [Input("In", Editable = true)] public float In;
        [Output("Out")] public float Out;

        public override object OnRequestValue(Port port) => Out = MathOps.Apply(Op, GetInputValue<float>(nameof(In), In));
    }

    [SchematicDoc(@"{""name"": ""Add"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""A plus B."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Add", Path = "Math"), Tags("Default")]
    public sealed class MathAddNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Add;
    }

    [SchematicDoc(@"{""name"": ""Subtract"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""A minus B."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Subtract", Path = "Math"), Tags("Default")]
    public sealed class MathSubtractNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Subtract;
    }

    [SchematicDoc(@"{""name"": ""Multiply"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""A times B: scales A (B of 0.5 halves it, 2 doubles it)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Multiply", Path = "Math"), Tags("Default")]
    public sealed class MathMultiplyNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Multiply;
    }

    [SchematicDoc(@"{""name"": ""Divide"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""A divided by B (0 where B is 0)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Divide", Path = "Math"), Tags("Default")]
    public sealed class MathDivideNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Divide;
    }

    [SchematicDoc(@"{""name"": ""Modulo"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""What's left of A after taking out whole Bs, always 0 up to B: counts that loop (A modulo 4 goes 0, 1, 2, 3, 0...)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Modulo", Path = "Math"), Tags("Default")]
    public sealed class MathModuloNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Modulo;
    }

    [SchematicDoc(@"{""name"": ""Power"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""A to the power of B: B of 2 squares it; on a 0 to 1 value, it starts slow and finishes fast."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Power", Path = "Math"), Tags("Default")]
    public sealed class MathPowerNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Power;
    }

    [SchematicDoc(@"{""name"": ""Min"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""The smaller of A and B: a ceiling on A."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Min", Path = "Math"), Tags("Default")]
    public sealed class MathMinNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Min;
    }

    [SchematicDoc(@"{""name"": ""Max"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""The larger of A and B: a floor under A."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Max", Path = "Math"), Tags("Default")]
    public sealed class MathMaxNode : BinaryMathNode
    {
        public override MathOp Op => MathOp.Max;
    }

    [SchematicDoc(@"{""name"": ""Sin"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""The sine of In, in radians (a full wave every 2π, about 6.28): smooth back-and-forth from -1 to 1."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sin", Path = "Math"), Tags("Default")]
    public sealed class MathSinNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Sin;
    }

    [SchematicDoc(@"{""name"": ""Cos"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""The cosine of In, in radians: like Sin, a quarter wave ahead."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Cos", Path = "Math"), Tags("Default")]
    public sealed class MathCosNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Cos;
    }

    [SchematicDoc(@"{""name"": ""Abs"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""In without its sign (always 0 or more): how far it is from 0."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Abs", Path = "Math"), Tags("Default")]
    public sealed class MathAbsNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Abs;
    }

    [SchematicDoc(@"{""name"": ""Floor"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""In rounded down to a whole number."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Floor", Path = "Math"), Tags("Default")]
    public sealed class MathFloorNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Floor;
    }

    [SchematicDoc(@"{""name"": ""Ceil"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""In rounded up to a whole number."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Ceil", Path = "Math"), Tags("Default")]
    public sealed class MathCeilNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Ceil;
    }

    [SchematicDoc(@"{""name"": ""Round"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""In rounded to the nearest whole number."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Round", Path = "Math"), Tags("Default")]
    public sealed class MathRoundNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Round;
    }

    [SchematicDoc(@"{""name"": ""Fraction"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""The part of In after the point, looping 0 to 1."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Fraction", Path = "Math"), Tags("Default")]
    public sealed class MathFractionNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Fraction;
    }

    [SchematicDoc(@"{""name"": ""Square Root"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""The square root of In (0 below 0)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Square Root", Path = "Math"), Tags("Default")]
    public sealed class MathSquareRootNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.SquareRoot;
    }

    [SchematicDoc(@"{""name"": ""Negate"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""In with its sign flipped."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Negate", Path = "Math"), Tags("Default")]
    public sealed class MathNegateNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Negate;
    }

    [SchematicDoc(@"{""name"": ""One Minus"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""1 minus In: turns a 0 to 1 amount into what's left of it (a fade in into a fade out)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "One Minus", Path = "Math"), Tags("Default")]
    public sealed class MathOneMinusNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.OneMinus;
    }

    [SchematicDoc(@"{""name"": ""Clamp 0 to 1"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""In kept between 0 and 1."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Clamp 0 to 1", Path = "Math"), Tags("Default")]
    public sealed class MathClamp01Node : UnaryMathNode
    {
        public override MathOp Op => MathOp.Clamp01;
    }

    [SchematicDoc(@"{""name"": ""Sign"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Math"", ""description"": ""1 where In is above 0, -1 below, 0 at 0."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sign", Path = "Math"), Tags("Default")]
    public sealed class MathSignNode : UnaryMathNode
    {
        public override MathOp Op => MathOp.Sign;
    }

    /// <summary>A logic gate (And, Or, Xor, Not), drawn as a square with its word.</summary>
    [Serializable]
    [NodeColor("#29A84D")]
    public abstract class BooleanNode : SchematicGraphNode, ISquareIconNode
    {
        public abstract string Symbol { get; }
        public Vector2 PlotRange => new(-1f, 1f);
        public float PlotB => 0f;
        public bool IsFunction => false;
        public void Evaluate(float[] a, float[] b, float[] result, int count) { }
    }

    /// <summary>A gate on two values (A and B), out of Result.</summary>
    [Serializable]
    public abstract class BinaryBooleanNode : BooleanNode
    {
        [Input("A", Editable = true)] public bool A;
        [Input("B", Editable = true)] public bool B;
        [Output("Result")] public bool Result;

        protected abstract bool Apply(bool a, bool b);

        public override object OnRequestValue(Port port)
            => Result = Apply(GetInputValue<bool>(nameof(A), A), GetInputValue<bool>(nameof(B), B));
    }

    [SchematicDoc(@"{""name"": ""And"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Logic"", ""description"": ""True when A and B are both true."", ""inputs"": [{""name"": ""A"", ""type"": ""bool""}, {""name"": ""B"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Result"", ""type"": ""bool""}]}")]
    [Serializable]
    [Node(Name = "And", Path = "Logic"), Tags("Default")]
    public sealed class AndNode : BinaryBooleanNode
    {
        public override string Symbol => "and";
        protected override bool Apply(bool a, bool b) => a && b;
    }

    [SchematicDoc(@"{""name"": ""Or"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Logic"", ""description"": ""True when A or B (or both) is true."", ""inputs"": [{""name"": ""A"", ""type"": ""bool""}, {""name"": ""B"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Result"", ""type"": ""bool""}]}")]
    [Serializable]
    [Node(Name = "Or", Path = "Logic"), Tags("Default")]
    public sealed class OrNode : BinaryBooleanNode
    {
        public override string Symbol => "or";
        protected override bool Apply(bool a, bool b) => a || b;
    }

    [SchematicDoc(@"{""name"": ""Xor"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Logic"", ""description"": ""True when exactly one of A and B is true (not both)."", ""inputs"": [{""name"": ""A"", ""type"": ""bool""}, {""name"": ""B"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Result"", ""type"": ""bool""}]}")]
    [Serializable]
    [Node(Name = "Xor", Path = "Logic"), Tags("Default")]
    public sealed class XorNode : BinaryBooleanNode
    {
        public override string Symbol => "xor";
        protected override bool Apply(bool a, bool b) => a ^ b;
    }

    [SchematicDoc(@"{""name"": ""Not"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Logic"", ""description"": ""The opposite of In: true when it's false."", ""inputs"": [{""name"": ""In"", ""type"": ""bool""}], ""outputs"": [{""name"": ""Out"", ""type"": ""bool""}]}")]
    [Serializable]
    [Node(Name = "Not", Path = "Logic"), Tags("Default")]
    public sealed class NotNode : BooleanNode
    {
        [Input("In", Editable = true)] public bool In;
        [Output("Out")] public bool Out;

        public override string Symbol => "not";

        public override object OnRequestValue(Port port) => Out = !GetInputValue<bool>(nameof(In), In);
    }
}
