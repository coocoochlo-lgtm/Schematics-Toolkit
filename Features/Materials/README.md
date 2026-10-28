# Materials: Smart Materials

**Smart Materials** are materials built from nodes, like Substance, and baked to textures for one mesh. Without a
mesh they make a flat material that tiles. Create one with Create → Schematics → Smart Material, or from the Project
Manager's Materials page. A new one comes with a starter stack: blue paint, worn through to metal at the edges, with
grime in the creases.

## How it works

- **The Output's Mesh** is what the material is made for: a Mesh Graph, a Mesh, or a model (FBX or prefab). Its UVs
  lay the textures out. Every texel knows where it sits on the model, so:
  - noise is read in 3D and runs straight across UV seams;
  - the mesh bakes know the model's shape.
- **Wires carry two things.** Material wires carry a whole material: colour, metallic, roughness and height. Mask wires
  carry a grey map from 0 to 1. A mask input also takes a typed number, or a Parameter, which makes it the same
  everywhere.
- **The preview** sits in the graph window's corner. It shows the material on the model, or one map laid flat (2D).
  It remakes itself a moment after each edit, at the Output's Preview Resolution.
- **Bake** makes the material at the Output's Resolution and saves PNGs next to the graph, plus a material that uses
  them:
  - Base Color.
  - A Normal map, made from the height. Height Depth sets how deep the bumps are.
  - Roughness, metallic and occlusion, packed for your render pipeline: HDRP's Mask Map, or a Metallic map plus an
    Occlusion map for URP and Built-in. ORM (Unreal/glTF) and Separate are options too.
  - Padding spreads colour past the UV islands' edges, so seams don't show at a distance.

## Nodes

- **Generators:** Noise (with Stretch, for streaks), Cells (cells, distance, bumps, cracks), Scratches, Gradient
  (along an axis of the model), Facing (tops, one side).
- **Mesh:** Ambient Occlusion, Thickness, and Curvature. Curvature's Edges output follows corners that stick out;
  its Cavities output follows creases. These are baked once per mesh and kept.
- **Smart Masks:** Edge Wear (chipped edges), Grime (dirt in creases, dark places and splashed up from the bottom),
  and Dust (on tops). Each is broken up by noise and has an Amount you can wire a Parameter into.
- **Masks:** Levels, Blend Masks, Paint Mask.
- **Materials:**
  - Fill: one material, with a little natural variation.
  - Mask Fill: two materials picked by a mask.
  - Layer: Top over Base through a mask. It can height-blend (rust in the low spots first, wear off the high spots
    first) and turn individual channels on or off.
  - Adjust: hue, saturation, brightness, roughness, metallic and height.
  - Paint.

## Painting

Painting works like Substance Painter. Brush strokes go onto the model itself in 3D, so they run straight across UV
seams.

- **Paint** paints colour, roughness, metallic, height and a mask over the material coming in.
- **Paint Mask** paints a mask. Wire any mask into its In and you paint over it: painting adds, and erasing (Ctrl)
  carves away. That way a procedural mask can be touched up by hand.
- **Paint anything:** right-click any node.
  - **Paint Where This Applies** limits the node's effect to where you paint. A material node gets a Layer over what
    came into it; a mask node gets multiplied by a painted mask.
  - **Paint (input)** puts a Paint Mask on that mask input, over whatever was wired there.
- **The painter:** each Paint node's **Paint...** button opens its viewport.
  - Left-drag paints; Ctrl erases.
  - [ and ] (or Shift+wheel) change the brush size.
  - Mirror X paints both sides at once.
  - Z undoes a stroke; Shift+Z redoes it.
  - Right-drag turns the view, middle-drag pans, and F frames the model.
  - Strokes save themselves in the background a moment after you stop. Each node's painting goes in one small
    `.bytes` file in a "<graph> Paint" folder. Only the painted 32×32 tiles are stored, compressed, so a 1024
    painting is usually a few hundred KB. Unity imports it when the window closes (or on Save).
  - **Export PNGs** writes what's painted as textures too, for other apps. Older paintings saved as PNGs still load.
  - **Staying responsive:**
    - Brush colour shows on the model immediately.
    - The full material rebuilds in the background, coarse first (128, then 256, then the preview size) and
      sharpening when you stop.
    - Undo only remembers the texels each stroke touched.
