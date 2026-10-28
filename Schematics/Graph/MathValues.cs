using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A kind of value math nodes work on: numbers, vectors and colours here; features add their own (a Smart
    /// Material's masks and materials, say). Each <see cref="MathOp"/> is written once, for single numbers, in
    /// <see cref="MathOps"/>; a kind applies it across its values (channel by channel, texel by texel...).
    /// </summary>
    public abstract class MathKind
    {
        /// <summary>
        /// How wide it is: a pair of values is worked out by the wider one's kind (a mask times a number is a mask's
        /// business), which also says what comes out.
        /// </summary>
        public abstract int Rank { get; }

        /// <summary>Whether a value of this type is one of this kind's.</summary>
        public abstract bool Handles(Type type);

        /// <summary>What A op B gives when the wider of them is this kind's (null: they can't be combined).</summary>
        public abstract Type ResultType(Type a, Type b);

        /// <summary>A op B, where the wider of them is this kind's.</summary>
        public abstract object Combine(MathOp op, object a, object b);

        /// <summary>A one-value operation on one of this kind's values.</summary>
        public abstract object Apply(MathOp op, object value);
    }

    /// <summary>
    /// Math on any kind of value: one Multiply for numbers, vectors, colours, masks... A pair works out as the wider
    /// of the two (a vector times a number scales the vector; a vector times a colour is a colour, channel by channel).
    /// </summary>
    public static class MathValues
    {
        private static readonly List<MathKind> _kinds = new() { new NumberKind() };

        /// <summary>Adds a kind of value (a feature's own: masks, materials, sound...). Called once, at load.</summary>
        public static void Register(MathKind kind)
        {
            lock (_kinds)
            {
                if (kind == null || _kinds.Exists(k => k.GetType() == kind.GetType()))
                    return;
                _kinds.Add(kind);
                _kinds.Sort((x, y) => y.Rank.CompareTo(x.Rank));
            }
        }

        /// <summary>The kind a type belongs to (null: math can't use it).</summary>
        public static MathKind KindOf(Type type)
        {
            if (type == null)
                return null;
            lock (_kinds)
                foreach (var kind in _kinds)
                    if (kind.Handles(type))
                        return kind;
            return null;
        }

        private static MathKind Wider(Type a, Type b)
        {
            var ka = KindOf(a);
            var kb = KindOf(b);
            if (ka == null) return kb;
            if (kb == null) return ka;
            return kb.Rank > ka.Rank ? kb : ka;
        }

        /// <summary>What A op B gives (float when neither is known yet; null when they can't be combined).</summary>
        public static Type ResultType(Type a, Type b)
        {
            a ??= typeof(float);
            b ??= typeof(float);
            return Wider(a, b)?.ResultType(a, b);
        }

        /// <summary>What a one-value operation on a value of this type gives.</summary>
        public static Type ResultType(Type value) => ResultType(value, typeof(float)) ?? typeof(float);

        /// <summary>A op B, whatever kinds of value they are.</summary>
        public static object Combine(MathOp op, object a, object b)
        {
            a ??= 0f;
            b ??= 0f;
            var kind = Wider(a.GetType(), b.GetType());
            if (kind == null)
                throw new InvalidOperationException($"Math can't use {a.GetType().Name} and {b.GetType().Name} together.");
            return kind.Combine(op, a, b);
        }

        /// <summary>A one-value operation (Sin, Abs, One Minus...) on any kind of value.</summary>
        public static object Apply(MathOp op, object value)
        {
            value ??= 0f;
            var kind = KindOf(value.GetType());
            if (kind == null)
                throw new InvalidOperationException($"Math can't use {value.GetType().Name}.");
            return kind.Apply(op, value);
        }

        /// <summary>A blended towards B by T (0 all A, 1 all B), for any kinds: A + (B − A) × T.</summary>
        public static object Mix(object a, object b, object t)
            => Combine(MathOp.Add, a, Combine(MathOp.Multiply, Combine(MathOp.Subtract, b, a), t));

        // ---- Numbers, vectors and colours ----

        /// <summary>
        /// Numbers, Vector2/3/4 and colours, worked channel by channel. A number goes into every channel; the result has
        /// as many channels as the wider value, and is a colour if either was.
        /// </summary>
        private sealed class NumberKind : MathKind
        {
            public override int Rank => 0;

            public override bool Handles(Type type) => Width(type) > 0;

            private static int Width(Type type)
            {
                if (type == typeof(float) || type == typeof(int) || type == typeof(double) || type == typeof(bool)) return 1;
                if (type == typeof(Vector2)) return 2;
                if (type == typeof(Vector3)) return 3;
                if (type == typeof(Vector4)) return 4;
                if (type == typeof(Color)) return 5;
                return 0;
            }

            private static Type TypeOfWidth(int width) => width switch
            {
                1 => typeof(float),
                2 => typeof(Vector2),
                3 => typeof(Vector3),
                4 => typeof(Vector4),
                _ => typeof(Color),
            };

            public override Type ResultType(Type a, Type b)
            {
                int wa = Width(a), wb = Width(b);
                return wa == 0 || wb == 0 ? null : TypeOfWidth(Mathf.Max(wa, wb));
            }

            private static Vector4 ToVector(object value) => value switch
            {
                float f => new Vector4(f, f, f, f),
                int i => new Vector4(i, i, i, i),
                double d => new Vector4((float)d, (float)d, (float)d, (float)d),
                bool b => b ? Vector4.one : Vector4.zero,
                Vector2 v => new Vector4(v.x, v.y, 0f, 0f),
                Vector3 v => new Vector4(v.x, v.y, v.z, 0f),
                Vector4 v => v,
                Color c => new Vector4(c.r, c.g, c.b, c.a),
                _ => Vector4.zero,
            };

            private static object FromVector(Vector4 v, int width) => width switch
            {
                1 => v.x,
                2 => new Vector2(v.x, v.y),
                3 => new Vector3(v.x, v.y, v.z),
                4 => v,
                _ => new Color(v.x, v.y, v.z, v.w),
            };

            public override object Combine(MathOp op, object a, object b)
            {
                int width = Mathf.Max(Width(a.GetType()), Width(b.GetType()));
                var va = ToVector(a);
                var vb = ToVector(b);
                // A colour's alpha stays as A's (or B's, if only B is a colour): blending colours shouldn't fade them out.
                var result = new Vector4(MathOps.Apply(op, va.x, vb.x), MathOps.Apply(op, va.y, vb.y), MathOps.Apply(op, va.z, vb.z), MathOps.Apply(op, va.w, vb.w));
                if (width == 5)
                    result.w = a is Color ca ? ca.a : b is Color cb ? cb.a : 1f;
                return FromVector(result, width);
            }

            public override object Apply(MathOp op, object value)
            {
                int width = Width(value.GetType());
                var v = ToVector(value);
                var result = new Vector4(MathOps.Apply(op, v.x), MathOps.Apply(op, v.y), MathOps.Apply(op, v.z), MathOps.Apply(op, v.w));
                if (width == 5)
                    result.w = v.w;
                return FromVector(result, width);
            }
        }
    }
}
