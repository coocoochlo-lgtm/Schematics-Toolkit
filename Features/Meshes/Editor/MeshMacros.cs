using System;
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
                Path = "Characters/Humanoid",
                Description = "A 1.8 m box-modelled humanoid: every part a loop-cut cube shaped with Grab, welded to the next at the joints so edge loops run through them, relaxed, given a face (brow, eyes, nose, cheeks, lips, chin, ears: Grabs on the head), shaped and subdivided (Catmull-Clark). The right arm and leg are mirrored to the left. Parameters: Height, Build (broadness, 1 = average), Head Size and Arms Down (degrees from a T-pose).",
                GraphType = typeof(MeshGraph),
                Build = Humanoid,
            });
        }

        // One row per part (its cube, loop cuts, shaping Grabs and placement), the limbs merged and mirrored; then the
        // body along the bottom, left to right: merged, joints welded (Select Near, Delete Selected, Join), relaxed, the
        // face (the head selected and rounded, its Grabs along a row below), the whole-body Grabs, Subdivide, and sized by
        // the Parameters into the Output (if it's free).
        private static void Humanoid(MacroBuilder b)
        {
            var height = b.Add<MeshParameterNode>(0f, 0f, n => { n.ParameterName = "Height"; n.Default = HumanoidRecipe.BaseHeight; n.Name = "Height"; });
            var build = b.Add<MeshParameterNode>(0f, Row, n => { n.ParameterName = "Build"; n.Default = 1f; n.Name = "Build"; });
            var headSize = b.Add<MeshParameterNode>(0f, Row * 2f, n => { n.ParameterName = "Head Size"; n.Default = 1f; n.Name = "Head Size"; });
            var armsDown = b.Add<MeshParameterNode>(0f, Row * 3f, n => { n.ParameterName = "Arms Down"; n.Default = HumanoidRecipe.ArmsDown; n.Name = "Arms Down"; });

            var parts = HumanoidRecipe.Parts();
            float partsRight = Column * 14f;
            var limbs = b.Add<MeshMergeNode>(partsRight, Row * 4f, n => n.Name = "Right Arm and Leg");
            var mirror = b.Add<MeshMirrorNode>(partsRight + Column, Row * 4f, n => { n.Name = "Both Sides"; n.Axis = MeshAxis.X; n.KeepOriginal = true; });
            b.Link(limbs, MeshNode.MeshPort, mirror, "In");
            var merged = b.Add<MeshMergeNode>(partsRight + Column * 2f, 0f, n => n.Name = "All Parts");
            b.Link(mirror, MeshNode.MeshPort, merged, "In");

            float y = 0f;
            foreach (var part in parts)
            {
                float x = Column;
                MeshNode last = b.Add<MeshBoxNode>(x, y, n => { n.Name = part.Name; n.Size = part.Size; n.Segments = 1; });
                foreach (var (axis, cuts) in new[] { (MeshAxis.X, part.CutsX), (MeshAxis.Y, part.CutsY), (MeshAxis.Z, part.CutsZ) })
                {
                    x += Column;
                    if (cuts <= 0)
                        continue;
                    var cut = b.Add<MeshLoopCutNode>(x, y, n => { n.Name = $"{part.Name} Loops {axis}"; n.Axis = axis; n.Cuts = cuts; });
                    b.Link(last, MeshNode.MeshPort, cut, "In");
                    last = cut;
                }
                if (part.DetailX + part.DetailY + part.DetailZ > 0)
                {
                    // Detail loops on every side but the underside (where the next part joins).
                    var under = b.Add<MeshSelectFacingNode>(x, y + Row * 0.5f, n => { n.Name = $"{part.Name} Underside"; n.Direction = Vector3.down; n.Angle = 45f; });
                    b.Link(last, MeshNode.MeshPort, under, "In");
                    var sides = b.Add<MeshEditSelectionNode>(x + Column, y + Row * 0.5f, n => { n.Name = $"{part.Name} All But Underside"; n.Edit = SelectionEdit.Invert; });
                    b.Link(under, MeshNode.MeshPort, sides, "In");
                    last = sides;
                    x += Column;
                    foreach (var (axis, cuts) in new[] { (MeshAxis.X, part.DetailX), (MeshAxis.Y, part.DetailY), (MeshAxis.Z, part.DetailZ) })
                    {
                        if (cuts <= 0)
                            continue;
                        x += Column;
                        var cut = b.Add<MeshLoopCutNode>(x, y + Row * 0.5f, n => { n.Name = $"{part.Name} Detail {axis}"; n.Axis = axis; n.Cuts = cuts; });
                        b.Link(last, MeshNode.MeshPort, cut, "In");
                        last = cut;
                    }
                    x += Column;
                    var clear = b.Add<MeshEditSelectionNode>(x, y + Row * 0.5f, n => { n.Name = $"{part.Name} Whole Again"; n.Edit = SelectionEdit.Clear; });
                    b.Link(last, MeshNode.MeshPort, clear, "In");
                    last = clear;
                    y += Row;
                }
                foreach (var step in part.Shaping)
                {
                    x += Column;
                    last = AddGrab(b, x, y, step, last);
                }
                var place = b.Add<MeshTransformNode>(Math.Max(Column * 8f, x + Column), y, n => { n.Name = part.Name + " Placement"; n.Position = part.Position; });
                b.Link(last, MeshNode.MeshPort, place, "In");
                b.Link(place, MeshNode.MeshPort, part.Limb ? limbs : merged, "In");
                y += Row;
            }

            // The body, along the bottom.
            float bodyY = y + Row;
            float bx = Column;
            MeshNode body = merged;
            MeshNode Next(MeshNode node, string from = MeshNode.MeshPort)
            {
                b.Link(body, from, node, "In");
                bx += Column;
                return body = node;
            }
            Next(b.Add<MeshSelectNearNode>(bx, bodyY, n => { n.Name = "Joint Ends"; n.Distance = HumanoidRecipe.JoinDistance; }));
            Next(b.Add<MeshDeleteSelectedNode>(bx, bodyY, n => { n.Name = "Open The Joints"; n.AllCorners = true; }));
            Next(b.Add<MeshJoinNode>(bx, bodyY, n => { n.Name = "Weld The Joints"; n.Distance = HumanoidRecipe.JoinDistance * 2f; n.Mode = MeshSelection.JoinMode.Weld; n.SmoothAngle = 60f; }));

            Next(b.Add<MeshRelaxNode>(bx, bodyY, n => { n.Name = "Round Off"; n.Iterations = HumanoidRecipe.RelaxIterations; n.Strength = HumanoidRecipe.RelaxStrength; }));

            // The face: on the head alone (its Grab boxes measured across the head).
            var relaxed = MeshModifiers.Relax(HumanoidRecipe.Seal(HumanoidRecipe.Assemble()), HumanoidRecipe.RelaxIterations, HumanoidRecipe.RelaxStrength);
            var (headMin, headMax, headFalloff) = HumanoidRecipe.HeadRegion(relaxed);
            Next(b.Add<MeshSelectBoundsNode>(bx, bodyY, n => { n.Name = "Head"; n.Min = headMin; n.Max = headMax; n.Falloff = headFalloff; }));
            Next(b.Add<MeshSpherizeNode>(bx, bodyY, n => { n.Name = "Round Head"; n.Amount = HumanoidRecipe.HeadRound; n.Radius = 0f; }));
            float faceY = bodyY + Row * 4f;
            foreach (var step in HumanoidRecipe.FaceSteps())
            {
                body = AddGrab(b, bx, faceY, step, body);
                bx += Column;
            }
            Next(b.Add<MeshEditSelectionNode>(bx, bodyY, n => { n.Name = "Whole Body Again"; n.Edit = SelectionEdit.Clear; }));

            foreach (var step in HumanoidRecipe.BodySteps())
            {
                var grab = AddGrab(b, bx, bodyY, step, body);
                body = grab;
                bx += Column;
                if (step.Name == HumanoidRecipe.ArmsStep)
                {
                    // Down is a turn the other way about Z on the right arm (the left gets the mirror image).
                    var negate = b.Add<MeshMathNode>(grab.Position.x - Column, bodyY + Row * 1.5f, n => { n.Name = "Arms Down Turn"; n.Formula = "-a"; });
                    b.Link(armsDown, "Value", negate, "A");
                    var turn = b.Add<MeshMakeVectorNode>(grab.Position.x - Column * 0.5f, bodyY + Row * 2.5f, n => n.Name = "Arms Down Rotation");
                    b.Link(negate, "Result", turn, "Z");
                    b.Link(turn, "Vector", grab, "Rotate");
                }
                else if (step.Name == HumanoidRecipe.HeadStep)
                {
                    var scale = b.Add<MeshMakeVectorNode>(grab.Position.x - Column * 0.5f, bodyY + Row * 1.5f, n => n.Name = "Head Scale");
                    b.Link(headSize, "Value", scale, "X");
                    b.Link(headSize, "Value", scale, "Y");
                    b.Link(headSize, "Value", scale, "Z");
                    b.Link(scale, "Vector", grab, "Scale");
                }
            }

            Next(b.Add<MeshSubdivideNode>(bx, bodyY, n =>
            {
                n.Name = "Smooth"; n.Levels = HumanoidRecipe.SmoothLevels; n.Smooth = true; n.Method = MeshOperations.SubdivideMethod.CatmullClark; n.SmoothAngle = 80f;
            }));

            // Sized: Height / 1.8 all over, Build across (X and Z).
            var heightScale = b.Add<MeshMathNode>(bx - Column, bodyY + Row * 1.5f, n => { n.Name = "Height Scale"; n.Formula = "a / " + HumanoidRecipe.BaseHeight.ToString(System.Globalization.CultureInfo.InvariantCulture); });
            b.Link(height, "Value", heightScale, "A");
            var broadness = b.Add<MeshMakeVectorNode>(bx - Column, bodyY + Row * 3f, n => { n.Name = "Broadness"; n.Y = 1f; });
            b.Link(build, "Value", broadness, "X");
            b.Link(build, "Value", broadness, "Z");
            var size = Next(b.Add<MeshTransformNode>(bx, bodyY, n => n.Name = "Size"));
            b.Link(broadness, "Vector", size, "Scale");
            b.Link(heightScale, "Result", size, "Uniform Scale");

            var output = b.Existing<MeshOutputNode>();
            if (output != null && output.GetPort(MeshNode.MeshPort)?.ConnectionCount == 0)
                b.Link(size, MeshNode.MeshPort, output, MeshNode.MeshPort);
        }

        private static MeshGrabNode AddGrab(MacroBuilder b, float x, float y, HumanoidRecipe.GrabStep step, MeshNode from)
        {
            var grab = b.Add<MeshGrabNode>(x, y, n =>
            {
                n.Name = step.Name; n.Min = step.Min; n.Max = step.Max; n.Falloff = step.Falloff; n.Pivot = step.Pivot;
                n.Move = step.Move; n.Rotate = step.Rotate; n.Scale = step.Scale; n.MirrorX = step.MirrorX; n.SmoothAngle = 60f;
            });
            b.Link(from, MeshNode.MeshPort, grab, "In");
            return grab;
        }
    }
}
