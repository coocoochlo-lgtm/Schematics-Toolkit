# Manifold Neuron Network

A small recurrent neural network that remembers through geometry. Each memory **head** is a point moving through its
own fixed, random cloud of values (a **value space**). Where it sits, and how it's moving, is the memory. A **master**
steers the heads, a **translator** reads them, and the translator's output feeds back into the master on the next
step. It learns by **weight perturbation**: it runs slightly nudged copies of itself and moves toward the nudges that
did better. There's no backprop and no ML libraries, so it can keep learning while the game runs, on the GPU or the
CPU.

## How one step works, in plain terms

1. **The master** (a tiny MLP) reads the inputs, its own last answer, and how training is going. It writes one short
   "steering mask" per head.
2. **Each head** has its own tiny MLP that turns its mask into a **target** somewhere in its space, plus a **gate**
   ("pay attention now"). The head then moves, pulled by three things:
   - the target (`pull`);
   - the centre of the points it's sitting among, a Hopfield-style **attractor** (`settle`). Clusters of points become
     basins a head likes to stay in, so its memory survives a few noisy frames;
   - its own **pace**. Every head has a base timescale, from fast and twitchy to slow and steady
     (`timescaleMin`-`timescaleMax`), and the gate can speed it up for a moment (`liquidGates`).

   The space wraps around like a donut (a torus), so heads never get stuck at an edge.
3. **Each head reads its space**: a blend of the values of the points near it (Soft) or the nearest point's values
   (Nearest). It also keeps a **trail**, a fading average of what it has read. The trail fades faster when the gate
   says things are changing.
4. **The translator** reads every head's reading and trail, plus the raw inputs (so it never depends on one head
   alone), and writes the outputs. Those outputs are the answer, and they also go back to the master next step.

Movement uses real time: set `DeltaTime` to the seconds since the last step, and the brain behaves the same at 20 Hz,
60 Hz or uneven ticks.

## Two assemblies

| Assembly | What | Depends on |
| --- | --- | --- |
| `Remedy.ManifoldNet` (`Runtime/`) | The network: `ManifoldNet`, `MnnConfig`, `MnnBrain`, `MnnRunner`, the CPU reference (`Runtime/Core`), the compute shader | Unity only |
| `Remedy.ManifoldNet.Schematics` (`Schematics/`) | The **Brain** behavior, its graph nodes, the brain clouds | The network + Schematics |

You can use the network on its own from C#:

```csharp
var net = new ManifoldNet(config);           // or new ManifoldNet(config, instanceCount, brain, preferCpu)
void FixedUpdate() {
    net.DeltaTime = Time.fixedDeltaTime;      // optional: real time per step (default: one nominal tick)
    net.Step(sensors);                        // or Step(inputs, targets) to teach
    if (net.TryGetOutputs(actions)) Apply(actions);
    net.ReportError(0, DistanceToGoal());     // population / sequential training
}
void OnDestroy() => net.Dispose();
```

Or drop an **MnnRunner** component on a GameObject: it owns a network made from a config, steps it in `FixedUpdate`
(with a **Prefer CPU** toggle), and raises `BeforeStep` / `AfterStep` so a script can fill `Inputs` and read
`Outputs`.

It runs on the GPU when compute shaders are available. There, outputs arrive one step late through `TryGetOutputs`,
or you can keep them on the GPU through `OutputBuffer`. It falls back to the CPU automatically (WebGL, some mobile),
or uses it when you pass `preferCpu`. The CPU reference is the source of truth: the GPU kernels are tested against it.
For a single small network the CPU is often faster; the GPU earns its keep with many instances.

## How it learns

Every training window (`evalWindow` steps, default 1), the network snapshots each instance's memory and runs the real
weights next to `perturbPairs` nudged copies (by default each nudge both ways: θ + σε and θ − σε). The nudges come
from a hash of the seed, the window and the weight's index, so they're never stored or uploaded. At the end of the
window, every weight moves toward the nudges that scored better.

Where the score comes from:

| Mode | Use when | How copies get scored |
| --- | --- | --- |
| Supervised | You know the right answer (prediction, imitation) | `Step(inputs, targets)`: each copy's mean squared error |
| Population | Many agents share one brain (a crowd) | Each instance runs a different nudge; call `ReportError(instance, e)` |
| Sequential | One agent, error only from the game | Copies take turns, one window each; slowest but always works |

Every error also goes into a short-term **trace** and a long-term **baseline**. When the trace climbs above the
baseline, hidden layers get more chaotic (an adaptive sine blended into tanh), which shakes heads out of ruts. The
master also sees that ratio as an input. A head the translator stops using is **revived** with fresh weights and a
random position. A copy of the **best weights** so far is kept: if the error drifts off a cliff, it rolls back once.

## The shared plan grid (WFC, optional)

With `wfcEnabled`, every point gets one of T **types** (from the seed). The heads share a **plan grid**: one row per
head and one column per plan step (now, then `planColumns - 1` steps into the future). Each cell is "what this head
will be doing then", either undecided (a spread over types) or committed to one type.

Three shared **rulebooks** (T × T each, learned like the weights) link the cells:

- **Coexist**: what neighbouring heads may do at the same time.
- **Follow**: what a head may do next.
- **Handoff**: how what head A does shapes what head B does next.

Each step:

1. The now column is measured from what each head is reading.
2. The future starts even, optionally biased by the master.
3. The cells pass messages to each other through the rulebooks.
4. The most certain cells commit. When the network is struggling, it picks more adventurously.
5. A committed head reads only points of its type.
6. Every `planTickSteps` ticks, the plan slides one column toward now.

A sudden spike in error wipes the future (the now column stays). `wfcAxis` picks the shape:

- **Heads**: coordination only.
- **Time**: each head sequences on its own.
- **Grid**: the full plan.

## The episodic memory bank (optional)

With `memoryEnabled`, a small bank of slots stores key moments instantly. A slot holds:

- a **key**: the master's hidden activations at that moment, a compact summary of what it was seeing;
- a **value**: outputs to recall;
- a signed **strength** (positive: do this; negative: avoid this) and its age.

Every step, each copy compares its key with every slot (softmax of β × cosine similarity). The translator gets the
blended recall and a confidence. Only the real network writes, between steps, in instance order. A near-identical
moment merges into its slot; otherwise the new moment takes an empty slot or replaces the weakest, least recently
recalled one. Strength halves every `memoryHalfLife` seconds. Call `Remember(instance, value, strength)` and
`Reinforce(instance, amount)` from C#, or let the Brain do it. Slots save with the brain (`MnnBrain.MemorySlots`).
Clear the bank (`ClearMemory`) when the sensor layout changes.

## Identities, imitation and familiar exploration (optional)

Four small learners that work next to the weight nudges. They use each step's error: the supervised error, or what
you report with `ReportError`. They're all off by default, and with them off the network behaves exactly as before.

- **Identity masks** (`identityMasks`). Each head of each instance gets an *identity*: one number between 0 and 1 per
  value it reads (K of them; K = `identityWidth`, or all N). Every step the identity is expressed as on/off bits, with
  a little random wobble while training. The bits are compared with the signs of what the head just read (XOR), passed
  through a small matrix the head learns like its other weights, and added to the reading (× `identityAlpha`). So two
  bots with the same weights still read their heads a little differently.

  The identity learns on its own, not by gradient. When a step goes better than usual, the identity moves toward the
  bits it just expressed; when it goes worse, it moves away. Each instance starts from a different seeded identity, so
  a crowd stays diverse. Identities save with the brain (`MnnBrain.IdentityMasks`).

  Why the head's reading: it's what the head hands to the translator, so it's the head's "feature vector" (like the
  last hidden layer in an ordinary network). It exists whatever the MLP shape, it's signed, so its sign bits mean
  something, and it only adds H × K × K weights.
- **Imitation** (`imitationEnabled`). A table of `imitationSlots` situations and the outputs that did better than
  usual there.
  - A *situation* is a 64-bit fingerprint (a SimHash) of the inputs, plus the last outputs with
    `imitationKeyFeedback`. Nearby situations share most bits. Similarity is exp(−differing bits × `imitationFalloff`).
  - A step only writes when it beat the usual error. Then the remembered output for that situation is pulled toward
    what was just done (by `imitationRate` × how much better it was). A new situation takes an empty slot or replaces
    the weakest one.
  - Every entry fades every step (half-life `imitationHalfLife`), so stale habits wash out.
  - Each step, each copy recalls a target and a confidence c. Its training score becomes
    (1 − c) × error + c × `imitationWeight` × (distance to the target)², so copies that do what worked before score
    better.

  This matters most when all you can give is a score (`ReportError`, Crowd Learning). A score says *how* wrong, not
  *which way*. The remembered good outputs supply the direction.
- **Familiar exploration** (`familiarityExploration`). In a situation the imitation memory (or the episodic memory)
  knows well, the brain explores less: the nudge size, the adaptive sine and the identity wobble are all scaled by
  lerp(1, `familiarExploreScale`, familiarity). New situations keep the full amount.
- **Trajectory credit** (`trajectoryCredit`, needs imitation). When a step beats the usual error, the outputs of the
  `creditSteps` steps before it are written too, fading by `creditDecay` per step back. A late reward then marks the
  actions that led to it.

The memories are written by the real network between steps, in instance order, as with the episodic memory. In
Population and Sequential training each instance runs a nudged copy, so it writes from that. The imitation memory is
shared by every instance and isn't saved with the brain: it fades within minutes anyway. The shared plan doesn't read
the identities. There was no clean way to turn K × K bits into per-type plan preferences.

## Making a bot with the Brain behavior

1. **Add a Brain** to the Schematic Object (IO Dock, **+ Add Behavior > AI/Brain**). In its dock section, press
   **Create Brain Definition**. That creates an asset next to the prefab that holds the slots and what it learns.
2. **Add slots.** Inputs are what it senses: a name, a Size (how many values, like 7 for a ray fan), and the
   values' Min-Max range. Outputs are what it decides:
   - an **Axis**: -1 to 1;
   - a **Button**: pressed above 0.6 and released below 0.4, so it doesn't flicker;
   - a **Choice**: one of several options, where the strongest wins.

   Each output gets a color for the brain clouds.
3. **Feed it.** Drag **On Think** from the Brain's dock section into the graph. From there:
   - build senses with ordinary nodes (**Ray Fan**, **Direction To**, **Nearest With Tag**, **Make Values**, or
     anything else);
   - wire them into **Set Brain Input** nodes, one per slot.
4. **Act on it.** Drag **On Decided** into the graph, then use **Get Brain Output** for each slot and drive the
   character with it.
5. **Teach and reward it.**
   - **Teach Brain Output**: "the right answer was this". Wire the player's own input in so the bot learns by
     watching.
   - **Reward Brain**: positive for praise, negative for punishment. A reward credits the bot's recent decisions,
     most of all the latest ones, fading with the definition's **Credit Half-Life**. The brain then replays those
     moments as lessons: praised decisions again, punished ones avoided.
   - Rewards are compared to the usual reward per tick and scaled by their typical size, so "+1" and "+500" train
     the same, and a steady penalty says nothing on its own.
6. **Watch it think.** In Play Mode, a cloud floats above each agent:
   - one mini value space per head;
   - the points, colored by their outputs;
   - the heads, as bright orbs with fading tails.

   **Preferences > Manifold Network Gizmos** sets the size, height, opacity, tail length, selected-only, max
   distance, and whether the clouds also show in the Game view. **Tools > Remedy > Manifold Network > Debug Window**
   shows each head's trail, target and attractor, head usage, the recent error, the outputs, and the plan grid and
   memory slots when those are on.
7. **Keep what it learned.** When Play Mode ends, the weights save into the definition (**Learned**). Ship the
   definition and the bot ships trained. Changing the slots or network settings makes saved weights stop fitting,
   because weights are tied to the input order. The dock warns you and offers **Reset**.

### Shared plan, memory and the learners

The dock's **Network** foldout has these optional modules (all off by default):

- **Shared Plan**: the heads agree on a short plan together (see "The shared plan grid" below).
- **Episodic Memory**: a teaching moment the brain got wrong (its own answer was off by more than 0.05, mean
  squared) is remembered at once with the taught answer. A big reward (more than `writeThreshold` typical rewards
  away from the usual) remembers what the bot did, signed, so a punished moment is remembered as "avoid". Every
  reward also strengthens or weakens whatever the bot had just recalled. The bot can repeat something it was shown
  once while training slowly makes it permanent. Memories are saved with the learned weights.
- **Identity Masks**, **Imitation** (with **Credit Earlier Steps**) and **Familiar Exploration**: see "Identities,
  imitation and familiar exploration". They learn from teaching errors and, with Crowd Learning, from rewards. Each
  bot's identities are saved with the learned weights.

The brain's **Thinks / Second** is also its nominal tick: heads move in real time against it.

### Shared or per-instance brains

- **Shared**: every instance of the object thinks with one network and learns together. The instances all run in
  one batched GPU dispatch. **Crowd Learning** also has each bot try a slightly different version of the brain,
  scored by its own rewards.
- **Per Instance**: each instance gets its own network, all starting from the same weights.

Either way, each instance has its own memory, the positions of its heads.

Running networks live in `BrainHub.Networks[objectID][networkID]`.

### Breeding

- **Copy Brain** gives one brain's weights to another.
- **Mix Brains** crossbreeds two parents into a third:
  - **Blend** averages the weights;
  - **Swap Parts** takes whole parts (the master, each head, the translator) from one parent or the other, and
    usually breeds better;
  - **Per Weight** flips a coin for each weight.
- **Mutate Brain** adds a little noise.

Mixing only works between brains with the same layout: the same slots, network settings and seed.

## Config

Defaults are starting guesses. Fields marked † change the weight layout, so saved brains stop fitting.

| Field | Default | What it does |
| --- | --- | --- |
| `inputCount` † (I) | 8 | Base inputs, not counting feedback |
| `outputCount` † (N) | 4 | Outputs; also values per point (max 64) |
| `headCount` † (H) | 8 | Memory heads (max 256; 32 with the plan grid) |
| `maskWidth` † (M) | 8 | Master outputs per head |
| `spaceDimensions` † (D) | 3 | Dimensions of each value space (max 8) |
| `pointsPerSpace` † (P) | 256 | Random points per space |
| `hiddenWidth` / `hiddenLayers` † | 16 / 1 | Size of every tiny MLP (width max 64) |
| `dispersion` (σ) | 0.15 | Sampling falloff, in space-radius units; points beyond 3σ are skipped |
| `trajectorySpeed` | 0.05 | The most a head moves per nominal tick |
| `pull` | 0.5 | How hard heads chase the master's target |
| `settle` | 0.1 | How hard heads are pulled into nearby point clusters |
| `timescaleMin` / `timescaleMax` | 0.05 / 1 s | Range of the heads' base timescales (fast to slow) |
| `trailTimescale` | 0.5 s | Base timescale of the trails |
| `liquidGates` † | on | Each head's MLP also outputs a gate that speeds it up; off: fixed timescales |
| `tickRate` | 20 Hz | The nominal tick that `trajectorySpeed` is measured in |
| `samplingMode` | Soft | Soft (Gaussian blend) or Nearest (closest point's values) |
| `trainingMode` | Supervised | Supervised, Population or Sequential |
| `perturbMode` | Antithetic | Antithetic (±ε pairs) or OneSided |
| `perturbPairs` (K) | 8 | Nudges per window |
| `perturbSigma` (σp) | 0.02 | Nudge size |
| `updateRule` | Adam | Adam, or Momentum (the blueprint's plain step; see Notes) |
| `learningRate` / `momentum` | 0.001 / 0.9 | Step size; momentum (Adam's β1) |
| `evalWindow` | 1 | Steps per evaluation window (longer: credit for things that pay off later) |
| `errorFalloff` / `baselineFalloff` | 0.9 / 0.999 | Speeds of the error trace and baseline |
| `sineOmega` / `sineGain` / `sineAlphaMax` | 3 / 1 / 0.5 | Adaptive sine activation |
| `alphaScalesSigma` | off | Bigger nudges when struggling |
| `balanceLambda` | 0.01 | Penalty for leaning on few heads |
| `reviveThreshold` / `reviveSteps` | 0.05 / 500 | A head under this share of use for this long is revived |
| `bestWeightsRollback` | on | Keep the best weights; roll back once if training drifts off a cliff |
| `rollbackMargin` / `rollbackSteps` | 1 / 1000 | "Off a cliff" = baseline above (1 + margin) × best for this many steps |
| `clockInputs` † / `clockFrequencies` | off / 0.01, 0.05, 0.2 | Sine/cosine of the step count into the master |
| `changeInputs` † | off | The master also sees input − previous input |
| `velocityReadout` † | off | The translator also sees each head's velocity |
| `surpriseReset` / `resetThreshold` / `resetAmount` | off / 2.5 / 0.5 | An error spike pulls heads partway back to rest |
| `trainableValueSpaces` † / `valueSpaceRate` | off / 0.1 | Point values learn too, at a lower rate |
| `spatialGrid` | off | Bucket points so heads only check nearby cells (D ≤ 3, σ ≤ 0.22; worth it above ~4,096 points) |
| `wfcEnabled` † | off | The shared plan grid |
| `wfcAxis` † | Grid | Heads, Time or Grid |
| `typeCount` † (T) / `planColumns` † (F) | 8 / 8 | Point types (max 16) / now + future columns (max 16); H × F × T ≤ 4,096 |
| `planTickSteps` | 5 | Brain ticks per plan column |
| `wfcPasses` / `wfcCollapseBudget` | 3 / 2 | Message passes and collapses per step |
| `headNeighbors` | Ring | Ring, or All (needs H ≤ 8) |
| `wfcResetThreshold` | 2 | Trace / baseline that wipes the plan's future |
| `wfcRuleLrScale` | 0.25 | Rulebook learning-rate multiplier |
| `masterPlanBias` † | on | The master also biases each future column ((F − 1) × T outputs) |
| `memoryEnabled` † | off | The episodic memory bank (needs hiddenLayers ≥ 1) |
| `memorySlots` (S) | 64 | Slots (max 256) |
| `memoryBeta` (β) | 8 | Recall sharpness (trainable) |
| `writeThreshold` / `mergeThreshold` | 0.5 / 0.95 | Reward size that writes / key similarity that merges |
| `memoryHalfLife` | 600 s | Time for an unused slot's strength to halve |
| `memoryScope` | Shared | One bank for every instance, or one each |
| `identityMasks` † | off | A learned identity per head and instance that reshapes the head's reading |
| `identityWidth` † (K) | 0 | Reading values the identity covers (0 = all N) |
| `identityAlpha` / `identityNoise` / `identityRate` | 0.5 / 0.2 / 0.01 | How loud the identity is / its wobble while training / how fast it learns |
| `imitationEnabled` | off | The self-imitation memory |
| `imitationSlots` | 128 | Remembered situations (max 1,024) |
| `imitationWeight` / `imitationRate` | 1 / 0.5 | Its share of the training score at full confidence / how far a good attempt pulls its entry |
| `imitationFalloff` / `imitationMergeBits` | 0.2 / 4 | Similarity exp(−bits × falloff) / keys this close are the same situation |
| `imitationHalfLife` | 60 s | Time for every entry's quality and familiarity to halve |
| `imitationKeyFeedback` | on | The situation also includes the last outputs (its context) |
| `familiarityExploration` / `familiarExploreScale` | off / 0.3 | Explore less where it's familiar / exploration left at full familiarity |
| `trajectoryCredit` / `creditSteps` / `creditDecay` | off / 8 / 0.7 | A good step also credits the steps before it / how many / fade per step back |
| `seed` † | 1234 | Everything random comes from this |

## Debug window

**Tools > Remedy > Manifold Network > Debug Window** watches any running network:

- **Heads**: each head's trajectory in its space (first two dimensions), with the master's target (○) and the
  attractor (+). Under each: its timescale, gate and current liquid rate.
- **Head usage** bars (red: in danger of being revived), the **recent error**, and the **outputs**.
- **Plan grid** (plan grid on): heads down, columns from now to the horizon. Committed cells show their type's color;
  undecided cells fade by how sure they are. Resets are marked. Three small heatmaps show the rulebooks.
- **Memory** (memory on): a graph of recall confidence, and each slot's strength, age, time since recalled, and how
  much it's being recalled now.
- **Identities and imitation** (when on): the imitation confidence, the mean familiarity and the exploration scale
  it gives, and each head's identity bits (filled: set; bright: sure).

## Tests

The tests are in **Window > General > Test Runner > EditMode > Remedy.ManifoldNet.Tests**:

- **MathTests** (CPU) cover:
  - torus wrap;
  - the hash (fixed values, unit-variance noise);
  - same seed, same outputs;
  - the liquid rate (two half steps equal one full step);
  - wrapped attractor offsets across the seam;
  - soft-sampling weights summing to 1;
  - nearest-mode IDs;
  - no NaNs far from every point;
  - 20 Hz vs 60 Hz ending within 2%;
  - an antithetic (and one-sided) update pointing downhill;
  - the spatial grid matching brute force.
- **ModuleTests** (CPU) cover:
  - the plan grid on every axis (finite, commits, changes behaviour, rolls left);
  - memory: only the real network writes, merging, replacing the weakest;
  - a brain round trip with rulebooks, point types and memory slots.
- **LearningTests**:
  - sine prediction under 20% of its untrained error within 20,000 steps (CPU, both perturb modes, and with both
    modules on), with no head under 5% usage;
  - population mode learning a constant;
  - the GPU learning the same;
  - saving, loading and mixing.
- **ImitationTests** (CPU) cover:
  - the SimHash keeping nearby situations close;
  - imitation writing only better-than-usual attempts, pulling toward them, and fading every entry each step;
  - trajectory credit fading per step back;
  - identities moving toward the bits of good steps and away from bad ones, seeded per instance;
  - the layout staying the same with the features off, familiarity rising, and no allocations per step.
- **ParityTests** (GPU, skipped without compute shaders) check the GPU against the CPU over five configs (the fourth
  with the plan grid, memory and spatial grid on, at uneven ticks; the fifth with identities, imitation, credit and
  familiar exploration): outputs over 1,000 steps, weights after 100 updates, reward-only training, practice, memory
  and imitation banks, identities, plan grids, state capture and restore, and 1,024 instances.

## Notes

- **Training defaults to Adam on the perturbation estimates.** The blueprint's plain step with momentum
  (`updateRule = Momentum`, learning rate 0.01) is there too. At that rate it blows up on the default network: the
  estimate over ~2,000 weights is large and noisy. At 0.001 it learned the sine task in one run (8.7% of the
  untrained error) and diverged in another. Adam with 0.001 learns reliably.
- **Best-weights rollback rolls back once per cliff.** If the restored weights can't hold the best level either,
  that level was a lucky stretch. The current level becomes the new reference instead of rolling back again and
  again.
- **Population mode writes memories from the instance's own (nudged) copy.** Every instance runs a nudge there, so
  there's no unperturbed copy to write from. The nudge is tiny, so the keys are still good.
- **Point types in Nearest mode.** The plan grid's per-type readings use the soft (Gaussian) weights in both
  sampling modes. A committed head always reads its type's soft average.
- **GPU kernels are one thread per (instance, copy), or per (instance, copy, head)** for the head kernels. They don't
  use groupshared tiling yet. That would be a speed-up, not a change in results.
- **"Better than usual" is scale-free.** Identities and imitation use (baseline − error) / (2 × spread), clamped to
  ±1. The spread is the running mean distance of errors from the baseline, so the rates behave the same whatever the
  units of the errors or rewards.
- **The learners haven't paid off on the ablation tasks yet.** On CPU, 30,000 steps, 3 seeds, with the plan grid and
  every older extra on: adding identities, imitation, credit and familiar exploration was within noise on cue, delay
  and flip. It lowered the hidden-target error from 0.187 to 0.172, but the heads-cut floor (0.162) was lower still. On
  a reward-only version (only a score per step, Sequential training), imitation made things worse (0.31 vs 0.24 with
  the plain network). Treat them as experimental, and measure on your own task.
