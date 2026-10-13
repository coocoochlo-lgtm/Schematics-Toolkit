# Networking (Netcode for GameObjects)

Everything here compiles only when **Netcode for GameObjects 2.x** is installed (the `SCHEMATICS_NETCODE` define,
set by the asmdefs' version defines). Without it, Schematics run exactly as a single-player game: every hook below is
left empty and every Signal and Variable stays local.

## Making an object networked

Select it and turn on **Network Sync** under its GameObject header. That adds:

- a **NetworkObject** and **NetworkTransform**,
- a **Network Box3D Body** if it has a Box3D body (one peer simulates it, everyone else gets a kinematic proxy),
- a **Schematic Network Graph** if it's a Schematic Object: it runs a Networked graph's events on the authority,
  carries the object's synced Variables, networked Signals and Behavior state, and catches up players who join late.

**Simulated by Owner** (shown once Network Sync is on) makes whoever owns the object move it: its NetworkTransform
uses owner authority and its Box3D body is simulated by the owner. Behaviors marked `[NetworkOwnerSimulated]`
(Vehicle Seats, Arcade Car) turn it on by default. Otherwise the authority (the server, or the owner in distributed
authority) moves it.

## Network Sync for Variables

A Variable with **Network Sync** (the ⇅ marker on its row) is sent by the object's authority whenever it changes
(20 times a second at most), and every player who joins later gets the current value. Changes made anywhere else
are overwritten. Toggle it by clicking a Variable's row (IO Dock or Inspector) and using **Network Sync** in the
Inspector, or by right-clicking the row in the Inspector. Graph Variables have the same toggle.

Only values that can travel are synced: numbers, bools, vectors, colours, rotations, strings, and GameObjects or
components that belong to a spawned NetworkObject. Lists, arrays and asset references aren't.

## Network Sync for Signals

A Signal with **Network Sync** (⇅ on its row) fires for every player when it's sent. Click the Signal's row and set
**Network Sync** and **Sender** in the Inspector (or right-click a row in the Inspector). Behavior Signals, graph
Signals and plain Signal assets (Input Maps, seat Signals...) all have it; the setting is stored on the Signal asset.

**Sender** says who may send it:

| Sender | Who sends | What happens | Use it for |
|---|---|---|---|
| **Authority** | the object's authority | it fires on the authority and everyone else. A send from any other peer is dropped (that peer hears the authority's copy instead), so a Behavior that runs on every peer still fires it once everywhere. | results: died, picked up, opened, spawned |
| **Owner → Authority** | the object's owner | the owner hears it straight away and sends it to the authority, which checks the sender owns the object, fires it, and passes it on to everyone else. Anyone else's send (the authority's too, unless it owns the object) is dropped. | inputs: fire, reload, equip, throttle |
| **Anyone** | anybody | it fires on everyone. | chat, emotes, pings; requests the authority decides on |

Signals sent **to everyone** (UID 0, e.g. a Broadcast) travel through the server: *Authority* means the server (or
the session owner in distributed authority), the other two let any player send.

Rules that keep things from firing twice:

- Whatever arrives from another peer is delivered locally only: Signals its listeners send in turn are not passed on
  (every peer runs those listeners itself). The same goes for nodes set to run on **Everyone** in a Networked graph,
  and for a **Send a Signal** node set to Everyone (each peer sends its own copy locally).
- A Networked graph's events still run on the authority only, so its **On Signal Received** fires once.
- Parameters must be able to travel (see Variables above). A GameObject without a spawned NetworkObject arrives as
  null; the first time that happens you get a warning naming the Signal.
- A networked Signal sent more than 30 times a second logs a warning: use a synced Variable for values that change
  every frame.
- The target object needs a Schematic Network Graph (on it or on a networked parent). Signals sent to objects that
  aren't networked stay local.

## For Behavior authors: `[NetworkSync]`

Decide in code what matters in multiplayer, so level designers don't have to:

```csharp
[NetworkSync] public readonly VariableHandle<float> Health = 100f;           // synced by default
[OutputSignal, NetworkSync] public readonly SignalHandle OnDeath;              // Authority by default
[InputSignal, NetworkSync(SignalSender.OwnerToAuthority)] public readonly SignalHandle Fire;
```

The Behavior Metadata generator applies these as defaults. Once someone flips the toggle by hand
(`NetworkedSetByUser` on the Variable/Signal definition) their choice is kept, even if the attribute changes later.
Rules of thumb: sync authoritative gameplay state (health, ammo, equipped slot, open/locked, inventory changes,
spawn/death), not values worked out every frame (grounded, velocity, aim) or purely local ones (camera, UI).
Inputs are *Owner → Authority*; results are *Authority*; Signals only ever fired by one peer (whoever simulates a
vehicle) can be *Anyone*.

Behaviors with state Variables can't carry implement `INetworkStateBehavior` (`WriteNetworkState` /
`ReadNetworkState` with Union values) and call `SchematicNetwork.MarkStateChanged(instance)` when it changes; the
authority sends it and late joiners get it. `SchematicNetwork.HasAuthority`, `IsNetworked` and
`IsLocallyControlled` answer the usual questions without referencing Netcode (offline they say "yes, local").

## Vehicles

Vehicle Seats and Arcade Car are fully networked: seating is decided by the authority and synced (late joiners
included), enter/exit requests are networked Signals, networked occupants follow their seat on every peer instead of
being re-parented, the vehicle's ownership goes to the driver so the driver simulates it, the car's inputs come from
the driver only, and drift/boost/crash Signals reach everyone for effects. See `Features/Vehicles/README.md`.

## How it fits together

- `SignalNetwork` (Schematics Graph) / `SchematicNetwork` (Schematics Core): the hooks. Signals and Behaviors call
  them; this assembly fills them in, the same way `GraphNetwork` works for graphs.
- `SignalNetworkBridge`: decides whether a send may go out, packs it (Signal id + object path + arguments with
  `UnionNetworkSerializer`) and sends it through the object's Schematic Network Graph (RPCs) or, for Signals sent to
  everyone, as a named message.
- `SchematicNetworkGraph`: Variable sync, Signal RPCs (to everyone, to the authority, relayed), Behavior state, late
  joiners, replicated nodes.
- `NetworkBox3DBody`: who simulates a body (owner or authority), switching on ownership changes.
