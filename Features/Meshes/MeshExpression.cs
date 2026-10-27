using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>A value in a formula: a number, or a vector (x, y, z).</summary>
    public readonly struct ExprValue
    {
        public readonly Vector3 V;
        public readonly bool IsVector;

        public ExprValue(float number) { V = new Vector3(number, 0f, 0f); IsVector = false; }
        public ExprValue(Vector3 vector) { V = vector; IsVector = true; }

        /// <summary>As a number (a vector's x).</summary>
        public float Number => V.x;

        /// <summary>As a vector (a number is (n, n, n)).</summary>
        public Vector3 Vector => IsVector ? V : new Vector3(V.x, V.x, V.x);

        public static implicit operator ExprValue(float f) => new(f);
        public static implicit operator ExprValue(Vector3 v) => new(v);
    }

    /// <summary>
    /// A formula typed on a Mesh Graph node, read once (main thread) and worked out per point or vertex (any thread).
    /// Numbers and vectors mix as you'd expect: <c>p + vec(0, sin(x * 3) * 0.2, 0)</c>, <c>p * (1 + noise(p) * 0.1)</c>,
    /// <c>i % 2 == 0 ? 1 : 0.5</c>.
    /// <para>Operators: + - * / % ^ (power), comparisons (&lt; &gt; &lt;= &gt;= == !=), &amp;&amp; || !, and
    /// <c>a ? b : c</c>. A vector's parts: <c>p.x</c>, <c>p.y</c>, <c>p.z</c>. Functions: sin cos tan asin acos atan
    /// atan2 sqrt abs floor ceil round frac sign min max clamp lerp pow exp log step smoothstep radians degrees vec
    /// length normalize dot cross distance noise fbm rand. Constants: pi, tau, e. The Graph's Parameters can be used by
    /// name.</para>
    /// </summary>
    public sealed class MeshExpression
    {
        private readonly Node _root;

        /// <summary>The text it was read from.</summary>
        public string Text { get; }

        private MeshExpression(string text, Node root)
        {
            Text = text;
            _root = root;
        }

        /// <summary>
        /// Reads a formula. <paramref name="variables"/> are the names it can use that change per point (their values
        /// are given to <see cref="Evaluate"/> in that order); <paramref name="constants"/> are fixed for the whole
        /// graph (its Parameters). Null, with <paramref name="error"/> saying why, if it can't be read.
        /// </summary>
        public static MeshExpression Parse(string text, IReadOnlyList<string> variables, IReadOnlyDictionary<string, float> constants, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The formula is empty.";
                return null;
            }
            try
            {
                var parser = new Parser(text, variables, constants);
                var root = parser.ParseAll();
                return new MeshExpression(text, root);
            }
            catch (FormatException exception)
            {
                error = $"\"{text}\": {exception.Message}";
                return null;
            }
        }

        /// <summary>Works it out, with the per-point variables' values (any thread).</summary>
        public ExprValue Evaluate(ExprValue[] variables) => _root.Eval(variables);

        #region The tree

        private abstract class Node
        {
            public abstract ExprValue Eval(ExprValue[] vars);
        }

        private sealed class Constant : Node
        {
            private readonly ExprValue _value;
            public Constant(ExprValue value) => _value = value;
            public override ExprValue Eval(ExprValue[] vars) => _value;
        }

        private sealed class Variable : Node
        {
            private readonly int _index;
            public Variable(int index) => _index = index;
            public override ExprValue Eval(ExprValue[] vars) => _index < vars.Length ? vars[_index] : default;
        }

        private sealed class Component : Node
        {
            private readonly Node _of;
            private readonly int _axis;
            public Component(Node of, int axis) { _of = of; _axis = axis; }
            public override ExprValue Eval(ExprValue[] vars) => _of.Eval(vars).Vector[_axis];
        }

        private sealed class Unary : Node
        {
            private readonly Node _of;
            private readonly char _op;
            public Unary(char op, Node of) { _op = op; _of = of; }
            public override ExprValue Eval(ExprValue[] vars)
            {
                var v = _of.Eval(vars);
                if (_op == '!')
                    return v.Number == 0f ? 1f : 0f;
                return v.IsVector ? new ExprValue(-v.V) : new ExprValue(-v.Number);
            }
        }

        private sealed class Binary : Node
        {
            private readonly Node _a, _b;
            private readonly string _op;
            public Binary(string op, Node a, Node b) { _op = op; _a = a; _b = b; }

            public override ExprValue Eval(ExprValue[] vars)
            {
                // && and || only work out their right side when they need to.
                if (_op == "&&")
                    return _a.Eval(vars).Number != 0f && _b.Eval(vars).Number != 0f ? 1f : 0f;
                if (_op == "||")
                    return _a.Eval(vars).Number != 0f || _b.Eval(vars).Number != 0f ? 1f : 0f;
                var a = _a.Eval(vars);
                var b = _b.Eval(vars);
                switch (_op)
                {
                    case "<": return a.Number < b.Number ? 1f : 0f;
                    case ">": return a.Number > b.Number ? 1f : 0f;
                    case "<=": return a.Number <= b.Number ? 1f : 0f;
                    case ">=": return a.Number >= b.Number ? 1f : 0f;
                    case "==": return Mathf.Abs(a.Number - b.Number) < 1e-6f ? 1f : 0f;
                    case "!=": return Mathf.Abs(a.Number - b.Number) >= 1e-6f ? 1f : 0f;
                }
                if (!a.IsVector && !b.IsVector)
                    return Apply(_op, a.Number, b.Number);
                var x = a.Vector;
                var y = b.Vector;
                return new Vector3(Apply(_op, x.x, y.x), Apply(_op, x.y, y.y), Apply(_op, x.z, y.z));
            }

            private static float Apply(string op, float a, float b) => op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => b != 0f ? a / b : 0f,
                "%" => b != 0f ? a - b * Mathf.Floor(a / b) : 0f,
                "^" => Mathf.Pow(a, b),
                _ => 0f,
            };
        }

        private sealed class Conditional : Node
        {
            private readonly Node _if, _then, _else;
            public Conditional(Node condition, Node then, Node otherwise) { _if = condition; _then = then; _else = otherwise; }
            public override ExprValue Eval(ExprValue[] vars) => _if.Eval(vars).Number != 0f ? _then.Eval(vars) : _else.Eval(vars);
        }

        private sealed class Call : Node
        {
            private readonly Func<ExprValue[], ExprValue> _function;
            private readonly Node[] _args;
            public Call(Func<ExprValue[], ExprValue> function, Node[] args) { _function = function; _args = args; }

            public override ExprValue Eval(ExprValue[] vars)
            {
                var values = new ExprValue[_args.Length];
                for (int i = 0; i < _args.Length; i++)
                    values[i] = _args[i].Eval(vars);
                return _function(values);
            }
        }

        #endregion

        #region Functions

        private static readonly Dictionary<string, (int min, int max, Func<ExprValue[], ExprValue> function)> Functions = new()
        {
            ["sin"] = (1, 1, a => Each(a[0], Mathf.Sin)),
            ["cos"] = (1, 1, a => Each(a[0], Mathf.Cos)),
            ["tan"] = (1, 1, a => Each(a[0], Mathf.Tan)),
            ["asin"] = (1, 1, a => Each(a[0], v => Mathf.Asin(Mathf.Clamp(v, -1f, 1f)))),
            ["acos"] = (1, 1, a => Each(a[0], v => Mathf.Acos(Mathf.Clamp(v, -1f, 1f)))),
            ["atan"] = (1, 1, a => Each(a[0], Mathf.Atan)),
            ["atan2"] = (2, 2, a => Mathf.Atan2(a[0].Number, a[1].Number)),
            ["sqrt"] = (1, 1, a => Each(a[0], v => Mathf.Sqrt(Mathf.Max(0f, v)))),
            ["abs"] = (1, 1, a => Each(a[0], Mathf.Abs)),
            ["floor"] = (1, 1, a => Each(a[0], Mathf.Floor)),
            ["ceil"] = (1, 1, a => Each(a[0], Mathf.Ceil)),
            ["round"] = (1, 1, a => Each(a[0], Mathf.Round)),
            ["frac"] = (1, 1, a => Each(a[0], v => v - Mathf.Floor(v))),
            ["sign"] = (1, 1, a => Each(a[0], v => v > 0f ? 1f : v < 0f ? -1f : 0f)),
            ["exp"] = (1, 1, a => Each(a[0], Mathf.Exp)),
            ["log"] = (1, 1, a => Each(a[0], v => v > 0f ? Mathf.Log(v) : 0f)),
            ["radians"] = (1, 1, a => Each(a[0], v => v * Mathf.Deg2Rad)),
            ["degrees"] = (1, 1, a => Each(a[0], v => v * Mathf.Rad2Deg)),
            ["min"] = (2, 2, a => Each2(a[0], a[1], Mathf.Min)),
            ["max"] = (2, 2, a => Each2(a[0], a[1], Mathf.Max)),
            ["pow"] = (2, 2, a => Each2(a[0], a[1], Mathf.Pow)),
            ["step"] = (2, 2, a => Each2(a[0], a[1], (edge, v) => v >= edge ? 1f : 0f)),
            ["clamp"] = (3, 3, a => Each3(a[0], a[1], a[2], Mathf.Clamp)),
            ["lerp"] = (3, 3, a => a[0].IsVector || a[1].IsVector
                ? new ExprValue(a[0].Vector + (a[1].Vector - a[0].Vector) * a[2].Number)
                : new ExprValue(a[0].Number + (a[1].Number - a[0].Number) * a[2].Number)),
            ["smoothstep"] = (3, 3, a =>
            {
                float t = Mathf.Clamp01((a[2].Number - a[0].Number) / Mathf.Max(1e-6f, a[1].Number - a[0].Number));
                return t * t * (3f - 2f * t);
            }),
            ["vec"] = (1, 3, a => a.Length == 1 ? new ExprValue(a[0].Vector)
                : new ExprValue(new Vector3(a[0].Number, a[1].Number, a.Length > 2 ? a[2].Number : 0f))),
            ["length"] = (1, 1, a => a[0].IsVector ? a[0].V.magnitude : Mathf.Abs(a[0].Number)),
            ["normalize"] = (1, 1, a => new ExprValue(a[0].Vector.normalized)),
            ["dot"] = (2, 2, a => Vector3.Dot(a[0].Vector, a[1].Vector)),
            ["cross"] = (2, 2, a => new ExprValue(Vector3.Cross(a[0].Vector, a[1].Vector))),
            ["distance"] = (2, 2, a => (a[0].Vector - a[1].Vector).magnitude),
            // Smooth noise, about -1 to 1: noise(position) or noise(position, seed).
            ["noise"] = (1, 2, a => Noise.At(a[0].Vector, a.Length > 1 ? Mathf.RoundToInt(a[1].Number) : 0)),
            // Layered noise: fbm(position, octaves) or fbm(position, octaves, seed).
            ["fbm"] = (1, 3, a => Noise.Fractal(a[0].Vector, a.Length > 2 ? Mathf.RoundToInt(a[2].Number) : 0, a.Length > 1 ? Mathf.Clamp(Mathf.RoundToInt(a[1].Number), 1, 8) : 3)),
            // A random number 0 to 1 that's always the same for the same input: rand(i), rand(i, seed), rand(p).
            ["rand"] = (1, 2, a => Hash01(a[0].Vector, a.Length > 1 ? a[1].Number : 0f)),
        };

        private static ExprValue Each(ExprValue a, Func<float, float> f)
            => a.IsVector ? new ExprValue(new Vector3(f(a.V.x), f(a.V.y), f(a.V.z))) : new ExprValue(f(a.Number));

        private static ExprValue Each2(ExprValue a, ExprValue b, Func<float, float, float> f)
        {
            if (!a.IsVector && !b.IsVector)
                return f(a.Number, b.Number);
            Vector3 x = a.Vector, y = b.Vector;
            return new Vector3(f(x.x, y.x), f(x.y, y.y), f(x.z, y.z));
        }

        private static ExprValue Each3(ExprValue a, ExprValue b, ExprValue c, Func<float, float, float, float> f)
        {
            if (!a.IsVector && !b.IsVector && !c.IsVector)
                return f(a.Number, b.Number, c.Number);
            Vector3 x = a.Vector, y = b.Vector, z = c.Vector;
            return new Vector3(f(x.x, y.x, z.x), f(x.y, y.y, z.y), f(x.z, y.z, z.z));
        }

        private static float Hash01(Vector3 v, float seed)
        {
            unchecked
            {
                uint h = (uint)(v.x * 73856.093f) ^ (uint)(v.y * 19349.663f) * 31u ^ (uint)(v.z * 83492.791f) * 17u ^ (uint)(seed * 2654.435761f) * 2246822519u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        #endregion

        #region Reading

        private sealed class Parser
        {
            private readonly string _text;
            private readonly IReadOnlyList<string> _variables;
            private readonly IReadOnlyDictionary<string, float> _constants;
            private int _at;

            public Parser(string text, IReadOnlyList<string> variables, IReadOnlyDictionary<string, float> constants)
            {
                _text = text;
                _variables = variables ?? Array.Empty<string>();
                _constants = constants;
            }

            public Node ParseAll()
            {
                var node = Ternary();
                Skip();
                if (_at < _text.Length)
                    throw new FormatException($"didn't expect \"{_text.Substring(_at)}\".");
                return node;
            }

            private void Skip()
            {
                while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                    _at++;
            }

            private bool Take(string token)
            {
                Skip();
                if (string.CompareOrdinal(_text, _at, token, 0, token.Length) != 0)
                    return false;
                // "<" isn't the start of "<=".
                if (token.Length == 1 && _at + 1 < _text.Length && "<>=!".IndexOf(token[0]) >= 0 && _text[_at + 1] == '=')
                    return false;
                _at += token.Length;
                return true;
            }

            private Node Ternary()
            {
                var condition = Or();
                if (!Take("?"))
                    return condition;
                var then = Ternary();
                if (!Take(":"))
                    throw new FormatException("a \"?\" needs a \":\" (condition ? this : that).");
                var otherwise = Ternary();
                return new Conditional(condition, then, otherwise);
            }

            private Node Or()
            {
                var node = And();
                while (Take("||"))
                    node = new Binary("||", node, And());
                return node;
            }

            private Node And()
            {
                var node = Compare();
                while (Take("&&"))
                    node = new Binary("&&", node, Compare());
                return node;
            }

            private Node Compare()
            {
                var node = Sum();
                while (true)
                {
                    string op = Take("<=") ? "<=" : Take(">=") ? ">=" : Take("==") ? "==" : Take("!=") ? "!=" : Take("<") ? "<" : Take(">") ? ">" : null;
                    if (op == null)
                        return node;
                    node = new Binary(op, node, Sum());
                }
            }

            private Node Sum()
            {
                var node = Product();
                while (true)
                {
                    if (Take("+")) node = new Binary("+", node, Product());
                    else if (Take("-")) node = new Binary("-", node, Product());
                    else return node;
                }
            }

            private Node Product()
            {
                var node = Prefix();
                while (true)
                {
                    if (Take("*")) node = new Binary("*", node, Prefix());
                    else if (Take("/")) node = new Binary("/", node, Prefix());
                    else if (Take("%")) node = new Binary("%", node, Prefix());
                    else return node;
                }
            }

            private Node Prefix()
            {
                if (Take("-")) return new Unary('-', Prefix());
                if (Take("+")) return Prefix();
                if (Take("!")) return new Unary('!', Prefix());
                return Power();
            }

            private Node Power()
            {
                var node = Postfix();
                if (Take("^"))
                    node = new Binary("^", node, Prefix());
                return node;
            }

            private Node Postfix()
            {
                var node = Primary();
                while (Take("."))
                {
                    string part = Name();
                    int axis = part switch { "x" or "r" => 0, "y" or "g" => 1, "z" or "b" => 2, _ => -1 };
                    if (axis < 0)
                        throw new FormatException($"a vector has .x, .y and .z, not .{part}.");
                    node = new Component(node, axis);
                }
                return node;
            }

            private Node Primary()
            {
                Skip();
                if (_at >= _text.Length)
                    throw new FormatException("it ends too soon.");
                char c = _text[_at];
                if (char.IsDigit(c) || c == '.')
                {
                    int start = _at;
                    while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] == '.'))
                        _at++;
                    if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E') && _at + 1 < _text.Length && (char.IsDigit(_text[_at + 1]) || _text[_at + 1] == '-'))
                    {
                        _at += 2;
                        while (_at < _text.Length && char.IsDigit(_text[_at]))
                            _at++;
                    }
                    if (!float.TryParse(_text.Substring(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        throw new FormatException($"\"{_text.Substring(start, _at - start)}\" isn't a number.");
                    return new Constant(number);
                }
                if (Take("("))
                {
                    var inner = Ternary();
                    if (!Take(")"))
                        throw new FormatException("a \"(\" isn't closed.");
                    return inner;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    string name = Name();
                    if (Take("("))
                    {
                        var args = new List<Node>();
                        if (!Take(")"))
                        {
                            do args.Add(Ternary()); while (Take(","));
                            if (!Take(")"))
                                throw new FormatException($"{name}( isn't closed.");
                        }
                        if (!Functions.TryGetValue(name.ToLowerInvariant(), out var function))
                            throw new FormatException($"there's no function called {name}.");
                        if (args.Count < function.min || args.Count > function.max)
                            throw new FormatException(function.min == function.max
                                ? $"{name} takes {function.min} value(s), not {args.Count}."
                                : $"{name} takes {function.min} to {function.max} values, not {args.Count}.");
                        return new Call(function.function, args.ToArray());
                    }
                    for (int i = 0; i < _variables.Count; i++)
                        if (_variables[i] == name)
                            return new Variable(i);
                    switch (name.ToLowerInvariant())
                    {
                        case "pi": return new Constant(Mathf.PI);
                        case "tau": return new Constant(Mathf.PI * 2f);
                        case "e": return new Constant((float)Math.E);
                    }
                    if (_constants != null)
                        foreach (var entry in _constants)
                            if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                                return new Constant(entry.Value);
                    throw new FormatException($"there's nothing called {name} (it can use {string.Join(", ", _variables)}, the graph's Parameters, pi and functions).");
                }
                throw new FormatException($"didn't expect \"{c}\".");
            }

            private string Name()
            {
                Skip();
                int start = _at;
                while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_'))
                    _at++;
                if (_at == start)
                    throw new FormatException("expected a name.");
                return _text.Substring(start, _at - start);
            }
        }

        #endregion
    }
}
