# Sticky Notes

World-space sticky notes for level design and playtesting. Testers stick notes on the world while they play.
Developers see the same notes in the Scene view, fix the problem, and resolve the note. Notes sync live through the
collaboration room. When the last editor leaves, they're committed to git.

There are three note types: **Default** (yellow, a remark), **Warning** (orange, something's off) and **Error** (red,
something's broken). Resolved notes turn muted green, and you can hide them.

## In game (Play Mode and builds)

Notes are hidden until someone turns the view on.

| Key / API | What |
| --- | --- |
| **F8** / `StickyNotesView.Toggle()`, `StickyNotesView.Enabled` | Show or hide notes. This also frees the mouse cursor while notes are shown. |
| **F9** / `StickyNotesView.AddNoteAtView()` | Start a new note where the camera is looking. |
| Click a note | It scales up (about 0.15 s) into the editor panel, laid out on the sticky-note paper: type, text, snapshot, Save, Resolve/Reopen, Delete (click twice), Cancel. Drag the panel by its top edge. Esc closes it. |
| Hover a note | Read it (also shows who's on it and the snapshot summary). |
| **Tasks** button | A small list of the level's open notes: To Do first, then In Progress with the assignee. Click one to open it. `StickyNotesView.ShowTaskList` from code. |

Settings live in code: `ToggleKey`, `AddKey`, `TargetCamera` (Camera.main by default), `ShowResolved`, `MaxDistance`
and `UnlockCursor`. `StickyNotesView.IsEditing` is true while the panel is open, so gameplay can ignore keys the tester
types. `Raycaster` decides where a new note goes. By default it casts a Box3D ray through `SchematicPhysics` and
skips hits closer than 0.5 m (usually the player's own body). If nothing is hit, the note goes 3 m ahead.
`ObjectIdProvider` gives a stable id for the object a note is stuck on. It's empty by default, so only the
object's name is stored.

The view is drawn with IMGUI. That needs no assets, theme, PanelSettings or input package, and works with both input
backends in any build. A hidden, never-saved object hosts the view. It's created when the game starts.

**Availability.** The view is always available in the editor and in development builds. In release builds it's only
available when the project opts in (see Builds below). Game code can override this with
`StickyNotesView.Available`.

## In the editor

- **Scene view.** Each note shows as a small piece of sticky-note paper, tinted for its type, with a type badge in the
  corner and a ring on the surface where it's stuck. A thin stripe in the author's collaboration colour runs along the
  bottom. Hover a note to read it. Click it and the paper scales up next to it (about 0.15 s) into the note editor,
  which is drawn as the paper with the type, text, task and snapshot info and the buttons inside it. It saves when
  you click away. Cancel or Esc throws the changes away. Right-click a note for Edit, Resolve, Take/Give Back, Show on
  Kanban Board, Type, Select Attached Object and Delete.
- **Adding notes.**
  - Click **Add Note** in the *Sticky Notes* Scene view overlay, then click where the note goes. Esc cancels.
  - **Shift+right-click** in the Scene view and choose **Add Sticky Note here**. A plain right-click still opens
    Unity's own menu.
  - **GameObject > Sticky Note > Add Sticky Note on Selection** puts a note on top of the selected object.
  - **Tools > Schematics > Sticky Notes > Add Note at Scene View Center** puts a note at the Scene view's pivot.

  A new note is only added once it has some text. It's attached to the object under the mouse (stored as a
  GlobalObjectId) and goes into that object's scene, or the active scene.
- **Sticky Notes window** (Window > Schematics > Sticky Notes, or **List** in the overlay):
  - Lists the open scenes' notes, or every scene's.
  - Filter by type, by resolved state, or by search text (matches text, author and object).
  - Double-click a note to frame it. If its scene isn't open, the window offers to open it additively.
  - Right-click a note for more actions.
  - The header shows whether you're live in a session and who saves the notes files. **Commit** saves and commits
    your notes files now.
- **Visibility.** Use the overlay toggles, the window's **Visible** button, or **Tools > Schematics > Sticky Notes >
  Show Notes in Scene View**. These are saved per person, in EditorPrefs.

## The paper image

Notes are drawn with the toolkit's sticky-note image, `media/StickyNote.png` (GUID `2058c76d2bbf14646a01acabc182ba10`).
It's tinted per type: Default stays yellow, Warning turns orange, Error turns red, and resolved notes turn muted green.
A small badge in the type's colour sits on the corner.

- **Editor.** The image is loaded by GUID. **Project Settings > Schematics > Sticky Notes > Paper Image Override**
  swaps it for another texture. If neither exists, any texture named `StickyNote` is used, for example
  `Features/Sticky Notes/Textures/StickyNote.png`. With no image at all, notes fall back to the old coloured squares.
- **Builds.** The build step puts a reference to the image on the `StickyNotesSceneData` object it adds to each
  scene, and that reference is what puts the image into the build.
- Text and buttons go inside `StickyNoteStyle.PaperInsets` (fractions of the paper: left, top, right, bottom). The
  bottom inset is larger because of the curled corner. Change it if you use an image with a different layout.
- `StickyNoteStyle.OpenAnimationSeconds` (0.15) sets how long the scale-up takes. Set it to 0 to turn it off.

## Performance snapshots

A note can carry a snapshot of how the game was running when the note was started:

- average and minimum FPS over the last N frames (*FPS Over Frames*, 120 by default);
- CPU and GPU frame time from `FrameTimingManager`. These are only filled in when the platform provides them;
  enable *Frame Timing Stats* in Player Settings for builds;
- draw calls, batches, SetPass calls, triangles and vertices, read through `ProfilerRecorder`. These are only
  available in the editor and in development builds;
- memory: Unity's allocated and reserved memory, the managed heap, GC heap size and GC count;
- the scene, the note's position and the camera position;
- device information: model, OS, CPU, RAM, GPU, VRAM, graphics API, resolution, quality level, app and Unity
  versions, and the build type.

**When a note gets one.** It's conditional:

- automatically for **Warning** and **Error** notes, while *Auto For Warnings/Errors* is on (the default);
- otherwise only when the author ticks **Attach performance snapshot** on a new note.

The snapshot is captured when the note is started, so typing the note doesn't skew it. In Edit Mode there's no game
loop, so the snapshot only holds memory and device information.

**How it's stored.** The snapshot goes in the note's `Perf` field as compact `key=value;…` text. Notes without a
snapshot stay as small as before. The note editor shows a one-line summary that unfolds into the full details. Kanban
cards show the summary too, with the details in the tooltip. Code can read snapshots with `StickyNotePerf.Summary`,
`StickyNotePerf.Details` and `StickyNotePerf.Parse`.

**Profiler captures (editor Play Mode).** With *Profiler Capture (Play Mode)* on, a note that gets a snapshot also
records about 60 frames of Profiler data to a `.raw` file, using `Profiler.logFile` and `enableBinaryLog`. The note
links the file in `PerfCapture`. Use **Open in Profiler** to load it, or **Reveal** to find it. Captures are saved
under `Library/StickyNotes/Captures/` by default, which is local and never committed. Set *Capture Location* to put
them in a `<Scene>.notes-captures/` folder next to the scene's notes file instead, which shares them through git.
These files are large.

## Kanban board

**Window > Schematics > Sticky Notes Board** opens the board. You can also use **Board** in the overlay or the list
window. It opens as a dockable tab next to the Scene view and has three columns:

| Column | Which notes |
| --- | --- |
| **To Do** | Open notes nobody has taken. A new note appears here immediately, whether it was made in the editor or by a playtester in a build. |
| **In Progress** | Open notes with an assignee, grouped by assignee: yours first, then others by name. People who aren't here are marked "away". |
| **Done** | Resolved notes, newest first. |

The column always comes from the note itself: resolved means Done, an assignee means In Progress, and anything else
is To Do. The board never keeps its own copy, so it can't disagree with the notes.

- **Take** assigns the note to you and moves it to In Progress. **Give back** returns it to To Do. **Done** resolves
  it. **Reopen** moves a Done note back to To Do. You can also take over someone else's task.
- **Drag** a card to another column to do the same things.
- **Double-click** a card to frame its note in the Scene view. If the note's scene isn't open, you're offered to open
  it. The **…** menu has Edit, Select Attached Object, Open Profiler Capture, Copy Text and Delete.
- Sorting is automatic. **To Do** is ordered Error, then Warning, then Default, oldest first within each. **In
  Progress** uses the same order inside each assignee group. **Done** is newest first.
- Use **All scenes**, **Only mine** and the search field to filter the board.

Assignments are stored on the note in `Assignee`, `AssigneeId` and `ClaimedAt`. They sync like everything else: the
newest version of a note wins. A name is kept as well as an id, because collaboration ids change between sessions.
In the editor, your name outside a session is your collaboration display name, so your tasks stay recognisable as
yours either way.

**Tasks go back to To Do when their assignee leaves.** This is handled by `StickyNotesTaskReturn`. One editor does it
for the whole room: the session host, or, when no host is known, the editor with the smallest id. Without a session,
your own editor does it.

- When someone with In Progress tasks **leaves the session**, their tasks go back once they've been gone for *Leave
  Grace* (120 s by default). If they reconnect or restart within that time, they keep their tasks. A peer with the
  same id or the same name counts as the same person.
- **On load**, an assignee who isn't present and wasn't seen leaving loses their tasks once the claim is older than
  *Absent Grace* (24 h by default). While you're around, your own editor refreshes the claim time on your tasks every
  half grace period, so the claim time works as "last seen".
- When **you quit** the editor, your own unfinished tasks go back first (*Return My Tasks On Quit*). They're then
  saved and committed with the notes.

**In game**, the **Tasks** list shows playtesters what's still open in the level and who's working on it.

## Storage

Each scene's notes live next to the scene as `<Scene>.notes.json`. For example, `Forest.unity` has
`Forest.notes.json`. This location was chosen because:

- the file is easy to find;
- it shows up in the same diff or pull request as the scene it's about;
- it moves with the scene when you move or rename the scene (an AssetPostprocessor handles this);
- builds don't need scene GUIDs, because they know scenes by path.

The file is a `StickyNoteFile` written with JsonUtility, pretty-printed and sorted by creation time. Types are stored
as text so diffs read well:

```json
{ "Version": 1, "Scene": "Assets/Levels/Forest.unity", "Notes": [ { "Id": "…", "Scene": "…", "Position": {…},
  "Normal": {…}, "Type": "Warning", "Text": "…", "Author": "…", "AuthorId": "…", "AuthorColor": "#E8B931",
  "Created": "2026-10-14T09:30:00.000Z", "Updated": "…", "Resolved": false, "ResolvedBy": "", "AttachedId": "…",
  "AttachedName": "Crate (3)", "Deleted": false, "Assignee": "Sam", "AssigneeId": "…",
  "ClaimedAt": "2026-10-14T10:02:00.000Z", "Perf": "v=1;fps=58.2;fpsMin=41.0;n=120;cpu=12.30;…", "PerfCapture": "" } ] }
```

**Builds.** `StickyNotesBuildProcessor` (an `IProcessSceneWithReport`) adds a `StickyNotesSceneData` object to each
scene as it goes into the build. That object carries the scene's notes. Only the build's copy of the scene changes:
the scene asset is never modified, and no Resources or StreamingAssets files are added. Which builds get the notes
is set in **Project Settings > Schematics > Sticky Notes > Include In Builds**:

- **Development Builds Only** (the default)
- **Always Include**
- **Never**

In an opted-in build, every scene gets the data object, even scenes with no notes. The object's presence is what
makes the in-game view available in release builds.

Builds also keep a local copy of each scene's notes in `persistentDataPath/StickyNotes/`. A tester who isn't
connected therefore keeps their notes, and they're shared with the room the next time the tester connects.

## Sync

Notes are mixed with one rule: **each note's newest version wins.**

- Every change stamps the note's `Updated` time (UTC, to the millisecond). The stamp is always later than the
  previous version's, even if this machine's clock is behind.
- Two versions with equal stamps are ordered by a fixed tie-break, so every peer picks the same one.
- A deleted note stays as a *tombstone* (`Deleted: true`), so an old copy can't bring it back. Tombstones live in
  the room and in memory only, and are never written to the file.

Because of this rule, the order that messages, the shared store, files and build copies arrive in doesn't matter.

| Channel / key | Content |
| --- | --- |
| channel `sticky-notes` | `StickyNoteMessage { Op: add/update/resolve/reopen/delete/claim/giveback/return, Note }`: one note's newest version, sent on every change. |
| shared `notes:<scene path>` | `StickyNoteFile`: the scene's whole set, tombstones included (dropped after 14 days), so late joiners see everything. |
| shared `notes-index` | `{ Scenes: [...] }`: the scenes that have notes in the room. |

**Writing to the shared store.** Whoever changes a note first mixes the room's current set into its own, then
writes the merged set. Anyone who sees a shared set that lacks something they have writes the merged set back. A
race between two writers therefore settles after one more write.

**Who sends.** The editor sends through `CollabSession` (contract C3). Builds send through `CollabRuntime` (contract
C4). In the editor's Play Mode, notes keep going through the editor's session, so one process never sends a note
twice.

**Saving to git.** This is the "finally git when the last person disconnects" part. While other editors are in the
session, nobody writes the notes file: the room holds the notes. This keeps everyone's working tree clean, so the
automatic git pulls never hit a locally modified notes file.

As soon as you're the only editor left, your editor writes the files. Playtesters don't count as editors. The files
are then committed through `CollabGit.CommitScene(notesPath, coAuthors, summary)`, which commits any path plus its
`.meta`. The commit happens when you:

- **quit** the editor;
- **lose the session as the last editor**;
- **are joined by another editor**, as a hand-over, so the newcomer starts from a clean tree;
- press **Commit** in the window.

Everyone who sent changes to those notes is listed as a co-author. Without a session, the files are written but not
committed, so commit them like any other file. Turn automatic commits off with *Commit When Last Editor Leaves*.

If you drop out of a session that still has other editors in it, your notes stay in memory, because the others will
save them. They're published again when you reconnect. If you quit while disconnected, your notes are written to
the file anyway, so nothing is lost. In-memory notes survive script reloads and entering Play Mode, through
SessionState.

Notes testers make in a build reach the editors over the room, and an editor saves them.

## Files

| Runtime (`Remedy.StickyNotes`) | |
| --- | --- |
| `StickyNote.cs` | `StickyNote`, `StickyNoteType`, `StickyNoteStatus`, `StickyNoteFile`, `StickyNoteMessage`, `StickyNoteStyle` (colours, glyphs, paper image) |
| `StickyNoteStore.cs` | The store: add/update/resolve/delete, claim/give back, merging, room sync |
| `StickyNotePerf.cs` | Performance snapshots and Play Mode Profiler captures |
| `IStickyNotesTransport.cs`, `CollabRuntimeTransport.cs` | The route to the room (runtime: `CollabRuntime`) |
| `StickyNotesRuntime.cs` | Start-up, scene tracking, builds' local copies |
| `StickyNotesView.cs` | The in-game view |
| `StickyNotesSceneData.cs` | Notes embedded in built scenes |

| Editor (`Remedy.StickyNotes.Editor`) | |
| --- | --- |
| `StickyNotesEditorService.cs` | Loading and writing files, when to commit, surviving reloads, moving files with scenes |
| `CollabSessionTransport.cs` | The route to the room in the editor (`CollabSession`), playtester detection |
| `StickyNotesSceneGUI.cs` | Scene view drawing, picking, adding, the overlay |
| `StickyNoteEditWindow.cs` | The note popup, drawn on the paper |
| `StickyNotesWindow.cs` | The list window |
| `StickyNotesKanbanWindow.cs` | The Kanban board (UI Toolkit) |
| `StickyNotesTaskReturn.cs` | Returns the tasks of people who left to To Do |
| `StickyNotesBuildProcessor.cs` | Embedding notes into builds |
| `StickyNotesSettings.cs` | Project settings and personal view options |

## Limitations

- Notes merge whole: the newest version of a note wins. If one person edits a note's text while someone else takes
  it at nearly the same moment, only one of the two changes is kept.

- A note stays where it was stuck. If the object it's attached to moves, the note doesn't follow.
- Ordering by `Updated` depends on each machine's clock. Two people editing the same note within a few seconds of
  each other on machines whose clocks differ by more than that may keep the "wrong" edit. Both peers still agree
  on which edit they keep.
- In-game text input uses IMGUI. Gameplay that reads the keyboard directly still sees what the tester types. Check
  `StickyNotesView.IsEditing` to ignore it.
- A peer counts as a playtester when the session reports their role: a `Role` member on `CollabPeer`, or a `role`
  presence key. Anyone else counts as an editor.
