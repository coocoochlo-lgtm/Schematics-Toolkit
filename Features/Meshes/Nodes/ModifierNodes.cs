// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    // What Mesh Graph nodes do to meshes. Each reads its numbers as the graph is read (main thread) and does its work
    // when the model is made (background thread), on a copy of what's wired in.

    public enum MeshAxis { X, Y, Z }

    [SchematicDoc(@"{""name"": ""Transform"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Moves, turns (degrees) and scales a mesh. A negative scale mirrors it (its faces are turned to stay outward)."", ""inputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}, {""name"": ""Position"", ""type"": ""Vector3""}, {""name"": ""Rotation"", ""type"": ""Vector3""}, {""name"": ""Scale"", ""type"": ""Vector3""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Transform", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshTransformNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Position", Editable = true)] public Vector3 Position;
        [Input("Rotation", Editable = true)] public Vector3 Rotation;
        [Input("Scale", Editable = true)] public Vector3 Scale = Vector3.one;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var matrix = MeshModifiers.TRS(c.Vector(this, "Position", Position), c.Vector(this, "Rotation", Rotation), c.Vector(this, "Scale", Scale));
            return new MeshOp(() => { var mesh = input.Copy(); mesh.Transform(matrix); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Merge"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Puts several meshes together into one (wire as many as you like into In). Each keeps its material slots."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Merge", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshMergeNode : MeshNode
    {
        [Input("In", Editable = false, Multiple = true)] [NonSerialized] public MeshData In;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var inputs = c.Meshes(this, "In");
            return new MeshOp(() =>
            {
                var mesh = new MeshData();
                foreach (var input in inputs)
                    mesh.Append(input.Get());
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Array"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Count copies of a mesh, each moved by Offset, turned by Rotation and scaled by Scale from the one before: stairs, fences, spirals, chains."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Count"", ""type"": ""int""}, {""name"": ""Offset"", ""type"": ""Vector3""}, {""name"": ""Rotation"", ""type"": ""Vector3""}, {""name"": ""Scale"", ""type"": ""Vector3""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Array", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshArrayNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Count", Editable = true)] public int Count = 4;
        [Input("Offset", Editable = true)] public Vector3 Offset = new(1f, 0f, 0f);
        [Input("Rotation", Editable = true)] public Vector3 Rotation;
        [Input("Scale", Editable = true)] public Vector3 Scale = Vector3.one;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int count = Mathf.Clamp(c.Integer(this, "Count", Count), 1, 4096);
            var step = MeshModifiers.TRS(c.Vector(this, "Offset", Offset), c.Vector(this, "Rotation", Rotation), c.Vector(this, "Scale", Scale));
            return new MeshOp(() => MeshModifiers.Array(input.Get(), count, step));
        }
    }

    [SchematicDoc(@"{""name"": ""Radial Array"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Count copies of a mesh in a ring around Y, Radius out, each turned to face outwards (with Face Out): petals, spokes, columns round a temple."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Count"", ""type"": ""int""}, {""name"": ""Radius"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Radial Array", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshRadialArrayNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Count", Editable = true)] public int Count = 6;
        [Input("Radius", Editable = true)] public float Radius = 1f;
        [Editable] public bool FaceOut = true;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int count = Mathf.Clamp(c.Integer(this, "Count", Count), 1, 4096);
            float radius = c.Number(this, "Radius", Radius);
            bool faceOut = FaceOut;
            return new MeshOp(() => MeshModifiers.Radial(input.Get(), count, radius, faceOut));
        }
    }

    [SchematicDoc(@"{""name"": ""Mirror"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Adds a mirror image across the origin on Axis (or, without Keep Original, just flips it): build half a model and mirror it."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Mirror", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshMirrorNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Editable] public MeshAxis Axis = MeshAxis.X;
        [Editable] public bool KeepOriginal = true;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int axis = (int)Axis;
            bool keep = KeepOriginal;
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.Mirror(mesh, axis, keep); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Twist"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Twists a mesh around Y: its top turns Degrees further than its bottom. Needs height segments to twist smoothly."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Degrees"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Twist", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshTwistNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Degrees", Editable = true)] public float Degrees = 90f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float degrees = c.Number(this, "Degrees", Degrees);
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.Twist(mesh, degrees); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Bend"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Bends a mesh's height into an arc towards +X: the top ends up turned Degrees from the bottom. A tall box bent 180 is an arch; a cylinder bent a little, a leaning trunk."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Degrees"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Bend", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshBendNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Degrees", Editable = true)] public float Degrees = 45f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float degrees = c.Number(this, "Degrees", Degrees);
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.Bend(mesh, degrees); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Taper"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Narrows (or widens) a mesh towards its top: Top Scale times as wide at the top as at the bottom."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Top Scale"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Taper", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshTaperNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Top Scale", Editable = true)] public float TopScale = 0.5f;
        [Editable, Tooltip("Normals are worked out again afterwards: edges sharper than this stay hard.")] public float SmoothAngle = 60f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float scale = c.Number(this, "Top Scale", TopScale), smooth = SmoothAngle;
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.Taper(mesh, scale); mesh.RecalculateNormals(smooth); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Noise"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Pushes the surface in and out by smooth noise: Amount far, Frequency bumps per metre, a different pattern per Seed. Along each normal (rocks, bark, lumpy ground) or straight up (terrain). Needs segments to have something to move."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Amount"", ""type"": ""float""}, {""name"": ""Frequency"", ""type"": ""float""}, {""name"": ""Seed"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Noise", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshNoiseNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Amount", Editable = true)] public float Amount = 0.1f;
        [Input("Frequency", Editable = true)] public float Frequency = 2f;
        [Input("Seed", Editable = true)] public int Seed;
        [Editable] public bool AlongNormals = true;
        [Editable, Tooltip("Normals are worked out again afterwards: edges sharper than this stay hard.")] public float SmoothAngle = 60f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float amount = c.Number(this, "Amount", Amount), frequency = c.Number(this, "Frequency", Frequency);
            int seed = c.Integer(this, "Seed", Seed);
            bool alongNormals = AlongNormals;
            float smooth = SmoothAngle;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                MeshModifiers.Displace(mesh, amount, frequency, seed, alongNormals);
                mesh.RecalculateNormals(smooth);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Shading"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Works the normals out again: faces meeting at more than Smooth Angle degrees keep a hard edge, the rest are smoothed. 0 is faceted (low-poly look), 180 smooth all over. Flip turns the faces inside out (a sky dome, a room)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Smooth Angle"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Shading", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshShadingNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Smooth Angle", Editable = true)] public float SmoothAngle = 60f;
        [Editable] public bool Flip;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float smooth = c.Number(this, "Smooth Angle", SmoothAngle);
            bool flip = Flip;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                if (flip) mesh.FlipTriangles();
                mesh.RecalculateNormals(smooth);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Scatter"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Places Count copies of Item over Surface's surface (evenly by area), stood up along it (Align 1) or straight up (Align 0), spun at random and sized between Min Scale and Max Scale. The result is the copies alone: Merge it with the surface. Leaves on a crown, rocks on a hill, studs on armour."", ""inputs"": [{""name"": ""Surface"", ""type"": ""MeshData""}, {""name"": ""Item"", ""type"": ""MeshData""}, {""name"": ""Count"", ""type"": ""int""}, {""name"": ""Seed"", ""type"": ""int""}, {""name"": ""Min Scale"", ""type"": ""float""}, {""name"": ""Max Scale"", ""type"": ""float""}, {""name"": ""Align"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Scatter", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshScatterNode : MeshNode
    {
        [Input("Surface", Editable = false)] [NonSerialized] public MeshData Surface;
        [Input("Item", Editable = false)] [NonSerialized] public MeshData Item;
        [Input("Count", Editable = true)] public int Count = 20;
        [Input("Seed", Editable = true)] public int Seed;
        [Input("Min Scale", Editable = true)] public float MinScale = 0.8f;
        [Input("Max Scale", Editable = true)] public float MaxScale = 1.2f;
        [Input("Align", Editable = true)] public float Align = 1f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var surface = c.Mesh(this, "Surface");
            var item = c.Mesh(this, "Item");
            int count = Mathf.Clamp(c.Integer(this, "Count", Count), 0, 100000), seed = c.Integer(this, "Seed", Seed);
            float min = c.Number(this, "Min Scale", MinScale), max = c.Number(this, "Max Scale", MaxScale), align = c.Number(this, "Align", Align);
            return new MeshOp(() => MeshModifiers.Scatter(surface.Get(), item.Get(), count, seed, min, max, align));
        }
    }

    [SchematicDoc(@"{""name"": ""Material Slot"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Surface"", ""description"": ""Puts all of a mesh in a material slot (0 is the first material on the renderer, 1 the second...). Give a tree's leaves slot 1 and its trunk slot 0, then Merge them."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Slot"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Material Slot", Path = "Surface"), Tags(MeshNode.Tag)]
    public sealed class MeshSlotNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Slot", Editable = true)] public int Slot;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int slot = Mathf.Clamp(c.Integer(this, "Slot", Slot), 0, 31);
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.SetSlot(mesh, slot); return mesh; });
        }
    }

    public enum UVProjection { Box, PlanarX, PlanarY, PlanarZ }

    [SchematicDoc(@"{""name"": ""UV Project"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Surface"", ""description"": ""New UVs, projected: Box takes each part's from the side it faces most (any shape, no stretching), Planar from one axis. Scale is texture repeats per metre."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Scale"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "UV Project", Path = "Surface"), Tags(MeshNode.Tag)]
    public sealed class MeshUVProjectNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Editable] public UVProjection Projection = UVProjection.Box;
        [Input("Scale", Editable = true)] public float Scale = 1f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var projection = Projection;
            float scale = c.Number(this, "Scale", Scale);
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                if (projection == UVProjection.Box)
                    MeshModifiers.BoxProject(mesh, scale);
                else
                    MeshModifiers.PlanarProject(mesh, (int)projection - 1, scale);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""UV Transform"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Surface"", ""description"": ""Scales and moves a mesh's UVs (to tile a texture more, or pick a part of an atlas)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Scale"", ""type"": ""Vector2""}, {""name"": ""Offset"", ""type"": ""Vector2""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "UV Transform", Path = "Surface"), Tags(MeshNode.Tag)]
    public sealed class MeshUVTransformNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Scale", Editable = true)] public Vector2 Scale = Vector2.one;
        [Input("Offset", Editable = true)] public Vector2 Offset;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            Vector2 scale = c.Vector(this, "Scale", Scale), offset = c.Vector(this, "Offset", Offset);
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.TransformUVs(mesh, scale, offset); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Vertex Color"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Surface"", ""description"": ""Colors a mesh's vertices: Bottom at its lowest point fading to Top at its highest (the same color twice for a flat one). Shaders that read vertex color tint it; handy to tell merged parts apart in one material."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Bottom"", ""type"": ""Color""}, {""name"": ""Top"", ""type"": ""Color""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Vertex Color", Path = "Surface"), Tags(MeshNode.Tag)]
    public sealed class MeshVertexColorNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Bottom", Editable = true)] public Color Bottom = Color.white;
        [Input("Top", Editable = true)] public Color Top = Color.white;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            Color bottom = c.ColorValue(this, "Bottom", Bottom), top = c.ColorValue(this, "Top", Top);
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.HeightColors(mesh, bottom, top); return mesh; });
        }
    }

    [SchematicDoc(@"{""name"": ""Simplify"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Fewer triangles, keeping the shape: Quality is the share kept (0.25: a quarter). For a deliberately low-poly look, or a heavy part of a model (the Output makes LODs by itself)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Quality"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Simplify", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshSimplifyNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Quality", Editable = true)] public float Quality = 0.5f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float quality = Mathf.Clamp01(c.Number(this, "Quality", Quality));
            return new MeshOp(() => MeshSimplifier.Simplify(input.Get(), quality));
        }
    }

    [SchematicDoc(@"{""name"": ""Weld"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Modify"", ""description"": ""Joins vertices that are identical (same place, normal, UV and color) into one: a lighter mesh, and smooth surfaces stay joined when deformed."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Weld", Path = "Modify"), Tags(MeshNode.Tag)]
    public sealed class MeshWeldNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            return new MeshOp(() => { var mesh = input.Copy(); MeshModifiers.Weld(mesh); return mesh; });
        }
    }
}
