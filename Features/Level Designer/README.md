# Level Designer

A workflow for building levels in two passes: a quick **blockout** (grey-box shapes with exact sizes in meters,
rulers, and your character's real jump and run distances), then one or more **variants** built on top of it (art
passes, lighting setups, gameplay alternatives; they used to be called *art designs*), with the blockout shown
underneath as an X-ray so art lines up with the gameplay space. Variants flagged **Compose into build** are merged
with the blockout into one scene when the game is built.

Open it from **Window → Remedy → Level Designer** (also under **Tools → Remedy**). It's a normal dockable window
(UI Toolkit): drag its tab next to the Inspector, the Scene view or anywhere else.

## The window

- **Levels tab**
  - **Left: blockouts**, each with its thumbnail. Type a level name and press **+** (or Enter) to make a new
    blockout scene (camera, light, a 20 x 20 m floor and the blockout marker) in the levels folder and open it: no
    save dialog. **Add…** adds an existing scene. **−** removes the selected one from the list (optionally moving
    its scenes to the trash). Double-click opens it. The blockout open right now is marked with ●. **New Folder**
    (or right-click) makes a folder in the list: drag blockouts (or other folders) onto it, double-click its name to
    rename it, and new blockouts go in the selected folder. These folders only group the list (they're kept in the
    `LevelDesignProject` asset); the scenes stay in their level folders on disk.
  - **Right: variants** of the selected blockout, as cards with thumbnails: rename, notes, **Compose into build**,
    **Open** (opens the variant scene with the blockout loaded underneath, the variant active), **Duplicate**, **✕**
    (remove). The **+** card (or **+ Add Variant** with a name typed in) makes a new, empty variant scene in the
    blockout's **Variants** folder and opens it. **▶ Test Composed Scene…** merges the blockout with the variants
    you tick (the flagged ones to start with) and enters Play Mode (see *Composing variants*). **Blockout when
    composed** picks whether the blockout comes along visible, collision only, or not at all.
- **Reference character** (in the Scene view, the blockout toolbar's **Character** button): the Schematic prefab
  with the Physics Based Character Controller that the Jump Arc and Run rulers are measured from. Drop a character
  prefab on the button, or click it to pick one (**Find ▾** lists every character in the project), see its movement
  numbers (see *Character metrics*), place **Jump / Run Ruler Here**, and **Export JSON / CSV** for animators and
  spreadsheets.
- **Palette tab**: the project's named blockout colours (see *Colours and the paint bucket*): recolour, rename,
  reorder (▲ ▼), remove, **+ Add Colour**, **Reset to Defaults**, and per colour **Use** / **Paint Selected**.
- **Header**: **Blockout Overlay** (the X-ray toggle) and its opacity, the **Snap** step (0.125 / 0.25 / 0.5 /
  1 / 2 m, or off) used when placing and sizing shapes, and the **Levels** menu (below, which also holds **Compose
  Flagged Variants into Builds**, on by default).

The list lives in a `LevelDesignProject` asset (made in `Assets/Level Design/` the first time). Scenes are remembered
by GUID, so moving or renaming them in the Project window is fine.

### Levels folder

Every scene the Level Designer makes goes in one folder, set once for the whole team in the `LevelDesignProject`
asset (**Levels → Change Levels Folder…**, default `Assets/Levels`):

```
Assets/Levels/
  Canyon/
    Canyon_Blockout.unity
    Variants/
      Canyon_Desert.unity
      Canyon_Night.unity
  Thumbnails~/<scene GUID>.png
```

One folder per level keeps its blockout and every variant together; the variants sit in a `Variants` folder next
to the blockout, and the level-name prefix keeps scene names unique in searches and the Build Profile.
**Levels → Move Existing Scenes into the Levels Folder…** moves scenes made before (or elsewhere) into this layout
with `AssetDatabase.MoveAsset` (GUIDs and references are kept), after showing what will move. It never overwrites
and leaves the old folders alone (except old `Art` folders that end up empty, which are removed). Variants remember
their blockout by GUID; the path they use to load it in Play Mode is refreshed when the variant scene is opened and
whenever a scene is processed for a build or Play Mode.

**From "Art" to "Variants"**: projects made before the rename keep their variant scenes in `<Level>/Art/`. They
keep working where they are; the Levels tab shows a banner offering **Review Move…** (the same preview-then-move
dialog, GUIDs kept) or **Dismiss**. Nothing moves without that confirmation. The project asset keeps its data (the
list was renamed with `FormerlySerializedAs`).

### Thumbnails

Saving a blockout or a variant while it's the active scene renders the Scene view's camera into a 256 x 144
thumbnail, so each level shows the view of the last person who saved it. They're stored as
`<Levels folder>/Thumbnails~/<scene GUID>.png`: Unity ignores folders ending in `~` (nothing to import) but git
commits them, so the whole team sees them. A variant's thumbnail leaves its blockout out. **Levels → Capture
Thumbnail of the Active Scene** takes one on demand.

## Blockouts

A blockout is any scene with a **Blockout Scene** marker. Cameras and lights in that scene are automatically removed
in Edit Mode; put presentation lighting and cameras in a variant instead. While a blockout is the active scene, the
Scene view shows the **Blockout Tools** toolbar:

| Button | Left click | Right click |
| --- | --- | --- |
| **Primitive** | places the last primitive used | a searchable list: Box, Floor, Wall, Platform, Ramp, Stairs, Pillar, Sphere, Wedge, Arch, Quarter Pipe, Hazard, Water |
| **Rulers** | places the last ruler used | Distance, Jump Arc, Run 1 s, Run 3 s |
| **Colour swatch** | the palette menu: Auto, the palette, any colour, + Palette, Paint Selected, Paint Bucket, Edit Palette… | |
| **Paint** | the paint bucket tool (on/off) | |
| **Schematics** | places the last Schematic used | every prefab tagged **Level Design** (Int Tags), plus Schematic Templates marked for level design |

The list opens across the toolbar (below it when the toolbar is horizontal, beside it when it's vertical). Picking an
item switches to the **Blockout Place** tool:

- **Primitives**: click to drop one at its default size, or **drag out its footprint** on any surface (floor, wall,
  ceiling: the shape grows out of the surface). The drag direction is the shape's forward, so ramps and stairs rise
  the way you dragged. Let go, move the mouse to set the **height**, click. Hold **Shift** when letting go to keep
  the default height. Sizes and positions snap to the Snap step, and live sizes are shown while dragging. While
  this tool is active, other shapes' resize handles (and ruler end handles) are switched off, so a click always
  starts a new shape instead of grabbing the one placed before.
- **Rulers**: Distance drags point to point (hold **Shift** to measure straight out of the surface it started on:
  that's how heights and clearances are measured, there is no separate Height ruler any more); Jump Arc and Run drag
  the direction from the take-off point.
- **Schematics**: click to place, or drag to face it along the drag.
- **Esc** or right-click cancels.

**Ctrl + drag** anywhere in a blockout's Scene view (any tool) measures with a temporary ruler: total distance plus
its horizontal and vertical parts. Two ways to place its end, both handy:

- **Plain**: the end sits on whatever surface is under the mouse (wall to wall, floor to ledge).
- **Hold Shift**: the end goes **straight out of the surface the ruler started on** (perpendicular to it), as far
  as the mouse: clearance above a floor, depth out from a wall, headroom under a ceiling. Pressing or letting go of
  Shift mid-drag switches mode.

Hold **Alt** as you let go to keep it as a Distance ruler (Shift used to do this, but now picks the perpendicular
mode), or turn on **Keep Ctrl + Drag Rulers** in the toolbar's Snap menu to keep every one. (The Ctrl ruler can be
switched off in the same menu, since Ctrl + drag also means "snap" on Unity's move handle.)

### Colours and the paint bucket

The **colour swatch** on the blockout toolbar sets the colour of whatever you place next: new shapes become
*Custom* with that colour, new rulers (and kept Ctrl rulers) are drawn in it. **Auto** (the default) keeps the old
behaviour: shapes use their category colour, rulers their usual yellow / blue.

Click the swatch for the **palette**: named colours stored in the `LevelDesignProject` asset, so the whole team
shares one colour language ("Climbable", "Enemy zone", "Secret"…). Pick one, choose any colour, or name the current
colour and **+ Palette** it. Edit the palette (rename, reorder, remove) in the window's **Palette** tab.

The **Paint** bucket (toolbar button, or *Paint Bucket Tool* in the swatch menu): click a shape or a ruler in the
Scene view to give it the current colour (undoable; Esc ends it). For many at once select them (parents count for
their children) and use **Paint Selected** (swatch menu, Snap menu or the Palette tab). Painting a shape sets its
Category to Custom with that colour; painting a Jump Arc ruler colours its take-off ring (the arc keeps its green /
red verdict).

### Blockout shapes

**Blockout Shape** (`BlockoutShape`) is a procedural Box, Ramp, Stairs, Cylinder, Sphere, Wedge, Arch or Quarter
Pipe with its **Size in meters** (width x height x length; the pivot is the centre of the bottom face). Select one
to resize it with the face handles (snapped; the opposite face stays put). Prefer Size over scaling (**Apply Scale to
Size** fixes a scaled one).

- **Category** colours it: Floor, Wall, Platform, Hazard, Interactive, Water, Decoration, or a Custom colour.
- The **blockout shader** (`Remedy/Level Designer/Blockout`, URP) draws a 1 m grid with 0.25 m subdivisions
  starting at each face's bottom-left corner, outlines every face, and puts a small label in every 1 m square saying
  which meter of the face it is, counted from that corner: **x3 y2** is the third meter across (along the shape's X)
  and the second up (along Y). The squares along the far edges therefore read the face's full size. Slanted
  directions (a ramp's slope) show the number without a letter ("x2,4"). The total size is in the box above a
  selected shape. All without textures. *World-Aligned Grid* on the material switches back to a world grid; *Show
  Dimensions* on a shape turns its cell labels off.
- **Collision**: a Box3D mesh shape on its own static body, so characters on the toolkit's physics can play the
  blockout at once (even when the blockout is parented under another body).

### Rulers

Rulers (`BlockoutRuler`) are kept under a `[Rulers]` object, tagged EditorOnly (builds leave them out).

- **Distance**: select one to drag its end. Its end discs face along the ruler, like the caps of a tape measure.
  (Height rulers were retired: old ones load as Distance rulers, which measure the same thing.)
- **Jump Arc** (a *locked* ruler): the active character's running jump from the ruler's position along its forward
  axis. Move or rotate it like any object: it turns **green** when the arc lands on top of a surface and **red** when
  it hits a wall or falls past *Max Drop*. Labels show the jump height, the distance on flat ground, and where it
  landed. *With Hover* adds a full hover for characters that have one.
- **Run Distance**: how far the character runs in *Seconds*, ticked every second (acceleration included).

Locked rulers keep a copy of the character's numbers, so they stay as made; re-place them after retuning.

## Variants

A variant is a scene with a **Variant Scene** marker remembering its blockout. Open it from the window and the
blockout comes along underneath. While the variant is the active scene:

- The blockout's normal rendering is hidden in the Scene view and it's drawn as an **X-ray**
  (`Remedy/Level Designer/Blockout X-Ray`): see-through, outlined, on top of the art and fainter where art covers it.
- Blockout objects can't be clicked, so selection lands on the art.
- The **Variant** panel in the Scene view (and the window's header) toggles the overlay, sets its opacity and can
  tint shapes by category. Turning the overlay off hides the blockout completely. If the variant scene was opened on
  its own, the panel offers **Load Blockout Underneath**.

Only the Scene view is affected; the Game view never shows the X-ray. In Play Mode the marker's **In Play Mode**
setting decides whether the blockout comes along (through the Level Loader, so it must be in the Build Profile for
builds): *None*, *Collision Only* (its shapes don't render: handy while the art has no collision yet), or *Visible*.

The markers now each have their own script file (`BlockoutScene.cs`, `VariantScene.cs`): Unity only saves a
MonoBehaviour whose file carries its name, so markers saved from the old shared `LevelDesignerScenes.cs` may show
as *missing script*. Opening the scene from the window puts the marker back on the same `[Blockout]` /
`[Art Design]` object (the missing-script entry is removed).

### Composing variants

Tick **Compose into build** on the variants that make up the finished level. When the game is built (Build
Profiles / Build Settings build button), every blockout in the build's scene list whose level has flagged variants
is replaced, for that build only, by a **composed scene**: the first flagged variant is the base (its lighting,
skybox and fog are used, since that's where the art's lighting is made), the blockout and the other flagged variants
are merged into it (`SceneManager.MergeScenes`), and the Variant / Blockout markers are removed. It's written to
`Assets/LevelDesign.Generated/<Level>/<blockout scene name>.unity` (same scene name and build index as the blockout,
so code that loads the level still finds it) and that folder is deleted after the build. Your scenes and Build
Profile aren't changed; open scenes with unsaved changes are offered for saving first.

- **Blockout when composed**: *Visible*, *Collision Only* (the shapes' renderers are switched off in the composed
  copy) or *Leave Out*.
- The scene list is swapped through `BuildPlayerWindow.RegisterBuildPlayerHandler` (the build window's own hook):
  an `IPreprocessBuildWithReport` runs too late to change the scene list, and scenes can't be opened and merged while
  a build processes them. **Scripted / CI builds** call
  `Remedy.LevelDesigner.Editor.LevelBuildComposition.BuildWithComposedLevels(options)` instead of
  `BuildPipeline.BuildPlayer`; a build that skipped composition logs a warning.
- **▶ Test Composed Scene…** composes the same way into `Assets/LevelDesign.Generated/Test/` and enters Play Mode.
  When Play Mode ends, the scenes you had open come back and the test scene is deleted. It merges (rather than just
  loading the scenes side by side) because that's exactly what the build gets.

## For other tools

`Remedy.LevelDesigner.Editor.LevelDesigner` (assembly `Remedy.LevelDesigner.Editor`):

- `bool OpenVariant(string scenePath)`: opens a variant like the window does (blockout first, the variant on top,
  active).
- `bool TryGetLevelOf(string scenePath, out string levelName, out bool isBlockout)`: which level a scene belongs to.

## Box3D primitives

**GameObject → 3D Object → Cube / Sphere / Capsule / Cylinder / Plane / Quad** make Box3D objects: Unity's mesh, no
PhysX collider, a static `Box3DBody` (none when made inside an existing body, so it joins that compound) and the
matching shape (Box, Sphere, Capsule; a convex Hull of the cylinder mesh; a Mesh shape for Plane and Quad, which are
always static). Unity won't let a second menu item take an existing path, so instead of new menu items this watches
what Unity's own items create (`ObjectFactory.componentWasAdded`) and swaps the collider right after, undoable.
**Project Settings → Remedy → Box3D Primitives** switches it off or makes the bodies dynamic.

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

- Runtime (`Remedy.LevelDesigner`): `LevelDesignProject` (levels, variants, palette), `BlockoutScene`, `VariantScene`, `BlockoutShape`,
  `BlockoutMeshBuilder`, `BlockoutRuler`, `CharacterMetrics`, `Shaders/`.
- Editor (`Remedy.LevelDesigner.Editor`): the window, contexts and settings, the toolbar overlay and flyout, the place
  tool, the paint bucket and colour menu (`BlockoutPaint`), ruler drawing, the X-ray, the shape inspector, the
  metrics analyzer, the catalog, `LevelThumbnails`, `VariantScenePathFixer`, `LevelComposer` (composing, builds,
  Test Composed Scene), the public `LevelDesigner` API, and `Primitives/` (Box3D 3D Object primitives).
- Generated in your project: `Assets/Level Design/LevelDesignProject.asset`, `Assets/Level Design/Materials/Blockout.mat`,
  the levels folder (`Assets/Levels/` by default) with its `Thumbnails~`.
