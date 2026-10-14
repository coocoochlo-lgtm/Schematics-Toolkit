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
| Click a note | Open the small editor: type, text, Save, Resolve/Reopen, Delete (click twice), Cancel. |
| Hover a note | Read it. |

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

- **Scene view.** Each note shows as a small square in its type's colour, with a ring on the surface where it's
  stuck. A thin stripe in the author's collaboration colour runs along the bottom of the square. Hover a note to read
  it. Click it to edit it in a small popup, which saves when you click away. Cancel or Esc throws the changes away.
  Right-click a note for Edit, Resolve, Type, Select Attached Object and Delete.
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
  "AttachedName": "Crate (3)", "Deleted": false } ] }
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
| channel `sticky-notes` | `StickyNoteMessage { Op: add/update/resolve/reopen/delete, Note }`: one note's newest version, sent on every change. |
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
| `StickyNote.cs` | `StickyNote`, `StickyNoteType`, `StickyNoteFile`, `StickyNoteMessage`, `StickyNoteStyle` (colours, glyphs) |
| `StickyNoteStore.cs` | The store: add/update/resolve/delete, merging, room sync |
| `IStickyNotesTransport.cs`, `CollabRuntimeTransport.cs` | The route to the room (runtime: `CollabRuntime`) |
| `StickyNotesRuntime.cs` | Start-up, scene tracking, builds' local copies |
| `StickyNotesView.cs` | The in-game view |
| `StickyNotesSceneData.cs` | Notes embedded in built scenes |

| Editor (`Remedy.StickyNotes.Editor`) | |
| --- | --- |
| `StickyNotesEditorService.cs` | Loading and writing files, when to commit, surviving reloads, moving files with scenes |
| `CollabSessionTransport.cs` | The route to the room in the editor (`CollabSession`), playtester detection |
| `StickyNotesSceneGUI.cs` | Scene view drawing, picking, adding, the overlay |
| `StickyNoteEditWindow.cs` | The note popup |
| `StickyNotesWindow.cs` | The list window |
| `StickyNotesBuildProcessor.cs` | Embedding notes into builds |
| `StickyNotesSettings.cs` | Project settings and personal view options |

## Limitations

- A note stays where it was stuck. If the object it's attached to moves, the note doesn't follow.
- Ordering by `Updated` depends on each machine's clock. Two people editing the same note within a few seconds of
  each other on machines whose clocks differ by more than that may keep the "wrong" edit. Both peers still agree
  on which edit they keep.
- In-game text input uses IMGUI. Gameplay that reads the keyboard directly still sees what the tester types. Check
  `StickyNotesView.IsEditing` to ignore it.
- A peer counts as a playtester when the session reports their role: a `Role` member on `CollabPeer`, or a `role`
  presence key. Anyone else counts as an editor.
