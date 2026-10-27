# Meshes: Mesh Graphs and Auto LOD

**Mesh Graphs** build 3D models from nodes, like a shader graph for geometry. Create one with
Create → Schematics → Mesh Graph, or from the Project Manager's Meshes page.

- **Shapes:** Box, Plane, Sphere, Cylinder (a top radius of 0 makes a cone), Torus, and Lathe (a profile curve spun around Y).
- **Modify:** Transform, Merge, Array, Radial Array, Mirror, Scatter, Shading (hard or smooth edges), Simplify, Weld.
- **Deform:** Twist, Bend, Taper, Noise.
- **Surface:** Material Slot, UV Project (box or planar), UV Transform, Vertex Color.
- **Values:** Parameter (a number each object sets for itself), Random (seeded), and any Float / Integer / Vector value node.
- **Output:** the model, plus its LODs: LOD1 and LOD2 at the share of triangles you pick, and when each one shows.

The graph window shows a live preview in its corner, rebuilt in the background a moment after each edit. It has
buttons for each LOD with their triangle counts, and **Bake**, which saves the meshes and a prefab with its LOD Group
next to the graph.

**Mesh Graph Renderer** (Add Component → Schematics → Meshes) shows a graph's model on an object:

- Its Parameters are set in the Inspector, or with Set Graph Parameters from a Schematic graph. Each change rebuilds
  the model.
- It sets up a LOD Group and, optionally, a Mesh Collider made from the simplest LOD.
- Objects with the same graph and Parameters share one mesh.
- **Batch** merges nearby renderers (32 m cells, same materials) into one mesh per LOD, in play mode.

Everything heavy happens on a background thread: building the model, simplifying the LODs, and merging batches. Only
the upload to Unity meshes happens on the main thread.

**Auto LOD** (Tools → Remedy → Meshes → Auto LOD, or right-click objects → Remedy → Generate LODs) gives any model
LODs: select prefabs or scene objects and press Generate. LOD meshes are saved as assets, shown as LOD1/LOD2
children, and set up in a LOD Group. From code: `await AutoLOD.Make(mesh)`.

The simplifier (`MeshSimplifier`) uses quadric edge collapse. It welds hard edges so faceted models simplify too,
keeps UV seams and open edges in place, and is pure math, so it's safe on any thread.
