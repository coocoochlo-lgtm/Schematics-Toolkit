using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// One step of a mesh plan: makes its mesh (on whatever thread runs the plan) the first time it's asked, and
    /// remembers it, so a mesh used by two nodes is made once. Steps that change a mesh take a <see cref="Copy"/>.
    /// </summary>
    public sealed class MeshOp
    {
        private readonly Func<MeshData> _make;
        private MeshData _result;

        public MeshOp(Func<MeshData> make) => _make = make;

        /// <summary>Nothing (an input with nothing wired in).</summary>
        public static MeshOp Empty => new(() => new MeshData());

        /// <summary>The mesh (shared: don't change it).</summary>
        public MeshData Get() => _result ??= _make() ?? new MeshData();

        /// <summary>A copy of the mesh to change.</summary>
        public MeshData Copy() => Get().Clone();
    }

    /// <summary>
    /// A Mesh Graph read into a plan, with everything it needs from the graph (settings, Parameters, sampled curves)
    /// copied out, so <see cref="Run"/> can make the model and its LODs on a background thread.
    /// </summary>
    public sealed class MeshPlan
    {
        public MeshOp Root;
        public bool GenerateLODs;
        public float[] LODQualities = { 1f };
        /// <summary>The screen height (fraction) below which each level gives way to the next; the last is the cull height.</summary>
        public float[] ScreenHeights = { 0.01f };
        public float SmoothAngle = -1f;
        public readonly List<string> Warnings = new();

        /// <summary>Makes the model and its LODs (any thread).</summary>
        public List<MeshData> Run()
        {
            var model = Root != null ? Root.Get() : new MeshData();
            if (SmoothAngle >= 0f)
            {
                model = model.Clone();
                model.RecalculateNormals(SmoothAngle);
            }
            var levels = new List<MeshData> { model };
            if (GenerateLODs)
                for (int i = 1; i < LODQualities.Length; i++)
                    levels.Add(MeshSimplifier.Simplify(model, LODQualities[i]));
            return levels;
        }
    }

    /// <summary>
    /// Reads a Mesh Graph into a <see cref="MeshPlan"/> (main thread): each node's settings and number inputs are
    /// read now, with the object's Parameters, and its mesh work is put off into a <see cref="MeshOp"/>.
    /// </summary>
    public sealed class MeshCompiler
    {
        [ThreadStatic] private static MeshCompiler _current;

        /// <summary>The compiler reading a graph right now (Parameter nodes read their values from it).</summary>
        public static MeshCompiler Current => _current;

        private readonly IReadOnlyDictionary<string, float> _parameters;
        private readonly Dictionary<MeshNode, MeshOp> _ops = new();
        private readonly Dictionary<MeshPointsNode, PointOp> _pointOps = new();
        private readonly Dictionary<string, float> _constants = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<MeshNode> _building = new();

        /// <summary>Problems found while reading the graph (shown in the graph window).</summary>
        public readonly List<string> Warnings = new();

        private MeshCompiler(IReadOnlyDictionary<string, float> parameters) => _parameters = parameters;

        /// <summary>Reads a Mesh Graph with these Parameter values (null: their defaults).</summary>
        public static MeshPlan Compile(MeshGraph graph, IReadOnlyDictionary<string, float> parameters = null)
        {
            var plan = new MeshPlan();
            var output = graph != null ? graph.Output : null;
            if (output == null)
            {
                plan.Root = MeshOp.Empty;
                plan.Warnings.Add("The Mesh Graph has no Output.");
                return plan;
            }
            var compiler = new MeshCompiler(parameters ?? new Dictionary<string, float>());
            // The graph's Parameters, as formulas see them (set values, or defaults).
            foreach (var (name, value) in graph.ParameterList)
                compiler._constants[name] = compiler.Parameter(name, value);
            var previous = _current;
            _current = compiler;
            try
            {
                plan.Root = output.Compile(compiler);
                plan.GenerateLODs = output.GenerateLODs;
                plan.LODQualities = output.GenerateLODs
                    ? new[] { 1f, Mathf.Clamp(output.LOD1Quality, 0.01f, 1f), Mathf.Clamp(output.LOD2Quality, 0.01f, 1f) }
                    : new[] { 1f };
                plan.ScreenHeights = output.GenerateLODs
                    ? new[] { Mathf.Clamp01(output.LOD0Until), Mathf.Clamp01(output.LOD1Until), Mathf.Clamp01(output.CullBelow) }
                    : new[] { Mathf.Clamp01(output.CullBelow) };
                plan.SmoothAngle = output.SmoothAngle;
                plan.Warnings.AddRange(compiler.Warnings);
            }
            finally
            {
                _current = previous;
            }
            return plan;
        }

        /// <summary>A Parameter's value for this object (its default when not set).</summary>
        public float Parameter(string name, float defaultValue)
        {
            name = (name ?? "").Trim();
            foreach (var entry in _parameters)
                if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            return defaultValue;
        }

        /// <summary>The step making a node's mesh (each node made once).</summary>
        public MeshOp Step(MeshNode node)
        {
            if (_ops.TryGetValue(node, out var op))
                return op;
            if (!_building.Add(node))
            {
                Warnings.Add($"\"{node.Name}\" feeds into itself, which can't be made.");
                return MeshOp.Empty;
            }
            try
            {
                op = node.Compile(this) ?? MeshOp.Empty;
            }
            catch (Exception exception)
            {
                Warnings.Add($"\"{node.Name}\": {exception.Message}");
                op = MeshOp.Empty;
            }
            _building.Remove(node);
            _ops[node] = op;
            return op;
        }

        /// <summary>The graph's Parameters by name, with this object's values (for formulas).</summary>
        public IReadOnlyDictionary<string, float> Constants => _constants;

        /// <summary>
        /// Reads a formula typed on a node, which can use <paramref name="variables"/> (given per point or vertex) and the
        /// graph's Parameters. Null (with a warning naming the node) if it can't be read.
        /// </summary>
        public MeshExpression Expression(SchematicGraphNode node, string text, params string[] variables)
        {
            var expression = MeshExpression.Parse(text, variables, _constants, out string error);
            if (expression == null)
                Warnings.Add($"\"{node.Name}\": {error}");
            return expression;
        }

        /// <summary>The points wired into an input (none, if nothing is).</summary>
        public PointOp Points(MeshNode node, string port)
        {
            var input = node.GetPort(port);
            if (input != null)
                foreach (var source in input.ConnectedPorts)
                    if (source?.Node is MeshPointsNode sourceNode)
                        return StepPoints(sourceNode);
            return PointOp.Empty;
        }

        /// <summary>Every set of points wired into an input.</summary>
        public List<PointOp> AllPoints(MeshNode node, string port)
        {
            var found = new List<PointOp>();
            var input = node.GetPort(port);
            if (input != null)
                foreach (var source in input.ConnectedPorts)
                    if (source?.Node is MeshPointsNode sourceNode)
                        found.Add(StepPoints(sourceNode));
            return found;
        }

        private PointOp StepPoints(MeshPointsNode node)
        {
            if (_pointOps.TryGetValue(node, out var op))
                return op;
            if (!_building.Add(node))
            {
                Warnings.Add($"\"{node.Name}\" feeds into itself, which can't be made.");
                return PointOp.Empty;
            }
            try
            {
                op = node.CompilePoints(this) ?? PointOp.Empty;
            }
            catch (Exception exception)
            {
                Warnings.Add($"\"{node.Name}\": {exception.Message}");
                op = PointOp.Empty;
            }
            _building.Remove(node);
            _pointOps[node] = op;
            return op;
        }

        /// <summary>The mesh wired into an input (nothing, if nothing is).</summary>
        public MeshOp Mesh(MeshNode node, string port)
        {
            var input = node.GetPort(port);
            if (input != null)
                foreach (var source in input.ConnectedPorts)
                    if (source?.Node is MeshNode sourceNode)
                        return Step(sourceNode);
            return MeshOp.Empty;
        }

        /// <summary>Every mesh wired into an input (a merge takes several).</summary>
        public List<MeshOp> Meshes(MeshNode node, string port)
        {
            var found = new List<MeshOp>();
            var input = node.GetPort(port);
            if (input != null)
                foreach (var source in input.ConnectedPorts)
                    if (source?.Node is MeshNode sourceNode)
                        found.Add(Step(sourceNode));
            return found;
        }

        /// <summary>A number input: what's wired in (a Parameter, a value node...), or the number typed on the node.</summary>
        public float Number(SchematicGraphNode node, string port, float constant)
            => Connected(node, port, out object value) ? ToFloat(value, constant) : constant;

        public int Integer(SchematicGraphNode node, string port, int constant)
            => Connected(node, port, out object value) ? Mathf.RoundToInt(ToFloat(value, constant)) : constant;

        public Vector3 Vector(SchematicGraphNode node, string port, Vector3 constant)
        {
            if (!Connected(node, port, out object value))
                return constant;
            return value switch
            {
                Vector3 v3 => v3,
                Vector2 v2 => v2,
                Vector4 v4 => v4,
                _ => Vector3.one * ToFloat(value, 1f),
            };
        }

        public Color ColorValue(SchematicGraphNode node, string port, Color constant)
            => Connected(node, port, out object value) && value is Color c ? c : constant;

        private bool Connected(SchematicGraphNode node, string port, out object value)
        {
            value = null;
            var input = node.GetPort(port);
            if (input == null)
                return false;
            foreach (var source in input.ConnectedPorts)
            {
                // A value (not a mesh): a Parameter, a Random, or any value node (Float Value...), read once now.
                if (source?.Node is SchematicGraphNode valueNode && source.Type != typeof(MeshData) && source.Type != typeof(PointData))
                {
                    try
                    {
                        value = valueNode.OnRequestValue(source);
                        return value != null;
                    }
                    catch (Exception exception)
                    {
                        Warnings.Add($"\"{source.Node.Name}\" couldn't give a value: {exception.Message}");
                        return false;
                    }
                }
            }
            return false;
        }

        private static float ToFloat(object value, float fallback) => value switch
        {
            float f => f,
            int i => i,
            bool b => b ? 1f : 0f,
            double d => (float)d,
            Remedy.Framework.Union u => SignalArgs.FromUnion<float>(u),
            IConvertible convertible => Convert.ToSingle(convertible, System.Globalization.CultureInfo.InvariantCulture),
            _ => fallback,
        };
    }
}
