# Meshes: Mesh Graphs and Auto LOD

**Mesh Graphs** build 3D models from nodes, like a shader graph for geometry. Create one with
Create → Schematics → Mesh Graph, or from the Project Manager's Meshes page.

- **Shapes:** Box, Plane, Sphere, Cylinder (a top radius of 0 makes a cone), Torus, and Lathe (a profile curve spun around Y).
- **Modify:** Transform, Merge, Array, Radial Array, Mirror, Scatter, Shading (hard or smooth edges), Simplify, Weld.
- **Deform:** Twist, Bend, Taper, Noise.
- **Surface:** Material Slot, UV Project (box or planar), UV Transform, Vertex Color.
- **Model:** Boolean (union, subtract and intersect for closed meshes), Subdivide (smooth Loop subdivision, or
  linear), Spherize (an Amount from 0 to 1), Loop Cut (rings of edges across an axis), and Extrude. Extrude pulls out
  faces picked by facing, all of them, or a material slot, builds walls joining them to where they were, and can
  shrink the moved faces with Cap Scale.
- **Points (loops over space):** Point Grid, Point Line, Point Circle and Points On Mesh make points.
  - **Point Function** reshapes them with formulas per point: position, rotation, scale, and a Keep filter.
  - **Point Jitter** randomises them, and **Merge Points** combines sets.
  - **Stamp** puts a mesh on every point.
  - **Vertex Function** moves a mesh's own vertices by a formula.
- **Formulas** can use `+ - * / % ^`, comparisons, `&& || !`, `a ? b : c`, `.x .y .z`, sin cos tan asin acos atan
  atan2 sqrt abs floor ceil round frac sign min max clamp lerp pow exp log step smoothstep radians degrees vec length
  normalize dot cross distance noise fbm rand, pi, tau, and the graph's Parameters by name. Point Function's variables
  are p, r, s, x, y, z, i, n and t. Vertex Function's are p, x, y, z, nrm, uv, i and n.
- **Values:** Parameter (a number each object sets for itself), Random (seeded), Math (a formula of A–D), Make Vector,
  and any Float or Integer value node.

**Macros** (right-click in a Mesh Graph → Macros): **Characters/Humanoid (mannequin)**. It builds a humanoid from
editable nodes (a shape and a placement per part, the right arm and leg mirrored), with Height, Build and Head Size
Parameters. Its body plan is `HumanoidRecipe`, which can also build the mesh directly from code.
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
