using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// A humanoid's body plan, box-modelled: every part is a cube with loop cuts, shaped with Grab, and the parts are
    /// welded together at the joints so edge loops run through them (for bending when animated). 1.8 m tall, feet on the
    /// origin, facing +Z, built in a T-pose and then swung down into an A-pose.
    ///
    /// How the joints fit: every limb is cut once across each of its two short sides, so its ends are rings of eight
    /// corners round a middle point. The torso is cut so that the patches where the neck, shoulders and legs go are
    /// rings of the same size, in the same place. Merged, the matching ends sit a couple of millimetres apart; Select
    /// Near picks those ends, Delete Selected cuts them away (leaving open rings), and Join welds each pair of rings into one. Then the
    /// head is rounded (Spherize on it alone), the whole body is shaped with Grab (waist, chest, seat, shoulders, jaw,
    /// the arms swung down, the head sized), relaxed so the cubes' corners round off, and subdivided (Catmull-Clark).
    ///
    /// The Humanoid macro builds it as nodes you can edit; <see cref="Build"/> makes it directly.
    /// </summary>
    public static class HumanoidRecipe
    {
        /// <summary>How tall it's made (the Height Parameter scales from this).</summary>
        public const float BaseHeight = 1.8f;

        /// <summary>How far the arms swing down from the T-pose, in degrees.</summary>
        public const float ArmsDown = 45f;

        /// <summary>Relax passes (and their strength) that round the cubes' corners before subdividing.</summary>
        public const int RelaxIterations = 6;
        public const float RelaxStrength = 0.5f;

        /// <summary>How far the head is pulled towards a sphere (Spherize on the head alone).</summary>
        public const float HeadRound = 0.45f;

        /// <summary>Catmull-Clark levels the body is rounded off by at the end.</summary>
        public const int SmoothLevels = 2;

        /// <summary>How close the joints' ends must be to be picked and welded.</summary>
        public const float JoinDistance = 0.006f;

        // Each part sits this far from the one it joins (ends touching exactly would already count as one piece, and
        // Select Near looks for other pieces); Join's weld closes it.
        private const float Gap = 0.002f;

        /// <summary>One Grab: a box 0 to 1 across the mesh's bounds, and what's done to it (see <see cref="MeshModifiers.Grab"/>).</summary>
        public sealed class GrabStep
        {
            public string Name;
            public Vector3 Min, Max = Vector3.one, Pivot = new(0.5f, 0.5f, 0.5f), Move, Rotate, Scale = Vector3.one;
            public float Falloff;
            public bool MirrorX;

            public MeshData Apply(MeshData mesh) => MeshModifiers.Grab(mesh, Min, Max, Falloff, Pivot, Move, Rotate, Scale, MirrorX);
        }

        public sealed class Part
        {
            public string Name;
            /// <summary>The cube it starts as.</summary>
            public Vector3 Size;
            /// <summary>Loop cuts across X, Y and Z.</summary>
            public int CutsX, CutsY, CutsZ;
            /// <summary>Its shaping, in order, on the cube where it was made (centred on the origin).</summary>
            public readonly List<GrabStep> Shaping = new();
            /// <summary>Where its middle goes.</summary>
            public Vector3 Position;
            /// <summary>On the right side: mirrored to the left.</summary>
            public bool Limb;
        }

        // The torso, and the cuts that make its sockets: five across X (the middle two cells for the neck, the outer two
        // each side for a leg), five up Y (the top two cells of each side for a shoulder), one across Z.
        private static readonly Vector3 TorsoSize = new(0.32f, 0.62f, 0.2f);
        private const float TorsoBottom = 0.88f;
        private const int TorsoCutsX = 5, TorsoCutsY = 5;

        // A part's end scaled to a new size: the box at one end (axis, 0 or 1), the grab easing in along the whole part.
        private static GrabStep End(string name, int axis, float end, Vector3 scale, Vector3 move = default)
        {
            var min = Vector3.zero;
            var max = Vector3.one;
            min[axis] = max[axis] = end;
            return new GrabStep { Name = name, Min = min, Max = max, Falloff = 1f, Scale = scale, Move = move };
        }

        /// <summary>Every part, in building order.</summary>
        public static List<Part> Parts()
        {
            float cellX = TorsoSize.x / (TorsoCutsX + 1);
            float cellY = TorsoSize.y / (TorsoCutsY + 1);
            float top = TorsoBottom + TorsoSize.y;
            float shoulderY = top - cellY;                 // the middle of the top two cells up the side
            float shoulderHeight = cellY * 2f;
            float side = TorsoSize.x * 0.5f;
            float legWidth = cellX * 2f;                   // the outer two cells across the bottom
            float legX = side - cellX;
            const float knee = 0.48f, ankle = 0.08f;
            const float kneeIn = 0.01f;

            var torso = new Part
            {
                Name = "Torso", Size = TorsoSize, CutsX = TorsoCutsX, CutsY = TorsoCutsY, CutsZ = 1,
                Position = new Vector3(0f, TorsoBottom + TorsoSize.y * 0.5f, 0f),
            };

            // Neck: from the torso's middle two top cells up to the head's underside, narrowing and leaning forward.
            var neckTop = new Vector3(0.08f, 0f, 0.1f);
            var neck = new Part { Name = "Neck", Size = new Vector3(cellX * 2f, 0.08f, TorsoSize.z), CutsX = 1, CutsY = 1, CutsZ = 1, Position = new Vector3(0f, top + 0.04f, 0f) };
            neck.Shaping.Add(End("Narrow To Head", 1, 1f, new Vector3(neckTop.x / neck.Size.x, 1f, neckTop.z / neck.Size.z), new Vector3(0f, 0f, 0.01f)));

            // Head: cut in quarters across X and Z so its underside has the neck's ring, and in quarters up (loops for
            // the brow, eyes and mouth later).
            var head = new Part { Name = "Head", Size = new Vector3(0.16f, 0.22f, 0.2f), CutsX = 3, CutsY = 3, CutsZ = 3, Position = new Vector3(0f, top + 0.08f + 0.11f, 0.01f) };

            // Arm, along +X: shoulder, upper arm, forearm, hand, each ending the size the next one starts.
            var shoulder = new Part { Name = "Shoulder", Size = new Vector3(0.08f, shoulderHeight, TorsoSize.z), CutsX = 1, CutsY = 1, CutsZ = 1, Position = new Vector3(side + 0.04f, shoulderY, 0f), Limb = true };
            shoulder.Shaping.Add(End("Round To Arm", 0, 1f, new Vector3(1f, 0.12f / shoulderHeight, 0.12f / TorsoSize.z)));

            var upperArm = new Part { Name = "Upper Arm", Size = new Vector3(0.26f, 0.12f, 0.12f), CutsX = 3, CutsY = 1, CutsZ = 1, Position = new Vector3(side + 0.08f + 0.13f, shoulderY, 0f), Limb = true };
            upperArm.Shaping.Add(End("Narrow To Elbow", 0, 1f, new Vector3(1f, 0.085f / 0.12f, 0.085f / 0.12f)));

            var forearm = new Part { Name = "Forearm", Size = new Vector3(0.25f, 0.085f, 0.085f), CutsX = 3, CutsY = 1, CutsZ = 1, Position = new Vector3(side + 0.34f + 0.125f, shoulderY, 0f), Limb = true };
            forearm.Shaping.Add(End("Flatten To Wrist", 0, 1f, new Vector3(1f, 0.05f / 0.085f, 0.07f / 0.085f)));
            forearm.Shaping.Add(new GrabStep { Name = "Forearm Muscle", Min = new Vector3(0.15f, 0f, 0f), Max = new Vector3(0.35f, 1f, 1f), Falloff = 0.15f, Scale = new Vector3(1f, 1.12f, 1.12f) });

            var hand = new Part { Name = "Hand", Size = new Vector3(0.19f, 0.05f, 0.07f), CutsX = 2, CutsY = 1, CutsZ = 1, Position = new Vector3(side + 0.59f + 0.095f, shoulderY, 0f), Limb = true };
            hand.Shaping.Add(End("Flat Fingers", 0, 1f, new Vector3(1f, 0.5f, 1.25f)));
            hand.Shaping.Add(new GrabStep { Name = "Palm", Min = new Vector3(0.3f, 0f, 0f), Max = new Vector3(0.6f, 1f, 1f), Falloff = 0.25f, Scale = new Vector3(1f, 1f, 1.15f) });

            // Leg, down -Y: thigh (the knee drawn in a little), shin, then the foot under the ankle.
            var thigh = new Part { Name = "Thigh", Size = new Vector3(legWidth, TorsoBottom - knee, TorsoSize.z), CutsX = 1, CutsY = 3, CutsZ = 1, Position = new Vector3(legX, (TorsoBottom + knee) * 0.5f, 0f), Limb = true };
            thigh.Shaping.Add(End("Narrow To Knee", 1, 0f, new Vector3(0.1f / legWidth, 1f, 0.11f / TorsoSize.z), new Vector3(-kneeIn, 0f, 0f)));
            thigh.Shaping.Add(new GrabStep { Name = "Thigh Front", Min = new Vector3(0f, 0.4f, 0.5f), Max = new Vector3(1f, 0.8f, 1f), Falloff = 0.2f, Move = new Vector3(0f, 0f, 0.01f) });

            float shinX = legX - kneeIn;
            var shin = new Part { Name = "Shin", Size = new Vector3(0.1f, knee - ankle, 0.11f), CutsX = 1, CutsY = 3, CutsZ = 1, Position = new Vector3(shinX, (knee + ankle) * 0.5f, 0f), Limb = true };
            shin.Shaping.Add(End("Narrow To Ankle", 1, 0f, new Vector3(0.9f, 1f, 0.08f / 0.11f)));
            shin.Shaping.Add(new GrabStep { Name = "Calf", Min = new Vector3(0f, 0.5f, 0f), Max = new Vector3(1f, 0.7f, 0.5f), Falloff = 0.2f, Move = new Vector3(0f, 0f, -0.015f) });

            // Foot: cut in sixths along its length so the back two cells of its top are the ankle's ring.
            var foot = new Part { Name = "Foot", Size = new Vector3(0.09f, ankle, 0.24f), CutsX = 1, CutsY = 1, CutsZ = 5, Position = new Vector3(shinX, ankle * 0.5f, 0.08f), Limb = true };
            foot.Shaping.Add(new GrabStep { Name = "Toes Down", Min = new Vector3(0f, 0.6f, 0.6f), Max = Vector3.one, Falloff = 0.25f, Move = new Vector3(0f, -0.03f, 0f) });

            // Nudged apart along each chain, a gap at every joint.
            neck.Position.y += Gap;
            head.Position.y += Gap * 2f;
            shoulder.Position.x += Gap;
            upperArm.Position.x += Gap * 2f;
            forearm.Position.x += Gap * 3f;
            hand.Position.x += Gap * 4f;
            thigh.Position.y -= Gap;
            shin.Position.y -= Gap * 2f;
            foot.Position.y -= Gap * 3f;

            return new List<Part> { torso, neck, head, shoulder, upperArm, forearm, hand, thigh, shin, foot };
        }

        /// <summary>A part's mesh: its cube, loop cut, shaped and put in place.</summary>
        public static MeshData PartMesh(Part part)
        {
            var mesh = MeshShapes.Box(part.Size);
            mesh = MeshOperations.LoopCut(mesh, 0, part.CutsX);
            mesh = MeshOperations.LoopCut(mesh, 1, part.CutsY);
            mesh = MeshOperations.LoopCut(mesh, 2, part.CutsZ);
            foreach (var step in part.Shaping)
                mesh = step.Apply(mesh);
            mesh.Transform(MeshModifiers.TRS(part.Position, Vector3.zero, Vector3.one));
            return mesh;
        }

        /// <summary>Every part merged (the right limbs mirrored to the left), not yet joined.</summary>
        public static MeshData Assemble()
        {
            var body = new MeshData();
            var limbs = new MeshData();
            foreach (var part in Parts())
                (part.Limb ? limbs : body).Append(PartMesh(part));
            MeshModifiers.Mirror(limbs, 0);
            body.Append(limbs);
            return body;
        }

        /// <summary>
        /// Welds the parts at their joints: the touching ends picked (Select Near), cut away (Delete Selected, whole
        /// faces only) and the open rings welded (Join, Weld).
        /// </summary>
        public static MeshData Seal(MeshData parts)
        {
            var mesh = parts.Clone();
            var near = MeshSelection.Near(mesh, JoinDistance, 0f);
            MeshSelection.Apply(mesh, i => near[i], MeshSelection.Mode.Replace);
            mesh = MeshSelection.DeleteSelected(mesh, true);
            return MeshSelection.Join(mesh, JoinDistance * 2f, 60f, MeshSelection.JoinMode.Weld);
        }

        // A whole-body grab given in metres on the T-posed body; Steps turns it into 0 to 1 of the bounds at that point.
        private sealed class BodyGrab
        {
            public string Name;
            public Vector3 Min = Vector3.one * -10f, Max = Vector3.one * 10f, Pivot = new(0.5f, 0.5f, 0.5f), Move, Rotate, Scale = Vector3.one;
            public float Falloff;
            public bool MirrorX;
        }

        /// <summary>The names of the whole-body grabs the Parameters drive.</summary>
        public const string ArmsStep = "Arms Down", HeadStep = "Head Size";

        private static List<BodyGrab> BodyGrabs()
        {
            float top = TorsoBottom + TorsoSize.y;
            float side = TorsoSize.x * 0.5f;
            return new List<BodyGrab>
            {
                new() { Name = "Waist", Min = new Vector3(-side, 1.06f, -1f), Max = new Vector3(side, 1.14f, 1f), Falloff = 0.07f, Scale = new Vector3(0.86f, 1f, 0.9f) },
                new() { Name = "Chest", Min = new Vector3(-side, 1.26f, 0f), Max = new Vector3(side, 1.38f, 1f), Falloff = 0.05f, Move = new Vector3(0f, 0f, 0.02f) },
                new() { Name = "Seat", Min = new Vector3(-side, 0.9f, -1f), Max = new Vector3(side, 1f, -0.02f), Falloff = 0.05f, Move = new Vector3(0f, 0f, -0.02f) },
                new() { Name = "Sloped Shoulders", Min = new Vector3(0.1f, top - 0.02f, -1f), Max = new Vector3(side + 0.08f, top + 0.01f, 1f), Falloff = 0.04f, Move = new Vector3(0f, -0.03f, 0f), MirrorX = true },
                new() { Name = "Jaw", Min = new Vector3(-0.08f, top + 0.08f, 0.03f), Max = new Vector3(0.08f, top + 0.14f, 1f), Falloff = 0.03f, Scale = new Vector3(0.8f, 1f, 1f) },
                new() { Name = "Back Of Head", Min = new Vector3(-0.08f, top + 0.16f, -1f), Max = new Vector3(0.08f, top + 0.3f, -0.03f), Falloff = 0.03f, Move = new Vector3(0f, 0f, -0.015f) },
                // Swung down about the shoulder, the shoulder easing into the torso.
                new() { Name = ArmsStep, Min = new Vector3(side + 0.04f, top - 0.24f, -1f), Max = new Vector3(10f, top + 0.02f, 1f), Pivot = new Vector3(0f, 0.5f, 0.5f), Falloff = 0.04f, Rotate = new Vector3(0f, 0f, -ArmsDown), MirrorX = true },
                // Sized about the bottom of the head, the neck easing into it.
                new() { Name = HeadStep, Min = new Vector3(-10f, top + 0.08f, -0.09f), Max = new Vector3(10f, 10f, 0.11f), Pivot = new Vector3(0.5f, 0f, 0.5f), Falloff = 0.02f },
            };
        }

        /// <summary>
        /// The head as Select Bounds takes it on the sealed body (0 to 1 of its bounds), with a little falloff into the
        /// neck: what Round Head spherizes.
        /// </summary>
        public static (Vector3 min, Vector3 max, float falloff) HeadRegion(MeshData sealedBody)
        {
            var bounds = sealedBody.Bounds;
            float headBottom = TorsoBottom + TorsoSize.y + 0.08f;
            float y = (headBottom - bounds.min.y) / bounds.size.y;
            return (new Vector3(0f, y, 0f), Vector3.one, 0.02f / bounds.size.y);
        }

        /// <summary>The head pulled towards a sphere (selected, spherized, the selection cleared again).</summary>
        public static MeshData RoundHead(MeshData sealedBody)
        {
            var (min, max, falloff) = HeadRegion(sealedBody);
            var selected = sealedBody.Clone();
            var weights = MeshSelection.BoundsWeights(selected, min, max, falloff);
            MeshSelection.Apply(selected, i => weights[i], MeshSelection.Mode.Replace);
            var round = MeshOperations.Spherize(selected, HeadRound);
            MeshSelection.Restrict(round, selected);
            MeshSelection.Clear(round);
            return round;
        }

        /// <summary>
        /// The whole-body grabs as Grab nodes take them (0 to 1 of the body's bounds when each runs), worked out on the
        /// sealed, head-rounded body with the arms at <see cref="ArmsDown"/> and the head at full size.
        /// </summary>
        public static List<GrabStep> BodySteps()
        {
            var mesh = RoundHead(Seal(Assemble()));
            var steps = new List<GrabStep>();
            foreach (var grab in BodyGrabs())
            {
                var bounds = mesh.Bounds;
                Vector3 Normalized(Vector3 p)
                {
                    var t = Vector3.zero;
                    for (int axis = 0; axis < 3; axis++)
                        t[axis] = Mathf.Clamp01((p[axis] - bounds.min[axis]) / Mathf.Max(1e-5f, bounds.size[axis]));
                    return t;
                }
                var step = new GrabStep
                {
                    Name = grab.Name, Min = Normalized(grab.Min), Max = Normalized(grab.Max), Pivot = grab.Pivot,
                    Move = grab.Move, Rotate = grab.Rotate, Scale = grab.Scale, MirrorX = grab.MirrorX,
                    // Measured against the height, so it's about the same distance whichever way.
                    Falloff = grab.Falloff / Mathf.Max(1e-5f, bounds.size.y),
                };
                steps.Add(step);
                mesh = step.Apply(mesh);
            }
            return steps;
        }

        /// <summary>
        /// The humanoid as a mesh: <paramref name="height"/> tall, <paramref name="build"/> times as broad (1 is
        /// average), its head <paramref name="headSize"/> times the size, its arms <paramref name="armsDown"/> degrees
        /// down from a T-pose.
        /// </summary>
        public static MeshData Build(float height = BaseHeight, float build = 1f, float headSize = 1f, float armsDown = ArmsDown)
        {
            var mesh = RoundHead(Seal(Assemble()));
            foreach (var step in BodySteps())
            {
                if (step.Name == ArmsStep)
                    step.Rotate = new Vector3(0f, 0f, -armsDown);
                else if (step.Name == HeadStep)
                    step.Scale = Vector3.one * headSize;
                mesh = step.Apply(mesh);
            }
            mesh = MeshModifiers.Relax(mesh, RelaxIterations, RelaxStrength);
            mesh = MeshOperations.SubdivideQuads(mesh, SmoothLevels);
            mesh.RecalculateNormals(80f);
            mesh.Transform(MeshModifiers.TRS(Vector3.zero, Vector3.zero, new Vector3(build, 1f, build) * (height / BaseHeight)));
            return mesh;
        }
    }
}
