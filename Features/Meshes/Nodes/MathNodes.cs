// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Meshes
{
    // Mesh Graph math, one node per operation (Add, Multiply, Sin...), the same set as logic graphs': small square nodes
    // with no title bar and an icon of what each does, values in on the left, the result out on the right. (Math, the
    // formula node, is still there for anything longer.)

    /// <summary>A Mesh Graph math node: one <see cref="MathOp"/>, drawn as a square with its icon.</summary>
    [Serializable]
    [NodeColor("#29A84D")]
    public abstract class MeshMathOpNode : MeshNode, ISquareIconNode
    {
        public abstract MathOp Op { get; }

        public string Symbol => MathOps.Symbol(Op);
        public Vector2 PlotRange => MathOps.PlotRange(Op);
        public float PlotB => MathOps.PlotB(Op);
        public bool IsFunction => MathOps.IsUnary(Op);
        public void Evaluate(float[] a, float[] b, float[] result, int count) => MathOps.Apply(Op, a, b, result, count);

        public override MeshOp Compile(MeshCompiler compiler) => MeshOp.Empty;
    }

    /// <summary>An operation on two values (A and B), out of Result.</summary>
    [Serializable]
    public abstract class MeshBinaryMathNode : MeshMathOpNode
    {
        [Input("A", Editable = true)] public float A;
        [Input("B", Editable = true)] public float B;
        [Output("Result")] public float Result;

        protected MeshBinaryMathNode() => B = MathOps.DefaultB(Op);

        public override object OnRequestValue(Port port)
        {
            var c = MeshCompiler.Current;
            return c == null ? MathOps.Apply(Op, A, B) : MathOps.Apply(Op, c.Number(this, "A", A), c.Number(this, "B", B));
        }
    }

    /// <summary>An operation on one value (In), out of Out.</summary>
    [Serializable]
    [NodeColor("#EDB51A")]
    public abstract class MeshUnaryMathNode : MeshMathOpNode
    {
        [Input("In", Editable = true)] public float In;
        [Output("Out")] public float Out;

        public override object OnRequestValue(Port port)
        {
            var c = MeshCompiler.Current;
            return MathOps.Apply(Op, c == null ? In : c.Number(this, "In", In));
        }
    }

    [SchematicDoc(@"{""name"": ""Add"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A plus B."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Add", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshAddNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Add;
    }

    [SchematicDoc(@"{""name"": ""Subtract"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A minus B."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Subtract", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshSubtractNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Subtract;
    }

    [SchematicDoc(@"{""name"": ""Multiply"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A times B: scales A (B of 0.5 halves it, 2 doubles it)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Multiply", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshMultiplyNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Multiply;
    }

    [SchematicDoc(@"{""name"": ""Divide"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A divided by B (0 where B is 0)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Divide", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshDivideNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Divide;
    }

    [SchematicDoc(@"{""name"": ""Modulo"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""What's left of A after taking out whole Bs, always 0 up to B: counts that loop (A modulo 4 goes 0, 1, 2, 3, 0...)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Modulo", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshModuloNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Modulo;
    }

    [SchematicDoc(@"{""name"": ""Power"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A to the power of B: B of 2 squares it; on a 0 to 1 value, it starts slow and finishes fast."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Power", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshPowerNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Power;
    }

    [SchematicDoc(@"{""name"": ""Min"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""The smaller of A and B: a ceiling on A."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Min", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshMinNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Min;
    }

    [SchematicDoc(@"{""name"": ""Max"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""The larger of A and B: a floor under A."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Max", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshMaxNode : MeshBinaryMathNode
    {
        public override MathOp Op => MathOp.Max;
    }

    [SchematicDoc(@"{""name"": ""Sin"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""The sine of In, in radians (a full wave every 2π, about 6.28): smooth back-and-forth from -1 to 1."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sin", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshSinNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Sin;
    }

    [SchematicDoc(@"{""name"": ""Cos"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""The cosine of In, in radians: like Sin, a quarter wave ahead."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Cos", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshCosNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Cos;
    }

    [SchematicDoc(@"{""name"": ""Abs"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""In without its sign (always 0 or more): how far it is from 0."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Abs", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshAbsNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Abs;
    }

    [SchematicDoc(@"{""name"": ""Floor"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""In rounded down to a whole number."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Floor", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshFloorNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Floor;
    }

    [SchematicDoc(@"{""name"": ""Ceil"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""In rounded up to a whole number."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Ceil", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshCeilNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Ceil;
    }

    [SchematicDoc(@"{""name"": ""Round"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""In rounded to the nearest whole number."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Round", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshRoundNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Round;
    }

    [SchematicDoc(@"{""name"": ""Fraction"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""The part of In after the point, looping 0 to 1."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Fraction", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshFractionNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Fraction;
    }

    [SchematicDoc(@"{""name"": ""Square Root"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""The square root of In (0 below 0)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Square Root", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshSquareRootNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.SquareRoot;
    }

    [SchematicDoc(@"{""name"": ""Negate"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""In with its sign flipped."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Negate", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshNegateNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Negate;
    }

    [SchematicDoc(@"{""name"": ""One Minus"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""1 minus In: turns a 0 to 1 amount into what's left of it (a fade in into a fade out)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "One Minus", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshOneMinusNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.OneMinus;
    }

    [SchematicDoc(@"{""name"": ""Clamp 0 to 1"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""In kept between 0 and 1."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Clamp 0 to 1", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshClamp01Node : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Clamp01;
    }

    [SchematicDoc(@"{""name"": ""Sign"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""1 where In is above 0, -1 below, 0 at 0."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sign", Path = "Values/Math"), Tags(MeshNode.Tag)]
    public sealed class MeshSignNode : MeshUnaryMathNode
    {
        public override MathOp Op => MathOp.Sign;
    }

}
