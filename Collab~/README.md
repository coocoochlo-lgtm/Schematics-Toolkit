# SchematicsCollab: live collaboration helper

Several people edit the same scenes at the same time, and assets anyone adds reach everyone else. You only need a
network connection and the same project. The Unity side is in `Schematics/Editor/Collaboration/` (Core: the
`CollabSession` / `CollabGit` API and the bridge; Features: scene sync, presence, the team window and chat). This folder
holds the helper program those use. Unity ignores this folder because its name ends in `~`.

## How it fits together

```
 Unity editor ──localhost TCP──> SchematicsCollab helper ──LAN TCP/UDP──> other helpers (one of them hosts)
 (CollabBridge)                   discovery, session, git        └──or──> a relay (internet sessions)
```

- The **helper** is a small .NET 10 program. The editor starts it (and builds it with `dotnet build` the first time) and
  it keeps running across script reloads. Its heartbeats come from the helper, not from Unity, so an editor that freezes
  for minutes during an import stays in the session. It exits when the editor exits.
- After a script reload the editor reconnects within about a second. Events it missed (messages, store changes, git
  results) are replayed from the helper's buffer, which keeps the newest 20,000.
- Runtime files live in `<project>/Library/SchematicsCollab/`: `helper.json` (pid and ports), `collab.log`.
- .NET: the helper uses the same dotnet as Sprocket's setup (`Agent~/dotnet-path.txt`, `Agent~/.dotnet`). If neither
  exists, it uses `dotnet` from the PATH. It needs no NuGet packages. To build it yourself:
  `dotnet build Collab~/SchematicsCollab/SchematicsCollab.csproj -c Release -o Collab~/bin`.

## Zero setup on a local network

Peers find each other with UDP beacons, sent once a second to multicast `239.255.71.11:47811`, broadcast, and each
subnet's broadcast address. A beacon carries a **project key**, so only peers of the same project and branch see each
other:

- with git: a hash of the normalized remote URL, the branch, and the project's folder inside the repository;
- without git: a hash of `Application.productName`, `ProjectSettings/ProjectVersion.txt` and `Collab~/project-id`. The
  helper creates `project-id` once. Don't ignore it: copies of the project need the same one.

The first peer up becomes the host. The others connect to it over TCP. The host relays messages to everyone, in order
and reliably. It also keeps the shared store and everyone's presence, and gives each person a different colour. Every
peer keeps a copy of the store and the roster.

**Host migration (hop in, hop out).** Every peer measures round trips to the others (UDP pings, plus heartbeats to the
host). The host shares each peer's average in the roster. When the host leaves or goes silent (20 s), every peer makes
the same choice from the last roster: the best average ping wins, and ties go to the lowest id. The winner hosts with
everything it already has, and the others reconnect to it by themselves. Messages sent in between are held and
delivered afterwards. If two hosts of one project find each other (a network split heals), the smaller session merges
into the bigger one.

The firewall must allow UDP 47811 and the helper's TCP/UDP peer port (a random free port) on the local network. On
Windows, allow "dotnet" the first time Windows asks.

## Across the internet: relay mode

Beacons don't cross the internet. Run a relay on a machine everyone can reach, for example a small server or a PC with
the port forwarded:

```
dotnet Collab~/bin/SchematicsCollab.dll --relay --port 47812
```

Then each person sets **Preferences > Schematics > Collaboration > Relay Address** to `that-host:47812`. The relay hosts
one session per project key and never leaves, so there's no migration. It names one member as the lead, the peer that
editors see as host (it commits scene snapshots). When the lead leaves, the next one takes over. Traffic is not
encrypted, so use the relay on networks you trust or tunnel it (for example SSH or WireGuard).

## Playtest builds: the repository is the room

Every build is a playtest client too, with no setup. At build time the editor writes the project key (the same hash of
remote URL + branch + project folder the helpers use, `CollabProtocol.ProjectKey`), the repository, branch, commit and
your relay address into the build (`Resources/SchematicsCollabBuildInfo`, a temporary asset deleted after the build).
When the game starts, `CollabRuntime` (runtime assembly `Remedy.Schematics.Collaboration.Runtime`,
`Schematics/Runtime/Collaboration/`) listens for the host's beacons with that key (or goes to the relay), joins the room
as a **playtester** and can then send and receive channel messages, the shared store and presence like an editor.
An editor in Play Mode is a playtester as well; its game uses the editor's own connection (no second one).

- **Builds never host.** A build joins with port 0 and `client: "build"`; helpers leave it out when choosing a new host,
  and a relay never makes it the lead. A build may be on a console, paused in a debugger or killed at any moment, while
  the host keeps the store and commits scene snapshots, which only an editor's helper can do. When the host goes, the
  build waits for the next host's beacon and joins again (the editors elect one among themselves). With nobody to join,
  it keeps looking. Two builds alone (no editor) only meet through a relay.
- **Command line** (all optional): `-collab-off`, `-collab-name NAME`, `-collab-relay HOST:PORT`,
  `-collab-peer HOST:PORT` (ask an editor's helper directly when UDP broadcast is blocked; repeatable),
  `-collab-live-edits`.
- **Settings:** Project Settings > Schematics > Collaboration Features > Playtest builds (on by default; can be limited
  to development builds, or switched off: the build then has no collaboration client at all). WebGL builds never
  connect (no sockets in browsers).

## Errors reach the Unity Console

Every warning and error the helper logs (`Log.Warn` / `Log.Error` / `Log.Exception` in `Wire.cs`: sockets, malformed
frames, failing handlers, git) is also sent to the editor and shown in the Console with a `[Collab]` prefix and a link
to `Library/SchematicsCollab/collab.log`. Ones from before the editor attached (start-up) are delivered when it does.
The same message repeating is folded: shown once, then again with a count after 30 s. On the Unity side every
exception in the bridge, the features and `CollabRuntime` callbacks is caught and logged the same way
(`CollabLog`), never thrown back into the editor or the game loop.

## Git automation

The helper finds the repository from the Unity project folder (`git rev-parse --show-toplevel`), along with the
branch, the remote (the branch's upstream, else `origin`) and your `user.name` / `user.email`. Peers see each other's
git name and email in their presence (`git.name`, `git.email`).

- **Polling.** Every N seconds (Preferences, default 15; every 3 s while an announced asset is on its way) it runs
  `git fetch`. When the remote branch is ahead, the editor stops auto refresh (`AssetDatabase.DisallowAutoRefresh`).
  Then it pulls: `git merge --ff-only`, or `git rebase --autostash` when you have unpushed commits (aborted if it
  conflicts). Then auto refresh is allowed again, the editor refreshes, and `CollabGit.Pulled` reports the changed
  paths. Without an editor attached, nothing is pulled.
- **Live scenes are never pulled over.** A scene open in an editor that's in a session with others keeps its bytes on
  disk. HEAD moves past the new snapshot, but the scene file stays as it was. It shows as modified in `git status` until
  the next snapshot. After the session, an unchanged kept file is updated normally by the next pull.
- **New assets.** `CollabGit.AnnounceAsset(path, bounds)` first tells peers (guid + bounds, so they can show a
  placeholder of the same size). The helper then stages that asset, its `.meta` and its dependencies under `Assets/`,
  commits "Add <path> (collab)" with your Co-authored-by trailer, and pushes. On peers, `IsPending(guid)` is true until
  their next pull brings the asset in, which they start at once.
- **Scene snapshots.** `CollabGit.CommitScene(scene, coAuthors, summary)`, called by the host on the saved scene, pulls
  first. It then commits the scene and its `.meta` with `Co-authored-by: Name <email>` for every other participant, and
  pushes.

### Safety rules

- Only the given paths are staged (`git add -- paths`, `git commit --only -- paths`). Your other changes, staged or
  not, are never committed.
- It never force-pushes. A rejected push is pulled (rebasing only Collab's own commits) and retried.
- It only pushes when every unpushed commit on the branch is one Collab made (marked `Schematics-Collab: auto`).
  Otherwise it commits locally and asks you to push.
- It never pulls over local edits. If a file you changed (or an untracked file) was also changed upstream, the pull
  waits with a warning in the Console, and it goes ahead once you commit, stash or revert that file. It also doesn't
  pull during a merge, rebase or cherry-pick.
- It never asks for credentials (`GIT_TERMINAL_PROMPT=0`). Fetching and pushing use whatever git already uses (an SSH
  key, a credential manager). Errors show in the Console with a `[Collab]` prefix.

## Protocol

Every frame is a 4-byte big-endian length followed by UTF-8 JSON: one `CollabFrame` (see
`Schematics/Runtime/Collaboration/CollabProtocol.cs`, the one source of truth). The helper and Unity (the runtime
assembly, used by the editor bridge and by builds) compile that file, so a helper built from older sources reports an
older `Version` and the editor rebuilds it. The helper also compiles the build's client (`CollabRuntimeClient.cs`,
`CollabLiveEditCodec.cs`, plain C#) for its self test. UDP beacons and pings are one JSON frame per datagram.

Version 2 added roles: `CollabProfile` / `CollabPeerInfo` carry `role` (`editor` | `playtester`) and `client`
(`editor` | `build`), the state frame and `helper.json` carry the project `key`, and log frames carry a repeat `count`.

## Testing

```
dotnet build Collab~/SchematicsCollab/SchematicsCollab.csproj -c Release -o Collab~/bin
dotnet Collab~/bin/SchematicsCollab.dll --selftest          # add --keep to keep logs
```

The test runs real helper processes on localhost and plays the editor for each one. It checks host election,
fan-out, targeted and ordered messages, presence, the shared store, replay after an editor reconnect, host migration
when the host is killed, rejoining, a polite host exit, LAN discovery and project-key isolation, and relay mode. It
also runs the git automation against a throwaway bare repository: announce, push, pull and resolve; snapshot
co-authors; live-scene protection; postponing over local edits; never pushing your own commits. And it runs the
build's real client (`CollabRuntimeClient`): it joins the room by project key as a playtester, exchanges channel
messages and shared keys with editors, receives and decodes a live scene edit, sees an editor turn playtester in Play
Mode, follows a host migration without ever hosting, and a helper warning reaches the editor for the Console.
