// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Meshes
{
    // Vertex selection: nodes that pick vertices (a weight from 0 to 1 on each), and the nodes that act on the pick.
    // The deformers (Transform, Twist, Bend, Taper, Noise, Spherize, Vertex Function) only move what's selected, and
    // Extrude can pull the selected faces. Each Select node's Mode says how its pick mixes with the one already there.

    [SchematicDoc(@"{""name"": ""Select"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects vertices by a formula: Weight, from 0 (not selected) to 1 (selected), using p (where it is), x y z, nrm (its normal), uv, i (its number), n (how many), sel (its selection so far) and the graph's Parameters. The top half: y > 0. A soft band: smoothstep(0.2, 0.5, y). Mode mixes it with the selection already there."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Editable, Tooltip("How selected each vertex is, 0 to 1.")] public string Weight = "y > 0";
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        private static readonly string[] Variables = { "p", "x", "y", "z", "nrm", "uv", "i", "n", "sel" };

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var weight = c.Expression(this, Weight, Variables);
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                if (weight == null)
                    return mesh;
                var vars = new ExprValue[Variables.Length];
                int n = mesh.VertexCount;
                MeshSelection.Apply(mesh, i =>
                {
                    var p = mesh.Positions[i];
                    vars[0] = p;
                    vars[1] = p.x; vars[2] = p.y; vars[3] = p.z;
                    vars[4] = mesh.Normals[i];
                    vars[5] = (Vector3)mesh.UVs[i];
                    vars[6] = i;
                    vars[7] = n;
                    vars[8] = mesh.Selected(i);
                    return weight.Evaluate(vars).Number;
                }, mode);
                return mesh;
            });
        }
    }

    public enum SelectRegionShape { Sphere, Box }

    [SchematicDoc(@"{""name"": ""Select Region"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects the vertices inside a sphere (Size.x is its radius) or a box (Size is its full size) at Center, fading out over Falloff beyond its edge for soft edits: a belly to puff out, a brow to pull forward."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Center"", ""type"": ""Vector3""}, {""name"": ""Size"", ""type"": ""Vector3""}, {""name"": ""Falloff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select Region", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectRegionNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("Sphere (Size's X is its radius) or Box (Size is its full size).")]
        [Editable] public SelectRegionShape Shape = SelectRegionShape.Sphere;
        [Tooltip("Where the region's centre is (meters, in the mesh's own space).")]
        [Input("Center", Editable = true)] public Vector3 Center;
        [Input("Size", Editable = true)] public Vector3 Size = new(0.5f, 0.5f, 0.5f);
        [Tooltip("How far beyond the edge the selection fades out, for a soft edit (meters). 0 is a hard edge.")]
        [Input("Falloff", Editable = true)] public float Falloff;
        [Tooltip("How it combines with what's already selected: Replace, Add to it, Subtract from it, or Intersect (keep only what's in both).")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var center = c.Vector(this, "Center", Center);
            var size = c.Vector(this, "Size", Size);
            float falloff = Mathf.Max(0f, c.Number(this, "Falloff", Falloff));
            var shape = Shape;
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                MeshSelection.Apply(mesh, i =>
                {
                    var d = mesh.Positions[i] - center;
                    // How far outside the shape the vertex is (0 or less: inside).
                    float outside;
                    if (shape == SelectRegionShape.Sphere)
                        outside = d.magnitude - Mathf.Abs(size.x);
                    else
                    {
                        var q = new Vector3(Mathf.Abs(d.x) - Mathf.Abs(size.x) * 0.5f, Mathf.Abs(d.y) - Mathf.Abs(size.y) * 0.5f, Mathf.Abs(d.z) - Mathf.Abs(size.z) * 0.5f);
                        outside = Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
                    }
                    if (outside <= 0f)
                        return 1f;
                    return falloff > 0f ? 1f - Mathf.Clamp01(outside / falloff) : 0f;
                }, mode);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Select Bounds"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects the vertices in a box given in the mesh's own bounds, 0 to 1 on each axis (Y 0.8 to 1: the top fifth, whatever its size), fading out over Falloff (0 to 1 of the bounds) beyond it. The same box Grab uses."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Min"", ""type"": ""Vector3""}, {""name"": ""Max"", ""type"": ""Vector3""}, {""name"": ""Falloff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select Bounds", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectBoundsNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Min", Editable = true)] public Vector3 Min = new(0f, 0.8f, 0f);
        [Input("Max", Editable = true)] public Vector3 Max = Vector3.one;
        [Tooltip("How far beyond the box the selection fades out, as a share of the bounds (0 to 1). 0 is a hard edge.")]
        [Input("Falloff", Editable = true)] public float Falloff;
        [Tooltip("How it combines with what's already selected: Replace, Add, Subtract or Intersect.")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var min = c.Vector(this, "Min", Min);
            var max = c.Vector(this, "Max", Max);
            float falloff = Mathf.Max(0f, c.Number(this, "Falloff", Falloff));
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                // Measured without the selection already there, so Mode can mix them.
                var plain = mesh.Clone();
                plain.Selection.Clear();
                var weights = MeshSelection.BoundsWeights(plain, min, max, falloff);
                MeshSelection.Apply(mesh, i => weights[i], mode);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Select Facing"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects the vertices whose normals face within Angle degrees of Direction (fading out over Falloff degrees more): the tops of things, the underside, the front of a face."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Direction"", ""type"": ""Vector3""}, {""name"": ""Angle"", ""type"": ""float""}, {""name"": ""Falloff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select Facing", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectFacingNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Direction", Editable = true)] public Vector3 Direction = Vector3.up;
        [Tooltip("How far from Direction a face can point and still be selected (degrees).")]
        [Input("Angle", Editable = true)] public float Angle = 30f;
        [Tooltip("How many degrees more it fades out over, for a soft edge.")]
        [Input("Falloff", Editable = true)] public float Falloff;
        [Tooltip("How it combines with what's already selected: Replace, Add, Subtract or Intersect.")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var direction = c.Vector(this, "Direction", Direction);
            direction = direction.sqrMagnitude > 1e-10f ? direction.normalized : Vector3.up;
            float angle = Mathf.Clamp(c.Number(this, "Angle", Angle), 0f, 180f);
            float falloff = Mathf.Max(0f, c.Number(this, "Falloff", Falloff));
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                MeshSelection.Apply(mesh, i =>
                {
                    float between = Mathf.Acos(Mathf.Clamp(Vector3.Dot(mesh.Normals[i].normalized, direction), -1f, 1f)) * Mathf.Rad2Deg;
                    if (between <= angle)
                        return 1f;
                    return falloff > 0f ? 1f - Mathf.Clamp01((between - angle) / falloff) : 0f;
                }, mode);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Select Slot"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects the vertices of material slot Slot's triangles: everything made in one material (the windows of a building, the trim of a sword)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Slot"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select Slot", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectSlotNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("Which material slot's faces to select: 0 the first material, 1 the second...")]
        [Input("Slot", Editable = true)] public int Slot;
        [Tooltip("How it combines with what's already selected: Replace, Add, Subtract or Intersect.")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int slotIndex = c.Integer(this, "Slot", Slot);
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                var inSlot = new float[mesh.VertexCount];
                if (slotIndex >= 0 && slotIndex < mesh.Slots.Count)
                    foreach (int v in mesh.Slots[slotIndex])
                        inSlot[v] = 1f;
                MeshSelection.Apply(mesh, i => inSlot[i], mode);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Select Inside"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects the vertices hidden inside other parts: where merged shapes overlap (a neck's top inside the head, a joint inside a limb), or inside Other if it's wired. Follow with Delete Selected to clear out the hidden faces. The parts should be closed (no holes)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Other"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select Inside", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectInsideNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Other", Editable = false)] [NonSerialized] public MeshData Other;
        [Tooltip("How it combines with what's already selected: Replace, Add, Subtract or Intersect.")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var other = c.Mesh(this, "Other");
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                var against = other.Get();
                var inside = MeshSelection.Inside(mesh, against.VertexCount > 0 ? against : null);
                MeshSelection.Apply(mesh, i => inside[i], mode);
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Select Near"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Selects the vertices within Distance of another part of the mesh (or of Other's vertices, if it's wired), fading out over Falloff: the places where parts meet, to Join, smooth or pull together."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Other"", ""type"": ""MeshData""}, {""name"": ""Distance"", ""type"": ""float""}, {""name"": ""Falloff"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Select Near", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshSelectNearNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Other", Editable = false)] [NonSerialized] public MeshData Other;
        [Input("Distance", Editable = true)] public float Distance = 0.05f;
        [Tooltip("How far beyond Distance the selection fades out (meters), for a soft edge.")]
        [Input("Falloff", Editable = true)] public float Falloff;
        [Tooltip("How it combines with what's already selected: Replace, Add, Subtract or Intersect.")]
        [Editable] public MeshSelection.Mode Mode = MeshSelection.Mode.Replace;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            var other = c.Mesh(this, "Other");
            float distance = Mathf.Max(0f, c.Number(this, "Distance", Distance));
            float falloff = Mathf.Max(0f, c.Number(this, "Falloff", Falloff));
            var mode = Mode;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                var against = other.Get();
                var near = MeshSelection.Near(mesh, distance, falloff, against.VertexCount > 0 ? against : null);
                MeshSelection.Apply(mesh, i => near[i], mode);
                return mesh;
            });
        }
    }

    public enum SelectionEdit { Invert, Grow, Shrink, Clear }

    [SchematicDoc(@"{""name"": ""Edit Selection"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Changes the selection: Invert (selected becomes unselected and the other way round), Grow or Shrink by Rings rings of neighbouring vertices, or Clear (nothing selected: the next nodes work on the whole mesh again)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Rings"", ""type"": ""int""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Edit Selection", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshEditSelectionNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("Invert: swap selected and unselected. Grow / Shrink: by Rings of neighbouring vertices. Clear: nothing selected (later nodes work on the whole mesh).")]
        [Editable] public SelectionEdit Edit = SelectionEdit.Invert;
        [Tooltip("How many rings of neighbouring vertices to grow or shrink by.")]
        [Input("Rings", Editable = true)] public int Rings = 1;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            int rings = Mathf.Clamp(c.Integer(this, "Rings", Rings), 0, 64);
            var edit = Edit;
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                switch (edit)
                {
                    case SelectionEdit.Invert: MeshSelection.Invert(mesh); break;
                    case SelectionEdit.Grow: MeshSelection.Grow(mesh, rings); break;
                    case SelectionEdit.Shrink: MeshSelection.Grow(mesh, -rings); break;
                    default: MeshSelection.Clear(mesh); break;
                }
                return mesh;
            });
        }
    }

    [SchematicDoc(@"{""name"": ""Delete Selected"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Removes the selected part of the mesh: triangles with any selected corner, or with All Corners only those with every corner selected (gentler: after Select Inside it leaves the cut edges just inside the neighbouring part, ready for Join). Afterwards nothing is selected."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Delete Selected", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshDeleteSelectedNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("Only remove triangles whose corners are all selected (gentler). Off: any triangle touching the selection goes.")]
        [Editable] public bool AllCorners = true;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            bool all = AllCorners;
            return new MeshOp(() => MeshSelection.DeleteSelected(input.Get(), all));
        }
    }

    [SchematicDoc(@"{""name"": ""Join"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Joins separate parts where they meet: each cut edge (left by Delete Selected) within Distance of another part snaps onto that part's surface, so the parts end exactly on each other with no gap and nothing poking through, and edges that land together weld. Only selected vertices move, if any are selected. Mode Weld instead pairs up matching rings of cut edges (a limb's open end and the hole cut for it) and welds them, so edge loops run straight through the joint. Normals are worked out again with Smooth Angle (-1: keep them)."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Distance"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Join", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshJoinNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Distance", Editable = true)] public float Distance = 0.05f;
        [Editable, Tooltip("Surface: cut edges snap onto the other part. Weld: matching rings of cut edges weld into one surface.")] public MeshSelection.JoinMode Mode = MeshSelection.JoinMode.Surface;
        [Editable, Tooltip("Normals are worked out again afterwards: edges sharper than this stay hard (-1: keep them).")] public float SmoothAngle = 50f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float distance = Mathf.Max(0f, c.Number(this, "Distance", Distance));
            float smooth = SmoothAngle;
            var mode = Mode;
            return new MeshOp(() => MeshSelection.Join(input.Get(), distance, smooth, mode));
        }
    }

    [SchematicDoc(@"{""name"": ""Merge By Distance"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Select"", ""description"": ""Vertices closer together than Distance become one point, and triangles that shrink to nothing go: closes hairline cracks and clears up slivers. Only selected vertices merge, if any are selected."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Distance"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}]}")]
    [Serializable]
    [Node(Name = "Merge By Distance", Path = "Select"), Tags(MeshNode.Tag)]
    public sealed class MeshMergeByDistanceNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Input("Distance", Editable = true)] public float Distance = 0.001f;
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float distance = Mathf.Max(0f, c.Number(this, "Distance", Distance));
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                MeshModifiers.MergeByDistance(mesh, distance);
                return mesh;
            });
        }
    }
}
