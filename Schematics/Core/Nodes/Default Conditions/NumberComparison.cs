// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Compares two numbers of any kind (int, long, float, double, decimal, bool, enums...) in the most precise type
    /// the pair needs, so no number gets rounded before it's compared.
    /// </summary>
    /// <remarks>
    /// How the type is picked:
    /// <list type="bullet">
    /// <item>Two whole numbers: compared as whole numbers (long, or decimal when a ulong is involved), so huge ints
    /// stay exact.</item>
    /// <item>A whole number and a decimal: as decimal.</item>
    /// <item>Anything with a float or double: as double, which holds every int and float exactly.</item>
    /// </list>
    /// Fractional numbers count as equal when they're within rounding error of each other (0.1 + 0.2 equals 0.3): a
    /// float's rounding error when either side is a float, a double's otherwise. NaN is never less, equal or greater.
    /// </remarks>
    public static class NumberComparison
    {
        /// <summary>How two numbers relate. Unordered when either isn't a number (or is NaN).</summary>
        public enum Order { Less, Equal, Greater, Unordered }

        private enum Kind { None, Integer, UInteger, Single, Double, Decimal }

        /// <summary>True when the value is a number this can compare.</summary>
        public static bool IsNumber(object value) => KindOf(value) != Kind.None;

        public static Order Compare(object a, object b)
        {
            Kind ka = KindOf(a), kb = KindOf(b);
            if (ka == Kind.None || kb == Kind.None)
                return Order.Unordered;

            bool aWhole = ka == Kind.Integer || ka == Kind.UInteger;
            bool bWhole = kb == Kind.Integer || kb == Kind.UInteger;

            if (aWhole && bWhole)
            {
                if (ka == Kind.UInteger || kb == Kind.UInteger)
                    return Sign(ToDecimal(a).CompareTo(ToDecimal(b)));
                return Sign(ToLong(a).CompareTo(ToLong(b)));
            }

            if ((ka == Kind.Decimal || aWhole) && (kb == Kind.Decimal || bWhole))
                return Sign(ToDecimal(a).CompareTo(ToDecimal(b)));

            double x = ToDouble(a), y = ToDouble(b);
            if (double.IsNaN(x) || double.IsNaN(y))
                return Order.Unordered;
            if (x == y)
                return Order.Equal;
            // Within rounding error of the least precise side counts as equal.
            bool single = ka == Kind.Single || kb == Kind.Single;
            double scale = Math.Max(Math.Abs(x), Math.Abs(y));
            if (!double.IsInfinity(scale) && Math.Abs(x - y) <= (single ? 1e-6 : 1e-12) * scale)
                return Order.Equal;
            return x < y ? Order.Less : Order.Greater;
        }

        private static Order Sign(int compared) => compared < 0 ? Order.Less : compared > 0 ? Order.Greater : Order.Equal;

        private static Kind KindOf(object value)
        {
            switch (value)
            {
                case int _: case long _: case short _: case sbyte _: case byte _: case ushort _: case uint _: case char _: case bool _:
                    return Kind.Integer;
                case ulong _:
                    return Kind.UInteger;
                case float _:
                    return Kind.Single;
                case double _:
                    return Kind.Double;
                case decimal _:
                    return Kind.Decimal;
                case LayerMask _:
                    return Kind.Integer;
                case Enum _:
                    return Enum.GetUnderlyingType(value.GetType()) == typeof(ulong) ? Kind.UInteger : Kind.Integer;
                default:
                    return Kind.None;
            }
        }

        private static long ToLong(object value) => value switch
        {
            bool b => b ? 1 : 0,
            LayerMask mask => mask.value,
            _ => Convert.ToInt64(value),
        };

        private static decimal ToDecimal(object value) => value switch
        {
            bool b => b ? 1 : 0,
            LayerMask mask => mask.value,
            Enum e when KindOf(e) == Kind.UInteger => Convert.ToUInt64(e),
            Enum e => Convert.ToInt64(e),
            _ => Convert.ToDecimal(value),
        };

        private static double ToDouble(object value) => value switch
        {
            bool b => b ? 1 : 0,
            LayerMask mask => mask.value,
            Enum e => (double)ToDecimal(e),
            _ => Convert.ToDouble(value),
        };
    }

    /// <summary>
    /// The shared part of the comparison nodes (Greater Than, Equal To...): two inputs that take any number, typed in
    /// or connected, compared with <see cref="NumberComparison"/>.
    /// </summary>
    [Serializable]
    public abstract class NumberComparisonNode : SchematicConditionalNode
    {
        // The ports take any value (so int, long, float, double and Union outputs all connect); the fields are what's
        // typed in when nothing is connected, or when what's connected isn't a number.
        [Input("A", typeof(object), Editable = true)]
        public double A;
        [Input("B", typeof(object), Editable = true)]
        public double B;

        /// <summary>Whether the result fits, given how A relates to B.</summary>
        protected abstract bool Accepts(NumberComparison.Order order);

        public override bool Condition()
            => Accepts(NumberComparison.Compare(Read("A", A), Read("B", B)));

        /// <summary>A connected number as it is (int stays int, double stays double), else the typed-in one.</summary>
        protected object Read(string port, double typed)
        {
            if (TryGetInputValue<object>(port, out var value) && NumberComparison.IsNumber(value))
                return value;
            return Typed(typed);
        }

        /// <summary>A typed-in number: whole ones count as whole numbers, so they compare exactly with connected ints.</summary>
        protected static object Typed(double typed)
            => Math.Floor(typed) == typed && Math.Abs(typed) < 9e15 ? (object)(long)typed : typed;
    }

    [SchematicDoc(@"{""name"": ""Greater Than"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when A is greater than B. Takes any numbers (int, float, double...) and compares them without rounding."", ""inputs"": [{""name"": ""A"", ""type"": ""number""}, {""name"": ""B"", ""type"": ""number""}], ""use_for"": [""score thresholds"", ""speed limits""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Name = "Greater Than", Path = "Conditions/Compare"), Tags("Default")]
    public class GreaterThan : NumberComparisonNode
    {
        protected override bool Accepts(NumberComparison.Order order) => order == NumberComparison.Order.Greater;
    }

    [SchematicDoc(@"{""name"": ""Greater Than Or Equal"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when A is greater than or equal to B. Takes any numbers (int, float, double...) and compares them without rounding."", ""inputs"": [{""name"": ""A"", ""type"": ""number""}, {""name"": ""B"", ""type"": ""number""}], ""use_for"": [""having enough of something (coins >= price)""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Name = "Greater Than Or Equal", Path = "Conditions/Compare"), Tags("Default")]
    public class GreaterThanOrEqual : NumberComparisonNode
    {
        protected override bool Accepts(NumberComparison.Order order)
            => order == NumberComparison.Order.Greater || order == NumberComparison.Order.Equal;
    }

    [SchematicDoc(@"{""name"": ""Less Than"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when A is less than B. Takes any numbers (int, float, double...) and compares them without rounding."", ""inputs"": [{""name"": ""A"", ""type"": ""number""}, {""name"": ""B"", ""type"": ""number""}], ""use_for"": [""distance checks"", ""low health warnings""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Name = "Less Than", Path = "Conditions/Compare"), Tags("Default")]
    public class LessThan : NumberComparisonNode
    {
        protected override bool Accepts(NumberComparison.Order order) => order == NumberComparison.Order.Less;
    }

    [SchematicDoc(@"{""name"": ""Less Than Or Equal"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when A is less than or equal to B. Takes any numbers (int, float, double...) and compares them without rounding."", ""inputs"": [{""name"": ""A"", ""type"": ""number""}, {""name"": ""B"", ""type"": ""number""}], ""use_for"": [""health checks (health <= 0)"", ""timers running out""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Name = "Less Than Or Equal", Path = "Conditions/Compare"), Tags("Default")]
    public class LessThanOrEqual : NumberComparisonNode
    {
        protected override bool Accepts(NumberComparison.Order order)
            => order == NumberComparison.Order.Less || order == NumberComparison.Order.Equal;
    }

    [SchematicDoc(@"{""name"": ""Equal"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when A equals B. Takes any numbers (int, float, double...); fractional numbers within rounding error of each other count as equal."", ""inputs"": [{""name"": ""A"", ""type"": ""number""}, {""name"": ""B"", ""type"": ""number""}], ""use_for"": [""reaching an exact count"", ""checking a state number""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Name = "Equal", Path = "Conditions/Compare"), Tags("Default")]
    public class NumbersEqual : NumberComparisonNode
    {
        protected override bool Accepts(NumberComparison.Order order) => order == NumberComparison.Order.Equal;
    }

    [SchematicDoc(@"{""name"": ""Not Equal"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Conditions"", ""description"": ""True when A doesn't equal B. Takes any numbers (int, float, double...); fractional numbers within rounding error of each other count as equal."", ""inputs"": [{""name"": ""A"", ""type"": ""number""}, {""name"": ""B"", ""type"": ""number""}], ""use_for"": [""reacting when a value changed from a known one""], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [Node(Name = "Not Equal", Path = "Conditions/Compare"), Tags("Default")]
    public class NumbersNotEqual : NumberComparisonNode
    {
        protected override bool Accepts(NumberComparison.Order order) => order != NumberComparison.Order.Equal;
    }
}
