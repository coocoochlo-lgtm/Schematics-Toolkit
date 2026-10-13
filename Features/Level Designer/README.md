# Level Designer

A workflow for building levels in two passes: a quick **blockout** (grey-box shapes with exact sizes in meters,
rulers, and your character's real jump and run distances), then one or more **art designs** built on top of it,
with the blockout shown underneath as an X-ray so art lines up with the gameplay space.

Open it from **Window → Remedy → Level Designer** (also under **Tools → Remedy**).

## The window

- **Levels tab**
  - **Left: blockouts.** **+** makes a new blockout scene (camera, light, a 20 x 20 m floor and the blockout
    marker) and opens it. **Add…** adds an existing scene. **−** removes the selected one from the list (optionally
    moving its scenes to the trash). Double-click opens it. The blockout open right now is marked with ●.
  - **Right: art designs** of the selected blockout, as cards: rename, notes, **Open** (opens the art scene with the
    blockout loaded underneath, the art scene active), **Duplicate**, **✕** (remove). The **+** card (or
    **+ Add Art Design** with a name typed in) makes a new, empty art design scene next to the blockout scene and
    opens it.
- **Characters tab**: movement numbers for character prefabs (see *Character metrics*). **Find in Project** lists
  every prefab with the Physics Based Character Controller; drop one in the field to add it by hand. **Use for
  Rulers** picks the character the blockout's Jump Arc and Run rulers use. **Export JSON / CSV** saves everyone's
  numbers for animators and spreadsheets.
- **Header**: **Blockout Overlay** (the X-ray toggle) and its opacity, and the **Snap** step (0.125 / 0.25 / 0.5 /
  1 / 2 m, or off) used when placing and sizing shapes.

The list lives in a `LevelDesignProject` asset (made in `Assets/Level Design/` the first time). Scenes are remembered
by GUID, so moving or renaming them in the Project window is fine.

## Blockouts

A blockout is any scene with a **Blockout Scene** marker. While one is the active scene, the Scene view shows the
**Blockout Tools** toolbar:

| Button | Left click | Right click |
| --- | --- | --- |
| **Primitive** | places the last primitive used | a searchable list: Box, Floor, Wall, Platform, Ramp, Stairs, Pillar, Sphere, Wedge, Arch, Quarter Pipe, Hazard, Water |
| **Rulers** | places the last ruler used | Distance, Height, Jump Arc, Run 1 s, Run 3 s |
| **Schematics** | places the last Schematic used | every prefab tagged **Level Design** (Int Tags), plus Schematic Templates marked for level design |

The list opens across the toolbar (below it when the toolbar is horizontal, beside it when it's vertical). Picking an
item switches to the **Blockout Place** tool:

- **Primitives**: click to drop one at its default size, or **drag out its footprint** on any surface (floor, wall,
  ceiling: the shape grows out of the surface). The drag direction is the shape's forward, so ramps and stairs rise
  the way you dragged. Let go, move the mouse to set the **height**, click. Hold **Shift** when letting go to keep
  the default height. Sizes and positions snap to the Snap step, and live sizes are shown while dragging.
- **Rulers**: Distance drags point to point; Height drags up from a surface; Jump Arc and Run drag the direction
  from the take-off point.
- **Schematics**: click to place, or drag to face it along the drag.
- **Esc** or right-click cancels.

**Ctrl + drag** anywhere in a blockout's Scene view (any tool) measures with a temporary ruler: total distance plus
its horizontal and vertical parts. Hold **Shift** as you let go to keep it as a Distance ruler. (It can be switched
off in the toolbar's Snap menu, since Ctrl + drag also means "snap" on Unity's move handle.)

### Blockout shapes

**Blockout Shape** (`BlockoutShape`) is a procedural Box, Ramp, Stairs, Cylinder, Sphere, Wedge, Arch or Quarter
Pipe with its **Size in meters** (width x height x length; the pivot is the centre of the bottom face). Select one
to resize it with the face handles (snapped; the opposite face stays put). Prefer Size over scaling (**Apply Scale to
Size** fixes a scaled one).

- **Category** colours it: Floor, Wall, Platform, Hazard, Interactive, Water, Decoration, or a Custom colour.
- The **blockout shader** (`Remedy/Level Designer/Blockout`, URP) draws a world-aligned 1 m grid with 0.25 m
  subdivisions, outlines every face, and prints the shape's size on its faces ("4x0.5x8m"), all without textures.
- **Collision**: a static Box3D mesh shape, so characters on the toolkit's physics can play the blockout at once.

### Rulers

Rulers (`BlockoutRuler`) are kept under a `[Rulers]` object, tagged EditorOnly (builds leave them out).

- **Distance** and **Height**: select one to drag its end.
- **Jump Arc** (a *locked* ruler): the active character's running jump from the ruler's position along its forward
  axis. Move or rotate it like any object: it turns **green** when the arc lands on top of a surface and **red** when
  it hits a wall or falls past *Max Drop*. Labels show the jump height, the distance on flat ground, and where it
  landed. *With Hover* adds a full hover for characters that have one.
- **Run Distance**: how far the character runs in *Seconds*, ticked every second (acceleration included).

Locked rulers keep a copy of the character's numbers, so they stay as made; re-place them after retuning.

## Art designs

An art design is a scene with an **Art Design Scene** marker remembering its blockout. Open it from the window and
the blockout comes along underneath. While the art design is the active scene:

- The blockout's normal rendering is hidden in the Scene view and it's drawn as an **X-ray**
  (`Remedy/Level Designer/Blockout X-Ray`): see-through, outlined, on top of the art and fainter where art covers it.
- Blockout objects can't be clicked, so selection lands on the art.
- The **Art Design** panel in the Scene view (and the window's header) toggles the overlay, sets its opacity and can
  tint shapes by category. Turning the overlay off hides the blockout completely. If the art scene was opened on its
  own, the panel offers **Load Blockout Underneath**.

Only the Scene view is affected; the Game view never shows the X-ray. In Play Mode the marker's **In Play Mode**
setting decides whether the blockout comes along (through the Level Loader, so it must be in the Build Profile for
builds): *None*, *Collision Only* (its shapes don't render: handy while the art has no collision yet), or *Visible*.

## Character metrics

`CharacterMetricsAnalyzer` reads a prefab's **Physics Based Character Controller** Properties (and the **Hover
Controller**'s if present) and predicts:

| Number | How |
| --- | --- |
| Run speed, time and distance to top speed, stopping distance | Move Speed, Acceleration, Deceleration (per second) |
| Jump height (feet) | Jump Height − Ride Height (the body rides Ride Height above the ground and rises to Jump Height) |
| Rise time / hang time | rise at Jump Speed, then hold for Hang Time Duration |
| Fall acceleration | gravity + Fall Speed / fixed time step (the controller adds Fall Speed every physics step) |
| Air time, jump distance | a running jump landing at its starting height |
| Hover time, speed, distance | Hover Controller Properties |

Gravity is the scene's Box3D World gravity (9.81 m/s² when there's none). These are predictions from the tuning;
springs and collisions make the real motion slightly softer. The controller has one move speed (no separate walk).

## Files

- Runtime (`Remedy.LevelDesigner`): `LevelDesignProject`, `BlockoutScene`, `ArtDesignScene`, `BlockoutShape`,
  `BlockoutMeshBuilder`, `BlockoutRuler`, `CharacterMetrics`, `Shaders/`.
- Editor (`Remedy.LevelDesigner.Editor`): the window, contexts and settings, the toolbar overlay and flyout, the place
  tool, ruler drawing, the X-ray, the shape inspector, the metrics analyzer, the catalog.
- Generated in your project: `Assets/Level Design/LevelDesignProject.asset`, `Assets/Level Design/Materials/Blockout.mat`.
