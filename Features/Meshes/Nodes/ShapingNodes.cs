// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Meshes
{
    // Modelling: booleans, subdivision, spherize, loop cuts and extrusion.

    [SchematicDoc(@"{""name"": ""Boolean"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Model"", ""description"": ""Combines two closed meshes as solids. Union: one shell, the insides gone. Subtract: A with B carved out (windows, doorways, a bite). Intersect: only where they overlap. Both should be closed (no holes); the carved surfaces take B's material slot. Points closer than Merge Distance afterwards become one, cleaning up slivers along the cut (0: off)."", ""inputs"": [{""name"": ""A"", ""type"": ""MeshData""}, {""name"": ""B"", ""type"": ""MeshData""}, {""name"": ""Merge Distance"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Boolean", Path = "Model"), Tags(MeshNode.Tag)]
    public sealed class MeshBooleanNode : MeshNode
    {
        [Input("A", Editable = false)] [NonSerialized] public MeshData A;
        [Input("B", Editable = false)] [NonSerialized] public MeshData B;
        [Tooltip("Points closer than this (meters) are joined, so the result has no tiny cracks.")]
        [Input("Merge Distance", Editable = true)] public float MergeDistance = 0.001f;
        [Tooltip("Union joins them into one shell; Subtract carves B out of A; Intersect keeps only where they overlap.")]
        [Editable] public MeshBoolean.Operation Operation = MeshBoolean.Operation.Subtract;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var a = c.Mesh(this, "A");
            var b = c.Mesh(this, "B");
            var operation = Operation;
            float merge = c.Number(this, "Merge Distance", MergeDistance);
            return new MeshOp(() => MeshBoolean.Apply(a.Get(), b.Get(), operation, merge));
        }
    }

    [SchematicDoc(@"{""name"": ""Subdivide"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Model"", ""description"": ""Splits every face into four, Levels times (each level is 4x the triangles). Smooth rounds it off as it goes (a cube becomes a smooth blob, a low-poly sketch an organic shape); off keeps the shape exactly, just with more triangles for Bend, Twist and Noise. Method Catmull-Clark is for quads (boxes and loop-cut cubes: box modelling), keeping edge loops even; Loop is for triangle meshes."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Levels"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Subdivide", Path = "Model"), Tags(MeshNode.Tag)]
    public sealed class MeshSubdivideNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("How many times to split every face in four (each level is 4x the triangles; 2 or 3 is usually plenty).")]
        [Input("Levels", Editable = true)] public int Levels = 1;
        [Tooltip("Round it off as it splits (a cube becomes a blob). Off: same shape, just more triangles.")]
        [Editable] public bool Smooth = true;
        [Editable, Tooltip("Catmull-Clark: for quads (boxes, loop-cut cubes, box modelling). Loop: for triangle meshes.")] public MeshOperations.SubdivideMethod Method = MeshOperations.SubdivideMethod.CatmullClark;
        [Editable, Tooltip("Normals are worked out again afterwards: edges sharper than this stay hard (-1: keep them).")] public float SmoothAngle = 60f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int levels = Mathf.Clamp(c.Integer(this, "Levels", Levels), 0, 5);
            bool smooth = Smooth;
            var method = Method;
            float angle = SmoothAngle;
            return new MeshOp(() =>
            {
                var mesh = smooth && method == MeshOperations.SubdivideMethod.CatmullClark
                    ? MeshOperations.SubdivideQuads(input.Get(), levels)
                    : MeshOperations.Subdivide(input.Get(), levels, smooth);
                if (angle >= 0f && smooth)
                    mesh.RecalculateNormals(angle);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Spherize"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Model"", ""description"": ""Pulls a mesh towards a sphere around its middle by Amount: 0 leaves it, 1 makes it a perfect sphere, between rounds it off (a subdivided box at 0.3 is a pillow, at 0.7 a pebble). Radius 0 uses its average size. With a selection, only what's selected moves, towards a sphere round the selection (round a head on a whole body). Needs enough triangles to round smoothly."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Amount"", ""type"": ""float""}, {""name"": ""Radius"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Spherize", Path = "Model"), Tags(MeshNode.Tag)]
    public sealed class MeshSpherizeNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Amount", Editable = true)] public float Amount = 0.5f;
        [Input("Radius", Editable = true)] public float Radius;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float amount = c.Number(this, "Amount", Amount), radius = c.Number(this, "Radius", Radius);
            return new MeshOp(() =>
            {
                var before = input.Get();
                var mesh = MeshOperations.Spherize(before, amount, radius);
                MeshSelection.Restrict(mesh, before);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Loop Cut"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Model"", ""description"": ""Cuts rings of edges around a mesh across Axis: Cuts evenly spaced slices. The shape doesn't change, but it can then bend, twist and taper smoothly, and Extrude has new faces to pull. With a selection, only the selected faces are cut, evenly across what's selected: extra loops for a face without slicing the whole body."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Cuts"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Loop Cut", Path = "Model"), Tags(MeshNode.Tag)]
    public sealed class MeshLoopCutNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("The axis the rings go around: Y cuts horizontal rings up its height.")]
        [Editable] public MeshAxis Axis = MeshAxis.Y;
        [Tooltip("How many evenly spaced rings to cut.")]
        [Input("Cuts", Editable = true)] public int Cuts = 4;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int axis = (int)Axis;
            int cuts = Mathf.Clamp(c.Integer(this, "Cuts", Cuts), 0, 256);
            return new MeshOp(() => MeshOperations.LoopCut(input.Get(), axis, cuts));
        }
    }

    [SchematicDoc(@"{""name"": ""Extrude"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Model"", ""description"": ""Pulls faces out of a mesh, with walls joining them to where they were: a box's top pulled up into a tower, a plane into a slab, the tops of a stamped grid of tiles into a city. Faces are picked by Select: those facing within Angle degrees of Direction, all of them, material slot Slot, or Selected (faces whose corners are all selected by Select nodes). They move Distance along Direction (or along their own normals), and Cap Scale shrinks or grows the moved faces about their middle (0.5: a taper). Wall Slot puts the new walls in their own material slot (-1: the first)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Direction"", ""type"": ""Vector3""}, {""name"": ""Distance"", ""type"": ""float""}, {""name"": ""Cap Scale"", ""type"": ""float""}, {""name"": ""Angle"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Extrude", Path = "Model"), Tags(MeshNode.Tag)]
    public sealed class MeshExtrudeNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("Which faces are pulled: Facing Direction (within Angle of Direction), All, a Material Slot, or the Selected ones.")]
        [Editable] public MeshOperations.ExtrudeSelection Select = MeshOperations.ExtrudeSelection.FacingDirection;
        [Input("Direction", Editable = true)] public Vector3 Direction = Vector3.up;
        [Input("Distance", Editable = true)] public float Distance = 0.5f;
        [Tooltip("How big the pulled-out faces are compared with where they started: 1 straight out, under 1 tapers in, over 1 flares.")]
        [Input("Cap Scale", Editable = true)] public float CapScale = 1f;
        [Tooltip("With Select Facing Direction: how far a face can point from Direction and still be pulled (degrees).")]
        [Input("Angle", Editable = true)] public float Angle = 30f;
        [Editable, Tooltip("With Select: Material Slot, the slot whose faces are pulled.")] public int Slot;
        [Editable, Tooltip("Move along the faces' own normals (puffs a shape out) instead of Direction.")] public bool AlongNormals;
        [Editable, Tooltip("The material slot the new walls go in (-1: the first).")] public int WallSlot = -1;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var select = Select;
            var direction = c.Vector(this, "Direction", Direction);
            float distance = c.Number(this, "Distance", Distance), capScale = c.Number(this, "Cap Scale", CapScale), angle = c.Number(this, "Angle", Angle);
            int slot = Slot, wallSlot = WallSlot;
            bool alongNormals = AlongNormals;
            return new MeshOp(() => MeshOperations.Extrude(input.Get(), select, direction, angle, slot, distance, alongNormals, capScale, wallSlot));
        }
    }
}
