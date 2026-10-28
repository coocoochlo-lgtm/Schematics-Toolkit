using System;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A node drawn as a small flat square with no title bar and an icon of what it does in the middle: its
    /// <see cref="Symbol"/>, or (with none) a plot of its own operation over <see cref="PlotRange"/>. Math nodes in every
    /// kind of graph (logic, mesh, sound) are drawn this way.
    /// </summary>
    public interface ISquareIconNode
    {
        /// <summary>The symbol drawn as its icon (+, ×, and...), or null to plot its curve.</summary>
        string Symbol { get; }

        /// <summary>For a plotted icon: the stretch of input it's drawn over.</summary>
        Vector2 PlotRange { get; }

        /// <summary>For a plotted icon of an operation on two values: B, while A runs along the plot.</summary>
        float PlotB { get; }

        /// <summary>A function of one value (Sin, Abs...: drawn yellow) rather than an operator (+, ×, and...: drawn green).</summary>
        bool IsFunction { get; }

        /// <summary>The operation over the first <paramref name="count"/> values (for the plot; B ignored by one-value operations).</summary>
        void Evaluate(float[] a, float[] b, float[] result, int count);
    }

    /// <summary>The operations math nodes do, the same in every kind of graph.</summary>
    public enum MathOp
    {
        // Two values (A, B → Result)
        Add, Subtract, Multiply, Divide, Modulo, Power, Min, Max,
        // One value (In → Out)
        Sin, Cos, Abs, Floor, Ceil, Round, Fraction, SquareRoot, Negate, OneMinus, Clamp01, Sign,
    }

    /// <summary>
    /// What each <see cref="MathOp"/> does and how it's drawn: shared by the logic, mesh and sound graphs' math nodes so
    /// an Add is the same Add everywhere.
    /// </summary>
    public static class MathOps
    {
        public static bool IsUnary(MathOp op) => op >= MathOp.Sin;

        public static float Apply(MathOp op, float a, float b = 0f) => op switch
        {
            MathOp.Add => a + b,
            MathOp.Subtract => a - b,
            MathOp.Multiply => a * b,
            MathOp.Divide => b != 0f ? a / b : 0f,
            // Always 0 up to B (even for negative A), so a looping count never goes negative.
            MathOp.Modulo => b != 0f ? a - b * Mathf.Floor(a / b) : 0f,
            MathOp.Power => Mathf.Pow(a, b),
            MathOp.Min => Mathf.Min(a, b),
            MathOp.Max => Mathf.Max(a, b),
            MathOp.Sin => Mathf.Sin(a),
            MathOp.Cos => Mathf.Cos(a),
            MathOp.Abs => Mathf.Abs(a),
            MathOp.Floor => Mathf.Floor(a),
            MathOp.Ceil => Mathf.Ceil(a),
            MathOp.Round => Mathf.Round(a),
            MathOp.Fraction => a - Mathf.Floor(a),
            MathOp.SquareRoot => a > 0f ? Mathf.Sqrt(a) : 0f,
            MathOp.Negate => -a,
            MathOp.OneMinus => 1f - a,
            MathOp.Clamp01 => Mathf.Clamp01(a),
            MathOp.Sign => a > 0f ? 1f : a < 0f ? -1f : 0f,
            _ => a,
        };

        /// <summary>The same over arrays (for icons' plots).</summary>
        public static void Apply(MathOp op, float[] a, float[] b, float[] result, int count)
        {
            for (int i = 0; i < count; i++)
                result[i] = Apply(op, a[i], b != null ? b[i] : 0f);
        }

        /// <summary>B's value on a new node (what leaves A as it is, mostly).</summary>
        public static float DefaultB(MathOp op) => op switch
        {
            MathOp.Multiply or MathOp.Divide or MathOp.Modulo or MathOp.Min => 1f,
            MathOp.Power => 2f,
            _ => 0f,
        };

        public static string Symbol(MathOp op) => op switch
        {
            MathOp.Add => "+",
            MathOp.Subtract => "−",
            MathOp.Multiply => "×",
            MathOp.Divide => "÷",
            MathOp.Modulo => "%",
            MathOp.Power => "xʸ",
            MathOp.Min => "min",
            MathOp.Max => "max",
            MathOp.Negate => "−x",
            MathOp.OneMinus => "1−x",
            _ => null,
        };

        public static Vector2 PlotRange(MathOp op) => op switch
        {
            MathOp.Power or MathOp.SquareRoot => new Vector2(0f, 1f),
            MathOp.Sin or MathOp.Cos => new Vector2(-Mathf.PI, Mathf.PI),
            MathOp.Floor or MathOp.Ceil or MathOp.Round => new Vector2(-2f, 2f),
            MathOp.Fraction => new Vector2(-1f, 2f),
            MathOp.Clamp01 => new Vector2(-0.5f, 1.5f),
            _ => new Vector2(-1f, 1f),
        };

        public static float PlotB(MathOp op) => op switch
        {
            MathOp.Power => 2f,
            MathOp.Min => 0.3f,
            MathOp.Max => -0.3f,
            _ => 1f,
        };
    }
}
