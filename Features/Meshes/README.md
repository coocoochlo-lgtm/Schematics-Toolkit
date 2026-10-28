# Meshes: Mesh Graphs and Auto LOD

**Mesh Graphs** build 3D models from nodes, like a shader graph for geometry. Create one with
Create → Schematics → Mesh Graph, or from the Project Manager's Meshes page.

- **Shapes:** Box, Plane, Sphere, Cylinder (a top radius of 0 makes a cone), Torus, and Lathe (a profile curve spun around Y).
- **Modify:** Transform, Merge, Array, Radial Array, Mirror, Scatter, Shading (hard or smooth edges), Simplify, Weld.
- **Deform:** Twist, Bend, Taper, Noise, Grab and Relax.
  - **Grab** works like a sculpting brush. It takes a box of the mesh given as 0 to 1 across its bounds on each axis
    (X 0.8 to 1 is the right-hand fifth), with a soft falloff. It scales, turns and moves that part about a pivot.
    **Mirror X** does the mirror image on the other side, so one Grab swings both arms.
  - **Relax** eases points towards their neighbours, which rounds a loop-cut cube's corners without shrinking it.
  - All of these, plus Transform, Spherize and Vertex Function, only move selected vertices when there's a selection
    (blended by how selected each is).
- **Surface:** Material Slot, UV Project (box or planar), UV Transform, Vertex Color.
- **Model:** Boolean (union, subtract and intersect for closed meshes; points closer than Merge Distance merge
  afterwards, 1 mm by default, which cleans slivers off the cut), Subdivide (Catmull-Clark for quads and box
  modelling, Loop for triangle meshes, or linear), Spherize (an Amount from 0 to 1), Loop Cut (clean rings of edges
  across an axis; square faces are cut as squares), and Extrude. Extrude pulls out faces picked by facing, all of
  them, a material slot, or the selection, builds walls joining them to where they were, and can shrink the moved
  faces with Cap Scale.
- **Select:** a weight from 0 to 1 on each vertex. Each Select node's Mode replaces, adds to, subtracts from or
  intersects the selection already there.
  - **Select** (a formula), **Select Region** (sphere or box, with a soft falloff), **Select Bounds** (Grab's 0 to 1
    box), **Select Facing**, **Select Slot**.
  - **Select Inside** picks what's hidden inside other parts, and **Select Near** what's close to them.
  - **Edit Selection** inverts, grows, shrinks or clears it.
  - **Delete Selected** removes it. **Join** closes the gaps between parts in one of two ways. **Surface** snaps cut
    edges onto the neighbouring part's surface. **Weld** pairs up matching rings of cut edges and welds them, so edge
    loops run straight through the joint. **Merge By Distance** welds points closer than a distance.
- **Points (loops over space):** Point Grid, Point Line, Point Circle and Points On Mesh make points.
  - **Point Function** reshapes them with formulas per point: position, rotation, scale, and a Keep filter.
  - **Point Jitter** randomises them, and **Merge Points** combines sets.
  - **Stamp** puts a mesh on every point.
  - **Vertex Function** moves a mesh's own vertices by a formula.
- **Formulas** can use `+ - * / % ^`, comparisons, `&& || !`, `a ? b : c`, `.x .y .z`, sin cos tan asin acos atan
  atan2 sqrt abs floor ceil round frac sign min max clamp lerp pow exp log step smoothstep radians degrees vec length
  normalize dot cross distance noise fbm rand, pi, tau, and the graph's Parameters by name. Point Function's variables
  are p, r, s, x, y, z, i, n and t. Vertex Function's are p, x, y, z, nrm, uv, i and n; Select's add sel.
- **Values:** Parameter (a number each object sets for itself), Random (seeded), Math (a formula of A–D), Make Vector,
  and any Float or Integer value node.

**Macros** (right-click in a Mesh Graph → Macros): **Characters/Humanoid**, a box-modelled body built from editable
nodes (`HumanoidRecipe` can also build it directly from code).
- Every part is a loop-cut cube shaped with Grab. Each limb's ends are rings of 8 corners, and the torso is cut so its
  neck, shoulder and leg patches are the same rings.
- The parts sit a couple of millimetres apart. Select Near picks the facing ends, Delete Selected opens them, and
  Join (Weld) welds each pair of rings, so the body is one closed skin with edge loops running through every joint.
- Then the head is rounded (a Spherize on just the head), the body is shaped with Grab (waist, chest, seat, sloped
  shoulders, jaw), relaxed and subdivided with Catmull-Clark.
- It's built in a T-pose, then one mirrored Grab swings the arms down at the shoulders.
- Parameters: Height, Build, Head Size and Arms Down.
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
