// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Linq;
using UnityEngine;

namespace Remedy.Materials
{
    // Math in a Smart Material: the same Add, Multiply, Screen... as everywhere else, on whatever's wired in. Masks,
    // materials, colours and numbers all go through one node: a mask times a number scales the mask, a mask times a
    // colour paints the mask in that colour (a material), a material times a mask darkens its colour where the mask
    // is dark. Each node's Result pin takes the type of what it makes, so it wires on to wherever that fits.

    /// <summary>Masks and materials as kinds of value math can use (see <see cref="MathValues"/>).</summary>
    public static class MaterialMath
    {
        static MaterialMath()
        {
            MathValues.Register(new MaskKind());
            MathValues.Register(new MaterialKind());
        }

        /// <summary>Makes sure math knows masks and materials (call before using it on them).</summary>
        public static void Ensure() { }

        // A value's number at a texel (a mask's, or a number's everywhere; a vector's or colour's first channel).
        private static Func<int, float> Scalar(object value) => value switch
        {
            MaskMap mask => i => mask.Values[i],
            MaterialMaps maps => i => maps.BaseColor[i].grayscale,
            float f => _ => f,
            int n => _ => n,
            bool b => _ => b ? 1f : 0f,
            Vector2 v => _ => v.x,
            Vector3 v => _ => v.x,
            Vector4 v => _ => v.x,
            Color c => _ => c.grayscale,
            _ => _ => 0f,
        };

        // A value's colour at a texel (a mask's grey, a material's base colour, a vector as RGB).
        private static Func<int, Color> Colour(object value) => value switch
        {
            MaskMap mask => i => { float v = mask.Values[i]; return new Color(v, v, v, 1f); },
            MaterialMaps maps => i => maps.BaseColor[i],
            Color c => _ => c,
            Vector3 v => _ => new Color(v.x, v.y, v.z, 1f),
            Vector4 v => _ => new Color(v.x, v.y, v.z, 1f),
            Vector2 v => _ => new Color(v.x, v.y, 0f, 1f),
            float f => _ => new Color(f, f, f, 1f),
            int n => _ => new Color(n, n, n, 1f),
            bool b => _ => b ? Color.white : Color.black,
            _ => _ => Color.black,
        };

        private static Color CombineColour(MathOp op, Color a, Color b)
            => new(MathOps.Apply(op, a.r, b.r), MathOps.Apply(op, a.g, b.g), MathOps.Apply(op, a.b, b.b), 1f);

        private static bool IsColourLike(Type type) => type == typeof(Color) || type == typeof(Vector3) || type == typeof(Vector4);

        /// <summary>
        /// Masks: worked texel by texel. With a number, a mask; with a colour (or a 3D vector), the mask painted in it:
        /// a material, coloured by the mask.
        /// </summary>
        private sealed class MaskKind : MathKind
        {
            public override int Rank => 10;

            public override bool Handles(Type type) => type == typeof(MaskMap);

            public override Type ResultType(Type a, Type b)
            {
                var other = a == typeof(MaskMap) ? b : a;
                if (other == typeof(MaskMap))
                    return typeof(MaskMap);
                var kind = MathValues.KindOf(other);
                if (kind == null)
                    return null;
                return IsColourLike(other) ? typeof(MaterialMaps) : typeof(MaskMap);
            }

            public override object Combine(MathOp op, object a, object b)
            {
                var mask = a as MaskMap ?? (MaskMap)b;
                var other = a is MaskMap ? b : a;
                if (IsColourLike(other.GetType()))
                {
                    var ca = Colour(a);
                    var cb = Colour(b);
                    var maps = MaterialMaps.Default(mask.Width, mask.Height);
                    maps.ForEach(i => maps.BaseColor[i] = CombineColour(op, ca(i), cb(i)));
                    return maps;
                }
                var sa = Scalar(a);
                var sb = Scalar(b);
                var result = new MaskMap(mask.Width, mask.Height);
                result.Fill(i => MathOps.Apply(op, sa(i), sb(i)));
                return result;
            }

            public override object Apply(MathOp op, object value)
            {
                var mask = (MaskMap)value;
                var result = new MaskMap(mask.Width, mask.Height);
                result.Fill(i => MathOps.Apply(op, mask.Values[i]));
                return result;
            }
        }

        /// <summary>Materials: math works on their colour (roughness, metallic and height stay as the material's).</summary>
        private sealed class MaterialKind : MathKind
        {
            public override int Rank => 20;

            public override bool Handles(Type type) => type == typeof(MaterialMaps);

            public override Type ResultType(Type a, Type b)
            {
                var other = a == typeof(MaterialMaps) ? b : a;
                return other == typeof(MaterialMaps) || MathValues.KindOf(other) != null ? typeof(MaterialMaps) : null;
            }

            public override object Combine(MathOp op, object a, object b)
            {
                var source = a as MaterialMaps ?? (MaterialMaps)b;
                var ca = Colour(a);
                var cb = Colour(b);
                var maps = source.Clone();
                maps.ForEach(i => maps.BaseColor[i] = CombineColour(op, ca(i), cb(i)));
                return maps;
            }

            public override object Apply(MathOp op, object value)
            {
                var source = (MaterialMaps)value;
                var maps = source.Clone();
                maps.ForEach(i =>
                {
                    var c = source.BaseColor[i];
                    maps.BaseColor[i] = new Color(MathOps.Apply(op, c.r), MathOps.Apply(op, c.g), MathOps.Apply(op, c.b), 1f);
                });
                return maps;
            }
        }
    }

    /// <summary>
    /// A math node in a Smart Material: one <see cref="MathOp"/> on any kind of value, drawn as a square with its icon.
    /// Its Result pin's type follows what's wired in.
    /// </summary>
    [Serializable]
    [NodeColor("#29A84D")]
    public abstract class MaterialMathNode : MaterialNode, ISquareIconNode
    {
        public const string ResultPort = "Result";

        public abstract MathOp Op { get; }

        public virtual string Symbol => MathOps.Symbol(Op);
        public Vector2 PlotRange => MathOps.PlotRange(Op);
        public float PlotB => MathOps.PlotB(Op);
        public bool IsFunction => MathOps.IsUnary(Op);
        public void Evaluate(float[] a, float[] b, float[] result, int count) => MathOps.Apply(Op, a, b, result, count);

        /// <summary>Its value inputs, in order.</summary>
        protected abstract string[] Inputs { get; }

        /// <summary>The type its result has, from the types wired into <paramref name="inputs"/> (null: they don't go together).</summary>
        protected virtual Type ResultFor(Type[] inputs)
        {
            var type = inputs[0];
            for (int i = 1; i < inputs.Length; i++)
                type = type == null ? null : MathValues.ResultType(type, inputs[i]);
            return inputs.Length == 1 ? MathValues.ResultType(type) : type;
        }

        // What's wired into an input: its source pin's type, or a number when nothing is.
        private Type WiredType(string name)
        {
            var port = GetPort(name);
            var source = port?.ConnectedPorts.FirstOrDefault(p => p != null);
            return source?.Type ?? typeof(float);
        }

        [NonSerialized] private bool _resolving;

        /// <summary>Sets the Result pin's type from what's wired in, and passes the change on to what it feeds.</summary>
        public void ResolveTypes()
        {
            MaterialMath.Ensure();
            if (_resolving)
                return;
            var result = GetPort(ResultPort);
            if (result == null)
                return;
            var type = ResultFor(Inputs.Select(WiredType).ToArray());
            Warning = type == null ? "These can't be combined (" + string.Join(", ", Inputs.Select(i => WiredType(i).Name)) + ")." : null;
            type ??= typeof(float);
            if (result.Type == type)
                return;
            result.Type = type;
            _resolving = true;
            try
            {
                // Math nodes it feeds take the new type on.
                foreach (var target in result.ConnectedPorts)
                    if (target?.Node is MaterialMathNode next)
                        next.ResolveTypes();
            }
            finally
            {
                _resolving = false;
            }
            DynamicPorts.Notify(this);
        }

        public override void OnValidate()
        {
            base.OnValidate();
            ResolveTypes();
        }

        public override void OnAddedToGraph()
        {
            base.OnAddedToGraph();
            ResolveTypes();
        }

        /// <summary>The operation on the inputs' values.</summary>
        protected abstract object Calculate(object[] values);

        /// <summary>The number typed on an input.</summary>
        protected abstract float Constant(string input);

        public override Func<object> Compile(MaterialCompiler c, string output)
        {
            MaterialMath.Ensure();
            var inputs = Inputs.Select(name => c.Value(this, name, Constant(name))).ToArray();
            return () => Calculate(inputs.Select(input => input()).ToArray());
        }

        // As a plain value (numbers, vectors, colours wired into a number pin): worked out now.
        public override object OnRequestValue(Port port)
        {
            var c = MaterialCompiler.Current;
            if (c == null)
                return null;
            var values = Inputs.Select(name => c.Value(this, name, Constant(name))()).ToArray();
            if (values.Any(v => v is MaskMap || v is MaterialMaps))
                return null;
            try
            {
                return Calculate(values);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>An operation on two values (A and B), out of Result.</summary>
    [Serializable]
    public abstract class BinaryMaterialMathNode : MaterialMathNode
    {
        [Tooltip("A mask, a material, a colour or a number (typed here when nothing's wired in).")]
        [Input("A", typeof(object), Editable = true)] public float A;
        [Tooltip("A mask, a material, a colour or a number (typed here when nothing's wired in).")]
        [Input("B", typeof(object), Editable = true)] public float B;
        [Output(ResultPort)] [NonSerialized] public object Result;

        protected BinaryMaterialMathNode() => B = MathOps.DefaultB(Op);

        private static readonly string[] Names = { "A", "B" };
        protected override string[] Inputs => Names;
        protected override float Constant(string input) => input == "A" ? A : B;
        protected override object Calculate(object[] values) => MathValues.Combine(Op, values[0], values[1]);
    }

    /// <summary>An operation on one value (In), out of Result.</summary>
    [Serializable]
    [NodeColor("#EDB51A")]
    public abstract class UnaryMaterialMathNode : MaterialMathNode
    {
        [Tooltip("A mask, a material, a colour or a number (typed here when nothing's wired in).")]
        [Input("In", typeof(object), Editable = true)] public float In;
        [Output(ResultPort)] [NonSerialized] public object Result;

        private static readonly string[] Names = { "In" };
        protected override string[] Inputs => Names;
        protected override float Constant(string input) => In;
        protected override object Calculate(object[] values) => MathValues.Apply(Op, values[0]);
    }

    /// <summary>A blended towards B by T: a crossfade of anything (masks, materials' colours, colours, numbers).</summary>
    [SchematicDoc(@"{""name"": ""Mix"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A blended towards B by T: T 0 is all A, 1 all B, 0.5 half and half. Any kinds: two masks, two colours, a material's colour and a colour (T a mask: tint it where the mask says), numbers."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}, {""name"": ""T"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Mix", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialMixNode : MaterialMathNode
    {
        [Input("A", typeof(object), Editable = true)] public float A;
        [Input("B", typeof(object), Editable = true)] public float B = 1f;
        [Tooltip("How much of B: 0 all A, 1 all B. A mask mixes by its value at each spot.")]
        [Input("T", typeof(object), Editable = true)] public float T = 0.5f;
        [Output(ResultPort)] [NonSerialized] public object Result;

        public override MathOp Op => MathOp.Add;
        public override string Symbol => "mix";

        private static readonly string[] Names = { "A", "B", "T" };
        protected override string[] Inputs => Names;
        protected override float Constant(string input) => input == "A" ? A : input == "B" ? B : T;
        protected override object Calculate(object[] values) => MathValues.Mix(values[0], values[1], values[2]);
    }

    /// <summary>A colour (with a number for its alpha), for math: tint a mask, multiply a material's colour.</summary>
    [SchematicDoc(@"{""name"": ""Color"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A colour, for math: times a mask, it paints the mask in this colour (a material); times a material, it tints the material's colour."", ""outputs"": [{""name"": ""Color"", ""type"": ""Color""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Color", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialColorNode : MaterialNode
    {
        [Editable] public Color Value = new(0.8f, 0.5f, 0.3f, 1f);
        [Output("Color")] public Color Color;

        public override Func<object> Compile(MaterialCompiler compiler, string output) => null;
        public override object OnRequestValue(Port port) => Value;
    }

    // ---- The operations ----

    [SchematicDoc(@"{""name"": ""Add"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A plus B, for any kinds: two masks (both together), a mask and a number (lighter), a material and a colour (its colour brightened by it)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Add", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialAddNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Add; }

    [SchematicDoc(@"{""name"": ""Subtract"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A minus B, for any kinds: wear minus dust (wear, but not where it's dusty)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Subtract", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialSubtractNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Subtract; }

    [SchematicDoc(@"{""name"": ""Multiply"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A times B, for any kinds: two masks (only where both are: wear only on top), a mask and a number (fainter or stronger), a mask and a colour (the mask painted in it: a material), a material and a mask (its colour darkened where the mask is dark, like ambient occlusion)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Multiply", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialMultiplyNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Multiply; }

    [SchematicDoc(@"{""name"": ""Divide"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A divided by B (0 where B is 0), for any kinds."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Divide", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialDivideNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Divide; }

    [SchematicDoc(@"{""name"": ""Power"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A to the power of B: on a mask, B above 1 tightens it towards its brightest parts, below 1 spreads it out."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Power", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialPowerNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Power; }

    [SchematicDoc(@"{""name"": ""Min"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""The smaller of A and B at each spot (two masks: only where both are; Darken)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Min", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialMinNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Min; }

    [SchematicDoc(@"{""name"": ""Max"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""The larger of A and B at each spot (two masks: wherever either is; Lighten)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Max", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialMaxNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Max; }

    [SchematicDoc(@"{""name"": ""Screen"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""A softer either-or: lightens A by B, never past 1 (two masks overlapping add up gently)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Screen", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialScreenNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Screen; }

    [SchematicDoc(@"{""name"": ""Overlay"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""B adds contrast to A: darkens where A is dark, lightens where it's light (noise overlaid on a mask or a colour for texture)."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Overlay", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialOverlayNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Overlay; }

    [SchematicDoc(@"{""name"": ""Difference"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""How far apart A and B are at each spot."", ""inputs"": [{""name"": ""A"", ""type"": ""any""}, {""name"": ""B"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Difference", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialDifferenceNode : BinaryMaterialMathNode { public override MathOp Op => MathOp.Difference; }

    [SchematicDoc(@"{""name"": ""One Minus"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""1 minus In: inverts a mask (or a colour)."", ""inputs"": [{""name"": ""In"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "One Minus", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialOneMinusNode : UnaryMaterialMathNode { public override MathOp Op => MathOp.OneMinus; }

    [SchematicDoc(@"{""name"": ""Clamp 0 to 1"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""In kept between 0 and 1 (after adding masks together, say)."", ""inputs"": [{""name"": ""In"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Clamp 0 to 1", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialClamp01Node : UnaryMaterialMathNode { public override MathOp Op => MathOp.Clamp01; }

    [SchematicDoc(@"{""name"": ""Abs"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""In without its sign."", ""inputs"": [{""name"": ""In"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Abs", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialAbsNode : UnaryMaterialMathNode { public override MathOp Op => MathOp.Abs; }

    [SchematicDoc(@"{""name"": ""Fraction"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""The part after the point, looping 0 to 1: a gradient times 5, then Fraction, makes 5 repeating bands."", ""inputs"": [{""name"": ""In"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Fraction", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialFractionNode : UnaryMaterialMathNode { public override MathOp Op => MathOp.Fraction; }

    [SchematicDoc(@"{""name"": ""Floor"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""Rounded down to a whole number: a gradient times 4, then Floor and divided by 4, makes 4 flat steps."", ""inputs"": [{""name"": ""In"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Floor", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialFloorNode : UnaryMaterialMathNode { public override MathOp Op => MathOp.Floor; }

    [SchematicDoc(@"{""name"": ""Sin"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Smart Material/Math"", ""description"": ""The sine of In (radians): a gradient times 20, then Sin, makes smooth stripes (-1 to 1: Add 1 and multiply by 0.5 for 0 to 1)."", ""inputs"": [{""name"": ""In"", ""type"": ""any""}], ""outputs"": [{""name"": ""Result"", ""type"": ""any""}]}")]
    [Serializable]
    [Node(Name = "Sin", Path = "Math"), Tags(MaterialNode.Tag)]
    public sealed class MaterialSinNode : UnaryMaterialMathNode { public override MathOp Op => MathOp.Sin; }
}
