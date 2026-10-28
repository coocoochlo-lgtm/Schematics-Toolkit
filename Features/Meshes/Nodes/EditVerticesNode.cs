// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Meshes
{
    /// <summary>
    /// Hand edits in a Mesh Graph: vertices picked and moved in its editor (Edit... on the node), with proportional
    /// editing (the vertices around following with a falloff). The edits replay on whatever comes in, found by where
    /// the vertices were, so changing the nodes before it doesn't throw them away.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Edit Vertices"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Mesh/Deform"", ""description"": ""Move vertices by hand. Click Edit on the node to open its viewport: click vertices to pick them (Shift adds, Ctrl takes away, drag a box for many), then G to move, R to turn, S to scale (X, Y or Z while doing it locks to that axis; click or Enter to keep it, Esc or right-click to cancel), or just drag a picked vertex. Turn on Proportional (O) and the vertices around follow, less the further away they are, out to Radius (the mouse wheel changes it while moving), along the falloff curve; Connected Only keeps it to vertices joined to the picked ones (move a lip without the other). Each edit is kept, and can be changed or deleted in the list. Strength scales every edit (0 none, 1 as made): wire a Parameter in and the edits become a slider, like a shape key."", ""inputs"": [{""name"": ""In"", ""type"": ""MeshData""}, {""name"": ""Strength"", ""type"": ""float""}], ""outputs"": [{""name"": ""Mesh"", ""type"": ""MeshData""}], ""use_for"": [""fixing or tweaking a made shape by eye"", ""a hand-made shape change driven by a Parameter"", ""sculpting a silhouette with soft proportional moves""], ""notes"": ""Edits remember where their vertices were, not their numbers: if the nodes before it change the mesh a little, each edit picks the nearest vertices instead. A big change (a different shape) can move different vertices than you meant.""}")]
    [Serializable]
    [Node(Name = "Edit Vertices", Path = "Deform"), Tags(MeshNode.Tag)]
    public sealed class MeshEditVerticesNode : MeshNode
    {
        [Input("In", Editable = false)] [NonSerialized] public MeshData In;
        [Tooltip("How much of the edits: 0 none, 1 as made, 2 twice as far. Wire a Parameter in to make the edits a slider.")]
        [Input("Strength", Editable = true)] public float Strength = 1f;
        [Editable, Tooltip("Normals are worked out again afterwards: edges sharper than this stay hard (-1: keep the normals, turned with the edits).")]
        public float SmoothAngle = 60f;
        [SerializeField, HideInInspector] private List<VertexMove> _moves = new();
        [Output(MeshPort)] [NonSerialized] public MeshData Mesh;

        /// <summary>The edits, in the order they're done.</summary>
        public List<VertexMove> Moves => _moves ??= new();

        public override MeshOp Compile(MeshCompiler c)
        {
            var input = c.Mesh(this, "In");
            float strength = c.Number(this, "Strength", Strength);
            float smooth = SmoothAngle;
            // A copy: the editor may change the edits while the mesh is made in the background.
            var moves = Moves.ConvertAll(m => m?.Copy());
            return new MeshOp(() =>
            {
                var mesh = input.Copy();
                if (moves.Count == 0 || strength == 0f)
                    return mesh;
                MeshVertexEdits.Apply(mesh, moves, strength);
                if (smooth >= 0f)
                    mesh.RecalculateNormals(smooth);
                return mesh;
            });
        }
    }
}
