// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    // Points: loop over space (a grid, a line, a circle, a mesh's surface), shape the points with formulas, then stamp
    // a mesh on every one. The procedural way to make fences, colonnades, spiral stairs, studded armour, crowds of rocks.

    /// <summary>A Mesh Graph node that makes points (its output is Points, not a mesh).</summary>
    [Serializable]
    [NodeColor(NodeColors.Math)]
    public abstract class MeshPointsNode : MeshNode
    {
        public const string PointsPort = "Points";

        public override MeshOp Compile(MeshCompiler compiler) => MeshOp.Empty;

        /// <summary>This node's step (main thread): read its settings now, make the points later.</summary>
        public abstract PointOp CompilePoints(MeshCompiler compiler);
    }

    [SchematicDoc(@"{""name"": ""Point Grid"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""A loop over space: Count X by Count Y by Count Z points, Spacing apart (centred on the origin with Centered). A row of 10 is Count 10, 1, 1."", ""inputs"": [{""name"": ""Count X"", ""type"": ""int""}, {""name"": ""Count Y"", ""type"": ""int""}, {""name"": ""Count Z"", ""type"": ""int""}, {""name"": ""Spacing"", ""type"": ""Vector3""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Point Grid", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshPointGridNode : MeshPointsNode
    {
        [Input("Count X", Editable = true)] public int CountX = 5;
        [Input("Count Y", Editable = true)] public int CountY = 1;
        [Input("Count Z", Editable = true)] public int CountZ = 5;
        [Input("Spacing", Editable = true)] public Vector3 Spacing = Vector3.one;
        [Editable] public bool Centered = true;
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        public override PointOp CompilePoints(MeshCompiler c)
        {
            int nx = Mathf.Clamp(c.Integer(this, "Count X", CountX), 1, 1000);
            int ny = Mathf.Clamp(c.Integer(this, "Count Y", CountY), 1, 1000);
            int nz = Mathf.Clamp(c.Integer(this, "Count Z", CountZ), 1, 1000);
            var spacing = c.Vector(this, "Spacing", Spacing);
            bool centered = Centered;
            return new PointOp(() =>
            {
                var points = new PointData();
                var offset = centered ? Vector3.Scale(new Vector3(nx - 1, ny - 1, nz - 1), spacing) * -0.5f : Vector3.zero;
                for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                        for (int x = 0; x < nx; x++)
                            points.Add(offset + Vector3.Scale(new Vector3(x, y, z), spacing));
                return points;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Point Line"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Count points evenly from Start to End (both included), each turned to face along the line."", ""inputs"": [{""name"": ""Count"", ""type"": ""int""}, {""name"": ""Start"", ""type"": ""Vector3""}, {""name"": ""End"", ""type"": ""Vector3""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Point Line", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshPointLineNode : MeshPointsNode
    {
        [Input("Count", Editable = true)] public int Count = 10;
        [Input("Start", Editable = true)] public Vector3 Start;
        [Input("End", Editable = true)] public Vector3 End = new(5f, 0f, 0f);
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        public override PointOp CompilePoints(MeshCompiler c)
        {
            int count = Mathf.Clamp(c.Integer(this, "Count", Count), 1, 100000);
            Vector3 start = c.Vector(this, "Start", Start), end = c.Vector(this, "End", End);
            return new PointOp(() =>
            {
                var points = new PointData();
                var facing = MeshModifiers.EulerFromAxes(end - start, Vector3.up);
                for (int i = 0; i < count; i++)
                    points.Add(Vector3.Lerp(start, end, count > 1 ? i / (float)(count - 1) : 0f), facing, Vector3.one);
                return points;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Point Circle"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Count points round a circle of Radius (lying flat), over Arc degrees (360: all the way), each turned to face outwards with Face Out."", ""inputs"": [{""name"": ""Count"", ""type"": ""int""}, {""name"": ""Radius"", ""type"": ""float""}, {""name"": ""Arc"", ""type"": ""float""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Point Circle", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshPointCircleNode : MeshPointsNode
    {
        [Input("Count", Editable = true)] public int Count = 12;
        [Input("Radius", Editable = true)] public float Radius = 2f;
        [Input("Arc", Editable = true)] public float Arc = 360f;
        [Editable] public bool FaceOut = true;
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        public override PointOp CompilePoints(MeshCompiler c)
        {
            int count = Mathf.Clamp(c.Integer(this, "Count", Count), 1, 100000);
            float radius = c.Number(this, "Radius", Radius), arc = c.Number(this, "Arc", Arc);
            bool faceOut = FaceOut;
            return new PointOp(() =>
            {
                var points = new PointData();
                // A full circle doesn't repeat its first point at the end.
                float step = Mathf.Abs(arc) >= 359.99f ? arc / count : count > 1 ? arc / (count - 1) : 0f;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * step * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    points.Add(outward * radius, faceOut ? MeshModifiers.EulerFromAxes(outward, Vector3.up) : Vector3.zero, Vector3.one);
                }
                return points;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Points On Mesh"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Count points scattered over a mesh's surface (evenly by area, the same for the same Seed), each stood up along the surface with Align. Or, with Vertices, one point at each corner of the mesh."", ""inputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}, {""name"": ""Count"", ""type"": ""int""}, {""name"": ""Seed"", ""type"": ""int""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Points On Mesh", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshPointsOnMeshNode : MeshPointsNode
    {
        [Input("Mesh", Editable = false)] [NonSerialized] public MeshData Mesh;
        [Input("Count", Editable = true)] public int Count = 50;
        [Input("Seed", Editable = true)] public int Seed;
        [Editable, Tooltip("Stand each point up along the surface (off: they all point up).")] public bool Align = true;
        [Editable, Tooltip("A point at every corner of the mesh instead (Count and Seed aren't used).")] public bool Vertices;
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        public override PointOp CompilePoints(MeshCompiler c)
        {
            var mesh = c.Mesh(this, "Mesh");
            int count = Mathf.Clamp(c.Integer(this, "Count", Count), 0, 100000), seed = c.Integer(this, "Seed", Seed);
            bool align = Align, vertices = Vertices;
            return new PointOp(() => vertices ? AtVertices(mesh.Get(), align) : OnSurface(mesh.Get(), count, seed, align));
        }

        private static PointData AtVertices(MeshData mesh, bool align)
        {
            var points = new PointData();
            var seen = new HashSet<Vector3>(new MeshData.PositionComparer());
            for (int i = 0; i < mesh.VertexCount; i++)
                if (seen.Add(mesh.Positions[i]))
                    points.Add(mesh.Positions[i], align ? Standing(mesh.Normals[i]) : Vector3.zero, Vector3.one);
            return points;
        }

        private static PointData OnSurface(MeshData mesh, int count, int seed, bool align)
        {
            var points = new PointData();
            var triangles = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 n)>();
            var cumulative = new List<float>();
            float total = 0f;
            foreach (var slot in mesh.Slots)
                for (int t = 0; t + 2 < slot.Count; t += 3)
                {
                    Vector3 a = mesh.Positions[slot[t]], b = mesh.Positions[slot[t + 1]], c = mesh.Positions[slot[t + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * 0.5f;
                    if (area <= 1e-10f) continue;
                    total += area;
                    triangles.Add((a, b, c, MeshData.Direction(cross)));
                    cumulative.Add(total);
                }
            if (triangles.Count == 0)
                return points;
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                int index = cumulative.BinarySearch((float)random.NextDouble() * total);
                if (index < 0) index = ~index;
                var (a, b, c, n) = triangles[Mathf.Clamp(index, 0, triangles.Count - 1)];
                float u = (float)random.NextDouble(), v = (float)random.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                points.Add(a + (b - a) * u + (c - a) * v, align ? Standing(n) : Vector3.zero, Vector3.one);
            }
            return points;
        }

        // A rotation whose up is the surface's normal.
        private static Vector3 Standing(Vector3 normal)
        {
            var forward = Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.forward;
            // (Forward along the surface: the up direction flattened onto it.)
            return MeshModifiers.EulerFromAxes(Vector3.Cross(Vector3.Cross(normal, forward), normal), normal);
        }
    }

    [SchematicDoc(@"{""name"": ""Point Function"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Reshapes points with formulas worked out for each one: Position (default p), Rotation in degrees (r) and Scale (s), and Keep (drop the point where it's 0). They can use p (position), r, s, x y z (p's parts), i (the point's number), n (how many there are), t (i spread 0 to 1), the graph's Parameters, and functions like sin, noise, rand, lerp. A wave: p + vec(0, sin(x * 2) * 0.5, 0). A spiral: vec(cos(t * tau * 3) * 2, t * 4, sin(t * tau * 3) * 2)."", ""inputs"": [{""name"": ""Points"", ""type"": ""PointData""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Point Function", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshPointFunctionNode : MeshPointsNode
    {
        [Input("In", Editable = false)] [NonSerialized] public PointData In;
        [Editable, Tooltip("Where the point goes. p is where it is.")] public string Position = "p";
        [Editable, Tooltip("Its rotation (Euler degrees). r is its rotation now.")] public string Rotation = "r";
        [Editable, Tooltip("Its scale (a number, or vec(x, y, z)). s is its scale now.")] public string Scale = "s";
        [Editable, Tooltip("Keep the point? 0 drops it (e.g. i % 2 == 0 keeps every other one). Empty keeps them all.")] public string Keep = "";
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        private static readonly string[] Variables = { "p", "r", "s", "x", "y", "z", "i", "n", "t" };

        public override PointOp CompilePoints(MeshCompiler c)
        {
            var input = c.Points(this, "In");
            var position = Parse(c, Position, "p");
            var rotation = Parse(c, Rotation, "r");
            var scale = Parse(c, Scale, "s");
            var keep = string.IsNullOrWhiteSpace(Keep) ? null : c.Expression(this, Keep, Variables);
            return new PointOp(() =>
            {
                var source = input.Get();
                var result = new PointData();
                int n = source.Count;
                var vars = new ExprValue[Variables.Length];
                for (int i = 0; i < n; i++)
                {
                    var p = source.Positions[i];
                    vars[0] = p;
                    vars[1] = source.Rotations[i];
                    vars[2] = source.Scales[i];
                    vars[3] = p.x; vars[4] = p.y; vars[5] = p.z;
                    vars[6] = i;
                    vars[7] = n;
                    vars[8] = n > 1 ? i / (float)(n - 1) : 0f;
                    if (keep != null && keep.Evaluate(vars).Number == 0f)
                        continue;
                    result.Add(
                        position != null ? position.Evaluate(vars).Vector : p,
                        rotation != null ? rotation.Evaluate(vars).Vector : source.Rotations[i],
                        scale != null ? scale.Evaluate(vars).Vector : source.Scales[i]);
                }
                return result;
            });
        }

        private MeshExpression Parse(MeshCompiler c, string text, string unchanged)
            => string.IsNullOrWhiteSpace(text) || text.Trim() == unchanged ? null : c.Expression(this, text, Variables);
    }

    [SchematicDoc(@"{""name"": ""Point Jitter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Shakes points up at random (the same for the same Seed): moved up to Offset each way, turned up to Rotation degrees each way, and scaled between Min Scale and Max Scale. Makes stamped copies look placed by hand."", ""inputs"": [{""name"": ""In"", ""type"": ""PointData""}, {""name"": ""Offset"", ""type"": ""Vector3""}, {""name"": ""Rotation"", ""type"": ""Vector3""}, {""name"": ""Min Scale"", ""type"": ""float""}, {""name"": ""Max Scale"", ""type"": ""float""}, {""name"": ""Seed"", ""type"": ""int""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Point Jitter", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshPointJitterNode : MeshPointsNode
    {
        [Input("In", Editable = false)] [NonSerialized] public PointData In;
        [Input("Offset", Editable = true)] public Vector3 Offset = new(0.2f, 0f, 0.2f);
        [Input("Rotation", Editable = true)] public Vector3 Rotation = new(0f, 180f, 0f);
        [Input("Min Scale", Editable = true)] public float MinScale = 0.8f;
        [Input("Max Scale", Editable = true)] public float MaxScale = 1.2f;
        [Input("Seed", Editable = true)] public int Seed;
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        public override PointOp CompilePoints(MeshCompiler c)
        {
            var input = c.Points(this, "In");
            Vector3 offset = c.Vector(this, "Offset", Offset), rotation = c.Vector(this, "Rotation", Rotation);
            float min = c.Number(this, "Min Scale", MinScale), max = c.Number(this, "Max Scale", MaxScale);
            int seed = c.Integer(this, "Seed", Seed);
            return new PointOp(() =>
            {
                var points = input.Copy();
                var random = new System.Random(seed);
                float Signed() => (float)random.NextDouble() * 2f - 1f;
                for (int i = 0; i < points.Count; i++)
                {
                    points.Positions[i] += Vector3.Scale(new Vector3(Signed(), Signed(), Signed()), offset);
                    points.Rotations[i] += Vector3.Scale(new Vector3(Signed(), Signed(), Signed()), rotation);
                    points.Scales[i] *= Mathf.Lerp(min, max, (float)random.NextDouble());
                }
                return points;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Merge Points"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Puts several sets of points together (wire as many as you like into In)."", ""inputs"": [{""name"": ""In"", ""type"": ""PointData""}], ""outputs"": [{""name"": ""Points"", ""type"": ""PointData""}]}")]
    [Serializable]
    [Node(Name = "Merge Points", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshMergePointsNode : MeshPointsNode
    {
        [Input("In", Editable = false, Multiple = true)] [NonSerialized] public PointData In;
        [Output(PointsPort)] [NonSerialized] public PointData Points;

        public override PointOp CompilePoints(MeshCompiler c)
        {
            var inputs = c.AllPoints(this, "In");
            return new PointOp(() =>
            {
                var points = new PointData();
                foreach (var input in inputs)
                    points.Append(input.Get());
                return points;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Stamp"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Points"", ""description"": ""Puts a copy of Item at every point: moved there, turned by its rotation and sized by its scale. With Points from a Point Grid shaped by a Point Function, this is a loop over space that builds the model."", ""inputs"": [{""name"": ""Points"", ""type"": ""PointData""}, {""name"": ""Item"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Stamp", Path = "Points"), Tags(MeshNode.Tag)]
    public sealed class MeshStampNode : MeshNode
    {
        [Input("Points", Editable = false)] [NonSerialized] public PointData Points;
        [Input("Item", Editable = false)] [NonSerialized] public MeshData Item;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var points = c.Points(this, "Points");
            var item = c.Mesh(this, "Item");
            return new MeshOp(() =>
            {
                var source = points.Get();
                var mesh = item.Get();
                var result = new MeshData();
                for (int i = 0; i < source.Count; i++)
                    result.Append(mesh, source.Matrix(i));
                return result;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Vertex Function"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Deform"", ""description"": ""Moves every vertex of a mesh by a formula: Position, using p (where it is), x y z, nrm (its normal), uv, i (its number), n (how many) and the graph's Parameters. A wave: p + nrm * sin(y * 8) * 0.05. A bulge: p * (1 + 0.3 * (1 - abs(y))). Corners at one place move together, so hard edges stay closed."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Vertex Function", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshVertexFunctionNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Editable, Tooltip("Where each vertex goes. p is where it is.")] public string Position = "p + nrm * sin(y * 8) * 0.05";
        [Editable, Tooltip("Normals are worked out again afterwards: edges sharper than this stay hard (-1: keep them).")] public float SmoothAngle = 60f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        private static readonly string[] Variables = { "p", "x", "y", "z", "nrm", "uv", "i", "n" };

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var position = c.Expression(this, Position, Variables);
            float smooth = SmoothAngle;
            return new MeshOp(() =>
            {
                var before = input.Get();
                var mesh = before.Clone();
                if (position == null)
                    return mesh;
                // One result per place (worked out with the average normal there), so welded corners stay welded.
                var normals = new Dictionary<Vector3, Vector3>(new MeshData.PositionComparer());
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    normals.TryGetValue(mesh.Positions[i], out var sum);
                    normals[mesh.Positions[i]] = sum + mesh.Normals[i];
                }
                var moved = new Dictionary<Vector3, Vector3>(new MeshData.PositionComparer());
                var vars = new ExprValue[Variables.Length];
                int n = mesh.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var p = mesh.Positions[i];
                    if (!moved.TryGetValue(p, out var to))
                    {
                        vars[0] = p;
                        vars[1] = p.x; vars[2] = p.y; vars[3] = p.z;
                        vars[4] = MeshData.Direction(normals[p]);
                        vars[5] = (Vector3)mesh.UVs[i];
                        vars[6] = i;
                        vars[7] = n;
                        moved[p] = to = position.Evaluate(vars).Vector;
                    }
                    mesh.Positions[i] = to;
                }
                MeshSelection.Restrict(mesh, before);
                if (smooth >= 0f)
                    mesh.RecalculateNormals(smooth);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Math"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A number from a formula of A, B, C and D (wire numbers or Parameters into them) and the graph's Parameters by name: Height / 1.8, a * 0.5 + 0.1, max(a, b), sin(a * pi)."", ""inputs"": [{""name"": ""A"", ""type"": ""float""}, {""name"": ""B"", ""type"": ""float""}, {""name"": ""C"", ""type"": ""float""}, {""name"": ""D"", ""type"": ""float""}], ""outputs"": [{""name"": ""Result"", ""type"": ""float""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Math", Path = "Values"), Tags(MeshNode.Tag)]
    public sealed class MeshMathNode : MeshNode
    {
        [Editable] public string Formula = "a * b";
        [Input("A", Editable = true)] public float A = 1f;
        [Input("B", Editable = true)] public float B = 1f;
        [Input("C", Editable = true)] public float C;
        [Input("D", Editable = true)] public float D;
        [Output("Result")] public float Result;

        private static readonly string[] Variables = { "a", "b", "c", "d" };

        public override MeshOp Compile(MeshCompiler compiler) => MeshOp.Empty;

        public override object OnRequestValue(Port port)
        {
            var c = MeshCompiler.Current;
            if (c == null)
                return 0f;
            var expression = c.Expression(this, Formula, Variables);
            if (expression == null)
                return 0f;
            return expression.Evaluate(new ExprValue[]
            {
                c.Number(this, "A", A), c.Number(this, "B", B), c.Number(this, "C", C), c.Number(this, "D", D),
            }).Number;
        }
    }

    [SchematicDoc(@"{""name"": ""Make Vector"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Values"", ""description"": ""A vector from three numbers (wire Parameters or Math into them): a position, a rotation or a scale driven by the graph's Parameters."", ""inputs"": [{""name"": ""X"", ""type"": ""float""}, {""name"": ""Y"", ""type"": ""float""}, {""name"": ""Z"", ""type"": ""float""}], ""outputs"": [{""name"": ""Vector"", ""type"": ""Vector3""}]}")]
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Name = "Make Vector", Path = "Values"), Tags(MeshNode.Tag)]
    public sealed class MeshMakeVectorNode : MeshNode
    {
        [Input("X", Editable = true)] public float X;
        [Input("Y", Editable = true)] public float Y;
        [Input("Z", Editable = true)] public float Z;
        [Output("Vector")] public Vector3 Vector;

        public override MeshOp Compile(MeshCompiler compiler) => MeshOp.Empty;

        public override object OnRequestValue(Port port)
        {
            var c = MeshCompiler.Current;
            return c == null ? new Vector3(X, Y, Z) : new Vector3(c.Number(this, "X", X), c.Number(this, "Y", Y), c.Number(this, "Z", Z));
        }
    }
}
