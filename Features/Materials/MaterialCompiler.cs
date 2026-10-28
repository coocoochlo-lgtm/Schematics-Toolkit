using BlueGraph;
using Remedy.Meshes;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Remedy.Materials
{
    /// <summary>What a Smart Material makes: its material, texel by texel, and the surface it was made on.</summary>
    public sealed class MaterialResult
    {
        public MaterialMaps Maps;
        public MaterialSurface Surface;
        public List<string> Warnings;
    }

    /// <summary>A Smart Material read into a plan: <see cref="Run"/> makes it, on any thread.</summary>
    public sealed class MaterialPlan
    {
        public Func<object> Root;
        public Func<MaterialSurface> Surface;
        public int Width, Height;
        public readonly List<string> Warnings = new();

        public MaterialResult Run()
        {
            var maps = Root?.Invoke() as MaterialMaps ?? MaterialMaps.Default(Width, Height);
            return new MaterialResult { Maps = maps, Surface = Surface(), Warnings = Warnings };
        }
    }

    /// <summary>
    /// Reads a Smart Material into a <see cref="MaterialPlan"/> (main thread): each node's settings, numbers and
    /// painted textures are read now; its texel work is put off until the plan runs. Each node output is worked out
    /// once, however many nodes use it.
    /// </summary>
    public sealed class MaterialCompiler
    {
        [ThreadStatic] private static MaterialCompiler _current;

        /// <summary>The compiler reading a graph right now (Parameter nodes read their values from it).</summary>
        public static MaterialCompiler Current => _current;

        /// <summary>The size of the maps it makes.</summary>
        public readonly int Width, Height;

        private readonly IReadOnlyDictionary<string, float> _parameters;
        private readonly Dictionary<(MaterialNode, string), Func<object>> _steps = new();
        private readonly HashSet<(MaterialNode, string)> _building = new();
        private readonly Lazy<MaterialSurface> _surface;
        private List<string> _late;

        /// <summary>Problems found while reading the graph.</summary>
        public readonly List<string> Warnings = new();

        private MaterialCompiler(int width, int height, Func<MaterialSurface> surface, IReadOnlyDictionary<string, float> parameters)
        {
            Width = width;
            Height = height;
            _parameters = parameters ?? new Dictionary<string, float>();
            _surface = new Lazy<MaterialSurface>(surface, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>The surface the maps are laid out on (made the first time it's asked for, on the plan's thread).</summary>
        public MaterialSurface Surface => _surface.Value;

        /// <summary>
        /// Reads a Smart Material, to make it <paramref name="size"/> texels square (0: the Output's Resolution), with
        /// these Parameter values (null: their defaults).
        /// </summary>
        public static MaterialPlan Compile(SmartMaterialGraph graph, int size = 0, IReadOnlyDictionary<string, float> parameters = null)
        {
            var output = graph != null ? graph.Output : null;
            size = MaterialOutputNode.SnapSize(size > 0 ? size : output != null ? output.Resolution : 512);
            var surface = SurfaceSource(output != null ? output.Mesh : null, size, out string problem);
            var compiler = new MaterialCompiler(size, size, surface, parameters);
            var plan = new MaterialPlan { Width = size, Height = size, Surface = () => compiler.Surface };
            if (problem != null)
                plan.Warnings.Add(problem);
            if (output == null)
            {
                plan.Warnings.Add("The Smart Material has no Output.");
                return plan;
            }
            var previous = _current;
            _current = compiler;
            try
            {
                plan.Root = compiler.Step(output, MaterialNode.MaterialPort);
                plan.Warnings.AddRange(compiler.Warnings);
                compiler._late = plan.Warnings;
            }
            finally
            {
                _current = previous;
            }
            return plan;
        }

        /// <summary>A Parameter's value (its default when not set).</summary>
        public float Parameter(string name, float defaultValue)
        {
            name = (name ?? "").Trim();
            foreach (var entry in _parameters)
                if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            return defaultValue;
        }

        /// <summary>A problem found while the plan runs (any thread).</summary>
        public void RunWarning(string warning)
        {
            var list = _late ?? Warnings;
            lock (list)
                if (!list.Contains(warning))
                    list.Add(warning);
        }

        // ---- Steps ----

        /// <summary>The work making one of a node's outputs (each made once; a node feeding into itself gives nothing).</summary>
        public Func<object> Step(MaterialNode node, string output)
        {
            var key = (node, output);
            if (_steps.TryGetValue(key, out var step))
                return step;
            if (!_building.Add(key))
            {
                Warnings.Add($"\"{node.Name}\" feeds into itself, which can't be made.");
                return null;
            }
            try
            {
                var make = node.Compile(this, output);
                if (make != null)
                {
                    // Made once, the first time it's asked for, whichever thread asks.
                    var once = new Lazy<object>(() =>
                    {
                        try
                        {
                            return make();
                        }
                        catch (Exception exception)
                        {
                            RunWarning($"\"{node.Name}\": {exception.Message}");
                            return null;
                        }
                    }, LazyThreadSafetyMode.ExecutionAndPublication);
                    step = () => once.Value;
                }
            }
            catch (Exception exception)
            {
                Warnings.Add($"\"{node.Name}\": {exception.Message}");
                step = null;
            }
            _building.Remove(key);
            _steps[key] = step;
            return step;
        }

        private bool Source(SchematicGraphNode node, string port, out MaterialNode source, out string sourcePort)
        {
            source = null;
            sourcePort = null;
            var input = node.GetPort(port);
            if (input == null)
                return false;
            foreach (var connected in input.ConnectedPorts)
                if (connected?.Node is MaterialNode found && (connected.Type == typeof(MaterialMaps) || connected.Type == typeof(MaskMap)))
                {
                    source = found;
                    sourcePort = connected.Name;
                    return true;
                }
            return false;
        }

        /// <summary>The material wired into an input (plain grey if nothing is).</summary>
        public Func<MaterialMaps> Material(SchematicGraphNode node, string port)
        {
            int width = Width, height = Height;
            if (Source(node, port, out var source, out var sourcePort))
            {
                var step = Step(source, sourcePort);
                if (step != null)
                    return () => step() switch
                    {
                        MaterialMaps maps => maps,
                        // A mask wired into a material: grey, by its values.
                        MaskMap mask => FromMask(mask),
                        _ => MaterialMaps.Default(width, height),
                    };
            }
            return () => MaterialMaps.Default(width, height);
        }

        /// <summary>Whether anything's wired into an input.</summary>
        public bool IsConnected(SchematicGraphNode node, string port) => Source(node, port, out _, out _) || Connected(node, port, out _);

        /// <summary>
        /// The mask wired into an input; a number wired in (a Parameter) or typed on the node (<paramref name="constant"/>)
        /// makes it the same everywhere.
        /// </summary>
        public Func<MaskMap> Mask(SchematicGraphNode node, string port, float constant)
        {
            int width = Width, height = Height;
            if (Source(node, port, out var source, out var sourcePort))
            {
                var step = Step(source, sourcePort);
                if (step != null)
                    return () => step() switch
                    {
                        MaskMap mask => mask,
                        // A material wired into a mask: how bright its colour is.
                        MaterialMaps maps => ToMask(maps),
                        _ => MaskMap.Constant(width, height, constant),
                    };
            }
            float value = Number(node, port, constant);
            return () => MaskMap.Constant(width, height, value);
        }

        private static MaterialMaps FromMask(MaskMap mask)
        {
            var maps = MaterialMaps.Default(mask.Width, mask.Height);
            for (int i = 0; i < maps.Count; i++)
            {
                float v = mask.Values[i];
                maps.BaseColor[i] = new Color(v, v, v, 1f);
            }
            return maps;
        }

        private static MaskMap ToMask(MaterialMaps maps)
        {
            var mask = new MaskMap(maps.Width, maps.Height);
            for (int i = 0; i < maps.Count; i++)
                mask.Values[i] = maps.BaseColor[i].grayscale;
            return mask;
        }

        // ---- Numbers ----

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
                if (source?.Node is SchematicGraphNode valueNode && source.Type != typeof(MaterialMaps) && source.Type != typeof(MaskMap))
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
            IConvertible convertible => Convert.ToSingle(convertible, System.Globalization.CultureInfo.InvariantCulture),
            _ => fallback,
        };

        // ---- The mesh it's made for ----

        private static readonly Dictionary<string, MaterialSurface> _surfaces = new();
        private static readonly List<string> _surfaceOrder = new();

        /// <summary>
        /// The work giving the surface for a mesh source (a Mesh Graph, a Mesh, a model; null: flat) at a size. Read on
        /// the main thread; laid out when first asked for, and kept, so later plans for the same mesh reuse it and its
        /// bakes.
        /// </summary>
        public static Func<MaterialSurface> SurfaceSource(UnityEngine.Object source, int size, out string problem)
        {
            problem = null;
            if (source == null)
            {
                string key = $"flat|{size}";
                return () => Cached(key, () => MaterialSurface.Flat(size, size));
            }
            if (source is MeshGraph graph)
            {
                string key = $"graph|{graph.GetEntityId()}|{graph.Version}|{size}";
                lock (_surfaces)
                    if (_surfaces.TryGetValue(key, out var ready))
                        return () => ready;
                var plan = MeshCompiler.Compile(graph);
                return () => Cached(key, () =>
                {
                    var levels = plan.Run();
                    return MaterialSurface.FromMesh(levels.Count > 0 ? levels[0] : null, size, size);
                });
            }
            if (source is UnityEngine.Mesh || source is GameObject || source is Component)
            {
                var data = MeshSource(source, out string key);
                key += "|" + size;
                if (data.VertexCount == 0)
                    problem = $"{source.name} has no mesh to read: the material's made flat.";
                return () => Cached(key, () => MaterialSurface.FromMesh(data, size, size));
            }
            problem = $"{source.name} isn't a Mesh Graph, a mesh or a model: the material's made flat.";
            return () => MaterialSurface.Flat(size, size);
        }

        private static readonly Dictionary<string, MeshData> _meshes = new();

        // A Mesh or model as mesh data (main thread), kept while it's unchanged.
        private static MeshData MeshSource(UnityEngine.Object source, out string key)
        {
            var mesh = source as UnityEngine.Mesh;
            var go = source as GameObject ?? (source as Component)?.gameObject;
            int vertices = mesh != null ? mesh.vertexCount : 0;
            if (go != null)
            {
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null) vertices = vertices * 31 + filter.sharedMesh.vertexCount;
                foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (skinned.sharedMesh != null) vertices = vertices * 31 + skinned.sharedMesh.vertexCount;
            }
            key = $"mesh|{source.GetEntityId()}|{vertices}";
            lock (_meshes)
                if (_meshes.TryGetValue(key, out var known))
                    return known;
            var data = MeshModelNode.Read(source, true);
            lock (_meshes)
            {
                if (_meshes.Count > 8)
                    _meshes.Clear();
                _meshes[key] = data;
            }
            return data;
        }

        private static MaterialSurface Cached(string key, Func<MaterialSurface> make)
        {
            lock (_surfaces)
                if (_surfaces.TryGetValue(key, out var ready))
                    return ready;
            var surface = make();
            lock (_surfaces)
            {
                _surfaces[key] = surface;
                _surfaceOrder.Remove(key);
                _surfaceOrder.Add(key);
                // Only the last few (each holds its bakes).
                while (_surfaceOrder.Count > 6)
                {
                    _surfaces.Remove(_surfaceOrder[0]);
                    _surfaceOrder.RemoveAt(0);
                }
            }
            return surface;
        }

        /// <summary>The surface for a graph's mesh at a size, made now (for painting on it).</summary>
        public static MaterialSurface SurfaceFor(SmartMaterialGraph graph, int size)
        {
            var output = graph != null ? graph.Output : null;
            return SurfaceSource(output != null ? output.Mesh : null, size, out _)();
        }
    }
}
