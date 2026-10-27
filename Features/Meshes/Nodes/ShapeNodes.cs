// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Meshes
{
    // The shapes a Mesh Graph starts from. Centred on the origin with Y up (cylinders and lathes stand on it).

    [SchematicDoc(@"{""name"": ""Box"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""A box of Size, each side split into Segments by Segments (more segments bend and displace smoothly)."", ""inputs"": [{""name"": ""Size"", ""type"": ""Vector3""}, {""name"": ""Segments"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Box", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshBoxNode : MeshNode
    {
        [Input("Size", Editable = true)] public Vector3 Size = Vector3.one;
        [Input("Segments", Editable = true)] public int Segments = 1;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var size = c.Vector(this, "Size", Size);
            int segments = Mathf.Clamp(c.Integer(this, "Segments", Segments), 1, 128);
            return new MeshOp(() => MeshShapes.Box(size, segments));
        }
    }

    [SchematicDoc(@"{""name"": ""Plane"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""A flat, upward-facing grid, Width by Length, split into Segments each way (ground, water, a canopy to displace)."", ""inputs"": [{""name"": ""Width"", ""type"": ""float""}, {""name"": ""Length"", ""type"": ""float""}, {""name"": ""Segments"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Plane", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshPlaneNode : MeshNode
    {
        [Input("Width", Editable = true)] public float Width = 1f;
        [Input("Length", Editable = true)] public float Length = 1f;
        [Input("Segments", Editable = true)] public int Segments = 1;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var size = new Vector2(c.Number(this, "Width", Width), c.Number(this, "Length", Length));
            int segments = Mathf.Clamp(c.Integer(this, "Segments", Segments), 1, 512);
            return new MeshOp(() => MeshShapes.Plane(size, segments, segments));
        }
    }

    [SchematicDoc(@"{""name"": ""Sphere"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""A sphere of Radius, made of Segments around and Rings from bottom to top."", ""inputs"": [{""name"": ""Radius"", ""type"": ""float""}, {""name"": ""Segments"", ""type"": ""int""}, {""name"": ""Rings"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Sphere", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshSphereNode : MeshNode
    {
        [Input("Radius", Editable = true)] public float Radius = 0.5f;
        [Input("Segments", Editable = true)] public int Segments = 24;
        [Input("Rings", Editable = true)] public int Rings = 16;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            float radius = c.Number(this, "Radius", Radius);
            int segments = Mathf.Clamp(c.Integer(this, "Segments", Segments), 3, 256);
            int rings = Mathf.Clamp(c.Integer(this, "Rings", Rings), 2, 256);
            return new MeshOp(() => MeshShapes.Sphere(radius, segments, rings));
        }
    }

    [SchematicDoc(@"{""name"": ""Cylinder"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""A cylinder standing on the origin, Height tall. Top Radius 0 makes a cone; a different one makes a tapered post. Caps close its ends."", ""inputs"": [{""name"": ""Bottom Radius"", ""type"": ""float""}, {""name"": ""Top Radius"", ""type"": ""float""}, {""name"": ""Height"", ""type"": ""float""}, {""name"": ""Segments"", ""type"": ""int""}, {""name"": ""Height Segments"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Cylinder", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshCylinderNode : MeshNode
    {
        [Input("Bottom Radius", Editable = true)] public float BottomRadius = 0.5f;
        [Input("Top Radius", Editable = true)] public float TopRadius = 0.5f;
        [Input("Height", Editable = true)] public float Height = 1f;
        [Input("Segments", Editable = true)] public int Segments = 24;
        [Input("Height Segments", Editable = true)] public int HeightSegments = 1;
        [Editable] public bool Caps = true;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            float bottom = c.Number(this, "Bottom Radius", BottomRadius), top = c.Number(this, "Top Radius", TopRadius);
            float height = c.Number(this, "Height", Height);
            int segments = Mathf.Clamp(c.Integer(this, "Segments", Segments), 3, 256);
            int heightSegments = Mathf.Clamp(c.Integer(this, "Height Segments", HeightSegments), 1, 256);
            bool caps = Caps;
            return new MeshOp(() => MeshShapes.Cylinder(bottom, top, height, segments, heightSegments, caps));
        }
    }

    [SchematicDoc(@"{""name"": ""Torus"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""A ring lying flat: Radius to the middle of its tube, Thickness across the tube."", ""inputs"": [{""name"": ""Radius"", ""type"": ""float""}, {""name"": ""Thickness"", ""type"": ""float""}, {""name"": ""Segments"", ""type"": ""int""}, {""name"": ""Sides"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Torus", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshTorusNode : MeshNode
    {
        [Input("Radius", Editable = true)] public float Radius = 0.5f;
        [Input("Thickness", Editable = true)] public float Thickness = 0.2f;
        [Input("Segments", Editable = true)] public int Segments = 32;
        [Input("Sides", Editable = true)] public int Sides = 16;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            float radius = c.Number(this, "Radius", Radius), thickness = c.Number(this, "Thickness", Thickness);
            int segments = Mathf.Clamp(c.Integer(this, "Segments", Segments), 3, 256), sides = Mathf.Clamp(c.Integer(this, "Sides", Sides), 3, 256);
            return new MeshOp(() => MeshShapes.Torus(radius, thickness, segments, sides));
        }
    }

    [SchematicDoc(@"{""name"": ""Lathe"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Shapes"", ""description"": ""A shape turned on a lathe: Profile is its radius (times Radius) from the bottom (left of the curve) to the top (right), spun around, Height tall. Vases, pillars, bottles, tree trunks, chess pieces."", ""inputs"": [{""name"": ""Radius"", ""type"": ""float""}, {""name"": ""Height"", ""type"": ""float""}, {""name"": ""Steps"", ""type"": ""int""}, {""name"": ""Segments"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Lathe", Path = "Shapes"), Tags(MeshNode.Tag)]
    public sealed class MeshLatheNode : MeshNode
    {
        [Editable, Tooltip("Radius (times Radius) from bottom (time 0) to top (time 1).")]
        public AnimationCurve Profile = new(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(0.8f, 0.45f), new Keyframe(1f, 0.55f));
        [Input("Radius", Editable = true)] public float Radius = 0.5f;
        [Input("Height", Editable = true)] public float Height = 1f;
        [Input("Steps", Editable = true)] public int Steps = 16;
        [Input("Segments", Editable = true)] public int Segments = 24;
        [Editable] public bool Caps = true;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            float radius = c.Number(this, "Radius", Radius), height = c.Number(this, "Height", Height);
            int steps = Mathf.Clamp(c.Integer(this, "Steps", Steps), 1, 512), segments = Mathf.Clamp(c.Integer(this, "Segments", Segments), 3, 256);
            // The curve is read here, on the main thread (curves are engine objects); the lathe is turned later.
            var radii = new float[steps + 1];
            for (int i = 0; i <= steps; i++)
                radii[i] = Mathf.Max(0f, (Profile != null ? Profile.Evaluate(i / (float)steps) : 1f) * radius);
            bool caps = Caps;
            return new MeshOp(() => MeshShapes.Lathe(radii, height, segments, caps));
        }
    }
}
