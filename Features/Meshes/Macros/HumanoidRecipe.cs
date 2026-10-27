using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// A humanoid mannequin's body plan: its parts (shapes, where they sit, how they're turned), at 1.8 m tall, feet on
    /// the origin, facing +Z, arms in a relaxed A-pose. The Humanoid macro builds it as nodes you can edit (one Shape
    /// and Transform per part, the right arm and leg mirrored to the left); <see cref="Build"/> makes it directly.
    /// </summary>
    public static class HumanoidRecipe
    {
        public enum Shape { Sphere, Cylinder, Lathe }

        public sealed class Part
        {
            public string Name;
            public Shape Shape;
            /// <summary>Sphere: radius. Cylinder: bottom radius. Lathe: radius the profile multiplies.</summary>
            public float Radius;
            /// <summary>Cylinder: top radius.</summary>
            public float TopRadius;
            /// <summary>Cylinder and Lathe: height.</summary>
            public float Height;
            /// <summary>Lathe: radius (times Radius) from bottom (time 0) to top (time 1).</summary>
            public Keyframe[] Profile;
            public Vector3 Position, Rotation, Scale = Vector3.one;
            /// <summary>On the right side: mirrored to the left.</summary>
            public bool Limb;
            /// <summary>Sized by Head Size.</summary>
            public bool Head;
        }

        /// <summary>How tall the parts are made (Height scales from this).</summary>
        public const float BaseHeight = 1.8f;

        // Arms hang 10 degrees out from straight down, legs 2: rotations about Z of 180 + that turn a part built
        // standing up so it hangs from its joint.
        private const float ArmOut = 10f, LegOut = 2f;

        private static Vector3 Down(float outDegrees, float length)
            => new(Mathf.Sin(outDegrees * Mathf.Deg2Rad) * length, -Mathf.Cos(outDegrees * Mathf.Deg2Rad) * length, 0f);

        /// <summary>Every part, in building order.</summary>
        public static List<Part> Parts()
        {
            var shoulder = new Vector3(0.2f, 1.42f, 0f);
            var elbow = shoulder + Down(ArmOut, 0.30f);
            var wrist = elbow + Down(ArmOut, 0.27f);
            var hip = new Vector3(0.095f, 0.92f, 0f);
            var knee = hip + Down(LegOut, 0.44f);
            var ankle = knee + Down(LegOut, 0.42f);
            var armTurn = new Vector3(0f, 0f, 180f + ArmOut);
            var legTurn = new Vector3(0f, 0f, 180f + LegOut);

            return new List<Part>
            {
                new() { Name = "Head", Shape = Shape.Sphere, Radius = 0.11f, Position = new Vector3(0f, 1.66f, 0.01f), Scale = new Vector3(0.88f, 1.08f, 1f), Head = true },
                new() { Name = "Neck", Shape = Shape.Cylinder, Radius = 0.05f, TopRadius = 0.045f, Height = 0.12f, Position = new Vector3(0f, 1.46f, 0f) },
                new()
                {
                    Name = "Torso", Shape = Shape.Lathe, Radius = 0.17f, Height = 0.58f, Position = new Vector3(0f, 0.9f, 0f), Scale = new Vector3(1.1f, 1f, 0.68f),
                    // Hips, waist, chest, shoulders rounding in to the neck.
                    Profile = new[] { new Keyframe(0f, 0.92f), new Keyframe(0.15f, 0.95f), new Keyframe(0.38f, 0.8f), new Keyframe(0.65f, 0.97f), new Keyframe(0.86f, 0.98f), new Keyframe(1f, 0.45f) },
                },
                new() { Name = "Hips", Shape = Shape.Sphere, Radius = 0.16f, Position = new Vector3(0f, 0.93f, 0f), Scale = new Vector3(1.05f, 0.62f, 0.75f) },
                new() { Name = "Shoulder", Shape = Shape.Sphere, Radius = 0.062f, Position = shoulder, Limb = true },
                new() { Name = "Upper Arm", Shape = Shape.Cylinder, Radius = 0.05f, TopRadius = 0.042f, Height = 0.30f, Position = shoulder, Rotation = armTurn, Limb = true },
                new() { Name = "Elbow", Shape = Shape.Sphere, Radius = 0.042f, Position = elbow, Limb = true },
                new() { Name = "Forearm", Shape = Shape.Cylinder, Radius = 0.042f, TopRadius = 0.032f, Height = 0.27f, Position = elbow, Rotation = armTurn, Limb = true },
                new() { Name = "Hand", Shape = Shape.Sphere, Radius = 1f, Position = wrist + Down(ArmOut, 0.085f), Rotation = new Vector3(0f, 0f, ArmOut), Scale = new Vector3(0.028f, 0.09f, 0.048f), Limb = true },
                new() { Name = "Thigh", Shape = Shape.Cylinder, Radius = 0.085f, TopRadius = 0.06f, Height = 0.44f, Position = hip, Rotation = legTurn, Limb = true },
                new() { Name = "Knee", Shape = Shape.Sphere, Radius = 0.06f, Position = knee, Limb = true },
                new() { Name = "Shin", Shape = Shape.Cylinder, Radius = 0.058f, TopRadius = 0.04f, Height = 0.42f, Position = knee, Rotation = legTurn, Limb = true },
                new() { Name = "Foot", Shape = Shape.Sphere, Radius = 1f, Position = new Vector3(ankle.x, 0.036f, 0.05f), Scale = new Vector3(0.05f, 0.036f, 0.13f), Limb = true },
            };
        }

        /// <summary>Builds a part's shape where it was made (before its Transform).</summary>
        public static MeshData ShapeOf(Part part)
        {
            switch (part.Shape)
            {
                case Shape.Sphere:
                    return MeshShapes.Sphere(part.Radius, 20, 14);
                case Shape.Cylinder:
                    return MeshShapes.Cylinder(part.Radius, part.TopRadius, part.Height, 16, 4);
                default:
                    const int steps = 16;
                    var radii = new float[steps + 1];
                    for (int i = 0; i <= steps; i++)
                        radii[i] = Sample(part.Profile, i / (float)steps) * part.Radius;
                    return MeshShapes.Lathe(radii, part.Height, 24);
            }
        }

        // The profile, straight between its keys (the node's curve is smooth; close enough for building directly).
        private static float Sample(Keyframe[] keys, float t)
        {
            if (keys == null || keys.Length == 0)
                return 1f;
            if (t <= keys[0].time)
                return keys[0].value;
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].time)
                    return Mathf.Lerp(keys[i - 1].value, keys[i].value, (t - keys[i - 1].time) / Mathf.Max(1e-5f, keys[i].time - keys[i - 1].time));
            return keys[keys.Length - 1].value;
        }

        /// <summary>
        /// The mannequin as a mesh: <paramref name="height"/> tall, <paramref name="build"/> times as broad (1 is
        /// average), its head <paramref name="headSize"/> times the size.
        /// </summary>
        public static MeshData Build(float height = BaseHeight, float build = 1f, float headSize = 1f)
        {
            var body = new MeshData();
            var limbs = new MeshData();
            foreach (var part in Parts())
            {
                var scale = part.Head ? part.Scale * headSize : part.Scale;
                (part.Limb ? limbs : body).Append(ShapeOf(part), MeshModifiers.TRS(part.Position, part.Rotation, scale));
            }
            MeshModifiers.Mirror(limbs, 0);
            body.Append(limbs);
            body.Transform(MeshModifiers.TRS(Vector3.zero, Vector3.zero, new Vector3(build, 1f, build) * (height / BaseHeight)));
            return body;
        }
    }
}
