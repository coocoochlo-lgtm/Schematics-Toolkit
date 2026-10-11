# Manifold Neuron Network

A small recurrent neural network that remembers through geometry. Each memory **head** moves through its own random
cloud of values (a **value space**), and where it sits is the memory. A **master** steers the heads, a **translator**
reads them, and the translator's output feeds back into the master on the next step. It learns by **weight
perturbation**: it runs slightly nudged copies of itself and moves toward the nudges that did better. There's no
backprop and no ML libraries, so it can keep learning while the game runs, on the GPU or the CPU.

## Two assemblies

| Assembly | What | Depends on |
| --- | --- | --- |
| `Remedy.ManifoldNet` (`Runtime/`) | The network: `ManifoldNet`, `MnnConfig`, `MnnBrain`, the CPU reference, the compute shader | Unity only |
| `Remedy.ManifoldNet.Schematics` (`Schematics/`) | The **Brain** behavior, its graph nodes, the brain clouds | The network + Schematics |

You can use the network on its own from C#:

```csharp
var net = new ManifoldNet(config);           // or new ManifoldNet(settings, instanceCount, brain)
void FixedUpdate() {
    net.Step(sensors);                        // or Step(inputs, targets) to teach
    if (net.TryGetOutputs(actions)) Apply(actions);
    net.ReportError(0, DistanceToGoal());     // population / sequential training
}
void OnDestroy() => net.Dispose();
```

It runs on the GPU when compute shaders are available. There, outputs arrive one step late through `TryGetOutputs`,
or you can keep them on the GPU through `OutputBuffer`. It falls back to the CPU automatically (WebGL, some mobile),
or uses it when you pass `preferCpu`. The CPU reference is the source of truth: the GPU kernels are tested against it.

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
   shows each head's trail, head usage, the recent error, and the outputs.
7. **Keep what it learned.** When Play Mode ends, the weights save into the definition (**Learned**). Ship the
   definition and the bot ships trained. Changing the slots or network settings makes saved weights stop fitting,
   because weights are tied to the input order. The dock warns you and offers **Reset**.

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

## Tests

The tests are in **Window > General > Test Runner > EditMode > Remedy.ManifoldNet.Tests**:

- **ParityTests** checks the GPU against the CPU: outputs over 1,000 steps on three configs, weights after 100
  training updates, replay practice, memory capture and restore, and 1,024 instances in one dispatch.
- **LearningTests** checks that sine prediction learns on the CPU and on the GPU, that a saved brain loads exactly,
  and that mixing rules hold.

The CPU side has also been verified outside Unity: determinism, torus wrap, sampling safety, zero-allocation steps,
and learning in supervised, population and sequential modes. Untrained sine-prediction error is about 0.80; trained,
it is about 0.02-0.05. The GPU side needs those parity tests run in the editor.

## Notes

- **Training uses Adam on the perturbation estimates.** The blueprint's plain momentum step blew up in testing,
  because one noisy window could fling the weights. `learningRate` is now Adam's step size (0.001) and `momentum`
  is its β1.
- **Best-weights rollback is on, but cautious.** The defaults are margin 1.0 (the baseline at twice the best) and
  1,000 steps; tighter settings fought learning in tests.
- **The spatial grid isn't built.** It's a speed-up for spaces above about 4,096 points.
