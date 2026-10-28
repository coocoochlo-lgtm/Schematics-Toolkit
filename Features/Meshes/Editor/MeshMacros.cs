using System;
using System.Collections.Generic;
using System.Globalization;
using Remedy.Schematics;
using Remedy.Schematics.Macros;
using UnityEditor;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Mesh Graph macros (right-click in a Mesh Graph: Macros): ready-made models built from ordinary nodes, so every
    /// part can be changed, with their main settings as Parameters.
    /// </summary>
    /// <remarks>
    /// The Humanoid is an ensemble of functions: three Mesh Graphs in a library (made the first time it's used, under
    /// <see cref="LibraryFolder"/>), each in the node search under Functions/Characters/Humanoid, called in turn by
    /// Function nodes whose sliders are their Parameters.
    /// </remarks>
    [InitializeOnLoad]
    internal static class MeshMacros
    {
        private const float Column = 260f, Row = 120f;

        /// <summary>Where the Humanoid's function graphs are made.</summary>
        public const string LibraryFolder = MeshGraph.Folder + "/Library/Humanoid";

        private const string LibraryPath = "Characters/Humanoid";

        static MeshMacros()
        {
            SchematicMacros.Register(new BuiltInMacro
            {
                Path = "Characters/Humanoid",
                Description = "A 1.8 m box-modelled humanoid, made of three functions (Mesh Graphs in " + LibraryFolder + ", made the first time): Humanoid Body (loop-cut cubes welded at the joints so edge loops run through them, relaxed), Humanoid Face (sliders for the skull, jaw, ears, eyes, brow, nose, cheekbones, lips and chin) and Humanoid Shape (waist, chest, seat, shoulder slope, arms down, head size), then subdivided (Catmull-Clark) and sized by the Height and Build Parameters. Wire a Parameter into any slider's pin to set it per object.",
                GraphType = typeof(MeshGraph),
                Build = Humanoid,
            });
        }

        #region Humanoid

        // Body, Face and Shape called in turn, subdivided, and sized by the graph's Height and Build into its Output
        // (if it's free).
        private static void Humanoid(MacroBuilder b)
        {
            var (bodyGraph, faceGraph, shapeGraph) = HumanoidLibrary();

            var height = b.Add<MeshParameterNode>(0f, Row * 2f, n => { n.ParameterName = "Height"; n.Default = HumanoidRecipe.BaseHeight; n.Name = "Height"; });
            var build = b.Add<MeshParameterNode>(0f, Row * 3f, n => { n.ParameterName = "Build"; n.Default = 1f; n.Name = "Build"; });

            var body = Call(b, Column, 0f, bodyGraph);
            var face = Call(b, Column * 2f, 0f, faceGraph);
            b.Link(body, MeshNode.MeshPort, face, "Body");
            var shape = Call(b, Column * 3.5f, 0f, shapeGraph);
            b.Link(face, MeshNode.MeshPort, shape, "Body");
            var smooth = b.Add<MeshSubdivideNode>(Column * 5f, 0f, n =>
            {
                n.Name = "Smooth"; n.Levels = HumanoidRecipe.SmoothLevels; n.Smooth = true; n.Method = MeshOperations.SubdivideMethod.CatmullClark; n.SmoothAngle = 80f;
            });
            b.Link(shape, MeshNode.MeshPort, smooth, "In");

            // Sized: Height / 1.8 all over, Build across (X and Z).
            var heightScale = b.Add<MeshMathNode>(Column * 5f, Row * 2f, n => { n.Name = "Height Scale"; n.Formula = "a / " + HumanoidRecipe.BaseHeight.ToString(CultureInfo.InvariantCulture); });
            b.Link(height, "Value", heightScale, "A");
            var broadness = b.Add<MeshMakeVectorNode>(Column * 5f, Row * 3.5f, n => { n.Name = "Broadness"; n.Y = 1f; });
            b.Link(build, "Value", broadness, "X");
            b.Link(build, "Value", broadness, "Z");
            var size = b.Add<MeshTransformNode>(Column * 6f, 0f, n => n.Name = "Size");
            b.Link(smooth, MeshNode.MeshPort, size, "In");
            b.Link(broadness, "Vector", size, "Scale");
            b.Link(heightScale, "Result", size, "Uniform Scale");

            var output = b.Existing<MeshOutputNode>();
            if (output != null && output.GetPort(MeshNode.MeshPort)?.ConnectionCount == 0)
                b.Link(size, MeshNode.MeshPort, output, MeshNode.MeshPort);
        }

        /// <summary>The Humanoid's three function graphs, made in <see cref="LibraryFolder"/> if they aren't there yet.</summary>
        internal static (MeshGraph body, MeshGraph face, MeshGraph shape) HumanoidLibrary()
        {
            var body = LibraryGraph("Humanoid Body",
                "A 1.8 m humanoid in a T-pose, box-modelled: loop-cut cubes (torso, neck, head, shoulders, arms, hands, legs, feet) welded at the joints so edge loops run through every joint, relaxed so their corners round off. The head has extra loops for a face. Feed it to Humanoid Face, then Humanoid Shape, then Subdivide (Catmull-Clark).",
                "the start of a character\na base mesh to shape with Grab",
                BuildBody);
            var face = LibraryGraph("Humanoid Face",
                "Gives the head of a Humanoid Body a face: rounds it, then shapes the skull, jaw, ears, eye sockets, brow, nose, cheekbones, lips and chin with mirrored Grabs, each feature a slider (0 none, 1 as made, 2 twice as much).",
                "a character's face\nvarying faces across a crowd (wire Random or Parameters into the sliders)",
                b => BuildFace(b, body));
            var shape = LibraryGraph("Humanoid Shape",
                "Shapes a humanoid body (from Humanoid Body, usually through Humanoid Face): pinches the waist, brings the chest forward and the seat back, slopes the shoulders, swings the arms down from the T-pose (degrees) and sizes the head.",
                "a character's figure and pose\nposing the arms for rigging (A-pose or T-pose)",
                b => BuildShape(b, body, face));
            return (body, face, shape);
        }

        private static MeshGraph LibraryGraph(string name, string description, string useFor, Action<MacroBuilder> build)
        {
            string path = $"{LibraryFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<MeshGraph>(path);
            if (existing != null)
                return existing;
            MeshGraphEditor.EnsureFolder(LibraryFolder);
            var graph = ScriptableObject.CreateInstance<MeshGraph>();
            AssetDatabase.CreateAsset(graph, path);
            graph.Description = description;
            graph.Function.InLibrary = true;
            graph.Function.Path = LibraryPath;
            graph.Function.UseFor = useFor;
            SchematicMacros.BuildInto(graph, build);
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssetIfDirty(graph);
            return graph;
        }

        // A Function node calling a graph, its pins made.
        private static MeshFunctionNode Call(MacroBuilder b, float x, float y, MeshGraph graph)
            => b.Add<MeshFunctionNode>(x, y, n => { n.Calls = graph; n.Name = graph.name; n.SyncPorts(); });

        // The graph's Output (a new one, in a new graph), with no LODs of its own: whatever calls it makes those.
        private static MeshOutputNode Output(MacroBuilder b, float x, float y)
            => b.Existing<MeshOutputNode>() ?? b.Add<MeshOutputNode>(x, y, n => { n.Name = "Output"; n.GenerateLODs = false; });

        // A slider Parameter, for a Function node calling the graph.
        private static MeshParameterNode Slider(MacroBuilder b, float x, float y, HumanoidRecipe.Slider slider)
            => b.Add<MeshParameterNode>(x, y, n =>
            {
                n.Name = slider.Name;
                n.ParameterName = slider.Name;
                n.Default = slider.Default;
                n.UI = new ParameterUI { Control = ParameterControl.Slider, Min = slider.Min, Max = slider.Max, Group = slider.Group, Description = slider.Description };
            });

        // One row per part (its cube, loop cuts, detail loops, shaping Grabs and placement), the limbs merged and
        // mirrored; then along the bottom: the joints welded (Select Near, Delete Selected, Join) and relaxed.
        private static void BuildBody(MacroBuilder b)
        {
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

            // Welded and relaxed, along the bottom.
            float bodyY = y + Row;
            float bx = Column;
            MeshNode body = merged;
            MeshNode Next(MeshNode node)
            {
                b.Link(body, MeshNode.MeshPort, node, "In");
                bx += Column;
                return body = node;
            }
            Next(b.Add<MeshSelectNearNode>(bx, bodyY, n => { n.Name = "Joint Ends"; n.Distance = HumanoidRecipe.JoinDistance; }));
            Next(b.Add<MeshDeleteSelectedNode>(bx, bodyY, n => { n.Name = "Open The Joints"; n.AllCorners = true; }));
            Next(b.Add<MeshJoinNode>(bx, bodyY, n => { n.Name = "Weld The Joints"; n.Distance = HumanoidRecipe.JoinDistance * 2f; n.Mode = MeshSelection.JoinMode.Weld; n.SmoothAngle = 60f; }));
            Next(b.Add<MeshRelaxNode>(bx, bodyY, n => { n.Name = "Round Off"; n.Iterations = HumanoidRecipe.RelaxIterations; n.Strength = HumanoidRecipe.RelaxStrength; }));
            b.Link(body, MeshNode.MeshPort, Output(b, bx, bodyY), MeshNode.MeshPort);
        }

        // The body comes in (previewed with Humanoid Body); the head is selected and rounded, each feature's Grabs
        // driven by its slider, and the selection cleared.
        private static void BuildFace(MacroBuilder b, MeshGraph bodyGraph)
        {
            var preview = Call(b, 0f, Row, bodyGraph);
            var input = b.Add<MeshInputNode>(Column, 0f, n => { n.Name = "Body"; n.InputName = "Body"; n.Description = "A humanoid body (Humanoid Body's), its head on top."; });
            b.Link(preview, MeshNode.MeshPort, input, "Preview");

            var sliders = new Dictionary<string, MeshParameterNode>();
            float sy = Row * 3f;
            foreach (var slider in HumanoidRecipe.FaceSliders)
            {
                sliders[slider.Name] = Slider(b, Column, sy, slider);
                sy += Row;
            }

            var (min, max, falloff) = HumanoidRecipe.HeadRegion(HumanoidRecipe.Body());
            var head = b.Add<MeshSelectBoundsNode>(Column * 2f, 0f, n => { n.Name = "Head"; n.Min = min; n.Max = max; n.Falloff = falloff; });
            b.Link(input, MeshNode.MeshPort, head, "In");
            var round = b.Add<MeshSpherizeNode>(Column * 3f, 0f, n => { n.Name = "Round Head"; n.Amount = HumanoidRecipe.HeadRound; n.Radius = 0f; });
            b.Link(head, MeshNode.MeshPort, round, "In");
            b.Link(sliders[HumanoidRecipe.HeadRoundness], "Value", round, "Amount");

            MeshNode last = round;
            float x = Column * 4f;
            foreach (var step in HumanoidRecipe.FaceSteps())
            {
                var grab = AddGrab(b, x, 0f, step, last);
                if (step.Slider != null && sliders.TryGetValue(step.Slider, out var driver))
                    b.Link(driver, "Value", grab, "Strength");
                last = grab;
                x += Column;
            }
            var clear = b.Add<MeshEditSelectionNode>(x, 0f, n => { n.Name = "Whole Body Again"; n.Edit = SelectionEdit.Clear; });
            b.Link(last, MeshNode.MeshPort, clear, "In");
            b.Link(clear, MeshNode.MeshPort, Output(b, x + Column, 0f), MeshNode.MeshPort);
        }

        // The body comes in (previewed with Humanoid Body through Humanoid Face); the whole-body Grabs each driven by
        // a slider: the arms' by degrees (a / 45 of the made turn), the head's by its size.
        private static void BuildShape(MacroBuilder b, MeshGraph bodyGraph, MeshGraph faceGraph)
        {
            var previewBody = Call(b, -Column, Row, bodyGraph);
            var previewFace = Call(b, 0f, Row, faceGraph);
            b.Link(previewBody, MeshNode.MeshPort, previewFace, "Body");
            var input = b.Add<MeshInputNode>(Column, 0f, n => { n.Name = "Body"; n.InputName = "Body"; n.Description = "A humanoid body in a T-pose (Humanoid Body's, usually through Humanoid Face)."; });
            b.Link(previewFace, MeshNode.MeshPort, input, "Preview");

            var sliders = new Dictionary<string, MeshParameterNode>();
            float sy = Row * 3f;
            foreach (var slider in HumanoidRecipe.ShapeSliders)
            {
                sliders[slider.Name] = Slider(b, Column, sy, slider);
                sy += Row;
            }

            MeshNode last = input;
            float x = Column * 2f;
            foreach (var step in HumanoidRecipe.BodySteps())
            {
                var grab = AddGrab(b, x, 0f, step, last);
                if (step.Name == HumanoidRecipe.ArmsStep)
                {
                    var turn = b.Add<MeshMathNode>(x, Row * 1.5f, n => { n.Name = "Arms Down Strength"; n.Formula = "a / " + HumanoidRecipe.ArmsDown.ToString(CultureInfo.InvariantCulture); });
                    b.Link(sliders[HumanoidRecipe.ArmsStep], "Value", turn, "A");
                    b.Link(turn, "Result", grab, "Strength");
                }
                else if (step.Name == HumanoidRecipe.HeadStep)
                {
                    var scale = b.Add<MeshMakeVectorNode>(x, Row * 1.5f, n => n.Name = "Head Scale");
                    b.Link(sliders[HumanoidRecipe.HeadStep], "Value", scale, "X");
                    b.Link(sliders[HumanoidRecipe.HeadStep], "Value", scale, "Y");
                    b.Link(sliders[HumanoidRecipe.HeadStep], "Value", scale, "Z");
                    b.Link(scale, "Vector", grab, "Scale");
                }
                else if (step.Slider != null && sliders.TryGetValue(step.Slider, out var driver))
                    b.Link(driver, "Value", grab, "Strength");
                last = grab;
                x += Column;
            }
            b.Link(last, MeshNode.MeshPort, Output(b, x, 0f), MeshNode.MeshPort);
        }

        #endregion

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
