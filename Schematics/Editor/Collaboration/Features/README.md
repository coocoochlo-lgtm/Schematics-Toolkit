# Collaboration Features

Editor-only features built on the collaboration session (`Remedy.Schematics.Collaboration`, in `../Core`). Everything
here pauses while you're disconnected or in Play Mode (live edits to a game in Play Mode go through `CollabRuntime`).

| What | Where |
| --- | --- |
| Live scene editing | `SceneSync`, `SceneSnapshot`, `PropertyCodec`, `CollabObjectIds` |
| Placeholders for assets still on their way | `CollabPlaceholders`, `CollabPlaceholder`, `NewAssetTracker` |
| Teammates in the Scene view, selection locks | `ScenePresence` |
| Teammates in Schematic Graphs, graph sync | `GraphCollab` |
| Team window and chat (Window > Schematics > Team) | `TeamWindow`, `ChatService`, `CollabNavigation` |
| Connection indicator (Scene view overlay "Collaboration") | `CollabStatusOverlay` |
| Settings (Project Settings > Schematics > Collaboration Features) | `CollabFeaturesSettings` |
| Playtest builds: build info and baked object ids | `CollabBuildSteps` (runtime side: `Schematics/Runtime/Collaboration`) |
| Live edits in the editor's Play Mode | `CollabPlayModeIds` |

## What people see

- **Each other in the Scene view.** Anyone who has one of your open scenes open (a blockout scene or one of its art
  scenes) appears where their Scene view camera is: the avatar prefab from settings (tinted in their colour), or a
  head-and-camera-frustum placeholder, with their name. A dotted line runs to everything they have selected, with a box
  around it, in their colour.
- **Selection locks.** You can't select what someone else has selected: it's deselected with "Selected by Name", and
  it can't be clicked in the Scene view until they let go. If two people grab the same thing at once, whoever
  selected it first keeps it.
- **Live editing.** Moving, editing, adding, deleting, re-parenting and adding/removing components show up for
  everyone straight away. Only scenes saved as assets take part.
- **Placeholders.** Place an asset you just made, and teammates see a translucent box of the same size in your colour,
  labelled with its name, until git delivers the asset; then it turns into the real thing.
- **Schematic Graphs.** Teammates' cursors and selected nodes show in your graph window; nodes they've selected can't
  be selected; edits arrive a second or two after they're made.
- **Team window.** Everyone's avatar, name, host crown, ping, what they're working on and whether they're active.
  Double-click someone to jump to them (their scene(s) from their camera, or their graph at their cursor). Chat at
  the bottom (Enter to send, unread count in the tab, optional sound).

## Editors and playtesters

Everyone in the room is an **editor** (a Unity editor in Edit Mode) or a **playtester** (a build of the game, or an
editor in Play Mode). The Team window shows it as a badge (EDITOR, PLAYTESTER, PLAYTESTER · BUILD) and counts who's
playtesting; `CollabPeer.Role` / `CollabPeer.IsBuild` give it to code. A build's player shows in the Scene view like a
teammate (an avatar where its game camera is, "Name (playing)"), and its scene in the Team window.

**Live edits reach the running game.** The scene edits editors send each other on the `scene` channel also reach
playtesters. A playtester who ticks *Receive live edits* (the overlay in the corner of a build, or Team window menu >
Receive Live Edits In Play Mode) sees them applied to the running game by `CollabLiveEdits`, best effort:

- GameObject active state, name and layer; Transform local position, rotation (validated, normalized) and scale;
  `enabled` of Behaviours, Renderers and Colliders;
- serialized fields of components by reflection (public or `[SerializeField]`, nested structs, arrays and lists):
  numbers, bools, strings, enums, vectors, quaternions, colours, rects; built-in components' `m_X` as their C# property
  `x` where one exists.
- Not mirrored: creating, deleting or re-parenting objects (the new local transform is applied), object references,
  objects created after the build. Edits only received while the toggle is on.

Builds can't compute GlobalObjectIds, so `CollabBuildSteps` adds one hidden object with a `CollabSceneIds` map
(`"g:" + GlobalObjectId` → object) to each scene copy that goes into the build. In Play Mode, `CollabPlayModeIds`
records each known id's hierarchy path just before Play Mode starts and finds the play-mode copy by it.
`CollabRuntime.GetObjectId(GameObject)` / `CollabRuntime.FindObject(string)` expose the mapping to game code.

## How scene sync works

**Ids.** Saved objects are identified by `"g:" + GlobalObjectId` (everyone opened the same committed scene, so
everyone computes the same id). Objects created during the session get `"n:" + creatorPeerId + random` from their
creator, sent along with them. Ids are remembered per object in a hidden never-saved registry that survives script
reloads, and never recomputed, so a local save doesn't change an id everyone knows. When the host commits, its
`n:` objects get real GlobalObjectIds and the host broadcasts aliases (`n:… = g:…`), also kept in the shared log for
late joiners. Components are addressed as object id + `Type:index`. Nothing is added to scene files.

**Capture.** `Undo.postprocessModifications` gives property-level changes (sent as just the changed property paths).
`ObjectChangeEvents` gives creations, deletions (registered objects that no longer exist), parent/order changes,
component additions/removals, and property changes made without Undo (those send the whole component). Changes are
batched 20 times a second.

**Values.** `PropertyCodec` writes numbers, bools, strings, enums, vectors, quaternions, colours, rects, bounds,
layer masks, hashes and arrays (size then elements) as text, and object references portably: assets as
`a:guid:localFileId`, scene objects as `s:id`, scene components as `s:id#Type:index`. Components with
`[SerializeReference]` data also carry their `EditorJsonUtility` JSON (applied first; property values after fix the
references, which JSON stores as local instance ids).

**Applying.** Remote changes are applied with a guard so they aren't sent back. By default they don't go into Undo
(Unity has one undo stack, so Ctrl+Z would otherwise undo teammates' work); turn on *Record Remote Edits In Undo* to
get each remote change as its own named Undo group. Messages about objects that don't exist yet wait until they do.

**Conflicts.** Selection locks, then last writer wins per property.

**Late joiners and commits.** The host keeps every scene message since the scene's last commit in the shared store
(`scene:<path>`, compacted so only the latest value of each property stays). Joiners open the committed scene and
replay it; message ids make replays harmless. The host saves and commits edited scenes (`CollabGit.CommitScene`, with
everyone who edited as co-authors) every *Commit Interval Minutes*, when a scene closes, when the session ends and
when the editor quits, then starts a new log. A scene others edit that the host doesn't have open is opened
additively in the background for the commit. Scenes with placeholders wait (the missing objects would be lost).

## Message formats

All JSON via `JsonUtility` (classes in `CollabMessages.cs`).

| Channel / key | Format |
| --- | --- |
| `scene` | `SceneMsg { op, scene, mid, from, id, comp, props[{p,v}], data, nodes[], parentId, sibling, aliases[] }`. `op`: `props` (property values of a GameObject (`comp` = "") or component), `comp` (whole component: `data`), `create` / `structure` (hierarchy snapshot: `nodes`, flattened, index 0 = root, with `parentId`/`sibling`), `destroy`, `parent` (new parent, sibling index and local transform), `committed` (host: aliases after a save). |
| `graph` | `GraphMsg { op:"changed", guid, path, from, rev, enc:"text"\|"base64", data }`: the saved graph asset file. |
| `chat` | `ChatMsg { id, from, name, color:"#RRGGBB", text, t:unixMs }` |
| presence `camera` | `CameraPresence { scene, scenes[], pivot, rotation, size, ortho, in2D, position }` (builds send their game camera; rotations are validated where parsed) |
| presence `selection` | `SelectionPresence { scene, ids[], t }` |
| presence `location` | `LocationPresence { kind:"scene"\|"graph"\|"none", path, name, scenes[], status:"Active"\|"Idle"\|"Play Mode" }` |
| presence `graph` | `GraphPresence { path, guid, cursor, cursorInside, selected[], t, viewPosition, viewScale }` |
| shared `scene:<path>` | `SceneLog { baseId, committedAt, entries[SceneMsg], aliases[] }` |
| shared `graph:<guid>` | the latest `GraphMsg` for that graph |
| shared `chat` | `ChatLog { messages[] }` (last 200) |

A snapshot node: `SnapshotNode { id, parent, sibling, name, tag, layer, active, staticFlags, prefabGuid, prefabPath,
boundsCenter, boundsSize, comps[ComponentData { type, key, json, props[] }] }`. Prefab instances carry the prefab GUID
and only overridden properties; objects inside an instance are matched by `prefabPath`.

## Limitations (v1)

- Changes made by scripts without Undo and without `ObjectChangeEvents` (rare) aren't seen.
- Not synced: removed-from-prefab components/objects, AnimationCurve/Gradient values, references to unsaved
  non-scene objects (e.g. a generated Mesh), terrain heightmaps (asset data, travels through git).
- Unsaved local changes from before joining stay local (a warning is logged).
- When the host's commit is pulled while you have the scene open with changes, Unity may ask whether to reload; your
  in-memory scene already matches, so keeping it is fine.
- Objects added under a placeholder are parented to a never-saved object until the asset arrives; keep commits for
  after placeholders resolve (the host waits automatically).
- Graph sync replaces the whole graph (last writer wins at graph level); local unsaved edits win over an incoming
  version and are sent shortly after. New graph assets arrive through git.
- Selection-lock tie-breaks compare each editor's clock; with large clock differences the wrong person may keep a
  contested object (both can't keep it).
