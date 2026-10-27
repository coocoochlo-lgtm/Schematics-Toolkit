using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Points in space, each with a rotation (Euler degrees) and a scale: what a Mesh Graph's point wires carry. Made
    /// by looping over space (a grid, a line, a circle, a mesh's surface), shaped by formulas (Point Function), and
    /// turned into geometry by stamping a mesh at each one (Stamp). Plain data: safe on any thread.
    /// </summary>
    public sealed class PointData
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Rotations = new();
        public readonly List<Vector3> Scales = new();

        public int Count => Positions.Count;

        public void Add(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            Positions.Add(position);
            Rotations.Add(rotation);
            Scales.Add(scale);
        }

        public void Add(Vector3 position) => Add(position, Vector3.zero, Vector3.one);

        public void Append(PointData other)
        {
            if (other == null)
                return;
            Positions.AddRange(other.Positions);
            Rotations.AddRange(other.Rotations);
            Scales.AddRange(other.Scales);
        }

        public PointData Clone()
        {
            var copy = new PointData();
            copy.Append(this);
            return copy;
        }

        /// <summary>Where a point puts what's stamped on it.</summary>
        public Matrix4x4 Matrix(int index) => MeshModifiers.TRS(Positions[index], Rotations[index], Scales[index]);
    }

    /// <summary>One step of a mesh plan that makes points (see <see cref="MeshOp"/>).</summary>
    public sealed class PointOp
    {
        private readonly Func<PointData> _make;
        private PointData _result;

        public PointOp(Func<PointData> make) => _make = make;

        public static PointOp Empty => new(() => new PointData());

        public PointData Get() => _result ??= _make() ?? new PointData();

        public PointData Copy() => Get().Clone();
    }
}
