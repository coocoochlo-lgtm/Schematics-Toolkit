# Timeline Graphs

Cutscenes (and any timed sequence) as their own Schematic Graph. Needs the Timeline package
(`com.unity.timeline`, built into Unity); without it this folder simply doesn't compile in.

## Making one

**Assets → Create → Schematics → Timeline Graph** makes the graph and its Timeline (`<name> Timeline.playable`,
with a **Schematic** track). Open the graph: the **Timeline** bar along the top opens the Timeline in Unity's
Timeline window. In Play Mode the bar also lists the objects playing it, with play/pause, stop, restart and scrubbing.

## Playing it

- **Its own object**: give a prefab's Schematic Object this graph, and tick **Play On Start** (or play it from the graph).
- **From any graph**: **Play Timeline** (pick the Timeline Graph; wire an object into **Of** to play it on that object).
  Each object gets its own player (a `[Timeline] name` child with a Playable Director), so many objects can play
  the same Timeline Graph at once, and one object can play several.
- Control nodes: **Play / Pause / Resume / Stop Timeline**, **Seek Timeline** (seconds, or an Event marker by
  name), **Set Timeline Speed**, **Bind Timeline Track** (point a track at a scene object, e.g. the camera or the
  player), **Timeline Info** (time, duration, progress, playing).
- Events inside the Timeline Graph: **On Timeline Started / Paused / Resumed / Finished** (Completed: reached the end
  vs. stopped) and **On Timeline Marker** (Listen For a name, or any).

## Markers and clips (on a Schematic track, or the Timeline's marker row)

- **Signal Marker**: sends a Signal (the graph's own, a Behavior's, or any asset) to the object playing the
  Timeline, a child of it, or everyone, with its parameters filled in.
- **Variable Marker**: sets a graph Variable or a Behavior Variable of the object playing it.
- **Event Marker**: fires **On Timeline Marker** in the graph; also a target for **Seek Timeline**.
- **Variable Clip**: blends a Variable from one value to another across the clip, along a curve.

Markers can be **Retroactive** (fire when the Timeline starts or jumps past them) and **Only Once**.

## Tracks and bindings

Schematic tracks bind themselves. Other tracks (Animation, Audio, Activation...) bind on the object playing the
Timeline: to the path listed in the graph's **Bindings**, else to a child named like the track, else to the object
itself; the component the track needs is found (or added) there. Scene objects: **Bind Timeline Track**.
