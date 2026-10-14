# Level Streaming

Two ways to keep big worlds smooth, both spread over several frames so nothing loads in one big hitch:

- **Chunks**: parts of a level that are *already in the scene*, shown and hidden as the player moves around.
  No loading at all, just switching on and off.
- **Additive levels**: whole levels (a **scene**, or a **level prefab**) *added on top of* what's loaded, and taken
  away again. Their content wakes a few objects per frame instead of all at once.

Both are driven by **Level Streaming Zones** (boxes: while the player is inside, their chunks show and their levels
load), or from code and graphs.

## Chunks

- **GameObject → Level Streaming → Level Chunk From Selection** wraps the selection in a chunk; everything under the
  chunk object is the chunk. Double-click its volume in the Scene view to edit it on its own.
- **Hide mode**: *Deactivate* (nothing runs, renders or collides) or *Renderers Only* (scripts and physics keep
  running; cheaper to show again).
- A chunk is hidden at start unless **Visible At Start** is on, or a zone already wants it.

## Additive levels

### Making a level

- **Scene levels**: open the level scene and add **GameObject → Level Streaming → Streamed Level** (one per scene).
  By default the whole scene streams in; **Streamed Level From Selection** wraps only the selected objects instead
  (*Children Only*). Put things that must exist on the very first frame (lighting, a spawn point) in **Wake
  Immediately**. The scene should be in the Build Profile's scene list for builds (in the editor any scene works).
- **Prefab levels**: any prefab. It's placed under a `[Level] Name` holder object in the active scene.

### Loading one

- **Zones**: list the level in a zone's **Levels**. It loads when a streaming target enters, and unloads after
  **Exit Delay** once the last target leaves, unless another zone still wants it.
- **Graphs**: **Load Level** (Scene name or path, or a Prefab; the nodes after it run once it's in, and it outputs
  **Progress**, **Level Name** and the prefab level's **Root**) and **Unload Level**. In Singleton Graphs,
  **On Level Start** fires when a level has finished streaming in and **On Level Exit** when it's gone, with the
  level's name.
- **Code**:

  ```csharp
  var handle = LevelLoader.Load("Forest");                    // returns at once; handle.Progress, handle.State
  await LevelLoader.LoadAsync(LevelReference.FromPrefab(cave)); // waits until it has streamed in
  await LevelLoader.UnloadAsync("Forest");
  LevelLoader.LevelLoaded += h => Debug.Log(h.Name);           // also LevelUnloaded, LevelProgress
  ```

  Use a `LevelReference` field in your own components to get the scene/prefab picker.

A level stays while anything wants it: code (or a Load Level node) that loaded it, or an occupied zone. Loading a
level that's already loaded gives back the same one. **Unload** lets go of the code's claim and removes the level
unless a zone still wants it (*Force* removes it regardless). Unloading a level that is still streaming in stops it.

## How the streaming works

An object whose parent is switched off has never woken (no Awake, no OnEnable). So a level arrives with its content
switched off, and wakes **top-down**: just before an object is switched on, its children are switched off, so
switching it on wakes that one object only; its children follow in later frames. Every object ends up exactly as it
was saved (children saved inactive are never touched). What's nearest to the player wakes first.

- **Schematic Objects wake whole**, together with their children: their Behaviors on child objects register the
  moment the root wakes. (A level built as *one* big Schematic Object therefore wakes in one go; split it up.)
  `LevelLoader.KeepSchematicObjectsWhole` turns this off.
- **Chunks inside a level** that will hide themselves at start keep their children asleep; a zone shows them later.
- **Prefab levels** are copied under a switched-off holder (with `InstantiateAsync`, spread over frames, when
  `LevelLoader.InstantiateInBackground` is on), so nothing in them wakes until it's their turn.
- **Scene levels** can't be loaded asleep by Unity, so the **Streamed Level**'s scene processor (an
  `IProcessSceneWithReport`) switches the content off *in the copy of the scene being built, or loaded in Play Mode*.
  The scene asset you edit never changes. If the processor didn't run (e.g. Play Mode entered without reloading the
  scene), the content just wakes all at once, as Unity normally does, and a note is logged once.
  A scene with a Streamed Level that isn't loaded through the loader (the game's first scene, `SceneManager`) streams
  itself in.
- **Unloading** switches off and destroys a few objects per frame (children first) before the scene is unloaded
  (`LevelLoader.SliceUnload`).

Scripts in streamed content should not assume their siblings or children are already awake in Awake/Start (same as
with any object that's spawned later). Look things up lazily, or put the object in **Wake Immediately**.

## Budgets

All streaming work (chunks, levels waking, levels being taken apart) shares **one budget per frame**:

- `StreamingBudget.MillisecondsPerFrame` (default 1.5 ms; `LevelStreamer.FrameBudgetMilliseconds` is the same
  setting). Only time spent inside streaming is counted.
- `StreamingBudget.MaxObjectsPerFrame` (default 200): a safety cap, because part of what waking costs (Start,
  the first render) lands later in the frame where the clock can't see it.

Work that couldn't get any budget for a few frames in a row does one object anyway, so nothing ever stalls.

## Networking

Objects with a **NetworkObject** (Netcode for GameObjects) can't be created by a client on its own, and in-scene
NetworkObjects need Netcode's scene management to line up on every peer. Load networked levels the Netcode way
(`NetworkManager.SceneManager.LoadScene(..., LoadSceneMode.Additive)`), not through the Level Loader. The loader and
the scene processor warn when a level they handle contains NetworkObjects.
