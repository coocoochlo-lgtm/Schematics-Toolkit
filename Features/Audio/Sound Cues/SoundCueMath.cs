// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    // Sound Cue math, one node per operation (Add, Multiply, Sine Wave...): small square nodes with no title bar, an
    // icon of what each does in the middle (its symbol, or a plot of its curve), values in on the left, the result out
    // on the right. Each runs sample by sample over a whole block.

    /// <summary>A Sound Cue math node: what its icon shows, and the operation itself (for its processor and its icon's plot).</summary>
    [Serializable]
    [NodeColor(NodeColors.Math)]
    public abstract class SoundMathOpNode : SoundNode
    {
        /// <summary>The symbol drawn as its icon (+, ×, 1−x...), or null to plot its curve.</summary>
        public virtual string Symbol => null;

        /// <summary>For a plotted icon: the stretch of input it's drawn over.</summary>
        public virtual Vector2 PlotRange => new(-1f, 1f);

        /// <summary>For a plotted icon of an operation on two values: B, while A runs along the plot.</summary>
        public virtual float PlotB => 1f;

        /// <summary>
        /// The operation on the first <paramref name="count"/> samples (B ignored by operations on one value). Runs on
        /// the audio thread, so it reads nothing but its arguments.
        /// </summary>
        public abstract void Evaluate(float[] a, float[] b, float[] result, int count);
    }

    /// <summary>An operation on one value (In), out of Out.</summary>
    [Serializable]
    public abstract class SoundUnaryMathNode : SoundMathOpNode
    {
        [Input("In", Editable = true)] public float In;
        [Output("Out")] public float Out;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { Node = this, In = builder.Input(this, "In", In) };

        /// <summary>Its pins, for a node made in code (they're made from its fields in the editor).</summary>
        internal void AddPins()
        {
            if (GetPort("In") == null)
                AddPort(new Port { Name = "In", Type = typeof(float), Direction = PortDirection.Input, Capacity = PortCapacity.Single });
            if (GetPort("Out") == null)
                AddPort(new Port { Name = "Out", Type = typeof(float), Direction = PortDirection.Output, Capacity = PortCapacity.Multiple });
        }

        private sealed class Processor : SoundProcessor
        {
            public SoundMathOpNode Node;
            public float[] In;

            public override void Process(SoundContext context, int count) => Node.Evaluate(In, null, Output("Out"), count);
        }
    }

    /// <summary>An operation on two values (A and B), out of Result.</summary>
    [Serializable]
    public abstract class SoundBinaryMathNode : SoundMathOpNode
    {
        [Input("A", Editable = true)] public float A;
        [Input("B", Editable = true)] public float B = 1f;
        [Output("Result")] public float Result;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
            => new Processor { Node = this, A = builder.Input(this, "A", A), B = builder.Input(this, "B", B) };

        /// <summary>Its pins, for a node made in code (they're made from its fields in the editor).</summary>
        internal void AddPins()
        {
            if (GetPort("A") == null)
                AddPort(new Port { Name = "A", Type = typeof(float), Direction = PortDirection.Input, Capacity = PortCapacity.Single });
            if (GetPort("B") == null)
                AddPort(new Port { Name = "B", Type = typeof(float), Direction = PortDirection.Input, Capacity = PortCapacity.Single });
            if (GetPort("Result") == null)
                AddPort(new Port { Name = "Result", Type = typeof(float), Direction = PortDirection.Output, Capacity = PortCapacity.Multiple });
        }

        private sealed class Processor : SoundProcessor
        {
            public SoundMathOpNode Node;
            public float[] A, B;

            public override void Process(SoundContext context, int count) => Node.Evaluate(A, B, Output("Result"), count);
        }
    }

    // --- Two values ---

    [SchematicDoc(@"{""name"": ""Add"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A plus B, sample by sample. Adding two sounds mixes them; adding a number to Time starts what it plays later (or earlier)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Add", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundAddNode : SoundBinaryMathNode
    {
        public SoundAddNode() => B = 0f;
        public override string Symbol => "+";
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = a[i] + b[i]; }
    }

    [SchematicDoc(@"{""name"": ""Subtract"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A minus B, sample by sample."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Subtract", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundSubtractNode : SoundBinaryMathNode
    {
        public SoundSubtractNode() => B = 0f;
        public override string Symbol => "−";
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = a[i] - b[i]; }
    }

    [SchematicDoc(@"{""name"": ""Multiply"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A times B, sample by sample. Multiplying a sound by a value changes its volume; multiplying Time by one changes the speed (and pitch) of what it plays."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Multiply", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundMultiplyNode : SoundBinaryMathNode
    {
        public override string Symbol => "×";
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = a[i] * b[i]; }
    }

    [SchematicDoc(@"{""name"": ""Divide"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A divided by B, sample by sample (0 where B is 0)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Divide", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundDivideNode : SoundBinaryMathNode
    {
        public override string Symbol => "÷";
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = b[i] != 0f ? a[i] / b[i] : 0f; }
    }

    [SchematicDoc(@"{""name"": ""Modulo"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""What's left of A after taking out whole Bs, always 0 up to B: Time modulo 2 loops every 2 seconds."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Modulo", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundModuloNode : SoundBinaryMathNode
    {
        public override string Symbol => "%";
        // Always positive, so a looped Time never goes negative.
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = b[i] != 0f ? a[i] - b[i] * Mathf.Floor(a[i] / b[i]) : 0f; }
    }

    [SchematicDoc(@"{""name"": ""Power"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A to the power of B, sample by sample: B of 2 on a 0 to 1 fade makes it start slow and finish fast."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Power", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundPowerNode : SoundBinaryMathNode
    {
        public SoundPowerNode() => B = 2f;
        public override Vector2 PlotRange => new(0f, 1f);
        public override float PlotB => 2f;
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = Mathf.Pow(a[i], b[i]); }
    }

    [SchematicDoc(@"{""name"": ""Min"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""The smaller of A and B, sample by sample: a ceiling on A."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Min", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundMinNode : SoundBinaryMathNode
    {
        public override float PlotB => 0.3f;
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = Mathf.Min(a[i], b[i]); }
    }

    [SchematicDoc(@"{""name"": ""Max"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""The larger of A and B, sample by sample: a floor under A."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Max", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundMaxNode : SoundBinaryMathNode
    {
        public SoundMaxNode() => B = 0f;
        public override float PlotB => -0.3f;
        public override void Evaluate(float[] a, float[] b, float[] r, int count) { for (int i = 0; i < count; i++) r[i] = Mathf.Max(a[i], b[i]); }
    }

    // --- One value ---

    [SchematicDoc(@"{""name"": ""Sine Wave"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""One full sine wave per 1 of In, from -1 to 1: Time times 440 into it is a 440 Hz tone; a slow one wobbles a control."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sine Wave", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundSineWaveNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(0f, 1f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Sin(x[i] * 2f * Mathf.PI); }
    }

    [SchematicDoc(@"{""name"": ""Sin"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""The sine of In, in radians (a full wave every 2π)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sin", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundSinNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(-Mathf.PI, Mathf.PI);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Sin(x[i]); }
    }

    [SchematicDoc(@"{""name"": ""Cos"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""The cosine of In, in radians (a full wave every 2π)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Cos", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundCosNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(-Mathf.PI, Mathf.PI);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Cos(x[i]); }
    }

    [SchematicDoc(@"{""name"": ""Abs"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""In without its sign (always 0 or more): a sound through it is rectified, an octave up and buzzier."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Abs", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundAbsNode : SoundUnaryMathNode
    {
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Abs(x[i]); }
    }

    [SchematicDoc(@"{""name"": ""Floor"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""In rounded down to a whole number: Time through it steps once a second."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Floor", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundFloorNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(-2f, 2f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Floor(x[i]); }
    }

    [SchematicDoc(@"{""name"": ""Fraction"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""The part of In after the point, looping 0 to 1: Time through it is a ramp that restarts every second."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Fraction", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundFractionNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(-1f, 2f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = x[i] - Mathf.Floor(x[i]); }
    }

    [SchematicDoc(@"{""name"": ""Square Root"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""The square root of In (0 below 0): a 0 to 1 fade through it starts fast and eases in."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Square Root", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundSqrtNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(0f, 1f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = x[i] > 0f ? Mathf.Sqrt(x[i]) : 0f; }
    }

    [SchematicDoc(@"{""name"": ""Negate"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""In with its sign flipped: a sound through it is upside down (cancelling the original where they're mixed)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Negate", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundNegateNode : SoundUnaryMathNode
    {
        public override string Symbol => "−x";
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = -x[i]; }
    }

    [SchematicDoc(@"{""name"": ""One Minus"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""1 minus In: turns a 0 to 1 fade in into a fade out (Progress through it is what's left of the cue)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "One Minus", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundOneMinusNode : SoundUnaryMathNode
    {
        public override string Symbol => "1−x";
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = 1f - x[i]; }
    }

    [SchematicDoc(@"{""name"": ""Clamp 0 to 1"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""In kept between 0 and 1: a control that mustn't go past full or below nothing."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Clamp 0 to 1", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundClamp01Node : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(-0.5f, 1.5f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Clamp01(x[i]); }
    }

    [SchematicDoc(@"{""name"": ""Sign"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""1 where In is above 0, -1 below, 0 at 0: a sine wave through it is a square wave."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Sign", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundSignNode : SoundUnaryMathNode
    {
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = x[i] > 0f ? 1f : x[i] < 0f ? -1f : 0f; }
    }

    [SchematicDoc(@"{""name"": ""Decibels To Volume"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""Decibels (0 is full, -6 about half, -60 nearly silent) as a volume to multiply a sound by."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Decibels To Volume", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundDecibelsToVolumeNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(-40f, 6f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = Mathf.Pow(10f, x[i] / 20f); }
    }

    [SchematicDoc(@"{""name"": ""Volume To Decibels"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Math"", ""description"": ""A volume (1 is full) in decibels (-80 for silence)."", ""inputs"": [{""name"": ""In"", ""type"": ""float""}], ""outputs"": [{""name"": ""Out"", ""type"": ""float""}]}")]
    [Serializable]
    [Node(Name = "Volume To Decibels", Path = "Math"), Tags(SoundNode.Tag)]
    public class SoundVolumeToDecibelsNode : SoundUnaryMathNode
    {
        public override Vector2 PlotRange => new(0.01f, 2f);
        public override void Evaluate(float[] x, float[] b, float[] o, int count) { for (int i = 0; i < count; i++) o[i] = x[i] > 0f ? 20f * Mathf.Log10(x[i]) : -80f; }
    }

    /// <summary>Turns the old all-in-one Math and Function nodes into the node for their operation (same pins, same wires).</summary>
    public static class SoundMathUpgrade
    {
        /// <summary>The new node standing in for an old one, with its values and name; null if it isn't one.</summary>
        public static SoundMathOpNode For(Node old)
        {
            SoundMathOpNode made;
            switch (old)
            {
                case SoundMathNode math:
                {
                    SoundBinaryMathNode binary = math.Operator switch
                    {
                        SoundMathNode.Operation.Add => new SoundAddNode(),
                        SoundMathNode.Operation.Subtract => new SoundSubtractNode(),
                        SoundMathNode.Operation.Divide => new SoundDivideNode(),
                        SoundMathNode.Operation.Modulo => new SoundModuloNode(),
                        SoundMathNode.Operation.Power => new SoundPowerNode(),
                        SoundMathNode.Operation.Min => new SoundMinNode(),
                        SoundMathNode.Operation.Max => new SoundMaxNode(),
                        _ => new SoundMultiplyNode(),
                    };
                    binary.A = math.A;
                    binary.B = math.B;
                    binary.AddPins();
                    made = binary;
                    break;
                }
                case SoundFunctionNode function:
                {
                    SoundUnaryMathNode unary = function.Operator switch
                    {
                        SoundFunctionNode.Function.Sin => new SoundSinNode(),
                        SoundFunctionNode.Function.Cos => new SoundCosNode(),
                        SoundFunctionNode.Function.Abs => new SoundAbsNode(),
                        SoundFunctionNode.Function.Floor => new SoundFloorNode(),
                        SoundFunctionNode.Function.Fraction => new SoundFractionNode(),
                        SoundFunctionNode.Function.Sqrt => new SoundSqrtNode(),
                        SoundFunctionNode.Function.Negate => new SoundNegateNode(),
                        SoundFunctionNode.Function.OneMinus => new SoundOneMinusNode(),
                        SoundFunctionNode.Function.Clamp01 => new SoundClamp01Node(),
                        SoundFunctionNode.Function.Sign => new SoundSignNode(),
                        SoundFunctionNode.Function.DecibelsToVolume => new SoundDecibelsToVolumeNode(),
                        SoundFunctionNode.Function.VolumeToDecibels => new SoundVolumeToDecibelsNode(),
                        _ => new SoundSineWaveNode(),
                    };
                    unary.In = function.In;
                    unary.AddPins();
                    made = unary;
                    break;
                }
                default:
                    return null;
            }
            made.Name = NameOf(made.GetType());
            made.CustomName = old.CustomName;
            return made;
        }

        private static string NameOf(Type type)
            => Attribute.GetCustomAttribute(type, typeof(NodeAttribute)) is NodeAttribute node && !string.IsNullOrEmpty(node.Name) ? node.Name : type.Name;

        /// <summary>Replaces a cue's old Math and Function nodes; true if there were any.</summary>
        public static bool Upgrade(SoundCueGraph cue)
        {
            bool any = false;
            foreach (var node in new System.Collections.Generic.List<Node>(cue.Nodes))
            {
                var replacement = For(node);
                if (replacement == null)
                    continue;
                cue.ReplaceNode(node, replacement);
                any = true;
            }
            return any;
        }
    }
}
