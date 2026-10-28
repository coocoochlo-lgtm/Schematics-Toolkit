using Remedy.Schematics.Macros;
using UnityEditor;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Mesh Graph macros (right-click in a Mesh Graph: Macros): ready-made models built from ordinary nodes, so every
    /// part can be changed, with their main settings as Parameters.
    /// </summary>
    [InitializeOnLoad]
    internal static class MeshMacros
    {
        private const float Column = 260f, Row = 120f;

        static MeshMacros()
        {
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Characters/Humanoid (mannequin)",
                Description = "A 1.8 m humanoid mannequin, a node pair per part (shape and placement), the right arm and leg mirrored to the left, then sealed into one skin (Select Inside, Delete Selected, Join). Parameters: Height, Build (broadness, 1 = average) and Head Size.",
                GraphType = typeof(MeshGraph),
                Build = Humanoid,
            });
        }

        // Parameters on the left; a column of shapes, a column of their Transforms; the limbs merged and mirrored; the
        // body merged, sealed (hidden insides cut away, the cut edges joined onto their neighbours), sized by the
        // Parameters and wired into the Output (if it's free).
        private static void Humanoid(MacroBuilder b)
        {
            var height = b.Add<MeshParameterNode>(0f, 0f, n => { n.ParameterName = "Height"; n.Default = HumanoidRecipe.BaseHeight; n.Name = "Height"; });
            var build = b.Add<MeshParameterNode>(0f, Row, n => { n.ParameterName = "Build"; n.Default = 1f; n.Name = "Build"; });
            var headSize = b.Add<MeshParameterNode>(0f, Row * 2f, n => { n.ParameterName = "Head Size"; n.Default = 1f; n.Name = "Head Size"; });

            var parts = HumanoidRecipe.Parts();
            float bodyY = 0f, limbY = 0f;
            var limbs = b.Add<MeshMergeNode>(Column * 3f, Row * 8f, n => n.Name = "Right Arm and Leg");
            var mirror = b.Add<MeshMirrorNode>(Column * 4f, Row * 8f, n => { n.Name = "Both Sides"; n.Axis = MeshAxis.X; n.KeepOriginal = true; });
            b.Link(limbs, MeshNode.MeshPort, mirror, "In");
            var body = b.Add<MeshMergeNode>(Column * 5f, Row * 3f, n => n.Name = "Body");
            b.Link(mirror, MeshNode.MeshPort, body, "In");

            foreach (var part in parts)
            {
                float y = part.Limb ? Row * 7f + limbY : bodyY;
                if (part.Limb) limbY += Row; else bodyY += Row;
                float x = Column;

                MeshNode shape = part.Shape switch
                {
                    HumanoidRecipe.Shape.Sphere => b.Add<MeshSphereNode>(x, y, n => { n.Name = part.Name; n.Radius = part.Radius; n.Segments = 20; n.Rings = 14; }),
                    HumanoidRecipe.Shape.Cylinder => b.Add<MeshCylinderNode>(x, y, n =>
                    {
                        n.Name = part.Name; n.BottomRadius = part.Radius; n.TopRadius = part.TopRadius; n.Height = part.Height; n.Segments = 16; n.HeightSegments = 4;
                    }),
                    _ => b.Add<MeshLatheNode>(x, y, n =>
                    {
                        n.Name = part.Name; n.Radius = part.Radius; n.Height = part.Height; n.Steps = 16; n.Segments = 24;
                        var curve = new AnimationCurve(part.Profile);
                        for (int i = 0; i < curve.length; i++)
                            curve.SmoothTangents(i, 0f);
                        n.Profile = curve;
                    }),
                };
                var place = b.Add<MeshTransformNode>(x + Column, y, n =>
                {
                    n.Name = part.Name + " Placement"; n.Position = part.Position; n.Rotation = part.Rotation; n.Scale = part.Scale;
                });
                b.Link(shape, MeshNode.MeshPort, place, "In");
                if (part.Head)
                    b.Link(headSize, "Value", place, "Uniform Scale");
                b.Link(place, MeshNode.MeshPort, part.Limb ? limbs : body, "In");
            }

            // Sized: Height / 1.8 all over, Build across (X and Z).
            var heightScale = b.Add<MeshMathNode>(Column * 8f, Row * 5f, n => { n.Name = "Height Scale"; n.Formula = "a / " + HumanoidRecipe.BaseHeight.ToString(System.Globalization.CultureInfo.InvariantCulture); });
            b.Link(height, "Value", heightScale, "A");
            var broadness = b.Add<MeshMakeVectorNode>(Column * 8f, Row * 6.5f, n => { n.Name = "Broadness"; n.Y = 1f; });
            b.Link(build, "Value", broadness, "X");
            b.Link(build, "Value", broadness, "Z");
            // Sealed: one skin instead of overlapping parts.
            var hidden = b.Add<MeshSelectInsideNode>(Column * 6f, Row * 3f, n => n.Name = "Hidden Insides");
            b.Link(body, MeshNode.MeshPort, hidden, "In");
            var cut = b.Add<MeshDeleteSelectedNode>(Column * 7f, Row * 3f, n => { n.Name = "Cut Away"; n.AllCorners = true; });
            b.Link(hidden, MeshNode.MeshPort, cut, "In");
            var join = b.Add<MeshJoinNode>(Column * 8f, Row * 3f, n => { n.Name = "Join Seams"; n.Distance = HumanoidRecipe.JoinDistance; });
            b.Link(cut, MeshNode.MeshPort, join, "In");

            var size = b.Add<MeshTransformNode>(Column * 9f, Row * 3f, n => n.Name = "Size");
            b.Link(join, MeshNode.MeshPort, size, "In");
            b.Link(broadness, "Vector", size, "Scale");
            b.Link(heightScale, "Result", size, "Uniform Scale");

            var output = b.Existing<MeshOutputNode>();
            if (output != null && output.GetPort(MeshNode.MeshPort)?.ConnectionCount == 0)
                b.Link(size, MeshNode.MeshPort, output, MeshNode.MeshPort);
        }
    }
}
