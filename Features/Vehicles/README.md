# Vehicles

Drivable things for the toolkit, in two halves that work apart or together:

- **Vehicle Seats** (`Vehicles/Seats`): who sits where, who drives, getting in and out, and a Signal for each of
  those per seat. It knows nothing about movement, so a car, boat, plane, mech or horse all share it.
- **Arcade Car** (`Vehicles/Arcade Car`): a car on a real Box3D body with Need for Speed Underground 2 / Mario Kart
  handling (easy drifts, mini-turbos, a car that wants to stay on its wheels), while crashes, ramps and rollovers
  are still physics.

## Quick start

`ArcadeCarMenu.CreateArcadeCarPrefab(folder)` (editor code, meant for a future Arcade Car template) makes a ready-to-drive prefab in the given Project folder: a
Box3D body with a chassis and a cabin shape, Arcade Car with four wheels (placeholder cylinders), its Properties
asset, and Vehicle Seats with a driver and a passenger seat, each with its Signals.

By hand:

1. Make a prefab with a **Schematic Object**.
2. Add **Arcade Car**. It adds a **Box3D Body**, a **Box3D Box Shape** (size it to the chassis; add more shapes on
   children for a cabin, bumpers...) and a **Schematic Impact Relay**, and makes four wheel anchors
   (`Wheel_FrontLeft`...). The IO Dock creates its **Properties** asset.
3. Place the wheel anchors at the top of each suspension (inside the body, above the hub) and give each wheel its
   **Visual** (a child of the anchor or the car, never the anchor itself) and **Radius**.
4. Add **Vehicle Seats**. It makes a driver seat (`Seat_Driver`). In the IO Dock, under the Behavior:
   - **+ Seat** adds a seat: a child anchor placed next to the others (mirrored across the car, then a row
     behind), an exit on the outside, and its four Signals. The first seat is the driver's.
   - Each seat row: rename it (renames its anchor and Signals too), toggle **Driver**, **◎** selects its anchor
     (opening the prefab), **×** deletes it.
   - The seat's **On Entered / On Exited / Enter Request / Exit Request** show as ordinary dock rows: drag them
     into the graph to react or to send, like any Behavior Signal. They live in
     `<Prefab> Seat Signals.asset` next to the prefab. **Tidy Signals** deletes ones left behind by deleted seats.
   - **Details…** opens the prefab. With the vehicle selected, the Scene view shows each seat (coloured, labelled,
     the driver's with a wheel), its exit, and the wheels; click a dot to get a move handle (**Tools → Remedy →
     Vehicles → Scene Handles** turns them off). Exit points, occupant options and poses are in the Inspector.
5. Wire the player (below) and drive.

## Getting in and out

Send the vehicle **Enter** (Occupant) to seat someone in the first free seat (the driver's first, unless
**Driver Seat First** is off), **Enter Seat** (Occupant, Seat) or a seat's own **Enter Request** for a specific one,
**Exit** (Occupant) / a seat's **Exit Request** to get out, **Switch Seat**, **Exit All**. **Max Enter Distance**
limits the Enter Signals to people standing close.

Sitting down, per seat:

- **Attach Occupant**: parented to the anchor (world scale kept), or, off, moved onto it every frame.
- **Occupant Physics**: *Disable Body* (default: its Box3D body leaves the simulation so it can't shove the car from
  inside), *Kinematic* (rides along and still blocks, e.g. an exposed gunner; its old type comes back on exit), or
  *Leave Alone*.
- **Disable Occupant Behaviors**: its movement controller and friends switch off while seated (its graph keeps
  running, so it can still send input). Only the ones that were on come back on.
- **Hide Occupant**: its renderers, for closed cockpits.
- **Pose Id / Pose Parameter**: an int sent to the occupant's Animator (and 0 on leaving), or read the seat in a
  graph and drive the animation yourself.

Getting out puts all of that back and places them at the **Exit Point** (or **Exit Offset** from the anchor). If the
way out is blocked (**Exit Blocking Layers**), the other side is tried, then the roof. They leave with the vehicle's
velocity unless **Inherit Vehicle Velocity** is off. A vehicle that's despawned or disabled lets everyone out first.

Signals out: **On Occupant Entered / Exited** (Occupant, Seat), **On Driver Changed** (Driver, or null),
**On Full**, **On Empty**, plus each seat's **On Entered / On Exited**. Variables: **Driver**, **Has Driver**,
**Occupant Count**, **Is Occupied**.

From C#: `VehicleSeats.TryEnter(vehicle, occupant, seat)`, `VehicleSeats.ExitVehicle(occupant)`,
`VehicleSeats.VehicleOf(occupant)`, `SeatOf(occupant)`, `IsSeated`, `IsDriving`, `DriverOf(vehicle)`, and the static
`OccupantEntered` / `OccupantExited` events. (The C# exit is `ExitVehicle` because `Exit` is the Signal.)

## Driving it (player input)

Vehicles don't know about players: a graph routes input while someone sits in a driver seat. Two ways:

- **Driver Inputs** on Arcade Car (like the Weapon Holder's Weapon Inputs): add an entry per input, pick the
  Signal (e.g. an Input Map's *Input Accelerate*) and what it does (Throttle for forward target speed, Brake override,
  Steer, signed Target Speed, Drive (a Vector2 stick), Handbrake, Boost, Unflip). The car listens itself, but only
  while the player drives it (**Player Driver Only**: the driver has the player tag), so parked and AI cars ignore the
  keys.
- Or from a graph: on the vehicle's graph, forward input to **Target Speed / Brake / Steer / Drive / Handbrake / Boost**
  while **Has Driver** is on. AI does the same (or calls `SetInputs` from C#).

**Target Speed** accepts one signed float from -1 to 1: -1 targets maximum reverse speed, 1 targets maximum forward
speed, and 0 targets a stop. The car scales actual forward/reverse speed by the corresponding direction's maximum,
then applies throttle or brake in proportion to the difference from the target. If the requested direction opposes
motion, it brakes until current speed is within 20% of the current direction's maximum before accelerating the other
way. The value holds until changed. **Brake** remains available as an independent brake override and never requests
reverse.

Impacts reduce the target-speed multiplier for the nearest wheel based on normalized impact kinetic energy; landings
apply the same effect to the contacting wheel. The wheel multipliers combine to scale the requested target speed.
They recover while grounded and reset while airborne. **Ground Material Resistance** is a temporary placeholder until
surface materials are available; it increases landing loss and slows recovery. During handbrake drifts, the movement
heading lags behind the car's forward heading, with realignment slowing as actual speed approaches the target.

A typical player graph: on the interact button, if not seated send **Enter** (self) to the vehicle in front of you,
else **Exit**. On the vehicle's **On Driver Changed**, switch the input map (`RemedyInput.Instance.SetInputMap`) and the camera
target. Without Vehicle Seats the car is always active; with them it only drives while someone's in a driver seat
(or **Always Active**, for AI), and with **Park When Empty** its brakes hold it still.

## Tuning the drift

Everything is in the car's **Properties** (accelerations are m/s², so the numbers mean the same for a kart and a
truck). Right-click the asset's header for presets: **Street Racer (NFS Underground)**, **Kart (Mario Kart)** and
**Physical (flips easily)**.

How a drift works: it starts by **Drift Entry** (handbrake + steering, sharp steering at speed, either, or off) above
**Drift Min Speed**. While drifting the rear tyres keep **Drift Rear Grip** of their grip, the steering swings the
tail (**Drift Steer Assist**), **Drift Auto Turn** curves it into the drift whatever you steer (karts),
**Drift Max Angle** + **Drift Counter Steer** stop spin-outs, and **Drift Momentum Keep** swings your speed round
to where the nose points so drifts don't scrub speed. It ends when the button's let go (**Hold Button To Drift**,
kart style) or when the slide straightens under **Drift Exit Angle** (street style), or below half the minimum
speed. **Mini-Turbo**: drifting charges (**Charge Rate**, faster when steering into it); ending the drift past a
**Turbo Level** gives its boost. Signals: **On Drift Started / Level / Ended**, **On Boost** (level, 0 for a Boost
input).

| | NFS Underground 2 | Mario Kart |
|---|---|---|
| Drift Entry / Hold Button | Handbrake Or Sharp Steer / off | Handbrake / on |
| Drift Min Speed / Steer Threshold | 14 / 0.8 | 8 / 0.3 |
| Drift Hop | 0 | 2.5 |
| Drift Rear Grip / Front Grip | 0.3 / 0.95 | 0.45 / 1 |
| Drift Steer Assist / Auto Turn | 2.5 / 0 | 2 / 2.5 |
| Drift Max Angle / Counter Steer | 50 / 6 | 35 / 12 |
| Drift Momentum Keep | 1.2 | 3 |
| Mini-Turbo | off (use Boost as nitro) | on, 3 levels |
| Front / Rear Grip, Max Tyre Accel | 16 / 13, 28 | 20 / 20, 35 |
| Max Speed, Mass | 55 m/s, 1300 kg | 32 m/s, 300 kg |

Rules of thumb: more front grip than rear means understeer (safe), the reverse oversteer (playful). Lower
**Handbrake Grip** makes flicks easier. If drifts feel floaty raise **Drift Rear Grip**; if they spin raise
**Drift Counter Steer** or lower **Drift Max Angle**.

## Staying upright vs. flipping

The body is a real Box3D body, so it can roll, tumble and land on its roof. How hard it fights that is up to you:

- **Tyre Force Height** (the big one): where cornering grip pushes. 0 = at the road (realistic, the car leans
  and rolls), 1 = at the centre of mass height (never rolls from cornering). 0.5-0.7 still flips on kerbs and in
  crashes.
- **Center Of Mass Offset**: lower (negative y) is harder to roll. **Inertia Scale**: higher is heavier to spin.
- **Stabilize Strength / Damping**: levels toward a support-load-weighted ground normal and damps roll/pitch. Both
  fade to zero at **Max Speed**, and weak or sparse tire support fades stabilization out. **Stabilize Transition
  Time** smooths the correction's fade and direction changes as the car moves between ramps and airborne states. The
  controller compensates for the torque from grounded tires' suspension, traction and sticky forces; 0 strength and
  damping = pure physics.
- **Ground Check Distance** sets the full wheel ground-probe length. **Tire Placement Threshold** (0..1) places the
  tire's outer edge at that fraction of the probe when ground is farther away (0.5 places it halfway along); a nearer
  hit moves the tire toward the surface. The full cast continues beyond the tire, including while it is airborne.
  **Suspension Rest Length** limits spring travel, not tire placement.
- Ungrounded tires within the check distance borrow an averaged ground plane from the tires that did hit. Their
  suspension, grip, sticky and drive forces are blended in by the grounded-tire fraction, so the assist fades toward
  zero as actual support disappears. **Stabilize Transition Time** also smooths each tire's contact normal, distance
  and force handoff to soften brief missed hits. Only actual hits count as grounded or trigger landings.
- **Sticky Threshold / Sticky Force**: above the speed threshold, tires with actual or blended ground contact get a
  force along the car's local -up direction, proportional to forward speed. This can press the car onto walls or
  ceilings when its wheels are oriented toward them.
- **Air Level Strength / Damping**: levelling and damping in the air. **Air Control** applies steering as roll about
  the car's forward axis and signed Target Speed as pitch about its right axis, using forces at ungrounded tire
  anchors; it only runs when really airborne, not when lying on its roof.
- **Torque Guard Max Angular Acceleration / Blend Time** caps and smooths the combined rotational effect of the car's
  tire forces and handling assists. The requested rotation still contributes, but a sudden spike is limited and
  blended instead of jolting the chassis. 0 max acceleration disables the cap; external collision impulses are
  unaffected. Enable **Log Torque Guard Diagnostics** to compare measured angular acceleration with the previous
  step's guarded tire/controller contribution; the log separates tire, stabilization, rotation-assist, air-control
  and unexplained physics acceleration.
- **Self Right Strength / Delay / Max Speed / Lift**: a car lying flipped (past **Flipped Tilt**) and nearly
  stopped turns back over after the delay. 0 = flips stay; send **Unflip** (a "reset car" button) to right it
  anyway. Signals: **On Flipped**, **On Righted**; variable **Is Flipped**.

For a car that flips like a real one, use the **Physical** preset: stabilizing, levelling and self-righting at 0,
Tyre Force Height 0.15, real-ish tyre limits.

Also: **On Crash** (impact speed, above **Min Crash Speed**), **On Landed** (air time), and the variables
**Speed** (km/h), **Forward Speed** (m/s, negative reversing), **Is Grounded**, **Grounded Wheels**,
**Is Drifting**, **Drift Angle**, **Drift Charge**, **Drift Level**, **Is Boosting**, **Air Time**.

## Other kinds of vehicle

Vehicle Seats is the shared part. A boat, hovercraft, plane, mech or horse is its own Schematic Behavior that:

1. Moves its Box3D body in `OnFixedUpdate` (skipping `body.IsNetworkProxy()`; only the authority simulates).
2. Only drives while `VehicleSeats.DriverOf(Self.GameObject) != null` (or always, without Vehicle Seats), like
   `ArcadeCar.IsActive`.
3. Takes input from its own input Signals, and optionally a `VehicleInputBinding[]` heard through
   `VehicleInput.AcceptsPlayerInput(vehicle, playerDriverOnly)` and `VehicleInput.ToFloat / ToBool / ToVector2`, so
   any Input Map Signal works.

Everything about seats (signals, the IO Dock, gizmos, handles, getting in and out) then comes for free.

## Networking

With Netcode for GameObjects installed, turn on the vehicle's **Network Sync** (the toggle under its GameObject
header). Because Vehicle Seats and Arcade Car are marked `[NetworkOwnerSimulated]`, that also sets its
NetworkTransform to **owner authority** ("Simulated by Owner"), and adds a Network Box3D Body and a Schematic
Network Graph. Then, with nothing else to wire:

- **Seats are synced.** The vehicle's authority (the server, or the owner in distributed authority) decides who sits
  where. **Enter / Enter Seat / Exit / Switch Seat / Exit All** (and each seat's **Enter Request / Exit Request**,
  networked when "+ Seat" creates them) are networked with sender *Anyone*: any player may send them, only the
  authority acts on them. The seating it decides is sent to every player as Behavior state (players who join late get
  it too), and each player seats the occupants on their own copy, so **On Entered / On Exited / On Driver Changed /
  On Full / On Empty** fire locally for everyone (they aren't networked themselves, or they'd fire twice). Driver, Has
  Driver, Occupant Count and Is Occupied follow on every peer. The C# API (`TryEnter`, `ExitVehicle`, ...) only acts
  on the authority online; send the Signals from clients.
- **Occupants follow, they aren't parented.** Netcode only lets the server re-parent NetworkObjects (and only under
  other NetworkObjects), and a parented player would fight its own NetworkTransform. So a networked occupant is moved
  onto its seat's anchor every frame on every peer instead (the owner's NetworkTransform then sends that position).
  Non-networked occupants are still parented when **Attach Occupant** is on. Body disabling, hidden renderers and
  disabled Behaviors happen on every peer the same way.
- **The driver owns the car.** When someone takes a driver seat, the server hands the vehicle's NetworkObject to the
  driver's client (`Features/Vehicles/Networking`, `ChangeOwnership`); when nobody drives it goes back to the server.
  With owner authority the driver simulates the Box3D body (no input lag) and everyone else, the server included,
  sees a kinematic proxy. In distributed authority the owner hands it over (the NetworkObject's Ownership needs
  *Transferable*).
- **Inputs come from the driver.** Driver Inputs only listen on the driver's own machine (`VehicleInput` checks the
  driver is controlled locally), and **Target Speed / Brake / Steer / Drive / Handbrake / Boost / Unflip** are networked
  *Owner → Authority*: only the vehicle's owner can send them, the authority checks and passes them on. The owner also
  mirrors its current inputs through them up to 15 times a second when they change, so proxies turn their front wheels
  and the server has them if it takes the car back.
- **Effects play everywhere.** **On Drift Started / Ended / Level, On Boost, On Landed, On Flipped / Righted, On
  Crash** are networked (sender *Anyone*: only the peer that simulates ever fires them). Proxies also set **Is
  Drifting, Drift Level, Is Boosting, Is Flipped** from them, and work out **Speed / Forward Speed** and wheel spin
  from their synced motion.

Offline (or without Netcode) everything works exactly as before. A late joiner may see an occupant that isn't a
networked object (an NPC without a NetworkObject) as an empty seat: give such occupants Network Sync.

## Notes

- Wheels are raycasts against **Ground Layers**, ignoring the car's own body. A wheel visual that is its anchor
  (or contains it) isn't moved, since that would move the ray.
- Mass, centre of mass and inertia come from the Properties on top of what the shapes give, each spawn.
- Seat Signals are sent to the vehicle (its UID), so the vehicle's graph hears them. Copies of a vehicle prefab
  share its Seat Signals asset until you give a copy's seats new ones (**Create Signals** after clearing them).
